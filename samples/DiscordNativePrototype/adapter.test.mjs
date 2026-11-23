import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { EventEmitter } from 'node:events';

const build=JSON.parse(await readFile(new URL('./adapter-contract.json',import.meta.url),'utf8')).build;
let source = (await readFile(new URL('./activity-provider.js', import.meta.url), 'utf8'))+'\n'+(await readFile(new URL('./adapter.js', import.meta.url), 'utf8'))+`\ninitializeWidgetRailDiscordAdapter(${build});`;
if(process.env.WRAIL_MINIFIED_TESTS==='1')source=await(await import('../../tools/DiscordWebViewPrototype/Build-Backend.mjs')).minifySource(source);
function fixture() {
    const events = [], pending = [], captureOptions = [], audioReads = [], demandCalls=[], peers=[], timers = new Map();
    let nextTimer = 0;
    const state = { account: '1', channel: '20', muted: false, deafened: false, share: null, capability: true, viewers:[],watchClose:true, closed: 0, destroyed: 0, started: 0, now: 1000,
        audioAvailable:true,streamMuted:false,streamGain:18,voiceMuted:false,voiceGain:100,demandAvailable:true,globalVideo:true };
    const pool={capture:{id:'capture',destroy:()=>state.destroyed++}};
    // Discord DesktopInputPool.get transfers ownership: it deletes its entry.
    // A Map.get mock hides accidental consumption by diagnostic/quality reads.
    const desktopInputPool={pool,get(id){state.poolTakes=(state.poolTakes??0)+1;const value=pool[id];delete pool[id];return value;},
        set(id,value){pool[id]=value;return this;},release(value){pool[value.id]=value;}};
    const engine = { connections: [],
        getDesktopSource: options => { captureOptions.push(options);return new Promise(resolve => pending.push(resolve)); }, desktopInputPool };
    const makeRtc=(trackId='track')=>{
        const track={kind:'video',readyState:'live',muted:false,id:trackId};
        class QualityManager {
            incomingVideoEnabled=true;
            getUserID(){return '2';}
            onIncomingVideoEnabled(value){demandCalls.push([this,value]);this.incomingVideoEnabled=value;track.muted=!value;}
        }
        const manager=new QualityManager();
        const connection={context:'stream',streamUserId:'2',pc:{getReceivers:()=>[{track}]}};
        engine.connections.push(connection);
        const rtc={context:'stream',streamKey:'guild:10:20:2',streamContext:{ownerId:'2',channelId:'20'},
            _destroyed:false,isOwner:false,_goLiveQualityManager:manager,_connection:connection};
        return {rtc,manager,connection,track};
    };
    const initial=makeRtc();state.rtc=initial.rtc;
    const visibility={isIncomingVideoEnabled:()=>state.globalVideo,lastIncomingVideoEnabledChangeTime:()=>state.now};
    const stores = {
        UserStore: { getCurrentUser: () => state.account ? { id: state.account } : null, getUser: () => ({ username: 'Test participant' }) },
        SelectedChannelStore: { getVoiceChannelId: () => state.channel },
        ChannelStore: { getChannel: id => id==='20'?({ id, type:2, name: 'Test call', guild_id: '10' }):null },
        PermissionStore:{can:()=>true},
        GuildStore: { getGuilds: () => ({ '10': { id: '10', name: 'Test server' } }) },
        GuildChannelStore: { getChannels: () => ({ voice: [{ channel: { id: '20', name: 'Voice', type: 2 } }] }) },
        MediaEngineStore: { getMediaEngine: () => engine, isSelfMute: () => state.muted, isSelfDeaf: () => state.deafened,
            isLocalMute:(userId,context='default')=>{audioReads.push(['mute',userId,context]);return context==='stream'?state.streamMuted:state.voiceMuted;},
            getLocalVolume:(userId,context='default')=>{audioReads.push(['volume',userId,context]);return context==='stream'?state.streamGain:state.voiceGain;} },
        VoiceStateStore: { getVoiceStatesForChannel: () => ({ '2': { userId: '2', selfStream: true } }) },
        StreamRTCConnectionStore:{getRTCConnection:key=>key==='guild:10:20:2'?state.rtc:null},
        ApplicationStreamingStore: { getCurrentUserActiveStream: () => state.share, getViewerIds: () => state.viewers,
            getAllActiveStreamsForChannel: () => [], getAllApplicationStreamsForChannel: () => [] }
    };
    const audioActions={
        toggleLocalMute:(userId,context='default')=>{
            events.push(['streamMute',userId,context]);
            if(context==='stream')state.streamMuted=!state.streamMuted;else state.voiceMuted=!state.voiceMuted;
        },
        setLocalVolume:(userId,value,context='default')=>{
            events.push(['streamVolume',userId,value,context]);
            if(context==='stream')state.streamGain=value;else state.voiceGain=value;
        }
    };
    class Peer {
        constructor() { if(state.failPeer)throw Error('Synthetic peer allocation failure');this.iceGatheringState = 'complete'; this.connectionState = 'new';peers.push(this); }
        addTrack(t) { this.sender={track:t,replaceTrack:async t=>{this.sender.track=t;}};return this.sender; }
        async createOffer() { return { type: 'offer', sdp: 'test' }; }
        async setLocalDescription(value) { this.localDescription = { ...value, toJSON: () => value }; }
        async setRemoteDescription() { this.connectionState = 'connected'; }
        close() { state.closed++; }
    }
    const context = vm.createContext({ console, RTCPeerConnection: Peer, Date: class extends Date { static now() { return state.now; } },
        setTimeout: (cb, ms) => { const id = ++nextTimer; timers.set(id, { cb, ms }); return id; },
        clearTimeout: id => timers.delete(id), setInterval: (cb,ms) => {const id=++nextTimer;timers.set(id,{cb,ms,interval:true});return id;}, clearInterval: id => timers.delete(id),
        VesktopNative: { win: { focus: async () => {} } },
        Vencord: { Webpack: { findStore: name => stores[name],
            findByProps: (...names) => names.includes('isIncomingVideoEnabled') ? (state.demandAvailable?visibility:undefined) : names.includes('setLocalVolume') ? (state.audioAvailable?audioActions:undefined) : state.capability ? { selectVoiceChannel: id=>{events.push({type:'VOICE_CHANNEL_SELECT',channelId:id});state.channel=id;},toggleSelfMute: () => { state.muted = !state.muted; events.push('mute'); },
                toggleSelfDeaf: () => { state.deafened = !state.deafened; events.push('deafen'); } } : undefined,
            findByCode: pattern => {
                if (!state.capability) return undefined;
                if (pattern.includes('STREAM_START')) return () => { state.started++; state.share = { channelId: state.channel, guildId: '10', ownerId: state.account }; };
                if (pattern.includes('STREAM_STOP')) return () => { state.share = null; events.push('stop'); };
                if (pattern.includes('STREAM_CLOSE')) return state.watchClose?key=>events.push('close:'+key):undefined;
                if (pattern.includes('STREAM_WATCH')) return descriptor => {
                    assert.equal(descriptor.streamType, 'guild'); assert.equal(descriptor.ownerId, '2');
                    events.push('watch');state.viewers=['1'];
                };
                return () => events.push(pattern);
            }, Common: { FluxDispatcher: { dispatch: event => { events.push(event); state.channel = event.channelId; } } } } }
    });
    vm.runInContext(source, context);
    const invoke = (method, args = {}) => context.WidgetRailDiscordPrototype.invoke({ method, owner: 'c'.repeat(32), args: { account: '1', channelId: '20', ...args } });
    return { state, stores, events, pending, captureOptions, audioReads, audioActions, demandCalls,peers,initial,visibility,makeRtc,timers, invoke, context, engine };
}
const settle = () => new Promise(resolve => setImmediate(resolve));

test('snapshot is read-only, bounded and does not expose credentials', async () => {
    const f = fixture(); const result = await f.invoke('snapshot');
    assert.equal(result.ok, true); assert.equal(result.value.channels.length, 1); assert.equal(result.value.streams.length, 1);
    assert.equal(f.events.length, 0); assert.equal(JSON.stringify(result).includes('token'), false);
});
test('rejects commands for a prior account', async () => {
    const f = fixture(); f.state.account = '3';
    assert.equal((await f.invoke('mute', { value: true })).code, 'account_changed'); assert.equal(f.events.length, 0);
});
test('rejects commands for a prior call', async () => {
    const f = fixture(); f.state.channel = '21';
    assert.equal((await f.invoke('leave')).code, 'call_changed'); assert.equal(f.events.length, 0);
});
test('mute is a desired state, not a replayable blind toggle', async () => {
    const f = fixture(); await f.invoke('mute', { value: true }); await f.invoke('mute', { value: true });
    assert.equal(f.state.muted, true); assert.deepEqual(f.events, ['mute']);
});
test('missing capabilities fail closed', async () => {
    const f = fixture(); f.state.capability = false;
    assert.equal((await f.invoke('share')).code, 'share_unavailable'); assert.equal(f.pending.length, 0);
});
test('joining validates current channel catalogue', async () => {
    const f = fixture(); assert.equal((await f.invoke('join', { target: '999' })).code, 'channel_unavailable');
    assert.equal(f.events.length, 0);
});
test('duplicate share request does not open a second picker', async () => {
    const f = fixture(); await f.invoke('share'); await settle();
    assert.equal((await f.invoke('share')).code, 'share_already_active'); assert.equal(f.pending.length, 1);
    await f.invoke('stopShare'); f.pending[0]('capture'); await settle();
});

test('omitted quality preserves the existing provider source request',async()=>{
    const f=fixture();assert.equal((await f.invoke('share')).ok,true);await settle();
    assert.deepEqual(JSON.parse(JSON.stringify(f.captureOptions[0])),{width:1280,height:720});
    await f.invoke('stopShare');f.pending[0]('capture');await settle();
});

test('baseline quality reaches provider discovery without authorizing premium modes',async()=>{
    for(const resolution of [480,720])for(const frameRate of [15,30]){
        const f=fixture();assert.equal((await f.invoke('share',{captureQuality:{resolution,frameRate}})).ok,true);await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(f.captureOptions[0])),{width:resolution===480?854:1280,height:resolution,frameRate});
        await f.invoke('stopShare');f.pending[0]('capture');await settle();
    }
});

test('invalid or premium quality is rejected before opening a source',async()=>{
    for(const captureQuality of [null,{},[],{resolution:1080,frameRate:60},{resolution:720,frameRate:60},{resolution:720,frameRate:30,source:'other'}]){
        const f=fixture();assert.equal((await f.invoke('share',{captureQuality})).code,'share_quality_unsupported');
        assert.equal(f.pending.length,0);assert.equal(f.state.started,0);
    }
});
test('cancel during capture never starts late and disposes acquired source', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); await f.invoke('stopShare');
    f.pending[0]('capture'); await settle(); assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
});
test('account change during capture prevents publication', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); f.state.account = '3'; f.pending[0]('capture'); await settle();
    assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
});
test('call change during capture prevents publication', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); f.state.channel = '21'; f.pending[0]('capture'); await settle();
    assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
});
test('capture timeout discards a late source', async () => {
    const f = fixture(); await f.invoke('share'); await settle();
    [...f.timers.values()].find(t => t.ms === 60000).cb(); f.pending[0]('capture'); await settle();
    assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
});
test('selected capture starts once and stop reflects actual Discord state', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); f.pending[0]('capture'); await settle();
    assert.equal(f.state.started, 1); assert.equal((await f.invoke('snapshot')).value.sharing, true);
    await f.invoke('stopShare'); assert.equal((await f.invoke('snapshot')).value.sharing, false);
});
test('relay accepts only a currently advertised stream and matching answer lease', async () => {
    const f = fixture(); const lease = 'a'.repeat(32);
    assert.equal((await f.invoke('watch', { key: 'missing', lease })).code, 'stream_ended');
    const result = await f.invoke('watch', { key: 'guild:10:20:2', lease }); assert.equal(result.ok, true);
    assert.equal((await f.invoke('answer', { lease: 'b'.repeat(32), answer: {} })).code, 'watch_cancelled');
    await f.invoke('unwatch', { lease }); assert.equal(f.state.closed, 1);
    assert.equal((await f.invoke('answer', { lease, answer: {} })).code, 'watch_cancelled');
});
test('reinjection preserves adapter instead of duplicating timers', () => {
    const f = fixture(); const before = f.context.WidgetRailDiscordPrototype;
    vm.runInContext(source, f.context); assert.equal(before, f.context.WidgetRailDiscordPrototype);
});
test('unknown commands are rejected', async () => {
    assert.equal((await fixture().invoke('eval', { code: 'anything' })).code, 'unknown_command');
});

test('widget teardown cancels pending capture without ending an active broadcast', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); await f.invoke('cancelPendingShare');
    f.pending[0]('capture'); await settle(); assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
    f.state.share = { channelId: '20', guildId: '10', ownerId: '1' };
    await f.invoke('cancelPendingShare'); assert.notEqual(f.state.share, null);
});

test('another connection cannot cancel a pending capture owner', async () => {
    const f = fixture(); await f.invoke('share'); await settle();
    await f.context.WidgetRailDiscordPrototype.invoke({ method: 'cancelPendingShare', owner: 'd'.repeat(32), args: { account: '1' } });
    f.pending[0]('capture'); await settle(); assert.equal(f.state.started, 1);
});

test('a lost widget heartbeat rejects late capture even if another probe is alive', async () => {
    const f = fixture(); await f.invoke('share'); await settle(); f.state.now += 13000;
    await f.context.WidgetRailDiscordPrototype.invoke({ method: 'snapshot', owner: 'd'.repeat(32) });
    f.pending[0]('capture'); await settle(); assert.equal(f.state.started, 0); assert.equal(f.state.destroyed, 1);
});

test('packaged receiver loads SDK messaging before its code and reports ready', async () => {
    const html = await readFile(new URL('./web/receiver.html', import.meta.url), 'utf8');
    assert.ok(html.indexOf('src="web-session.js"') < html.indexOf('src="receiver.js"'));
    assert.ok(html.includes('src="web-session.js"'));
    const runtime = await readFile(new URL('../../src/WidgetSdk/WebSessionRuntime.js', import.meta.url), 'utf8');
    const receiver = await readFile(new URL('./web/receiver.js', import.meta.url), 'utf8');
    const posted = [], listeners = [];
    const context = vm.createContext({ TextEncoder, Map, AbortController, setTimeout, clearTimeout,
        document: { getElementById: () => ({ srcObject: null, textContent: '', play: async () => {} }) },
        chrome: { webview: { postMessage: value => posted.push(value), addEventListener: (...a) => listeners.push(a) } } });
    context.window = context; context.addEventListener = () => {};
    vm.runInContext(runtime, context); vm.runInContext(receiver, context);
    assert.equal(posted.length, 1); assert.equal(posted[0].ready, true); assert.equal(listeners.length, 1);
});

