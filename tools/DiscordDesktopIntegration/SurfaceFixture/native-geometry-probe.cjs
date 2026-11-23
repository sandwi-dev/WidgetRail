'use strict';
// Hidden own-process windows only. Proves geometry and teardown, never UI/input
// delivery, native Discord media, clipping, modal behavior or visual acceptance.
const { app, BrowserWindow, session } = require('electron');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const root = path.resolve(__dirname, '../../../artifacts/discord-desktop');
const output = path.resolve(process.argv.find(a => a.startsWith('--output='))?.slice(9) ?? '');
const modulePath = path.resolve(process.argv.find(a => a.startsWith('--geometry-addon='))?.slice(17) ?? '');
for (const candidate of [output, modulePath]) {
    const relative = path.relative(root, candidate);
    if (!relative || path.isAbsolute(relative) || relative.startsWith('..')) throw new Error('Artifact child required');
}
if (fs.existsSync(output) || path.basename(modulePath) !== 'geometry_lease.node') throw new Error('Fresh output and generated addon required');
fs.mkdirSync(output);
const geometry = require(modulePath);
app.setPath('userData', path.join(output, 'profile'));
app.setPath('sessionData', path.join(output, 'session'));
app.on('window-all-closed', () => {});
const checks = [], observations = [];
const check = (name, action) => { action(); checks.push(name); };
const settle = () => new Promise(resolve => setTimeout(resolve, 50));
const deadline = (promise, ms = 5000) => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('generated_fixture_timeout')), ms);
    promise.then(value => { clearTimeout(timer); resolve(value); }, error => { clearTimeout(timer); reject(error); });
});

