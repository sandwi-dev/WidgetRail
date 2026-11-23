import test from 'node:test';
import assert from 'node:assert/strict';
import { createNativeSharePreparation, readNativeSharePolicy } from './share-preparation.mjs';

function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
const high = { resolution: 1440, frameRate: 60 };
const low = { resolution: 720, frameRate: 30 };
const source = () => ({ id: 'window:123:0', name: 'Generated test window', identity: 'pid:8;created:9;hwnd:123', applicationAudioAvailable: true,
    thumbnail: 'PRIVATE PIXELS', arbitrary: 'PRIVATE PROVIDER OBJECT' });
function fixture() {
    const owner = {};
    const state = { owner, at: 1000, sources: [source()], policy: { available: true, allowed: [low, high], preferred: high } };
    const calls = { list: 0, policy: 0, source: [] };
    const deps = { getOwner: () => state.owner, now: () => state.at,
        listSources: async actualOwner => { assert.equal(actualOwner, owner); calls.list++; return state.sources; },
        readPolicy: actualOwner => { assert.equal(actualOwner, owner); calls.policy++; return state.policy; },
        sourceIsCurrent: async (id, identity, actualOwner, audio) => { calls.source.push([id, identity, actualOwner, audio]); return true; }
    };
    return { owner, state, calls, deps, create: () => createNativeSharePreparation(deps),
        choice: lease => ({ lease, sourceId: 'window:123:0', applicationAudio: true, quality: high, explicitUserChoice: true }) };
}
function policyFixture() {
    const owner = {};
    const user = { id: '1' }; const channel = { id: '2', getGuildId: () => '3' };
    const state = { owner, saved: { preset: 3, resolution: 1440, fps: 60 } };
    const calls = [];
    const deps = { getOwner: () => state.owner, userStore: { getCurrentUser: () => user },
        channelSelection: { getVoiceChannelId: () => '2' }, channelStore: { getChannel: () => channel },
        guildStore: { getGuild: id => { assert.equal(id, '3'); return { premiumTier: 2 }; } },
        settingsStore: { getState: () => state.saved }, presets: { PRESET_CUSTOM: 3 },
        canShareInChannel: actualChannel => actualChannel === channel,
        canStreamWithSettings: (...args) => { calls.push(args); return args[1] === 720 && args[2] === 30 || args[1] === 1440 && args[2] === 60; }
    };
    return { owner, user, channel, state, calls, deps };
}
test('a stale preparation request cannot retire the current chooser', async () => {
    const f = fixture(), preparation = f.create();
    const current = await preparation.prepare(f.owner);
    assert.equal(current.status, 'prepared');
    const stale = await preparation.prepare({});
    assert.equal(stale.reason, 'owner_changed');
    assert.equal((await preparation.takeSelection(f.choice(current.lease))).status, 'selection_validated');
});

test('native policy queries every fixed-mode pair through real provider eligibility shape without mutations', () => {
    const f = policyFixture(); const actual = readNativeSharePolicy(f.deps, f.owner);
    assert.equal(actual.available, true); assert.deepEqual(actual.allowed, [low, high]);
    assert.deepEqual(actual.preferred, high); assert.equal(f.calls.length, 12);
    for (const [preset, resolution, fps, user, tier, channel] of f.calls) {
        assert.equal(preset, 3); assert.ok([480, 720, 1080, 1440].includes(resolution));
        assert.ok([15, 30, 60].includes(fps)); assert.equal(user, f.user); assert.equal(tier, 2); assert.equal(channel, f.channel);
    }
    assert.deepEqual(f.state.saved, { preset: 3, resolution: 1440, fps: 60 });
});

test('missing policy and permissions fail closed, never baseline-allow unknown modes', () => {
    for (const failure of ['eligibility', 'permission', 'permission-unknown', 'missing', 'owner']) {
        const f = policyFixture();
        if (failure === 'eligibility') f.deps.canStreamWithSettings = () => undefined;
        if (failure === 'permission') f.deps.canShareInChannel = () => false;
        if (failure === 'permission-unknown') f.deps.canShareInChannel = () => null;
        if (failure === 'missing') delete f.deps.settingsStore;
        if (failure === 'owner') f.deps.canStreamWithSettings = () => { f.state.owner = {}; return true; };
        const actual = readNativeSharePolicy(f.deps, f.owner);
        assert.equal(actual.available, false); assert.deepEqual(actual.allowed, []); assert.equal(actual.preferred, null);
    }
});

test('an unavailable saved quality is not overwritten during preparation', () => {
    const f = policyFixture(); f.state.saved = { preset: 3, resolution: 1080, fps: 60 };
    assert.equal(readNativeSharePolicy(f.deps, f.owner).preferred, null);
    assert.deepEqual(f.state.saved, { preset: 3, resolution: 1080, fps: 60 });
});