test('missing or malformed viewer ownership fails closed before opening or closing a provider watch',async()=>{
 for(const invalid of [undefined,null,{},[null],['not-an-id'],Array(1001).fill('2')]){
  const f=fixture();f.state.viewers=invalid;const s=await f.invoke('snapshot');assert.equal(s.value.capabilities.watch,false);
  assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).code,'watch_ownership_unavailable');assert.deepEqual(f.events,[]);
 }
 const f=fixture();delete f.stores.ApplicationStreamingStore.getViewerIds;assert.equal((await f.invoke('snapshot')).value.capabilities.watch,false);assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).ok,false);assert.deepEqual(f.events,[]);
});
test('new provider watches require an available exact close action',async()=>{
 const f=fixture();f.state.watchClose=false;assert.equal((await f.invoke('snapshot')).value.capabilities.watch,false);
 assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).ok,false);assert.deepEqual(f.events,[]);
});
test('preexisting user subscription can be relayed without taking ownership or requiring close',async()=>{
 const f=fixture();f.state.viewers=['1'];f.state.watchClose=false;assert.equal((await f.invoke('snapshot')).value.capabilities.watch,true);
 assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).ok,true);await f.invoke('unwatch',{lease:'a'.repeat(32)});assert.deepEqual(f.events,[]);
});
test('owned watch retires with its captured exact close action even if discovery changes',async()=>{
 const f=fixture();assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).ok,true);f.state.watchClose=false;
 await f.invoke('unwatch',{lease:'a'.repeat(32)});assert.deepEqual(f.events,['watch','close:guild:10:20:2']);
});
test('same-stream relay replacement transfers our subscription without a close/reopen gap',async()=>{
 const f=fixture();assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)})).ok,true);
 assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'b'.repeat(32)})).ok,true);assert.deepEqual(f.events,['watch']);
 await f.invoke('unwatch',{lease:'b'.repeat(32)});assert.deepEqual(f.events,['watch','close:guild:10:20:2']);
});
test('invalid later ownership read preserves the current relay and subscription',async()=>{
 const f=fixture();await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)});f.state.viewers=null;
 assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'b'.repeat(32)})).ok,false);assert.equal(f.state.closed,0);assert.deepEqual(f.events,['watch']);
 await f.invoke('unwatch',{lease:'a'.repeat(32)});assert.deepEqual(f.events,['watch','close:guild:10:20:2']);
});

test('replacement peer allocation failure preserves the previous owned subscription',async()=>{
 const f=fixture();await f.invoke('watch',{key:'guild:10:20:2',lease:'a'.repeat(32)});
 f.state.failPeer=true;
 assert.equal((await f.invoke('watch',{key:'guild:10:20:2',lease:'b'.repeat(32)})).ok,false);
 assert.equal(f.state.closed,0);assert.deepEqual(f.events,['watch']);
 await f.invoke('unwatch',{lease:'a'.repeat(32)});
 assert.equal(f.state.closed,1);assert.deepEqual(f.events,['watch','close:guild:10:20:2']);
});

const streamTarget={key:'guild:10:20:2',userId:'2',lease:'a'.repeat(32)};
async function watchingFixture(){
    const f=fixture();assert.equal((await f.invoke('watch',streamTarget)).ok,true);f.events.length=0;return f;
}

test('snapshot keeps voice and stream percentages in their separate contexts',async()=>{
    const f=fixture();const snapshot=(await f.invoke('snapshot')).value,audio=snapshot.streams[0].audio;
    assert.equal(audio.available,true);assert.equal(audio.muted,false);
    assert.ok(Math.abs(audio.volume-100*Math.pow(0.18,1/2.8))<1e-10);
    assert.equal(snapshot.participants[0].audio.volume,100);
    assert.deepEqual(f.audioReads,[['mute','2','default'],['volume','2','default'],['mute','2','stream'],['volume','2','stream']]);
    assert.deepEqual(f.events,[]);
});

test('stream mute applies desired state once and leaves voice untouched',async()=>{
    const f=await watchingFixture();
    for(const value of [true,true,false,false])assert.equal((await f.invoke('streamMute',{...streamTarget,value})).ok,true);
    assert.deepEqual(f.events,[['streamMute','2','stream'],['streamMute','2','stream']]);
    assert.equal(f.state.voiceMuted,false);assert.equal(f.state.voiceGain,100);assert.equal(f.state.streamGain,18);
});

test('stream volume roundtrips perceptual percentage, preserves mute and accepts exact endpoints',async()=>{
    const f=await watchingFixture();f.state.streamMuted=true;
    for(const value of [0,25,50,75,100]){
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value})).ok,true);
        assert.ok(Math.abs(f.state.streamGain-100*Math.pow(value/100,2.8))<1e-10);
        assert.ok(Math.abs((await f.invoke('snapshot')).value.streams[0].audio.volume-value)<1e-8);
        const count=f.events.length;
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value})).ok,true);assert.equal(f.events.length,count);
    }
    assert.ok(f.events.every(([name,userId,,context])=>name==='streamVolume'&&userId==='2'&&context==='stream'));
    assert.equal(f.state.streamMuted,true);assert.equal(f.state.voiceMuted,false);assert.equal(f.state.voiceGain,100);
});

test('invalid audio values are rejected without clamping, coercion or provider changes',async()=>{
    const f=await watchingFixture();
    for(const value of [undefined,null,true,'50',NaN,Infinity,-Infinity,-0.001,100.001,200,[],{}])
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value})).code,'invalid_request');
    for(const value of [undefined,null,0,1,'true',[],{}])
        assert.equal((await f.invoke('streamMute',{...streamTarget,value})).code,'invalid_request');
    assert.deepEqual(f.events,[]);assert.equal(f.state.streamGain,18);assert.equal(f.state.streamMuted,false);
});

test('missing audio APIs and invalid provider state fail closed',async()=>{
    const changes=[
        f=>f.state.audioAvailable=false,
        f=>delete f.audioActions.setLocalVolume,
        f=>delete f.audioActions.toggleLocalMute,
        f=>delete f.stores.MediaEngineStore.getLocalVolume,
        f=>delete f.stores.MediaEngineStore.isLocalMute,
        f=>f.state.streamMuted='false',
        f=>f.stores.MediaEngineStore.getLocalVolume=()=>{throw Error('provider unavailable');},
        ...[null,undefined,NaN,Infinity,-1,200.01,'18'].map(value=>f=>f.state.streamGain=value)
    ];
    for(const change of changes){
        const f=await watchingFixture();change(f);
        const snapshot=await f.invoke('snapshot');assert.equal(snapshot.ok,true);
        assert.deepEqual(JSON.parse(JSON.stringify(snapshot.value.streams[0].audio)),{available:false,muted:false,volume:null});
        assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).code,'stream_audio_unavailable');
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:50})).code,'stream_audio_unavailable');
        assert.deepEqual(f.events,[]);
    }
});

test('desktop-boosted volume displays the browser ceiling without rewriting saved gain',async()=>{
    for(const gain of [100.01,156.00303649902344,200]){
        const f=await watchingFixture();f.state.streamGain=gain;f.state.streamMuted=true;
        const audio=(await f.invoke('snapshot')).value.streams[0].audio;
        assert.equal(audio.available,true);assert.equal(audio.muted,true);assert.equal(audio.volume,100);
        assert.equal((await f.invoke('snapshot')).value.streams[0].audio.volume,100);
        assert.equal(f.state.streamGain,gain);assert.deepEqual(f.events,[]);
        assert.equal((await f.invoke('streamMute',{...streamTarget,value:false})).ok,true);
        assert.equal(f.state.streamMuted,false);assert.ok(Object.is(f.state.streamGain,gain));
        assert.deepEqual(f.events,[['streamMute','2','stream']]);
        // Reading or re-sending the slider's unchanged ceiling never discards
        // a desktop preference. A genuine adjustment writes without unmuting.
        f.state.streamMuted=true;
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:100})).ok,true);
        assert.equal(f.state.streamGain,gain);assert.deepEqual(f.events,[['streamMute','2','stream']]);
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:95})).ok,true);
        const adjusted=100*Math.pow(0.95,2.8);
        assert.equal(f.state.streamGain,adjusted);assert.equal(f.state.streamMuted,true);
        assert.ok(Math.abs((await f.invoke('snapshot')).value.streams[0].audio.volume-95)<1e-8);
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:100})).ok,true);
        assert.equal(f.state.streamGain,100);assert.equal(f.state.streamMuted,true);
        assert.equal((await f.invoke('snapshot')).value.streams[0].audio.volume,100);
        assert.deepEqual(f.events,[['streamMute','2','stream'],['streamVolume','2',adjusted,'stream'],['streamVolume','2',100,'stream']]);
    }
});

test('stream audio requires current account, call, key, user and exact relay lease',async()=>{
    for(const method of ['streamMute','streamVolume']){
        const value=method==='streamMute'?true:50;
        for(const [patch,code] of [
            [{account:'3'},'account_changed'],[{channelId:'21'},'call_changed'],
            [{key:'guild:10:20:3'},'watch_cancelled'],[{userId:'3'},'watch_cancelled'],
            [{userId:undefined},'watch_cancelled'],[{lease:'b'.repeat(32)},'watch_cancelled'],[{lease:undefined},'watch_cancelled']]){
            const f=await watchingFixture();assert.equal((await f.invoke(method,{...streamTarget,value,...patch})).code,code);
            assert.deepEqual(f.events,[]);
        }
        const f=fixture();assert.equal((await f.invoke(method,{...streamTarget,value})).code,'watch_cancelled');assert.deepEqual(f.events,[]);
    }
});

test('another adapter caller cannot control audio with a copied relay lease',async()=>{
    const f=await watchingFixture();
    const result=await f.context.WidgetRailDiscordPrototype.invoke({method:'streamMute',owner:'d'.repeat(32),
        args:{account:'1',channelId:'20',...streamTarget,value:true}});
    assert.equal(result.code,'watch_cancelled');assert.deepEqual(f.events,[]);
});

test('ended subscriptions, expired leases and replaced receivers cannot change audio',async()=>{
    for(const change of [f=>f.state.viewers=[],f=>f.state.viewers=null,f=>f.state.now+=26000,
        async f=>{await f.invoke('watch',{...streamTarget,lease:'b'.repeat(32)});},
        async f=>{await f.invoke('unwatch',streamTarget);},f=>{f.stores.VoiceStateStore.getVoiceStatesForChannel=()=>({});}]){
        const f=await watchingFixture();await change(f);f.events.length=0;
        assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).ok,false);
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:50})).ok,false);assert.deepEqual(f.events,[]);
    }
});

test('preexisting user watch permits lease-owned audio controls without taking subscription ownership',async()=>{
    const f=fixture();f.state.viewers=['1'];f.state.watchClose=false;
    assert.equal((await f.invoke('watch',streamTarget)).ok,true);
    assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).ok,true);
    await f.invoke('unwatch',streamTarget);assert.deepEqual(f.events,[['streamMute','2','stream']]);
});

test('synchronous provider read cannot apply audio to a newly changed account or call',async()=>{
    for(const change of [f=>f.state.account='3',f=>f.state.channel='21']){
        const f=await watchingFixture();const read=f.stores.MediaEngineStore.getLocalVolume;
        f.stores.MediaEngineStore.getLocalVolume=(...args)=>{change(f);return read(...args);};
        assert.equal((await f.invoke('streamVolume',{...streamTarget,value:50})).ok,false);assert.deepEqual(f.events,[]);
    }
});

async function relayTick(f){
    const tick=[...f.timers.values()].find(t=>t.interval&&t.ms===1000);
    assert.ok(tick,'Expected the existing relay maintenance timer');await tick.cb();
}

test('incoming demand prevents the 30-second hidden-page shutdown only for its exact Go Live manager',async()=>{
    const f=await watchingFixture();const other=f.makeRtc('unrelated');
    const original=Object.getPrototypeOf(f.initial.manager).onIncomingVideoEnabled;
    f.state.now+=20000;await f.invoke('keep',{lease:streamTarget.lease});
    f.state.now+=9000;await f.invoke('keep',{lease:streamTarget.lease});f.state.now+=1000;
    f.state.globalVideo=false;
    f.initial.manager.onIncomingVideoEnabled(false);other.manager.onIncomingVideoEnabled(false);
    assert.equal(f.state.globalVideo,false);assert.equal(f.initial.manager.incomingVideoEnabled,true);
    assert.equal(f.initial.track.muted,false);assert.equal(other.manager.incomingVideoEnabled,false);
    assert.equal(Object.getPrototypeOf(f.initial.manager).onIncomingVideoEnabled,original);
    assert.equal(Object.hasOwn(other.manager,'onIncomingVideoEnabled'),false);
});

test('already hidden reception resumes before waiting for an unmuted track',async()=>{
    const f=fixture();f.state.globalVideo=false;f.initial.manager.onIncomingVideoEnabled(false);
    assert.equal(f.initial.track.muted,true);
    assert.equal((await f.invoke('watch',streamTarget)).ok,true);
    assert.equal(f.initial.track.muted,false);assert.equal(f.peers[0].sender.track,f.initial.track);
    assert.equal([...f.timers.values()].some(t=>!t.interval&&t.ms===100),false);
});

test('demand retirement restores current global policy and exact original descriptor once',async()=>{
    const f=fixture(),m=f.initial.manager,original=m.onIncomingVideoEnabled;
    Object.defineProperty(m,'onIncomingVideoEnabled',{value:original,writable:true,configurable:true,enumerable:true});
    const descriptor=Object.getOwnPropertyDescriptor(m,'onIncomingVideoEnabled');
    await f.invoke('watch',streamTarget);
    f.state.globalVideo=false; // Current policy, even without another emitted event.
    await f.invoke('unwatch',streamTarget);
    assert.deepEqual(Object.getOwnPropertyDescriptor(m,'onIncomingVideoEnabled'),descriptor);
    assert.equal(m.incomingVideoEnabled,false);
    const count=f.demandCalls.length;f.context.WidgetRailDiscordPrototype.dispose();
    assert.equal(f.demandCalls.length,count);
});

test('same-stream replacement transfers demand without withdrawing it for owned or preexisting subscriptions',async()=>{
    for(const preexisting of [false,true]){
        const f=fixture();if(preexisting)f.state.viewers=['1'];
        await f.invoke('watch',streamTarget);f.state.globalVideo=false;
        const wrapper=f.initial.manager.onIncomingVideoEnabled;f.demandCalls.length=0;
        await f.invoke('watch',{...streamTarget,lease:'b'.repeat(32)});
        assert.equal(f.initial.manager.onIncomingVideoEnabled,wrapper);assert.deepEqual(f.demandCalls,[]);
        f.initial.manager.onIncomingVideoEnabled(false);assert.equal(f.initial.manager.incomingVideoEnabled,true);
        await f.invoke('unwatch',{lease:streamTarget.lease});assert.equal(f.initial.manager.onIncomingVideoEnabled,wrapper);
        await f.invoke('unwatch',{lease:'b'.repeat(32)});
        assert.equal(f.initial.manager.incomingVideoEnabled,false);assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);
    }
});

test('replacement allocation failure preserves active demand and owned watch',async()=>{
    const f=await watchingFixture(),wrapper=f.initial.manager.onIncomingVideoEnabled;
    f.state.failPeer=true;f.demandCalls.length=0;
    assert.equal((await f.invoke('watch',{...streamTarget,lease:'b'.repeat(32)})).ok,false);
    assert.equal(f.initial.manager.onIncomingVideoEnabled,wrapper);assert.deepEqual(f.demandCalls,[]);assert.equal(f.state.closed,0);
});

test('same-key provider connection replacement discards obsolete same-user tracks and hooks',async()=>{
    const f=await watchingFixture(),old=f.initial;
    f.state.globalVideo=false;const replacement=f.makeRtc('replacement');replacement.manager.onIncomingVideoEnabled(false);
    f.state.rtc=replacement.rtc;f.demandCalls.length=0;
    await relayTick(f);
    assert.equal(Object.hasOwn(old.manager,'onIncomingVideoEnabled'),false);
    assert.equal(f.demandCalls.some(([manager])=>manager===old.manager),false,'Never replay policy through an obsolete connection');
    assert.equal(replacement.manager.incomingVideoEnabled,true);
    assert.equal(f.peers[0].sender.track,replacement.track);
    assert.equal(f.engine.connections[0],old.connection,'Old media connection remains first to exercise exact lookup');
});

test('manager replacement on the same RTC connection moves demand to the new instance',async()=>{
    const f=await watchingFixture(),old=f.initial.manager,newManager=f.makeRtc('new-manager').manager;
    f.state.rtc._goLiveQualityManager=newManager;await relayTick(f);
    assert.equal(Object.hasOwn(old,'onIncomingVideoEnabled'),false);
    assert.equal(Object.hasOwn(newManager,'onIncomingVideoEnabled'),true);
    newManager.onIncomingVideoEnabled(false);assert.equal(newManager.incomingVideoEnabled,true);
});

