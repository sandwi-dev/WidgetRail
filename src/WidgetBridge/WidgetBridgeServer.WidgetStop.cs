using WidgetRail.PlatformSettings;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private readonly SemaphoreSlim widgetStopGate = new(1, 1);
    private readonly WidgetStopStateStore? runtimeStopStore;

    private async Task SetWidgetStoppedAsync(string widgetId, bool stopped, CancellationToken token)
    {
        if (runtimeStopStore is null) throw new BridgeProtocolException("Persistent widget lifecycle settings are unavailable.");
        await widgetStopGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var (catalog, _) = _registry.CatalogSnapshot();
            var target = catalog.GetConfigured(widgetId);
            if (stopped && !target.CanStop)
                throw new BridgeProtocolException("Settings must remain available and cannot be stopped.");
            var saved = await runtimeStopStore.SetStoppedAsync(target.PackageId, stopped, token).ConfigureAwait(false);
            // Once persisted, publish even if the caller has left the page. Never
            // acknowledge a stop while the old worker can accept new operations.
            if (_catalogMonitor is not null) _catalogMonitor.PublishStoppedWidgets(saved);
            else
            {
                var (latest, revision) = _registry.CatalogSnapshot();
                ApplyCatalog(latest.WithStoppedWidgets(saved), revision + 1);
            }
            await _registry.WaitForWidgetRetirementAsync(widgetId).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformSettingsException or TimeoutException or AggregateException)
        { throw new BridgeProtocolException("The widget lifecycle change could not finish. Check Settings diagnostics before retrying."); }
        finally { widgetStopGate.Release(); }
    }
}
