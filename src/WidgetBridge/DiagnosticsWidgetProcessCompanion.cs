using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Bridge-owned diagnostics and bounded management channel for the exact trusted
/// Settings worker. It is not a manifest capability and is never attached to
/// community workers. Management delegates expose only sanitized values.
/// </summary>
internal sealed class DiagnosticsWidgetProcessCompanion : IWidgetProcessCompanionSession
{
    private readonly PlatformDiagnosticsPipeServer _server;

    public DiagnosticsWidgetProcessCompanion(
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> snapshotProvider,
        Func<string, CancellationToken, ValueTask<PlatformAuthorityRecoveryRetryResult>>
            authorityRecoveryRetry,
        WidgetProcessCompanionContext context)
        : this(
            snapshotProvider,
            authorityRecoveryRetry,
            static (id, token) => ValueTask.FromResult(
                new PlatformWidgetLocalDataInspection(
                    id, id, false, "inspection_unsupported", null)),
            static (_, _, token) => ValueTask.FromResult(
                new PlatformWidgetLocalDataClearResult(
                    PlatformWidgetLocalDataClearStatus.Refused, "clear_unsupported")),
            context)
    {
    }

    public DiagnosticsWidgetProcessCompanion(
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> snapshotProvider,
        Func<string, CancellationToken, ValueTask<PlatformAuthorityRecoveryRetryResult>>
            authorityRecoveryRetry,
        Func<string, CancellationToken, ValueTask<PlatformWidgetLocalDataInspection>>
            localDataInspection,
        Func<string, string, CancellationToken, ValueTask<PlatformWidgetLocalDataClearResult>>
            localDataClear,
        WidgetProcessCompanionContext context)
        : this(
            snapshotProvider, authorityRecoveryRetry, localDataInspection, localDataClear,
            static (id, token) => ValueTask.FromResult(
                new PlatformWidgetPackageUninstallInspection(
                    id, id, string.Empty, string.Empty, 0, false,
                    "inspection_unsupported", null)),
            static (_, _, _, _, token) => ValueTask.FromResult(
                new PlatformWidgetPackageUninstallResult(
                    PlatformWidgetPackageUninstallStatus.Refused,
                    "uninstall_unsupported")),
            context)
    {
    }

    public DiagnosticsWidgetProcessCompanion(
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> snapshotProvider,
        Func<string, CancellationToken, ValueTask<PlatformAuthorityRecoveryRetryResult>>
            authorityRecoveryRetry,
        Func<string, CancellationToken, ValueTask<PlatformWidgetLocalDataInspection>>
            localDataInspection,
        Func<string, string, CancellationToken, ValueTask<PlatformWidgetLocalDataClearResult>>
            localDataClear,
        Func<string, CancellationToken, ValueTask<PlatformWidgetPackageUninstallInspection>>
            packageUninstallInspection,
        Func<string, string, string, string, CancellationToken,
            ValueTask<PlatformWidgetPackageUninstallResult>> packageUninstall,
        WidgetProcessCompanionContext context,
        Func<bool, CancellationToken, ValueTask<ControllerControlResult>>? exclusiveControl = null,
        Func<bool, CancellationToken, ValueTask<ApplicationControlResult>>? applicationControl = null,
        Func<CancellationToken, ValueTask<PlatformWidgetPackageNotification>>? packageNotification = null,
        Func<string, CancellationToken, ValueTask<PlatformWidgetLocalDataInspection>>? builtInLocalDataInspection = null,
        Func<string, string, CancellationToken, ValueTask<PlatformWidgetLocalDataClearResult>>? builtInLocalDataClear = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(authorityRecoveryRetry);
        ArgumentNullException.ThrowIfNull(localDataInspection);
        ArgumentNullException.ThrowIfNull(localDataClear);
        ArgumentNullException.ThrowIfNull(packageUninstallInspection);
        ArgumentNullException.ThrowIfNull(packageUninstall);
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsolationPolicy != WidgetWorkerIsolationPolicy.HostTrustedJobOnly)
            throw new InvalidOperationException(
                "The private diagnostics companion is limited to a trusted platform worker.");
        var pipeName = $"wrail-diagnostics-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new PlatformDiagnosticsPipeServer(
            pipeName, snapshotProvider,
            authorityRecoveryRetry: authorityRecoveryRetry,
            localDataInspection: localDataInspection,
            localDataClear: localDataClear,
            packageUninstallInspection: packageUninstallInspection,
            packageUninstall: packageUninstall,
            exclusiveControl: exclusiveControl,
            applicationControl: applicationControl,
            packageNotification: packageNotification,
            builtInLocalDataInspection: builtInLocalDataInspection,
            builtInLocalDataClear: builtInLocalDataClear);
        WorkerArguments =
        [
            "--diagnostics-pipe", pipeName,
            "--diagnostics-nonce", _server.ChannelNonce,
            "--diagnostics-server-pid",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];
    }

    public IReadOnlyList<string> WorkerArguments { get; }

    public void BindWorkerProcess(int processId) => _server.BindExpectedClientProcess(processId);

    public Task RunAsync(CancellationToken cancellationToken) => _server.RunAsync(cancellationToken);

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
