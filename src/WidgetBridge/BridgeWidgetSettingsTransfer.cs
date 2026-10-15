using WidgetRail.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

internal sealed class BridgeWidgetSettingsTransfer(
    string stateRoot, BridgeClientRegistry? registry, Func<CancellationToken, Task> reload) : IAsyncDisposable
{
    private IDisposable? lease;

    internal async Task PrepareAsync(InstalledWidgetVersion previous, InstalledWidgetVersion replacement,
        CancellationToken token)
    {
        if (registry is not null)
            lease = await registry.AcquirePackageUpdateAsync(previous.Id, token).ConfigureAwait(false);
        await new WidgetSettingsTransfer(stateRoot).CopyAsync(previous, replacement, token).ConfigureAwait(false);
    }

    internal async Task ReloadAsync()
    {
        try { await reload(CancellationToken.None).ConfigureAwait(false); }
        finally { lease?.Dispose(); lease = null; }
    }

    public async ValueTask DisposeAsync()
    {
        if (lease is null) return;
        await ReloadAsync().ConfigureAwait(false);
    }
}
