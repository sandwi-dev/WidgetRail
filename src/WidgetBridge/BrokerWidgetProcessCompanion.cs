using WidgetRail.PlatformBroker;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Bridge-owned capability channel created once per worker process. Identity,
/// declarations, consent and backend never come from the widget package.
/// </summary>
internal sealed class BrokerWidgetProcessCompanion :
    IWidgetProcessCompanionSession,
    IWidgetActionEffectCoordinator
{
    private readonly BrokerPipeServer _server;
    private readonly DeferredHostEffectCoordinator _hostEffects;
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
        Action<BrokerHostEffect>? hostEffectSink = null,
        Action<BrokerCapabilityDiagnostic>? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = new BrokerWidgetIdentity(packageId, publisherId, instanceId);
        _hostEffects = new DeferredHostEffectCoordinator(hostEffectSink);
        _isolated = context.IsolationPolicy == WidgetWorkerIsolationPolicy.RequireAppContainer;
        if (_isolated && string.IsNullOrWhiteSpace(context.AppContainerSid))
            throw new InvalidOperationException(
                "An isolated widget broker requires the runtime AppContainer SID.");
        var pipeName = $"wrail-broker-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _server = new BrokerPipeServer(
            pipeName,
            identity,
            declaredCapabilities,
            consentStore,
            backend,
            artworkRegistry ?? new AppLibraryArtworkRegistry(),
            isolatedClientAppContainerSid: context.AppContainerSid,
            hostGrantedCapabilities: [PlatformCapabilities.PrivateStateV1],
            hostEffectSink: null,
            diagnosticSink: diagnosticSink,
            contextualHostEffectSink: _hostEffects.Register,
            actionExecutionAdmission: _hostEffects.Admit);
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
        var brokerState = state switch
        {
            WidgetLifecycleState.Background => BrokerLifecycleState.Background,
            WidgetLifecycleState.Visible => BrokerLifecycleState.Visible,
            WidgetLifecycleState.Interactive => BrokerLifecycleState.Interactive,
            WidgetLifecycleState.Destroying => BrokerLifecycleState.Destroying,
            _ => throw new InvalidOperationException(
                "Only stable host lifecycle states may reach the capability broker."),
        };
        if (state == WidgetLifecycleState.Interactive)
        {
            _server.SetLifecycle(brokerState);
            _hostEffects.SetLifecycle(state);
        }
        else
        {
            _hostEffects.SetLifecycle(state);
            _server.SetLifecycle(brokerState);
        }
        return Task.CompletedTask;
    }

    void IWidgetActionEffectCoordinator.CompleteAction(
        WidgetActionExecutionTerminal terminal) => _hostEffects.Complete(terminal);

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

    public ValueTask DisposeAsync()
    {
        _hostEffects.Dispose();
        return _server.DisposeAsync();
    }

    private sealed class DeferredHostEffectCoordinator(
        Action<BrokerHostEffect>? sink) : IDisposable
    {
        private readonly object _gate = new();
        private long _lastTerminalExecutionId;
        private PendingEffect? _pending;
        private bool _interactive;
        private bool _disposed;

        internal void Register(
            BrokerHostEffect effect,
            long? actionExecutionId,
            long? lifecycleGeneration)
        {
            ArgumentNullException.ThrowIfNull(effect);
            lock (_gate)
            {
                if (_disposed || !_interactive) return;
                if (actionExecutionId is null)
                {
                    PublishSafe(effect);
                    return;
                }
                if (lifecycleGeneration != _lifecycleGeneration ||
                    _admitted is not { } admitted ||
                    admitted.ExecutionId != actionExecutionId.Value ||
                    admitted.LifecycleGeneration != lifecycleGeneration)
                    return;
                if (actionExecutionId > _lastTerminalExecutionId)
                {
                    if (_pending is null)
                        _pending = new PendingEffect(
                            actionExecutionId.Value,
                            lifecycleGeneration.Value,
                            effect);
                    else if (_pending.ExecutionId != actionExecutionId.Value)
                        return;
                }
            }
        }

        internal long? Admit(long actionExecutionId)
        {
            lock (_gate)
            {
                if (_disposed || !_interactive ||
                    actionExecutionId <= _lastTerminalExecutionId ||
                    _pending is not null && _pending.ExecutionId != actionExecutionId ||
                    _admitted is not null && _admitted.ExecutionId != actionExecutionId)
                    return null;
                _admitted ??= new AdmittedEffect(
                    actionExecutionId, _lifecycleGeneration);
                return _admitted.LifecycleGeneration;
            }
        }

        internal void Complete(WidgetActionExecutionTerminal terminal)
        {
            lock (_gate)
            {
                if (_disposed || terminal.ExecutionId <= _lastTerminalExecutionId) return;
                _lastTerminalExecutionId = terminal.ExecutionId;
                if (_admitted is { } admitted &&
                    admitted.ExecutionId <= terminal.ExecutionId)
                    _admitted = null;
                if (_pending is { } pending && pending.ExecutionId <= terminal.ExecutionId)
                {
                    if (pending.ExecutionId == terminal.ExecutionId &&
                        pending.LifecycleGeneration == _lifecycleGeneration &&
                        terminal.Outcome == WidgetActionExecutionOutcome.Succeeded &&
                        _interactive)
                        PublishSafe(pending.Effect);
                    _pending = null;
                }
            }
        }

        internal void SetLifecycle(WidgetLifecycleState state)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var interactive = state == WidgetLifecycleState.Interactive;
                if (_interactive != interactive) _lifecycleGeneration++;
                _interactive = interactive;
                if (!_interactive)
                {
                    _admitted = null;
                    _pending = null;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _interactive = false;
                _admitted = null;
                _pending = null;
            }
        }

        private void PublishSafe(BrokerHostEffect effect)
        {
            try { sink?.Invoke(effect); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Launch completion is already final. Presentation publication
                // cannot poison the worker action or broker session.
            }
        }

        private long _lifecycleGeneration;
        private AdmittedEffect? _admitted;

        private sealed record AdmittedEffect(
            long ExecutionId,
            long LifecycleGeneration);

        private sealed record PendingEffect(
            long ExecutionId,
            long LifecycleGeneration,
            BrokerHostEffect Effect);
    }
}
