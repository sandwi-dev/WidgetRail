import test from 'node:test';
import assert from 'node:assert/strict';
import { duplexPair } from 'node:stream';
import { randomBytes } from 'node:crypto';
import { DesktopFrames, encodeFrame, maximumFrameBytes, handshakeProof, authenticateDesktopClient } from './desktop-pipe.mjs';

function pair() {
    const [a, b] = duplexPair();
    return { a, b, client: new DesktopFrames(a), server: new DesktopFrames(b) };
}
const instance = '11'.repeat(16), connection = '22'.repeat(16), nonce = '33'.repeat(32);
async function serverHandshake(server, secret, alter = value => value) {
    const hello = await server.read();
    const proof = handshakeProof(secret, 'server', hello.clientNonce, nonce, connection, hello.clientInstance);
    await server.write(alter({ type: 'serverHello', version: 1, connectionId: connection, serverNonce: nonce, serverProof: proof }));
    const response = await server.read();
    assert.equal(response.type, 'clientProof');
    assert.equal(response.proof, handshakeProof(secret, 'client', hello.clientNonce, nonce, connection, hello.clientInstance));
    await server.write({ type: 'ready', connectionId: connection });
}
test('mutual proof completes before application frames and does not alter caller secret', async () => {
    const p = pair(), secret = randomBytes(32), before = Buffer.from(secret);
    try {
        const server = serverHandshake(p.server, secret);
        const result = await authenticateDesktopClient(p.client, { secret, clientInstance: instance });
        await server;
        assert.equal(result.connectionId, connection); assert.deepEqual(secret, before);
        const write = p.server.write({ type: 'fixtureState', value: 'a🟢b' });
        assert.deepEqual(await p.client.read(), { type: 'fixtureState', value: 'a🟢b' }); await write;
    } finally { p.client.close(); p.server.close(); }
});
test('wrong proof, version and extra fields never receive a client proof', async () => {
    for (const alter of [v => ({ ...v, serverProof: '00'.repeat(32) }),
        v => ({ ...v, version: 2 }), v => ({ ...v, extra: true }),
        v => ({ ...v, serverNonce: 'bad' })]) {
        const p = pair(), secret = randomBytes(32);
        try {
            const auth = authenticateDesktopClient(p.client, { secret, clientInstance: instance });
            const rejected = assert.rejects(auth, /authentication_failed|invalid_handshake/);
            await p.server.read();
            p.b.write(encodeFrame(alter({ type: 'serverHello', version: 1, connectionId: connection, serverNonce: nonce, serverProof: '00'.repeat(32) })));
            await rejected;
            assert.equal(p.a.destroyed, true);
        } finally { p.client.close(); p.server.close(); }
    }
});
test('close rejects an outstanding backpressured write without waiting for its callback', async () => {
    const [a, b] = duplexPair();
    const client = new DesktopFrames(a);
    try {
        const writing = client.write({ value: 'pending' });
        const rejected = assert.rejects(writing, /pipe_closed/);
        client.close(); await rejected;
    } finally { client.close(); b.destroy(); }
});
test('proof is bound to role, both nonces, connection and client instance', () => {
    const secret = Buffer.alloc(32, 5);
    const original = handshakeProof(secret, 'server', nonce, nonce, connection, instance);
    for (const args of [['client', nonce, nonce, connection, instance],
        ['server', '44'.repeat(32), nonce, connection, instance], ['server', nonce, '44'.repeat(32), connection, instance],
        ['server', nonce, nonce, '44'.repeat(16), instance], ['server', nonce, nonce, connection, '44'.repeat(16)]])
        assert.notEqual(handshakeProof(secret, ...args), original);
});
test('fragmented header and multibyte payload are decoded only after the whole frame', async () => {
    const p = pair();
    try {
        const bytes = encodeFrame({ type: 'test', text: '字🙂' });
        const read = p.client.read();
        for (const byte of bytes) p.b.write(Buffer.from([byte]));
        assert.deepEqual(await read, { type: 'test', text: '字🙂' });
    } finally { p.client.close(); p.server.close(); }
});
test('invalid lengths, UTF8 and non-object JSON retire the connection', async () => {
    const oversized = Buffer.alloc(4); oversized.writeUInt32LE(maximumFrameBytes + 1);
    for (const bytes of [Buffer.alloc(4), oversized, Buffer.from([1, 0, 0, 0, 0xff]),
        Buffer.from([2, 0, 0, 0, 91, 93]), Buffer.from([4, 0, 0, 0, 110, 117, 108, 108])]) {
        const p = pair();
        try { const read = p.client.read(); p.b.write(bytes); await assert.rejects(read, /invalid_frame/); }
        finally { p.client.close(); p.server.close(); }
    }
});
test('partial EOF, bounded queued messages, overlapping reads and cancellation fail closed', async () => {
    let p = pair();
    try {
        const read = p.client.read(); p.b.end(Buffer.from([9, 0, 0, 0, 123]));
        await assert.rejects(read, /pipe_closed/);
    } finally { p.client.close(); p.server.close(); }
    p = pair();
    try {
        p.b.write(Buffer.concat(Array.from({ length: 9 }, () => encodeFrame({ test: true }))));
        await new Promise(resolve => setImmediate(resolve));
        await assert.rejects(p.client.read(), /invalid_frame/);
    } finally { p.client.close(); p.server.close(); }
    p = pair();
    try {
        const cancellation = new AbortController();
        const read = p.client.read(cancellation.signal);
        await assert.rejects(p.client.read(), /overlapping_read/);
        cancellation.abort(); await assert.rejects(read, /operation_cancelled/);
    } finally { p.client.close(); p.server.close(); }
});
test('handshake cancellation closes ownership and never sends state', async () => {
    const p = pair();
    try {
        const cancellation = new AbortController();
        const auth = authenticateDesktopClient(p.client, { secret: randomBytes(32), clientInstance: instance, signal: cancellation.signal });
        const hello = await p.server.read(); assert.equal(hello.type, 'clientHello');
        cancellation.abort(); await assert.rejects(auth, /operation_cancelled/);
        assert.equal(p.a.destroyed, true);
    } finally { p.client.close(); p.server.close(); }
});
test('duplicate decoded property names and excessive nesting do not disappear in JSON.parse', async () => {
    for (const toJSON of [() => [], () => null, () => 'text', () => undefined])
        assert.throws(() => encodeFrame({ toJSON }), /invalid_frame/);
    for (const raw of ['{"a":1,"a":2}', '{"nested":{"a":1,"\\u0061":2}}',
        '{"a":'.repeat(33) + '1' + '}'.repeat(33)]) {
        const p = pair();
        try {
            const read = p.client.read();
            const body = Buffer.from(raw); const header = Buffer.alloc(4); header.writeUInt32LE(body.length);
            p.b.write(Buffer.concat([header, body]));
            await assert.rejects(read, /invalid_frame/);
        } finally { p.client.close(); p.server.close(); }
    }
    const p = pair();
    try {
        const value = { a: [{ a: 'quoted " and \\ with { } , : [ ]' }], b: { a: 1 } };
        const written = p.server.write(value); assert.deepEqual(await p.client.read(), value); await written;
        assert.throws(() => encodeFrame(JSON.parse('{"a":'.repeat(33) + '{}' + '}'.repeat(33))), /invalid_frame/);
    } finally { p.client.close(); p.server.close(); }
});
