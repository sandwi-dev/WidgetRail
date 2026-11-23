// Uses only generated local peers and a public synthetic test key. Never reads
// the installed adapter, pairing configuration, account state or Discord process.
import { spawn } from 'node:child_process';
import { access } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import { connectDesktopPipe } from '../../samples/DiscordDesktopAdapter/plugin/desktop-pipe.mjs';

if (process.platform !== 'win32') throw new Error('Windows named-pipe interop requires Windows.');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const assembly = resolve(root, 'tests/DiscordDesktopConnection.Tests/bin/Release/net8.0/DiscordDesktopConnection.Tests.dll');
await access(assembly);
const pipeName = 'wrail-discord-desktop-' + randomBytes(16).toString('hex');
const child = spawn('dotnet', [assembly, '--interop-server', pipeName, String(process.pid)],
    { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
let output = '', errors = '', connection;
let readyResolve, readyReject;
const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
const exited = new Promise(resolve => {
    child.once('close', (code, signal) => {
        if (!output.includes('LISTENING')) readyReject(new Error('Synthetic server exited before readiness.'));
        resolve({ code, signal });
    });
});
child.on('error', () => readyReject(new Error('Synthetic server could not launch.')));
child.stdout.on('data', bytes => {
    if (output.length + bytes.length > 16384) { readyReject(new Error('Synthetic server output exceeded bound.')); child.kill(); return; }
    output += bytes.toString('utf8');
    if (output.includes('LISTENING')) readyResolve();
});
child.stderr.on('data', bytes => {
    if (errors.length + bytes.length > 16384) { child.kill(); return; }
    errors += bytes.toString('utf8');
});
const deadline = AbortSignal.timeout(15000);
const bounded = promise => Promise.race([promise, new Promise((_, reject) => {
    if (deadline.aborted) reject(new Error('Synthetic interop deadline expired.'));
    else deadline.addEventListener('abort', () => reject(new Error('Synthetic interop deadline expired.')), { once: true });
})]);
let failure;
try {
    await bounded(ready);
    connection = await connectDesktopPipe({ pipeName, secret: Buffer.from(Array.from({ length: 32 }, (_, i) => i)),
        clientInstance: randomBytes(16).toString('hex'), signal: deadline });
    // The connected pipe must survive beyond both implementations' five-second
    // handshake deadline. This is one functional lifetime check, not a benchmark.
    await bounded(new Promise(resolve => setTimeout(resolve, 5200)));
    await connection.channel.write({ type: 'ping' }, deadline);
    assert.deepEqual(await connection.channel.read(deadline), { type: 'pong' });
    const exit = await bounded(exited);
    assert.equal(exit.code, 0); assert.ok(output.includes('INTEROP PASS')); assert.equal(errors, '');
    console.log(JSON.stringify({ passed: true, serverExited: true, authenticated: true,
        survivedSetupDeadline: true, scope: '.NET server to Node client on a real local Windows pipe; generated peers only.' }));
} catch (error) { failure = error; }
finally {
    connection?.channel.close();
    if (child.exitCode === null && child.signalCode === null) {
        child.kill();
        await Promise.race([exited, new Promise((_, reject) => setTimeout(() => reject(new Error('Synthetic server did not exit.')), 3000))]);
    }
}
if (failure) {
    // This process contains only synthetic fixture data, never real Discord state.
    if (errors) console.error(errors);
    throw failure;
}
