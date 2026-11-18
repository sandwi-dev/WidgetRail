import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {webcrypto} from 'node:crypto';
import {runInNewContext} from 'node:vm';
import test from 'node:test';

const source=readFileSync(new URL('./messaging-provider.js',import.meta.url),'utf8');
const plain=value=>JSON.parse(JSON.stringify(value));
const error=code=>value=>value.adapterCode===code;
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const message=(id,extra={})=>({id:String(id),channel_id:'10',author:{id:'2',username:'Bob'},content:'Hello',
    timestamp:'2026-11-16T12:00:00.000Z',edited_timestamp:null,attachments:[],...extra});
function fixture(){
    const f={account:'1',now:1000000,invalidations:0,posts:[],gets:[],messages:[],maximum:1000000,blocked:new Set(),friends:['2','3','4'],
        unread:new Set(['10']),readListeners:new Set()};
    const users=new Map(['1','2','3','4'].map((id,i)=>[id,{id,username:['Me','Bob','Carol','Dave'][i]}]));
    f.channels=new Map([
        ['10',{id:'10',type:1,recipients:['2']}],
        ['20',{id:'20',type:3,name:'Group',recipients:['2','3']}],
        ['30',{id:'30',type:0,guild_id:'100',recipients:[]}]
    ]);
    f.listeners=new Map();
    f.dispatcher={subscribe(name,fn){let callbacks=f.listeners.get(name);if(!callbacks)f.listeners.set(name,callbacks=new Set());callbacks.add(fn);},
        unsubscribe(name,fn){f.listeners.get(name)?.delete(fn);}};
    f.emit=(name,value)=>{for(const fn of f.listeners.get(name)??[])fn(value);};
    f.stores={ChannelStore:{getChannel:id=>f.channels.get(id),getSortedPrivateChannels:()=>[...f.channels.values()],
        getDMFromUserId:id=>[...f.channels.values()].find(value=>value.type===1&&value.recipients[0]===id)?.id},
        UserStore:{getUser:id=>users.get(id)},RelationshipStore:{getFriendIDs:()=>f.friends,isFriend:id=>f.friends.includes(id),
            isBlockedOrIgnored:id=>f.blocked.has(id)},ReadStateStore:{hasUnread:id=>f.unread.has(id),lastMessageId:()=>null,
            addChangeListener:fn=>f.readListeners.add(fn),removeChangeListener:fn=>f.readListeners.delete(fn)}};
    f.api={get:async args=>{f.gets.push(plain(args));if(f.get)return f.get(args);
        let items=f.messages.filter(item=>(!args.query.before||BigInt(item.id)<BigInt(args.query.before))&&
            (!args.query.after||BigInt(item.id)>BigInt(args.query.after)));
        items.sort((a,b)=>BigInt(a.id)<BigInt(b.id)?-1:1);
        items=args.query.after?items.slice(0,args.query.limit):items.slice(-args.query.limit);
        return {body:items.toReversed(),status:200};},
        post:async args=>{f.posts.push(plain(args));if(f.post)return f.post(args);
            if(args.url==='/users/@me/channels')return {body:{id:'99',type:1,recipients:[{id:args.body.recipient_id}]}};
            if(args.url.endsWith('/attachments'))return {body:{attachments:args.body.files.map(file=>({id:file.id,
                upload_url:'https://discord-attachments-uploads-prd.storage.googleapis.com/pending/'+file.id+'?signature=test',
                upload_filename:'pending/'+file.filename}))}};
            return {body:message('900',{channel_id:args.url.split('/')[2],author:{id:f.account,username:'Me'},
                content:args.body.content,nonce:args.body.nonce})};},del(){},put(){},patch(){}};
    const context={URL,crypto:webcrypto,Date:class extends Date{static now(){return f.now;}}};
    runInNewContext(source,context);
    f.provider=context.createWidgetRailDiscordMessaging({store:name=>f.stores[name],
        props:(...names)=>{
            if(names[0]==='getUserMaxFileSize')return {getUserMaxFileSize:()=>f.maximum};
            const predicate=typeof names.at(-1)==='function'?names.at(-1):()=>true;
            return [f.genericApi,f.api].find(candidate=>candidate&&predicate(candidate));
        },
        me:()=>f.account?{id:f.account}:null,dispatcher:()=>f.dispatcher,invalidate:()=>++f.invalidations});
    return f;
}

test('live changes replay bounded full messages, partial edits and deletions without REST',async()=>{
    const f=fixture();f.provider.observe(historyArgs);const initial=f.provider.state();
    f.emit('MESSAGE_CREATE',{message:message('101')});
    f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:'Edited',attachments:[],edited_timestamp:'2026-11-17T12:00:00.000Z'}});
    f.emit('MESSAGE_DELETE',{channelId:'10',id:'101'});
    const query={...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision};
    const result=f.provider.readChanges(query);
    assert.deepEqual(plain(result.items.map(item=>item.kind)),['upsert','upsert','remove']);
    assert.equal(result.items[1].message.authorName,'Bob');assert.equal(result.items[1].message.content,'Edited');
    assert.equal(result.resetRequired,false);assert.deepEqual(plain(f.provider.readChanges(query)),plain(result));
    assert.equal(f.gets.length,0);assert.equal(f.posts.length,0);
});

