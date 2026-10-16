using WidgetRail.PlatformSettings.BrowserExtensions;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsExtensionsState(ExtensionCatalog Catalog, string? Selected = null, string Profile = "", bool Busy = false, string? Error = null);

public sealed partial class SettingsWidget
{
    private SettingsExtensionsState _extensions = new(ExtensionCatalog.Empty);
    private async Task ReloadExtensionsAsync(CancellationToken token)
    {
        try
        {
            var catalog = await BrowserExtensionCatalogFile.ReadAsync(_store.Paths.RootDirectory, token).ConfigureAwait(false);
            lock (_stateLock)
            {
                _extensions = _extensions with { Catalog = catalog, Busy = false, Error = null };
                if (_page == SettingsPage.ExtensionDetails && !catalog.Library.Any(e => e.Slot == _extensions.Selected)) _page = SettingsPage.Extensions;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        { lock (_stateLock) _extensions = _extensions with { Busy = false, Error = "Extensions could not be loaded. Try Refresh." }; }
        PublishSettingsChanged();
    }
    private async Task<bool> HandleExtensionActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (action.ActionId == "open.extensions")
        {
            lock (_stateLock) { _page = SettingsPage.Extensions; _extensions = _extensions with { Profile = "", Selected = null }; }
            await ReloadExtensionsAsync(token).ConfigureAwait(false); return true;
        }
        if (CurrentPage is not (SettingsPage.Extensions or SettingsPage.ExtensionDetails)) return false;
        if (action.ActionId == "extensions.refresh") { await ReloadExtensionsAsync(token).ConfigureAwait(false); return true; }
        if (action.ActionId.StartsWith("extensions.open.", StringComparison.Ordinal))
        {
            var slot = action.ActionId[16..];
            lock (_stateLock)
                if (_extensions.Catalog.Library.Any(e => e.Slot == slot)) { _extensions = _extensions with { Selected = slot }; _page = SettingsPage.ExtensionDetails; }
            PublishSettingsChanged(); return true;
        }
        if (action.ActionId.StartsWith("extensions.profile.", StringComparison.Ordinal))
        {
            var profile = action.ActionId[19..];
            lock (_stateLock) if (profile == "all" || _extensions.Catalog.Profiles.Any(p => p.Name == profile && !p.Private))
                _extensions = _extensions with { Profile = profile == "all" ? "" : profile };
            PublishSettingsChanged(); return true;
        }
        if (action.ActionId.StartsWith("host.extensions.", StringComparison.Ordinal))
        { lock (_stateLock) _extensions = _extensions with { Busy = true }; PublishSettingsChanged(); return true; }
        return false;
    }
}
