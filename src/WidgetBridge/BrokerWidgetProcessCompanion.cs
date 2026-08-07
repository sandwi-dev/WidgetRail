using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

/// <summary>
/// Bridge-owned capability channel created once per worker process. Identity,
/// declarations, consent and backend never come from the widget package.
/// </summary>
internal sealed class BrokerWidgetProcessCompanion : IWidgetProcessCompanionSession
{
    private readonly BrokerPipeServer _server;

    public BrokerWidgetProcessCompanion(
        string packageId,
        string publisherId,
        string instanceId,
        IReadOnlyList<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend)
    {
        var identity = new BrokerWidgetIdentity(packageId, publisherId, instanceId);
        var pipeName = $"gba-broker-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new BrokerPipeServer(
            pipeName,
            identity,
            declaredCapabilities,
            consentStore,
            backend);
        WorkerArguments =
        [
            "--broker-pipe", pipeName,
            "--broker-package", identity.PackageId,
            "--broker-publisher", identity.PublisherId,
            "--broker-instance", identity.InstanceId,
            "--broker-nonce", _server.ChannelNonce,
        ];
    }

    public IReadOnlyList<string> WorkerArguments { get; }

    public Task RunAsync(CancellationToken cancellationToken) =>
        _server.RunAsync(cancellationToken);

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _server.SetLifecycle(state switch
        {
            WidgetLifecycleState.Background => BrokerLifecycleState.Background,
            WidgetLifecycleState.Visible => BrokerLifecycleState.Visible,
            WidgetLifecycleState.Interactive => BrokerLifecycleState.Interactive,
            WidgetLifecycleState.Destroying => BrokerLifecycleState.Destroying,
            _ => throw new InvalidOperationException(
                "Only stable host lifecycle states may reach the capability broker."),
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
