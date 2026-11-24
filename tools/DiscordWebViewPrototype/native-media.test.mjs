import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('./native-media.js',import.meta.url),'utf8');
function setup(origin='https://discord.com'){
    let now=1000;const messages=[],tracks=[],released=[],writes=[],timers=[],listeners={};
    class Frame{constructor(...args){this.args=args;}close(){this.closed=true;}}
    class Track{
        constructor({kind}){
            this.kind=kind;this.readyState='live';this.events=new Map();this.constraints={};this.settings={};this.constraintCalls=[];tracks.push(this);
            this.writable={getWriter:()=>({write:async f=>{
                writes.push(f);if(this.kind==='video'&&!this.deferNativeSettings){
                    const source=f.args[1];const scale=Math.min(1,(this.constraints.width?.max??source.codedWidth)/source.codedWidth,
                        (this.constraints.height?.max??source.codedHeight)/source.codedHeight);
                    this.settings.width=Math.floor(source.codedWidth*scale);this.settings.height=Math.floor(source.codedHeight*scale);
                }
            },abort:async()=>{this.readyState='ended';this.events.get('ended')?.();}})};
        }
        getSettings(){return {...this.settings};}
        getConstraints(){return structuredClone(this.constraints);}
        async applyConstraints(value){
            this.constraintCalls.push(structuredClone(value));if(this.applyHook)await this.applyHook(value);
            this.constraints=structuredClone(value);if(value.frameRate?.max)this.settings.frameRate=value.frameRate.max;
        }
        stop(){this.readyState='ended';this.stopped=true;}
        addEventListener(name,fn){this.events.set(name,fn);}
        removeEventListener(name,fn){if(this.events.get(name)===fn)this.events.delete(name);}
    }
    class Stream{constructor(tracks){this.tracks=tracks;}getTracks(){return this.tracks;}getVideoTracks(){return this.tracks.filter(track=>track.kind==='video');}}
    const context={location:{origin},navigator:{mediaDevices:{getDisplayMedia:async()=>{throw Error('must never call original picker');}}},
        Date:{now:()=>now},setTimeout,clearTimeout,DOMException,MediaStreamTrackGenerator:Track,MediaStream:Stream,VideoFrame:Frame,AudioData:Frame,Uint8Array,
        chrome:{webview:{postMessage:m=>messages.push(m),releaseBuffer:b=>released.push(b),addEventListener:(type,f)=>listeners[type]=f}},setInterval:f=>timers.push(f)};
    context.window=context;context.top=context;vm.createContext(context);vm.runInContext(source,context);
    context.initializeWidgetRailNativeCapture(['https://discord.com','https://capture-probe.widgetrail.test']);
    return {context,messages,tracks,released,writes,timers,setNow:n=>now=n,frame:(epoch=1,kind=1,metadata={},buffer)=>listeners.sharedbufferreceived({getBuffer:()=>buffer??new ArrayBuffer(metadata.bytes??16),additionalData:{epoch,kind,width:2,height:2,time:1,bytes:16,...metadata}})};
}
test('native H264 uses independent reader generations and preserves the raw track after reader failure',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia,open=async(id,opt)=>{
        api.start(id,false,'720p30','detail',opt);
        assert.equal(api.openH264(()=>{}),null);
        const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(id);await pending;
    };
    await open(1,false);assert.equal(api.openH264(()=>{}),null);
    await open(2,true);
    const seen=[],a=api.openH264(u=>seen.push(u));
    a.update(2500000,true);assert.equal(s.messages.at(-1).captureEncoder,a.generation);
    const buf=new ArrayBuffer(43),h=new DataView(buf,16);h.setUint32(0,a.generation,true);h.setUint32(4,2,true);
    h.setUint32(8,3,true);h.setBigInt64(16,100n,true);new Uint8Array(buf).set([1,2,3],40);
    await s.frame(2,1,{},buf);new Uint8Array(buf)[40]=9;
    assert.deepEqual([...new Uint8Array(seen[0].data)],[1,2,3]);assert.equal(seen[0].time,100);
    a.close();const b=api.openH264(()=>{throw Error('consumer retired');});
    await s.frame(2,1,{},buf);assert.equal(seen.length,1);
    h.setUint32(0,b.generation,true);await s.frame(2,1,{},buf);
    assert.equal(api.status().active,true);assert.equal(s.messages.some(m=>m.captureError),false);
    const before=s.messages.length;a.update(9,true);b.update(9,true);assert.equal(s.messages.length,before);
});
test('inactive request does not open browser chooser',async()=>{const s=setup();await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'NotAllowedError'});});
test('explicit start is one-shot and audio is optional',async()=>{const s=setup();assert.equal(s.context.WidgetRailNativeMedia.start(1,false),true);const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame();const stream=await pending;assert.equal(stream.getTracks().length,1);await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('audio start produces distinct audio and video tracks',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame();const stream=await pending;assert.deepEqual(Array.from(stream.getTracks(),t=>t.kind),['video','audio']);});
test('stop ends every owned track and rejects further capture',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.context.WidgetRailNativeMedia.stop(1);assert.ok(s.tracks.every(t=>t.readyState==='ended'));await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('new source retires old tracks',()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.context.WidgetRailNativeMedia.start(2,false);assert.deepEqual(s.tracks.map(t=>t.readyState),['ended','ended','live']);});
test('expired chooser authorization is rejected',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,false);s.setNow(17000);await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('lost host heartbeat ends capture',()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.setNow(12000);s.timers[0]();assert.ok(s.tracks.every(t=>t.readyState==='ended'));});
test('stale frame is released and acknowledged without publication',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(2,false);await s.frame(1);assert.equal(s.writes.length,0);assert.equal(s.released.length,1);assert.equal(s.messages[0].captureEpoch,1);});
test('accepted frame is closed and buffer released after writing',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,false);await s.frame();assert.equal(s.writes.length,1);assert.equal(s.writes[0].closed,true);assert.equal(s.released.length,1);assert.equal(s.context.WidgetRailNativeMedia.status().frames,1);});
test('late rejected write cannot stop a replacement session',async()=>{const s=setup();let reject;s.context.WidgetRailNativeMedia.start(1,false);s.tracks[0].writable.getWriter=()=>({write:()=>new Promise((_,r)=>reject=r),abort:async()=>{}});
    // Recreate using a writer whose completion is controlled by this test.
    const Original=s.context.MediaStreamTrackGenerator;s.context.MediaStreamTrackGenerator=class extends Original{constructor(args){super(args);this.writable.getWriter=()=>({write:()=>new Promise((_,r)=>reject=r),abort:async()=>{}});}};
    s.context.WidgetRailNativeMedia.start(2,false);const pending=s.frame(2);s.context.MediaStreamTrackGenerator=Original;s.context.WidgetRailNativeMedia.start(3,false);reject(Error('old writer cancelled'));await pending;assert.equal(s.tracks.at(-1).readyState,'live');assert.equal(s.context.WidgetRailNativeMedia.status().active,true);
});
test('adapter does not install on unrelated origin',()=>{const s=setup('https://example.com');assert.equal(s.context.WidgetRailNativeMedia,undefined);});

