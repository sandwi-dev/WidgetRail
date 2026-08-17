using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsInstalledWidgetUninstallExecution(
    PlatformWidgetPackageUninstallInspection Current,
    bool ReloadCatalog,
    bool Error,
    string Status);

/// <summary>
/// Owns the trusted companion request sequence for package-only uninstall.
/// SettingsWidget remains the sole committed page/status owner.
/// </summary>
internal sealed class SettingsInstalledWidgetUninstallOperation(
    IPlatformDiagnosticsService diagnostics)
{
    public async ValueTask<PlatformWidgetPackageUninstallInspection> InspectAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await diagnostics.InspectWidgetPackageUninstallAsync(
                widgetId, cancellationToken).ConfigureAwait(false);
        }
        catch (PlatformDiagnosticsException exception)
        {
            return new(widgetId, widgetId, string.Empty, string.Empty, 0,
                false, exception.Code, null);
        }
    }

    public async ValueTask<SettingsInstalledWidgetUninstallExecution> ExecuteAsync(
        PlatformWidgetPackageUninstallInspection displayed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        var current = await InspectAsync(displayed.WidgetId, cancellationToken)
            .ConfigureAwait(false);
        if (!SameIdentity(current, displayed))
            return new(
                current, ReloadCatalog: false, Error: true,
                "Installed package changed; review and confirm again");
        try
        {
            var result = await diagnostics.UninstallWidgetPackageAsync(
                displayed.WidgetId, displayed.PublisherId, displayed.ActiveVersion,
                displayed.ConfirmationToken!, cancellationToken).ConfigureAwait(false);
            var completed = result.Status is PlatformWidgetPackageUninstallStatus.Uninstalled or
                    PlatformWidgetPackageUninstallStatus.CleanupPending or
                    PlatformWidgetPackageUninstallStatus.RecoveryPending;
            return new(
                current,
                ReloadCatalog: completed,
                Error: result.Status != PlatformWidgetPackageUninstallStatus.Uninstalled,
                Status: result.Status switch
                {
                    PlatformWidgetPackageUninstallStatus.Uninstalled =>
                        $"{displayed.DisplayName} uninstalled; local data was preserved",
                    PlatformWidgetPackageUninstallStatus.CleanupPending =>
                        "Widget uninstalled; package cleanup is pending and retryable",
                    PlatformWidgetPackageUninstallStatus.RecoveryPending =>
                        "Widget removed; catalog recovery is pending",
                    PlatformWidgetPackageUninstallStatus.Stale =>
                        "Installed package changed; review and confirm again",
                    PlatformWidgetPackageUninstallStatus.Resident =>
                        "Widget package is still resident; disable it and retry",
                    _ => $"Widget uninstall failed ({result.Code})",
                });
        }
        catch (PlatformDiagnosticsException exception)
        {
            return new(
                current, ReloadCatalog: false, Error: true,
                $"Widget uninstall failed ({exception.Code})");
        }
    }

    private static bool SameIdentity(
        PlatformWidgetPackageUninstallInspection current,
        PlatformWidgetPackageUninstallInspection displayed) =>
        current.CanUninstall && current.ConfirmationToken is not null &&
        string.Equals(current.WidgetId, displayed.WidgetId, StringComparison.Ordinal) &&
        string.Equals(current.PublisherId, displayed.PublisherId, StringComparison.Ordinal) &&
        string.Equals(current.ActiveVersion, displayed.ActiveVersion, StringComparison.Ordinal) &&
        string.Equals(current.ConfirmationToken, displayed.ConfirmationToken,
            StringComparison.Ordinal);
}
