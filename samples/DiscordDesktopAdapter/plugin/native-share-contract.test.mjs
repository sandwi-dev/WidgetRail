import test from 'node:test';
import assert from 'node:assert/strict';
import { buildNativeStartContract, isNativeStartContractCurrent, buildNativeStopContract, isNativeStopContractCurrent } from './native-share-contract.mjs';

const quality = { resolution: 1440, frameRate: 60 };
function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
function fixture() {
    const owner = { privateMarker: 'PRIVATE_OWNER' };
    const state = { context: { owner, accountId: '1', channelId: '2', guildId: '3', sharing: false, pending: false },
        source: { sourceId: 'window:123:0', sourceIdentity: 'process-created-111-window-123', processId: 8,
            name: 'Generated source', applicationAudioAvailable: true, ignoredData: 'PRIVATE_SOURCE' },
        policy: { customPreset: 3, canShare: true, previewDisabled: true, allowed: [quality] } };
    const calls = [];
    const selection = { status: 'selection_validated', owner, sourceId: state.source.sourceId,
        sourceIdentity: state.source.sourceIdentity, applicationAudio: true, quality, startImplemented: false };
    const deps = { readContext: () => state.context,
        observePermission: async () => { calls.push('permission'); return 'granted'; },
        readSource: async input => { calls.push(['source', input]); return state.source; },
        readPolicy: () => { calls.push('policy'); return state.policy; } };
    return { owner, state, selection, calls, deps };
}
function ownBroadcast(f) {
    return { context: { ...f.state.context, sharing: true }, streamKey: 'guild:3:2:1',
        broadcastOwnerId: '1', metadataSourceId: f.state.source.sourceId,
        metadata: { secret: 'PRIVATE_METADATA' }, connection: { secret: 'PRIVATE_RTC' }, source: { ...f.state.source } };
}

test('builds source-supported window parameters and preserves exact source instead of game PID conversion', async () => {
    const f = fixture(); const contract = await buildNativeStartContract(f.selection, f.deps);
    assert.equal(contract.status, 'contract_ready'); assert.equal(contract.bindingQualified, false);
    assert.deepEqual(contract.steps, [
        { kind: 'updateSettings', parameters: { preset: 3, resolution: 1440, frameRate: 60, soundshareEnabled: true } },
        { kind: 'startStream', guildId: '3', channelId: '2', options: { sourceId: 'window:123:0', sourceName: 'Generated source',
            sourcePid: 8, sound: true, previewDisabled: true } }
    ]);
    assert.equal(Object.hasOwn(contract.steps[1].options, 'pid'), false);
    assert.equal(Object.hasOwn(contract.steps[1].options, 'audioSourceId'), false);
    assert.deepEqual(f.calls, ['permission', ['source', { sourceId: 'window:123:0',
        sourceIdentity: 'process-created-111-window-123', applicationAudio: true }], 'policy']);
    assert.ok(isNativeStartContractCurrent(contract, f.state));
    assert.doesNotMatch(JSON.stringify(contract), /PRIVATE|process-created|owner/);
    assert.equal(f.state.context.sharing, false); // No start implementation was called.
});

test('contract values are frozen and cloned descriptors have no internal identity authority', async () => {
    const f = fixture(); const contract = await buildNativeStartContract(f.selection, f.deps);
    assert.throws(() => { contract.steps[1].options.sourceId = 'window:999'; }, TypeError);
    assert.equal(isNativeStartContractCurrent(JSON.parse(JSON.stringify(contract)), f.state), false);
    f.state.source.name = 'Changed title'; // Titles are presentation, not identity.
    assert.ok(isNativeStartContractCurrent(contract, f.state));
    assert.equal(contract.steps[1].options.sourceName, 'Generated source');
});

test('invalid/prepicked/screen sources never reach a permission query or fallback window', async () => {
    for (const id of ['screen:1', 'prepicked:0', 'window:', 'window:123\n']) {
        const f = fixture(); f.selection.sourceId = id;
        assert.equal((await buildNativeStartContract(f.selection, f.deps)).reason, 'invalid_selection');
        assert.deepEqual(f.calls, []);
    }
});

test('already active/pending share and stale initial owner reject before permission observation', async () => {
    for (const change of ['sharing', 'pending', 'owner']) {
        const f = fixture();
        if (change === 'owner') f.state.context.owner = {};
        else f.state.context[change] = true;
        assert.equal((await buildNativeStartContract(f.selection, f.deps)).reason, 'owner_or_share_changed');
        assert.deepEqual(f.calls, []);
    }
});

