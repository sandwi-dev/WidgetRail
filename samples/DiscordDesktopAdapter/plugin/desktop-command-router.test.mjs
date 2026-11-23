import test from 'node:test';
import assert from 'node:assert/strict';
import { createDesktopCommandRouter, DesktopProviderGate } from './desktop-command-router.mjs';

const id = character => character.repeat(32);
const connectionId = id('1');
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function fixture() {
    const holder = { owner: Object.freeze({ rendererEpoch: id('a'), accountEpoch: id('b'), callEpoch: id('c') }) };
    const state = { mute: false, deaf: false, revision: 0 };
    const calls = [];
    const media = { getSettings: () => state, isSupported: () => true,
        isMute: () => state.mute || state.deaf, isDeaf: () => state.deaf };
    const actions = {
        setSelfMute(context, value, sound) { calls.push(['mute', context, value, sound]); state.mute = value; state.revision++; },
        toggleSelfDeaf(options) { calls.push(['deafen', options]); state.deaf = !state.deaf; state.revision++; }
    };
    const dispatcher = { isDispatching: () => false, _processingWaitQueue: false, _waitQueue: [] };
    const providerGate = new DesktopProviderGate();
    const observation = () => ({ revision: state.revision, signedIn: true, voiceConnection: 'connected',
        voice: { preference: { muted: state.mute, deafened: state.deaf }, effective: { muted: state.mute || state.deaf, deafened: state.deaf } } });
    let readCalls = 0;
    const dependencies = { connectionId, providerGate, getOwner: () => holder.owner,
        readState: () => { readCalls++; return observation(); }, getVoiceBindings: () => ({ media, actions, dispatcher }) };
    const makeRouter = overrides => createDesktopCommandRouter({ ...dependencies, ...overrides });
    const request = (requestId, method = 'state.read', value = true, scope = holder.owner, connection = connectionId) => ({
        type: 'request', version: 1, requestId, connectionId: connection, scope: { ...scope }, method,
        args: method === 'state.read' ? {} : { value }
    });
    return { holder, state, calls, media, actions, dispatcher, providerGate, observation, dependencies, makeRouter, request, readCalls: () => readCalls };
}

test('state read returns only a copied strict projection bound to its current scope', async () => {
    const f = fixture(); const raw = f.observation(); const router = f.makeRouter({ readState: () => raw });
    const result = await router.dispatch(f.request(1));
    assert.equal(result.status, 'read'); assert.equal(result.reason, 'state_observed');
    assert.equal(result.mutationAttempted, false); assert.deepEqual(result.scope, f.holder.owner);
    raw.voice.preference.muted = true; assert.equal(result.state.voice.preference.muted, false);
    assert.deepEqual(f.calls, []);
});

test('schemas reject unknown methods, fields, non-booleans, malformed IDs and accessors without provider calls', async () => {
    const f = fixture(); let providerCalls = 0;
    const router = f.makeRouter({ getOwner() { providerCalls++; return f.holder.owner; } });
    const cases = [
        { ...f.request(1), method: 'eval', args: { code: 'private' } },
        { ...f.request(1), extra: true }, { ...f.request(1), version: 2 },
        { ...f.request(1), requestId: '1' }, { ...f.request(1), requestId: Number.MAX_SAFE_INTEGER + 1 },
        { ...f.request(1), requestId: 0 }, { ...f.request(1), requestId: 1.5 },
        { ...f.request(1), connectionId: id('A') },
        { ...f.request(1), args: { value: true } },
        { ...f.request(1, 'voice.setMuted'), args: { value: 1 } },
        { ...f.request(1), scope: { ...f.holder.owner, accountId: 'should-not-be-here' } }
    ];
    const accessor = f.request(1); Object.defineProperty(accessor.args, 'value', { get() { throw new Error('must not execute'); }, enumerable: true }); cases.push(accessor);
    for (const value of cases) {
        const result = await router.dispatch(value);
        assert.equal(result.reason, 'invalid_request'); assert.equal(result.mutationAttempted, false);
        assert.equal(result.requestId, null); assert.equal(result.state, null); assert.equal(result.voice, null);
    }
    assert.equal(providerCalls, 0); assert.equal(f.readCalls(), 0); assert.equal(f.calls.length, 0);
});