app.whenReady().then(async () => {
    session.defaultSession.setPermissionRequestHandler((_wc, _permission, done) => done(false));
    session.defaultSession.setPermissionCheckHandler(() => false);
    for (const resizable of [false, true]) {
        const window = new BrowserWindow({ x: 80, y: 80, width: 640, height: 480, frame: false, show: false,
            resizable, webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true } });
        let lease;
        try {
            const handle = window.getNativeWindowHandle();
            const original = geometry.read(handle);
            observations.push({ resizable, phase: 'original', state: original });
            check('invalid handle buffer rejected', () => assert.throws(() => geometry.acquire(Buffer.alloc(1))));
            check('zero handle rejected', () => assert.throws(() => geometry.acquire(Buffer.alloc(handle.length))));
            check('foreground grant rejects invalid or own receiving process', () => {
                for (const receiver of [0, -1, 0xffffffff, 1.5, '123', process.pid])
                    assert.throws(() => geometry.grantForegroundReturn(handle, receiver));
            });
            lease = geometry.acquire(handle);
            check('duplicate active lease rejected', () => assert.throws(() => geometry.acquire(handle)));
            check('invalid placement rejected before mutation', () => {
                assert.throws(() => lease.placeClient({ x: 400, y: 400, width: 0, height: 456 }));
                assert.throws(() => lease.placeClient({ ['x\0suffix']: 400, y: 400, width: 808, height: 456 }));
                assert.deepEqual(geometry.read(handle), original);
            });
            if (!resizable) {
                check('fixed-size client constraint rejects unsupported resize', () => assert.throws(() =>
                    lease.placeClient({ x: 400, y: 400, width: 808, height: 456 }), { code: 'placement_unconfirmed' }));
                observations.push({ resizable, phase: 'fixed-size-refusal', state: geometry.read(handle) });
            }
            for (const requested of (resizable ? [
                { x: 400, y: 400, width: 808, height: 456 },
                { x: -120, y: 113, width: 913, height: 517 }
            ] : [])) {
                try { lease.placeClient(requested); }
                catch (error) {
                    observations.push({ resizable, phase: 'placement-failed', requested, state: geometry.read(handle) });
                    throw error;
                }
                await settle();
                const observed = geometry.read(handle);
                observations.push({ resizable, phase: 'placed', requested, state: observed });
                check('exact physical client placement', () => assert.deepEqual(observed.client, requested));
                check('placement leaves generated window hidden', () => assert.equal(observed.visible, false));
            }
            const result = lease.restore();
            await settle();
            check('restore reports fresh observation', () => assert.equal(result.observed, true));
            check('exact original outer rectangle restored', () => assert.deepEqual(geometry.read(handle).outer, original.outer));
            check('exact original client rectangle restored', () => assert.deepEqual(geometry.read(handle).client, original.client));
            check('repeated restore is idempotent without another window operation', () => {
                assert.equal(lease.restore().observed, false);
                assert.deepEqual(geometry.read(handle).outer, original.outer);
            });
            check('retired lease rejects placement', () => assert.throws(() => lease.placeClient({ x: 1, y: 1, width: 800, height: 450 })));
            check('retired lease rejects reads', () => assert.throws(() => lease.read()));
            lease = geometry.acquire(handle);
            check('new lease allowed after explicit release', () => assert.deepEqual(lease.read().outer, original.outer));
            lease.restore();
            lease = undefined;
        } finally {
            lease?.restore();
            window.destroy();
        }
    }

    const expired = new BrowserWindow({ show: false, frame: false });
    const expiredHandle = expired.getNativeWindowHandle();
    const expiredLease = geometry.acquire(expiredHandle);
    expired.destroy();
    check('destroyed window rejects stale read', () => assert.throws(() => expiredLease.read()));
    check('destroyed window rejects stale placement', () => assert.throws(() => expiredLease.placeClient({ x: 1, y: 1, width: 800, height: 450 })));
    check('destroyed window restore fails safely', () => assert.throws(() => expiredLease.restore()));

    // Source-observed official9266 main-window minimum. The generic surface host
    // must honor this policy; native placement does not disable app constraints.
    const constrained = new BrowserWindow({ x: 80, y: 80, width: 1280, height: 720,
        minWidth: 940, minHeight: 500, resizable: true, frame: false, show: false });
    let constrainedLease;
    try {
        const handle = constrained.getNativeWindowHandle();
        const original = geometry.read(handle);
        constrainedLease = geometry.acquire(handle);
        check('Discord-like minimum rejects an undersized content slot', () => assert.throws(() =>
            constrainedLease.placeClient({ x: 300, y: 300, width: 808, height: 456 }), { code: 'placement_unconfirmed' }));
        const adequate = { x: 300, y: 300, width: 1600, height: 900 };
        constrainedLease.placeClient(adequate);
        await settle();
        check('adequate content slot obeys application minimums', () => assert.deepEqual(geometry.read(handle).client, adequate));
        constrainedLease.restore();
        check('application minimums and original geometry survive restoration', () => {
            assert.deepEqual(constrained.getMinimumSize(), [940, 500]);
            assert.deepEqual(geometry.read(handle).outer, original.outer);
        });
        observations.push({ phase: 'minimum-constraint-restored', state: geometry.read(handle) });
    } finally { constrainedLease?.restore(); constrained.destroy(); }

    const window = new BrowserWindow({ x: 80, y: 80, width: 640, height: 480, show: false, frame: false });
    let child, socket, lease;
    try {
        const handle = window.getNativeWindowHandle();
        const original = geometry.read(handle);
        lease = geometry.acquire(handle);
        lease.placeClient({ x: 300, y: 300, width: 1001, height: 601 });
        const pipe = '\\\\.\\pipe\\wrail-generated-geometry-' + randomBytes(16).toString('hex');
        child = spawn(process.execPath, ['-e',
            'require("node:net").createServer(s=>s.on("error",()=>{})).listen(process.argv[1],()=>process.stdout.write("READY\\n"));', pipe],
            { env: { ...process.env, ELECTRON_RUN_AS_NODE: '1' }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
        await deadline(new Promise((resolve, reject) => {
            child.once('error', reject);
            child.once('exit', () => reject(new Error('generated_host_exited_before_ready')));
            let text = '';
            child.stdout.on('data', bytes => { text += bytes.toString(); if (text === 'READY\n') resolve(); else if (text.length > 64) reject(new Error('bad_generated_host_readiness')); });
        }));
        socket = net.createConnection(pipe);
        socket.on('error', () => {});
        await deadline(new Promise((resolve, reject) => { socket.once('connect', resolve); socket.once('error', reject); }));
        const retired = new Promise((resolve, reject) => socket.once('close', () => {
            try { resolve(lease.restore()); } catch (error) { reject(error); }
        }));
        child.kill(); // Only the process created above; simulates host termination.
        const restored = await deadline(retired);
        check('host-process termination restores original bounds on disconnect', () => {
            assert.equal(restored.observed, true);
            assert.deepEqual(geometry.read(handle).outer, original.outer);
            assert.deepEqual(geometry.read(handle).client, original.client);
        });
        check('client remains alive and window hidden after host termination', () => {
            assert.equal(window.isDestroyed(), false); assert.equal(window.isVisible(), false);
        });
        observations.push({ phase: 'host-exit-restored', state: geometry.read(handle) });
    } finally {
        socket?.destroy();
        if (child && child.exitCode === null) child.kill();
        lease?.restore();
        window.destroy();
    }
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, electron: process.versions.electron,
        checks, observations, scope: 'Hidden generated ordinary-window geometry and host-exit cleanup only. No Discord, media, foreground/input, modal, maximized/minimized or visual acceptance.' }, null, 2));
    app.quit();
}).catch(error => {
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: false, checks, observations,
        failure: String(error), scope: 'Hidden generated-window native geometry only.' }, null, 2));
    app.exit(1);
});
