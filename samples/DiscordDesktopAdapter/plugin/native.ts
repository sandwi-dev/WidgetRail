import { app, BrowserWindow, IpcMainInvokeEvent } from 'electron';
import { mkdir, writeFile, rename } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { admittedOrigin, validateProbe } from './probe-contract.mjs';
import { createBoundedProbeWriter, isUniqueProbeSender } from './probe-lifecycle.mjs';

const instance = randomUUID();
const record = createBoundedProbeWriter(async (value: unknown) => {
    const localData = process.env.LOCALAPPDATA;
    if (!localData || process.platform !== 'win32') throw new Error('probe_platform_unavailable');
    const directory = join(localData, 'WidgetRail', 'DiscordDesktop', 'diagnostics');
    await mkdir(directory, { recursive: true });
    const temporary = join(directory, 'capabilities-' + instance + '.tmp');
    await writeFile(temporary, JSON.stringify(value, null, 2), { encoding: 'utf8' });
    await rename(temporary, join(directory, 'capabilities-' + instance + '.json'));
});

// Vencord exposes exported native functions over renderer IPC. Accept only a
// bounded schema from the top-level Discord frame; no renderer-supplied path.
export async function recordProbe(event: IpcMainInvokeEvent, value: unknown) {
    if (event.sender.isDestroyed() || !admittedOrigin(event.senderFrame?.url,
        event.senderFrame === event.sender.mainFrame)) throw new Error('probe_origin_rejected');
    const candidates = BrowserWindow.getAllWindows().filter(window => !window.isDestroyed() &&
        window.getParentWindow() === null && admittedOrigin(window.webContents.getURL(), true))
        .map(window => window.webContents.id);
    if (!isUniqueProbeSender(event.sender.id, candidates)) throw new Error('probe_window_ambiguous');
    const summary = validateProbe(value);
    const now = Date.now();
    return await record({ recordedAt: new Date(now).toISOString(), instance,
        desktopVersion: app.getVersion(), electronVersion: process.versions.electron,
        processId: process.pid, ...summary,
        scope: 'Read-only interface/state observation; not proof of voice, capture, rendering, hardware encoding or feature parity.'
    });
}