test('account/call/owner change during permission await prevents all later source/policy work', async () => {
    for (const changed of ['owner', 'accountId', 'channelId', 'guildId', 'sharing']) {
        const f = fixture(); const wait = deferred(); f.deps.observePermission = () => wait.promise;
        const pending = buildNativeStartContract(f.selection, f.deps);
        f.state.context[changed] = changed === 'owner' ? {} : changed === 'sharing' ? true : '99';
        wait.resolve('granted');
        assert.equal((await pending).reason, 'owner_or_share_changed');
        assert.deepEqual(f.calls, []);
    }
});

test('permission denial or unknown is not treated as a request to grant access', async () => {
    for (const observed of ['denied', 'unknown', true, undefined]) {
        const f = fixture(); f.deps.observePermission = async () => observed;
        assert.equal((await buildNativeStartContract(f.selection, f.deps)).reason, 'permission_unconfirmed');
        assert.deepEqual(f.calls, []);
    }
});

test('source or audio changes are rejected after the async source observation', async () => {
    for (const changed of ['sourceId', 'sourceIdentity', 'processId', 'applicationAudioAvailable']) {
        const f = fixture();
        f.state.source[changed] = changed === 'processId' ? 1 : changed === 'applicationAudioAvailable' ? false : 'different';
        assert.equal((await buildNativeStartContract(f.selection, f.deps)).reason, 'source_or_audio_changed');
    }
});

test('owner changes while verifying source cannot produce a start descriptor', async () => {
    const f = fixture(); const wait = deferred(); const entered = deferred();
    f.deps.readSource = () => { entered.resolve(); return wait.promise; };
    const pending = buildNativeStartContract(f.selection, f.deps); await entered.promise;
    f.state.context.owner = {}; wait.resolve(f.state.source);
    assert.equal((await pending).reason, 'owner_or_share_changed');
});

test('selected fields are copied before permission/source awaits', async () => {
    const f = fixture(); const wait = deferred(); f.deps.observePermission = () => wait.promise;
    const pending = buildNativeStartContract(f.selection, f.deps);
    f.selection.sourceId = 'window:999'; f.selection.applicationAudio = false;
    f.selection.quality = { resolution: 720, frameRate: 30 }; wait.resolve('granted');
    const actual = await pending;
    assert.equal(actual.steps[1].options.sourceId, 'window:123:0');
    assert.equal(actual.steps[1].options.sound, true); assert.equal(actual.steps[0].parameters.resolution, 1440);
});

test('fresh policy must admit the exact mode and preserve a known preview preference', async () => {
    for (const patch of [{ allowed: [] }, { previewDisabled: undefined }, { canShare: false }, { customPreset: undefined },
        { allowed: [quality, quality] }]) {
        const f = fixture(); Object.assign(f.state.policy, patch);
        assert.equal((await buildNativeStartContract(f.selection, f.deps)).reason, 'quality_or_preview_policy_unconfirmed');
    }
});

test('later start boundary rejects source incarnation, native process, policy and call changes', async () => {
    for (const change of ['sourceIdentity', 'processId', 'audio', 'preview', 'preset', 'quality', 'call']) {
        const f = fixture(); const contract = await buildNativeStartContract(f.selection, f.deps);
        if (change === 'sourceIdentity') f.state.source.sourceIdentity = 'reused-window';
        if (change === 'processId') f.state.source.processId++;
        if (change === 'audio') f.state.source.applicationAudioAvailable = false;
        if (change === 'preview') f.state.policy.previewDisabled = false;
        if (change === 'preset') f.state.policy.customPreset++;
        if (change === 'quality') f.state.policy.allowed = [];
        if (change === 'call') f.state.context.owner = {};
        assert.equal(isNativeStartContractCurrent(contract, f.state), false);
    }
});

// Deliberately test-only sequence driver. The production module has no execution
// entrypoint and its bindingQualified:false must not be bypassed in real wiring.
function runStubbedStart(contract, f, afterSettings = () => {}) {
    const observed = [];
    for (const step of contract.steps) {
        if (!isNativeStartContractCurrent(contract, f.state)) return observed;
        observed.push(step.kind);
        if (step.kind === 'updateSettings') afterSettings();
    }
    return observed;
}

test('stubbed sequence follows native settings-before-start and stops if ownership changes between them', async () => {
    const f = fixture(); const contract = await buildNativeStartContract(f.selection, f.deps);
    assert.deepEqual(runStubbedStart(contract, f), ['updateSettings', 'startStream']);
    assert.deepEqual(runStubbedStart(contract, f, () => { f.state.context.owner = {}; }), ['updateSettings']);
});