test('preparation exposes bounded detached labels/options, never thumbnails or source authority', async () => {
    const f = fixture(); const service = f.create(); const prepared = await service.prepare(f.owner);
    assert.equal(prepared.status, 'prepared'); assert.equal(prepared.expiresAfterMilliseconds, 30000);
    assert.deepEqual(prepared.sources, [{ id: 'window:123:0', name: 'Generated test window', applicationAudioAvailable: true }]);
    assert.deepEqual(prepared.allowed, [low, high]); assert.deepEqual(prepared.preferred, high);
    assert.doesNotMatch(JSON.stringify(prepared), /PRIVATE|created:9/);
    prepared.sources[0].id = 'window:999:0'; prepared.allowed[0].resolution = 480;
    assert.equal(f.state.sources[0].id, 'window:123:0'); assert.equal(f.state.policy.allowed[0].resolution, 720);
    assert.equal(f.calls.list, 1); assert.equal(f.calls.source.length, 0);
});

test('explicit source/audio/quality selection consumes once and never starts a broadcast', async () => {
    const f = fixture(); const service = f.create(); const prepared = await service.prepare(f.owner);
    const selection = f.choice(prepared.lease);
    const actual = await service.takeSelection(selection);
    assert.equal(actual.status, 'selection_validated'); assert.equal(actual.startImplemented, false);
    assert.equal(actual.owner, f.owner); assert.equal(actual.sourceIdentity, f.state.sources[0].identity);
    assert.equal(actual.applicationAudio, true); assert.deepEqual(actual.quality, high);
    assert.deepEqual(f.calls.source, [['window:123:0', f.state.sources[0].identity, f.owner, { applicationAudio: true }]]);
    assert.equal((await service.takeSelection(selection)).reason, 'preparation_missing');
    assert.equal(f.calls.source.length, 1);
});

test('source inventory is copied; provider mutation cannot retarget an issued source row', async () => {
    const f = fixture(); const service = f.create(); const prepared = await service.prepare(f.owner);
    f.state.sources[0].identity = 'NEW'; f.state.sources[0].id = 'window:999:0';
    f.deps.sourceIsCurrent = () => false; // Captured dependency remains the original fixture function.
    const actual = await service.takeSelection(f.choice(prepared.lease));
    assert.equal(actual.sourceId, 'window:123:0');
    assert.equal(actual.sourceIdentity, 'pid:8;created:9;hwnd:123');
});

test('invalid or nonwindow sources are rejected instead of silently truncated', async () => {
    for (const sources of [[{ ...source(), id: 'screen:1' }], [source(), source()], [{ ...source(), identity: '' }],
        [{ ...source(), name: 'unsafe\nlabel' }], Array.from({ length: 257 }, (_, i) => ({ ...source(), id: `window:${i}` }))]) {
        const f = fixture(); f.state.sources = sources;
        assert.equal((await f.create().prepare(f.owner)).status, 'unavailable');
    }
});

test('malformed policy, empty allowance and unqualified async policy cannot prepare', async () => {
    for (const policy of [null, { available: false }, { available: true, allowed: [] },
        { available: true, allowed: [low, low] }, { available: true, allowed: [{ resolution: 9999, frameRate: 60 }] },
        Promise.resolve({ available: true, allowed: [low] })]) {
        const f = fixture(); f.state.policy = policy;
        assert.equal((await f.create().prepare(f.owner)).status, 'unavailable');
    }
});

test('account/call replacement during source enumeration retires preparation', async () => {
    const f = fixture(); const wait = deferred(); f.deps.listSources = () => wait.promise;
    const service = f.create(); const pending = service.prepare(f.owner); f.state.owner = {};
    wait.resolve([source()]);
    assert.equal((await pending).reason, 'preparation_retired');
    assert.equal(f.calls.policy, 0);
});

test('late preparation cannot replace newer preparation', async () => {
    const f = fixture(); const wait = deferred(); let calls = 0;
    f.deps.listSources = () => ++calls === 1 ? wait.promise : Promise.resolve([source()]);
    const service = f.create(); const older = service.prepare(f.owner); const newer = await service.prepare(f.owner);
    wait.resolve([source()]); assert.equal((await older).reason, 'preparation_retired');
    assert.equal((await service.takeSelection(f.choice(newer.lease))).status, 'selection_validated');
});

test('audio choice must be explicit and application-only capability must be true', async () => {
    for (const failure of ['choice', 'audio-absent', 'audio-capability']) {
        const f = fixture(); if (failure === 'audio-capability') f.state.sources[0].applicationAudioAvailable = false;
        const service = f.create(); const prepared = await service.prepare(f.owner); const selection = f.choice(prepared.lease);
        if (failure === 'choice') selection.explicitUserChoice = false;
        if (failure === 'audio-absent') delete selection.applicationAudio;
        assert.equal((await service.takeSelection(selection)).status, 'unavailable');
        assert.equal(f.calls.source.length, 0);
        assert.equal((await service.takeSelection(selection)).reason, 'preparation_missing');
    }
    const f = fixture(); f.state.sources[0].applicationAudioAvailable = false;
    const service = f.create(); const prepared = await service.prepare(f.owner);
    assert.equal((await service.takeSelection({ ...f.choice(prepared.lease), applicationAudio: false })).status, 'selection_validated');
});

