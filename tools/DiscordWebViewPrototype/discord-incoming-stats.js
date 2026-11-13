// Opt-in diagnostic probe, injected only into the isolated test candidate.
// Read-only: no subscriptions, visibility overrides, playback, or call actions.
// The bounded in-page ring contains no IDs, URLs, names, SDP, or media content.
(() => {
    if (location.origin !== 'https://discord.com' || window !== top || globalThis.WidgetRailIncomingStats) return;
    const samples = [];
    let busy = false;
    const number = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
    async function read() {
        const wp = globalThis.WidgetRailWebWebpack;
        const visibility = wp?.findByProps('isIncomingVideoEnabled', 'lastIncomingVideoEnabledChangeTime');
        const result = {
            time: Date.now(), hidden: document.hidden, width: innerWidth, height: innerHeight,
            visibilityAvailable: !!visibility,
            incomingEnabled: visibility ? visibility.isIncomingVideoEnabled() : null,
            videoChangedAt: visibility ? number(visibility.lastIncomingVideoEnabledChangeTime()) : null,
            streams: []
        };
        const connections = wp?.findStore('MediaEngineStore')?.getMediaEngine?.()?.connections ?? [];
        for (const connection of connections) {
            if (connection.context !== 'stream' || result.streams.length >= 4) continue;
            const pc = Object.values(connection).find(value => value &&
                typeof value.getReceivers === 'function' && typeof value.getStats === 'function');
            if (!pc) continue;
            const track = pc.getReceivers().map(receiver => receiver.track).find(value => value?.kind === 'video');
            if (!track) continue;
            const entry = { live: track.readyState === 'live', muted: track.muted, enabled: track.enabled, inbound: [] };
            let timeout;
            try {
                const report = await Promise.race([pc.getStats(), new Promise((_, reject) => {
                    timeout = setTimeout(() => reject(new Error('stats_timeout')), 1500);
                })]);
                for (const row of report.values()) {
                    if (row.type !== 'inbound-rtp' || (row.kind ?? row.mediaType) !== 'video' || entry.inbound.length >= 4) continue;
                    entry.inbound.push({ bytes: number(row.bytesReceived), received: number(row.framesReceived),
                        decoded: number(row.framesDecoded), fps: number(row.framesPerSecond),
                        width: number(row.frameWidth), height: number(row.frameHeight) });
                }
            } catch { entry.unavailable = true; }
            finally { clearTimeout(timeout); }
            result.streams.push(entry);
        }
        return result;
    }
    async function sample() {
        if (busy) return;
        busy = true;
        try { samples.push(await read()); if (samples.length > 300) samples.shift(); }
        catch { samples.push({ time: Date.now(), unavailable: true }); if (samples.length > 300) samples.shift(); }
        finally { busy = false; }
    }
    const timer = setInterval(sample, 2000);
    globalThis.WidgetRailIncomingStats = Object.freeze({
        read, history: () => structuredClone(samples), stop: () => clearInterval(timer)
    });
    void sample();
})();