test('message mentions are projected and partial edits preserve or explicitly clear them without REST',async()=>{
    const f=fixture();f.provider.observe(historyArgs);
    f.messages=[message('101',{mentions:[{id:'3',global_name:'Sanky'},{id:'3',username:'duplicate'}]})];
    const page=await f.provider.history(historyArgs);assert.deepEqual(plain(page.items[0].mentions),[{id:'3',name:'Sanky'}]);
    let initial=f.provider.state();
    f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:'Updated <@3>'}});
    let result=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision});
    assert.equal(result.items[0].message.mentions[0].name,'Sanky');
    initial=f.provider.state();f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',mentions:[]}});
    result=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision});
    assert.equal(result.items[0].message.mentions.length,0);assert.equal(f.gets.length,1);assert.equal(f.posts.length,0);
});

test('around-message recovery uses one bounded mutually-exclusive read and preserves account fencing',async()=>{
    const f=fixture();f.get=async args=>{
        assert.deepEqual(plain(args.query),{limit:40,around:'150'});
        return {body:Array.from({length:40},(_,index)=>message(String(130+index))).toReversed(),status:200};
    };
    const page=await f.provider.history({...historyArgs,around:'150',limit:40});
    assert.equal(page.around,'150');assert.equal(page.items.length,40);assert.ok(page.items.some(item=>item.id==='150'));
    await assert.rejects(f.provider.history({...historyArgs,around:'150',before:'170'}),error('invalid_page'));
    assert.equal(f.gets.length,1);assert.equal(f.posts.length,0);
    const gate=deferred();f.get=()=>gate.promise;
    const pending=f.provider.history({...historyArgs,around:'150',limit:40});f.account='2';gate.resolve({body:[message('150')],status:200});
    await assert.rejects(pending,error('account_changed'));
});

test('rich message metadata is bounded, sanitizes media origins and preserves partial edit semantics',async()=>{
    const f=fixture();f.provider.observe(historyArgs);
    f.messages=[message('101',{type:3,message_reference:{message_id:'100'},referenced_message:message('100',{content:'Quote'}),
        embeds:[{title:'Link',description:'Details',url:'https://example.test',image:{url:'https://tracker.example/pixel'}},
            {title:'Image',image:{proxy_url:'https://media.discordapp.net/image.png'}},{title:'Third'}],
        reactions:[{emoji:{id:'400',name:'wave'},count:3,me:true}],
        sticker_items:[{id:'500',name:'Animated',format_type:3}],
        poll:{question:{text:'Question'},answers:[{answer_id:1,poll_media:{text:'Yes'}}],results:{answer_counts:[{id:1,count:4}],is_finalized:false}}})];
    const row=(await f.provider.history(historyArgs)).items[0];
    assert.equal(row.reply.content,'Quote');assert.equal(row.systemText,'Voice call');
    assert.equal(row.embeds.length,2);assert.equal(row.embeds[0].imageUrl,null);assert.equal(row.embeds[1].imageUrl,'https://media.discordapp.net/image.png');
    assert.equal(row.stickers[0].imageUrl,null);assert.equal(row.poll.answers[0],'Yes · 4');assert.equal(row.reactions[0].isOwn,true);
    let initial=f.provider.state();f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:'Edited'}});
    let changed=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision}).items[0].message;
    assert.equal(changed.reply.content,'Quote');assert.equal(changed.reactions[0].count,3);
    initial=f.provider.state();f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',reactions:[],embeds:[],poll:null,referenced_message:null}});
    changed=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision}).items[0].message;
    assert.equal(changed.reactions.length,0);assert.equal(changed.embeds.length,0);assert.equal(changed.poll,null);assert.equal(changed.reply.unavailable,true);
    assert.equal(f.gets.length,1);
});

test('notification tags resolve only exact observed private messages within the current account',()=>{
    const f=fixture();f.provider.state();
    assert.equal(f.provider.notificationTarget({account:'1',tag:'101'}),null);
    f.emit('MESSAGE_CREATE',{message:message('101')});
    const target=f.provider.notificationTarget({account:'1',tag:'101'});
    assert.equal(target.account,'1');assert.equal(target.messageId,'101');assert.equal(target.conversation.channelId,'10');
    assert.equal(f.provider.state().observedChannelId,null);assert.equal(f.invalidations,0);
    f.emit('MESSAGE_CREATE',{message:message('102',{channel_id:'30'})});
    assert.equal(f.provider.notificationTarget({account:'1',tag:'102'}),null);
    assert.equal(f.provider.notificationTarget({account:'1',tag:'Friend sent a message'}),null);
    for(let index=0;index<260;index++)f.emit('MESSAGE_CREATE',{message:message(String(1000+index))});
    assert.equal(f.provider.notificationTarget({account:'1',tag:'101'}),null);
    assert.equal(f.provider.notificationTarget({account:'1',tag:'1259'}).messageId,'1259');
    f.account='2';f.provider.state();assert.equal(f.provider.notificationTarget({account:'2',tag:'1259'}),null);
    f.account='1';f.provider.state();assert.equal(f.provider.notificationTarget({account:'1',tag:'1259'}),null);
    f.provider.dispose();assert.equal(f.listeners.get('MESSAGE_CREATE').size,0);
    assert.equal(f.gets.length,0);assert.equal(f.posts.length,0);
});