test('all twelve transport presets report selected rate while retaining actual dimensions',async()=>{
    for(const height of [480,720,1080,1440])for(const fps of [15,30,60]){const preset=`${height}p${fps}`;
        const s=setup();assert.equal(s.context.WidgetRailNativeMedia.start(1,false,preset),true);
        const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:640,height:360,bytes:640*360*4});
        const stream=await pending;const settings=stream.getTracks()[0].getSettings();
        assert.equal(settings.frameRate,Number(preset.slice(-2)));assert.equal(settings.width,640);assert.equal(settings.height,360);
        assert.deepEqual(stream.getVideoTracks()[0].getConstraints(),{width:{max:2*Math.floor(height*640/360/2)},height:{max:height},frameRate:{max:fps}});
        assert.equal(s.context.WidgetRailNativeMedia.status().quality,preset);assert.equal(s.context.WidgetRailNativeMedia.status().targetFrameRate,Number(preset.slice(-2)));
    }
});

test('1440p60 establishes real track bounds without upscaling a smaller captured window',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const track=s.tracks[0],getConstraints=track.getConstraints,applyConstraints=track.applyConstraints;
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:640,height:360,bytes:640*360*4});
    const result=await pending;
    assert.equal(result.getVideoTracks()[0],track);assert.equal(track.getConstraints,getConstraints);assert.equal(track.applyConstraints,applyConstraints);
    assert.deepEqual(track.constraintCalls,[{width:{max:2560},height:{max:1440},frameRate:{max:60}}]);
    assert.equal(track.getSettings().width,640);assert.equal(track.getSettings().height,360);
    assert.equal(api.status().constraintWidth,2560);assert.equal(api.status().constraintHeight,1440);assert.equal(api.status().constraintFrameRate,60);
});

