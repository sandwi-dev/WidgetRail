import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const read=path=>fs.readFileSync(new URL(path,import.meta.url),'utf8');
const build=JSON.parse(read('../../samples/DiscordWebViewWidget/web/provider/adapter-contract.json')).build;
const source=read('../../samples/DiscordWebViewWidget/web/provider/activity-provider.js')+'\n'+read('../../samples/DiscordWebViewWidget/web/provider/adapter.js')+`\ninitializeWidgetRailDiscordAdapter(${build});\n`+read('./discord-commands.js');
const owner='a'.repeat(32);
function setup(){
 const calls=[],state={account:'1',channelId:null,view:true,connect:true,action:true};
 const channel={id:'20',name:'Lobby',type:2,guild_id:'10'};
 const stores={
  UserStore:{getCurrentUser:()=>({id:state.account})},SelectedChannelStore:{getVoiceChannelId:()=>state.channelId},
  ChannelStore:{getChannel:id=>id==='20'?channel:null},PermissionStore:{can:bit=>bit===1024n?state.view:state.connect},
  GuildStore:{getGuilds:()=>({'10':{id:'10',name:'Server'}})},GuildChannelStore:{getChannels:()=>({voice:[{channel}]})},
  VoiceStateStore:{getVoiceStatesForChannel:()=>({})}
 };
 const wp={findStore:name=>stores[name],findByProps:()=>state.action?{selectVoiceChannel:id=>{calls.push(id);state.channelId=id;}}:undefined,findByCode:()=>undefined,Common:{FluxDispatcher:{dispatch(){throw Error('Raw dispatch forbidden');}}}};
 const context={WidgetRailWebWebpack:wp,setTimeout,clearTimeout,setInterval,clearInterval};
 vm.createContext(context);vm.runInContext(source,context);
 return {state,stores,calls,context,invoke:context.WidgetRailDiscordPrototype.invoke,
  join:(args={})=>context.WidgetRailDiscordPrototype.invoke({method:'join',owner,args:{account:'1',channelId:null,target:'20',...args}}),
  snapshot:()=>context.WidgetRailDiscordPrototype.invoke({method:'snapshot',owner})};
}
test('web join uses concrete action and current-call validation once',async()=>{
 const s=setup();assert.equal((await s.join()).ok,true);assert.equal((await s.join({channelId:'20'})).ok,true);assert.deepEqual(s.calls,['20']);
});
test('join rejects missing and unknown guild targets',async()=>{const s=setup();for(const target of [undefined,null,'999'])assert.equal((await s.join({target})).ok,false);assert.equal(s.calls.length,0);});
test('account change cannot trigger a guild voice action',async()=>{const s=setup();assert.equal((await s.join({account:'9'})).code,'account_changed');assert.equal(s.calls.length,0);});
test('invalid owner cannot invoke voice action',async()=>{const s=setup();assert.equal((await s.invoke({method:'join',owner:'invalid',args:{account:'1',channelId:null,target:'20'}})).ok,false);assert.equal(s.calls.length,0);});
test('leave validates current channel and preserves another call',async()=>{const s=setup();s.state.channelId='20';assert.equal((await s.invoke({method:'leave',owner,args:{account:'1',channelId:'21'}})).ok,false);assert.equal(s.calls.length,0);assert.equal((await s.invoke({method:'leave',owner,args:{account:'1',channelId:'20'}})).ok,true);assert.deepEqual(s.calls,[null]);});
test('other provider methods retain domain validation',async()=>{const s=setup();assert.equal((await s.invoke({method:'unknown',owner,args:{account:'1'}})).code,'unknown_command');});
test('permission revoked after catalog publication rejects generic join and removes executable row',async()=>{
 for(const permission of ['view','connect']){const s=setup();assert.equal((await s.snapshot()).value.channels.length,1);s.state[permission]=false;assert.equal((await s.join()).code,'channel_unavailable');assert.equal((await s.snapshot()).value.channels.length,0);assert.deepEqual(s.calls,[]);}
});
test('changed call rejects generic join without replacing the current call',async()=>{
 const s=setup();await s.snapshot();s.state.channelId='21';assert.equal((await s.join()).code,'call_changed');assert.equal(s.state.channelId,'21');assert.deepEqual(s.calls,[]);
});
test('missing permission channel or concrete-action APIs expose voice unavailable and do not join',async()=>{
 for(const change of [s=>delete s.stores.PermissionStore,s=>delete s.stores.PermissionStore.can,s=>delete s.stores.ChannelStore,s=>s.state.action=false]){
  const s=setup();change(s);const snapshot=await s.snapshot();assert.equal(snapshot.ok,true);assert.equal(snapshot.value.capabilities.voice,false);assert.equal(snapshot.value.channels.length,0);assert.equal((await s.join()).code,'voice_action_unavailable');assert.deepEqual(s.calls,[]);
 }
});
test('call change during permission lookup cannot slip through admission',async()=>{
 const s=setup();s.stores.PermissionStore.can=()=>{s.state.channelId='21';return true;};assert.equal((await s.join()).code,'call_changed');assert.deepEqual(s.calls,[]);
});
test('non-guild voice targets are not admitted or advertised',async()=>{
 for(const type of [1,3,13]){const s=setup();s.stores.ChannelStore.getChannel=id=>({id,type,guild_id:'10'});assert.equal((await s.join()).code,'channel_unavailable');assert.equal((await s.snapshot()).value.channels.length,0);}
});
