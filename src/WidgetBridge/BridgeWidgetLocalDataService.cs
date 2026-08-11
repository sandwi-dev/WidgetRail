using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetCatalog;

namespace GameBarAlternative.WidgetBridge;

/// <summary>
/// Trusted, document-blind management projection for the Settings companion.
/// The registry remains the sole worker-generation owner and the backend remains
/// the sole private-state document owner.
/// </summary>
internal sealed class BridgeWidgetLocalDataService(
    BridgeClientRegistry registry,
    IPrivateStatePlatformBrokerBackend? backend,
    BridgeCatalogMonitor? catalogMonitor = null)
{
    internal async ValueTask<PlatformWidgetLocalDataInspection> InspectAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        if (backend is null) return Unavailable(widgetId, "provider_unavailable");
        LocalDataTarget target;
        try
        {
            target = await ResolveAsync(widgetId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or
                                              BridgeProtocolException or
                                              WidgetPackageException)
        {
            return Unavailable(widgetId, "widget_not_found");
        }

        try
        {
            var state = await backend.ReadPrivateStateAsync(
                Identity(target.Configured), cancellationToken).ConfigureAwait(false);
            return new PlatformWidgetLocalDataInspection(
                target.Configured.Id,
                BridgeDiagnosticsProjection.SafeLabel(
                    target.Configured.Name, target.Configured.Id),
                state.Exists,
                state.Exists ? "local_data_present" : "no_local_data",
                state.Exists ? ConfirmationToken(target.Configured, state.Revision) : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is BrokerException or IOException or
                                               UnauthorizedAccessException)
        {
            return Unavailable(widgetId, SafeCode(exception));
        }
    }

    internal async ValueTask<PlatformWidgetLocalDataClearResult> ClearAsync(
        string widgetId,
        string confirmationToken,
        CancellationToken cancellationToken)
    {
        if (backend is null) return Result(
            PlatformWidgetLocalDataClearStatus.Unavailable, "provider_unavailable");
        try
        {
            var target = await ResolveAsync(widgetId, cancellationToken).ConfigureAwait(false);
            if (!target.HasRuntimeRegistration)
                return await ClearDisabledAsync(
                    target.Configured, confirmationToken, cancellationToken)
                    .ConfigureAwait(false);
            var replacement = await registry.ReplaceAsync(
                widgetId,
                async (configured, operationCancellation) =>
                {
                    PrivateStateSnapshotSummary state;
                    try
                    {
                        state = await backend.ReadPrivateStateAsync(
                            Identity(configured), operationCancellation).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is BrokerException or IOException or
                                                           UnauthorizedAccessException)
                    {
                        return Result(PlatformWidgetLocalDataClearStatus.ClearFailed,
                            SafeCode(exception));
                    }

                    if (!state.Exists)
                        return Result(PlatformWidgetLocalDataClearStatus.NoState, "no_local_data");
                    if (!CryptographicOperations.FixedTimeEquals(
                            Convert.FromHexString(ConfirmationToken(configured, state.Revision)),
                            Convert.FromHexString(confirmationToken)))
                        return Result(PlatformWidgetLocalDataClearStatus.Stale,
                            "confirmation_stale");
                    try
                    {
                        await backend.ClearPrivateStateAsync(
                            Identity(configured),
                            new ClearPrivateStateRequest(state.Revision),
                            operationCancellation).ConfigureAwait(false);
                        return Result(PlatformWidgetLocalDataClearStatus.Cleared, "cleared");
                    }
                    catch (Exception exception) when (exception is BrokerException or IOException or
                                                           UnauthorizedAccessException)
                    {
                        return Result(PlatformWidgetLocalDataClearStatus.ClearFailed,
                            SafeCode(exception));
                    }
                },
                cancellationToken).ConfigureAwait(false);
            using (replacement.Publication) return replacement.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(PlatformWidgetLocalDataClearStatus.Unavailable, "operation_cancelled");
        }
        catch (Exception exception) when (exception is BridgeProtocolException or
                                               AggregateException or ObjectDisposedException or
                                               KeyNotFoundException or WidgetPackageException)
        {
            return Result(PlatformWidgetLocalDataClearStatus.RestartFailed, "restart_failed");
        }
    }

    private async ValueTask<PlatformWidgetLocalDataClearResult> ClearDisabledAsync(
        ConfiguredWidget configured,
        string confirmationToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await ResolveAsync(configured.Id, cancellationToken)
                .ConfigureAwait(false);
            if (current.HasRuntimeRegistration ||
                !string.Equals(current.Configured.WorkerFingerprint,
                    configured.WorkerFingerprint, StringComparison.Ordinal))
                return Result(PlatformWidgetLocalDataClearStatus.Stale,
                    "confirmation_stale");
            var state = await backend!.ReadPrivateStateAsync(
                Identity(current.Configured), cancellationToken).ConfigureAwait(false);
            if (!state.Exists)
                return Result(PlatformWidgetLocalDataClearStatus.NoState, "no_local_data");
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(ConfirmationToken(current.Configured, state.Revision)),
                    Convert.FromHexString(confirmationToken)))
                return Result(PlatformWidgetLocalDataClearStatus.Stale,
                    "confirmation_stale");
            await backend.ClearPrivateStateAsync(
                Identity(current.Configured), new ClearPrivateStateRequest(state.Revision),
                cancellationToken).ConfigureAwait(false);
            return Result(PlatformWidgetLocalDataClearStatus.Cleared, "cleared");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(PlatformWidgetLocalDataClearStatus.Unavailable,
                "operation_cancelled");
        }
        catch (Exception exception) when (exception is BrokerException or IOException or
                                               UnauthorizedAccessException or
                                               WidgetPackageException)
        {
            return Result(PlatformWidgetLocalDataClearStatus.ClearFailed, SafeCode(exception));
        }
    }

    private async ValueTask<LocalDataTarget> ResolveAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        try
        {
            return new(
                registry.CatalogSnapshot().Catalog.GetConfigured(widgetId),
                HasRuntimeRegistration: true);
        }
        catch (BridgeProtocolException) when (catalogMonitor is not null)
        {
            var snapshot = await new GameBarAlternative.WidgetCatalog.WidgetCatalog(
                    catalogMonitor.InstalledCatalogRoot)
                .DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var installed = snapshot.Widgets.SingleOrDefault(widget => string.Equals(
                widget.Id, widgetId, StringComparison.Ordinal)) ??
                throw new KeyNotFoundException();
            if (installed.Enabled)
                throw new BridgeProtocolException(
                    "Installed widget catalog reconciliation is pending.");
            var version = installed.ActiveVersion;
            var manifest = version.Manifest;
            return new(new ConfiguredWidget
            {
                Id = manifest.Id,
                PackageId = manifest.Id,
                PublisherId = InstalledWidgetAuthority.PublisherId(version),
                Name = manifest.Name,
                InstanceId = InstalledWidgetInstanceIdentity.Derive(
                    manifest.Id, manifest.Version),
                WorkerExecutable = Environment.ProcessPath!,
                WorkerFingerprint = version.ContentDigest,
                CatalogFingerprint = version.ContentDigest,
            }, HasRuntimeRegistration: false);
        }
    }

    private static BrokerWidgetIdentity Identity(ConfiguredWidget configured) => new(
        configured.PackageId, configured.PublisherId, configured.InstanceId);

    private static string ConfirmationToken(ConfiguredWidget configured, long revision)
    {
        var material = Encoding.UTF8.GetBytes(string.Join('\n',
            configured.Id,
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.WorkerFingerprint,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string SafeCode(Exception exception) =>
        BridgeDiagnosticsProjection.SafeCode(exception is BrokerException broker
            ? broker.Code
            : exception is UnauthorizedAccessException ? "access_denied" : "io_error");

    private readonly record struct LocalDataTarget(
        ConfiguredWidget Configured,
        bool HasRuntimeRegistration);

    private static PlatformWidgetLocalDataInspection Unavailable(string id, string code) =>
        new(id, id, false, code, null);

    private static PlatformWidgetLocalDataClearResult Result(
        PlatformWidgetLocalDataClearStatus status,
        string code) => new(status, BridgeDiagnosticsProjection.SafeCode(code));
}