test('temporarily absent provider connection releases obsolete hook and binds only after exact replacement appears',async()=>{
    const f=await watchingFixture();f.state.rtc=null;await relayTick(f);
    assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);assert.equal(f.state.closed,0);
    const replacement=f.makeRtc('late');f.state.rtc=replacement.rtc;await relayTick(f);
    assert.equal(f.peers[0].sender.track,replacement.track);assert.equal(Object.hasOwn(replacement.manager,'onIncomingVideoEnabled'),true);
});

test('missing demand APIs fail before changing provider subscriptions and disable watch capability',async()=>{
    for(const change of [f=>f.state.demandAvailable=false,f=>delete f.stores.StreamRTCConnectionStore,
        f=>delete f.stores.StreamRTCConnectionStore.getRTCConnection,f=>f.state.globalVideo=null]){
        const f=fixture();change(f);
        assert.equal((await f.invoke('snapshot')).value.capabilities.watch,false);
        assert.equal((await f.invoke('watch',streamTarget)).code,'watch_demand_unavailable');
        assert.deepEqual(f.events,[]);assert.equal(f.peers.length,0);
    }
});

test('unmatched or unsupported stream-manager identities fail closed and retire the acquired watch',async()=>{
    for(const change of [f=>f.state.rtc.context='default',f=>f.state.rtc.streamKey='guild:10:20:3',
        f=>f.state.rtc.streamContext.ownerId='3',f=>f.state.rtc.streamContext.channelId='21',f=>f.state.rtc.isOwner=true,
        f=>f.state.rtc._destroyed=true,f=>f.state.rtc._connection.context='default',f=>f.state.rtc._connection.streamUserId='3',
        f=>f.engine.connections=[],f=>f.initial.manager.getUserID=()=> '3',f=>f.initial.manager.incomingVideoEnabled=null,
        f=>f.initial.manager.onIncomingVideoEnabled=null,f=>Object.preventExtensions(f.initial.manager),
        f=>Object.defineProperty(f.initial.manager,'onIncomingVideoEnabled',{value:f.initial.manager.onIncomingVideoEnabled,writable:false,configurable:false})]){
        const f=fixture();change(f);
        assert.equal((await f.invoke('watch',streamTarget)).code,'watch_demand_unavailable');
        assert.equal(f.state.closed,1);assert.deepEqual(f.events,['watch','close:guild:10:20:2']);assert.deepEqual(f.demandCalls,[]);
    }
});

test('foreign replacement is not overwritten and retained wrapper becomes inert after retirement',async()=>{
    const f=await watchingFixture(),m=f.initial.manager,ours=m.onIncomingVideoEnabled;
    const foreign=function(...args){return ours.apply(this,args);};m.onIncomingVideoEnabled=foreign;
    await relayTick(f);
    assert.equal(m.onIncomingVideoEnabled,foreign);assert.equal(f.state.closed,1);
    foreign.call(m,false);assert.equal(m.incomingVideoEnabled,false);
    assert.equal((await f.invoke('diagnostics')).value.detail,'watch_demand_replaced');
});

test('account call expiry and failed peers revoke demand before the maintenance timer retires the relay',async()=>{
    for(const change of [f=>f.state.account='3',f=>f.state.channel='21',f=>f.state.now+=26000,
        f=>f.peers[0].connectionState='failed',f=>f.peers[0].connectionState='closed']){
        const f=await watchingFixture();change(f);f.state.globalVideo=false;
        f.initial.manager.onIncomingVideoEnabled(false);assert.equal(f.initial.manager.incomingVideoEnabled,false);
        await relayTick(f);assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);assert.equal(f.state.closed,1);
    }
});

test('another adapter caller cannot prolong external demand with a copied heartbeat lease',async()=>{
    const f=await watchingFixture();f.state.now+=24000;
    await f.context.WidgetRailDiscordPrototype.invoke({method:'keep',owner:'d'.repeat(32),args:{account:'1',lease:streamTarget.lease}});
    f.state.now+=2000;await relayTick(f);assert.equal(f.state.closed,1);
});

test('lost visibility getter during cleanup uses the most recent provider event without blocking retirement',async()=>{
    const f=await watchingFixture();f.state.globalVideo=false;f.initial.manager.onIncomingVideoEnabled(false);
    f.visibility.isIncomingVideoEnabled=()=>{throw Error('provider disposed');};
    await f.invoke('unwatch',streamTarget);
    assert.equal(f.initial.manager.incomingVideoEnabled,false);assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);assert.equal(f.state.closed,1);
});

test('provider freezing the installed wrapper cannot prevent relay cleanup or retain forced demand',async()=>{
    const f=await watchingFixture();Object.freeze(f.initial.manager);f.state.globalVideo=false;
    assert.equal((await f.invoke('unwatch',streamTarget)).ok,true);assert.equal(f.state.closed,1);
    // The frozen manager cannot accept new state; the remaining wrapper still
    // forwards false rather than forcing true, proven by the original call log.
    try{f.initial.manager.onIncomingVideoEnabled(false);}catch{}
    assert.equal(f.demandCalls.at(-1)[1],false);
});

test('closing the provider watch retires demand without automatically subscribing again',async()=>{
    const f=await watchingFixture();f.state.globalVideo=false;f.state.viewers=[];
    await relayTick(f);
    assert.equal(f.state.closed,1);assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);
    assert.equal(f.initial.manager.incomingVideoEnabled,false);
    assert.equal(f.events.includes('watch'),false);assert.equal([...f.timers.values()].some(t=>t.interval),false);
});

test('expired heartbeat cannot resurrect demand before a throttled maintenance callback runs',async()=>{
    const f=await watchingFixture();f.state.now+=26000;await f.invoke('keep',{lease:streamTarget.lease});
    await relayTick(f);assert.equal(f.state.closed,1);
});

test('foreign adapter cannot answer or retire another caller relay with a copied lease',async()=>{
    const f=await watchingFixture(),wrapper=f.initial.manager.onIncomingVideoEnabled;
    const args={account:'1',channelId:'20',lease:streamTarget.lease,answer:{}};
    assert.equal((await f.context.WidgetRailDiscordPrototype.invoke({method:'answer',owner:'d'.repeat(32),args})).code,'watch_cancelled');
    await f.context.WidgetRailDiscordPrototype.invoke({method:'unwatch',owner:'d'.repeat(32),args});
    assert.equal(f.state.closed,0);assert.equal(f.initial.manager.onIncomingVideoEnabled,wrapper);
});

test('peer-close exception cannot prevent owned subscription and demand retirement',async()=>{
    const f=await watchingFixture();f.peers[0].close=()=>{throw Error('already disposed');};
    assert.equal((await f.invoke('unwatch',streamTarget)).ok,true);
    assert.equal(Object.hasOwn(f.initial.manager,'onIncomingVideoEnabled'),false);
    assert.deepEqual(f.events,['close:guild:10:20:2']);
});

test('friend activity watch availability adopts the same demand capability gate as stream controls',async()=>{
    const f=fixture();
    f.stores.RelationshipStore={getFriendIDs:()=>['2'],isFriend:()=>true,isBlockedOrIgnored:()=>false};
    f.stores.UserStore.getUser=id=>({id,username:'Test friend'});
    f.stores.PresenceStore={getStatus:()=> 'online',getActivities:()=>[]};
    f.stores.GuildStore.getGuild=()=>({id:'10',name:'Test server'});
    f.stores.VoiceStateStore.getDiscoverableVoiceStateForUser=()=>({userId:'2',channelId:'20',discoverable:true});
    f.stores.ApplicationStreamingStore.getAnyDiscoverableStreamForUser=()=>({ownerId:'2',guildId:'10',channelId:'20'});
    f.stores.ApplicationStreamingStore.isStreamMarkedFull=()=>false;
    assert.equal((await f.invoke('snapshot')).value.activities[0].canWatch,true);
    f.state.demandAvailable=false;
    const snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.capabilities.watch,false);assert.equal(snapshot.activities[0].canWatch,false);
});

test('audio commands return authoritative observed state without a separate full snapshot',async()=>{
    const f=await watchingFixture();
    const muted=await f.invoke('streamMute',{...streamTarget,value:true});
    assert.equal(muted.ok,true);assert.deepEqual(JSON.parse(JSON.stringify(muted.value)),
        {audio:{available:true,muted:true,volume:100*Math.pow(0.18,1/2.8)}});
    const volume=await f.invoke('streamVolume',{...streamTarget,value:75});
    assert.equal(volume.ok,true);assert.equal(volume.value.audio.muted,true);assert.ok(Math.abs(volume.value.audio.volume-75)<1e-8);
    f.state.streamGain=156.00303649902344;f.events.length=0;
    const unchanged=await f.invoke('streamVolume',{...streamTarget,value:100});
    assert.equal(unchanged.ok,true);assert.equal(unchanged.value.audio.volume,100);
    assert.equal(f.state.streamGain,156.00303649902344);assert.deepEqual(f.events,[]);
});

test('no-op mute and volume setters fail confirmation and are never retried',async()=>{
    for(const method of ['streamMute','streamVolume']){
        const f=await watchingFixture();let calls=0;
        f.audioActions[method==='streamMute'?'toggleLocalMute':'setLocalVolume']=()=>{calls++;};
        const result=await f.invoke(method,{...streamTarget,value:method==='streamMute'?true:75});
        assert.equal(result.code,'stream_audio_unconfirmed');assert.equal(calls,1);
        assert.equal(f.state.streamMuted,false);assert.equal(f.state.streamGain,18);
    }
});

test('unsupported media engine fails before calling actions but absent support getter remains compatible',async()=>{
    const f=await watchingFixture();f.stores.MediaEngineStore.isSupported=()=>false;
    assert.equal((await f.invoke('snapshot')).value.streams[0].audio.available,false);
    assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).code,'stream_audio_unavailable');
    assert.deepEqual(f.events,[]);
    delete f.stores.MediaEngineStore.isSupported;
    assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).value.audio.muted,true);
});

test('known provider default-volume snapping is confirmed without masking arbitrary no-ops',async()=>{
    const f=await watchingFixture(),setter=f.audioActions.setLocalVolume,defaultVolume=100*Math.pow(0.18,1/2.8);
    f.audioActions.setLocalVolume=(userId,gain,context)=>{
        const displayed=100*Math.pow(gain/100,1/2.8);
        setter(userId,Math.abs(displayed-defaultVolume)<1?18:gain,context);
    };
    const snap=await f.invoke('streamVolume',{...streamTarget,value:defaultVolume+0.5});
    assert.equal(snap.ok,true);assert.equal(snap.value.audio.volume,defaultVolume);
    f.audioActions.setLocalVolume=()=>{};
    assert.equal((await f.invoke('streamVolume',{...streamTarget,value:defaultVolume+1.01})).code,'stream_audio_unconfirmed');
    f.state.streamGain=100*Math.pow(0.75,2.8);
    assert.equal((await f.invoke('streamVolume',{...streamTarget,value:75.5})).code,'stream_audio_unconfirmed');
});

test('audio acknowledgement rejects domain changes during the write or confirmation read',async()=>{
    for(const during of ['write','read'])for(const change of [f=>f.state.account='3',f=>f.state.channel='21',
        f=>f.context.WidgetRailDiscordPrototype.dispose(),f=>f.state.viewers=[],f=>f.state.now+=26000]){
        const f=await watchingFixture(),setter=f.audioActions.toggleLocalMute,read=f.stores.MediaEngineStore.getLocalVolume;
        let written=false;
        f.audioActions.toggleLocalMute=(...args)=>{setter(...args);written=true;if(during==='write')change(f);};
        f.stores.MediaEngineStore.getLocalVolume=(...args)=>{const value=read(...args);if(written&&during==='read')change(f);return value;};
        const result=await f.invoke('streamMute',{...streamTarget,value:true});
        assert.equal(result.ok,false);assert.equal(result.value,undefined);
        assert.equal(f.events.filter(e=>Array.isArray(e)&&e[0]==='streamMute').length,1);
    }
});

test('audio acknowledgement rejects receiver replacement during the provider write',async()=>{
    const f=await watchingFixture(),setter=f.audioActions.toggleLocalMute;let replacing;
    f.audioActions.toggleLocalMute=(...args)=>{
        setter(...args);replacing=f.invoke('watch',{...streamTarget,lease:'b'.repeat(32)});
    };
    const result=await f.invoke('streamMute',{...streamTarget,value:true});await replacing;
    assert.equal(result.code,'watch_cancelled');assert.equal(result.value,undefined);
});

test('failed or malformed confirmation read does not claim a successful write',async()=>{
    for(const change of [f=>f.state.streamMuted=null,f=>f.state.streamGain=NaN,
        f=>f.stores.MediaEngineStore.getLocalVolume=()=>{throw Error('read unavailable');}]){
        const f=await watchingFixture(),setter=f.audioActions.toggleLocalMute;
        f.audioActions.toggleLocalMute=(...args)=>{setter(...args);change(f);};
        assert.equal((await f.invoke('streamMute',{...streamTarget,value:true})).code,'stream_audio_unconfirmed');
        assert.equal(f.events.filter(e=>Array.isArray(e)&&e[0]==='streamMute').length,1);
    }
});

