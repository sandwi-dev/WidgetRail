import test from 'node:test';
import assert from 'node:assert/strict';
import { applyVoiceIntent } from './voice-intent.mjs';

function fixture({ mute = false, deaf = false, effectiveMute = mute, effectiveDeaf = deaf } = {}) {
    const captured = {};
    const state = { mute, deaf };
    const calls = [];
    const holder = { owner: captured };
    const media = {
        getSettings(context) { assert.equal(this, media); assert.equal(context, 'default'); return state; },
        isSupported() { return true; },
        isMute() { return effectiveMute; }, isDeaf() { return effectiveDeaf; }
    };
    const actions = {
        setSelfMute(context, value, sound) {
            assert.equal(this, actions); calls.push([context, value, sound]); state.mute = value;
        },
        toggleSelfMute() { throw new Error('Toggle forbidden'); },
        toggleSelfDeaf(options) {
            assert.equal(this, actions); calls.push(options); state.deaf = !state.deaf;
        },
        enable() { throw new Error('Enable forbidden'); }
    };
    const dispatcher = { isDispatching: () => false, _processingWaitQueue: false, _waitQueue: [] };
    const dependencies = { getOwner: () => holder.owner, media, actions, dispatcher };
    const request = (intent, value) => ({ intent, value, owner: captured });
    return { holder, state, calls, media, actions, dispatcher, dependencies, request };
}

test('uses explicit context-aware mute setter once and snapshots provider primitives', () => {
    const f = fixture();
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'confirmed');
    assert.equal(actual.mutationAttempted, true);
    assert.deepEqual(f.calls, [['default', true, true]]);
    assert.deepEqual(actual.observed.preference, { muted: true, deafened: false });
    f.state.mute = false;
    assert.equal(actual.observed.preference.muted, true);
});

test('raw preference already satisfied does not toggle effective permission/server mute', () => {
    const f = fixture({ mute: false, effectiveMute: true });
    const actual = applyVoiceIntent(f.request('mute', false), f.dependencies);
    assert.equal(actual.status, 'confirmed');
    assert.equal(actual.mutationAttempted, false);
    assert.equal(actual.observed.preference.muted, false);
    assert.equal(actual.observed.effective.muted, true);
    assert.equal(f.calls.length, 0);
});

test('unmute while deafened changes only raw mute; never silently undeafens', () => {
    const f = fixture({ mute: true, deaf: true, effectiveMute: true, effectiveDeaf: true });
    const actual = applyVoiceIntent(f.request('mute', false), f.dependencies);
    assert.equal(actual.status, 'confirmed');
    assert.deepEqual(actual.observed.preference, { muted: false, deafened: true });
    assert.equal(actual.observed.effective.muted, true);
    assert.equal(f.calls.length, 1);
});

test('deafen desired state uses one source-proven synchronous compare-and-toggle', () => {
    for (const desired of [true, false]) {
        const f = fixture({ deaf: !desired, mute: true });
        f.actions.setSelfDeaf = () => { throw new Error('Unverified signature must not be called'); };
        const actual = applyVoiceIntent(f.request('deafen', desired), f.dependencies);
        assert.equal(actual.status, 'confirmed');
        assert.deepEqual(f.calls, [{ context: 'default', syncRemote: true, usedKeybind: false }]);
        assert.deepEqual(actual.observed.preference, { muted: true, deafened: desired });
        assert.equal(applyVoiceIntent(f.request('deafen', desired), f.dependencies).reason, 'already_satisfied');
        assert.equal(f.calls.length, 1);
    }
});

test('missing explicit mute setter never falls back to toggle or direct store mutation', () => {
    const f = fixture(); delete f.actions.setSelfMute;
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'unsupported'); assert.equal(f.state.mute, false);
});

test('stale owner is rejected before reading settings or changing anything', () => {
    const f = fixture(); f.holder.owner = {};
    f.media.getSettings = () => { throw new Error('Should not read'); };
    assert.deepEqual(applyVoiceIntent(f.request('mute', true), f.dependencies),
        { status: 'stale', mutationAttempted: false, reason: 'owner_changed', observed: null });
    assert.equal(f.calls.length, 0);
});

test('account/call replacement during settings or action discovery prevents mutation', () => {
    for (const phase of ['settings', 'action']) {
        const f = fixture();
        if (phase === 'settings') f.media.getSettings = () => { f.holder.owner = {}; return f.state; };
        else Object.defineProperty(f.actions, 'setSelfMute', { get() { f.holder.owner = {}; return () => assert.fail('stale setter'); } });
        const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
        assert.equal(actual.status, 'stale'); assert.equal(actual.mutationAttempted, false);
        assert.equal(f.calls.length, 0);
    }
});

