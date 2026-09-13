using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.FirstPartyWidgets.Settings;

/// <summary>
/// Value-only admission and result policy for package-only uninstall. The
/// widget owns committed Settings state; the trusted companion owns mutation.
/// </summary>
internal static class SettingsInstalledWidgetUninstallPolicy
{
    internal const string FocusId = "installed.details.uninstall";

    public static bool TryOpen(
        SettingsInstalledWidgetState state,
        out SettingsInstalledWidgetTransition transition)
    {
        var package = state.SelectedInstalled;
        var inspection = state.PackageUninstall;
        if (package is null || !state.CatalogValid || inspection is null ||
            !(package.Enabled && inspection.StatusCode == "widget_enabled" || inspection is { CanUninstall: true, ConfirmationToken: not null }) ||
            !Matches(package, inspection))
        {
            transition = default;
            return false;
        }
        transition = new(
            state with { DetailsFocusId = FocusId },
            SettingsPage.InstalledWidgetUninstall);
        return true;
    }

    public static SettingsInstalledWidgetTransition Cancel(
        SettingsInstalledWidgetState state) => new(
            state with { DetailsFocusId = FocusId },
            SettingsPage.InstalledWidgetDetails);

    public static bool Matches(
        CatalogWidget package,
        PlatformWidgetPackageUninstallInspection inspection) =>
        string.Equals(package.Id, inspection.WidgetId, StringComparison.Ordinal) &&
        string.Equals(
            InstalledWidgetAuthority.PublisherId(package.ActiveVersion),
            inspection.PublisherId,
            StringComparison.Ordinal) &&
        string.Equals(
            package.ActiveVersion.Version.ToString(),
            inspection.ActiveVersion,
            StringComparison.Ordinal) &&
        package.Versions.Count == inspection.VersionCount;
}
