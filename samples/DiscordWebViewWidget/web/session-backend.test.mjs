import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
import { webcrypto } from 'node:crypto';

let source=readFileSync(new URL('./session-backend.js',import.meta.url),'utf8');
if(process.env.WRAIL_MINIFIED_TESTS==='1')source=await(await import('../../../tools/DiscordWebViewPrototype/Build-Backend.mjs')).minifySource(source);
const owner='a'.repeat(32);
function fixture(count=1000){
  let handler;const calls=[];
  const state={revision:1,account:'1',ready:true,channelId:'1',channelName:'Voice',capabilities:{voice:true,mute:true,deafen:true,watch:true,share:true,stopShare:true},
    muted:false,deafened:false,sharing:false,sharePending:false,shareError:null,relayState:'closed',participants:[],streams:[],
    channels:Array.from({length:count},(_,i)=>({id:String(i+1),guildId:'1',name:`Channel ${i}`,guild:'Guild'}))};
  const context={crypto:webcrypto,host:{onRequest(fn){handler=fn;},batchResult(context,items){return {context,items,batched:true};}},
    WidgetRailDiscordPrototype:{async invoke(request){calls.push(request);return request.method==='snapshot'?{ok:true,value:structuredClone(state)}:{ok:true,value:{}};}}};
  vm.runInNewContext(source,context);
  return {state,calls,context,enableNativeCapture(){context.WidgetRailNativeMedia={start(){return true;}};},invoke:(method,payload={owner},signal=new AbortController().signal)=>handler(method,payload,{signal})};
}
test('complete 1,000-channel catalog uses framework batching and preserves order',async()=>{
  const f=fixture();const status=await f.invoke('snapshot');const result=await f.invoke('channels',{owner,account:'1',channelVersion:status.context.channelVersion});
  assert.equal(result.batched,true);assert.equal(result.items.length,1000);assert.equal(result.items[999].id,'1000');
  assert.equal(result.context.channels,undefined);assert.equal(status.context.channelCount,1000);assert.equal(status.items.length,0);
});
test('status remains small; content and account changes invalidate the catalog version',async()=>{
  const f=fixture();const first=await f.invoke('snapshot');
  const same=await f.invoke('snapshot');
  assert.equal(same.context.channelVersion,first.context.channelVersion);assert.equal(same.items.length,0);assert.equal(same.batched,undefined);
  f.state.channels[999].name='Renamed';const changed=await f.invoke('snapshot');
  assert.notEqual(changed.context.channelVersion,first.context.channelVersion);
  await assert.rejects(f.invoke('channels',{owner,account:'1',channelVersion:first.context.channelVersion}),/Channel catalog changed/);
  f.state.account='2';const account=await f.invoke('snapshot');
  assert.notEqual(account.context.channelVersion,changed.context.channelVersion);
  await assert.rejects(f.invoke('channels',{owner,account:'1',channelVersion:account.context.channelVersion}),/Channel catalog changed/);
});
test('empty catalogs do not manufacture a transport item',async()=>{
  const result=await fixture(0).invoke('snapshot');assert.equal(result.items.length,0);assert.equal(result.context.channelCount,0);
});
test('oversized catalogs fail rather than truncate silently',async()=>{
  await assert.rejects(fixture(1001).invoke('snapshot'),/Invalid Discord catalog/);
});
test('worst-case Unicode status and1000channels fit the actual host batching budgets without dropping any items',async()=>{
  const f=fixture();const label='界'.repeat(120);
  f.state.channelName=label;
  f.state.shareQualities=[480,720,1080,1440].flatMap(resolution=>[15,30,60].map(frameRate=>({resolution,frameRate})));
  f.state.shareQualityAvailability='ready';f.state.preferredShareQuality={resolution:1440,frameRate:60};
  f.state.activityAvailability='ready';f.state.activities=Array.from({length:50},(_,i)=>({userId:String(i+2),name:label,status:'online',kind:'playing',title:label,details:label,channelId:'20',guildId:'10',channelName:label,guildName:label,canJoinVoice:true,streamKey:null,canWatch:false,startedAt:1000}));
  f.state.channels=f.state.channels.map(channel=>({...channel,name:label,guild:label}));
  f.state.participants=Array.from({length:100},(_,i)=>({id:String(i+1),name:label,muted:false,deafened:false,speaking:true}));
  f.state.streams=Array.from({length:50},(_,i)=>({key:'guild:'+String(i).padStart(90,'0'),userId:String(i+1),name:label}));
  const status=await f.invoke('snapshot');const catalog=await f.invoke('channels',{owner,account:'1',channelVersion:status.context.channelVersion});
  assert.equal(status.items.length,200);assert.equal(catalog.items.length,1000);
  const host=readFileSync(new URL('../../../src/OverlayFrontend.WinUI/Browser/WebSessionPageAdapterHost.cs',import.meta.url),'utf8');
  const prelude=host.match(/private const string BatchPrelude\s*=\s*"""\r?\n([\s\S]*?)\r?\n\s*""";/)?.[1];
  assert.ok(prelude,'Read the current host batching algorithm');
  const scope={crypto:webcrypto,TextEncoder,maximumBatchItems:1000,location:{href:'https://discord.com/channels/1/2'}};
  vm.runInNewContext(prelude+'\nglobalThis.testBatch=batchPayloads;',scope);
  const size=value=>Buffer.byteLength(JSON.stringify(value),'utf8');
  for(const result of [status,catalog]){
    assert.ok(size(result.context)<2048,'Only bounded metadata remains in repeated context');
    const frames=scope.testBatch(result);
    assert.ok(frames.length<=48);assert.equal(frames.reduce((sum,frame)=>sum+frame.items.length,0),result.items.length);
    assert.ok(frames.reduce((sum,frame)=>sum+size(frame),0)<=3*1024*1024,'SDK aggregate bound');
    for(const payload of frames)assert.ok(size({widgetRail:1,kind:'response-batch',id:owner,payload})<=65536-2048,'Host binding frame bound');
  }
});
test('arbitrary methods are unavailable through this endpoint',async()=>{
  const f=fixture();
  for(const method of ['show','evaluate','fetch'])await assert.rejects(f.invoke('command',{owner,method,args:{}}),/Command unavailable/);
  assert.equal(f.calls.length,0);
});
test('share cannot fall back to the browser chooser when host native transport is missing',async()=>{
  const f=fixture();const result=await f.invoke('command',{owner,method:'share',args:{captureQuality:{resolution:720,frameRate:30}}});
  assert.equal(result.ok,false);assert.equal(result.code,'native_capture_unavailable');assert.equal(f.calls.length,0);
});
test('share requires explicit supported host-selected quality before provider invocation',async()=>{
  const f=fixture();f.enableNativeCapture();
  for(const quality of [undefined,{resolution:2160,frameRate:60},{resolution:720,frameRate:120},{resolution:720,frameRate:30,windowId:'forged'}])
    await assert.rejects(f.invoke('command',{owner,method:'share',args:{captureQuality:quality}}),/Capture quality unavailable/);
  assert.equal(f.calls.length,0);
  const args={account:'1',channelId:'2',captureQuality:{resolution:480,frameRate:15}};
  await f.invoke('command',{owner,method:'share',args});assert.equal(f.calls.length,1);assert.equal(f.calls[0].args,args);
});

