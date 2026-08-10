using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

/// <summary>
/// Read-only, bridge-owned diagnostics channel for the exact trusted Settings
/// worker. It is not a manifest capability and is never attached to community
/// workers.
/// </summary>
internal sealed class DiagnosticsWidgetProcessCompanion : IWidgetProcessCompanionSession
{
    private readonly PlatformDiagnosticsPipeServer _server;

    public DiagnosticsWidgetProcessCompanion(
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> snapshotProvider,
        Func<string, CancellationToken, ValueTask<PlatformAuthorityRecoveryRetryResult>>
            authorityRecoveryRetry,
        WidgetProcessCompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(authorityRecoveryRetry);
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsolationPolicy != WidgetWorkerIsolationPolicy.HostTrustedJobOnly)
            throw new InvalidOperationException(
                "The private diagnostics companion is limited to a trusted platform worker.");
        var pipeName = $"gba-diagnostics-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new PlatformDiagnosticsPipeServer(
            pipeName, snapshotProvider, authorityRecoveryRetry: authorityRecoveryRetry);
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
