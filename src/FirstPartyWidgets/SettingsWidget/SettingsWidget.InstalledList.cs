using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetCatalog;
using WidgetRail.PlatformSettings;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsInstalledRow(string PackageId, bool BuiltIn, string Name, string Version,
    string Status, WidgetGlyph Glyph, bool Busy, bool Disabled)
{
    internal WidgetPackageIcon? PackageIcon { get; init; }
    internal string? ContentDigest { get; init; }
    internal WidgetIcon Icon => PackageIcon is { } icon
        ? WidgetIcon.CatalogSvg(new(PackageId, Version, BuiltIn, ContentDigest), icon.AssetId, icon.ColorMode, Glyph)
        : WidgetIcon.Glyph(Glyph);
    internal string Key { get; } = KeyFor(PackageId, BuiltIn);
    internal static string KeyFor(string id, bool builtIn) => (builtIn ? "builtin." : "installed.") +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
}

internal sealed record SettingsInstalledListView(WidgetElement Content, string Id, int Count, FocusGroupEntryRequest? ReturnFocus);

public sealed partial class SettingsWidget
{
    private WidgetLoadedCollection<SettingsWidgetFilter, SettingsInstalledRow> installedList = null!;
    private IReadOnlyList<SettingsInstalledRow> installedRows => installedList?.Items ?? [];
    private SettingsInstalledWidgetState? installedRowsState;
    private BuiltInWidgetSettings? installedRowsPreferences;
    private bool installedRowsBusy;
    private IReadOnlyList<string>? installedRowsStops;
    private IReadOnlyList<string> stoppedWidgetIds = [];
    private bool installedListWasShown;
    private string? installedReturnKey;
    private long installedEntryRevision;
    private static string InstalledListId(SettingsWidgetFilter filter) => "installed.list." + filter.ToString().ToLowerInvariant();

    private void InitializeInstalledList()
    {
        installedList = CreateLoadedCollection<SettingsWidgetFilter, SettingsInstalledRow>("settings.widgets",
            _installedState.Filter.ToString(), _installedState.Filter, [], new()
        {
            ItemKey = row => new(row.Key),
            RenderItem = (_, row, context) => SettingsInstalledWidgetPresentation.InstalledEntry(row, context.Id("row")),
            OnAction = OpenInstalledRowAsync,
        });
    }

