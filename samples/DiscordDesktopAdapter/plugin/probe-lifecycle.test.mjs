import test from 'node:test';
import assert from 'node:assert/strict';
import { createBoundedProbeWriter, isUniqueProbeSender, createProbeLifecycle } from './probe-lifecycle.mjs';

test('stalled write admits no queue and only two lifetime attempts', async () => {
    let release;
    const held = new Promise(resolve => { release = resolve; });
    const writes = [];
    const record = createBoundedProbeWriter(async value => { writes.push(value); await held; });
    const first = record('first');
    for (let i = 0; i < 100; i++) assert.deepEqual(await record('busy'), { recorded: false });
    assert.deepEqual(writes, ['first']);
    release();
    assert.deepEqual(await first, { recorded: true });
    assert.deepEqual(await record('second'), { recorded: true });
    assert.deepEqual(await record('third'), { recorded: false });
    assert.deepEqual(writes, ['first', 'second']);
});

test('write failures do not poison busy state or create an unlimited retry path', async () => {
    let attempts = 0;
    const record = createBoundedProbeWriter(async () => { attempts++; throw new Error('filesystem'); });
    await assert.rejects(record({}), /filesystem/);
    await assert.rejects(record({}), /filesystem/);
    assert.deepEqual(await record({}), { recorded: false });
    assert.equal(attempts, 2);
});

test('only the unique eligible desktop window can submit the probe', () => {
    assert.equal(isUniqueProbeSender(3, [3]), true);
    for (const [sender, candidates] of [[3, []], [3, [4]], [3, [3, 4]], [3, [3, 3]], [0, [0]], ['3', [3]]])
        assert.equal(isUniqueProbeSender(sender, candidates), false);
});

const flush = () => new Promise(resolve => setImmediate(resolve));
test('initial native rejection is handled and only one late observation is scheduled', async () => {
    let errors = 0, writes = 0;
    const timers = [];
    const life = createProbeLifecycle({ observe: () => ({}), publish: async () => { writes++; throw new Error('IPC'); },
        schedule: action => { timers.push(action); return timers.length; }, cancel() {}, onError: () => errors++ });
    assert.equal(life.start(), undefined);
    await flush();
    assert.equal(errors, 1); assert.equal(timers.length, 1);
    timers[0](); await flush();
    assert.equal(errors, 2); assert.equal(writes, 2); assert.equal(timers.length, 1);
    life.stop();
});
test('stop during pending initial publication prevents delayed work', async () => {
    let release, scheduled = 0;
    const pending = new Promise(resolve => { release = resolve; });
    const life = createProbeLifecycle({ observe: () => ({}), publish: () => pending,
        schedule: () => scheduled++, cancel() {}, onError() { assert.fail('unexpected error'); } });
    life.start(); life.stop(); release(); await flush();
    assert.equal(scheduled, 0);
});
test('replacement start and already-dispatched old timer cannot publish after retirement', async () => {
    let writes = 0;
    const timers = [], canceled = [];
    const life = createProbeLifecycle({ observe: () => ({}), publish: async () => { writes++; },
        schedule: action => { timers.push(action); return timers.length; },
        cancel: id => canceled.push(id), onError() { assert.fail('unexpected error'); } });
    life.start(); await flush();
    life.start(); await flush();
    timers[0](); await flush();
    assert.equal(writes, 2); assert.deepEqual(canceled, [1]);
    life.stop(); timers[1](); await flush();
    assert.equal(writes, 2); assert.deepEqual(canceled, [1, 2]);
});
