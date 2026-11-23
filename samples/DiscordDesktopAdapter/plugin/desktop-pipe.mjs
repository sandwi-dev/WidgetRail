// Private desktop control-plane foundation. Not imported by the installed probe.
// Pairing/provisioning and domain command routing are separate admission layers.
import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { createConnection } from 'node:net';
import { once } from 'node:events';

export const maximumFrameBytes = 256 * 1024;
const maximumQueuedFrames = 8;
const noncePattern = /^[0-9a-f]{64}$/;
const idPattern = /^[0-9a-f]{32}$/;
const error = code => new Error(code);

function object(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function fields(value, names) {
    if (!object(value) || Object.keys(value).length !== names.length || names.some(name => !Object.hasOwn(value, name)))
        throw error('invalid_handshake');
}
// JSON.parse discards duplicate properties. Scan only structural tokens/key
// strings before standard parsing, so ambiguity and excessive nesting cannot
// disappear during decoding. JSON.parse still owns complete syntax validation.
function inspectJsonStructure(text) {
    const scopes = [];
    for (let i = 0; i < text.length; i++) {
        const character = text[i];
        if (character === '"') {
            const start = i;
            for (i++; i < text.length; i++) {
                if (text[i] === '\\') i++;
                else if (text[i] === '"') break;
            }
            const scope = scopes.at(-1);
            if (scope?.keys && scope.expectKey) {
                const key = JSON.parse(text.slice(start, i + 1));
                if (scope.keys.has(key)) throw error('invalid_frame');
                scope.keys.add(key); scope.expectKey = false;
            }
        } else if (character === '{' || character === '[') {
            scopes.push(character === '{' ? { keys: new Set(), expectKey: true } : {});
            if (scopes.length > 32) throw error('invalid_frame');
        } else if (character === '}' || character === ']') scopes.pop();
        else if (character === ',' && scopes.at(-1)?.keys) scopes.at(-1).expectKey = true;
    }
}
export function encodeFrame(value) {
    if (!object(value)) throw error('invalid_frame');
    const text = JSON.stringify(value);
    if (typeof text !== 'string' || text[0] !== '{') throw error('invalid_frame');
    const payload = Buffer.from(text, 'utf8');
    if (!payload.length || payload.length > maximumFrameBytes) throw error('frame_too_large');
    inspectJsonStructure(text);
    const packet = Buffer.allocUnsafe(4 + payload.length);
    packet.writeUInt32LE(payload.length);
    payload.copy(packet, 4);
    return packet;
}

// One reader/writer and a fixed receive-queue bound. This owns only its supplied
// duplex stream; it cannot open another endpoint or retry an ambiguous operation.
export class DesktopFrames {
    #stream; #header = Buffer.alloc(4); #headerUsed = 0; #body = null; #bodyUsed = 0;
    #queue = []; #waiting = null; #failure = null; #writing = false; #writeReject = null;
    constructor(stream) {
        this.#stream = stream;
        stream.on('data', this.#receive);
        stream.on('error', this.#ioError);
        stream.on('end', this.#ended);
        stream.on('close', this.#ended);
    }
    #ioError = () => this.#fail('pipe_failed');
    #ended = () => this.#fail('pipe_closed');
    #fail(code) {
        if (this.#failure) return;
        this.#failure = error(code);
        this.#body = null; this.#headerUsed = 0; this.#bodyUsed = 0; this.#queue.length = 0;
        const waiting = this.#waiting; this.#waiting = null;
        waiting?.reject(this.#failure);
        this.#writeReject?.(this.#failure);
        this.#stream.destroy();
    }
    #receive = chunk => {
        if (this.#failure) return;
        try {
            if (!Buffer.isBuffer(chunk)) throw error('invalid_frame');
            for (let offset = 0; offset < chunk.length;) {
                if (!this.#body) {
                    const count = Math.min(4 - this.#headerUsed, chunk.length - offset);
                    chunk.copy(this.#header, this.#headerUsed, offset, offset + count);
                    offset += count; this.#headerUsed += count;
                    if (this.#headerUsed !== 4) continue;
                    const size = this.#header.readUInt32LE();
                    if (!size || size > maximumFrameBytes) throw error('invalid_frame');
                    this.#body = Buffer.allocUnsafe(size); this.#bodyUsed = 0;
                }
                const count = Math.min(this.#body.length - this.#bodyUsed, chunk.length - offset);
                chunk.copy(this.#body, this.#bodyUsed, offset, offset + count);
                offset += count; this.#bodyUsed += count;
                if (this.#bodyUsed !== this.#body.length) continue;
                if (this.#body[0] === 0xef && this.#body[1] === 0xbb && this.#body[2] === 0xbf) throw error('invalid_frame');
                const text = new TextDecoder('utf-8', { fatal: true }).decode(this.#body);
                inspectJsonStructure(text);
                const value = JSON.parse(text);
                if (!object(value)) throw error('invalid_frame');
                this.#body = null; this.#headerUsed = 0; this.#bodyUsed = 0;
                if (this.#waiting) {
                    const waiting = this.#waiting; this.#waiting = null; waiting.resolve(value);
                } else {
                    if (this.#queue.length >= maximumQueuedFrames) throw error('receive_queue_exceeded');
                    this.#queue.push(value);
                }
            }
        } catch { this.#fail('invalid_frame'); }
    };
    async read(signal) {
        if (signal?.aborted) { this.#fail('operation_cancelled'); throw this.#failure; }
        if (this.#failure) throw this.#failure;
        if (this.#waiting) throw error('overlapping_read');
        if (this.#queue.length) return this.#queue.shift();
        let abort;
        try {
            return await new Promise((resolve, reject) => {
                this.#waiting = { resolve, reject };
                abort = () => this.#fail('operation_cancelled');
                signal?.addEventListener('abort', abort, { once: true });
            });
        } finally { if (abort) signal?.removeEventListener('abort', abort); }
    }
    async write(value, signal) {
        if (signal?.aborted) { this.#fail('operation_cancelled'); throw this.#failure; }
        if (this.#failure) throw this.#failure;
        if (this.#writing) throw error('overlapping_write');
        const packet = encodeFrame(value);
        this.#writing = true;
        let abort;
        try {
            await new Promise((resolve, reject) => {
                this.#writeReject = reject;
                abort = () => { this.#fail('operation_cancelled'); reject(this.#failure); };
                signal?.addEventListener('abort', abort, { once: true });
                this.#stream.write(packet, failure => {
                    if (failure) { this.#fail('pipe_failed'); reject(this.#failure); }
                    else if (this.#failure) reject(this.#failure);
                    else resolve();
                });
            });
        } catch { this.#fail('pipe_failed'); throw this.#failure; }
        finally { this.#writing = false; this.#writeReject = null; if (abort) signal?.removeEventListener('abort', abort); }
    }
    close() { this.#fail('pipe_closed'); }
}

export function handshakeProof(secret, role, clientNonce, serverNonce, connectionId, clientInstance) {
    if (!Buffer.isBuffer(secret) || secret.length !== 32 || !['server', 'client'].includes(role) ||
        [clientNonce, serverNonce, connectionId, clientInstance].some(value => typeof value !== 'string') ||
        !noncePattern.test(clientNonce) || !noncePattern.test(serverNonce) ||
        !idPattern.test(connectionId) || !idPattern.test(clientInstance)) throw error('invalid_pairing');
    const transcript = ['WidgetRail.DiscordDesktop.v1', role, clientNonce, serverNonce, connectionId, clientInstance].join('\n');
    return createHmac('sha256', secret).update(transcript, 'utf8').digest('hex');
}
function verifyProof(received, expected) {
    return typeof received === 'string' && noncePattern.test(received) &&
        timingSafeEqual(Buffer.from(received, 'hex'), Buffer.from(expected, 'hex'));
}

export async function authenticateDesktopClient(channel, { secret, clientInstance, signal }) {
    if (!Buffer.isBuffer(secret) || secret.length !== 32 || typeof clientInstance !== 'string' || !idPattern.test(clientInstance)) {
        channel.close(); throw error('invalid_pairing');
    }
    const key = Buffer.from(secret);
    const deadline = AbortSignal.timeout(5000);
    const bounded = signal ? AbortSignal.any([signal, deadline]) : deadline;
    try {
        const clientNonce = randomBytes(32).toString('hex');
        await channel.write({ type: 'clientHello', version: 1, clientNonce, clientInstance }, bounded);
        const challenge = await channel.read(bounded);
        fields(challenge, ['type', 'version', 'connectionId', 'serverNonce', 'serverProof']);
        if (challenge.type !== 'serverHello' || challenge.version !== 1 ||
            typeof challenge.connectionId !== 'string' || !idPattern.test(challenge.connectionId) ||
            typeof challenge.serverNonce !== 'string' || !noncePattern.test(challenge.serverNonce)) throw error('invalid_handshake');
        const expected = handshakeProof(key, 'server', clientNonce, challenge.serverNonce, challenge.connectionId, clientInstance);
        if (!verifyProof(challenge.serverProof, expected)) throw error('authentication_failed');
        await channel.write({ type: 'clientProof', proof: handshakeProof(key, 'client', clientNonce,
            challenge.serverNonce, challenge.connectionId, clientInstance) }, bounded);
        const ready = await channel.read(bounded);
        fields(ready, ['type', 'connectionId']);
        if (ready.type !== 'ready' || ready.connectionId !== challenge.connectionId) throw error('invalid_handshake');
        return { channel, connectionId: ready.connectionId };
    } catch (failure) { channel.close(); throw failure; }
    finally { key.fill(0); }
}

export async function connectDesktopPipe({ pipeName, secret, clientInstance, signal }) {
    if (process.platform !== 'win32' || typeof pipeName !== 'string' ||
        !/^wrail-discord-desktop-[0-9a-f]{32}$/.test(pipeName)) throw error('invalid_pipe_name');
    const deadline = AbortSignal.timeout(5000);
    const bounded = signal ? AbortSignal.any([signal, deadline]) : deadline;
    // The setup deadline must not remain attached to a successful socket: doing
    // so would terminate every healthy session five seconds after connecting.
    const socket = createConnection({ path: '\\\\.\\pipe\\' + pipeName });
    const channel = new DesktopFrames(socket);
    try {
        await once(socket, 'connect', { signal: bounded });
        return await authenticateDesktopClient(channel, { secret, clientInstance, signal: bounded });
    } catch { channel.close(); throw error('desktop_connection_failed'); }
}
