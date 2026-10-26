import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('./adapter.js', import.meta.url), 'utf8');
function fixture() {
    const events = [], pending = [], timers = new Map();
    let nextTimer = 0;
    const state = { account: '1', channel: '20', muted: false, deafened: false, share: null, capability: true, closed: 0, destroyed: 0, started: 0, now: 1000 };
    const track = { kind: 'video', readyState: 'live', muted: false, id: 'track' };
    const engine = { connections: [{ context: 'stream', streamUserId: '2', pc: { getReceivers: () => [{ track }] } }],
        getDesktopSource: () => new Promise(resolve => pending.push(resolve)), desktopInputPool: new Map([['capture', { destroy: () => state.destroyed++ }]]) };
    const stores = {
        UserStore: { getCurrentUser: () => state.account ? { id: state.account } : null, getUser: () => ({ username: 'Test participant' }) },
        SelectedChannelStore: { getVoiceChannelId: () => state.channel },
        ChannelStore: { getChannel: id => ({ id, name: 'Test call', guild_id: '10' }) },
        GuildStore: { getGuilds: () => ({ '10': { id: '10', name: 'Test server' } }) },
        GuildChannelStore: { getChannels: () => ({ voice: [{ channel: { id: '20', name: 'Voice', type: 2 } }] }) },
        MediaEngineStore: { getMediaEngine: () => engine, isSelfMute: () => state.muted, isSelfDeaf: () => state.deafened },
        VoiceStateStore: { getVoiceStatesForChannel: () => ({ '2': { userId: '2', selfStream: true } }) },
        ApplicationStreamingStore: { getCurrentUserActiveStream: () => state.share, getViewerIds: () => [],
            getAllActiveStreamsForChannel: () => [], getAllApplicationStreamsForChannel: () => [] }
    };
    class Peer {
        constructor() { this.iceGatheringState = 'complete'; this.connectionState = 'new'; }
        addTrack(t) { return { track: t, replaceTrack: async () => {} }; }
        async createOffer() { return { type: 'offer', sdp: 'test' }; }
        async setLocalDescription(value) { this.localDescription = { ...value, toJSON: () => value }; }
        async setRemoteDescription() { this.connectionState = 'connected'; }
        close() { state.closed++; }
    }
    const context = vm.createContext({ console, RTCPeerConnection: Peer, Date: class extends Date { static now() { return state.now; } },
        setTimeout: (cb, ms) => { const id = ++nextTimer; timers.set(id, { cb, ms }); return id; },
        clearTimeout: id => timers.delete(id), setInterval: () => ++nextTimer, clearInterval: () => {},
        VesktopNative: { win: { focus: async () => {} } },
        Vencord: { Webpack: { findStore: name => stores[name],
            findByProps: () => state.capability ? { toggleSelfMute: () => { state.muted = !state.muted; events.push('mute'); },
                toggleSelfDeaf: () => { state.deafened = !state.deafened; events.push('deafen'); } } : undefined,
            findByCode: pattern => {
                if (!state.capability) return undefined;
                if (pattern.includes('STREAM_START')) return () => { state.started++; state.share = { channelId: state.channel, guildId: '10', ownerId: state.account }; };
                if (pattern.includes('STREAM_STOP')) return () => { state.share = null; events.push('stop'); };
                if (pattern.includes('STREAM_WATCH')) return descriptor => {
                    assert.equal(descriptor.streamType, 'guild'); assert.equal(descriptor.ownerId, '2');
                    events.push('watch');
                };
                return () => events.push(pattern);
            }, Common: { FluxDispatcher: { dispatch: event => { events.push(event); state.channel = event.channelId; } } } } }
    });
    vm.runInContext(source, context);
    const invoke = (method, args = {}) => context.WidgetRailDiscordPrototype.invoke({ method, owner: 'c'.repeat(32), args: { account: '1', channelId: '20', ...args } });
    return { state, events, pending, timers, invoke, context, engine };
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