test('reaction and poll events use settled local stores with no HTTP or duplicate increments',async()=>{
    const f=fixture();f.provider.observe(historyArgs);f.messages=[message('101')];await f.provider.history(historyArgs);
    let stored={reactions:[{emoji:{name:'👍'},count:2,me:false}]};
    f.stores.MessageStore={getMessage:()=>stored};let initial=f.provider.state();
    f.emit('MESSAGE_REACTION_ADD',{channelId:'10',messageId:'101'});f.emit('MESSAGE_REACTION_ADD',{channelId:'10',messageId:'101'});await Promise.resolve();
    let changes=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision});
    assert.equal(changes.items.length,1);assert.equal(changes.items[0].message.reactions[0].count,2);
    initial=f.provider.state();stored={reactions:{unknownStoreShape:true}};
    f.emit('MESSAGE_REACTION_ADD',{channelId:'10',messageId:'101'});await Promise.resolve();
    assert.equal(f.provider.state().messageRevision,initial.messageRevision,'Unknown store shape must not erase known reaction counts');
    initial=f.provider.state();stored={poll:{question:{text:'Choose'},answers:[{answer_id:1,poll_media:{text:'Yes'}}],results:{answer_counts:[{id:1,count:2}]}}};
    f.emit('MESSAGE_POLL_VOTE_ADD',{channelId:'10',messageId:'101'});await Promise.resolve();
    changes=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision});assert.equal(changes.items[0].message.poll.answers[0],'Yes · 2');
    initial=f.provider.state();f.emit('MESSAGE_REACTION_REMOVE',{channelId:'10',messageId:'101'});f.account='2';await Promise.resolve();
    assert.equal(f.gets.length,1);assert.equal(f.posts.length,0);assert.equal(f.provider.state().messageRevision,initial.messageRevision);
});

test('watch epochs reject another visit and overflow requests one bounded recovery',()=>{
    const f=fixture();f.provider.observe(historyArgs);const initial=f.provider.state();
    for(let index=1;index<=140;index++)f.emit('MESSAGE_CREATE',{message:message(String(1000+index))});
    const query={...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision};
    const result=f.provider.readChanges(query);assert.equal(result.resetRequired,true);assert.equal(result.items.length,50);assert.equal(result.hasMore,true);
    f.provider.observe({...historyArgs,channelId:'20'});f.provider.observe(historyArgs);
    assert.throws(()=>f.provider.readChanges(query),error('message_watch_changed'));
    f.account='9';assert.throws(()=>f.provider.readChanges(query),error('account_changed'));
    assert.equal(f.gets.length,0);
});

test('a delayed history response cannot resurrect a deletion or overwrite a live edit',async()=>{
    const f=fixture();f.provider.observe(historyArgs);const gate=deferred();
    f.get=()=>gate.promise;const pending=f.provider.history(historyArgs);
    f.emit('MESSAGE_CREATE',{message:message('101')});
    f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:'Current'}});
    f.emit('MESSAGE_DELETE',{channelId:'10',id:'102'});
    gate.resolve({body:[message('101'),message('102')],status:200});
    const result=await pending;assert.deepEqual(plain(result.items.map(item=>item.id)),['101']);
    assert.equal(result.items[0].content,'Current');assert.equal(result.nextAfter,'102');assert.equal(f.gets.length,1);
});

test('partial edits before the first history base arrive merge without an extra GET',async()=>{
    const f=fixture();f.provider.observe(historyArgs);const gate=deferred();f.get=()=>gate.promise;
    const pending=f.provider.history(historyArgs);
    f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:'Edited before base'}});
    f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',edited_timestamp:'2026-11-17T12:00:00.000Z'}});
    f.emit('MESSAGE_DELETE',{channelId:'10',id:'102'});
    f.emit('MESSAGE_UPDATE',{message:{id:'102',channel_id:'10',content:'Late edit of deleted message'}});
    gate.resolve({body:[message('101'),message('102')],status:200});
    const result=await pending;assert.equal(result.items.length,1);assert.equal(result.items[0].content,'Edited before base');
    assert.equal(result.items[0].editedTimestamp,Date.parse('2026-11-17T12:00:00.000Z'));assert.equal(f.gets.length,1);
});

test('partial edits reuse bounded history cache and unrelated events do not fetch missing messages',async()=>{
    const f=fixture();f.provider.observe(historyArgs);f.messages=[message('101')];await f.provider.history(historyArgs);
    const initial=f.provider.state();f.emit('MESSAGE_UPDATE',{message:{id:'101',channel_id:'10',content:''}});
    f.emit('MESSAGE_UPDATE',{message:{id:'999',channel_id:'10',content:'Outside retained history'}});
    f.emit('MESSAGE_CREATE',{message:message('200',{channel_id:'20'})});
    const result=f.provider.readChanges({...historyArgs,watchEpoch:initial.watchEpoch,afterRevision:initial.messageRevision});
    assert.equal(result.items.length,1);assert.equal(result.items[0].message.content,'');assert.equal(result.resetRequired,false);
    assert.equal(f.gets.length,1);assert.equal(f.posts.length,0);
});