test('bounded high quality requests preserve provider eligibility validation and native source requirement',async()=>{
  const f=fixture();f.enableNativeCapture();
  for(const resolution of [480,720,1080,1440])for(const frameRate of [15,30,60]){
    const args={account:'1',channelId:'2',captureQuality:{resolution,frameRate}};
    await f.invoke('command',{owner,method:'share',args});assert.equal(f.calls.at(-1).args,args);
  }
  assert.equal(f.calls.length,12,'Provider still owns eligibility for each forwarded quality');
});

test('quality metadata is bounded and preserves an ineligible saved preference without changing it',async()=>{
  const f=fixture(0);f.state.shareQualities=[{resolution:720,frameRate:30}];
  f.state.shareQualityAvailability='ready';f.state.preferredShareQuality={resolution:1440,frameRate:60};
  const snapshot=await f.invoke('snapshot');assert.deepEqual(snapshot.context.preferredShareQuality,f.state.preferredShareQuality);
  for(const invalid of [null,{},Array(13).fill({resolution:720,frameRate:30}),[{resolution:2160,frameRate:30}],
    [{resolution:720,frameRate:30},{resolution:720,frameRate:30}]]){
    f.state.shareQualities=invalid;await assert.rejects(f.invoke('snapshot'),/Invalid share qualities/);
  }
});
test('owner validation and cancellation occur before provider invocation',async()=>{
  const f=fixture();await assert.rejects(f.invoke('snapshot',{owner:'bad'}),/Invalid owner/);
  const cancel=new AbortController();cancel.abort();await assert.rejects(f.invoke('snapshot',{owner},cancel.signal),/Cancelled/);
  assert.equal(f.calls.length,0);
});
test('supported commands preserve provider authorization payloads',async()=>{
  const f=fixture();const args={account:'1',channelId:'2',target:'3'};
  await f.invoke('command',{owner,method:'join',args});
  assert.equal(f.calls[0].method,'join');assert.equal(f.calls[0].args,args);assert.equal(f.calls[0].owner,owner);
});