function qualityFixture(){
    const f=fixture(),wp=f.context.Vencord.Webpack,findCode=wp.findByCode,findProps=wp.findByProps;
    f.state.qualityAvailable=true;f.state.entitled1080=false;f.state.guildTier=0;
    f.state.qualitySettings={preset:3,resolution:720,fps:30,soundshareEnabled:true};
    const reads=[],writes=[],presets={PRESET_CUSTOM:3,PRESET_AUTO:4};
    const api={
        canStream:(preset,resolution,fps,user,tier,channel)=>{
            reads.push({preset,resolution,fps,user,tier,channel});
            return resolution<=720&&fps<=30||f.state.entitled1080||tier>=2||resolution<=720&&tier>=1;
        },
        update:options=>{
            writes.push(JSON.parse(JSON.stringify(options)));
            f.state.qualitySettings={...f.state.qualitySettings,preset:options.preset,resolution:options.resolution,fps:options.frameRate};
        }
    };
    const listeners=new Set(),observed=[],emitter=new EventEmitter();
    // Discord651139.b.Connection is lower-case "connection". Use an actual
    // emitter, so wrong event names cannot be hidden by manually firing callbacks.
    f.engine.on=(event,fn)=>{emitter.on(event,fn);if(event==='connection')listeners.add(fn);};
    f.engine.removeListener=(event,fn)=>{emitter.removeListener(event,fn);if(event==='connection')listeners.delete(fn);};
    f.engine.connections.has=connection=>f.engine.connections.includes(connection);
    const source=f.engine.desktopInputPool.pool.capture;
    const sourceTrack={kind:'video',readyState:'live',getConstraints:()=>({width:{max:f.captureOptions.at(-1)?.width??1280},height:{max:f.captureOptions.at(-1)?.height??720}})};
    source.stream={getVideoTracks:()=>[sourceTrack]};
    f.stores.MediaEngineStore.getGoLiveSource=()=>f.state.goLiveSource;
    const getRtc=f.stores.StreamRTCConnectionStore.getRTCConnection;
    f.stores.StreamRTCConnectionStore.getRTCConnection=key=>key==='guild:10:20:1'?f.state.ownRtc:getRtc(key);
    const connect=()=>{
        const connection={context:'stream',userId:'1',streamUserId:'1',destroyed:false,
            videoStreamParameters:[{quality:100,maxFrameRate:30,maxResolution:{type:'fixed',width:1280,height:720},ssrc:12}],
            setStream(stream){
                const track=stream?.getVideoTracks?.()[0];
                if(track&&f.state.motionExperiment)track.contentHint='motion';
                this.streamVideoTrack=track;
                if(this.videoTransceiver)this.videoTransceiver.sender.track=track;
                f.state.duringStream?.(this);
            },
            setDesktopInput(input){
                f.state.beforeDesktop?.(this);
                if(f.state.providerBitrateReset){
                    const constraints=input.stream.getVideoTracks()[0].getConstraints();
                    const dimensions={width:constraints.width.max,height:constraints.height.max};
                    this.videoQualityManager.setGoliveQuality({capture:dimensions,encode:dimensions,bitrateMax:dimensions.height>720?9000000:3500000});
                }
                this.input={desktop:input};this.setStream(input.stream);observed.push(structuredClone({quality:this.videoQualityManager.goliveMaxQuality,parameters:this.videoStreamParameters}));
                return f.state.afterDesktop?.(this);
            }};
        connection.videoQualityManager={connection,contextType:'stream',goliveMaxQuality:{capture:{width:1280,height:720,framerate:30},encode:{width:1280,height:720,framerate:30}},
            setGoliveQuality(value){this.goliveMaxQuality=f.state.providerBitrateReset?
                {...this.goliveMaxQuality,...value,capture:{...this.goliveMaxQuality.capture,...value.capture},encode:{...this.goliveMaxQuality.encode,...value.encode}}:value;}};
        if(f.state.providerBitrateReset)Object.assign(connection.videoQualityManager.goliveMaxQuality,{bitrateMin:500000,bitrateMax:3500000,bitrateTarget:600000});
        f.state.configureConnection?.(connection);
        f.engine.connections.push(connection);
        emitter.emit('connection',connection);
        f.state.ownRtc={context:'stream',streamKey:'guild:10:20:1',_destroyed:false,isOwner:true,streamContext:{ownerId:'1',channelId:'20'},_connection:connection};
        if(!f.state.deferDesktop){const selected=f.engine.desktopInputPool.get('capture');if(selected)connection.setDesktopInput(selected);}
        return connection;
    };
    wp.findByCode=pattern=>pattern==='canStreamWithSettings'?(f.state.qualityAvailable?api.canStream:undefined):
        pattern==='"STREAM_UPDATE_SETTINGS"'?(f.state.qualityAvailable?api.update:undefined):
        pattern==='"STREAM_START",streamType'?()=>{findCode(pattern)();f.state.goLiveSource={desktopSource:{id:'capture'}};
            if(f.state.metadataAvailable)f.state.streamMetadata={sourceId:'capture'};
            connect();}:findCode(pattern);
    // Discord731854.ei is string-valued: FIXED="fixed", SOURCE="source".
    wp.findByProps=(...names)=>names.includes('SOURCE')?{SOURCE:'source',FIXED:'fixed'}:names.includes('PRESET_CUSTOM')?(f.state.qualityAvailable?presets:undefined):findProps(...names);
    f.stores.GuildStore.getGuild=id=>({id,premiumTier:f.state.guildTier});
    f.stores.ApplicationStreamingSettingsStore={getState:()=>f.state.qualitySettings};
    return {...f,qualityApi:api,qualityReads:reads,qualityWrites:writes,presets,source,sourceTrack,connect,qualityObserved:observed,listeners};
}
const hdQuality={resolution:1080,frameRate:30};

function encoderSdp(profile='42e01f',local=false){
    return ['v=0','o=- 123 2 IN IP4 127.0.0.1','s=-','t=0 0','a=group:BUNDLE audio0 video0 incoming0',
        'a=fingerprint:sha-256 synthetic-fingerprint',
        'm=audio 9 UDP/TLS/RTP/SAVPF 111','a=mid:audio0','a=sendrecv','a=rtpmap:111 opus/48000/2',
        'a=fmtp:111 minptime=10;useinbandfec=1;usedtx=0;stereo=1',
        'm=video 9 UDP/TLS/RTP/SAVPF 96 97','a=mid:video0',local?'a=sendrecv':'a=recvonly',
        'a=rtpmap:96 H264/90000','a=rtpmap:97 rtx/90000',
        'a=fmtp:96 x-google-max-bitrate=9000;level-asymmetry-allowed=1;packetization-mode=1;profile-level-id='+profile,
        'a=fmtp:97 apt=96','a=ssrc:123 cname:synthetic','a=ssrc-group:FID 123 124',
        'a=extmap:4 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time',
        'm=video 9 UDP/TLS/RTP/SAVPF 96 97','a=mid:incoming0',local?'a=recvonly':'a=sendonly',
        'a=rtpmap:96 H264/90000','a=fmtp:96 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f',
        'a=rtpmap:97 rtx/90000','a=fmtp:97 apt=96',''].join('\r\n');
}

async function encoderFixture(preference='prefer-hardware',{metadata=true,defer=false}={}){
    const f=qualityFixture();f.state.entitled1080=true;f.state.deferDesktop=defer;f.state.metadataAvailable=metadata;
    const calls=[];
    if(metadata)f.stores.ApplicationStreamingStore.getStreamerActiveStreamMetadataForStream=key=>key==='guild:10:20:1'?f.state.streamMetadata:null;
    f.state.configureConnection=connection=>{
        connection.videoCodec='H264';connection.videoPayloadType=96;
        connection.videoTransceiver={mid:'video0',sender:{track:null,getParameters:()=>({encodings:[{}]})}};
        connection.pc={signalingState:'have-local-offer',connectionState:'connected',localDescription:{type:'offer',sdp:encoderSdp('42001f',true)},remoteDescription:null,
            setRemoteDescription(description){
                calls.push({pc:this,description});
                if(f.state.remoteSetter)return f.state.remoteSetter(this,description);
                this.remoteDescription=description;this.signalingState='stable';return Promise.resolve();
            }};
        connection.getRawStats=async()=>new Map();connection.getStreamParameters=()=>connection.videoStreamParameters;
    };
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60},...(preference===undefined?{}:{encoderPreference:preference})});
    await settle();f.pending[0]('capture');await settle();
    const connection=f.state.ownRtc._connection,pc=connection.pc;
    const answer=sdp=>pc.setRemoteDescription({type:'answer',sdp:sdp??encoderSdp()});
    const nextOffer=()=>{pc.signalingState='have-local-offer';pc.localDescription={type:'offer',sdp:encoderSdp('42001f',true)};};
    const binding=async()=>{const result=await f.invoke('streamDiagnostics');assert.equal(result.ok,true);return result.value.attachment.qualityBinding;};
    return {...f,connection,pc,calls,answer,nextOffer,binding};
}

test('encoder preference rejects unknown policy or missing capture quality without opening a source',async()=>{
    for(const encoderPreference of [null,1,{},'hardware','PREFER-HARDWARE']){
        const f=qualityFixture();assert.equal((await f.invoke('share',{captureQuality:hdQuality,encoderPreference})).code,'share_encoder_unsupported');assert.equal(f.pending.length,0);
    }
    const f=qualityFixture();assert.equal((await f.invoke('share',{encoderPreference:'prefer-hardware'})).code,'share_encoder_unsupported');
});

test('Discord encoder policy leaves the provider answer and instance method unchanged',async()=>{
    const f=await encoderFixture('discord'),sdp=encoderSdp();
    assert.equal(f.pc.setRemoteDescription.name,'setRemoteDescription');await f.answer(sdp);
    assert.equal(f.calls[0].description.sdp,sdp);assert.equal(f.calls.length,1);
});

test('hardware preference changes exactly the offered outgoing H264 profile bytes',async()=>{
    const f=await encoderFixture(),sdp=encoderSdp();await f.answer(sdp);await settle();
    const expected=sdp.replace('profile-level-id=42e01f','profile-level-id=42001f');
    assert.equal(f.calls.length,1);assert.equal(f.calls[0].description.sdp,expected);
    assert.equal(f.pc.localDescription.sdp,encoderSdp('42001f',true),'The offer is never changed');
    assert.equal(f.calls[0].description.type,'answer');
    const info=await f.binding();assert.equal(info.requestedProfile,'42001f');assert.equal(info.appliedProfile,'42001f');
    assert.equal(info.encoderPreference,'prefer-hardware');assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
});

test('encoder answer correction accepts LF and preserves whitespace and unknown fmtp parameters byte for byte',async()=>{
    const f=await encoderFixture(),sdp=encoderSdp().replaceAll('\r\n','\n').replace('profile-level-id=42e01f','  profile-level-id = 42E01F ;x-test=preserved');
    await f.answer(sdp);assert.equal(f.calls[0].description.sdp,sdp.replace('42E01F','42001f'));
});

test('encoder correction snapshots description getters once and revalidates after reentrant changes',async()=>{
    const stable=await encoderFixture();let typeReads=0,sdpReads=0;
    await stable.pc.setRemoteDescription({get type(){typeReads++;return 'answer';},get sdp(){sdpReads++;return encoderSdp();}});
    await settle();assert.equal(typeReads,1);assert.equal(sdpReads,1);assert.equal((await stable.binding()).appliedProfile,'42001f');
    for(const change of ['metadata','signaling','codec','mid','payload','offer']){
        const f=await encoderFixture();
        const description={type:'answer',get sdp(){
            if(change==='metadata')f.state.streamMetadata={sourceId:'capture'};
            if(change==='signaling')f.pc.signalingState='stable';
            if(change==='codec')f.connection.videoCodec='VP8';
            if(change==='mid')f.connection.videoTransceiver.mid='other';
            if(change==='payload')f.connection.videoPayloadType=99;
            if(change==='offer')f.pc.localDescription={type:'offer',sdp:encoderSdp('420020',true)};
            return encoderSdp();
        }};
        await f.pc.setRemoteDescription(description);await settle();
        assert.equal(f.calls.length,1,change);assert.equal(f.calls[0].description,description,change);
    }
});

test('encoder answer correction refuses ambiguous malformed and unsupported SDP boundaries',async()=>{
    const cases=[
        s=>s.replace('a=mid:incoming0','a=mid:video0'),
        s=>s.replace('a=mid:video0','a=mid:video0\r\na=mid:video0'),
        s=>s.replace('a=rtpmap:96 H264/90000','a=rtpmap:96 H264/90000\r\na=rtpmap:96 H264/90000'),
        s=>s.replace('a=rtpmap:96 H264/90000','a=rtpmap:96 H264/90000\r\na=rtpmap:96\tH264/90000'),
        s=>s.replace('a=fmtp:96 x-google','a=fmtp:96 profile-level-id=42e01f\r\na=fmtp:96 x-google'),
        s=>s.replace('a=fmtp:96 x-google','a=fmtp:96\tprofile-level-id=42e01f\r\na=fmtp:96 x-google'),
        s=>s.replace('profile-level-id=42e01f','profile-level-id=42e01f;profile-level-id=42e01f'),
        s=>s.replace('packetization-mode=1','packetization-mode=0'),
        s=>s.replace('level-asymmetry-allowed=1','level-asymmetry-allowed=0'),
        s=>s.replace('profile-level-id=42e01f','profile-level-id=42e020'),
        s=>s.replace('profile-level-id=42e01f','profile-level-id=42e0ff'),
        s=>s.replace('profile-level-id=42e01f','profile-level-id=4d001f'),
        s=>s.replace('profile-level-id=42e01f','profile-level-id=42e01f;invalid'),
        s=>s.replace('a=rtpmap:96 H264/90000','a=rtpmap:96 VP8/90000'),
        s=>s.replace('a=recvonly','a=inactive'),
        s=>s.replace('a=recvonly','a=recvonly\r\na=sendrecv'),
        s=>s.replace('m=video 9 UDP/TLS/RTP/SAVPF 96 97','m=video 0 UDP/TLS/RTP/SAVPF 96 97'),
        s=>s.replace('m=video 9 UDP/TLS/RTP/SAVPF 96 97','m=video 9 UDP/TLS/RTP/SAVPF 96 96 97'),
        s=>s.replace('a=mid:video0','a=mid:video\r0'),
        s=>s+'a=x:'+ 'x'.repeat(65536)
    ];
    for(const mutate of cases){const f=await encoderFixture(),sdp=mutate(encoderSdp());await f.answer(sdp);await settle();
        assert.equal(f.calls.length,1);assert.equal(f.calls[0].description.sdp,sdp);
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_encoder_unconfirmed');assert.equal((await f.binding()).appliedProfile,null);}
});

test('local offer must already contain the exact Baseline payload level and packetization',async()=>{
    for(const mutate of [s=>s.replace('profile-level-id=42001f','profile-level-id=42e01f'),s=>s.replace('profile-level-id=42001f','profile-level-id=420020'),
        s=>s.replace('packetization-mode=1','packetization-mode=0'),s=>s.replace('a=mid:video0','a=mid:other'),s=>s.replace('a=rtpmap:96 H264/90000','a=rtpmap:96 VP9/90000')]){
        const f=await encoderFixture();f.pc.localDescription.sdp=mutate(f.pc.localDescription.sdp);const sdp=encoderSdp();await f.answer(sdp);
        assert.equal(f.calls[0].description.sdp,sdp);assert.equal(f.calls.length,1);
    }
});

test('unsupported state type codec MID payload or missing metadata preserves provider negotiation',async()=>{
    for(const change of ['offer','stable','codec','mid','payload','metadata']){
        const f=await encoderFixture('prefer-hardware',{metadata:change!=='metadata'});
        if(change==='stable')f.pc.signalingState='stable';if(change==='codec')f.connection.videoCodec='VP9';
        if(change==='mid')f.connection.videoTransceiver.mid='absent';if(change==='payload')f.connection.videoPayloadType=98;
        const description={type:change==='offer'?'offer':'answer',sdp:encoderSdp()};await f.pc.setRemoteDescription(description);
        assert.equal(f.calls[0].description,description,change);assert.equal(f.calls.length,1);
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_encoder_unconfirmed',change);
    }
});

test('setter rejection and synchronous throws never retry a changed negotiation',async()=>{
    for(const synchronous of [true,false]){
        const f=await encoderFixture();const failure=Error('native-setter-rejected');
        f.state.remoteSetter=()=>{if(synchronous)throw failure;return Promise.reject(failure);};
        if(synchronous)assert.throws(()=>f.answer(),e=>e===failure);else await assert.rejects(f.answer(),e=>e===failure);
        await settle();assert.equal(f.calls.length,1);assert.equal((await f.binding()).appliedProfile,null);
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_encoder_unconfirmed');
    }
});

test('ordinary stream UI replacement preserves the quality bandwidth and content owners',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.state.metadataAvailable=true;f.state.providerBitrateReset=true;f.state.motionExperiment=true;
    f.stores.ApplicationStreamingStore.getStreamerActiveStreamMetadataForStream=()=>f.state.streamMetadata;
    f.sourceTrack.contentHint='detail';f.context.WidgetRailNativeMedia={status:()=>({active:true,contentPreference:'detail'})};
    await f.invoke('share',{captureQuality:hdQuality,bandwidthPreference:'high-detail'});await settle();f.pending[0]('capture');await settle();
    const connection=f.state.ownRtc._connection;
    for(const state of ['ACTIVE','PAUSED','ACTIVE']){
        f.state.share={...f.state.share,state};await f.invoke('snapshot');connection.setStream(f.source.stream);
        assert.equal(f.sourceTrack.contentHint,'detail',state);assert.equal(f.listeners.size,1);assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,18000000);
    }
    f.state.streamMetadata=null;await f.invoke('snapshot');assert.equal(f.listeners.size,0);connection.setStream(f.source.stream);assert.equal(f.sourceTrack.contentHint,'motion');
});

