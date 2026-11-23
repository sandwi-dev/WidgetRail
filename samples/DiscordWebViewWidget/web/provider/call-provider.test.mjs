import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {webcrypto} from 'node:crypto';
import vm from 'node:vm';
import test from 'node:test';

const source=readFileSync(new URL('./call-provider.js',import.meta.url),'utf8');
const code=expected=>error=>error.adapterCode===expected;
function fixture(){
    const f={account:'1',selected:null,events:[],listeners:new Set(),calls:new Map(),people:new Map(),invalidations:0,blocked:false};
    f.channels=new Map([['10',{id:'10',type:1,recipients:['2']}],['20',{id:'20',type:3,recipients:['2','3'],name:'Test group'}]]);
    f.emit=()=>{for(const handler of [...f.listeners])handler();};
    f.stores={CallStore:{getCalls:()=>[...f.calls.values()],getCall:id=>f.calls.get(id),
        addChangeListener:handler=>f.listeners.add(handler),removeChangeListener:handler=>f.listeners.delete(handler)},
        ChannelStore:{getChannel:id=>f.channels.get(id)},RelationshipStore:{isBlockedOrIgnored:()=>f.blocked},
        UserStore:{getUser:id=>({username:'Person '+id})},VoiceStateStore:{getVoiceStatesForChannel:id=>f.people.get(id)??{}}};
    f.actions={startCall(id){f.events.push(['start',id]);f.selected=id;f.calls.set(id,{channelId:id,messageId:'100',ringing:['2']});f.emit();},
        selectVoiceChannel(id){f.events.push(['join',id]);f.selected=id;f.emit();},
        stopRinging(id,users){f.events.push(['decline',id,...users]);const call=f.calls.get(id);call.ringing=call.ringing.filter(user=>!users.includes(user));f.emit();}};
    const context=vm.createContext({crypto:webcrypto,setTimeout,clearTimeout});vm.runInContext(`(()=>{${source}\n})()`,context);
    f.provider=context.createWidgetRailDiscordCalls({store:name=>f.stores[name],props:()=>f.actions,me:()=>f.account?{id:f.account}:null,
        currentChannel:()=>f.selected,invalidate:()=>f.invalidations++});
    f.ring=(channel='10')=>{f.calls.set(channel,{channelId:channel,messageId:'100',ringing:['1','3']});f.emit();return f.provider.snapshot().incoming.find(item=>item.channelId===channel);};
    f.args=(extra={})=>({owner:'a'.repeat(32),commandId:'b'.repeat(32),account:'1',previousChannelId:f.selected,channelId:'10',...extra});
    return f;
}

test('incoming state is bounded private-call metadata with capability probes',()=>{
    const f=fixture(),invite=f.ring(),snapshot=f.provider.snapshot();
    assert.match(invite.id,/^[a-f0-9]{32}$/);assert.equal(invite.name,'Person 2');
    assert.equal(snapshot.available,true);assert.equal(snapshot.canAnswer,true);
    assert.equal(f.provider.snapshot().incoming[0].id,invite.id);assert.equal(f.events.length,0);
    delete f.actions.stopRinging;assert.equal(f.provider.snapshot().canDecline,false);
});

test('current CallActions supports outgoing calls and preserves recipient preflight',async()=>{
    const f=fixture();delete f.actions.startCall;
    f.actions.ring=()=>{throw Error('Provider must not ring separately');};
    f.actions.call=function(channel,video,ring,recipient,done){assert.equal(this,f.actions);f.events.push([channel,video,ring,recipient]);f.selected=channel;done();};
    assert.equal(f.provider.snapshot().canStart,true);
    await f.provider.invoke('callStart',f.args());
    assert.deepEqual(f.events,[['10',false,true,'2']]);
    f.selected=null;
    await f.provider.invoke('callStart',f.args({channelId:'20',commandId:'d'.repeat(32)}));
    assert.deepEqual(f.events[1],['20',false,true,undefined]);
});

test('a void-returning CallActions preflight remains single flight until its callback',async()=>{
    const f=fixture();delete f.actions.startCall;let finish;
    f.actions.ring=()=>{};
    f.actions.call=(_channel,_video,_ring,_recipient,done)=>{f.events.push(['call']);finish=done;};
    const args=f.args(),pending=f.provider.invoke('callStart',args);
    await Promise.resolve();
    assert.equal(f.provider.invoke('callStart',args),pending);
    assert.throws(()=>f.provider.invoke('callStart',f.args({commandId:'e'.repeat(32)})),code('call_action_pending'));
    assert.equal(f.events.length,1);finish();await pending;
});