test('provider failures and malformed raw values cannot establish a preference', () => {
    for (const settings of [null, {}, { mute: 'false', deaf: false }, { mute: false, deaf: 0 }]) {
        const f = fixture(); f.media.getSettings = () => settings;
        assert.equal(applyVoiceIntent(f.request('mute', true), f.dependencies).status, 'unavailable');
        assert.equal(f.calls.length, 0);
    }
    const f = fixture(); f.media.getSettings = () => { throw new Error('PRIVATE'); };
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'unavailable'); assert.doesNotMatch(JSON.stringify(actual), /PRIVATE/);
});

test('effective getters are optional and unknown does not become a false value', () => {
    const f = fixture(); delete f.media.isMute; f.media.isDeaf = () => { throw new Error('missing'); };
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'confirmed');
    assert.deepEqual(actual.observed.effective, { muted: null, deafened: null });
});

test('no-op or changed additional setting after mutation is unconfirmed, never retried', () => {
    for (const extraChange of [false, true]) {
        const f = fixture();
        f.actions.setSelfMute = () => { f.calls.push('one'); if (extraChange) { f.state.mute = true; f.state.deaf = true; } };
        const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
        assert.equal(actual.status, 'unconfirmed'); assert.equal(actual.mutationAttempted, true);
        assert.equal(f.calls.length, 1);
    }
});

test('setter throwing after applying state remains unconfirmed without leaking provider error', () => {
    const f = fixture();
    f.actions.setSelfMute = () => { f.calls.push('one'); f.state.mute = true; throw new Error('PRIVATE'); };
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'unconfirmed'); assert.equal(actual.mutationAttempted, true);
    assert.equal(f.calls.length, 1); assert.doesNotMatch(JSON.stringify(actual), /PRIVATE/);
});

test('owner or settings binding changes during mutation cannot confirm another incarnation', () => {
    for (const changed of ['owner', 'binding']) {
        const f = fixture();
        f.actions.setSelfMute = () => {
            f.calls.push('one'); f.state.mute = true;
            if (changed === 'owner') f.holder.owner = {};
            else f.media.getSettings = () => f.state;
        };
        const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
        assert.equal(actual.status, 'unconfirmed'); assert.equal(actual.observed, null);
        assert.equal(f.calls.length, 1);
    }
});

test('owner replacement while reading final settings invalidates confirmation', () => {
    const f = fixture(); let reads = 0;
    f.media.getSettings = () => { if (++reads === 3) f.holder.owner = {}; return f.state; };
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'unconfirmed'); assert.equal(actual.observed, null);
    assert.equal(f.calls.length, 1);
});

test('raw desired deafen is independent of effective server deafen', () => {
    const f = fixture({ deaf: false, effectiveDeaf: true });
    const actual = applyVoiceIntent(f.request('deafen', false), f.dependencies);
    assert.equal(actual.reason, 'already_satisfied'); assert.equal(f.calls.length, 0);
    assert.deepEqual(actual.observed.preference, { muted: false, deafened: false });
    assert.equal(actual.observed.effective.deafened, true);
});

test('deafen never invents missing action or enters unsupported-media dialog path', () => {
    for (const unavailable of ['action', 'support', 'support-unknown']) {
        const f = fixture();
        if (unavailable === 'action') delete f.actions.toggleSelfDeaf;
        else if (unavailable === 'support') f.media.isSupported = () => false;
        else delete f.media.isSupported;
        const actual = applyVoiceIntent(f.request('deafen', true), f.dependencies);
        assert.equal(actual.status, 'unsupported'); assert.equal(f.calls.length, 0);
    }
});

test('both mutations reject queued dispatch including store emission where public isDispatching is false', () => {
    for (const intent of ['mute', 'deafen']) for (const busy of ['dispatching', 'processing', 'queued']) {
        const f = fixture();
        if (busy === 'dispatching') f.dispatcher.isDispatching = () => true;
        if (busy === 'processing') f.dispatcher._processingWaitQueue = true;
        if (busy === 'queued') f.dispatcher._waitQueue.push(() => {});
        const actual = applyVoiceIntent(f.request(intent, true), f.dependencies);
        assert.equal(actual.status, 'busy'); assert.equal(actual.mutationAttempted, false);
        assert.equal(f.calls.length, 0); assert.deepEqual(f.state, { mute: false, deaf: false });
    }
});

