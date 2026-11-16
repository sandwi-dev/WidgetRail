using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsInstalledWidgetUninstallExecution(
    PlatformWidgetPackageUninstallInspection Current,
    bool ReloadCatalog,
    bool Error,
    string Status);

/// <summary>
/// Owns the trusted companion request sequence for package uninstall and its associated host-owned data cleanup.
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
        if(current.StatusCode is "diagnostics_timeout" or "diagnostics_unavailable")
            return new(current, ReloadCatalog: false, Error: true,
                "Could not check the installed package. Refresh and try again.");
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
                        $"{displayed.DisplayName} uninstalled; local data removed",
                    PlatformWidgetPackageUninstallStatus.CleanupPending =>
                        "Widget uninstalled; remaining cleanup will finish automatically",
                    PlatformWidgetPackageUninstallStatus.RecoveryPending =>
                        "Widget removed; catalog recovery is pending",
                    PlatformWidgetPackageUninstallStatus.Stale =>
                        "Installed package changed; review and confirm again",
                    PlatformWidgetPackageUninstallStatus.Resident =>
                        "Widget package is still resident; disable it and retry",
                    _ => $"Widget uninstall failed ({result.Code})",
                });
        }
        catch (PlatformDiagnosticsException exception) when(exception.Code is "diagnostics_timeout" or "diagnostics_unavailable")
        {
            return await ReconcileUncertainAsync(displayed).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {
            // The request may have crossed its durable commit boundary already.
            return await ReconcileUncertainAsync(displayed).ConfigureAwait(false);
        }
        catch (PlatformDiagnosticsException exception)
        {
            return new(
                current, ReloadCatalog: false, Error: true,
                $"Widget uninstall failed ({exception.Code})");
        }
    }

    private async ValueTask<SettingsInstalledWidgetUninstallExecution> ReconcileUncertainAsync(
        PlatformWidgetPackageUninstallInspection displayed)
    {
        // Read once; never replay the mutation or claim that cancellation undid it.
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        PlatformWidgetPackageUninstallInspection current;
        try{current=await InspectAsync(displayed.WidgetId,timeout.Token).ConfigureAwait(false);}
        catch(OperationCanceledException){current=displayed;}
        var status=current.StatusCode switch
        {
            "widget_not_found"=>"Widget is no longer installed. Any remaining cleanup will finish automatically.",
            "pending_uninstall_cleanup" or "uninstall_recovery_pending"=>"Widget removal is still being completed. Refresh to check its status.",
            _=>"Uninstall completion could not be confirmed. Refresh to check its status before trying again.",
        };
        return new(current,ReloadCatalog:true,Error:current.StatusCode!="widget_not_found",status);
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