test('connection, renderer, account and call scopes are checked before provider discovery', async () => {
    const f = fixture(); let discoveries = 0;
    const router = f.makeRouter({ getVoiceBindings() { discoveries++; return f.dependencies.getVoiceBindings(); } });
    const wrongConnection = await router.dispatch(f.request(1, 'voice.setMuted', true, f.holder.owner, id('2')));
    assert.equal(wrongConnection.reason, 'connection_changed');
    let requestId = 1;
    for (const key of ['rendererEpoch', 'accountEpoch', 'callEpoch']) {
        const result = await router.dispatch(f.request(requestId++, 'voice.setMuted', true, { ...f.holder.owner, [key]: id('e') }));
        assert.equal(result.reason, 'owner_changed'); assert.equal(result.mutationAttempted, false);
    }
    assert.equal(discoveries, 0); assert.equal(f.calls.length, 0);
});

test('explicit mute/deafen reuse qualified raw-preference semantics and applied already-satisfied results', async () => {
    const f = fixture(); const router = f.makeRouter();
    const mute = await router.dispatch(f.request(1, 'voice.setMuted', true));
    assert.equal(mute.status, 'applied'); assert.equal(mute.mutationAttempted, true);
    const deaf = await router.dispatch(f.request(2, 'voice.setDeafened', true));
    assert.equal(deaf.status, 'applied'); assert.deepEqual(deaf.voice.preference, { muted: true, deafened: true });
    const unmute = await router.dispatch(f.request(3, 'voice.setMuted', false));
    assert.equal(unmute.status, 'applied'); assert.deepEqual(unmute.voice.preference, { muted: false, deafened: true });
    assert.equal(unmute.voice.effective.muted, true);
    const satisfied = await router.dispatch(f.request(4, 'voice.setMuted', false));
    assert.equal(satisfied.status, 'applied'); assert.equal(satisfied.mutationAttempted, false);
    assert.equal(f.calls.length, 3);
});

test('duplicate or older IDs never replay, including rejected requests and across scope changes', async () => {
    const f = fixture(); const router = f.makeRouter();
    assert.equal((await router.dispatch(f.request(10, 'voice.setMuted'))).status, 'applied');
    assert.equal((await router.dispatch(f.request(10, 'voice.setMuted', false))).reason, 'duplicate_or_old_request');
    assert.equal((await router.dispatch(f.request(9))).reason, 'duplicate_or_old_request');
    assert.equal((await router.dispatch(f.request(11, 'voice.setMuted', false, { ...f.holder.owner, callEpoch: id('f') }))).reason, 'owner_changed');
    assert.equal((await router.dispatch(f.request(11, 'voice.setMuted', false))).reason, 'duplicate_or_old_request');
    f.holder.owner = Object.freeze({ ...f.holder.owner, accountEpoch: id('e') });
    assert.equal((await router.dispatch(f.request(10))).reason, 'duplicate_or_old_request');
    assert.equal((await router.dispatch(f.request(Number.MAX_SAFE_INTEGER))).status, 'read');
    assert.equal((await router.dispatch(f.request(Number.MAX_SAFE_INTEGER))).reason, 'duplicate_or_old_request');
    assert.equal(f.calls.length, 1);
});

test('one pending read admits no queue and retirement settles callers even when provider ignores abort', async () => {
    const f = fixture(); const delayed = deferred(); let invoked = 0, signal;
    const router = f.makeRouter({ readState(_, options) { invoked++; signal = options.signal; return delayed.promise; } });
    const pending = router.dispatch(f.request(1));
    for (let id = 2; id <= 12; id++) assert.equal((await router.dispatch(f.request(id))).reason, 'request_busy');
    assert.equal(invoked, 1);
    router.retire(); const result = await pending;
    assert.equal(result.reason, 'connection_retired'); assert.equal(result.state, null); assert.equal(signal.aborted, true);
    assert.equal(router.scope(), null); assert.equal((await router.dispatch(f.request(13))).reason, 'connection_retired');
    delayed.resolve(f.observation()); await Promise.resolve(); assert.equal(result.state, null); assert.equal(invoked, 1);
});