test('provider can subsequently lower real constraints and getSettings does not force selected FPS',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:1920,height:1080,bytes:1920*1080*4});
    const track=(await pending).getVideoTracks()[0];
    await track.applyConstraints({width:{max:640},height:{max:360},frameRate:{max:15}});
    await s.frame(1,1,{width:1920,height:1080,bytes:1920*1080*4});
    assert.equal(track.getSettings().width,640);assert.equal(track.getSettings().height,360);assert.equal(track.getSettings().frameRate,15);
    assert.equal(api.status().constraintWidth,640);assert.equal(api.status().constraintHeight,360);assert.equal(api.status().constraintFrameRate,15);
    assert.equal(api.status().targetFrameRate,60);assert.equal(track.constraintCalls.length,2);
    track.settings.frameRate=12;assert.equal(track.getSettings().frameRate,12);
    delete track.settings.frameRate;assert.equal(track.getSettings().frameRate,15);
});

test('capture handoff waits for successful native constraint application',async()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'1080p60');let release;
    s.tracks[0].applyHook=()=>new Promise(resolve=>release=resolve);
    let handedOff=false;const pending=s.context.navigator.mediaDevices.getDisplayMedia();pending.then(()=>handedOff=true);
    await s.frame();await new Promise(resolve=>setImmediate(resolve));assert.equal(handedOff,false);assert.equal(s.tracks[0].constraintCalls.length,1);
    release();await pending;assert.equal(handedOff,true);
});

test('constraint setup rejects unsupported and unconfirmed APIs instead of silently returning default bounds',async()=>{
    for(const failure of ['missing-apply','missing-read','no-op','wrong-height']){
        const s=setup();s.context.WidgetRailNativeMedia.start(1,true,'1440p60');const track=s.tracks[0];
        if(failure==='missing-apply')track.applyConstraints=undefined;
        if(failure==='missing-read')track.getConstraints=undefined;
        if(failure==='no-op')track.applyConstraints=async()=>{};
        if(failure==='wrong-height')track.applyConstraints=async value=>{track.constraints={...structuredClone(value),height:{max:720}};};
        const pending=s.context.navigator.mediaDevices.getDisplayMedia();
        const rejected=assert.rejects(pending,{name:failure.startsWith('missing')?'NotSupportedError':'OverconstrainedError'});
        await s.frame();await rejected;
        assert.equal(s.context.WidgetRailNativeMedia.status().active,false);assert.ok(s.tracks.every(item=>item.readyState==='ended'));
        assert.equal(s.messages.filter(message=>message.captureError).length,1);
    }
});

test('rejected native constraints retire only the owned source and report the failure',async()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'1440p60');
    s.tracks[0].applyHook=async()=>{throw new DOMException('Cannot satisfy selected quality','OverconstrainedError');};
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'OverconstrainedError'});
    await s.frame();await rejected;
    assert.equal(s.messages.find(message=>message.captureError)?.captureError,'OverconstrainedError');
    assert.equal(s.context.WidgetRailNativeMedia.status().active,false);
});

test('a stalled constraint application has a bounded timeout and cannot return an unconfirmed stream',async()=>{
    const s=setup(),scheduled=[];
    s.context.setTimeout=(callback,delay)=>{const timer={callback,delay};scheduled.push(timer);return timer;};
    s.context.clearTimeout=timer=>{if(timer)timer.cleared=true;};
    s.context.WidgetRailNativeMedia.start(1,false,'1440p60');s.tracks[0].applyHook=()=>new Promise(()=>{});
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'TimeoutError'});
    await s.frame();await new Promise(resolve=>setImmediate(resolve));const timeout=scheduled.find(timer=>timer.delay===5000&&!timer.cleared);assert.ok(timeout);timeout.callback();await rejected;
    assert.equal(s.context.WidgetRailNativeMedia.status().active,false);assert.equal(timeout.cleared,true);
});

test('replacement cancels an old pending handoff and late constraint outcomes cannot stop the new source',async()=>{
    for(const rejectLate of [false,true]){
        const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');let settle;
        s.tracks[0].applyHook=()=>new Promise((resolve,reject)=>settle=rejectLate?()=>reject(Error('old constraint failure')):resolve);
        const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'AbortError'});
        await s.frame();await new Promise(resolve=>setImmediate(resolve));assert.equal(typeof settle,'function');api.start(2,false,'720p30');await rejected;settle();
        await new Promise(resolve=>setImmediate(resolve));
        assert.equal(api.status().active,true);assert.equal(s.tracks[1].readyState,'live');assert.equal(s.messages.filter(m=>m.captureError).length,0);
        const pending= s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(2);await pending;
        assert.equal(api.status().constraintHeight,720);assert.equal(api.status().constraintFrameRate,30);
    }
});