test('friend activity uses existing bounded batching without duplicating rows in context',async()=>{
 const f=fixture(1);f.state.activityAvailability='ready';f.state.activitiesTruncated=true;
 f.state.activities=Array.from({length:50},(_,i)=>({userId:String(i+2),name:'Friend',status:'online',kind:'playing',title:'Game',details:'',channelId:null,guildId:null,channelName:null,guildName:null,canJoinVoice:false,streamKey:null,canWatch:false,startedAt:null}));
 const status=await f.invoke('snapshot');assert.equal(status.context.activities,undefined);assert.equal(status.context.activityAvailability,'ready');assert.equal(status.context.activitiesTruncated,true);
 assert.equal(status.items.filter(i=>i.kind==='activity').length,50);assert.equal(status.items[49].value.userId,'51');
 f.state.activities.push(f.state.activities[0]);await assert.rejects(f.invoke('snapshot'),/Invalid Discord activity/);
});
test('activity commands preserve captured account peer channel and exact stream fields',async()=>{
 const f=fixture();const args={account:'1',channelId:'20',userId:'2',target:'20',key:'guild:10:20:2',lease:'b'.repeat(32)};
 for(const method of ['joinActivity','watchActivity']){await f.invoke('command',{owner,method,args});assert.equal(f.calls.at(-1).method,method);assert.equal(f.calls.at(-1).args,args);}
});

test('stream audio commands preserve exact authority and desired values without transport rewriting',async()=>{
 const f=fixture(0);
 for(const [method,value] of [['streamMute',false],['streamVolume',37.5]]){
  const args={account:'1',channelId:'10',key:'guild:1:10:30',userId:'30',lease:'b'.repeat(32),value};
  await f.invoke('command',{owner,method,args});
  const forwarded=f.calls.at(-1);
  assert.equal(forwarded.method,method);assert.equal(forwarded.owner,owner);
  assert.equal(forwarded.args,args,'Account, call, key, peer, lease and value pass through as the exact captured argument object');
  assert.deepEqual(forwarded.args,{account:'1',channelId:'10',key:'guild:1:10:30',userId:'30',lease:'b'.repeat(32),value});
 }
 assert.equal(f.calls.length,2,'Audio writes do not inject extra provider requests');
});

