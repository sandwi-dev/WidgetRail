import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('./receiver.js',import.meta.url),'utf8');
function fixture(){
 const handlers={},messages=[],peers=[],page={};let rejectPlay=null;
 const status={textContent:''},video={srcObject:null,videoWidth:0,videoHeight:0,play:()=>Promise.resolve()};
 class Peer{constructor(){this.connectionState='new';this.iceGatheringState='complete';peers.push(this);}close(){this.connectionState='closed';this.onconnectionstatechange?.();}async setRemoteDescription(){}async createAnswer(){return {type:'answer',sdp:'test'};}async setLocalDescription(v){this.localDescription={toJSON:()=>v};}}
 class Track{constructor(){this.kind='video';this.events={};}addEventListener(type,fn){this.events[type]=fn;}end(){this.events.ended?.();}}
 const context={document:{getElementById:id=>id==='video'?video:status},RTCPeerConnection:Peer,MediaStream:class{constructor(tracks){this.tracks=tracks;}},setTimeout,Date,
  window:{addEventListener:(event,fn)=>page[event]=fn},WidgetRailWebSession:{handle:(method,fn)=>handlers[method]=fn,post:message=>messages.push(message)}};
 vm.runInNewContext(source,context);
 const offer=(lease='a'.repeat(32),healthEvents)=>handlers.offer({lease,offer:{type:'offer',sdp:'test'},...(healthEvents===undefined?{}:{healthEvents})},{signal:new AbortController().signal});
 return {handlers,messages,peers,video,status,page,offer,health:()=>messages.filter(m=>m.receiverHealth===1),
  state:value=>{const peer=peers.at(-1);peer.connectionState=value;peer.onconnectionstatechange?.();},
  track:()=>{const track=new Track();peers.at(-1).ontrack({track});return track;},
  holdPlay:()=>{video.play=()=>new Promise((_,reject)=>rejectPlay=reject);},rejectPlay:()=>rejectPlay(Error('play blocked'))};
}
test('legacy receiver posts only ready even through failure and teardown',async()=>{
 const f=fixture();await f.offer();f.state('disconnected');f.state('failed');f.handlers.stop();assert.equal(f.messages.length,1);assert.equal(f.messages[0].ready,true);
});
test('opted-in failed and closed connections emit one exact lease terminal message',async()=>{
 for(const state of ['failed','closed']){const f=fixture();await f.offer('a'.repeat(32),true);f.state(state);f.state('failed');f.state('closed');assert.equal(f.health().length,1);assert.equal(f.health()[0].lease,'a'.repeat(32));assert.equal(f.health()[0].phase,'rtc_'+state);}
});
test('transient disconnected recovers without retiring the receiver',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);f.state('disconnected');f.state('connected');assert.equal(f.health().length,0);assert.equal(f.status.textContent,'');
});
test('late RTC completion from a replaced connection is ignored',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);const old=f.peers[0];await f.offer('b'.repeat(32),true);old.connectionState='failed';old.onconnectionstatechange();assert.equal(f.health().length,0);f.state('failed');assert.equal(f.health()[0].lease,'b'.repeat(32));
});
test('late playback rejection from a replaced lease cannot change current status',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);f.holdPlay();f.track();await f.offer('b'.repeat(32),true);f.state('connected');f.rejectPlay();await new Promise(r=>setImmediate(r));assert.equal(f.health().length,0);assert.equal(f.status.textContent,'');
});
test('current playback rejection reports a bounded actionable terminal code',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);f.holdPlay();f.track();f.rejectPlay();await new Promise(r=>setImmediate(r));assert.equal(f.health().length,1);assert.equal(f.health()[0].phase,'playback_failed');
});
test('old track ended after replacement cannot terminate its current video track',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);const old=f.track(),current=f.track();old.end();assert.equal(f.health().length,0);current.end();assert.equal(f.health()[0].phase,'track_ended');
});
test('intentional stop and pagehide do not report a false receiver failure',async()=>{
 for(const end of [f=>f.handlers.stop(),f=>f.page.pagehide()]){const f=fixture();await f.offer('a'.repeat(32),true);end(f);assert.equal(f.health().length,0);}
});
test('malformed replacement offer leaves the current receiver intact',async()=>{
 const f=fixture();await f.offer('a'.repeat(32),true);await assert.rejects(f.offer('bad',true),/Invalid offer/);assert.equal(f.peers[0].connectionState,'new');assert.equal(f.health().length,0);
});
test('health events require explicit boolean opt-in',async()=>{
 for(const opt of [false,'true',1,null]){const f=fixture();await f.offer('a'.repeat(32),opt);f.state('failed');assert.equal(f.health().length,0);}
});