test('same-key metadata restart retires all prior source hooks even when the source ID is recycled',async()=>{
    const f=await encoderFixture();const held=f.pc.setRemoteDescription;f.state.streamMetadata={sourceId:'capture'};f.state.share={...f.state.share};
    const description={type:'answer',sdp:encoderSdp()};await held.call(f.pc,description);
    assert.equal(f.calls[0].description,description);await f.invoke('snapshot');assert.equal(f.listeners.size,0);
    assert.equal(f.pc.setRemoteDescription.name,'setRemoteDescription');assert.equal((await f.binding()),null);
});

test('async profile observation cannot publish after metadata or source retirement',async()=>{
    for(const change of ['metadata','source','account','call','stop','pc','sender']){
        const f=await encoderFixture();let finish;f.state.remoteSetter=(pc,description)=>new Promise(resolve=>finish=()=>{pc.remoteDescription=description;pc.signalingState='stable';resolve();});
        const pending=f.answer();
        if(change==='metadata')f.state.streamMetadata={sourceId:'capture'};
        if(change==='source')f.engine.desktopInputPool.set('capture',{});
        if(change==='account')f.state.account='other';if(change==='call')f.state.channel='other';
        if(change==='stop')await f.invoke('stopShare');if(change==='pc')f.connection.pc={};
        if(change==='sender')f.connection.videoTransceiver.sender={track:f.sourceTrack};
        finish();await pending;await settle();assert.equal(f.calls.length,1,change);
        if(['pc','sender'].includes(change))assert.equal((await f.binding()).appliedProfile,null,change);
        else {await f.invoke('snapshot');assert.equal(f.listeners.size,0,change);}
    }
});

test('same-broadcast reconnection installs a new exact-PC hook and retires the old one',async()=>{
    const f=await encoderFixture(),old=f.pc,held=old.setRemoteDescription;
    f.state.deferDesktop=true;const replacement=f.connect();replacement.setDesktopInput(f.source);
    await replacement.pc.setRemoteDescription({type:'answer',sdp:encoderSdp()});await settle();
    assert.equal(f.calls.at(-1).description.sdp,encoderSdp().replace('profile-level-id=42e01f','profile-level-id=42001f'));
    const oldDescription={type:'answer',sdp:encoderSdp()};old.signalingState='have-local-offer';await held.call(old,oldDescription);
    assert.equal(f.calls.at(-1).description,oldDescription);assert.equal(old.setRemoteDescription.name,'setRemoteDescription');
    assert.equal((await f.binding()).appliedProfile,'42001f','A stale old PC cannot erase the new PC observation');
});

test('encoder hooks never affect another PC and preserve provider method replacements on teardown',async()=>{
    const f=await encoderFixture(),held=f.pc.setRemoteDescription,description={type:'answer',sdp:encoderSdp()};
    const other={signalingState:'have-local-offer'};await held.call(other,description);assert.equal(f.calls[0].pc,other);assert.equal(f.calls[0].description,description);
    const replacement=()=>Promise.resolve();f.pc.setRemoteDescription=replacement;await f.invoke('stopShare');assert.equal(f.pc.setRemoteDescription,replacement);
});

test('same-broadcast renegotiation rechecks current offer and confirms only the latest answer',async()=>{
    const f=await encoderFixture();await f.answer();await settle();assert.equal((await f.binding()).appliedProfile,'42001f');
    f.nextOffer();f.pc.localDescription.sdp=f.pc.localDescription.sdp.replace('profile-level-id=42001f','profile-level-id=42e01f');
    await f.answer();await settle();assert.equal((await f.binding()).appliedProfile,null);assert.equal(f.calls.length,2);
    f.nextOffer();await f.answer();await settle();assert.equal((await f.binding()).appliedProfile,'42001f');assert.equal(f.calls.length,3);
});

test('profile diagnostics clear immediately when the observed PC or broadcast incarnation changes',async()=>{
    for(const change of ['metadata','pc','sender']){
        const f=await encoderFixture();await f.answer();await settle();assert.equal((await f.binding()).appliedProfile,'42001f');
        if(change==='metadata')f.state.streamMetadata={sourceId:'capture'};
        if(change==='pc')f.connection.pc={};
        if(change==='sender')f.connection.videoTransceiver.sender={track:f.sourceTrack};
        const info=await f.binding();
        if(change==='metadata')assert.equal(info,null);else {assert.equal(info.appliedProfile,null);assert.equal(info.requestedProfile,null);}
    }
});

async function bandwidthFixture(preference='high-detail',defer=false){
    const f=qualityFixture();f.state.entitled1080=true;f.state.providerBitrateReset=true;f.state.deferDesktop=defer;
    assert.equal((await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60},...(preference===undefined?{}:{bandwidthPreference:preference})})).ok,true);
    await settle();f.pending[0]('capture');await settle();return f;
}

test('bandwidth preference rejects unknown values and unqualified sources before capture',async()=>{
    for(const value of [null,18,{},'18mbps','HIGH-DETAIL']){
        const f=qualityFixture();assert.equal((await f.invoke('share',{captureQuality:hdQuality,bandwidthPreference:value})).code,'share_bandwidth_unsupported');
        assert.equal(f.pending.length,0);
    }
    const f=qualityFixture();assert.equal((await f.invoke('share',{bandwidthPreference:'high-detail'})).code,'share_bandwidth_unsupported');
});

test('Automatic keeps the provider ceiling and restores its ordinary attachment method',async()=>{
    const f=await bandwidthFixture('automatic'),connection=f.state.ownRtc._connection;
    assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,9000000);
    assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMin,500000);
    assert.equal(connection.setDesktopInput.name,'setDesktopInput');
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
});

test('high-detail changes only the exact provider source ceiling before downstream quality application',async()=>{
    const f=await bandwidthFixture(),connection=f.state.ownRtc._connection,manager=connection.videoQualityManager;
    assert.equal(manager.goliveMaxQuality.bitrateMax,18000000);
    assert.equal(manager.goliveMaxQuality.bitrateMin,500000);assert.equal(manager.goliveMaxQuality.bitrateTarget,600000);
    assert.equal(f.qualityObserved[0].quality.bitrateMax,18000000,'Provider downstream application sees the selected ceiling');
    assert.equal(manager.setGoliveQuality.name,'setGoliveQuality','Manager method is immediately restored');
    manager.setGoliveQuality({bitrateMax:7000000});assert.equal(manager.goliveMaxQuality.bitrateMax,7000000,'Unrelated policy writes remain unchanged');
    connection.setDesktopInput(f.source);assert.equal(manager.goliveMaxQuality.bitrateMax,18000000,'Same source reattachment retains the selected preference');
    await f.invoke('stopShare');assert.equal(connection.setDesktopInput.name,'setDesktopInput');assert.equal(f.listeners.size,0);
});

test('high-detail cannot apply premium resolution when the provider denies it',async()=>{
    const f=qualityFixture();assert.equal((await f.invoke('share',{captureQuality:hdQuality,bandwidthPreference:'high-detail'})).code,'share_quality_unsupported');
    assert.equal(f.pending.length,0);
});

test('high-detail source policy retires on changed account call source stream and provider replacement',async()=>{
    for(const change of ['account','call','source','stream','manager','rtc','stop','dispose']){
        const f=await bandwidthFixture(),connection=f.state.ownRtc._connection,held=connection.setDesktopInput;
        if(change==='account')f.state.account='other';
        if(change==='call')f.state.channel='other';
        if(change==='source')f.engine.desktopInputPool.set('capture',{});
        if(change==='stream')f.state.share={...f.state.share};
        if(change==='manager')connection.videoQualityManager={...connection.videoQualityManager};
        if(change==='rtc')f.state.ownRtc={...f.state.ownRtc};
        if(change==='stop')await f.invoke('stopShare');
        if(change==='dispose')f.context.WidgetRailDiscordPrototype.dispose();
        held.call(connection,f.source);
        assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,9000000,change);
        assert.equal(connection.setDesktopInput.name,'setDesktopInput',change);
        assert.equal(f.listeners.size,0,change);
    }
});

test('another source on the same connection retires high-detail without changing its ceiling',async()=>{
    const f=await bandwidthFixture(),connection=f.state.ownRtc._connection;
    const foreign={stream:{getVideoTracks:()=>[{kind:'video',getConstraints:()=>({width:{max:1280},height:{max:720}})}]}};
    connection.setDesktopInput(foreign);assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,3500000);
    assert.equal(connection.setDesktopInput.name,'setDesktopInput');assert.equal(f.listeners.size,0);
});

test('authority loss during original attachment never changes the provider rate',async()=>{
    for(const change of ['account','call','manager','source']){
        const f=await bandwidthFixture('high-detail',true),connection=f.state.ownRtc._connection;
        f.state.beforeDesktop=()=>{
            if(change==='account')f.state.account='other';
            if(change==='call')f.state.channel='other';
            if(change==='manager')connection.videoQualityManager={...connection.videoQualityManager};
            if(change==='source')f.engine.desktopInputPool.set('capture',{});
        };
        connection.setDesktopInput(f.source);assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,9000000,change);
    }
});

test('unwrappable or ignored provider ceiling warns without breaking the original capture',async()=>{
    for(const mode of ['frozen','ignored','async']){
        const f=await bandwidthFixture('high-detail',true),connection=f.state.ownRtc._connection,manager=connection.videoQualityManager;
        if(mode==='frozen')Object.defineProperty(manager,'setGoliveQuality',{value:manager.setGoliveQuality,configurable:false,writable:false});
        if(mode==='ignored'){
            const original=manager.setGoliveQuality;
            manager.setGoliveQuality=function(value){return original.call(this,value.bitrateMax===18000000?{...value,bitrateMax:9000000}:value);};
        }
        if(mode==='async')f.state.afterDesktop=()=>Promise.resolve();
        const original=manager.setGoliveQuality;
        connection.setDesktopInput(f.source);
        assert.equal(connection.input.desktop,f.source,mode);assert.equal(manager.setGoliveQuality,original,mode);
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_bandwidth_unconfirmed',mode);
    }
});

test('throwing provider attachment restores its manager and never retries the source',async()=>{
    const f=await bandwidthFixture('high-detail',true),connection=f.state.ownRtc._connection,manager=connection.videoQualityManager;
    const original=manager.setGoliveQuality;let calls=0;
    f.state.beforeDesktop=()=>{calls++;throw Error('provider-failure');};
    assert.throws(()=>connection.setDesktopInput(f.source),/provider-failure/);
    assert.equal(calls,1);assert.equal(manager.setGoliveQuality,original);
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_bandwidth_unconfirmed');
});

test('bandwidth hook cleanup preserves later provider method replacements',async()=>{
    const f=await bandwidthFixture(),connection=f.state.ownRtc._connection,manager=connection.videoQualityManager;
    const replacement=()=>{};f.state.afterDesktop=()=>{manager.setGoliveQuality=replacement;};
    connection.setDesktopInput(f.source);assert.equal(manager.setGoliveQuality,replacement);
    connection.setDesktopInput=replacement;await f.invoke('stopShare');assert.equal(connection.setDesktopInput,replacement);
});

test('later provider ceiling changes are reported without fighting adaptive or replacement policy',async()=>{
    for(const change of ['rate','manager','rtc']){
        const f=await bandwidthFixture(),connection=f.state.ownRtc._connection;
        if(change==='rate')connection.videoQualityManager.setGoliveQuality({bitrateMax:6000000});
        if(change==='manager')connection.videoQualityManager={...connection.videoQualityManager};
        if(change==='rtc')f.state.ownRtc={...f.state.ownRtc};
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_bandwidth_unconfirmed',change);
        if(change==='rate')assert.equal(connection.videoQualityManager.goliveMaxQuality.bitrateMax,6000000);
        assert.equal(f.state.started,1,'A failed readback does not restart sharing');
    }
});

test('quality inspection leaves the source for the provider single ownership transfer',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.state.poolTakes,1,'Only Discord takes the source');
    assert.equal(f.engine.desktopInputPool.pool.capture,undefined,'Discord consumed its queued source');
    assert.equal(f.state.ownRtc._connection.input.desktop,f.source);
    for(let i=0;i<3;i++)assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
    assert.equal(f.state.poolTakes,1,'Snapshot ownership checks cannot consume a source');
});

test('optional quality warnings never take the source and survive its normal handoff',async()=>{
    const f=qualityFixture();f.engine.on=undefined;
    await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.state.ownRtc._connection.input.desktop,f.source);
    for(let i=0;i<3;i++)assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_quality_unavailable');
    assert.equal(f.state.poolTakes,1);
});

test('quality discovery delegates account and guild eligibility to the exact provider predicate',async()=>{
    const f=qualityFixture();let snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualityAvailability,'ready');assert.equal(snapshot.shareQualities.length,4);
    const read=f.qualityReads.find(q=>q.resolution===1080&&q.fps===30);
    assert.equal(read.preset,3);assert.equal(read.resolution,1080);assert.equal(read.fps,30);
    assert.equal(read.user.id,'1');assert.equal(read.tier,0);assert.equal(read.channel.id,'20');
    f.state.entitled1080=true;snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualities.length,12);
    assert.deepEqual(JSON.parse(JSON.stringify(snapshot.shareQualities.at(-1))),{resolution:1440,frameRate:60});
    f.state.entitled1080=false;f.state.guildTier=2;
    assert.equal((await f.invoke('snapshot')).value.shareQualities.length,12);
    f.state.guildTier=1;assert.equal((await f.invoke('snapshot')).value.shareQualities.length,6);
    assert.deepEqual(f.qualityWrites,[]);assert.deepEqual(f.events,[]);
});

test('missing or malformed quality discovery retains baseline without guessing premium eligibility',async()=>{
    for(const change of [f=>f.state.qualityAvailable=false,f=>f.qualityApi.canStream=undefined,
        f=>f.qualityApi.update=undefined,f=>delete f.stores.ApplicationStreamingSettingsStore,
        f=>f.presets.PRESET_CUSTOM='3',f=>f.qualityApi.canStream=()=>1,
        f=>f.qualityApi.canStream=()=>{throw Error('provider unavailable');}]){
        const f=qualityFixture();f.state.entitled1080=true;change(f);
        const snapshot=(await f.invoke('snapshot')).value;
        assert.equal(snapshot.shareQualityAvailability,'unavailable');assert.equal(snapshot.shareQualities.length,4);
        assert.equal((await f.invoke('share',{captureQuality:hdQuality})).code,'share_quality_unsupported');
        assert.equal(f.pending.length,0);assert.deepEqual(f.qualityWrites,[]);
    }
});

test('quality discovery retries missing loaded APIs without mounting provider UI or caching failure',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.state.qualityAvailable=false;
    assert.equal((await f.invoke('snapshot')).value.shareQualities.length,4);
    f.state.qualityAvailable=true;assert.equal((await f.invoke('snapshot')).value.shareQualities.length,12);
    assert.deepEqual(f.events,[]);assert.deepEqual(f.qualityWrites,[]);
});

test('only bounded resolutions and frame rates are considered even for eligible accounts',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    for(const captureQuality of [{resolution:2160,frameRate:30},{resolution:0,frameRate:30},
        {resolution:1080,frameRate:120},{resolution:1440,frameRate:5},{resolution:1600,frameRate:30}])
        assert.equal((await f.invoke('share',{captureQuality})).code,'share_quality_unsupported');
    assert.equal(f.pending.length,0);assert.deepEqual(f.qualityWrites,[]);
});

test('all twelve custom quality pairs are advertised and applied only when provider permits them',async()=>{
    for(const resolution of [480,720,1080,1440])for(const frameRate of [15,30,60]){
        const f=qualityFixture();f.state.entitled1080=true;
        assert.equal((await f.invoke('share',{captureQuality:{resolution,frameRate}})).ok,true);await settle();
        f.pending[0]('capture');await settle();
        assert.deepEqual(f.qualityWrites,[{preset:3,resolution,frameRate}]);assert.equal(f.state.started,1);
    }
});

