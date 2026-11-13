import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./activity-provider.js',import.meta.url),'utf8');
const build=JSON.parse(fs.readFileSync(new URL('./adapter-contract.json',import.meta.url),'utf8')).build;
const adapter=fs.readFileSync(new URL('./adapter.js',import.meta.url),'utf8')+`\ninitializeWidgetRailDiscordAdapter(${build});`;
function fixture(){
 const state={account:'1',channel:null,friends:['2'],blocked:false,friend:true,status:'online',visible:true,connect:true,discoverable:true,voice:true,stream:true,joinAvailable:true,watchAvailable:true};
 const calls=[];
 const stores={
  RelationshipStore:{getFriendIDs:()=>state.friends,isFriend:id=>state.friend&&state.friends.includes(id),isBlockedOrIgnored:()=>state.blocked},
  PresenceStore:{getStatus:()=>state.status,getActivities:()=>[{type:0,name:'A game',details:'Playing safely',timestamps:{start:1000},metadata:{button_urls:['SECRET']},session_id:'SECRET'}],getHiddenActivities(){throw Error('hidden');},getUnfilteredActivities(){throw Error('unfiltered');}},
  UserStore:{getCurrentUser:()=>({id:state.account}),getUser:id=>({id,globalName:'Friend '+id,token:'SECRET'})},
  VoiceStateStore:{getDiscoverableVoiceStateForUser:id=>state.voice?{userId:id,channelId:'20',discoverable:state.discoverable}:null,getVoiceStatesForChannel:()=>({})},
  ChannelStore:{getChannel:id=>id==='20'?{id,type:2,guild_id:'10',name:'Lobby'}:null},
  GuildStore:{getGuild:id=>({id,name:'Server'}),getGuilds:()=>({})},GuildChannelStore:{getChannels:()=>({})},
  PermissionStore:{can:bit=>bit===1024n?state.visible:state.connect},
  SelectedChannelStore:{getVoiceChannelId:()=>state.channel},
  ApplicationStreamingStore:{getAnyDiscoverableStreamForUser:id=>state.stream?{ownerId:id,guildId:'10',channelId:'20'}:null,isStreamMarkedFull:()=>false,getCurrentUserActiveStream:()=>null,getAllActiveStreamsForChannel:()=>[],getAllApplicationStreamsForChannel:()=>[]}
 };
 const wp={findStore:name=>stores[name],findByProps:()=>state.joinAvailable?{selectVoiceChannel:id=>{calls.push(id);state.channel=id;}}:undefined,
  findByCode:()=>state.watchAvailable?()=>{}:undefined,Common:{FluxDispatcher:{dispatch(){throw Error('raw dispatch');}}}};
 const context={Date,Map,Set,WidgetRailWebWebpack:wp,setTimeout,clearTimeout,setInterval,clearInterval};vm.createContext(context);vm.runInContext(source,context);
 const provider=context.createWidgetRailDiscordActivities({store:name=>stores[name],me:()=>({id:state.account}),currentChannel:()=>state.channel,voiceActionAvailable:()=>state.joinAvailable,watchAvailable:()=>state.watchAvailable});
 return {state,stores,calls,context,provider,join:(args={})=>provider.validateJoin({account:'1',channelId:null,userId:'2',target:'20',...args}),
  watch:(args={})=>provider.validateWatch({account:'1',channelId:'20',userId:'2',target:'20',key:'guild:10:20:2',...args})};
}
test('projects only bounded visible fields without contacts or hidden data',()=>{
 const f=fixture(),s=f.provider.snapshot();assert.equal(s.activityAvailability,'ready');assert.equal(s.activities.length,1);
 const row=s.activities[0];assert.equal(row.userId,'2');assert.equal(row.kind,'playing');assert.equal(row.title,'A game');assert.equal(row.canJoinVoice,true);assert.equal(row.canWatch,false);
 assert.equal(JSON.stringify(s).includes('SECRET'),false);assert.deepEqual(f.calls,[]);
});
test('empty, unavailable and disconnected are distinct and late stores recover',()=>{
 const f=fixture();f.state.friends=[];assert.equal(f.provider.snapshot().activityAvailability,'ready');
 const saved=f.stores.PresenceStore;delete f.stores.PresenceStore;assert.equal(f.provider.snapshot().activityAvailability,'unavailable');
 f.stores.PresenceStore=saved;assert.equal(f.provider.snapshot().activityAvailability,'ready');f.state.account=null;assert.equal(f.provider.snapshot().activityAvailability,'disconnected');
});
test('missing required methods fail closed without partial friend data',()=>{
 for(const [store,method] of [['RelationshipStore','isBlockedOrIgnored'],['PresenceStore','getActivities'],['UserStore','getUser']]){
  const f=fixture();delete f.stores[store][method];assert.equal(f.provider.snapshot().activityAvailability,'unavailable');assert.equal(f.provider.snapshot().activities.length,0);
 }
});
test('blocked removed and self relationships never appear',()=>{
 const f=fixture();f.state.blocked=true;assert.equal(f.provider.snapshot().activities.length,0);assert.throws(()=>f.join());
 f.state.blocked=false;f.state.friend=false;assert.equal(f.provider.snapshot().activities.length,0);
 f.state.friend=true;f.state.friends=['1'];assert.equal(f.provider.snapshot().activities.length,0);
});
test('offline and invisible presence never exposes a cached activity',()=>{
 for(const status of ['offline','invisible']){const f=fixture();f.state.status=status;f.state.voice=false;assert.equal(f.provider.snapshot().activities.length,0);}
});
test('custom status is excluded and provider visible activity order is preserved',()=>{
 const f=fixture();f.stores.PresenceStore.getActivities=()=>[{type:4,name:'private custom'}, {type:2,name:'Music'},{type:0,name:'Game'}];
 assert.equal(f.provider.snapshot().activities[0].kind,'listening');assert.equal(f.provider.snapshot().activities[0].title,'Music');
});
test('hidden and non-discoverable channels expose no voice destination or stream',()=>{
 for(const field of ['visible','discoverable']){const f=fixture();f.state[field]=false;const row=f.provider.snapshot().activities[0];assert.equal(row.channelId,null);assert.equal(row.streamKey,null);assert.equal(row.canJoinVoice,false);assert.throws(()=>f.join());}
});
test('missing discoverable voice or permission APIs keep activity but no voice action',()=>{
 for(const key of ['VoiceStateStore','PermissionStore']){const f=fixture();delete f.stores[key];const row=f.provider.snapshot().activities[0];assert.equal(row.title,'A game');assert.equal(row.channelId,null);assert.throws(()=>f.join());}
});
test('DM stage and AFK channels are never join targets',()=>{
 for(const type of [1,3,13]){const f=fixture();f.stores.ChannelStore.getChannel=id=>({id,type,guild_id:'10'});assert.equal(f.provider.snapshot().activities[0].canJoinVoice,false);assert.throws(()=>f.join());}
 const f=fixture();f.stores.GuildStore.getGuild=id=>({id,afkChannelId:'20'});assert.throws(()=>f.join());
});
test('permission loss after rendering rejects join and watch',()=>{
 const f=fixture();assert.equal(f.provider.snapshot().activities[0].canJoinVoice,true);f.state.connect=false;assert.throws(()=>f.join());f.state.channel='20';assert.throws(()=>f.watch());assert.equal(f.provider.snapshot().activities[0].canWatch,false);
});
test('watch requires same call exact friend exact stream and current capability',()=>{
 const f=fixture();assert.throws(()=>f.watch());f.state.channel='20';assert.equal(f.provider.snapshot().activities[0].canWatch,true);assert.equal(f.watch().id,'20');
 assert.throws(()=>f.watch({key:'guild:10:20:3'}));f.state.stream=false;assert.throws(()=>f.watch());f.state.stream=true;f.state.watchAvailable=false;assert.throws(()=>f.watch());
});
test('changed account call friend voice destination and ended stream reject stale rows',()=>{
 for(const change of [f=>f.state.account='3',f=>f.state.channel='21',f=>f.state.friend=false,f=>f.stores.VoiceStateStore.getDiscoverableVoiceStateForUser=id=>({userId:id,channelId:'21',discoverable:true})]){const f=fixture();f.provider.snapshot();change(f);assert.throws(()=>f.join());assert.deepEqual(f.calls,[]);}
 const f=fixture();f.state.channel='20';f.stores.ApplicationStreamingStore.getAnyDiscoverableStreamForUser=()=>({ownerId:'2',channelId:'20',guildId:'10',state:'ENDED'});assert.throws(()=>f.watch());
});
test('bounded deterministic row keys survive changing names and duplicate friend IDs',()=>{
 const f=fixture();f.state.friends=Array.from({length:65},(_,i)=>String(i+2)).reverse();f.state.friends.push('2');
 const first=f.provider.snapshot();assert.equal(first.activities.length,50);assert.equal(first.activitiesTruncated,true);assert.equal(new Set(first.activities.map(r=>r.userId)).size,50);
 f.stores.UserStore.getUser=id=>({id,globalName:'Renamed'});assert.deepEqual(f.provider.snapshot().activities.map(r=>r.userId),first.activities.map(r=>r.userId));
});
test('malformed source collections and throwing stores fail unavailable without stale data',()=>{
 for(const change of [f=>f.stores.RelationshipStore.getFriendIDs=()=>null,f=>f.stores.PresenceStore.getActivities=()=>null,f=>f.stores.PresenceStore.getStatus=()=>{throw Error('private');}]){const f=fixture();f.provider.snapshot();change(f);const s=f.provider.snapshot();assert.equal(s.activityAvailability,'unavailable');assert.equal(s.activities.length,0);}
});
test('labels controls and timestamps are bounded before transport',()=>{
 const f=fixture();f.stores.PresenceStore.getActivities=()=>[{type:0,name:'x'.repeat(200)+'\n',details:'a\u0085b',timestamps:{start:Number.MAX_SAFE_INTEGER}}];
 const row=f.provider.snapshot().activities[0];assert.equal(row.title.length,120);assert.equal(row.details,'a b');assert.equal(row.startedAt,null);
});
test('UTF-16 transport limits preserve complete emoji and repair malformed provider labels',async()=>{
 for(const [input,expected] of [['x'.repeat(119)+'🎮','x'.repeat(119)],['x'.repeat(118)+'🎮','x'.repeat(118)+'🎮'],['bad\ud800text','bad\ufffdtext'],['bad\udc00text','bad\ufffdtext'],['a\u0085b','a b']]){
  const f=fixture();f.stores.PresenceStore.getActivities=()=>[{type:0,name:input}];
  f.stores.UserStore.getUser=id=>({id,username:input});f.state.channel='20';
  f.stores.VoiceStateStore.getVoiceStatesForChannel=()=>({'2':{userId:'2'}});
  assert.equal(f.provider.snapshot().activities[0].title,expected);
  vm.runInContext(adapter,f.context);const snapshot=(await f.context.WidgetRailDiscordPrototype.invoke({method:'snapshot',owner:'a'.repeat(32)})).value;
  assert.equal(snapshot.participants[0].name,expected);
  assert.equal(JSON.stringify(snapshot).match(/\\u[dD][89a-fA-F][0-9a-fA-F]{2}/),null,'transport must not escape any lone surrogate');
 }
});
test('bounded rows prioritize watch and join before truncation regardless of friend ID order',()=>{
 const f=fixture();f.state.friends=[...Array.from({length:50},(_,i)=>String(i+100)),'998','999'];f.state.channel='20';
 f.stores.VoiceStateStore.getDiscoverableVoiceStateForUser=id=>id==='998'?{userId:id,channelId:'21',discoverable:true}:
  id==='999'?{userId:id,channelId:'20',discoverable:true}:null;
 f.stores.ChannelStore.getChannel=id=>({id,type:2,guild_id:'10',name:'Lobby'});
 const result=f.provider.snapshot();assert.equal(result.activities.length,50);assert.equal(result.activitiesTruncated,true);
 assert.equal(result.activities[0].userId,'999');assert.equal(result.activities[0].canWatch,true);
 assert.equal(result.activities[1].userId,'998');assert.equal(result.activities[1].canJoinVoice,true);
 const keys=result.activities.map(row=>row.userId);f.state.friends.reverse();assert.deepEqual(f.provider.snapshot().activities.map(row=>row.userId),keys);
});
test('account changed during getter produces no mixed-account snapshot',()=>{
 const f=fixture();f.stores.PresenceStore.getActivities=()=>{f.state.account='3';return [{type:0,name:'Old account game'}];};assert.equal(f.provider.snapshot().activities.length,0);
});
test('real adapter activity join uses concrete action only after fresh validation',async()=>{
 const f=fixture();vm.runInContext(adapter,f.context);
 const args={account:'1',channelId:null,userId:'2',target:'20'},owner='a'.repeat(32);
 assert.equal((await f.context.WidgetRailDiscordPrototype.invoke({method:'joinActivity',args,owner})).ok,true);assert.deepEqual(f.calls,['20']);
 f.state.channel=null;f.state.blocked=true;assert.equal((await f.context.WidgetRailDiscordPrototype.invoke({method:'joinActivity',args,owner})).ok,false);assert.deepEqual(f.calls,['20']);
});