test('gap recovery stays inside the retained interval and does not mistake a large tail for its history',async()=>{
    const f=fixture();f.provider.observe(historyArgs);
    f.messages=Array.from({length:200},(_,index)=>message(String(100+index))).filter(value=>value.id!=='110');
    const result=await f.provider.recoverMessages({...historyArgs,watchEpoch:f.provider.state().watchEpoch,ids:['100','110','139']});
    assert.equal(f.gets.length,1);assert.equal(f.gets[0].query.before,'140');assert.equal(f.gets[0].query.limit,50);
    assert.equal(result.complete,true);assert.equal(result.after,'139');
    assert.deepEqual(plain(result.items.map(item=>[item.id,item.kind])),[['100','upsert'],['110','remove'],['139','upsert']]);
});

test('partial and empty recovery responses have explicit bounded deletion authority',async()=>{
    const f=fixture();f.provider.observe(historyArgs);
    f.messages=Array.from({length:100},(_,index)=>message(String(100+index)));
    const query={...historyArgs,watchEpoch:f.provider.state().watchEpoch,ids:['100','199']};
    const partial=await f.provider.recoverMessages(query);
    assert.equal(partial.complete,false);assert.deepEqual(plain(partial.items.map(item=>item.id)),['199']);
    f.messages=[];const empty=await f.provider.recoverMessages(query);
    assert.equal(empty.complete,true);assert.deepEqual(plain(empty.items.map(item=>item.kind)),['remove','remove']);
});

test('a delayed recovery is rejected after a watch replacement and never sends a message',async()=>{
    const f=fixture();f.provider.observe(historyArgs);const gate=deferred();f.get=()=>gate.promise;
    const pending=f.provider.recoverMessages({...historyArgs,watchEpoch:f.provider.state().watchEpoch,ids:['100']});
    f.provider.observe({...historyArgs,channelId:'20'});gate.resolve({body:[message('100')],status:200});
    await assert.rejects(pending,error('message_watch_changed'));assert.equal(f.posts.length,0);
});
const historyArgs={account:'1',channelId:'10'};
test('conversation unread target uses the authoritative oldest-unread store without reads or acknowledgements',()=>{
    const f=fixture();
    const read=()=>f.provider.conversations({account:'1'}).items.find(item=>item.channelId==='10');
    assert.equal(read().oldestUnreadMessageId,null);
    f.stores.ReadStateStore.getOldestUnreadMessageId=()=> '101';
    assert.equal(read().oldestUnreadMessageId,'101');
    f.stores.ReadStateStore.getOldestUnreadMessageId=()=> 'invalid';
    assert.equal(read().oldestUnreadMessageId,null);
    f.stores.ReadStateStore.getOldestUnreadMessageId=()=> '101';f.unread.clear();
    assert.equal(read().oldestUnreadMessageId,null);
    assert.equal(f.gets.length,0);assert.equal(f.posts.length,0);
});
const sendArgs={account:'1',channelId:'10',nonce:'123',content:'Explicit message'};
const fileArgs={account:'1',channelId:'10',files:[{id:'0',fileName:'capture.png',byteLength:50}]};
const sendFiles=(batch,extra={})=>({...sendArgs,content:'',attachmentBatchId:batch.batchId,
    attachments:batch.files.map(({id,fileName,uploadedFileName})=>({id,fileName,uploadedFileName})),...extra});

