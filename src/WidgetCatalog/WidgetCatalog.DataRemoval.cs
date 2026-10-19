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

    private async Task RemoveWidgetDataAsync(string widgetId, IEnumerable<string> publishers, string receiptDirectory)
    {
        var settingsRoot = _dataSettingsRoot;
        var states = new PrivateStateStore(Path.Combine(settingsRoot, "widget-state"));
        var configuration = new WidgetConfigurationStore(new PlatformSettingsPaths(settingsRoot));
        foreach (var publisher in publishers)
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