test('saved custom preference comes from provider persisted settings and survives restricted calls',async()=>{
    const f=qualityFixture();f.state.qualitySettings={preset:3,resolution:1440,fps:60,soundshareEnabled:true};
    let snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualities.length,4);
    assert.deepEqual(JSON.parse(JSON.stringify(snapshot.preferredShareQuality)),{resolution:1440,frameRate:60});
    f.state.entitled1080=true;snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualities.length,12);assert.equal(snapshot.preferredShareQuality.resolution,1440);
    assert.deepEqual(f.qualityWrites,[]);
    // Adapter recreation has no private copy of this preference to restore.
    const previous=f.context.WidgetRailDiscordPrototype;
    vm.runInContext(`initializeWidgetRailDiscordAdapter(${build+1});`,f.context);
    assert.notEqual(f.context.WidgetRailDiscordPrototype,previous);
    assert.deepEqual(JSON.parse(JSON.stringify((await f.invoke('snapshot')).value.preferredShareQuality)),{resolution:1440,frameRate:60});
    for(const saved of [null,{}, {preset:1,resolution:1080,fps:60},{preset:3,resolution:2160,fps:60},{preset:3,resolution:720,fps:120}]){
        f.state.qualitySettings=saved;assert.equal((await f.invoke('snapshot')).value.preferredShareQuality,null);
    }
});

test('eligible1080p30 configures and verifies provider encoder settings only after source choice',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    assert.equal((await f.invoke('share',{captureQuality:hdQuality})).ok,true);await settle();
    assert.deepEqual(JSON.parse(JSON.stringify(f.captureOptions[0])),{width:1920,height:1080,frameRate:30});
    assert.deepEqual(f.qualityWrites,[]);assert.equal(f.state.started,0);
    f.pending[0]('capture');await settle();
    assert.deepEqual(f.qualityWrites,[{preset:3,resolution:1080,frameRate:30}]);
    assert.deepEqual(f.state.qualitySettings,{preset:3,resolution:1080,fps:30,soundshareEnabled:true});
    assert.equal(f.state.started,1);assert.equal(f.state.destroyed,0);
});

test('explicit baseline qualities also update provider settings when the concrete API is available',async()=>{
    const f=qualityFixture();await f.invoke('share',{captureQuality:{resolution:480,frameRate:15}});await settle();
    f.pending[0]('capture');await settle();
    assert.deepEqual(f.qualityWrites,[{preset:3,resolution:480,frameRate:15}]);assert.equal(f.state.started,1);
});

test('legacy omitted quality preserves settings and source request even when higher options are available',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    await f.invoke('share');await settle();f.pending[0]('capture');await settle();
    assert.deepEqual(JSON.parse(JSON.stringify(f.captureOptions[0])),{width:1280,height:720});
    assert.deepEqual(f.qualityWrites,[]);assert.equal(f.state.started,1);
});

test('free-account1080 request fails before chooser and cannot authorize itself via request fields',async()=>{
    const f=qualityFixture();
    assert.equal((await f.invoke('share',{captureQuality:hdQuality,premium:true,guildTier:3})).code,'share_quality_unsupported');
    assert.equal(f.pending.length,0);assert.deepEqual(f.qualityWrites,[]);
});

test('entitlement loss while chooser is open prevents quality mutation and start and releases acquired source',async()=>{
    for(const change of [f=>f.state.entitled1080=false,f=>f.state.qualityAvailable=false]){
        const f=qualityFixture();f.state.entitled1080=true;
        await f.invoke('share',{captureQuality:hdQuality});await settle();change(f);f.pending[0]('capture');await settle();
        assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);assert.deepEqual(f.qualityWrites,[]);
        assert.equal((await f.invoke('snapshot')).value.shareError,'share_quality_unsupported');
    }
});

test('provider settings no-op or malformed confirmation cannot start1080 capture',async()=>{
    for(const change of [f=>f.qualityApi.update=()=>{},f=>f.stores.ApplicationStreamingSettingsStore.getState=()=>null]){
        const f=qualityFixture();f.state.entitled1080=true;change(f);
        await f.invoke('share',{captureQuality:hdQuality});await settle();f.pending[0]('capture');await settle();
        assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);
        assert.equal((await f.invoke('snapshot')).value.shareError,'share_quality_unconfirmed');
    }
});

test('quality callbacks changing account call or entitlement cannot publish an outgoing stream',async()=>{
    for(const change of [f=>f.state.account='3',f=>f.state.channel='21',f=>f.state.entitled1080=false]){
        const f=qualityFixture();f.state.entitled1080=true;const update=f.qualityApi.update;
        f.qualityApi.update=options=>{update(options);change(f);};
        await f.invoke('share',{captureQuality:hdQuality});await settle();f.pending[0]('capture');await settle();
        assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);assert.equal(f.qualityWrites.length,1);
    }
});

test('late source after cancellation never changes provider quality preferences',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    await f.invoke('share',{captureQuality:hdQuality});await settle();await f.invoke('cancelPendingShare');
    f.pending[0]('capture');await settle();assert.deepEqual(f.qualityWrites,[]);assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);
});

test('a user-started broadcast during chooser is preserved without changing its quality',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    await f.invoke('share',{captureQuality:hdQuality});await settle();
    const own=f.state.share={channelId:'20',guildId:'10',ownerId:'1'};
    f.pending[0]('capture');await settle();assert.equal(f.state.share,own);
    assert.deepEqual(f.qualityWrites,[]);assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);
});

test('account change inside eligibility getter never leaks a higher-quality allowance',async()=>{
    const f=qualityFixture();f.qualityApi.canStream=()=>{f.state.account='3';return true;};
    const snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualityAvailability,'unavailable');assert.equal(snapshot.shareQualities.length,4);
    assert.deepEqual(f.qualityWrites,[]);
});

function diagnosticsFixture(){
    const f=fixture(),own={channelId:'20',guildId:'10',ownerId:'1',state:'LIVE'};
    f.state.share=own;let reads=0;
    const reports=new Map([
        ['outgoing',{id:'private-stats-id',type:'outbound-rtp',kind:'video',frameWidth:1920,frameHeight:1080,framesPerSecond:59.5,
            qualityLimitationReason:'none',trackIdentifier:'private-track',remoteId:'private-remote',codecId:'private-codec'}],
        ['incoming',{type:'inbound-rtp',kind:'video',frameWidth:640,frameHeight:360,framesPerSecond:30}],
        ['audio',{type:'outbound-rtp',kind:'audio',bytesSent:999}],
        ['candidate',{type:'local-candidate',address:'private-address',url:'private-url'}]
    ]);
    const parameters=[{ssrc:123,sourceId:'private-source',rid:'private-rid',maxResolution:{type:'fixed',width:1280,height:720},maxFrameRate:60}];
    const connection={context:'stream',userId:'1',streamUserId:'1',getRawStats(){reads++;return Promise.resolve(reports);},getStreamParameters:()=>parameters};
    const rtc={context:'stream',streamKey:'guild:10:20:1',streamContext:{ownerId:'1',channelId:'20'},_destroyed:false,isOwner:true,_connection:connection};
    f.stores.StreamRTCConnectionStore.getRTCConnection=key=>key==='guild:10:20:1'?rtc:f.state.rtc;
    f.engine.connections=new Set([...f.engine.connections,connection]);
    f.context.WidgetRailNativeMedia={status:()=>({active:true,width:2560,height:1440,browserWidth:1920,browserHeight:1080,targetFrameRate:60,
        sourceId:'private-native-source',token:'private-token'})};
    return {...f,own,rtc,connection,reports,parameters,readCount:()=>reads};
}
const unavailableAttachment={nativeFrames:null,videoSupported:null,inputTrack:null,desktopTrack:null,senderTrack:null,peerState:null,signalingState:null,qualityBinding:null,qualityCheck:null};

test('stream diagnostics measure actual counter deltas without exporting private stats identities',async()=>{
    const f=diagnosticsFixture(),r=f.reports.get('outgoing');
    Object.assign(r,{timestamp:1000,framesEncoded:100,framesSent:98,bytesSent:1000000,totalEncodeTime:1,qpSum:3000,
        encoderImplementation:'SimulcastEncoderAdapter (OpenH264)',powerEfficientEncoder:false});
    assert.equal((await f.invoke('streamDiagnostics')).value.outbound[0].measurement,undefined);
    Object.assign(r,{timestamp:2000,framesEncoded:124,framesSent:122,bytesSent:2000000,totalEncodeTime:1.72,qpSum:3840});
    const result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.measurement.encodedFps,24);assert.equal(result.measurement.sentFps,24);
    assert.equal(result.measurement.bitrateMbps,8);assert.equal(result.measurement.meanQp,35);
    assert.ok(Math.abs(result.measurement.meanEncodeMs-30)<.0001);
    assert.equal(result.encoderImplementation,'OpenH264');assert.equal(result.powerEfficientEncoder,false);
    assert.equal(JSON.stringify(result).includes('private'),false);
});

test('diagnostic samples survive ordinary stream records but never span metadata incarnations',async()=>{
    const f=diagnosticsFixture(),r=f.reports.get('outgoing');let metadata={sourceId:'capture'};
    f.stores.ApplicationStreamingStore.getStreamerActiveStreamMetadataForStream=()=>metadata;
    f.stores.MediaEngineStore.getGoLiveSource=()=>({desktopSource:{id:'capture'}});
    Object.assign(r,{timestamp:1000,framesEncoded:100,bytesSent:1000});await f.invoke('streamDiagnostics');
    f.state.share={...f.own};Object.assign(r,{timestamp:2000,framesEncoded:160,bytesSent:2000});
    assert.equal((await f.invoke('streamDiagnostics')).value.outbound[0].measurement.encodedFps,60);
    metadata={sourceId:'capture'};Object.assign(r,{timestamp:3000,framesEncoded:220,bytesSent:3000});
    assert.equal((await f.invoke('streamDiagnostics')).value.outbound[0].measurement,undefined);
    f.connection.getRawStats=async()=>{f.state.share={...f.state.share};return f.reports;};
    assert.equal((await f.invoke('streamDiagnostics')).ok,true);
    f.connection.getRawStats=async()=>{metadata={sourceId:'capture'};return f.reports;};
    assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_stale');
});

test('diagnostics expose only bounded negotiated H264 parameters, and profile changes reset comparison',async()=>{
    const f=diagnosticsFixture(),r=f.reports.get('outgoing');
    f.reports.set('private-codec',{type:'codec',mimeType:'video/H264',sdpFmtpLine:'private=secret;profile-level-id=42e01f;packetization-mode=1'});
    Object.assign(r,{timestamp:1000,framesEncoded:100,bytesSent:1000});
    let result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.h264Profile,'42e01f');assert.equal(result.packetizationMode,1);assert.equal(JSON.stringify(result).includes('secret'),false);
    f.reports.get('private-codec').sdpFmtpLine='profile-level-id=42001f;packetization-mode=1';
    Object.assign(r,{timestamp:2000,framesEncoded:160,bytesSent:2000});
    result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.h264Profile,'42001f');assert.equal(result.measurement,undefined);
    f.reports.get('private-codec').sdpFmtpLine='profile-level-id=private;packetization-mode=8';
    result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.h264Profile,undefined);assert.equal(result.packetizationMode,undefined);
    f.reports.get('private-codec').sdpFmtpLine='profile-level-id=42e01f;profile-level-id=42001f;packetization-mode=1;packetization-mode=0';
    result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.h264Profile,undefined);assert.equal(result.packetizationMode,undefined);
});

test('diagnostic intervals reject reset counters, encoder changes and replacement streams',async()=>{
    for(const change of ['reset','geometry','codec','identity','stream','clock','too-long']){
        const f=diagnosticsFixture(),r=f.reports.get('outgoing');
        Object.assign(r,{timestamp:1000,framesEncoded:100,framesSent:100,bytesSent:1000,totalEncodeTime:1,qpSum:3000});
        await f.invoke('streamDiagnostics');
        Object.assign(r,{timestamp:2000,framesEncoded:160,framesSent:160,bytesSent:2000,totalEncodeTime:2,qpSum:4200});
        if(change==='reset')Object.assign(r,{framesEncoded:1,framesSent:1,bytesSent:1,totalEncodeTime:0,qpSum:1});
        if(change==='geometry')r.frameWidth=640;
        if(change==='codec')f.reports.set('private-codec',{type:'codec',mimeType:'video/H264'});
        if(change==='identity')r.id='new';
        if(change==='stream')f.state.share={...f.own};
        if(change==='clock')r.timestamp=999;
        if(change==='too-long')r.timestamp=32000;
        const result=(await f.invoke('streamDiagnostics')).value.outbound[0];
        if(change==='reset')assert.deepEqual(JSON.parse(JSON.stringify(result.measurement)),
            {milliseconds:1000,encodedFps:null,sentFps:null,bitrateMbps:null,meanEncodeMs:null,meanQp:null});
        else assert.equal(result.measurement,undefined,change);
    }
});

test('diagnostics expose bounded transport observations and never turn an unknown encoder into hardware evidence',async()=>{
    const f=diagnosticsFixture(),r=f.reports.get('outgoing');
    r.encoderImplementation='private';r.powerEfficientEncoder='true';
    f.reports.set('private-remote',{type:'remote-inbound-rtp',kind:'video',roundTripTime:.032,fractionLost:.02,address:'private'});
    let result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.equal(result.encoderImplementation,undefined);assert.equal(result.powerEfficientEncoder,undefined);
    assert.deepEqual(JSON.parse(JSON.stringify(result.remote)),{roundTripMs:32,fractionLost:.02});
    f.reports.set('private-remote',{type:'remote-inbound-rtp',kind:'video',roundTripTime:Infinity,fractionLost:-1});
    result=(await f.invoke('streamDiagnostics')).value.outbound[0];
    assert.deepEqual(JSON.parse(JSON.stringify(result.remote)),{roundTripMs:null,fractionLost:null});
});

test('stream diagnostics distinguish source pixels, outbound encoding and advertised quality without leaking provider metadata',async()=>{
    const f=diagnosticsFixture(),result=await f.invoke('streamDiagnostics');assert.equal(result.ok,true);
    const dto=JSON.parse(JSON.stringify(result.value));
    assert.deepEqual(dto,{source:{width:2560,height:1440,browserWidth:1920,browserHeight:1080,targetFrameRate:60},
        outbound:[{width:1920,height:1080,framesPerSecond:59.5,qualityLimitationReason:'none'}],advertised:[{width:1280,height:720,frameRate:60}],attachment:unavailableAttachment});
    assert.equal(JSON.stringify(dto).includes('private'),false);assert.equal(f.readCount(),1);
    assert.deepEqual(f.events,[]);assert.equal(f.pending.length,0);assert.equal(f.state.share,f.own);
});

test('stream diagnostics include only admitted codec names and the effective sender content hint',async()=>{
    const f=diagnosticsFixture();
    f.reports.set('private-codec',{type:'codec',mimeType:'video/VP8',sdpFmtpLine:'private-codec-parameters'});
    f.connection.videoTransceiver={sender:{track:{kind:'video',contentHint:'detail'},getParameters:()=>({encodings:[{}]})}};
    const value=(await f.invoke('streamDiagnostics')).value;
    assert.equal(value.outbound[0].codec,'VP8');assert.equal(value.sender.contentHint,'detail');
    assert.equal(JSON.stringify(value).includes('private'),false);
    f.reports.get('private-codec').mimeType='video/private';f.connection.videoTransceiver.sender.track.contentHint='private';
    const unknown=(await f.invoke('streamDiagnostics')).value;
    assert.equal(unknown.outbound[0].codec,undefined);assert.equal(unknown.sender.contentHint,undefined);
});