test('avatar projection uses only validated fixed Discord CDN paths at static64px',()=>{
 const f=fixture(),hash='a'.repeat(32);f.stores.UserStore.getUser=id=>({id,globalName:'Friend',avatar:hash,getAvatarURL(){throw Error('opaque API');}});
 assert.equal(f.provider.snapshot().activities[0].avatarUrl,`https://cdn.discordapp.com/avatars/2/${hash}.png?size=64`);
 assert.equal(f.provider.avatarUrl('2','a_'+hash),`https://cdn.discordapp.com/avatars/2/a_${hash}.png?size=64`);
 for(const value of [null,undefined,'','https://example.com/avatar',hash+'/../secret',hash+'?token=x','g'.repeat(32),'a'.repeat(33)])assert.equal(f.provider.avatarUrl('2',value),null);
 for(const id of ['../2','2?token=x','',null,'2'.repeat(33)])assert.equal(f.provider.avatarUrl(id,hash),null);
});
test('participant snapshot uses the same avatar whitelist and keeps missing images optional',async()=>{
 const f=fixture();f.stores.VoiceStateStore.getVoiceStatesForChannel=()=>({'2':{userId:'2'}});f.state.channel='20';
 const hash='b'.repeat(32);f.stores.UserStore.getUser=id=>({id,username:'Friend',avatar:hash});vm.runInContext(adapter,f.context);
 let s=await f.context.WidgetRailDiscordPrototype.invoke({method:'snapshot',owner:'a'.repeat(32)});assert.equal(s.value.participants[0].avatarUrl,`https://cdn.discordapp.com/avatars/2/${hash}.png?size=64`);
 f.stores.UserStore.getUser=id=>({id,username:'Friend',avatar:'https://example.com/private'});s=await f.context.WidgetRailDiscordPrototype.invoke({method:'snapshot',owner:'a'.repeat(32)});assert.equal(s.value.participants[0].avatarUrl,null);assert.equal(s.ok,true);
});