test('capabilities fail closed for signed-out, missing modules and unknown relationship state',()=>{
    const f=fixture();assert.equal(f.provider.state().availability,'ready');
    f.maximum=NaN;assert.equal(f.provider.state().maximumAttachmentBytes,null);
    f.stores.RelationshipStore=null;assert.equal(f.provider.state().canSend,false);
    f.api=null;assert.equal(f.provider.state().availability,'unavailable');
    f.account=null;assert.equal(f.provider.state().availability,'disconnected');
});
test('REST discovery rejects positional HTTP helpers even when their verb names match',async()=>{
    const f=fixture();let incorrectCalls=0;
    f.genericApi={get(url,data,callback){incorrectCalls++;},post(url,data,callback){incorrectCalls++;},put(){},del(){}};
    await f.provider.history(historyArgs);assert.equal(incorrectCalls,0);assert.equal(f.gets.length,1);
    f.api=null;assert.equal(f.provider.state().canRead,false);
    await assert.rejects(f.provider.history(historyArgs),error('messaging_unavailable'));assert.equal(incorrectCalls,0);
});
test('tracking delete-only facade is unavailable without the direct authenticated verb contract',async()=>{
    const f=fixture();delete f.api.del;f.api.delete=()=>{};
    assert.equal(f.provider.state().canRead,false);await assert.rejects(f.provider.history(historyArgs),error('messaging_unavailable'));assert.equal(f.gets.length,0);
});
test('authenticated object bag may expose two-argument bound functions',async()=>{
    const f=fixture(),get=f.api.get,post=f.api.post;
    f.api.get=function request(method,options,context){return get(options);}.bind(null,'GET');
    f.api.post=function request(method,options,context){return post(options);}.bind(null,'POST');
    assert.equal(f.api.get.length,2);assert.equal(f.provider.state().canRead,true);
    await f.provider.history(historyArgs);await f.provider.sendMessage(sendArgs);
    assert.equal(f.gets.length,1);assert.equal(f.posts.length,1);
});
test('catalog is bounded, deduplicates friends, filters and rejects stale page versions',()=>{
    const f=fixture(), first=f.provider.conversations({account:'1',limit:2});
    assert.deepEqual(plain(first.items.map(value=>value.id)),['10','20']);assert.equal(first.totalCount,4);assert.equal(first.hasMore,true);
    const rest=f.provider.conversations({account:'1',offset:2,version:first.version});
    assert.deepEqual(plain(rest.items.map(value=>value.id)),['friend.3','friend.4']);
    assert.equal(f.provider.conversations({account:'1',query:'boB'}).items[0].name,'Bob');assert.equal(f.gets.length,0);
    f.channels.delete('10');assert.throws(()=>f.provider.conversations({account:'1',offset:2,version:first.version}),error('conversation_catalog_changed'));
    assert.throws(()=>f.provider.conversations({account:'1',limit:51}),error('invalid_page'));
    for(let i=100;i<1200;i++)f.channels.set(String(i),{id:String(i),type:1,recipients:['2']});
    const bounded=f.provider.conversations({account:'1'});assert.ok(bounded.totalCount<=1000);assert.equal(bounded.truncated,true);
});
test('history preserves snowflakes and supports adjacent backward/forward reversals',async()=>{
    const f=fixture(), base=9007199254740993000n;
    f.messages=Array.from({length:9},(_,i)=>message(base+BigInt(i)));
    const latest=await f.provider.history({...historyArgs,limit:3});
    const older=await f.provider.history({...historyArgs,limit:3,before:latest.nextBefore});
    const restored=await f.provider.history({...historyArgs,limit:3,after:older.nextAfter});
    assert.deepEqual(plain(restored.items.map(x=>x.id)),plain(latest.items.map(x=>x.id)));
    assert.deepEqual(plain(older.items.map(x=>x.id)),[3,4,5].map(i=>String(base+BigInt(i))));
    assert.equal(latest.items[0].timestamp,1794830400000);assert.equal(f.gets[2].query.after,older.nextAfter);
    await assert.rejects(f.provider.history({...historyArgs,before:'12',after:'10'}),error('invalid_page'));
});
test('history preserves premium text and bounds/sanitizes attachment metadata',async()=>{
    const f=fixture();f.messages=[message('100',{content:'x'.repeat(3998)+'😀tail',attachments:[{id:'555',filename:'clip.mp4',size:12,
        url:'https://cdn.discordapp.com/attachments/10/555/clip.mp4',width:1920,height:1080,content_type:'video/mp4'},
        {id:'556',filename:'remote.png',size:2,url:'https://example.com/secret'}]})];
    const row=(await f.provider.history(historyArgs)).items[0];
    assert.equal(row.content.length,4000);assert.ok(row.content.endsWith('😀'));assert.equal(row.contentTruncated,true);
    assert.equal(row.attachments[0].width,1920);assert.equal(row.attachments[1].url,null);
});
test('history rejects wrong channels, duplicate IDs and cursor boundary violations',async()=>{
    const f=fixture();
    for(const values of [[message('100',{channel_id:'20'})],[message('100'),message('100')]]){
        f.get=async()=>({body:values});await assert.rejects(f.provider.history(historyArgs),e=>['invalid_message','invalid_message_response'].includes(e.adapterCode));
    }
    f.get=async()=>({body:[message('100')]});
    await assert.rejects(f.provider.history({...historyArgs,before:'100'}),error('invalid_message_response'));
    await assert.rejects(f.provider.history({...historyArgs,after:'100'}),error('invalid_message_response'));
});
test('late history cannot publish across account change, retirement, or removed channel',async()=>{
    for(const action of [f=>f.account='9',f=>f.provider.dispose(),f=>f.channels.delete('10')]){
        const f=fixture(), wait=deferred();f.get=()=>wait.promise;
        const pending=f.provider.history(historyArgs);action(f);wait.resolve({body:[message('100')]});
        await assert.rejects(pending,e=>['account_changed','messaging_retired','conversation_unavailable'].includes(e.adapterCode));
    }
});
test('DM opening uses known friend identity and bounded receipt until gateway publication',async()=>{
    const f=fixture();assert.equal((await f.provider.openDm({account:'1',userId:'2'})).channelId,'10');assert.equal(f.posts.length,0);
    await assert.rejects(f.provider.openDm({account:'1',userId:'50'}),error('recipient_unavailable'));
    const opened=await f.provider.openDm({account:'1',userId:'4'});assert.equal(opened.channelId,'99');
    await f.provider.history({account:'1',channelId:'99'});assert.equal(f.channels.has('99'),false);
    f.now+=30001;await assert.rejects(f.provider.history({account:'1',channelId:'99'}),error('conversation_unavailable'));
});
test('late DM receipt is rejected after account change or a newly blocked recipient',async()=>{
    for(const changed of [f=>f.account='9',f=>f.blocked.add('4')]){
        const f=fixture(), wait=deferred();f.post=()=>wait.promise;const pending=f.provider.openDm({account:'1',userId:'4'});
        changed(f);wait.resolve({body:{id:'99',type:1,recipients:[{id:'4'}]}});
        await assert.rejects(pending,e=>['account_changed','recipient_unavailable'].includes(e.adapterCode));
    }
});
test('send is explicit, bounded, disables automatic mentions and retries, and deduplicates a pending nonce',async()=>{
    const f=fixture(), wait=deferred();f.post=()=>wait.promise;
    const pending=f.provider.sendMessage(sendArgs);
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'pending');assert.equal(f.posts.length,1);
    await assert.rejects(f.provider.sendMessage({...sendArgs,content:'Other'}),error('message_nonce_reused'));
    assert.deepEqual(f.posts[0].body.allowed_mentions,{parse:[]});assert.equal(f.posts[0].retries,0);assert.equal(f.posts[0].body.enforce_nonce,true);
    wait.resolve({body:message('1234',{author:{id:'1'},nonce:'123'})});assert.equal((await pending).status,'sent');
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'sent');assert.equal(f.posts.length,1);
    await assert.rejects(f.provider.sendMessage({...sendArgs,nonce:'124',content:'x'.repeat(2001)}),error('invalid_message'));
});
test('guild and blocked recipients cannot be sent messages',async()=>{
    const f=fixture();await assert.rejects(f.provider.sendMessage({...sendArgs,channelId:'30'}),error('conversation_unavailable'));
    f.blocked.add('2');await assert.rejects(f.provider.sendMessage(sendArgs),error('recipient_unavailable'));assert.equal(f.posts.length,0);
});
test('ambiguous sends remain unknown and reconcile by nonce without replaying the mutation',async()=>{
    const f=fixture();f.post=async()=>{throw new Error('timeout with provider secrets');};
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'unknown');
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'unknown');assert.equal(f.posts.length,1);
    f.messages=[message('1234',{author:{id:'1'},nonce:'123'})];
    const settled=await f.provider.reconcileSend(sendArgs);assert.equal(settled.status,'sent');assert.equal(f.posts.length,1);
    assert.equal(JSON.stringify(settled).includes('secrets'),false);
});
test('definite rejection differs from ambiguous acknowledgment and ledger stays bounded',async()=>{
    const f=fixture();f.post=async()=>{throw {status:403};};
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'failed');
    f.post=async()=>({body:message('99',{author:{id:'2'},nonce:'124'})});
    assert.equal((await f.provider.sendMessage({...sendArgs,nonce:'124'})).status,'unknown');
    f.post=async()=>{throw new Error('network');};
    for(let i=0;i<127;i++)await f.provider.sendMessage({...sendArgs,nonce:String(1000+i)});
    await assert.rejects(f.provider.sendMessage({...sendArgs,nonce:'9000'}),error('message_send_capacity'));
});
test('late send completion is recorded but cannot publish into the next account',async()=>{
    const f=fixture(), wait=deferred();f.post=()=>wait.promise;
    const args={...sendArgs}, pending=f.provider.sendMessage(args);args.account='9';args.channelId='20';f.account='9';
    wait.resolve({body:message('1234',{author:{id:'1'},nonce:'123'})});await assert.rejects(pending,error('account_changed'));
    f.account='1';assert.equal((await f.provider.reconcileSend(sendArgs)).status,'sent');assert.equal(f.gets.length,0);
});
test('reconciliation can confirm an unresolved POST and a later network failure cannot erase confirmation',async()=>{
    const f=fixture(), wait=deferred();f.post=()=>wait.promise;const pending=f.provider.sendMessage(sendArgs);
    f.messages=[message('1234',{author:{id:'1'},nonce:'123'})];
    assert.equal((await f.provider.reconcileSend(sendArgs)).status,'sent');
    wait.reject(new Error('response lost'));assert.equal((await pending).status,'sent');assert.equal(f.posts.length,1);
});
test('a send acknowledgment without the matching nonce remains unknown',async()=>{
    const f=fixture();f.post=async()=>({body:message('1234',{author:{id:'1'}})});
    assert.equal((await f.provider.sendMessage(sendArgs)).status,'unknown');assert.equal(f.posts.length,1);
});
test('late empty reconciliation cannot erase a confirmed POST result',async()=>{
    const f=fixture(), post=deferred(), get=deferred();f.post=()=>post.promise;f.get=()=>get.promise;
    const pending=f.provider.sendMessage(sendArgs), reconciliation=f.provider.reconcileSend(sendArgs);
    post.resolve({body:message('1234',{author:{id:'1'},nonce:'123'})});assert.equal((await pending).status,'sent');
    get.resolve({body:[]});assert.equal((await reconciliation).status,'sent');
});
test('observation invalidates only the current conversation and releases handlers',()=>{
    const f=fixture();f.provider.observe(historyArgs);const revision=f.provider.state().messageRevision;
    f.emit('MESSAGE_CREATE',{message:{channel_id:'20'}});assert.equal(f.invalidations,1);assert.equal(f.provider.state().messageRevision,revision);
    f.emit('MESSAGE_CREATE',{message:{channel_id:'10'}});f.emit('MESSAGE_DELETE',{channelId:'10'});
    assert.equal(f.provider.state().messageRevision,revision+2);assert.equal(f.invalidations,3);
    f.provider.observe(historyArgs);assert.equal(f.listeners.get('MESSAGE_CREATE').size,2);
    f.account='9';f.emit('MESSAGE_CREATE',{message:{channel_id:'10'}});assert.equal(f.invalidations,3);
    assert.equal(f.provider.state().observedChannelId,null);assert.equal(f.listeners.get('MESSAGE_CREATE').size,1);
    f.account='1';f.provider.observe(historyArgs);f.provider.dispose();assert.equal(f.listeners.get('MESSAGE_CREATE').size,0);
});
test('catalog-only observation filters guild changes, observes private read state, and releases explicitly',()=>{
    const f=fixture();f.provider.observe({account:'1',channelId:null});
    const first=f.provider.state();assert.equal(f.readListeners.size,1);
    f.emit('MESSAGE_CREATE',{message:{channel_id:'30'}});f.emit('CHANNEL_UPDATE',{channel:f.channels.get('30')});
    assert.equal(f.provider.state().catalogRevision,first.catalogRevision);assert.equal(f.invalidations,0);
    f.emit('CHANNEL_CREATE',{channel:{id:'50',type:1,recipients:['4']}});
    f.emit('CHANNEL_DELETE',{channelId:'50'});
    assert.equal(f.provider.state().catalogRevision,first.catalogRevision+2);
    // Store notifications without a relevant private-read-state change are ignored.
    for(const callback of f.readListeners)callback();
    const revision=f.provider.state().catalogRevision;f.unread.delete('10');for(const callback of f.readListeners)callback();
    assert.equal(f.provider.state().catalogRevision,revision+1);assert.equal(f.provider.state().messageRevision,first.messageRevision);
    f.provider.observe({account:'1',active:false});assert.equal(f.readListeners.size,0);
    assert.equal(f.listeners.get('MESSAGE_CREATE').size,1);const count=f.invalidations;
    f.emit('MESSAGE_CREATE',{message:{channel_id:'10'}});assert.equal(f.invalidations,count);
});
test('partially failed observer attachment detaches all subscriptions',()=>{
    const f=fixture(), subscribe=f.dispatcher.subscribe;
    f.dispatcher.subscribe=(name,callback)=>{if(name==='CHANNEL_UPDATE')throw new Error('unavailable');subscribe(name,callback);};
    assert.throws(()=>f.provider.observe(historyArgs),error('message_updates_unavailable'));
    assert.equal([...f.listeners.values()].reduce((sum,set)=>sum+set.size,0),0);assert.equal(f.readListeners.size,0);
});
test('initial observation, history loading, read acknowledgment and channel metadata do not invent message mutations',()=>{
    const f=fixture();assert.equal(f.provider.state().messageRevision,0);
    f.provider.observe({account:'1',channelId:null});f.provider.observe(historyArgs);
    assert.equal(f.provider.state().messageRevision,0);assert.equal(f.provider.state().observedSinceRevision,0);
    f.emit('LOAD_MESSAGES_SUCCESS',{channelId:'10',messages:[message('100')]});
    f.emit('CHANNEL_UPDATE',{channel:f.channels.get('10')});
    f.unread.delete('10');for(const changed of f.readListeners)changed();
    assert.equal(f.provider.state().messageRevision,0);
    f.provider.observe({account:'1',active:false});assert.equal(f.provider.state().messageRevision,0);
});
test('observation watermark distinguishes another channel old mutation from genuine new watched-channel events',()=>{
    const f=fixture();f.provider.observe(historyArgs);f.emit('MESSAGE_CREATE',{message:message('100')});
    const switched=f.provider.observe({account:'1',channelId:'20'});
    assert.equal(switched.messageRevision,1);assert.equal(switched.observedSinceRevision,1);
    f.emit('MESSAGE_CREATE',{message:message('101',{channel_id:'10'})});assert.equal(f.provider.state().messageRevision,1);
    f.emit('MESSAGE_CREATE',{message:message('102',{channel_id:'20'})});
    assert.equal(f.provider.state().messageRevision,2);assert.equal(f.provider.state().observedSinceRevision,1);
    f.provider.observe({account:'1',channelId:'20'});assert.equal(f.provider.state().observedSinceRevision,1);
    for(const name of ['MESSAGE_UPDATE','MESSAGE_DELETE','MESSAGE_DELETE_BULK'])f.emit(name,{channelId:'20'});
    assert.equal(f.provider.state().messageRevision,5);
});
test('attachment preparation carries metadata only and receipts bind the empty-text send',async()=>{
    const f=fixture(), batch=await f.provider.prepareAttachments(fileArgs);
    assert.match(batch.batchId,/^[a-f0-9]{32}$/);assert.deepEqual(f.posts[0].body,{files:[{id:'0',filename:'capture.png',file_size:50}]});
    const result=await f.provider.sendMessage(sendFiles(batch));assert.equal(result.status,'sent');
    assert.deepEqual(f.posts[1].body.attachments,[{id:'0',filename:'capture.png',uploaded_filename:'pending/capture.png'}]);
    await assert.rejects(f.provider.sendMessage(sendFiles(batch,{nonce:'124'})),error('attachment_batch_unavailable'));
});
test('attachment receipts reject changed identity, filenames, origins and expiration',async()=>{
    const f=fixture(), batch=await f.provider.prepareAttachments(fileArgs), args=sendFiles(batch);
    await assert.rejects(f.provider.sendMessage({...args,channelId:'20'}),error('attachment_batch_unavailable'));
    await assert.rejects(f.provider.sendMessage({...args,attachments:[{...args.attachments[0],fileName:'other.png'}]}),error('invalid_attachments'));
    batch.files[0].fileName='mutated.png';await assert.rejects(f.provider.sendMessage(sendFiles(batch)),error('invalid_attachments'));
    f.now+=900001;await assert.rejects(f.provider.sendMessage(args),error('attachment_batch_unavailable'));
    f.post=async()=>({body:{attachments:[{id:'0',upload_url:'https://attacker.test/file',upload_filename:'file'}]}});
    await assert.rejects(f.provider.prepareAttachments(fileArgs),error('invalid_upload_response'));
});
test('concurrent preparations reserve bounded capacity and release failed reservations',async()=>{
    const f=fixture(), wait=deferred();f.post=()=>wait.promise;
    const jobs=Array.from({length:8},()=>f.provider.prepareAttachments(fileArgs));
    await assert.rejects(f.provider.prepareAttachments(fileArgs),error('attachment_capacity'));assert.equal(f.posts.length,8);
    wait.reject({status:429});await Promise.all(jobs.map(job=>assert.rejects(job,error('rate_limited'))));
    f.post=async()=>({body:{attachments:[]}});await assert.rejects(f.provider.prepareAttachments(fileArgs),error('invalid_upload_response'));
    assert.equal(f.posts.length,9);
});
test('preparation cannot publish after account change and inputs cannot mutate while pending',async()=>{
    const f=fixture(), wait=deferred();f.post=()=>wait.promise;
    const args=structuredClone(fileArgs), pending=f.provider.prepareAttachments(args);
    args.files[0].fileName='other.png';args.account='9';
    wait.resolve({body:{attachments:[{id:'0',upload_url:'https://discord-attachments-uploads-prd.storage.googleapis.com/file',upload_filename:'pending/capture.png'}]}});
    const batch=await pending;assert.equal(batch.files[0].fileName,'capture.png');assert.equal(batch.account,'1');
    const again=deferred();f.post=()=>again.promise;const late=f.provider.prepareAttachments(fileArgs);f.account='9';
    again.resolve({body:{attachments:[]}});await assert.rejects(late,error('account_changed'));
});
test('attachment validation enforces advertised limits and ambiguous batches cannot be rebound',async()=>{
    const f=fixture();f.maximum=10;await assert.rejects(f.provider.prepareAttachments(fileArgs),error('invalid_attachments'));
    f.maximum=100;const batch=await f.provider.prepareAttachments(fileArgs);f.post=async()=>{throw new Error('timeout');};
    assert.equal((await f.provider.sendMessage(sendFiles(batch))).status,'unknown');
    await assert.rejects(f.provider.sendMessage(sendFiles(batch,{nonce:'124'})),error('attachment_batch_unavailable'));
    f.now+=900001;assert.equal((await f.provider.sendMessage(sendFiles(batch))).status,'unknown');assert.equal(f.posts.length,2);
});
test('settled attachment batches release capacity while terminal nonce deduplication remains',async()=>{
    for(const failure of [false,true]){
        const f=fixture();let originalPost=f.api.post,first;
        f.api.post=async args=>{
            if(failure&&args.url.endsWith('/messages')){f.posts.push(plain(args));throw {status:403};}
            return originalPost(args);
        };
        for(let i=0;i<12;i++){
            const batch=await f.provider.prepareAttachments(fileArgs),args=sendFiles(batch,{nonce:String(100+i)});first??=args;
            assert.equal((await f.provider.sendMessage(args)).status,failure?'failed':'sent');
        }
        const count=f.posts.length;assert.equal((await f.provider.sendMessage(first)).status,failure?'failed':'sent');assert.equal(f.posts.length,count);
    }
});
test('unknown attachment batches retain capacity until read-only reconciliation confirms delivery',async()=>{
    const f=fixture(), originalPost=f.api.post;let first;
    f.api.post=async args=>{if(args.url.endsWith('/messages')){f.posts.push(plain(args));throw new Error('timeout');}return originalPost(args);};
    for(let i=0;i<8;i++){
        const batch=await f.provider.prepareAttachments(fileArgs),args=sendFiles(batch,{nonce:String(100+i)});first??=args;
        assert.equal((await f.provider.sendMessage(args)).status,'unknown');
    }
    await assert.rejects(f.provider.prepareAttachments(fileArgs),error('attachment_capacity'));
    f.messages=[message('1234',{author:{id:'1'},nonce:first.nonce})];
    assert.equal((await f.provider.reconcileSend(first)).status,'sent');
    await f.provider.prepareAttachments(fileArgs);
});