test('stream diagnostics fail before reading unless the exact current self connection can be established',async()=>{
    for(const mutate of [f=>f.state.share=null,f=>f.own.state='ENDED',f=>f.own.ownerId='2',f=>f.own.channelId='21',
        f=>f.rtc.isOwner=false,f=>f.rtc.streamKey='guild:10:21:1',f=>f.rtc.streamContext.ownerId='2',f=>f.rtc.streamContext.channelId='21',
        f=>f.rtc._destroyed=true,f=>f.connection.context='default',f=>f.connection.userId='2',f=>f.connection.streamUserId='2',
        f=>f.engine.connections.delete(f.connection),f=>f.stores.StreamRTCConnectionStore.getRTCConnection=()=>null,
        f=>f.connection.getRawStats=undefined,f=>f.connection.getStreamParameters=undefined]){
        const f=diagnosticsFixture();mutate(f);
        assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_unavailable');assert.equal(f.readCount(),0);assert.deepEqual(f.events,[]);
    }
});

test('stream diagnostics discard observations after account, call, stream or connection ownership changes',async()=>{
    for(const change of ['account','call','share','ended','rtc','connection','membership','share-epoch']){
        const f=diagnosticsFixture();let complete;
        f.connection.getRawStats=()=>new Promise(resolve=>complete=resolve);
        const pending=f.invoke('streamDiagnostics');await settle();
        if(change==='account')f.state.account='3';
        else if(change==='call')f.state.channel='21';
        else if(change==='share')f.state.share={...f.own};
        else if(change==='ended')f.own.state='ENDED';
        else if(change==='rtc')f.stores.StreamRTCConnectionStore.getRTCConnection=()=>({...f.rtc});
        else if(change==='connection')f.rtc._connection={...f.connection};
        else if(change==='membership')f.engine.connections.delete(f.connection);
        else{await f.invoke('stopShare');f.state.share=f.own;}
        complete(f.reports);const result=await pending;
        assert.equal(result.ok,false);assert.equal(result.value,undefined);
        assert.equal(result.code,change==='account'?'account_changed':change==='call'?'call_changed':'stream_diagnostics_stale');
    }
});

test('stream diagnostics have one outstanding provider read even when its response deadline expires',async()=>{
    const f=diagnosticsFixture();let complete,reads=0;
    f.connection.getRawStats=()=>{reads++;return new Promise(resolve=>complete=resolve);};
    const pending=f.invoke('streamDiagnostics');await settle();
    assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_busy');
    const timer=[...f.timers.values()].find(value=>value.ms===1500);assert.ok(timer);timer.cb();
    assert.equal((await pending).code,'stream_diagnostics_timeout');
    assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_busy');assert.equal(reads,1);
    complete(f.reports);await settle();f.connection.getRawStats=()=>Promise.resolve(f.reports);
    assert.equal((await f.invoke('streamDiagnostics')).ok,true);assert.deepEqual(f.events,[]);
});

test('stream diagnostics sanitize missing numbers and reason strings and omit a stopped native source',async()=>{
    const f=diagnosticsFixture();f.reports.set('outgoing',{type:'outbound-rtp',kind:'video',frameWidth:NaN,frameHeight:-1,framesPerSecond:Infinity,qualityLimitationReason:'private-reason'});
    f.parameters[0]={maxResolution:{width:'private-width',height:90000},maxFrameRate:-1};
    f.context.WidgetRailNativeMedia.status=()=>({active:false,width:2560,height:1440,targetFrameRate:60});
    const result=await f.invoke('streamDiagnostics');assert.equal(result.ok,true);
    assert.deepEqual(JSON.parse(JSON.stringify(result.value)),{source:{width:null,height:null,browserWidth:null,browserHeight:null,targetFrameRate:null},
        outbound:[{width:null,height:null,framesPerSecond:null,qualityLimitationReason:null}],advertised:[{width:null,height:null,frameRate:null}],attachment:unavailableAttachment});
});

test('attachment diagnostics distinguish captured frames from missing provider video tracks',async()=>{
    const f=diagnosticsFixture();f.context.WidgetRailNativeMedia.status=()=>({active:true,frames:432});
    f.connection.videoSupported=false;f.connection.pc={connectionState:'connected',signalingState:'stable'};
    f.connection.input={stream:{getVideoTracks:()=>[]},desktop:{stream:{getVideoTracks:()=>[{readyState:'live',id:'private-track'}]}}};
    const value=(await f.invoke('streamDiagnostics')).value;
    assert.deepEqual(JSON.parse(JSON.stringify(value.attachment)),{nativeFrames:432,videoSupported:false,inputTrack:null,desktopTrack:'live',senderTrack:null,
        peerState:'connected',signalingState:'stable',qualityBinding:null,qualityCheck:null});
    assert.equal(JSON.stringify(value).includes('private'),false);
});

test('quality diagnostic retains the exact failed checkpoint after hook retirement without provider text',async()=>{
    for(const checkpoint of ['quality-manager','primary-encoding','quality-readback']){
        const f=qualityFixture();f.state.deferDesktop=true;
        await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
        const connection=f.state.ownRtc._connection;
        if(checkpoint==='quality-manager')connection.videoQualityManager.contextType='unexpected-private-value';
        if(checkpoint==='primary-encoding')connection.videoStreamParameters=[];
        if(checkpoint==='quality-readback')connection.videoQualityManager.setGoliveQuality=()=>{connection.videoQualityManager.goliveMaxQuality={};};
        connection.setDesktopInput(f.source);
        connection.getRawStats=async()=>new Map();connection.getStreamParameters=()=>[];
        const result=await f.invoke('streamDiagnostics');assert.equal(result.ok,true);
        assert.equal(result.value.attachment.qualityCheck,checkpoint);
        assert.equal(result.value.attachment.qualityBinding,null);
        assert.equal(JSON.stringify(result.value).includes('private'),false);
    }
});

test('oversized or malformed diagnostics reports fail boundedly instead of enumerating arbitrary provider objects',async()=>{
    for(const change of ['report-count','video-count','advertised-count','wrong-container','iterator-overflow']){
        const f=diagnosticsFixture();let inspected=0;
        if(change==='report-count')f.connection.getRawStats=async()=>new Map(Array.from({length:257},(_,i)=>[String(i),{}]));
        else if(change==='video-count')f.connection.getRawStats=async()=>new Map(Array.from({length:5},(_,i)=>[String(i),{type:'outbound-rtp',kind:'video'}]));
        else if(change==='advertised-count')f.connection.getStreamParameters=()=>Array(5).fill({});
        else if(change==='wrong-container')f.connection.getRawStats=async()=>({private:'secret'});
        else f.connection.getRawStats=async()=>({size:0,*values(){while(true){inspected++;yield {};}}});
        assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_unavailable');assert.ok(inspected<=257);
    }
});

test('provider diagnostic failures are sanitized and release read admission after actual rejection',async()=>{
    const f=diagnosticsFixture();f.connection.getRawStats=async()=>{throw Error('private-provider-error');};
    assert.equal((await f.invoke('streamDiagnostics')).code,'stream_diagnostics_unavailable');
    assert.equal(JSON.stringify((await f.invoke('diagnostics')).value).includes('private-provider-error'),false);
    f.connection.getRawStats=async()=>f.reports;assert.equal((await f.invoke('streamDiagnostics')).ok,true);
});

test('voice connection status is scoped to actual RTC channel and known phases',async()=>{
    const f=fixture();f.state.rtcPhase='RTC_CONNECTED';f.state.rtcChannel='20';f.state.everConnected=false;
    f.stores.RTCConnectionStore={getState:()=>f.state.rtcPhase,getChannelId:()=>f.state.rtcChannel,getWasEverRtcConnected:()=>f.state.everConnected};
    assert.equal((await f.invoke('snapshot')).value.voiceConnection,'connected');
    for(const phase of ['CONNECTING','RTC_CONNECTING','AUTHENTICATING','AWAITING_ENDPOINT','ICE_CHECKING','DTLS_CONNECTING','RTC_DISCONNECTED','NO_ROUTE']){
        f.state.rtcPhase=phase;f.state.everConnected=false;assert.equal((await f.invoke('snapshot')).value.voiceConnection,'connecting');
        f.state.everConnected=true;assert.equal((await f.invoke('snapshot')).value.voiceConnection,'reconnecting');
    }
    f.state.rtcPhase='RTC_CONNECTED';f.state.rtcChannel='other';assert.equal((await f.invoke('snapshot')).value.voiceConnection,'unavailable');
    f.state.rtcChannel='20';f.state.rtcPhase='UNRECOGNIZED';assert.equal((await f.invoke('snapshot')).value.voiceConnection,'unavailable');
    f.state.rtcPhase='DISCONNECTED';assert.equal((await f.invoke('snapshot')).value.voiceConnection,'disconnected');
    delete f.stores.RTCConnectionStore;assert.equal((await f.invoke('snapshot')).value.voiceConnection,'unavailable');assert.deepEqual(f.events,[]);
});

test('participant mute and volume are desired-state changes only in the voice context',async()=>{
    const f=fixture();f.state.streamMuted=true;const gain=f.state.streamGain;
    assert.equal((await f.invoke('voiceMute',{userId:'2',value:true})).value.audio.muted,true);
    assert.equal((await f.invoke('voiceMute',{userId:'2',value:true})).ok,true);
    assert.deepEqual(f.events,[['streamMute','2','default']]);
    for(const volume of [0,5,50,100]){
        const reply=await f.invoke('voiceVolume',{userId:'2',value:volume});assert.equal(reply.ok,true);
        assert.ok(Math.abs(reply.value.audio.volume-volume)<1e-7);assert.equal(reply.value.audio.muted,true);
    }
    assert.equal(f.state.streamGain,gain);assert.equal(f.state.streamMuted,true);
    assert.ok(f.events.every(event=>Array.isArray(event)&&event.at(-1)==='default'));
});

test('voice audio commands reject stale peers, own user, mismatched authority and invalid values without mutation',async()=>{
    for(const args of [{account:'9',userId:'2',value:true},{channelId:'9',userId:'2',value:true},{userId:'9',value:true},{userId:'1',value:true},{userId:'2',value:'true'}]){
        const f=fixture();assert.equal((await f.invoke('voiceMute',args)).ok,false);assert.deepEqual(f.events,[]);
    }
    for(const value of [NaN,Infinity,-1,101,'50',null]){
        const f=fixture();assert.equal((await f.invoke('voiceVolume',{userId:'2',value})).ok,false);assert.deepEqual(f.events,[]);
    }
    const f=fixture();const get=f.stores.MediaEngineStore.getLocalVolume;
    f.stores.MediaEngineStore.getLocalVolume=(...args)=>{f.state.channel='21';return get(...args);};
    assert.equal((await f.invoke('voiceVolume',{userId:'2',value:50})).code,'call_changed');assert.deepEqual(f.events,[]);
});

test('voice audio preserves desktop boosts and unavailable provider state',async()=>{
    const f=fixture();f.state.voiceGain=180;
    assert.equal((await f.invoke('voiceVolume',{userId:'2',value:100})).value.audio.volume,100);
    assert.equal(f.state.voiceGain,180);assert.deepEqual(f.events,[]);
    f.state.audioAvailable=false;
    assert.equal((await f.invoke('snapshot')).value.participants[0].audio.available,false);
    assert.equal((await f.invoke('voiceMute',{userId:'2',value:true})).code,'voice_audio_unavailable');assert.deepEqual(f.events,[]);
});

function voiceSettingsFixture(){
    const f=fixture(),find=f.context.Vencord.Webpack.findByProps;
    f.state.input='default';f.state.output='speaker';
    f.inputs={default:{id:'default',name:'System default',disabled:false},mic:{id:'mic',name:'Microphone',disabled:false},off:{id:'off',name:'Hidden',disabled:true}};
    f.outputs={speaker:{id:'speaker',name:'Speakers',disabled:false},headset:{id:'headset',name:'Headset',disabled:false}};
    Object.assign(f.stores.MediaEngineStore,{supports:()=>true,getInputDevices:()=>f.inputs,getOutputDevices:()=>f.outputs,
        getInputDeviceId:()=>f.state.input,getOutputDeviceId:()=>f.state.output,getInputVolume:()=>80});
    f.deviceActions={setInputDevice:id=>{f.events.push(['input',id]);f.state.input=id;},setOutputDevice:id=>{f.events.push(['output',id]);f.state.output=id;},setInputVolume:()=>{throw Error('Browser no-op gain must never be called');}};
    f.context.Vencord.Webpack.findByProps=(...names)=>names.includes('setInputDevice')?f.deviceActions:find(...names);
    return f;
}

test('voice settings expose bounded enabled device choices without starting hardware or changing settings',async()=>{
    const f=voiceSettingsFixture(),reply=await f.invoke('voiceSettings');assert.equal(reply.ok,true);
    assert.deepEqual(JSON.parse(JSON.stringify(reply.value)),{inputAvailable:true,outputAvailable:true,inputVolumeAvailable:false,
        inputDevices:[{id:'default',name:'System default'},{id:'mic',name:'Microphone'}],outputDevices:[{id:'speaker',name:'Speakers'},{id:'headset',name:'Headset'}],
        inputDeviceId:'default',outputDeviceId:'speaker',inputVolume:null});
    assert.deepEqual(f.events,[]);assert.equal((await f.invoke('voiceInputVolume',{value:20})).code,'voice_setting_unavailable');
});

test('voice device selection uses current allowed IDs and confirms exact observed settings',async()=>{
    const f=voiceSettingsFixture();
    assert.equal((await f.invoke('voiceInputDevice',{value:'mic'})).value.inputDeviceId,'mic');
    assert.equal((await f.invoke('voiceInputDevice',{value:'mic'})).ok,true);
    assert.equal((await f.invoke('voiceOutputDevice',{value:'headset'})).value.outputDeviceId,'headset');
    assert.deepEqual(f.events,[['input','mic'],['output','headset']]);
    for(const value of ['off','unknown',null,'x'.repeat(257)])assert.equal((await f.invoke('voiceInputDevice',{value})).ok,false);
    assert.equal(f.events.length,2);
    f.deviceActions.setInputDevice=()=>{};
    assert.equal((await f.invoke('voiceInputDevice',{value:'default'})).code,'voice_setting_unconfirmed');
});

test('voice device read or write cannot cross account changes or unsupported device features',async()=>{
    const f=voiceSettingsFixture();f.stores.MediaEngineStore.supports=feature=>feature==='AUDIO_OUTPUT_DEVICE';
    const settings=(await f.invoke('voiceSettings')).value;assert.equal(settings.inputAvailable,false);assert.equal(settings.inputDevices.length,0);assert.equal(settings.outputAvailable,true);
    assert.equal((await f.invoke('voiceInputDevice',{value:'mic'})).code,'voice_setting_unavailable');assert.deepEqual(f.events,[]);
    f.stores.MediaEngineStore.getOutputDeviceId=()=>{f.state.account='3';return 'speaker';};
    assert.equal((await f.invoke('voiceOutputDevice',{value:'headset'})).code,'account_changed');assert.deepEqual(f.events,[]);
});

test('malformed and oversized device lists are unavailable, never truncated into misleading choices',async()=>{
    for(const mutate of [f=>f.inputs=Array(2),f=>f.inputs=Object.fromEntries(Array.from({length:65},(_,i)=>[String(i),{id:String(i),name:'Device',disabled:false}])),
        f=>f.inputs.mic.id='different',f=>f.inputs.mic.name=12,f=>f.inputs.mic.disabled=undefined]){
        const f=voiceSettingsFixture();mutate(f);const reply=await f.invoke('voiceSettings');assert.equal(reply.ok,true);assert.equal(reply.value.inputAvailable,false);
        assert.equal(reply.value.inputDevices.length,0);assert.equal(reply.value.outputAvailable,true);assert.deepEqual(f.events,[]);
    }
});

