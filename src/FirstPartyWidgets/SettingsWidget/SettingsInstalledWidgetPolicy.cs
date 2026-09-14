using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsInstalledWidgetState(
    WidgetCatalogSnapshot Catalog,
    WidgetCatalogHealthSnapshot Health,
    IReadOnlyList<WidgetManifest> BuiltIns,
    bool CatalogValid,
    string? Diagnostic,
    int Page,
    int VersionPage,
    string? SelectedInstalledId,
    string? SelectedBuiltInId,
    WidgetCatalogRepairCandidate? SelectedRepair)
{
    public Version? UpdateFromVersion { get; init; }
    public SettingsVersionRemoval? VersionRemoval { get; init; }
    public string? VersionFocusId { get; init; }
    public PlatformWidgetLocalDataInspection? LocalData { get; init; }
    public PlatformWidgetPackageUninstallInspection? PackageUninstall { get; init; }
    public string? DetailsFocusId { get; init; }
    public static SettingsInstalledWidgetState Empty { get; } = new(
        new WidgetCatalogSnapshot([]),
        new WidgetCatalogHealthSnapshot(null, []),
        [],
        CatalogValid: true,
        Diagnostic: null,
        Page: 0,
        VersionPage: 0,
        SelectedInstalledId: null,
        SelectedBuiltInId: null,
        SelectedRepair: null);

    public CatalogWidget? SelectedInstalled => Catalog.Widgets.FirstOrDefault(
        widget => string.Equals(widget.Id, SelectedInstalledId, StringComparison.Ordinal));

    public WidgetManifest? SelectedBuiltIn => BuiltIns.FirstOrDefault(
        manifest => string.Equals(manifest.Id, SelectedBuiltInId, StringComparison.Ordinal));

    public WidgetManifest? SelectedInstalledBuiltIn => BuiltIns.FirstOrDefault(
        manifest => string.Equals(manifest.Id, SelectedInstalledId, StringComparison.Ordinal));
}

internal readonly record struct SettingsInstalledWidgetTransition(
    SettingsInstalledWidgetState State,
    SettingsPage Page);