test('stop immediately cancels a handoff even when native constraints never settle',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false);s.tracks[0].applyHook=()=>new Promise(()=>{});
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'AbortError'});
    await s.frame();await new Promise(resolve=>setImmediate(resolve));api.stop(1);await rejected;assert.equal(api.status().active,false);
});

test('expired authority during constraint application prevents the handoff',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false);let finish;
    s.tracks[0].applyHook=()=>new Promise(resolve=>finish=resolve);
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'AbortError'});
    await s.frame();await new Promise(resolve=>setImmediate(resolve));s.setNow(12000);finish();await rejected;assert.equal(api.status().active,false);
});

test('an old track retains its own observed dimensions after replacement',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:640,height:360,bytes:640*360*4});const old=(await pending).getVideoTracks()[0];
    old.settings={};api.start(2,false,'720p30');await s.frame(2,1,{width:1280,height:720,bytes:1280*720*4});
    assert.equal(old.getSettings().width,640);assert.equal(old.getSettings().height,360);assert.equal(old.getSettings().frameRate,60);
});

function controlledTimeouts(s){
    const scheduled=[];
    s.context.setTimeout=(callback,delay)=>{const timer={callback,delay};scheduled.push(timer);return timer;};
    s.context.clearTimeout=timer=>{if(timer)timer.cleared=true;};
    return scheduled;
}

test('writer acceptance and fallback dimensions cannot stand in for native format readiness',async()=>{
    const s=setup(),scheduled=controlledTimeouts(s),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const track=s.tracks[0];track.deferNativeSettings=true;
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:640,height:360,bytes:640*360*4});
    await new Promise(resolve=>setImmediate(resolve));
    assert.equal(track.getSettings().width,640);assert.equal(api.status().browserWidth,undefined);
    assert.equal(track.constraintCalls.length,0);
    const observation=scheduled.find(timer=>timer.delay===16&&!timer.cleared);assert.ok(observation);
    track.settings={width:640,height:360,frameRate:30};observation.callback();const stream=await pending;
    assert.equal(stream.getVideoTracks()[0],track);assert.equal(track.constraintCalls.length,1);
    assert.equal(track.getConstraints().width.max,2560);assert.equal(track.getConstraints().height.max,1440);
    assert.ok(scheduled.filter(timer=>timer.delay===5000).every(timer=>timer.cleared));
});

test('native format readiness is bounded and never applies constraints after timeout',async()=>{
    const s=setup(),scheduled=controlledTimeouts(s),api=s.context.WidgetRailNativeMedia;api.start(1,false);
    const track=s.tracks[0];track.deferNativeSettings=true;
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'TimeoutError'});
    await s.frame();await new Promise(resolve=>setImmediate(resolve));
    const timeout=scheduled.find(timer=>timer.delay===5000&&!timer.cleared);assert.ok(timeout);timeout.callback();await rejected;
    assert.equal(track.constraintCalls.length,0);assert.equal(api.status().active,false);
    const observation=scheduled.find(timer=>timer.delay===16);assert.equal(observation.cleared,true);
    track.settings={width:2,height:2};observation.callback();assert.equal(track.constraintCalls.length,0);
});

test('replacement during native format discovery cannot apply old constraints to either source',async()=>{
    const s=setup(),scheduled=controlledTimeouts(s),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const old=s.tracks[0];old.deferNativeSettings=true;
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'AbortError'});
    await s.frame();await new Promise(resolve=>setImmediate(resolve));api.start(2,false,'720p30');await rejected;
    old.settings={width:2,height:2};const oldPoll=scheduled.find(timer=>timer.delay===16);assert.equal(oldPoll.cleared,true);oldPoll.callback();
    assert.equal(old.constraintCalls.length,0);assert.equal(s.tracks[1].constraintCalls.length,0);assert.equal(api.status().active,true);
    assert.equal(s.messages.filter(message=>message.captureError).length,0);
});

test('native format exceeding the authorized envelope cannot be handed off',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'720p30');s.tracks[0].deferNativeSettings=true;
    s.tracks[0].settings={width:3840,height:2160};
    const rejected=assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'OverconstrainedError'});
    await s.frame();await rejected;assert.equal(s.tracks[0].constraintCalls.length,0);assert.equal(api.status().active,false);
});

