import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('./native-media.js',import.meta.url),'utf8');
function setup(origin='https://discord.com'){
    let now=1000;const messages=[],tracks=[],released=[],writes=[],timers=[],listeners={};
    class Frame{constructor(...args){this.args=args;}close(){this.closed=true;}}
    class Track{constructor({kind}){this.kind=kind;this.readyState='live';tracks.push(this);this.writable={getWriter:()=>({write:async f=>{writes.push(f);},abort:async()=>{}})};}stop(){this.readyState='ended';}}
    class Stream{constructor(tracks){this.tracks=tracks;}getTracks(){return this.tracks;}}
    const context={location:{origin},navigator:{mediaDevices:{getDisplayMedia:async()=>{throw Error('must never call original picker');}}},
        Date:{now:()=>now},setTimeout,clearTimeout,DOMException,MediaStreamTrackGenerator:Track,MediaStream:Stream,VideoFrame:Frame,AudioData:Frame,Uint8Array,
        chrome:{webview:{postMessage:m=>messages.push(m),releaseBuffer:b=>released.push(b),addEventListener:(type,f)=>listeners[type]=f}},setInterval:f=>timers.push(f)};
    context.window=context;context.top=context;vm.createContext(context);vm.runInContext(source,context);
    return {context,messages,tracks,released,writes,timers,setNow:n=>now=n,frame:(epoch=1,kind=1)=>listeners.sharedbufferreceived({getBuffer:()=>new ArrayBuffer(16),additionalData:{epoch,kind,width:2,height:2,time:1,bytes:16}})};
}
test('inactive request does not open browser chooser',async()=>{const s=setup();await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia(),{name:'NotAllowedError'});});
test('explicit start is one-shot and audio is optional',async()=>{const s=setup();assert.equal(s.context.WidgetRailNativeMedia.start(1,false),true);const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame();const stream=await pending;assert.equal(stream.getTracks().length,1);await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('audio start produces distinct audio and video tracks',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);const pending=s.context.navigator.mediaDevices.getDisplayMedia();await s.frame();const stream=await pending;assert.deepEqual(Array.from(stream.getTracks(),t=>t.kind),['video','audio']);});
test('stop ends every owned track and rejects further capture',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.context.WidgetRailNativeMedia.stop();assert.ok(s.tracks.every(t=>t.readyState==='ended'));await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('new source retires old tracks',()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.context.WidgetRailNativeMedia.start(2,false);assert.deepEqual(s.tracks.map(t=>t.readyState),['ended','ended','live']);});
test('expired chooser authorization is rejected',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,false);s.setNow(17000);await assert.rejects(s.context.navigator.mediaDevices.getDisplayMedia());});
test('lost host heartbeat ends capture',()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,true);s.setNow(12000);s.timers[0]();assert.ok(s.tracks.every(t=>t.readyState==='ended'));});
test('stale frame is released and acknowledged without publication',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(2,false);await s.frame(1);assert.equal(s.writes.length,0);assert.equal(s.released.length,1);assert.equal(s.messages[0].captureEpoch,1);});
test('accepted frame is closed and buffer released after writing',async()=>{const s=setup();s.context.WidgetRailNativeMedia.start(1,false);await s.frame();assert.equal(s.writes.length,1);assert.equal(s.writes[0].closed,true);assert.equal(s.released.length,1);assert.equal(s.context.WidgetRailNativeMedia.keep().frames,1);});
test('late rejected write cannot stop a replacement session',async()=>{const s=setup();let reject;s.context.WidgetRailNativeMedia.start(1,false);s.tracks[0].writable.getWriter=()=>({write:()=>new Promise((_,r)=>reject=r),abort:async()=>{}});
    // Recreate using a writer whose completion is controlled by this test.
    const Original=s.context.MediaStreamTrackGenerator;s.context.MediaStreamTrackGenerator=class extends Original{constructor(args){super(args);this.writable.getWriter=()=>({write:()=>new Promise((_,r)=>reject=r),abort:async()=>{}});}};
    s.context.WidgetRailNativeMedia.start(2,false);const pending=s.frame(2);s.context.MediaStreamTrackGenerator=Original;s.context.WidgetRailNativeMedia.start(3,false);reject(Error('old writer cancelled'));await pending;assert.equal(s.tracks.at(-1).readyState,'live');assert.equal(s.context.WidgetRailNativeMedia.keep().active,true);
});
test('adapter does not install on unrelated origin',()=>{const s=setup('https://example.com');assert.equal(s.context.WidgetRailNativeMedia,undefined);});