test('inherited Function.call cannot masquerade as the current CallActions module',()=>{
    const f=fixture();delete f.actions.startCall;f.actions.call=Function.prototype.call;
    assert.equal(f.provider.snapshot().canStart,false);
});
test('decline stops only this account ringing and does not leave a current call',async()=>{
    const f=fixture();f.selected='20';const invite=f.ring();
    await f.provider.invoke('callDecline',f.args({invitationId:invite.id}));
    assert.deepEqual(f.events,[['decline','10','1']]);assert.deepEqual(f.calls.get('10').ringing,['3']);
    assert.equal(f.selected,'20');assert.equal(f.provider.snapshot().incoming.length,0);
});
test('answer joins without startCall and removes the local invitation',async()=>{
    const f=fixture(),invite=f.ring();
    await f.provider.invoke('callAnswer',f.args({invitationId:invite.id}));
    assert.deepEqual(f.events,[['join','10']]);assert.equal(f.provider.snapshot().incoming.length,0);
});
test('outgoing call becomes active from voice participants, not a retained CallStore record',async()=>{
    const f=fixture();await f.provider.invoke('callStart',f.args());
    assert.deepEqual(f.events,[['start','10']]);assert.equal(f.provider.snapshot().current.phase,'outgoing');
    f.calls.get('10').ringing=[];f.people.set('10',{2:{userId:'2',channelId:'10'}});
    assert.equal(f.provider.snapshot().current.phase,'active');
    f.people.clear();assert.equal(f.provider.snapshot().current.phase,'waiting');
    f.calls.get('10').unavailable=true;assert.equal(f.provider.snapshot().current.phase,'unavailable');
    f.selected=null;assert.equal(f.provider.snapshot().current,null);
});
test('an existing active group call is joined without ringing everyone again',async()=>{
    const f=fixture();f.calls.set('20',{channelId:'20',messageId:'200',ringing:[]});f.people.set('20',{2:{userId:'2'}});
    await f.provider.invoke('callStart',f.args({channelId:'20'}));assert.deepEqual(f.events,[['join','20']]);
});
test('cancelled and renewed invitation receives a new identity even for the same call message',async()=>{
    const f=fixture(),old=f.ring();f.calls.get('10').ringing=[];f.emit();const fresh=f.ring();
    assert.notEqual(fresh.id,old.id);
    await assert.rejects(f.provider.invoke('callAnswer',f.args({invitationId:old.id})),code('call_invitation_expired'));
    assert.equal(f.events.length,0);
});
test('current call changes revoke a delayed user action before dispatch',async()=>{
    const f=fixture(),invite=f.ring(),args=f.args({invitationId:invite.id});f.selected='20';
    await assert.rejects(f.provider.invoke('callAnswer',args),code('call_changed'));assert.equal(f.events.length,0);
});
test('duplicate command IDs reuse one result and reject changed intent',async()=>{
    const f=fixture(),args=f.args();const first=f.provider.invoke('callStart',args),second=f.provider.invoke('callStart',args);
    assert.equal(first,second);await first;await f.provider.invoke('callStart',args);assert.equal(f.events.length,1);
    assert.throws(()=>f.provider.invoke('callStart',{...args,channelId:'20'}),code('invalid_request'));
});
test('an unsettled call command rejects a second command and never automatically retries an ambiguous dispatch',async()=>{
    const f=fixture();let release;const wait=new Promise(resolve=>release=resolve);
    f.actions.startCall=async id=>{f.events.push(['start',id]);await wait;throw new Error('Lost response after dispatch');};
    const args=f.args(),first=f.provider.invoke('callStart',args);
    assert.throws(()=>f.provider.invoke('callStart',f.args({commandId:'c'.repeat(32)})),code('call_action_pending'));
    await Promise.resolve();release();await assert.rejects(first,/Lost response/);
    await assert.rejects(f.provider.invoke('callStart',args),/Lost response/);assert.equal(f.events.length,1);
});
test('account replacement rejects completion and old invitations',async()=>{
    const f=fixture(),invite=f.ring();f.actions.selectVoiceChannel=()=>{f.selected='10';f.account='9';};
    await assert.rejects(f.provider.invoke('callAnswer',f.args({invitationId:invite.id})),code('account_changed'));
    assert.throws(()=>f.provider.invoke('callStart',f.args()),code('account_changed'));
});
test('missing stores and blocked or guild targets fail closed without breaking snapshot',async()=>{
    const f=fixture();delete f.stores.CallStore;
    assert.equal(f.provider.snapshot().available,false);
    await assert.rejects(f.provider.invoke('callStart',f.args()),code('call_unavailable'));
    const blocked=fixture();blocked.blocked=true;assert.equal(blocked.ring(),undefined);
    await assert.rejects(blocked.provider.invoke('callStart',blocked.args()),code('call_unavailable'));
    const guild=fixture();guild.channels.set('10',{id:'10',type:2,guild_id:'30',recipients:[]});
    await assert.rejects(guild.provider.invoke('callStart',guild.args()),code('call_unavailable'));
});
test('source replacement and disposal retire store listeners and invitation identities',()=>{
    const f=fixture(),invite=f.ring(),old=f.stores.CallStore;assert.equal(f.listeners.size,1);
    f.stores.CallStore={...old};assert.notEqual(f.provider.snapshot().incoming[0].id,invite.id);
    assert.equal(f.listeners.size,1);f.provider.dispose();assert.equal(f.listeners.size,0);
    assert.throws(()=>f.provider.invoke('callStart',f.args()),code('calls_retired'));
    assert.equal(f.provider.snapshot().available,false);
});
test('invitation actions fail closed when store observation cannot track cancellation and re-ringing',async()=>{
    for(const broken of ['missing','throws']) {
        const f=fixture();
        if(broken==='missing')delete f.stores.CallStore.addChangeListener;
        else f.stores.CallStore.addChangeListener=()=>{throw Error('incompatible listener');};
        const invitation=f.ring(),state=f.provider.snapshot();
        assert.equal(state.canAnswer,false);assert.equal(state.canDecline,false);
        await assert.rejects(f.provider.invoke('callAnswer',f.args({invitationId:invitation.id})),code('call_action_unavailable'));
        assert.equal(f.events.length,0);
    }
});