test('same-public-scope provider replacement retires an awaited read without leaking new state', async () => {
    const f = fixture(); const delayed = deferred();
    const router = f.makeRouter({ readState: () => delayed.promise });
    const pending = router.dispatch(f.request(1));
    f.holder.owner = Object.freeze({ ...f.holder.owner });
    delayed.resolve({ ...f.observation(), privateName: 'must never escape' });
    const result = await pending;
    assert.equal(result.reason, 'owner_changed'); assert.equal(result.state, null);
    assert.ok(!JSON.stringify(result).includes('must never escape'));
});

test('an abort-ignoring read cannot accumulate across replacement connections', async () => {
    const f = fixture(); const delayed = deferred(); let reads = 0;
    const first = f.makeRouter({ readState() { reads++; return delayed.promise; } });
    const pending = first.dispatch(f.request(1)); first.retire();
    assert.equal((await pending).reason, 'connection_retired');
    const second = f.makeRouter({ connectionId: id('2'), readState() { reads++; return f.observation(); } });
    assert.equal((await second.dispatch(f.request(1, 'state.read', true, f.holder.owner, id('2')))).reason, 'request_busy');
    assert.equal(reads, 1);
    delayed.resolve(f.observation()); await Promise.resolve(); await Promise.resolve();
    assert.equal((await second.dispatch(f.request(2, 'state.read', true, f.holder.owner, id('2')))).status, 'read');
    assert.equal(reads, 2);
});

test('changed ownership during binding discovery prevents any mutation', async () => {
    const f = fixture();
    const router = f.makeRouter({ getVoiceBindings() { f.holder.owner = Object.freeze({ ...f.holder.owner, accountEpoch: id('e') }); return f.dependencies.getVoiceBindings(); } });
    const result = await router.dispatch(f.request(1, 'voice.setMuted'));
    assert.equal(result.reason, 'owner_changed'); assert.equal(result.mutationAttempted, false); assert.equal(f.calls.length, 0);
});

test('retirement before mutation rejects, while retirement inside setter is conservatively uncertain', async () => {
    const before = fixture(); let router;
    router = before.makeRouter({ getVoiceBindings() { router.retire(); return before.dependencies.getVoiceBindings(); } });
    const rejected = await router.dispatch(before.request(1, 'voice.setMuted'));
    assert.equal(rejected.status, 'rejected'); assert.equal(rejected.mutationAttempted, false); assert.equal(before.calls.length, 0);
    assert.equal((await before.makeRouter().dispatch(before.request(1, 'voice.setMuted'))).status, 'applied');

    const during = fixture(); const active = during.makeRouter();
    during.actions.setSelfMute = (_, value) => { active.retire(); during.state.mute = value; during.calls.push('changed'); };
    const uncertain = await active.dispatch(during.request(1, 'voice.setMuted'));
    assert.equal(uncertain.status, 'uncertain'); assert.equal(uncertain.mutationAttempted, null); assert.equal(uncertain.voice, null);
    const successor = during.makeRouter({ connectionId: id('2') });
    assert.equal((await successor.dispatch(during.request(1, 'voice.setMuted', false, during.holder.owner, id('2')))).reason, 'mutation_reconciliation_required');
    assert.equal(during.calls.length, 1);
});

test('uncertain async setter quarantines the provider across router replacement and state reads', async () => {
    const f = fixture(); const delayed = deferred();
    f.actions.setSelfMute = () => { f.calls.push('attempt'); return delayed.promise; };
    const first = f.makeRouter();
    const result = await first.dispatch(f.request(1, 'voice.setMuted'));
    assert.equal(result.status, 'uncertain'); assert.equal(result.mutationAttempted, true);
    assert.equal(result.reason, 'asynchronous_setter_unqualified');
    first.retire(); const second = f.makeRouter({ connectionId: id('2') });
    assert.equal((await second.dispatch(f.request(1, 'state.read', true, f.holder.owner, id('2')))).status, 'read');
    assert.equal((await second.dispatch(f.request(2, 'voice.setMuted', true, f.holder.owner, id('2')))).reason, 'mutation_reconciliation_required');
    delayed.resolve(); await Promise.resolve();
    assert.equal((await second.dispatch(f.request(3, 'voice.setMuted', true, f.holder.owner, id('2')))).reason, 'mutation_reconciliation_required');
    assert.equal(f.calls.length, 1);
});

