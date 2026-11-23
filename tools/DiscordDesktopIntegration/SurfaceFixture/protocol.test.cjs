'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { validateRequest, createReader } = require('./protocol.cjs');
const token = 'fixture-token';
test('only bounded declarative fixture commands are admitted', () => {
    assert.equal(validateRequest({ id: 1, token, command: 'status' }, token).command, 'status');
    assert.equal(validateRequest({ id: 2, token, command: 'borrow', bounds: { x: -400, y: 20, width: 700, height: 500 } }, token).id, 2);
    assert.equal(validateRequest({ id: 3, token, command: 'prepareReturn', processId: 1234 }, token).processId, 1234);
    for (const value of [null, [], { id: 1, token: 'other', command: 'status' },
        { id: 1, token, command: 'eval' }, { id: 1, token, command: 'status', script: 'bad' },
        { id: 1, token, command: 'borrow', bounds: { x: 0, y: 0, width: 9000, height: 400 } },
        { id: 1, token, command: 'borrow', bounds: { x: 0, y: 0, width: 400, height: 400, hwnd: 1 } }])
        assert.throws(() => validateRequest(value, token));
    for (const processId of [0, -1, 0xffffffff, 123.5, '123', NaN])
        assert.throws(() => validateRequest({ id: 3, token, command: 'prepareReturn', processId }, token));
});
test('pipe reader handles fragments and coalesced records with a strict byte bound', () => {
    const got = [], read = createReader(value => got.push(value));
    read(Buffer.from('{"id":')); read(Buffer.from('1}\n{"id":2}\n'));
    assert.deepEqual(got, [{ id: 1 }, { id: 2 }]);
    assert.throws(() => createReader(() => {})(Buffer.alloc(4097, 32)), /oversized/);
    assert.throws(() => createReader(() => {})(Buffer.from(' '.repeat(4097) + '\n')), /oversized/);
});
