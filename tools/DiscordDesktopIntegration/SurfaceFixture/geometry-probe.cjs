'use strict';
// Hidden generated-window geometry only. No input, foreground handoff, user
// windows, account, capture or media. This cannot establish an interactive pass.
const { app, BrowserWindow, screen, session } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../../../artifacts/discord-desktop');
const output = path.resolve(process.argv.find(a => a.startsWith('--output='))?.slice(9) ?? '');
const relative = path.relative(root, output);
if (!relative || path.isAbsolute(relative) || relative.startsWith('..') || fs.existsSync(output)) throw new Error('Fresh fixture output required');
fs.mkdirSync(output);
app.setPath('userData', path.join(output, 'profile'));
app.setPath('sessionData', path.join(output, 'session'));
app.on('window-all-closed', () => {});
app.whenReady().then(async () => {
    session.defaultSession.setPermissionRequestHandler((_wc, _permission, done) => done(false));
    session.defaultSession.setPermissionCheckHandler(() => false);
    const wanted = { x: 400, y: 400, width: 808, height: 456 };
    const rows = [];
    for (const resizable of [false, true]) {
        const window = new BrowserWindow({ x: 80, y: 80, width: 640, height: 480,
            frame: false, show: false, resizable, webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true } });
        try {
            const handle = window.getNativeWindowHandle();
            const hwnd = (handle.length === 8 ? handle.readBigUInt64LE() : BigInt(handle.readUInt32LE())).toString();
            const snapshot = () => ({ bounds: window.getBounds(), content: window.getContentBounds(),
                nativeBounds: JSON.parse(execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File',
                    path.join(__dirname, 'Read-GeneratedGeometry.ps1'), '-Window', hwnd, '-ExpectedProcess', String(process.pid)],
                    { encoding: 'utf8', windowsHide: true, timeout: 10000 })),
                physicalBounds: screen.dipToScreenRect(window, window.getBounds()),
                physicalContent: screen.dipToScreenRect(window, window.getContentBounds()), visible: window.isVisible() });
            rows.push({ resizable, phase: 'initial', ...snapshot() });
            const original = window.getBounds();
            const converted = screen.screenToDipRect(null, wanted);
            window.setBounds(converted);
            await new Promise(resolve => setTimeout(resolve, 100));
            rows.push({ resizable, phase: 'setBounds', wanted, converted, ...snapshot() });
            window.setContentBounds(converted);
            await new Promise(resolve => setTimeout(resolve, 100));
            rows.push({ resizable, phase: 'setContentBounds', wanted, converted, ...snapshot() });
            window.setBounds(original);
            await new Promise(resolve => setTimeout(resolve, 100));
            rows.push({ resizable, phase: 'restore', ...snapshot() });
        } finally { window.destroy(); }
    }
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ electron: process.versions.electron, rows,
        scope: 'Hidden generated-window geometry only; not input, clipping, rendering or handoff acceptance.' }, null, 2));
    app.quit();
}).catch(error => {
    fs.writeFileSync(path.join(output, 'error.txt'), String(error)); app.exit(1);
});