/// <summary>
/// Value-only installed-widget selection, paging, and stale-catalog policy.
/// Catalog I/O and committed Settings state remain owned by <see cref="SettingsWidget"/>.
/// </summary>
internal static class SettingsInstalledWidgetPolicy
{
    public static SettingsInstalledWidgetTransition Reconcile(
        SettingsInstalledWidgetState current,
        WidgetCatalogSnapshot catalog,
        IReadOnlyList<WidgetManifest> builtIns,
        SettingsPage page)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(builtIns);
        var selectedInstalled = current.SelectedInstalledId;
        var selectedBuiltIn = current.SelectedBuiltInId;
        var versionPage = current.VersionPage;
        if (selectedInstalled is not null &&
            !catalog.Widgets.Any(widget => string.Equals(
                widget.Id, selectedInstalled, StringComparison.Ordinal)))
        {
            selectedInstalled = null;
            versionPage = 0;
            if (page is SettingsPage.InstalledWidgetDetails or
                SettingsPage.InstalledWidgetVersions or
                SettingsPage.InstalledWidgetUpdate or
                SettingsPage.InstalledWidgetVersionRemoval or
                SettingsPage.InstalledWidgetRecovery)
                page = SettingsPage.InstalledWidgets;
        }
        else if (page == SettingsPage.InstalledWidgetVersionRemoval)
        {
            page = SettingsPage.InstalledWidgetVersions;
        }
        else if (page == SettingsPage.InstalledWidgetRecovery)
        {
            page = SettingsPage.InstalledWidgets;
        }
        if (selectedBuiltIn is not null &&
            !builtIns.Any(manifest => string.Equals(
                manifest.Id, selectedBuiltIn, StringComparison.Ordinal)))
        {
            selectedBuiltIn = null;
            if (page == SettingsPage.InstalledWidgetDetails)
                page = SettingsPage.InstalledWidgets;
        }
        var state = new SettingsInstalledWidgetState(
            catalog,
            new WidgetCatalogHealthSnapshot(null, []),
            builtIns,
            CatalogValid: true,
            Diagnostic: null,
            Page: Math.Clamp(current.Page, 0, LastCatalogPage(catalog)),
            VersionPage: versionPage,
            SelectedInstalledId: selectedInstalled,
            SelectedBuiltInId: selectedBuiltIn,
            SelectedRepair: null)
        {
            UpdateFromVersion = selectedInstalled is not null ? current.UpdateFromVersion : null,
            LocalData = selectedInstalled is not null || selectedBuiltIn is not null
                ? current.LocalData
                : null,
            PackageUninstall = selectedInstalled is not null
                ? current.PackageUninstall
                : null,
            DetailsFocusId = selectedInstalled is not null
                ? current.DetailsFocusId
                : null,
        };
        return new(state, page);
    }

    public static SettingsInstalledWidgetTransition Failure(
        SettingsInstalledWidgetState current,
        string diagnostic,
        WidgetCatalogHealthSnapshot health,
        SettingsPage page)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        ArgumentNullException.ThrowIfNull(health);
        if (page is SettingsPage.InstalledWidgetDetails or
            SettingsPage.InstalledWidgetVersions or
            SettingsPage.InstalledWidgetVersionRemoval or
            SettingsPage.InstalledWidgetRecovery or
            SettingsPage.InstalledWidgetLocalData)
            page = SettingsPage.InstalledWidgets;
        var retainedCatalog = current.Catalog.Widgets.Count != 0;
        return new(current with
        {
            Health = health,
            CatalogValid = false,
            Diagnostic = diagnostic,
            Page = retainedCatalog
                ? Math.Clamp(current.Page, 0, LastCatalogPage(current.Catalog))
                : 0,
            VersionPage = 0,
            SelectedInstalledId = null,
            SelectedBuiltInId = null,
            SelectedRepair = null,
            LocalData = null,
            PackageUninstall = null,
            DetailsFocusId = null,
        }, page);
    }

    public static SettingsInstalledWidgetState ChangeCatalogPage(
        SettingsInstalledWidgetState state,
        int delta)
    {
        var count = state.Catalog.Widgets.Count != 0
            ? state.Catalog.Widgets.Count
            : state.Health.Candidates.Count(item => item.CanRemove);
        var last = Math.Max(0, (count - 1) / SettingsWidget.InstalledWidgetsPerPage);
        return state with { Page = Math.Clamp(state.Page + delta, 0, last) };
    }

    public static bool TrySelectInstalled(
        SettingsInstalledWidgetState state,
        int index,
        out SettingsInstalledWidgetTransition transition)
    {
        if (index < 0 || index >= state.Catalog.Widgets.Count)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with
            {
                SelectedInstalledId = state.Catalog.Widgets[index].Id,
                SelectedBuiltInId = null,
                LocalData = null,
                PackageUninstall = null,
                DetailsFocusId = null,
                VersionPage = 0,
            },
            SettingsPage.InstalledWidgetDetails);
        return true;
    }

    public static bool TrySelectBuiltIn(
        SettingsInstalledWidgetState state,
        int index,
        out SettingsInstalledWidgetTransition transition)
    {
        if (!state.CatalogValid || index < 0 || index >= state.BuiltIns.Count)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with
            {
                SelectedBuiltInId = state.BuiltIns[index].Id,
                SelectedInstalledId = null,
                LocalData = null,
                PackageUninstall = null,
                DetailsFocusId = null,
                VersionPage = 0,
            },
            SettingsPage.InstalledWidgetDetails);
        return true;
    }

    public static bool TrySelectRepair(
        SettingsInstalledWidgetState state,
        int index,
        out SettingsInstalledWidgetTransition transition)
    {
        var removable = state.Health.Candidates.Where(item => item.CanRemove).ToArray();
        if (state.CatalogValid || state.Catalog.Widgets.Count != 0 ||
            index < 0 || index >= removable.Length)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with { SelectedRepair = removable[index] },
            SettingsPage.InstalledWidgetRecovery);
        return true;
    }

    public static bool TryOpenVersions(
        SettingsInstalledWidgetState state,
        out SettingsInstalledWidgetTransition transition)
    {
        if (!state.CatalogValid || state.SelectedInstalled is null ||
            state.SelectedBuiltIn is not null)
        {
            transition = default;
            return false;
        }
        transition = new(state, SettingsPage.InstalledWidgetVersions);
        return true;
    }

    public static SettingsInstalledWidgetState ChangeVersionPage(
        SettingsInstalledWidgetState state,
        int delta)
    {
        var selected = state.SelectedInstalled;
        if (selected is null) return state;
        return state with
        {
            VersionPage = Math.Clamp(
                state.VersionPage + delta,
                0,
                LastVersionPage(selected)),
        };
    }

    public static int LastCatalogPage(WidgetCatalogSnapshot snapshot) =>
        Math.Max(0, (snapshot.Widgets.Count - 1) / SettingsWidget.InstalledWidgetsPerPage);

    public static int LastVersionPage(CatalogWidget widget) =>
        Math.Max(0, (widget.Versions.Count - 1) / SettingsWidget.InstalledVersionsPerPage);
}
