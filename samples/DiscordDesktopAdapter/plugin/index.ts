// This Phase A plugin is read-only. It opens no socket, starts no call/share,
// requests no permission and does not change Discord settings or window state.
import definePlugin, { PluginNative, StartAt } from '@utils/types';
import { findStore } from '@webpack';
import { readDesktopProbe } from './provider.mjs';
import { createProbeLifecycle } from './probe-lifecycle.mjs';

const Native = VencordNative.pluginHelpers.WidgetRailDesktop as PluginNative<typeof import('./native')>;
const lifecycle = createProbeLifecycle({
    observe: () => {
        const scope = window as unknown as { DiscordNative?: unknown; createDiscordStream?: unknown };
        return readDesktopProbe({ findStore, discordNative: scope.DiscordNative,
            createDiscordStream: scope.createDiscordStream });
    },
    publish: (summary: unknown) => Native.recordProbe(summary),
    schedule: (action: () => void, milliseconds: number) => setTimeout(action, milliseconds),
    cancel: (timer: ReturnType<typeof setTimeout>) => clearTimeout(timer),
    onError: () => console.warn('[WidgetRailDesktop] Capability observation unavailable.')
});

export default definePlugin({
    name: 'WidgetRailDesktop',
    description: 'WidgetRail Desktop integration: read-only capability qualification.',
    authors: [{ name: 'WidgetRail', id: 0n }],
    enabledByDefault: true,
    startAt: StartAt.WebpackReady,
    start() { lifecycle.start(); },
    stop() { lifecycle.stop(); }
});
