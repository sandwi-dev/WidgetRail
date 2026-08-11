using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

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
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(authorityRecoveryRetry);
        ArgumentNullException.ThrowIfNull(localDataInspection);
        ArgumentNullException.ThrowIfNull(localDataClear);
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsolationPolicy != WidgetWorkerIsolationPolicy.HostTrustedJobOnly)
            throw new InvalidOperationException(
                "The private diagnostics companion is limited to a trusted platform worker.");
        var pipeName = $"gba-diagnostics-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new PlatformDiagnosticsPipeServer(
            pipeName, snapshotProvider,
            authorityRecoveryRetry: authorityRecoveryRetry,
            localDataInspection: localDataInspection,
            localDataClear: localDataClear);
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
