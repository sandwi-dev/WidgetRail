'use strict';
// Generated local test client only. This is not loaded into Discord or Vencord.
const { app, BrowserWindow, screen, session } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { validateRequest, createReader } = require('./protocol.cjs');

const configArg = process.argv.find(a => a.startsWith('--fixture-config='));
if (!configArg) throw new Error('Missing generated fixture configuration');
const configPath = path.resolve(configArg.slice('--fixture-config='.length));
const bytes = fs.readFileSync(configPath);
if (bytes.length > 16384) throw new Error('Oversized fixture configuration');
const config = JSON.parse(bytes);
if (!/^wrail-desktop-surface-[a-f0-9]{32}$/.test(config.pipeName) ||
    !/^[a-f0-9]{64}$/.test(config.token)) throw new Error('Invalid fixture configuration');
// Run configurations live in a fresh run directory under the artifact root. The launcher
// verifies the native binary hash; never accept a module path from pipe traffic.
const sourceRoot = path.dirname(path.dirname(configPath));
const nativePath = path.resolve(config.nativeGeometryPath ?? '');
const nativeRelative = path.relative(sourceRoot, nativePath);
if (!nativeRelative || path.isAbsolute(nativeRelative) || nativeRelative.startsWith('..') ||
    path.basename(nativePath) !== 'geometry_lease.node') throw new Error('Generated native geometry path required');
const geometry = require(nativePath);
const output = path.dirname(configPath);
app.setPath('userData', path.join(output, 'electron-profile'));
app.setPath('sessionData', path.join(output, 'electron-session'));

let window, server, client, borrowed = false, original, closing = false, geometryLease, watchdog, returnEligibilityGranted = false;
const windowFile = path.join(__dirname, 'surface.html');
function snapshot() {
    const handle = window.getNativeWindowHandle();
    return { processId: process.pid, electronVersion: process.versions.electron,
        window: (handle.length === 8 ? handle.readBigUInt64LE() : BigInt(handle.readUInt32LE())).toString(),
        bounds: window.getBounds(), physicalBounds: screen.dipToScreenRect(window, window.getBounds()),
        originalBounds: original, visible: window.isVisible(),
        focused: window.isFocused(), borrowed, returnEligibilityGranted,
        scope: 'Generated window only; no Discord, media or account.' };
}
function restore() {
    if (!window || window.isDestroyed()) return;
    borrowed = false;
    window.hide();
    window.setAlwaysOnTop(false);
    geometryLease?.restore();
    geometryLease = undefined;
}
function finish() {
    if (closing) return;
    closing = true;
    clearTimeout(watchdog);
    try { restore(); }
    catch { fs.writeFileSync(path.join(output, 'helper-error.txt'), 'Generated native restoration failed.'); }
    finally {
        client?.destroy();
        server?.close();
        app.quit();
    }
}
app.whenReady().then(async () => {
    // No permission, external link or network resource is admitted.
    session.defaultSession.setPermissionRequestHandler((_wc, _permission, done) => done(false));
    session.defaultSession.setPermissionCheckHandler(() => false);
    session.defaultSession.webRequest.onBeforeRequest({ urls: ['http://*/*', 'https://*/*', 'ws://*/*', 'wss://*/*'] }, (_details, done) => done({ cancel: true }));
    window = new BrowserWindow({ title: 'WidgetRail generated desktop surface',
        x: 80, y: 80, width: 640, height: 480, frame: false, show: false,
        // Official9266 mainWindowOptions is resizable:true. Fixed-size refusal
        // remains a separate hidden regression case; never bypass its constraints.
        resizable: true, webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true } });
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.on('will-navigate', event => event.preventDefault());
    await window.loadFile(windowFile);
    original = window.getBounds();
    server = net.createServer(socket => {
        if (client) { socket.destroy(); return; }
        client = socket;
        // Human readiness and an executing check have different lifetimes. A
        // short startup timer must not expire while the user reads the prompt.
        clearTimeout(watchdog);
        watchdog = setTimeout(finish, 120000);
        watchdog.unref();
        let count = 0, lastId = 0;
        const read = createReader(raw => {
            const request = validateRequest(raw, config.token);
            if (++count > 16 || request.id <= lastId) throw new Error('request_limit');
            lastId = request.id;
            if (request.command === 'borrow') {
                if (borrowed || geometryLease) throw new Error('already_borrowed');
                geometryLease = geometry.acquire(window.getNativeWindowHandle());
                borrowed = true;
                geometryLease.placeClient(request.bounds);
                window.setAlwaysOnTop(true);
                window.showInactive();
                window.focus();
            } else if (request.command === 'prepareReturn') {
                if (!borrowed || !geometryLease || returnEligibilityGranted) throw new Error('return_not_admitted');
                geometryLease.read(); // Exact current own-window lease, not a reused HWND.
                returnEligibilityGranted = geometry.grantForegroundReturn(window.getNativeWindowHandle(), request.processId);
            } else if (request.command === 'restore') restore();
            const response = { id: request.id, state: snapshot() };
            socket.write(JSON.stringify(response) + '\n');
            if (request.command === 'close') setImmediate(finish);
        });
        socket.on('data', chunk => { try { read(chunk); } catch { socket.destroy(); } });
        socket.on('error', () => {});
        socket.on('close', finish);
    });
    server.on('error', finish);
    server.listen('\\\\.\\pipe\\' + config.pipeName, () => {
        fs.writeFileSync(path.join(output, 'helper-ready.json'), JSON.stringify(snapshot(), null, 2));
    });
    // Bounded idle watchdog; once the real host connects the shorter test timer
    // above applies. The WinUI action has its own20-second operation deadline.
    watchdog = setTimeout(finish, 15 * 60 * 1000);
    watchdog.unref();
}).catch(error => {
    fs.writeFileSync(path.join(output, 'helper-error.txt'), String(error));
    app.quit();
});
app.on('window-all-closed', finish);
