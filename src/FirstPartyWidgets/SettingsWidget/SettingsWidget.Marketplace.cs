using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsMarketplaceState(MarketplaceSnapshot Snapshot, string? SelectedId = null,
    bool ReviewOpen = false, int ScreenshotIndex = 0, string? Error = null)
{
    internal static SettingsMarketplaceState Empty { get; } = new(MarketplaceSnapshot.Unavailable);
    internal static string Key(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
}

public sealed partial class SettingsWidget
{
    private SettingsMarketplaceState _marketplaceState = SettingsMarketplaceState.Empty;

    private async Task<bool> HandleMarketplaceActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (action.ActionId == "open.marketplace")
        {
            Navigate(SettingsPage.Marketplace);
            await ReadMarketplaceAsync(new("status"), token).ConfigureAwait(false);
            bool initial;
            lock (_stateLock) initial = _marketplaceState.Snapshot.Items.Count != 0 &&
                _marketplaceState.Snapshot.CheckedAtUtc is null && _marketplaceState.Snapshot.Operation is null;
            if (initial) await ReadMarketplaceAsync(new("refresh"), token).ConfigureAwait(false);
            else
            {
                bool canRefreshInventory;
                lock (_stateLock) canRefreshInventory = _marketplaceState.Snapshot.Operation?.State is not ("running" or "review");
                if (canRefreshInventory) await ReadMarketplaceAsync(new("inventory"), token).ConfigureAwait(false);
            }
            return true;
        }
        if (!action.ActionId.StartsWith("marketplace.", StringComparison.Ordinal)) return false;
        if (CurrentPage is not (SettingsPage.Marketplace or SettingsPage.MarketplaceDetails)) return true;
        SettingsMarketplaceState state;
        lock (_stateLock) state = _marketplaceState;
        if (action.ActionId.StartsWith("marketplace.select.", StringComparison.Ordinal))
        {
            var key = action.ActionId["marketplace.select.".Length..];
            var id = state.Snapshot.Items.FirstOrDefault(item => SettingsMarketplaceState.Key(item.Id) == key)?.Id;
            if (id is null) return true;
            lock (_stateLock) _marketplaceState = state with { SelectedId = id, ReviewOpen = false, ScreenshotIndex = 0 };
            Navigate(SettingsPage.MarketplaceDetails);
        }
        else if (action.ActionId is "marketplace.screenshot.next" or "marketplace.screenshot.previous")
        {
            var count = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.SelectedId)?.Screenshots.Count ?? 0;
            if (count > 0) lock (_stateLock) _marketplaceState = state with
            { ScreenshotIndex = (state.ScreenshotIndex + count + (action.ActionId.EndsWith("next", StringComparison.Ordinal) ? 1 : -1)) % count };
        }
        else if (action.ActionId == "marketplace.refresh") await ReadMarketplaceAsync(new("refresh", ForceRefresh: true), token).ConfigureAwait(false);
        else if (action.ActionId == "marketplace.manage" && state.SelectedId is not null)
        {
            await ReloadInstalledWidgetsAsync(token).ConfigureAwait(false);
            int index;
            lock (_stateLock) index = _installedState.CatalogValid
                ? _installedState.Catalog.Widgets.ToList().FindIndex(item => item.Id == state.SelectedId) : -1;
            if (index >= 0) await SelectInstalledWidgetAsync(index, token).ConfigureAwait(false);
            else Navigate(SettingsPage.InstalledWidgets);
        }
        else if (action.ActionId == "marketplace.download" && state.SelectedId is not null)
            await ReadMarketplaceAsync(new("download", PackageId: state.SelectedId), token).ConfigureAwait(false);
        else if (action.ActionId == "marketplace.review.open")
        {
            if (state.Snapshot.Operation is { State: "review", Review: { } review } && review.PackageId == state.SelectedId)
                lock (_stateLock) _marketplaceState = state with { ReviewOpen = true };
        }
        else if (action.ActionId == "marketplace.review.cancel")
        { lock (_stateLock) _marketplaceState = state with { ReviewOpen = false }; }
        else if (action.ActionId == "marketplace.install" && state.ReviewOpen &&
            state.Snapshot.Operation is { State: "review", Review: { Compatible: true } approved } operation && approved.PackageId == state.SelectedId)
        {
            lock (_stateLock) _marketplaceState = state with { ReviewOpen = false };
            await ReadMarketplaceAsync(new("install", OperationId: operation.Id, ReviewToken: approved.Token,
                ApproveFullTrust: approved.FullTrust), token).ConfigureAwait(false);
        }
        else if (action.ActionId == "marketplace.cancel" && state.Snapshot.Operation is { } cancel)
            await ReadMarketplaceAsync(new("cancel", OperationId: cancel.Id), token).ConfigureAwait(false);
        PublishSettingsChanged();
        return true;
    }

    // Called only while the Settings operation gate is held. Jobs are owned by
    // the Bridge; closing this page stops polling, not an already approved install.
    private async Task ReadMarketplaceAsync(MarketplaceCommand command, CancellationToken token)
    {
        try
        {
            var snapshot = await _diagnosticsService.MarketplaceAsync(command, token).ConfigureAwait(false);
            bool changed;
            lock (_stateLock)
            {
                changed = snapshot.Revision != _marketplaceState.Snapshot.Revision || _marketplaceState.Error is not null;
                _marketplaceState = _marketplaceState with { Snapshot = snapshot, Error = null,
                    ReviewOpen = _marketplaceState.ReviewOpen && snapshot.Operation?.State == "review" };
            }
            if (changed) PublishSettingsChanged();
        }
        catch (PlatformDiagnosticsException)
        {
            lock (_stateLock) _marketplaceState = _marketplaceState with
            { Error = "The catalogue request could not complete. Check its status before retrying.", ReviewOpen = false };
            PublishSettingsChanged();
        }
    }
}