    // Keep the derived list in step with committed Settings state, outside Render.
    // All Settings mutations publish through this method. Content-only updates
    // retain membership and scroll/focus keys; filtering/reordering retires leases.
    private void PublishSettingsChanged()
    {
        lock (_stateLock)
        {
            if (installedList is not null && !WidgetLifetimeToken.IsCancellationRequested)
            {
                var rowBusy = _busy || _initializing;
                if (_page == SettingsPage.InstalledWidgets && (installedRowsState is null || !ReferenceEquals(installedRowsState.Catalog, _installedState.Catalog) ||
                    !ReferenceEquals(installedRowsState.BuiltIns, _installedState.BuiltIns) || installedRowsState.Filter != _installedState.Filter ||
                    installedRowsState.CatalogValid != _installedState.CatalogValid || !ReferenceEquals(installedRowsPreferences, _settings.BuiltInWidgets) ||
                    !ReferenceEquals(installedRowsStops, stoppedWidgetIds) || installedRowsBusy != rowBusy))
                {
                    installedRowsState = _installedState;
                    installedRowsPreferences = _settings.BuiltInWidgets;
                    installedRowsBusy = rowBusy;
                    installedRowsStops = stoppedWidgetIds;
                    var rows = new List<SettingsInstalledRow>();
                    if (_installedState.Filter != SettingsWidgetFilter.Installed)
                        rows.AddRange(_installedState.BuiltIns.Select(manifest => new SettingsInstalledRow(manifest.Id, true,
                            manifest.Name, manifest.Version, "Built-in · " + (_settings.BuiltInWidgets.IsEnabled(manifest.Id) ? stoppedWidgetIds.Contains(manifest.Id) ? "Stopped" : "Enabled" : "Disabled"),
                            manifest.Presentation.Icon, rowBusy, !_installedState.CatalogValid) { PackageIcon = manifest.Presentation.PackageIcon }));
                    if (_installedState.Filter != SettingsWidgetFilter.BuiltIn)
                        foreach (var widget in _installedState.Catalog.Widgets)
                        {
                            var compatible = WidgetHostCompatibility.Evaluate(widget.ActiveVersion.Manifest).IsSupported;
                            var duplicate = _installedState.BuiltIns.Any(manifest => manifest.Id == widget.Id);
                            var status = duplicate ? "Unused copy · built-in version takes priority" : widget.Enabled
                                ? compatible ? stoppedWidgetIds.Contains(widget.Id) ? "Stopped" : "Enabled" : "Enabled · incompatible" : compatible ? "Disabled" : "Incompatible";
                            rows.Add(new(widget.Id, false, widget.Name, widget.ActiveVersion.Version.ToString(),
                                (_installedState.CatalogValid ? "" : "Last good · ") + status,
                                widget.ActiveVersion.Manifest.Presentation.Icon, rowBusy, false)
                                { PackageIcon = widget.ActiveVersion.Manifest.Presentation.PackageIcon, ContentDigest = widget.ActiveVersion.ContentDigest });
                        }
                    installedList.Publish(_installedState.Filter.ToString(), _installedState.Filter, rows);
                }
                if (_page == SettingsPage.InstalledWidgets && !installedListWasShown)
                {
                    var selectedId = _installedState.SelectedBuiltInId ?? _installedState.SelectedInstalledId;
                    installedReturnKey = selectedId is null ? null : SettingsInstalledRow.KeyFor(selectedId, _installedState.SelectedBuiltInId is not null);
                    installedEntryRevision = CreateFocusRequest().Id;
                }
                installedListWasShown = _page == SettingsPage.InstalledWidgets;
            }
        }
        Invalidate();
    }

    // Called while capturing the immutable presentation state under _stateLock.
    private SettingsInstalledListView CaptureInstalledList()
    {
        var id = InstalledListId(_installedState.Filter);
        var index = -1;
        if (installedReturnKey is not null)
            for (var candidate = 0; candidate < installedRows.Count; candidate++)
                if (installedRows[candidate].Key == installedReturnKey) { index = candidate; break; }
        var entry = index < 0 ? null : installedList.Enter(id, installedEntryRevision,
            installedList.FocusTarget(id, new(installedRows[index].Key), index));
        return new(UI.CollectionList(id, installedList, 72, "Installed widgets").Classes("installed-widget-list"), id, installedRows.Count, entry);
    }

    private async ValueTask OpenInstalledRowAsync(SettingsWidgetFilter _, SettingsInstalledRow row, WidgetActionEvent action, CancellationToken token)
    {
        if (action.ActionId != "installed.open") return;
        await _operationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            int index;
            lock (_stateLock)
            {
                if (!IsActive || _page != SettingsPage.InstalledWidgets || _busy || _initializing) return;
                if (_installedState.Filter != SettingsWidgetFilter.All && row.BuiltIn != (_installedState.Filter == SettingsWidgetFilter.BuiltIn)) return;
                index = row.BuiltIn ? _installedState.BuiltIns.ToList().FindIndex(item => item.Id == row.PackageId)
                    : _installedState.Catalog.Widgets.ToList().FindIndex(item => item.Id == row.PackageId);
            }
            if (index < 0) return;
            if (row.BuiltIn) await SelectBuiltInWidgetAsync(index, token).ConfigureAwait(false);
            else await SelectInstalledWidgetAsync(index, token).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    private void ChangeInstalledFilter(SettingsWidgetFilter filter)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgets) return;
            _installedState = _installedState with { Filter = filter };
            installedReturnKey = null;
        }
        PublishSettingsChanged();
    }
}
