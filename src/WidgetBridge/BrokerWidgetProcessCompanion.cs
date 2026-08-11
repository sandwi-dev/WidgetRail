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
    private readonly bool _isolated;

    public BrokerWidgetProcessCompanion(
        string packageId,
        string publisherId,
        string instanceId,
        IReadOnlyList<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend,
        WidgetProcessCompanionContext context,
        AppLibraryArtworkRegistry? artworkRegistry = null,
        Action<BrokerHostEffect>? hostEffectSink = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = new BrokerWidgetIdentity(packageId, publisherId, instanceId);
        _isolated = context.IsolationPolicy == WidgetWorkerIsolationPolicy.RequireAppContainer;
        if (_isolated && string.IsNullOrWhiteSpace(context.AppContainerSid))
            throw new InvalidOperationException(
                "An isolated widget broker requires the runtime AppContainer SID.");
        var pipeName = $"gba-broker-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new BrokerPipeServer(
            pipeName,
            identity,
            declaredCapabilities,
            consentStore,
            backend,
            artworkRegistry ?? new AppLibraryArtworkRegistry(),
            isolatedClientAppContainerSid: context.AppContainerSid,
            hostGrantedCapabilities: [PlatformCapabilities.PrivateStateV1],
            hostEffectSink: hostEffectSink);
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
    internal IReadOnlySet<string> DeclaredCapabilities => _server.DeclaredCapabilities;
    internal IReadOnlySet<string> HostGrantedCapabilities => _server.HostGrantedCapabilities;

    public void BindWorkerProcess(int processId)
    {
        if (_isolated) _server.BindExpectedIsolatedClientProcess(processId);
    }

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

    public Task GrantDashboardGestureAuthorityAsync(
        WidgetDashboardGestureAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        _server.GrantDashboardGestureAuthority(
            authority.CapabilityId,
            authority.OperationId,
            authority.InputSequence,
            authority.SnapshotSequence,
            authority.ValidFor);
        return Task.CompletedTask;
    }

    public Task RevokeDashboardGestureAuthorityAsync(
        long inputSequence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _server.RevokeDashboardGestureAuthority(inputSequence);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