test('initial native constraints follow source aspect rather than the maximum allocation rectangle',async()=>{
    for(const [width,height,maxWidth,maxHeight] of [[640,360,2560,1440],[1280,360,5120,1440],[360,640,810,1440],[2048,128,5120,320],[738,478,2222,1440]]){
        const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
        const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width,height,bytes:width*height*4});
        const track=(await pending).getVideoTracks()[0];
        assert.deepEqual(track.getConstraints(),{width:{max:maxWidth},height:{max:maxHeight},frameRate:{max:60}});
        assert.equal(track.getSettings().width,width);assert.equal(track.getSettings().height,height);
    }
});

test('later source reshaping does not overwrite a consumer constraint decision',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false,'1440p60');
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(1,1,{width:640,height:360,bytes:640*360*4});
    const track=(await pending).getVideoTracks()[0];await track.applyConstraints({width:{max:640},height:{max:360},frameRate:{max:30}});
    const constrained=track.getConstraints();
    await s.frame(1,1,{width:1280,height:320,bytes:1280*320*4});
    assert.equal(track.constraintCalls.length,2);assert.deepEqual(track.getConstraints(),constrained);
    assert.equal(track.getSettings().width,640);assert.equal(track.getSettings().height,160);
});

test('unsupported quality does not stop an existing capture or create tracks',()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,true,'480p15');
    for(const preset of ['2160p60','1440p120','__proto__',null,{id:'720p30'}])assert.equal(s.context.WidgetRailNativeMedia.start(2,false,preset),false);
    assert.equal(s.tracks.length,2);assert.ok(s.tracks.every(t=>t.readyState==='live'));assert.equal(s.context.WidgetRailNativeMedia.status().quality,'480p15');
});

test('1440p60 admits its complete bounded frame and releases the exact credit after writing',async()=>{
    const s=setup(),api=s.context.WidgetRailNativeMedia;assert.equal(api.start(1,false,'1440p60'),true);
    const bytes=5120*1440*4;await s.frame(1,1,{width:5120,height:1440,bytes,sequence:17});
    assert.equal(s.writes.length,1);assert.equal(s.released[0].byteLength,29491200);
    assert.equal(s.messages.at(-1).captureAck,1);assert.equal(s.messages.at(-1).captureSequence,17);
    assert.equal(api.status().width,5120);assert.equal(api.status().height,1440);assert.equal(api.status().targetFrameRate,60);
});
test('1440p transport rejects oversized odd and mismatched descriptors before writer admission',async()=>{
    for(const metadata of [{width:5122,height:1440,bytes:5122*1440*4},{width:5120,height:1442,bytes:5120*1442*4},
        {width:5119,height:1440,bytes:5119*1440*4},{width:5120,height:1440,bytes:16}]){
        const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'1440p60');await s.frame(1,1,metadata);
        assert.equal(s.writes.length,0);assert.equal(s.released.length,1);assert.equal(s.context.WidgetRailNativeMedia.status().active,false);
    }
});

test('frames exceeding the selected envelope stop and release rather than publish',async()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'480p30');
    await s.frame(1,1,{width:1920,height:1080,bytes:1920*1080*4});
    assert.equal(s.writes.length,0);assert.equal(s.released.length,1);assert.equal(s.context.WidgetRailNativeMedia.status().active,false);
    assert.ok(s.messages.some(m=>m.captureError==='RangeError'));
});

test('a replacement preset does not change the retired track target rate',()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'480p15');const old=s.tracks[0];
    s.context.WidgetRailNativeMedia.start(2,false,'720p30');assert.equal(old.readyState,'ended');assert.equal(old.getSettings().frameRate,15);assert.equal(s.tracks[1].getSettings().frameRate,30);
});

test('a host-selected non-Discord origin can initialize the same transport',()=>{
    const s=setup('https://another-provider.example');assert.equal(s.context.WidgetRailNativeMedia,undefined);
    s.context.initializeWidgetRailNativeCapture(['https://another-provider.example']);
    assert.equal(s.context.WidgetRailNativeMedia.start(1,false),true);
});

test('generic transport does not inspect provider modules',()=>{
    const s=setup();s.context.WidgetRailWebWebpack={findStore(){throw Error('provider access');}};
    s.context.WidgetRailNativeMedia.start(1,false);assert.equal(s.context.WidgetRailNativeMedia.status().active,true);
});