test('stop contract preserves clear-native-source before provider stop without exporting raw RTC objects', () => {
    const f = fixture(); const observed = ownBroadcast(f); const contract = buildNativeStopContract(observed);
    assert.equal(contract.status, 'stop_contract_ready'); assert.equal(contract.bindingQualified, false);
    assert.deepEqual(contract.steps, [{ kind: 'clearOwnNativeSource', parameters: null },
        { kind: 'stopStream', streamKey: 'guild:3:2:1', canShowFeedback: false, close: true }]);
    assert.ok(isNativeStopContractCurrent(contract, observed));
    assert.doesNotMatch(JSON.stringify(contract), /PRIVATE|process-created/);
    assert.equal(isNativeStopContractCurrent(JSON.parse(JSON.stringify(contract)), observed), false);
});

test('foreign/malformed broadcast cannot produce stop or clear intent', () => {
    for (const change of ['owner', 'key', 'metadata', 'connection', 'sourceMismatch', 'idle']) {
        const f = fixture(); const observed = ownBroadcast(f);
        if (change === 'owner') observed.broadcastOwnerId = '99';
        if (change === 'key') observed.streamKey = 'guild:3:2:99';
        if (change === 'metadata') observed.metadata = null;
        if (change === 'connection') observed.connection = null;
        if (change === 'sourceMismatch') observed.metadataSourceId = 'window:456';
        if (change === 'idle') observed.context.sharing = false;
        assert.equal(buildNativeStopContract(observed).status, 'unavailable');
    }
});

test('restart with same public key, changed source or changed call rejects the old stop', () => {
    for (const change of ['metadata', 'connection', 'sourceIdentity', 'sourceId', 'owner']) {
        const f = fixture(); const observed = ownBroadcast(f); const contract = buildNativeStopContract(observed);
        if (change === 'metadata') observed.metadata = {};
        if (change === 'connection') observed.connection = {};
        if (change === 'sourceIdentity') observed.source.sourceIdentity = 'replacement';
        if (change === 'sourceId') observed.metadataSourceId = 'window:456';
        if (change === 'owner') observed.context.owner = {};
        assert.equal(isNativeStopContractCurrent(contract, observed), false);
    }
});

function runStubbedStop(contract, observed, afterClear = () => {}) {
    const calls = [];
    if (!isNativeStopContractCurrent(contract, observed)) return calls;
    calls.push(contract.steps[0].kind); observed.source = null; afterClear();
    if (!isNativeStopContractCurrent(contract, observed, true)) return calls;
    calls.push(contract.steps[1].kind);
    return calls;
}

test('stubbed stop rechecks broadcast incarnation after clear before stopping', () => {
    const f = fixture(); const observed = ownBroadcast(f); const contract = buildNativeStopContract(observed);
    assert.deepEqual(runStubbedStop(contract, observed), ['clearOwnNativeSource', 'stopStream']);
    const other = ownBroadcast(f); const otherContract = buildNativeStopContract(other);
    assert.deepEqual(runStubbedStop(otherContract, other, () => { other.metadata = {}; }), ['clearOwnNativeSource']);
});

test('native source-ended but provider still sharing can be stopped by exact broadcast incarnation', () => {
    const f = fixture(); const observed = ownBroadcast(f); observed.source = null;
    const contract = buildNativeStopContract(observed);
    assert.equal(contract.status, 'stop_contract_ready');
    assert.deepEqual(runStubbedStop(contract, observed), ['clearOwnNativeSource', 'stopStream']);
});

test('DM call uses source-supported null guild argument and call stream key', async () => {
    const f = fixture(); f.state.context.guildId = null;
    const start = await buildNativeStartContract(f.selection, f.deps);
    assert.equal(start.steps[1].guildId, null);
    const observed = ownBroadcast(f); observed.context.guildId = null; observed.streamKey = 'call:2:1';
    assert.equal(buildNativeStopContract(observed).steps[1].streamKey, 'call:2:1');
});

test('observation errors remain unavailable without exposing provider data', async () => {
    const f = fixture(); f.deps.observePermission = async () => { throw new Error('PRIVATE FAILURE'); };
    const actual = await buildNativeStartContract(f.selection, f.deps);
    assert.equal(actual.status, 'unavailable'); assert.doesNotMatch(JSON.stringify(actual), /PRIVATE/);
});