test('on-demand stream diagnostics forward exact authority without starting capture or changing provider arguments',async()=>{
 const f=fixture(0),args={account:'1',channelId:'20'};
 await f.invoke('command',{owner,method:'streamDiagnostics',args});
 assert.equal(f.calls.length,1);assert.equal(f.calls[0].method,'streamDiagnostics');assert.equal(f.calls[0].args,args);assert.equal(f.calls[0].owner,owner);
});

test('cancelled diagnostic reads are not delivered after the provider completes',async()=>{
 const f=fixture(0);let finish;
 f.context.WidgetRailDiscordPrototype.invoke=request=>{f.calls.push(request);return new Promise(resolve=>finish=resolve);};
 const cancel=new AbortController();const result=f.invoke('command',{owner,method:'streamDiagnostics',args:{account:'1',channelId:'20'}},cancel.signal);
 cancel.abort();finish({ok:true,value:{source:{width:1},outbound:[],advertised:[]}});
 await assert.rejects(result,/Cancelled/);assert.equal(f.calls.length,1);
});

test('snapshot batching preserves displayed volume boundary and unavailable audio',async()=>{
 const f=fixture(0);
 f.state.streams=[
  {key:'guild:1:10:30',userId:'30',name:'Boosted',audio:{available:true,muted:true,volume:100}},
  {key:'guild:1:10:31',userId:'31',name:'Ordinary',audio:{available:true,muted:false,volume:37.5}},
  {key:'guild:1:10:32',userId:'32',name:'Unavailable',audio:{available:false,muted:false,volume:null}},
  {key:'guild:1:10:33',userId:'33',name:'Legacy'}
 ];
 const before=JSON.stringify(f.state.streams);
 const wire=JSON.parse(JSON.stringify(await f.invoke('snapshot')));
 assert.equal(wire.batched,true);assert.equal(wire.context.streams,undefined);
 assert.equal(wire.items.length,4);assert.ok(wire.items.every(item=>item.kind==='stream'));
 assert.deepEqual(wire.items.map(item=>item.value),f.state.streams);
 assert.equal(wire.items[0].value.audio.volume,100,'Provider-projected display boundary is preserved unchanged');
 assert.equal(wire.items[2].value.audio.volume,null,'Unavailable volume remains absent');
 assert.equal(JSON.stringify(f.state.streams),before,'Transport does not mutate provider observations');
});

test('voice controls preserve account peer authority and never change stream context in transport',async()=>{
 const f=fixture(0),args={account:'1',channelId:'20',userId:'2',value:50};
 for(const method of ['voiceMute','voiceVolume','voiceSettings','voiceInputDevice','voiceOutputDevice','voiceInputVolume']){
  await f.invoke('command',{owner,method,args});const forwarded=f.calls.at(-1);
  assert.equal(forwarded.method,method);assert.equal(forwarded.args,args);assert.equal(forwarded.owner,owner);
 }
 assert.equal(f.calls.length,6);
});

test('cancelled voice settings read cannot populate a retired modal after provider completion',async()=>{
 const f=fixture(0);let finish;f.context.WidgetRailDiscordPrototype.invoke=request=>{f.calls.push(request);return new Promise(resolve=>finish=resolve);};
 const cancel=new AbortController(),result=f.invoke('command',{owner,method:'voiceSettings',args:{account:'1'}},cancel.signal);
 cancel.abort();finish({ok:true,value:{inputDevices:[]}});await assert.rejects(result,/Cancelled/);assert.equal(f.calls.length,1);
});

test('RTC phase and participant voice audio survive normal batching without eager device enumeration',async()=>{
 const f=fixture(0);f.state.voiceConnection='reconnecting';f.state.participants=[{id:'2',name:'Peer',muted:false,deafened:false,speaking:true,audio:{available:true,muted:true,volume:25}}];
 const result=await f.invoke('snapshot');assert.equal(result.context.voiceConnection,'reconnecting');
 assert.equal(result.items[0].kind,'participant');assert.deepEqual(result.items[0].value.audio,f.state.participants[0].audio);
 assert.equal(f.calls.length,1);assert.equal(f.calls[0].method,'snapshot');
});