test('a throw after a side effect is uncertain and never automatically retried', async () => {
    const f = fixture();
    f.actions.setSelfMute = (_, value) => { f.state.mute = value; f.calls.push('applied'); throw new Error('private provider details'); };
    const router = f.makeRouter(); const result = await router.dispatch(f.request(1, 'voice.setMuted'));
    assert.equal(result.status, 'uncertain'); assert.equal(result.mutationAttempted, true);
    assert.equal((await router.dispatch(f.request(2, 'voice.setMuted'))).reason, 'mutation_reconciliation_required');
    assert.ok(!JSON.stringify(result).includes('private provider')); assert.equal(f.calls.length, 1);
});

test('pending state cannot overwrite a newer mutation acknowledgment, even through another connection', async () => {
    const f = fixture(); const delayed = deferred(); const older = f.observation();
    const reader = f.makeRouter({ readState: () => delayed.promise });
    const writer = f.makeRouter({ connectionId: id('2') });
    const pending = reader.dispatch(f.request(1));
    assert.equal((await writer.dispatch(f.request(1, 'voice.setMuted', true, f.holder.owner, id('2')))).status, 'applied');
    delayed.resolve(older); const result = await pending;
    assert.equal(result.reason, 'state_changed_during_read'); assert.equal(result.state, null);
});

test('shared mutation admission prevents reentrant cross-connection mutation', async () => {
    const f = fixture(); const other = f.makeRouter({ connectionId: id('2') }); let nested;
    f.actions.setSelfMute = (_, value) => { nested = other.dispatch(f.request(1, 'voice.setDeafened', true, f.holder.owner, id('2'))); f.state.mute = value; f.calls.push('mute'); };
    const router = f.makeRouter(); assert.equal((await router.dispatch(f.request(1, 'voice.setMuted'))).status, 'applied');
    assert.equal((await nested).reason, 'request_busy'); assert.deepEqual(f.calls, ['mute']);
});

test('owner discovery cannot reenter admission and reverse request ordering', async () => {
    const f = fixture(); let router, nested, first = true;
    router = f.makeRouter({ getOwner() { if (first) { first = false; nested = router.dispatch(f.request(2, 'voice.setDeafened')); } return f.holder.owner; } });
    assert.equal((await router.dispatch(f.request(1, 'voice.setMuted'))).status, 'applied');
    assert.equal((await nested).reason, 'request_busy'); assert.equal(f.calls.length, 1);
});

test('read projection rejects extra data and older revisions', async () => {
    const f = fixture(); let result = f.observation(); const router = f.makeRouter({ readState: () => result });
    result.revision = 10; assert.equal((await router.dispatch(f.request(1))).status, 'read');
    result = { ...f.observation(), revision: 9 }; assert.equal((await router.dispatch(f.request(2))).reason, 'stale_state_revision');
    result = { ...f.observation(), revision: 11, token: 'not allowed' };
    const invalid = await router.dispatch(f.request(3)); assert.equal(invalid.reason, 'invalid_state_projection'); assert.equal(invalid.state, null);
});

test('signed-out state is readable but call-scoped mutation is not admitted', async () => {
    const f = fixture(); f.holder.owner = Object.freeze({ ...f.holder.owner, callEpoch: null });
    const router = f.makeRouter({ readState: () => ({ revision: 0, signedIn: false, voiceConnection: 'disconnected', voice: null }) });
    assert.equal((await router.dispatch(f.request(1))).status, 'read');
    assert.equal((await router.dispatch(f.request(2, 'voice.setMuted'))).reason, 'call_required'); assert.equal(f.calls.length, 0);
});

test('provider-level mutation lifetime dependency is mandatory, not recreated implicitly per router', () => {
    const f = fixture(); const { providerGate, ...withoutGate } = f.dependencies;
    assert.throws(() => createDesktopCommandRouter(withoutGate), /invalid_desktop_router_dependencies/);
    assert.ok(providerGate instanceof DesktopProviderGate);
});