test('source identity is revalidated and missing/unknown/replaced source cannot proceed', async () => {
    for (const current of [false, undefined, 'true']) {
        const f = fixture(); f.deps.sourceIsCurrent = () => current;
        const service = f.create(); const prepared = await service.prepare(f.owner);
        assert.equal((await service.takeSelection(f.choice(prepared.lease))).reason, 'source_changed');
    }
});

test('requested application audio is revalidated even when the window identity is unchanged', async () => {
    const f = fixture(); let audioAvailable = true;
    f.deps.sourceIsCurrent = (_id, _identity, _owner, choice) => !choice.applicationAudio || audioAvailable;
    const service = f.create(); const prepared = await service.prepare(f.owner); audioAvailable = false;
    assert.equal((await service.takeSelection(f.choice(prepared.lease))).reason, 'source_changed');
});

test('quality downgrade during source revalidation is checked before returning selection', async () => {
    const f = fixture(); const wait = deferred(); f.deps.sourceIsCurrent = () => wait.promise;
    const service = f.create(); const prepared = await service.prepare(f.owner);
    const pending = service.takeSelection(f.choice(prepared.lease)); f.state.policy.allowed = [low]; wait.resolve(true);
    assert.equal((await pending).reason, 'quality_no_longer_allowed');
});

test('owner change and cancellation during source validation invalidate the selected lease', async () => {
    for (const change of ['owner', 'cancel', 'replace']) {
        const f = fixture(); const wait = deferred(); f.deps.sourceIsCurrent = () => wait.promise;
        const service = f.create(); const prepared = await service.prepare(f.owner);
        const pending = service.takeSelection(f.choice(prepared.lease));
        if (change === 'owner') f.state.owner = {};
        if (change === 'cancel') assert.equal(service.cancel(prepared.lease), true);
        if (change === 'replace') await service.prepare(f.owner);
        wait.resolve(true); assert.equal((await pending).reason, 'preparation_retired');
    }
});

test('simultaneous selection cannot consume the same lease twice', async () => {
    const f = fixture(); const wait = deferred(); f.deps.sourceIsCurrent = () => wait.promise;
    const service = f.create(); const prepared = await service.prepare(f.owner);
    const first = service.takeSelection(f.choice(prepared.lease));
    assert.equal((await service.takeSelection(f.choice(prepared.lease))).reason, 'preparation_missing');
    wait.resolve(true); assert.equal((await first).status, 'selection_validated');
});

test('selection fields are snapshotted before asynchronous verification', async () => {
    const f = fixture(); const wait = deferred(); f.deps.sourceIsCurrent = () => wait.promise;
    const service = f.create(); const prepared = await service.prepare(f.owner); const selection = f.choice(prepared.lease);
    const pending = service.takeSelection(selection);
    selection.applicationAudio = false; selection.sourceId = 'window:999'; selection.quality = low;
    wait.resolve(true); const actual = await pending;
    assert.equal(actual.applicationAudio, true); assert.equal(actual.sourceId, 'window:123:0'); assert.deepEqual(actual.quality, high);
});

test('expired, backwards and invalid clock observations fail closed without a timer', async () => {
    for (const at of [31000, 999, NaN]) {
        const f = fixture(); const service = f.create(); const prepared = await service.prepare(f.owner); f.state.at = at;
        assert.equal((await service.takeSelection(f.choice(prepared.lease))).reason, 'preparation_retired');
        assert.equal(f.calls.source.length, 0);
    }
});

test('provider failures do not escape or leak provider error strings', async () => {
    const f = fixture(); f.deps.listSources = async () => { throw new Error('PRIVATE'); };
    const service = f.create(); assert.doesNotMatch(JSON.stringify(await service.prepare(f.owner)), /PRIVATE/);
    const g = fixture(); let retired = false;
    g.deps.getOwner = () => { if (retired) throw new Error('PRIVATE'); return g.owner; };
    const wait = deferred(); g.deps.listSources = () => wait.promise;
    const other = g.create(); const pending = other.prepare(g.owner); retired = true; wait.resolve([source()]);
    const actual = await pending; assert.equal(actual.status, 'unavailable'); assert.doesNotMatch(JSON.stringify(actual), /PRIVATE/);
});

test('invalid source selection does not fall back to another window or the desktop', async () => {
    const f = fixture(); const service = f.create(); const prepared = await service.prepare(f.owner);
    assert.equal((await service.takeSelection({ ...f.choice(prepared.lease), sourceId: 'window:missing' })).reason, 'selection_invalid');
    assert.equal(f.calls.source.length, 0);
});
