using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;

namespace WidgetRail.WidgetCatalog;

public sealed partial class WidgetCatalog
{
    internal async Task RecoverUninstallMaintenanceAsync(CancellationToken token)
    {
        var staging = Path.Combine(_root, "staging");
        if (!Directory.Exists(staging) || !Directory.EnumerateFiles(staging, ".uninstall-*.pending.json").Any()) return;
        await using var operation = await _operationLock.AcquireAsync(token);
        await RecoverPendingAndCleanupRetiredTreesAsync(token).ConfigureAwait(false);
    }

    private async Task RemoveWidgetDataAsync(string widgetId, IEnumerable<string> publishers, string receiptDirectory,
        IReadOnlyList<BrokerWidgetIdentity>? sourceOwners = null)
    {
        var settingsRoot = _dataSettingsRoot;
        var authorities = publishers.ToHashSet(StringComparer.Ordinal);
        var paths = new PlatformSettingsPaths(settingsRoot);
        if (File.Exists(paths.SettingsFile))
        {
            // Shared by app/CLI uninstall and pending-uninstall recovery. Removing
            // an unused old version never enters this package-data removal path.
            await new PlatformSettingsStore(paths).UpdateAsync(current =>
            {
                var replay = current.ReplayCapture;
                // Full removal also retires intent from an older authority whose
                // inactive package bytes were already cleaned. This only revokes
                // recording intent; it never grants replacement code access.
                if (replay.OwnerPackageId != widgetId) return current;
                return current with { ReplayCapture = new()
                {
                    Revision = checked(replay.Revision + 1),
                    Settings = replay.Settings with { Enabled = false, AutoStart = false },
                } };
            }, CancellationToken.None).ConfigureAwait(false);
        }
        if (sourceOwners is not null)
            await CaptureLibraryStore.RetireSourceOwnersAsync(Path.Combine(settingsRoot, "capture-library-v2"), sourceOwners, CancellationToken.None).ConfigureAwait(false);
        var states = new PrivateStateStore(Path.Combine(settingsRoot, "widget-state"));
        var configuration = new WidgetConfigurationStore(new PlatformSettingsPaths(settingsRoot));
        foreach (var publisher in authorities)
        {
            var identity = new BrokerWidgetIdentity(widgetId, publisher, "uninstall");
            var state = await states.ReadAsync(identity, CancellationToken.None).ConfigureAwait(false);
            if (state.Exists) await states.ClearAsync(identity, new(state.Revision), CancellationToken.None).ConfigureAwait(false);
            await configuration.ClearAsync(widgetId, publisher, CancellationToken.None).ConfigureAwait(false);
        }
        await new BrowserProfileLifecycleStore(settingsRoot).ResetAsync(widgetId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(receiptDirectory))))[..32].ToLowerInvariant(), removed: true, CancellationToken.None).ConfigureAwait(false);
    }
}