test('selected stream quality is configured and advertised before the first provider Video event',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();
    f.pending[0]('capture');await settle();
    assert.equal(f.state.started,1);assert.equal((await f.invoke('snapshot')).value.shareError,null);
    const observed=f.qualityObserved[0];assert.deepEqual(observed.quality,{capture:{width:2560,height:1440,framerate:60},encode:{width:2560,height:1440,framerate:60}});
    assert.deepEqual(observed.parameters[0].maxResolution,{type:'fixed',width:2560,height:1440});assert.equal(observed.parameters[0].maxFrameRate,60);
    assert.equal(f.state.ownRtc._connection.setDesktopInput.name,'setDesktopInput','Instance wrapper is restored immediately after its source handoff');
    assert.equal(f.listeners.size,1);await f.invoke('stopShare');assert.equal(f.listeners.size,0);
});

test('unknown resolution-mode constants warn instead of writing an invented provider mode',async()=>{
    for(const constants of [{FIXED:1,SOURCE:0},{FIXED:'renamed',SOURCE:'source'}]){
        const f=qualityFixture(),find=f.context.Vencord.Webpack.findByProps;
        f.context.Vencord.Webpack.findByProps=(...names)=>names.includes('SOURCE')?constants:find(...names);
        await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_quality_unconfirmed');
        assert.equal(f.state.ownRtc._connection.input.desktop,f.source,'Unsupported metadata does not prevent normal video attachment');
        assert.deepEqual(f.state.ownRtc._connection.videoStreamParameters[0].maxResolution,{type:'fixed',width:1280,height:720});
    }
});

test('host-selected Clarity survives the provider motion experiment on first and later exact-source attachment',async()=>{
    const f=qualityFixture();f.state.motionExperiment=true;f.sourceTrack.contentHint='detail';
    f.context.WidgetRailNativeMedia={status:()=>({active:true,contentPreference:'detail'})};
    await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.sourceTrack.contentHint,'detail','The provider changes the same track to motion during setStream');
    const connection=f.state.ownRtc._connection;
    connection.setStream(f.source.stream);assert.equal(f.sourceTrack.contentHint,'detail');
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
    await f.invoke('stopShare');assert.equal(connection.setStream.name,'setStream');
});

async function clarityFixture(defer=false){
    const f=qualityFixture();f.state.motionExperiment=true;f.state.deferDesktop=defer;f.sourceTrack.contentHint='detail';
    f.context.WidgetRailNativeMedia={status:()=>({active:true,contentPreference:'detail'})};
    await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
    return f;
}

test('content preference hook does not change another track or the comparison client',async()=>{
    const f=await clarityFixture(),connection=f.state.ownRtc._connection;
    const foreign={kind:'video',contentHint:'detail'};
    connection.setStream({getVideoTracks:()=>[foreign]});assert.equal(foreign.contentHint,'motion');
    assert.equal(f.sourceTrack.contentHint,'detail');
    const comparison=qualityFixture();comparison.state.motionExperiment=true;comparison.sourceTrack.contentHint='detail';
    await comparison.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();comparison.pending[0]('capture');await settle();
    assert.equal(comparison.sourceTrack.contentHint,'motion');
    assert.equal(comparison.state.ownRtc._connection.setStream.name,'setStream');
});

test('retired content hooks cannot reapply Clarity after account call source or stream replacement',async()=>{
    for(const change of ['stop','account','call','source','stream','dispose']){
        const f=await clarityFixture(),connection=f.state.ownRtc._connection,held=connection.setStream;
        if(change==='stop')await f.invoke('stopShare');
        else if(change==='account')f.state.account='other';
        else if(change==='call')f.state.channel='other';
        else if(change==='source')f.engine.desktopInputPool.set('capture',{});
        else if(change==='stream')f.state.share={...f.state.share};
        else f.context.WidgetRailDiscordPrototype.dispose();
        held.call(connection,f.source.stream);
        assert.equal(f.sourceTrack.contentHint,'motion',change);
        assert.equal(connection.setStream.name,'setStream',change);
        assert.equal(f.listeners.size,0,change);
    }
});

test('content preference is not reapplied when authority changes during provider attachment',async()=>{
    const f=await clarityFixture(),connection=f.state.ownRtc._connection;
    f.state.duringStream=()=>{f.state.channel='other';};
    connection.setStream(f.source.stream);
    assert.equal(f.sourceTrack.contentHint,'motion');assert.equal(connection.setStream.name,'setStream');
});

test('unwritable hint reports a scoped warning while the provider stream remains active',async()=>{
    const f=await clarityFixture();let hint='detail';
    Object.defineProperty(f.sourceTrack,'contentHint',{get:()=>hint,set:value=>{if(value==='motion')hint=value;},configurable:true});
    f.state.ownRtc._connection.setStream(f.source.stream);
    const snapshot=(await f.invoke('snapshot')).value;
    assert.equal(hint,'motion');assert.equal(snapshot.shareQualityWarning,'share_content_preference_unconfirmed');
    assert.equal(snapshot.shareError,null);assert.equal(snapshot.sharing,true);
    f.state.share={...f.state.share};assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
});

test('an unwrappable provider method warns without blocking the original handoff',async()=>{
    const f=await clarityFixture(true),connection=f.state.ownRtc._connection;
    Object.defineProperty(connection,'setStream',{value:connection.setStream,configurable:false,writable:false});
    connection.setDesktopInput(f.source);
    assert.equal(f.sourceTrack.contentHint,'motion');
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_content_preference_unconfirmed');
    assert.equal(f.state.started,1);
});

test('content hook retirement preserves a later provider method replacement',async()=>{
    const f=await clarityFixture(),connection=f.state.ownRtc._connection;
    const replacement=()=>{};connection.setStream=replacement;
    await f.invoke('stopShare');assert.equal(connection.setStream,replacement);
});

test('optional native-status failure warns without rejecting supported high quality',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    f.context.WidgetRailNativeMedia={status(){throw Error('Unavailable optional observation');}};
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    const snapshot=(await f.invoke('snapshot')).value;
    assert.equal(snapshot.shareQualityWarning,'share_content_preference_unconfirmed');
    assert.equal(snapshot.shareError,null);assert.equal(snapshot.sharing,true);
    assert.equal(f.qualityObserved[0].parameters[0].maxResolution.height,1440);
});

test('a provider clone on the owned source warns instead of silently rewriting an unknown track',async()=>{
    const f=await clarityFixture(),connection=f.state.ownRtc._connection;
    const clone={kind:'video',readyState:'live',contentHint:'detail'};
    connection.setStream({getVideoTracks:()=>[clone]});
    assert.equal(clone.contentHint,'motion');assert.equal(f.sourceTrack.contentHint,'detail');
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_content_preference_unconfirmed');
});

test('an asynchronous provider attachment warns and preserves its returned promise',async()=>{
    const f=await clarityFixture(true),connection=f.state.ownRtc._connection,original=connection.setStream;
    const returned=Promise.resolve('completed');
    connection.setStream=function(stream){original.call(this,stream);return returned;};
    connection.setDesktopInput(f.source);
    assert.equal(connection.setStream(f.source.stream),returned);
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_content_preference_unconfirmed');
    assert.equal(f.sourceTrack.contentHint,'motion');
});

test('quality hook ignores unrelated connections and preserves source aspect ratio',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.sourceTrack.getConstraints=()=>({width:{max:3440},height:{max:1440}});
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.qualityObserved[0].parameters[0].maxResolution.width,3440);
    const other={context:'stream',userId:'1',streamUserId:'2',setDesktopInput(){}};f.engine.connections.push(other);
    const original=other.setDesktopInput;for(const listener of f.listeners)listener(other);assert.equal(other.setDesktopInput,original);
});

test('stopped, replaced and changed-owner quality hooks cannot rewrite a later connection',async()=>{
    for(const change of ['stop','account','channel','source','share']){
        const f=qualityFixture();f.state.entitled1080=true;f.state.deferDesktop=true;
        await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
        const old=f.state.ownRtc._connection,held=old.setDesktopInput;
        if(change==='stop')await f.invoke('stopShare');else if(change==='account')f.state.account='3';else if(change==='channel')f.state.channel='21';
        else if(change==='source')f.engine.desktopInputPool.set('capture',{});else{f.state.share={channelId:'99',ownerId:'1',guildId:'10'};}
        held.call(old,f.source);assert.equal(old.videoStreamParameters[0].maxResolution.height,720);
        assert.equal(f.listeners.size,0);
    }
});

test('missing or changed quality APIs fail clearly without changing an unknown provider connection',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.engine.on=undefined;
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.state.started,0);assert.equal(f.state.destroyed,1);assert.equal((await f.invoke('snapshot')).value.shareError,'share_quality_unavailable');
    const delayed=qualityFixture();delayed.state.entitled1080=true;delayed.state.deferDesktop=true;
    await delayed.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();delayed.pending[0]('capture');await settle();
    delayed.state.ownRtc._connection.videoQualityManager.setGoliveQuality=()=>{};
    delayed.state.ownRtc._connection.setDesktopInput(delayed.source);
    assert.equal((await delayed.invoke('snapshot')).value.shareQualityWarning,'share_quality_unconfirmed');
    assert.equal((await delayed.invoke('snapshot')).value.shareError,null);
    assert.equal(delayed.state.ownRtc._connection.videoStreamParameters[0].maxResolution.height,720);
    assert.equal(delayed.listeners.size,0);
});

test('late abandoned source cleanup cannot retire a newer quality binding',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;let abandonedDestroyed=0;
    f.engine.desktopInputPool.set('abandoned',{destroy(){abandonedDestroyed++;}});
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();
    await f.invoke('stopShare');
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();
    f.pending[1]('capture');await settle();assert.equal(f.listeners.size,1);
    f.pending[0]('abandoned');await settle();
    assert.equal(abandonedDestroyed,1);assert.equal(f.listeners.size,1);assert.equal(f.state.started,1);
    assert.equal(f.state.ownRtc._connection.videoStreamParameters[0].maxResolution.height,1440);
});

test('quality configuration never overwrites a later user preference',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.state.deferDesktop=true;
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    f.state.qualitySettings={preset:3,resolution:720,fps:30};f.state.ownRtc._connection.setDesktopInput(f.source);
    assert.equal(f.state.ownRtc._connection.videoStreamParameters[0].maxResolution.height,720);
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_quality_unconfirmed');assert.equal(f.listeners.size,0);
});

test('optional quality correction does not regress a previously working baseline share',async()=>{
    for(const quality of [{resolution:480,frameRate:15},{resolution:720,frameRate:30}]){
        const f=qualityFixture();f.engine.on=undefined;
        await f.invoke('share',{captureQuality:quality});await settle();f.pending[0]('capture');await settle();
        const state=(await f.invoke('snapshot')).value;
        assert.equal(f.state.started,1);assert.equal(f.state.destroyed,0);assert.equal(state.sharing,true);assert.equal(state.shareError,null);
        assert.equal(state.shareQualityWarning,'share_quality_unavailable');
    }
    const f=qualityFixture();f.state.entitled1080=true;f.engine.on=undefined;
    await f.invoke('share',{captureQuality:{resolution:720,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    assert.equal(f.state.started,0);assert.equal((await f.invoke('snapshot')).value.shareError,'share_quality_unavailable');
});

test('cold RTC modules may become available at exact source attachment without executing factories',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;
    const rtc=f.stores.StreamRTCConnectionStore,find=f.context.Vencord.Webpack.findByCode,findProps=f.context.Vencord.Webpack.findByProps;
    delete f.stores.StreamRTCConnectionStore;
    f.context.Vencord.Webpack.findByProps=(...names)=>names.includes('SOURCE')&&!f.state.started?undefined:findProps(...names);
    f.context.Vencord.Webpack.findByCode=pattern=>pattern==='"STREAM_START",streamType'?((...args)=>{f.stores.StreamRTCConnectionStore=rtc;return find(pattern)(...args);}):find(pattern);
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();assert.equal(f.state.started,0);
    f.pending[0]('capture');await settle();const state=(await f.invoke('snapshot')).value;
    assert.equal(f.state.started,1);assert.equal(state.shareError,null);assert.equal(state.shareQualityWarning,null);
    assert.equal(f.qualityObserved[0].parameters[0].maxResolution.height,1440);
});

test('safe sender diagnostics report real encoding controls without codec SDP or identity data',async()=>{
    const f=diagnosticsFixture();f.connection.videoTransceiver={sender:{track:{kind:'video'},getParameters:()=>({transactionId:'private',
        degradationPreference:'maintain-resolution',encodings:[{rid:'private',ssrc:123,active:true,maxBitrate:8000000,maxFramerate:60,scaleResolutionDownBy:1}]})}};
    const result=await f.invoke('streamDiagnostics');assert.equal(result.ok,true);
    assert.deepEqual(JSON.parse(JSON.stringify(result.value.sender)),{degradationPreference:'maintain-resolution',encodings:[{active:true,maxBitrate:8000000,maxFramerate:60,scaleResolutionDownBy:1}]});
    assert.equal(JSON.stringify(result.value).includes('private'),false);
    f.connection.videoTransceiver.sender.getParameters=()=>({degradationPreference:'private',encodings:[{maxBitrate:Infinity,maxFramerate:999,scaleResolutionDownBy:999}]});
    const malformed=(await f.invoke('streamDiagnostics')).value.sender;
    assert.equal(malformed.degradationPreference,null);assert.equal(malformed.encodings[0].maxBitrate,null);assert.equal(malformed.encodings[0].maxFramerate,null);
});

test('quality warnings retire with account call source and exact stream instead of leaking into later views',async()=>{
    for(const change of ['account','call','source','track-ended','stream-ended','stream-replaced','stream-gone','dispose']){
        const f=qualityFixture();f.engine.on=undefined;
        await f.invoke('share',{captureQuality:{resolution:720,frameRate:30}});await settle();f.pending[0]('capture');await settle();
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_quality_unavailable');
        const original=f.state.share;
        if(change==='account')f.state.account='3';else if(change==='call')f.state.channel='21';
        else if(change==='source')f.state.goLiveSource={desktopSource:{id:'another-source'}};
        else if(change==='track-ended')f.sourceTrack.readyState='ended';else if(change==='stream-ended')f.state.share.state='ENDED';
        else if(change==='stream-replaced')f.state.share={...f.state.share};else if(change==='stream-gone')f.state.share=null;
        else f.context.WidgetRailDiscordPrototype.dispose();
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null,change);
        f.state.account='1';f.state.channel='20';f.state.goLiveSource={desktopSource:{id:'capture'}};f.state.share=original;delete original.state;f.sourceTrack.readyState='live';
        assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null,'A retired warning cannot return after '+change);
    }
});

test('post-attachment warning survives binding cleanup but not a later stream',async()=>{
    const f=qualityFixture();f.state.entitled1080=true;f.state.deferDesktop=true;
    await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
    f.state.ownRtc._connection.videoQualityManager.setGoliveQuality=()=>{};f.state.ownRtc._connection.setDesktopInput(f.source);
    assert.equal(f.listeners.size,0);assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,'share_quality_unconfirmed');
    f.state.share={channelId:'20',guildId:'10',ownerId:'1'};
    assert.equal((await f.invoke('snapshot')).value.shareQualityWarning,null);
});

test('fatal share errors also clear on account or call departure',async()=>{
    for(const change of ['account','call']){
        const f=qualityFixture();f.state.entitled1080=true;f.engine.on=undefined;
        await f.invoke('share',{captureQuality:{resolution:1440,frameRate:60}});await settle();f.pending[0]('capture');await settle();
        assert.equal((await f.invoke('snapshot')).value.shareError,'share_quality_unavailable');
        f.state[change==='account'?'account':'channel']='99';assert.equal((await f.invoke('snapshot')).value.shareError,null);
        f.state.account='1';f.state.channel='20';assert.equal((await f.invoke('snapshot')).value.shareError,null);
    }
});
