import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {webcrypto} from 'node:crypto';
import vm from 'node:vm';
import test from 'node:test';

const script = await readFile(new URL('./experimental-encoded-source.js', import.meta.url), 'utf8');
const bind = vm.runInNewContext(script+'\nbindPreviewBitratePreset', {crypto:webcrypto, TextEncoder});
// Exact inspected selector, with its quality-record constructor supplied by the fixture.
const selector = [
    'getGoliveQuality(e,t){if(this.goliveMaxQuality.encode?.pixelCount===void 0||t<=0)return this.goliveMaxQuality;',
    'let n=null!==this.fakeGoLiveEncodePixelCount?Math.min(this.fakeGoLiveEncodePixelCount,this.goliveMaxQuality.encode.pixelCount):this.goliveMaxQuality.encode.pixelCount;',
    'if(t>=n)return this.goliveMaxQuality;let i=Math.min(.02*this.goliveMaxQuality.encode.pixelCount*this.goliveMaxQuality.encode.framerate,this.goliveMaxQuality.bitrateMax),',
    'r=this.scaleLinearly(t,n,this.goliveMaxQuality.bitrateMin),a=this.scaleLinearly(t,n,this.goliveMaxQuality.bitrateMax),',
    'l=null!=this.goliveMaxQuality.bitrateTarget?this.scaleLinearly(t,n,this.goliveMaxQuality.bitrateTarget):void 0;',
    'return new s({encode:this.goliveMaxQuality.encode,capture:this.goliveMaxQuality.capture,bitrateMin:Math.max(Math.ceil(r),this.options.videoBitrateFloor),',
    'bitrateMax:Math.max(Math.ceil(a),i),bitrateTarget:null!=l?Math.max(Math.ceil(l),this.options.videoBitrateFloor):void 0,localWant:e})}'
].join('');

function fixture(width=1280,height=720,fps=30) {
    const original = vm.runInNewContext('({'+selector+'}).getGoliveQuality', {s:class { constructor(values){Object.assign(this,values);} }});
    const state = {pixels:101468, viewer:true, current:true, updates:0};
    const size = {width,height,framerate:fps,pixelCount:width*height};
    const preset = {capture:{...size}, encode:{...size}, bitrateMin:500000, bitrateTarget:600000, bitrateMax:3500000};
    const encoding = {active:true, maxBitrate:Math.max(Math.ceil(state.pixels*preset.bitrateMax/size.pixelCount), Math.min(0.02*size.pixelCount*fps,preset.bitrateMax))};
    const sender = {getParameters:()=>({encodings:[encoding]})};
    const manager = Object.assign(Object.create({getGoliveQuality:original}), {
        goliveMaxQuality:preset, contextType:'stream', fakeGoLiveEncodePixelCount:null,
        options:{videoBitrateFloor:10000}, scaleLinearly:(pixels,area,bits)=>pixels*bits/area
    });
    const pc = {remoteDescription:{}};
    const limits = {media:3500000, hint:3500000};
    const connection = {pc, videoTransceiver:{mid:'1',sender},videoPayloadType:102, videoQualityManager:manager,
        videoStreamParameters:[{active:true,quality:100,ssrc:123}],sdp:'c=IN IP4 0.0.0.0\r\n',
        getRemoteVideoSinkPixelCount:ssrc=>ssrc===123?state.pixels:0,
        updateVideoQuality(){state.updates++;encoding.maxBitrate=manager.getGoliveQuality(100,state.pixels).bitrateMax;}
    };
    manager.connection=connection;
    const install=()=>bind({connection,pc,sender,format:{width,height,fps}},()=>state.current,()=>state.viewer,()=>limits);
    return {state,preset,encoding,connection,manager,original,limits,install,sender};
}

test('the preview policy keeps only the preset maximum and reconciles roster edges without polling writes',async()=>{
    const f=fixture();f.state.viewer=false;
    const baseline=f.manager.getGoliveQuality(100,f.state.pixels);
    const policy=await f.install();
    policy.sync();assert.equal(f.state.updates,0);assert.equal(policy.applied,false);
    f.state.viewer=true;policy.sync();policy.sync();
    assert.equal(f.encoding.maxBitrate,3500000);assert.equal(f.state.updates,1);
    const effective=f.manager.getGoliveQuality(100,f.state.pixels);
    assert.deepEqual({...effective,bitrateMax:baseline.bitrateMax},{...baseline});
    assert.equal(Object.getPrototypeOf(effective),Object.getPrototypeOf(baseline));
    assert.equal(policy.applied,true);assert.equal(f.connection.getRemoteVideoSinkPixelCount(123),101468);
    f.state.viewer=false;policy.sync();
    assert.equal(f.encoding.maxBitrate,552960);assert.equal(policy.applied,false);
    f.state.viewer=true;policy.sync();policy.release(true);
    assert.equal(f.encoding.maxBitrate,552960);assert.equal(f.manager.getGoliveQuality,f.original);
    assert.equal(Object.hasOwn(f.manager,'getGoliveQuality'),false);
});