test('stale host stop and keep cannot affect a replacement',()=>{
 const s=setup(),api=s.context.WidgetRailNativeMedia;
 api.start(1,false);api.start(2,false);
 assert.equal(api.stop(1),false);assert.equal(api.keep(1),false);
 assert.equal(api.status().active,true);assert.equal(s.tracks[1].readyState,'live');
 s.setNow(12000);api.status();s.timers[0]();assert.equal(api.status().active,false);
});
test('duplicate or older host start cannot replace the current stream',()=>{
 const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(2,false);
 assert.equal(api.start(2,true),false);assert.equal(api.start(1,true),false);
 assert.equal(s.tracks.length,1);assert.equal(s.tracks[0].readyState,'live');
 api.stop(2);assert.equal(api.start(2,false),false);
});
test('only the current epoch renews the capture lease',()=>{
 const s=setup(),api=s.context.WidgetRailNativeMedia;api.start(1,false);
 s.setNow(9000);assert.equal(api.keep(1).active,true);
 s.setNow(12000);s.timers[0]();assert.equal(api.status().active,true);
 s.setNow(20000);s.timers[0]();assert.equal(api.status().active,false);
});

test('normal source retirement ends via writer without suppressing consumer ended events',async()=>{
 const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.context.WidgetRailNativeMedia.stop(1);
 await Promise.resolve();assert.ok(s.tracks.every(t=>t.readyState==='ended'));assert.ok(s.tracks.every(t=>!t.stopped));
});
test('rejected source abort falls back only on its retired tracks',async()=>{
 const s=setup(),Real=s.context.MediaStreamTrackGenerator;
 s.context.MediaStreamTrackGenerator=class extends Real{constructor(args){super(args);this.writable.getWriter=()=>({write:async()=>{},abort:async()=>{throw Error('closed sink');}});}};
 s.context.WidgetRailNativeMedia.start(1,false);s.context.MediaStreamTrackGenerator=Real;s.context.WidgetRailNativeMedia.start(2,false);
 await new Promise(resolve=>setImmediate(resolve));assert.equal(s.tracks[0].stopped,true);assert.equal(s.tracks[1].readyState,'live');assert.equal(s.tracks[1].stopped,undefined);
});

test('a stuck abort has a bounded fallback and cannot retire a replacement source',()=>{
 const s=setup(),Real=s.context.MediaStreamTrackGenerator,timers=[];
 s.context.setTimeout=(callback,delay)=>{assert.equal(delay,1000);const timer={callback};timers.push(timer);return timer;};
 s.context.clearTimeout=timer=>{if(timer)timer.cleared=true;};
 s.context.MediaStreamTrackGenerator=class extends Real{constructor(args){super(args);this.writable.getWriter=()=>({write:async()=>{},abort:()=>new Promise(()=>{})});}};
 s.context.WidgetRailNativeMedia.start(1,false);s.context.MediaStreamTrackGenerator=Real;s.context.WidgetRailNativeMedia.start(2,false);
 assert.equal(timers.length,1);assert.equal(s.tracks[0].readyState,'live');timers[0].callback();
 assert.equal(s.tracks[0].stopped,true);assert.equal(timers[0].cleared,true);assert.equal(s.tracks[1].readyState,'live');assert.equal(s.context.WidgetRailNativeMedia.status().active,true);
});

test('content preference defaults to detail and motion is explicit without changing capture pacing',async()=>{
    const s=setup();assert.equal(s.context.WidgetRailNativeMedia.start(1,false,'1080p60'),true);
    assert.equal(s.tracks[0].contentHint,'detail');assert.equal(s.context.WidgetRailNativeMedia.status().targetFrameRate,60);
    assert.equal(s.context.WidgetRailNativeMedia.start(2,false,'1080p60','motion'),true);
    assert.equal(s.tracks[1].contentHint,'motion');assert.equal(s.context.WidgetRailNativeMedia.status().contentPreference,'motion');
    assert.equal(s.context.WidgetRailNativeMedia.status().targetFrameRate,60);
    const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame(2);assert.equal((await pending).getVideoTracks()[0].contentHint,'motion');
});

test('invalid content preference is rejected before replacing the admitted source',()=>{
    const s=setup();s.context.WidgetRailNativeMedia.start(1,false,'720p30','motion');
    for(const value of ['balanced','',null,{},1])assert.equal(s.context.WidgetRailNativeMedia.start(2,false,'720p30',value),false);
    assert.equal(s.tracks.length,1);assert.equal(s.tracks[0].readyState,'live');assert.equal(s.context.WidgetRailNativeMedia.status().active,true);
});
