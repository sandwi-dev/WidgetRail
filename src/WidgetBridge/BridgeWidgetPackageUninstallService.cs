using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Path-free uninstall authority for the exact trusted Settings companion.
/// WidgetCatalog owns identity validation and package retirement; the monitor
/// owns the single catalog revision published after a successful mutation.
/// </summary>
internal sealed class BridgeWidgetPackageUninstallService(
    WidgetCatalog.WidgetCatalog? catalog,
    BridgeCatalogMonitor? catalogMonitor)
{
    internal async ValueTask<PlatformWidgetPackageUninstallInspection> InspectAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        if (catalog is null || catalogMonitor is null)
            return Unavailable(widgetId, "provider_unavailable");
        try
        {
            var item = await catalog.InspectUninstallAsync(widgetId, cancellationToken)
                .ConfigureAwait(false);
            return new PlatformWidgetPackageUninstallInspection(
                item.Id,
                BridgeDiagnosticsProjection.SafeLabel(item.Name, item.Id),
                item.PublisherId,
                item.ActiveVersion.ToString(),
                item.VersionCount,
                CanUninstall: !item.Enabled,
                item.Enabled ? "widget_enabled" : "ready",
                item.Enabled ? null : item.ConfirmationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Unavailable(widgetId, "widget_not_found");
        }
        catch (WidgetPackageException exception)
        {
            return Unavailable(widgetId, BridgeDiagnosticsProjection.SafeCode(exception.Code));
        }
        catch (IOException)
        {
            return Unavailable(widgetId, "io_error");
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable(widgetId, "access_denied");
        }
    }

    internal async ValueTask<PlatformWidgetPackageUninstallResult> UninstallAsync(
        string widgetId,
        string publisherId,
        string activeVersion,
        string confirmationToken,
        CancellationToken cancellationToken)
    {
        if (catalog is null || catalogMonitor is null)
            return Result(PlatformWidgetPackageUninstallStatus.Unavailable,
                "provider_unavailable");
        if (!Version.TryParse(activeVersion, out var parsedVersion))
            return Result(PlatformWidgetPackageUninstallStatus.Refused,
                "invalid_package_identity");
        try
        {
            var removed = await catalog.UninstallConfirmedAsync(
                widgetId, publisherId, parsedVersion, confirmationToken, cancellationToken)
                .ConfigureAwait(false);
            var reload = await catalogMonitor.ReloadAfterMutationAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (reload.RetainedLastGood)
                return Result(PlatformWidgetPackageUninstallStatus.RecoveryPending,
                    "catalog_recovery_pending");
            return removed.CleanupPending
                ? Result(PlatformWidgetPackageUninstallStatus.CleanupPending,
                    "cleanup_pending")
                : Result(PlatformWidgetPackageUninstallStatus.Uninstalled, "uninstalled");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(PlatformWidgetPackageUninstallStatus.Unavailable,
                "operation_cancelled");
        }
        catch (KeyNotFoundException)
        {
            return Result(PlatformWidgetPackageUninstallStatus.Stale, "widget_not_found");
        }
        catch (WidgetPackageException exception)
        {
            return exception.Code switch
            {
                "confirmation_stale" => Result(
                    PlatformWidgetPackageUninstallStatus.Stale, exception.Code),
                "widget_enabled" => Result(
                    PlatformWidgetPackageUninstallStatus.Refused, exception.Code),
                "active_version_missing" or "catalog_state_invalid" or
                    "registration_cleanup_failed" or
                    "uninstall_recovery_pending" or
                    "pending_uninstall_cleanup" => Result(
                    PlatformWidgetPackageUninstallStatus.RecoveryPending, exception.Code),
                _ => Result(PlatformWidgetPackageUninstallStatus.Refused,
                    BridgeDiagnosticsProjection.SafeCode(exception.Code)),
            };
        }
        catch (IOException)
        {
            return Result(PlatformWidgetPackageUninstallStatus.Resident, "package_resident");
        }
        catch (UnauthorizedAccessException)
        {
            return Result(PlatformWidgetPackageUninstallStatus.Unavailable, "access_denied");
        }
    }

    private static PlatformWidgetPackageUninstallInspection Unavailable(
        string widgetId,
        string code) => new(
            widgetId, widgetId, string.Empty, string.Empty, 0, false,
            BridgeDiagnosticsProjection.SafeCode(code), null);

    private static PlatformWidgetPackageUninstallResult Result(
        PlatformWidgetPackageUninstallStatus status,
        string code) => new(status, BridgeDiagnosticsProjection.SafeCode(code));
}

internal sealed class BridgeWidgetUninstallAuthorityParticipant(
    IAppLibraryPlatformBrokerBackend appLibrary) :
    IWidgetUninstallAuthorityParticipant
{
    public async Task<WidgetUninstallAuthorityCommit> RetirePackageAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await appLibrary.RetireRunningAppPackageRegistrationsAsync(
                packageId, cancellationToken).ConfigureAwait(false);
            return new(result.Committed, result.CleanupPending);
        }
        catch (Exception exception) when (exception is BrokerException or
            IOException or UnauthorizedAccessException)
        {
            throw new WidgetPackageException(
                "registration_cleanup_failed",
                "Portable app registration cleanup failed.",
                exception);
        }
    }
}
