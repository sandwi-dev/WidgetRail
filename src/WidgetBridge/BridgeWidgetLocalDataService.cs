using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Trusted, document-blind management projection for the Settings companion.
/// The registry remains the sole worker-generation owner and the backend remains
/// the sole private-state document owner.
/// </summary>
internal sealed class BridgeWidgetLocalDataService(
    BridgeClientRegistry registry,
    IPrivateStatePlatformBrokerBackend? backend,
    BridgeCatalogMonitor? catalogMonitor = null,
    IAppLibraryPlatformBrokerBackend? appLibrary = null)
{
    internal ValueTask<PlatformWidgetLocalDataInspection> InspectAsync(string widgetId, CancellationToken cancellationToken) =>
        InspectCoreAsync(widgetId, false, cancellationToken);

    internal ValueTask<PlatformWidgetLocalDataInspection> InspectBuiltInAsync(string packageId, CancellationToken cancellationToken) =>
        InspectCoreAsync(packageId, true, cancellationToken);

    private async ValueTask<PlatformWidgetLocalDataInspection> InspectCoreAsync(
        string widgetId,
        bool builtIn,
        CancellationToken cancellationToken)
    {
        if (backend is null) return Unavailable(widgetId, "provider_unavailable");
        LocalDataTarget target;
        try
        {
            target = await ResolveAsync(widgetId, cancellationToken, builtIn).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or
                                              BridgeProtocolException or
                                              WidgetPackageException or BridgeCatalogException or IOException or UnauthorizedAccessException)
        {
            return Unavailable(widgetId, "widget_not_found");
        }

        try
        {
            var identity = Identity(target.Configured);
            var state = await ReadStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            return new PlatformWidgetLocalDataInspection(
                widgetId,
                BridgeDiagnosticsProjection.SafeLabel(
                    target.Configured.Name, target.Configured.Id),
                state.Exists,
                state.Exists ? "local_data_present" : "no_local_data",
                state.Exists ? ConfirmationToken(
                    target.Configured, state.PrivateRevision,
                    state.AppRegistrationRevision) : null);
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

    internal ValueTask<PlatformWidgetLocalDataClearResult> ClearAsync(string widgetId, string confirmationToken, CancellationToken cancellationToken) =>
        ClearCoreAsync(widgetId, confirmationToken, false, cancellationToken);

    internal ValueTask<PlatformWidgetLocalDataClearResult> ClearBuiltInAsync(string packageId, string confirmationToken, CancellationToken cancellationToken) =>
        ClearCoreAsync(packageId, confirmationToken, true, cancellationToken);

    private async ValueTask<PlatformWidgetLocalDataClearResult> ClearCoreAsync(
        string widgetId,
        string confirmationToken,
        bool builtIn,
        CancellationToken cancellationToken)
    {
        if (backend is null) return Result(
            PlatformWidgetLocalDataClearStatus.Unavailable, "provider_unavailable");
        try
        {
            var target = await ResolveAsync(widgetId, cancellationToken, builtIn).ConfigureAwait(false);
            if (!target.HasRuntimeRegistration)
                return await ClearDisabledAsync(
                    target.Configured, confirmationToken, cancellationToken, builtIn)
                    .ConfigureAwait(false);
            var replacement = await registry.ReplaceAsync(
                target.Configured.Id,
                async (configured, operationCancellation) =>
                {
                    LocalDataState state;
                    try
                    {
                        state = await ReadStateAsync(
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
                            Convert.FromHexString(ConfirmationToken(
                                configured, state.PrivateRevision,
                                state.AppRegistrationRevision)),
                            Convert.FromHexString(confirmationToken)))
                        return Result(PlatformWidgetLocalDataClearStatus.Stale,
                            "confirmation_stale");
                    try
                    {
                        await ClearStateAsync(
                            Identity(configured), state, operationCancellation)
                            .ConfigureAwait(false);
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
                                               KeyNotFoundException or WidgetPackageException or BridgeCatalogException or IOException or UnauthorizedAccessException)
        {
            return Result(PlatformWidgetLocalDataClearStatus.RestartFailed, "restart_failed");
        }
    }

    private async ValueTask<PlatformWidgetLocalDataClearResult> ClearDisabledAsync(
        ConfiguredWidget configured,
        string confirmationToken,
        CancellationToken cancellationToken,
        bool builtIn = false)
    {
        try
        {
            var current = await ResolveAsync(builtIn ? configured.PackageId : configured.Id, cancellationToken, builtIn)
                .ConfigureAwait(false);
            if (current.HasRuntimeRegistration ||
                !string.Equals(current.Configured.WorkerFingerprint,
                    configured.WorkerFingerprint, StringComparison.Ordinal))
                return Result(PlatformWidgetLocalDataClearStatus.Stale,
                    "confirmation_stale");
            var identity = Identity(current.Configured);
            var state = await ReadStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            if (!state.Exists)
                return Result(PlatformWidgetLocalDataClearStatus.NoState, "no_local_data");
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(ConfirmationToken(
                        current.Configured, state.PrivateRevision,
                        state.AppRegistrationRevision)),
                    Convert.FromHexString(confirmationToken)))
                return Result(PlatformWidgetLocalDataClearStatus.Stale,
                    "confirmation_stale");
            await ClearStateAsync(identity, state, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        bool builtIn = false)
    {
        if (builtIn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Resolve source before identity: an installed package with this ID
            // must never become the target of a built-in data operation.
            var trusted = catalogMonitor?.LoadTrustedForManagement().GetConfiguredPackage(widgetId) ??
                throw new BridgeProtocolException("Built-in management catalog is unavailable.");
            try
            {
                var active = registry.CatalogSnapshot().Catalog.GetConfigured(trusted.Id);
                if (active.PackageId != trusted.PackageId || active.PublisherId != trusted.PublisherId ||
                    active.InstanceId != trusted.InstanceId || active.WorkerFingerprint != trusted.WorkerFingerprint)
                    throw new KeyNotFoundException("Built-in widget catalog reconciliation is pending.");
                return new(active, HasRuntimeRegistration: true);
            }
            catch (BridgeProtocolException)
            {
                if (!await catalogMonitor!.IsBuiltInDisabledForManagementAsync(widgetId, cancellationToken).ConfigureAwait(false))
                    throw new KeyNotFoundException("Built-in widget catalog reconciliation is pending.");
                return new(trusted, HasRuntimeRegistration: false);
            }
        }
        try
        {
            return new(
                registry.CatalogSnapshot().Catalog.GetConfigured(widgetId),
                HasRuntimeRegistration: true);
        }
        catch (BridgeProtocolException) when (catalogMonitor is not null)
        {
            var snapshot = await new WidgetRail.WidgetCatalog.WidgetCatalog(
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

    private async Task<LocalDataState> ReadStateAsync(
        BrokerWidgetIdentity identity,
        CancellationToken cancellationToken)
    {
        var privateState = await backend!.ReadPrivateStateAsync(
            identity, cancellationToken).ConfigureAwait(false);
        var registrations = appLibrary is null
            ? new AppLibraryRegistrationStateSummary(false, 0)
            : await appLibrary.GetRunningAppRegistrationStateAsync(
                identity, cancellationToken).ConfigureAwait(false);
        return new(
            privateState.Exists || registrations.Exists,
            privateState.Exists,
            privateState.Revision,
            registrations.Exists,
            registrations.Revision);
    }

    private async Task ClearStateAsync(
        BrokerWidgetIdentity identity,
        LocalDataState state,
        CancellationToken cancellationToken)
    {
        if (state.AppRegistrationsExist && appLibrary is not null)
            await appLibrary.ClearRunningAppRegistrationsAsync(
                identity, state.AppRegistrationRevision, cancellationToken)
                .ConfigureAwait(false);
        if (state.PrivateStateExists)
            await backend!.ClearPrivateStateAsync(
                identity, new ClearPrivateStateRequest(state.PrivateRevision),
                cancellationToken).ConfigureAwait(false);
    }

    private static string ConfirmationToken(
        ConfiguredWidget configured,
        long privateRevision,
        long appRegistrationRevision)
    {
        var material = Encoding.UTF8.GetBytes(string.Join('\n',
            configured.Id,
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.WorkerFingerprint,
            privateRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            appRegistrationRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string SafeCode(Exception exception) =>
        BridgeDiagnosticsProjection.SafeCode(exception is BrokerException broker
            ? broker.Code
            : exception is UnauthorizedAccessException ? "access_denied" : "io_error");

    private readonly record struct LocalDataTarget(
        ConfiguredWidget Configured,
        bool HasRuntimeRegistration);

    private readonly record struct LocalDataState(
        bool Exists,
        bool PrivateStateExists,
        long PrivateRevision,
        bool AppRegistrationsExist,
        long AppRegistrationRevision);

    private static PlatformWidgetLocalDataInspection Unavailable(string id, string code) =>
        new(id, id, false, code, null);

    private static PlatformWidgetLocalDataClearResult Result(
        PlatformWidgetLocalDataClearStatus status,
        string code) => new(status, BridgeDiagnosticsProjection.SafeCode(code));
}