test('Prefer smoothness protects the preview bitrate with the adaptive WebRTC track still attached',async()=>{
    const f=fixture();
    let check, finishHash, encoderStarts=0;
    const track=Object.assign(new EventTarget(), {
        readyState:'live',contentHint:'motion',getConstraints:()=>({width:1280,height:720})
    });
    const desktop={stream:{getVideoTracks:()=>[track]}};
    Object.assign(f.sender,{
        track,
        getParameters:()=>({encodings:[f.encoding],codecs:[{
            mimeType:'video/H264',payloadType:102,sdpFmtpLine:'profile-level-id=42e01f'
        }]}),
        async getStats(){encoderStarts++;return new Map();},
        async replaceTrack(value){f.sender.track=value;},
        async createEncodedSource(){}
    });
    Object.assign(f.connection,{
        context:'stream',userId:'1',streamUserId:'1',destroyed:false,input:{desktop}
    });
    f.connection.pc.signalingState='stable';
    const engine={desktopInputPool:{},connections:new Set([f.connection])};
    const rtc={context:'stream',isOwner:true,_destroyed:false,_connection:f.connection,streamKey:'call:c:1',
        streamContext:{channelId:'c',ownerId:'1'}};
    const stores={
        StreamRTCConnectionStore:{getRTCConnection:()=>rtc},
        MediaEngineStore:{getMediaEngine:()=>engine},
        ApplicationStreamingStore:{getViewerIds:()=>f.state.viewer?['2']:[]}
    };
    const create=vm.runInNewContext(script+'\ncreateExperimentalCapture',{
        crypto:{subtle:{digest:(...args)=>finishHash=webcrypto.subtle.digest(...args)}},TextEncoder,performance,
        setTimeout(callback){check=callback;return 1;},clearTimeout(){},
        WidgetRailNativeMedia:{status:()=>({active:true,quality:'720p30',contentPreference:'motion',width:1280,height:720})}
    });
    const capture=create({
        store:name=>stores[name],me:()=>({id:'1'}),channelId:()=>'c',
        currentShare:()=>({ownerId:'1',channelId:'c'}),streamKey:()=>rtc.streamKey,
        currentEpoch:()=>1,peekDesktopSource:()=>desktop,videoRateLimits:()=>({...f.limits,h264Level:31})
    });
    capture.prepare({account:'1',channelId:'c'},{resolution:720,frameRate:30},engine,'game',1);
    check();
    assert.ok(finishHash,'the selected Smoothness share starts the preview policy');
    await finishHash;
    await new Promise(resolve=>setImmediate(resolve));
    check();
    assert.equal(f.encoding.maxBitrate,3500000);
    assert.equal(capture.sample(f.connection).presetBitrateActive,true);
    assert.equal(capture.sample(f.connection).encoderPath,'webrtc');
    assert.equal(capture.sample(f.connection).encoderFallback,null);
    assert.equal(f.sender.track,track);
    assert.equal(track.contentHint,'motion');
    assert.equal(encoderStarts,0);

    f.encoding.scaleResolutionDownBy=2;
    f.connection.updateVideoQuality();
    assert.equal(f.encoding.scaleResolutionDownBy,2);
    assert.equal(f.encoding.maxBitrate,552960);
    assert.equal(capture.sample(f.connection).presetBitrateActive,false);
    capture.dispose();
    assert.equal(f.manager.getGoliveQuality,f.original);
    assert.equal(f.sender.track,track);
});

test('limits, inactive or scaled encodings, wrong ownership, and other presets retain provider behavior',async()=>{
    for(const change of [
        f=>f.state.viewer=false,f=>f.state.current=false,f=>f.limits.media=1000000,f=>f.limits.hint=null,f=>f.limits.otherBandwidth=true,
        f=>f.connection.sdp+='b=TIAS:1200000\r\n',f=>f.encoding.maxBitrate=750000,
        f=>f.encoding.active=false,f=>f.encoding.maxBitrate=0,f=>f.encoding.scaleResolutionDownBy=2,
        f=>f.manager.qualityOverwrite={bitrateMax:700000},f=>f.preset.capture.framerate=60,
        f=>f.state.pixels=0,f=>f.state.pixels=2111875,f=>f.connection.videoStreamParameters[0].active=false
    ]){
        const f=fixture(),policy=await f.install();change(f);
        const expected=f.original.call(f.manager,100,f.state.pixels);
        assert.deepEqual(f.manager.getGoliveQuality(100,f.state.pixels),expected);
        assert.equal(policy.applied,false);policy.release(false);
    }
});

test('unknown or replaced methods and a share retired while hashing are left alone',async()=>{
    const f=fixture();f.manager.getGoliveQuality=()=>f.preset;assert.equal(await f.install(),null);
    delete f.manager.getGoliveQuality;
    const pending=f.install();f.state.current=false;assert.equal(await pending,null);
    f.state.current=true;const policy=await f.install();
    const replacement=()=>f.preset;f.manager.getGoliveQuality=replacement;policy.release(true);
    assert.equal(f.manager.getGoliveQuality,replacement);assert.equal(f.state.updates,0);
});

test('a viewer can join and leave between observations, including after a separate sender limit changes',async()=>{
    for (const independentMaximum of [null,700000]) {
        const f=fixture(),policy=await f.install();
        f.connection.updateVideoQuality();
        assert.equal(f.encoding.maxBitrate,3500000);
        f.state.viewer=false;
        if(independentMaximum!==null)f.encoding.maxBitrate=independentMaximum;
        policy.sync();
        assert.equal(f.encoding.maxBitrate,independentMaximum??552960);
        assert.equal(policy.applied,false);
        policy.release(false);
    }
});

test('the capture policy works independently of encoder at every Discord preset, including ultrawide',async()=>{
    for(const [width,height] of [[854,480],[1706,480],[1280,720],[2560,720],[1920,1080],[3840,1080]]) {
        for(const fps of [15,30,60]) {
            const f=fixture(width,height,fps);
            f.limits.media=null; // A known codec hint can also be sufficient without TIAS.
            const baseline=f.encoding.maxBitrate;
            const policy=await f.install();
            policy.sync();
            const max=f.preset.bitrateMax;
            assert.equal(f.encoding.maxBitrate,max);
            assert.equal(policy.applied,baseline<max);
            policy.release(true);
            assert.equal(f.encoding.maxBitrate,baseline);
        }
    }
});