test('missing or changed dispatcher queue contract fails closed', () => {
    for (const unknown of [undefined, {}, { isDispatching: () => false },
        { isDispatching: () => undefined, _processingWaitQueue: false, _waitQueue: [] }]) {
        const f = fixture(); f.dependencies.dispatcher = unknown;
        assert.equal(applyVoiceIntent(f.request('deafen', true), f.dependencies).status, 'unsupported');
        assert.equal(f.calls.length, 0);
    }
});

test('a provider read that enters a dispatch rejects mutation at the final boundary', () => {
    const f = fixture(); let reads = 0;
    f.media.getSettings = () => { if (++reads === 2) f.dispatcher._processingWaitQueue = true; return f.state; };
    const actual = applyVoiceIntent(f.request('deafen', true), f.dependencies);
    assert.equal(actual.status, 'busy'); assert.equal(actual.mutationAttempted, false);
    assert.equal(f.calls.length, 0);
});

test('deafen action using the source dispatcher queue semantics executes synchronously only when idle', () => {
    const f = fixture();
    f.dispatcher.dispatch = function (action) {
        return new Promise(resolve => {
            this._waitQueue.push(() => { action(); resolve(); });
            if (!this._processingWaitQueue) {
                this._processingWaitQueue = true;
                try { while (this._waitQueue.length) this._waitQueue.shift()(); }
                finally { this._processingWaitQueue = false; }
            }
        });
    };
    f.actions.toggleSelfDeaf = options => {
        f.calls.push(options);
        // Discord's action does not return the dispatch Promise.
        f.dispatcher.dispatch(() => { f.state.deaf = !f.state.deaf; });
    };
    const actual = applyVoiceIntent(f.request('deafen', true), f.dependencies);
    assert.equal(actual.status, 'confirmed'); assert.equal(f.state.deaf, true);
    assert.equal(f.calls.length, 1); assert.equal(f.dispatcher._waitQueue.length, 0);
    f.dispatcher._processingWaitQueue = true;
    const duringEmission = applyVoiceIntent(f.request('deafen', false), f.dependencies);
    assert.equal(duringEmission.status, 'busy');
    assert.equal(f.calls.length, 1); assert.equal(f.dispatcher._waitQueue.length, 0);
});

test('deafen rechecks raw state changed during binding discovery instead of reversing satisfied intent', () => {
    const f = fixture(); const action = f.actions.toggleSelfDeaf;
    Object.defineProperty(f.actions, 'toggleSelfDeaf', { get() { f.state.deaf = true; return action; } });
    const actual = applyVoiceIntent(f.request('deafen', true), f.dependencies);
    assert.equal(actual.reason, 'already_satisfied'); assert.equal(actual.mutationAttempted, false);
    assert.equal(f.state.deaf, true); assert.equal(f.calls.length, 0);
});

test('deafen never retries unconfirmed/no-op/throwing provider actions', () => {
    for (const failure of ['no-op', 'throw', 'owner']) {
        const f = fixture();
        f.actions.toggleSelfDeaf = () => {
            f.calls.push('one');
            if (failure === 'throw') throw new Error('PRIVATE');
            if (failure === 'owner') { f.state.deaf = true; f.holder.owner = {}; }
        };
        const actual = applyVoiceIntent(f.request('deafen', true), f.dependencies);
        assert.equal(actual.status, 'unconfirmed'); assert.equal(actual.mutationAttempted, true);
        assert.equal(f.calls.length, 1); assert.doesNotMatch(JSON.stringify(actual), /PRIVATE/);
    }
});

test('unexpected async setter cannot be confirmed from an immediate matching value', async () => {
    const f = fixture();
    f.actions.setSelfMute = () => { f.calls.push('one'); f.state.mute = true; return Promise.reject(new Error('PRIVATE')); };
    const actual = applyVoiceIntent(f.request('mute', true), f.dependencies);
    assert.equal(actual.status, 'unconfirmed');
    assert.equal(actual.reason, 'asynchronous_setter_unqualified');
    assert.equal(f.calls.length, 1);
    await new Promise(resolve => setImmediate(resolve));
});

test('malformed requests are rejected without provider access', () => {
    for (const request of [null, {}, { intent: 'toggle', value: true, owner: {} },
        { intent: 'mute', value: 'true', owner: {} }, { intent: 'mute', value: true, owner: null }]) {
        assert.equal(applyVoiceIntent(request, { getOwner() { assert.fail('No provider access'); } }).status, 'invalid');
    }
});
