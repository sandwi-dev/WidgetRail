using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

internal sealed record BridgeClientSnapshot(
    ConfiguredWidget Configured,
    ViewSnapshot Snapshot);

internal sealed record BridgeClientWorkerStatus(
    string Id,
    string Name,
    bool IsRunning,
    int Starts,
    string? FailureCode,
    bool CanRestart);

internal sealed record BridgeClientRegistrySnapshot(
    BridgeCatalog Catalog,
    long CatalogRevision,
    IReadOnlyList<BridgeClientWorkerStatus> Workers,
    WorkerResidencyBudgetSnapshot Residency);

internal sealed record BridgeClientInvalidation(string WidgetId, long Revision);
internal sealed record BridgeClientActionFailure(
    string WidgetId,
    string RuntimeGeneration,
    WidgetActionFailure Failure);
internal sealed record BridgeClientRuntimeFailure(string WidgetId, WidgetFailure Failure);

internal sealed class BridgeClientPublication<TValue>(
    TValue value,
    Action release) : IDisposable
{
    private Action? _release = release;
    internal TValue Value { get; } = value;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

internal interface IBridgeWidgetClient : IAsyncDisposable
{
    event EventHandler<long>? Invalidated;
    event EventHandler<WidgetActionFailure>? ActionFailed;
    event EventHandler<WidgetFailure>? Failed;
    bool IsRunning { get; }
    int Starts { get; }
    Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task SetLifecycleStateAsync(WidgetLifecycleState state, CancellationToken cancellationToken);
    Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken);
    Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken);
    Task UnloadAsync(CancellationToken cancellationToken);
}

internal sealed class WidgetProcessBridgeClient(WidgetProcessClient client)
    : IBridgeWidgetClient
{
    public event EventHandler<long>? Invalidated
    {
        add => client.Invalidated += value;
        remove => client.Invalidated -= value;
    }

    public event EventHandler<WidgetActionFailure>? ActionFailed
    {
        add => client.ActionFailed += value;
        remove => client.ActionFailed -= value;
    }

    public event EventHandler<WidgetFailure>? Failed
    {
        add => client.Failed += value;
        remove => client.Failed -= value;
    }

    public bool IsRunning => client.IsRunning;
    public int Starts => client.Starts;
    public Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        client.GetSnapshotAsync(cancellationToken);
    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken) =>
        client.SetLifecycleStateAsync(state, cancellationToken);
    public Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken) =>
        client.AdmitActionAsync(action, cancellationToken);
    public Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken) =>
        client.SendControllerInputAsync(input, authority, cancellationToken);
    public Task UnloadAsync(CancellationToken cancellationToken) =>
        client.UnloadAsync(cancellationToken);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}

internal sealed class BridgeClientRegistry : IAsyncDisposable
{
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RetireDeadline = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private readonly Dictionary<string, ClientRegistration> _clients =
        new(StringComparer.Ordinal);
    private readonly WorkerResidencyBudget _residentBudget;
    private readonly Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> _clientFactory;
    private readonly Func<BridgeClientInvalidation, Task> _invalidated;
    private readonly Func<BridgeClientActionFailure, Task> _actionFailed;
    private readonly Func<BridgeClientRuntimeFailure, Task> _failed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<Task> _detachedTasks = [];
    private readonly List<Exception> _detachedFailures = [];
    private BridgeCatalog _catalog;
    private long _catalogRevision;
    private bool _disposed;
    private readonly TaskCompletionSource _terminal = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal BridgeClientRegistry(
        BridgeCatalog catalog,
        WorkerResidencyBudgetOptions residencyBudget,
        Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> clientFactory,
        Func<BridgeClientInvalidation, Task> invalidated,
        Func<BridgeClientActionFailure, Task> actionFailed,
        Func<BridgeClientRuntimeFailure, Task> failed,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _residentBudget = new WorkerResidencyBudget(
            residencyBudget ?? throw new ArgumentNullException(nameof(residencyBudget)));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _invalidated = invalidated ?? throw new ArgumentNullException(nameof(invalidated));
        _actionFailed = actionFailed ?? throw new ArgumentNullException(nameof(actionFailed));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        _delay = delay ?? Task.Delay;
    }

    internal int RunningWorkerCount
    {
        get
        {
            lock (_gate)
                return _clients.Values.Count(item => !item.IsRetiring && item.Client.IsRunning);
        }
    }

    internal WorkerResidencyBudgetSnapshot ResidencyBudget => _residentBudget.Snapshot;

    internal (BridgeCatalog Catalog, long Revision) CatalogSnapshot()
    {
        lock (_gate) return (_catalog, _catalogRevision);
    }

    internal BridgeClientRegistrySnapshot DiagnosticsSnapshot()
    {
        lock (_gate)
        {
            var workers = _catalog.Widgets.Select(descriptor =>
            {
                _clients.TryGetValue(descriptor.Id, out var registration);
                var failure = registration?.LastFailure;
                return new BridgeClientWorkerStatus(
                    descriptor.Id,
                    descriptor.Name,
                    registration is { IsRetiring: false } && registration.Client.IsRunning,
                    registration?.Client.Starts ?? 0,
                    failure?.Code,
                    failure?.CanRestart ?? false);
            }).ToArray();
            return new BridgeClientRegistrySnapshot(
                _catalog, _catalogRevision, workers, _residentBudget.Snapshot);
        }
    }

    internal async Task<BridgeClientPublication<BridgeClientSnapshot>> GetSnapshotAsync(
        string widgetId,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            var residencyMode = WidgetResidencyPolicies.Resolve(
                registration.Configured.ResidencyPolicy).Mode;
            var hiddenAndRestricted =
                registration.HostLifecycle == WidgetLifecycleState.Background &&
                residencyMode is WidgetResidencyMode.SuspendWhenHidden or
                    WidgetResidencyMode.UnloadAfterIdle;
            ViewSnapshot snapshot;
            if (hiddenAndRestricted)
            {
                snapshot = registration.CachedSnapshot ??
                    throw new BridgeProtocolException(
                        "A hidden suspended widget has no cached snapshot. Make it Visible before rendering.");
            }
            else
            {
                registration.CancelIdleUnload();
                snapshot = await registration.Client.GetSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
                DemandCurrent(registration);
                registration.CachedSnapshot = snapshot;
                ScheduleIdleUnload(registration, sessionCancellation);
            }
            return AdmitPublication(
                registration,
                new BridgeClientSnapshot(registration.Configured, snapshot));
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal async Task<BridgeClientPublication<WidgetLifecycleState>> SetLifecycleAsync(
        string widgetId,
        WidgetLifecycleState state,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            registration.CancelIdleUnload();
            await registration.Client.SetLifecycleStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            registration.HostLifecycle = state;
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, state);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitActionAsync(
        string widgetId,
        WidgetActionEvent action,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        return await AdmitActionAsync(
            registration, action, sessionCancellation, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitQuickActionAsync(
        string widgetId,
        string quickActionId,
        long sequence,
        long monotonicTimestampMicroseconds,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        var quickAction = registration.Configured.QuickActions.SingleOrDefault(
            action => string.Equals(action.Id, quickActionId, StringComparison.Ordinal)) ??
            throw new BridgeProtocolException($"Unknown quick action '{quickActionId}'.");
        return await AdmitActionAsync(
            registration,
            new WidgetActionEvent(
                quickAction.ActionId,
                quickAction.SourceElementId,
                quickAction.ControllerButton,
                ControllerEventPhase.Pressed,
                sequence,
                monotonicTimestampMicroseconds),
            sessionCancellation,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BridgeClientPublication<bool>> SendControllerInputAsync(
        string widgetId,
        ControllerInputEvent input,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            DemandInteractionAllowed(registration);
            registration.CancelIdleUnload();
            var handled = await registration.Client.SendControllerInputAsync(
                    input,
                    ResolveDashboardGestureAuthority(registration, input),
                    cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, handled);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal async Task<BridgeClientPublication<WidgetLifecycleState>> RestartAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ClientRegistration oldRegistration;
            Task? priorRetirement = null;
            lock (_gate)
            {
                DemandNotDisposed();
                var configured = _catalog.GetConfigured(widgetId);
                if (!_clients.TryGetValue(widgetId, out oldRegistration!))
                {
                    var fresh = CreateRegistration(configured);
                    _clients.Add(widgetId, fresh);
                    return AdmitPublicationLocked(fresh, WidgetLifecycleState.Background);
                }
                if (oldRegistration.IsRetiring)
                    priorRetirement = oldRegistration.RetirementCompletion;
            }
            if (priorRetirement is not null)
            {
                await priorRetirement.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var gateTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            gateTimeout.CancelAfter(OperationDeadline);
            try
            {
                await oldRegistration.OperationGate.WaitAsync(gateTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BridgeProtocolException(
                    $"Widget '{widgetId}' did not become available for restart.");
            }

            var reserved = false;
            var published = false;
            ClientRegistration? freshRegistration = null;
            try
            {
                WidgetLifecycleState previousState;
                lock (_gate)
                {
                    DemandNotDisposed();
                    _ = _catalog.GetConfigured(widgetId);
                    if (!_clients.TryGetValue(widgetId, out var current) ||
                        !ReferenceEquals(current, oldRegistration) ||
                        oldRegistration.IsRetiring)
                        continue;
                    oldRegistration.BeginRetirementLocked();
                    oldRegistration.RetirementTask = oldRegistration.RetirementCompletion;
                    previousState = oldRegistration.HostLifecycle;
                    reserved = true;
                }

                await oldRegistration.PublicationsDrained.ConfigureAwait(false);
                oldRegistration.CancelIdleUnload();
                await oldRegistration.BeginTerminalAndDrainIdleUnloadAsync(OperationDeadline)
                    .ConfigureAwait(false);
                await DisposeClientWithDeadlineAsync(oldRegistration).ConfigureAwait(false);

                ConfiguredWidget configured;
                lock (_gate)
                {
                    DemandNotDisposed();
                    configured = _catalog.GetConfigured(widgetId);
                }
                freshRegistration = CreateRegistration(configured);
                if (previousState != WidgetLifecycleState.Background)
                {
                    await freshRegistration.Client.SetLifecycleStateAsync(
                        previousState, cancellationToken).ConfigureAwait(false);
                    freshRegistration.HostLifecycle = previousState;
                }

                BridgeClientPublication<WidgetLifecycleState> result;
                lock (_gate)
                {
                    DemandNotDisposed();
                    var latest = _catalog.GetConfigured(widgetId);
                    if (!_clients.TryGetValue(widgetId, out var current) ||
                        !ReferenceEquals(current, oldRegistration) ||
                        !string.Equals(
                            latest.WorkerFingerprint,
                            freshRegistration.Configured.WorkerFingerprint,
                            StringComparison.Ordinal))
                        throw new BridgeProtocolException(
                            $"Widget '{widgetId}' changed while its fresh worker was prepared.");
                    _clients[widgetId] = freshRegistration;
                    result = AdmitPublicationLocked(freshRegistration, previousState);
                    oldRegistration.CompleteRetirementLocked();
                    published = true;
                }
                return result;
            }
            catch (Exception exception)
            {
                if (freshRegistration is not null)
                {
                    try
                    {
                        await DisposeUnpublishedAsync(freshRegistration).ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(exception, cleanupFailure);
                    }
                }
                throw;
            }
            finally
            {
                if (reserved && !published)
                {
                    lock (_gate)
                    {
                        if (_clients.TryGetValue(widgetId, out var current) &&
                            ReferenceEquals(current, oldRegistration))
                            _clients.Remove(widgetId);
                        oldRegistration.CompleteRetirementLocked();
                    }
                }
                oldRegistration.OperationGate.Release();
            }
        }
    }

    internal bool ApplyCatalog(BridgeCatalog catalog, long revision)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        lock (_gate)
        {
            DemandNotDisposed();
            if (revision < _catalogRevision ||
                (revision == _catalogRevision && _catalog.IsEquivalentTo(catalog)))
                return false;
            if (revision == _catalogRevision) return false;
            _catalog = catalog;
            _catalogRevision = revision;
            foreach (var pair in _clients.ToArray())
            {
                ConfiguredWidget? configured = null;
                try { configured = catalog.GetConfigured(pair.Key); }
                catch (BridgeProtocolException) { }
                if (configured is not null && string.Equals(
                        configured.WorkerFingerprint,
                        pair.Value.Configured.WorkerFingerprint,
                        StringComparison.Ordinal))
                {
                    pair.Value.Configured = configured;
                    continue;
                }
                _ = StartDetachedRetirementLocked(pair.Value);
            }
        }
        return true;
    }

    internal BridgeClientPublication<BridgeWidgetDescriptor>? TryAdmitHostEffect(
        string widgetId,
        string expectedWorkerFingerprint)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(widgetId, out var registration) &&
                !registration.IsRetiring &&
                string.Equals(
                    registration.Configured.WorkerFingerprint,
                    expectedWorkerFingerprint,
                    StringComparison.Ordinal) &&
                registration.HostLifecycle == WidgetLifecycleState.Interactive)
                return AdmitPublicationLocked(
                    registration, registration.Configured.PublicDescriptor());
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        ClientRegistration[]? registrations = null;
        Task[]? retirementCompletions = null;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                registrations = _clients.Values.ToArray();
                retirementCompletions = registrations
                    .Select(StartDetachedRetirementLocked)
                    .ToArray();
            }
        }
        if (registrations is null)
        {
            await _terminal.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            await Task.WhenAll(retirementCompletions!).ConfigureAwait(false);
            while (true)
            {
                Task[] detached;
                lock (_gate) detached = _detachedTasks.ToArray();
                if (detached.Length == 0) break;
                try { await Task.WhenAll(detached).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
            }

            Exception[] failures;
            lock (_gate) failures = _detachedFailures.ToArray();
            if (failures.Length != 0)
                throw new AggregateException("One or more bridge clients failed terminal disposal.", failures);
            _terminal.TrySetResult();
        }
        catch (Exception exception)
        {
            _terminal.TrySetException(exception);
            throw;
        }
    }

    private async Task<ClientRegistration> GetOrCreateAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? retirement = null;
            lock (_gate)
            {
                DemandNotDisposed();
                var configured = _catalog.GetConfigured(widgetId);
                if (_clients.TryGetValue(widgetId, out var existing))
                {
                    if (!existing.IsRetiring && string.Equals(
                            existing.Configured.WorkerFingerprint,
                            configured.WorkerFingerprint,
                            StringComparison.Ordinal))
                        return existing;
                    retirement = StartDetachedRetirementLocked(existing);
                }
                else
                {
                    var registration = CreateRegistration(configured);
                    _clients.Add(widgetId, registration);
                    return registration;
                }
            }

            await retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task StartDetachedRetirementLocked(ClientRegistration registration)
    {
        if (registration.RetirementTask is not null)
            return registration.RetirementCompletion;
        registration.BeginRetirementLocked();
        var task = RetireRegistrationAsync(registration);
        registration.RetirementTask = task;
        TrackDetachedLocked(task);
        return registration.RetirementCompletion;
    }

    private async Task RetireRegistrationAsync(ClientRegistration registration)
    {
        try
        {
            await DisposeRegistrationAsync(registration).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_clients.TryGetValue(registration.Configured.Id, out var current) &&
                    ReferenceEquals(current, registration))
                    _clients.Remove(registration.Configured.Id);
                registration.CompleteRetirementLocked();
            }
        }
    }

    private ClientRegistration CreateRegistration(ConfiguredWidget configured)
    {
        var reservationOwner = new object();
        var client = _clientFactory(
            configured,
            () => _residentBudget.Reserve(
                reservationOwner,
                configured.Id,
                configured.MemoryLimitMb,
                WidgetBridgeServer.IsTrustedSettings(configured)));
        var registration = new ClientRegistration(configured, client);
        var runtimeGeneration = configured.PublicDescriptor().RuntimeGeneration;
        client.Invalidated += (_, revision) =>
            TrackDetached(PublishIfCurrentAsync(
                registration,
                () => _invalidated(new BridgeClientInvalidation(configured.Id, revision)),
                requireInvalidationAuthority: true));
        client.ActionFailed += (_, failure) =>
            TrackDetached(PublishIfCurrentAsync(
                registration,
                () => _actionFailed(new BridgeClientActionFailure(
                    configured.Id, runtimeGeneration, failure))));
        client.Failed += (_, failure) =>
        {
            registration.RecordFailure(failure);
            TrackDetached(PublishIfCurrentAsync(
                registration,
                () => _failed(new BridgeClientRuntimeFailure(configured.Id, failure))));
        };
        return registration;
    }

    private async Task PublishIfCurrentAsync(
        ClientRegistration registration,
        Func<Task> publish,
        bool requireInvalidationAuthority = false)
    {
        BridgeClientPublication<bool>? admission = null;
        lock (_gate)
        {
            if (IsCurrentLocked(registration) &&
                (!requireInvalidationAuthority || registration.MayPublishInvalidation))
                admission = AdmitPublicationLocked(registration, true);
        }
        if (admission is null) return;
        using (admission)
            await publish().ConfigureAwait(false);
    }

    private BridgeClientPublication<TValue> AdmitPublication<TValue>(
        ClientRegistration registration,
        TValue value)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(registration))
                throw new BridgeProtocolException(
                    $"Widget '{registration.Configured.Id}' changed during the operation.");
            return AdmitPublicationLocked(registration, value);
        }
    }

    private BridgeClientPublication<TValue> AdmitPublicationLocked<TValue>(
        ClientRegistration registration,
        TValue value)
    {
        registration.AdmitPublicationLocked();
        return new BridgeClientPublication<TValue>(
            value,
            () => ReleasePublication(registration));
    }

    private void ReleasePublication(ClientRegistration registration)
    {
        lock (_gate) registration.ReleasePublicationLocked();
    }

    private void TrackDetached(Task task)
    {
        if (task.IsCompletedSuccessfully) return;
        lock (_gate) TrackDetachedLocked(task);
    }

    private void TrackDetachedLocked(Task task)
    {
        if (task.IsCompletedSuccessfully) return;
        _detachedTasks.Add(task);
        _ = ObserveDetachedAsync(task);
    }

    private async Task ObserveDetachedAsync(Task task)
    {
        Exception? failure = null;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
        }
        finally
        {
            lock (_gate)
            {
                _detachedTasks.Remove(task);
                if (failure is not null) _detachedFailures.Add(failure);
            }
        }
    }

    private async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitActionAsync(
        ClientRegistration registration,
        WidgetActionEvent action,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            if (registration.HostLifecycle == WidgetLifecycleState.Background)
                return AdmitPublication(registration, WidgetOperationAdmission.RejectedInactive);
            registration.CancelIdleUnload();
            var admission = await registration.Client.AdmitActionAsync(action, cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, admission);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private void ScheduleIdleUnload(
        ClientRegistration registration,
        CancellationToken sessionCancellation)
    {
        var policy = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy);
        if (policy.Mode != WidgetResidencyMode.UnloadAfterIdle ||
            registration.HostLifecycle != WidgetLifecycleState.Background ||
            !registration.Client.IsRunning || policy.IdleDuration is not { } delay)
            return;
        registration.ScheduleIdleUnload(
            sessionCancellation,
            (generation, cancellationToken) =>
                RunIdleUnloadAsync(registration, generation, delay, cancellationToken));
    }

    private async Task RunIdleUnloadAsync(
        ClientRegistration registration,
        long generation,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);
            await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!registration.IsIdleUnloadCurrent(generation) ||
                    !IsCurrent(registration) ||
                    registration.HostLifecycle != WidgetLifecycleState.Background ||
                    !registration.Client.IsRunning ||
                    WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode !=
                        WidgetResidencyMode.UnloadAfterIdle)
                    return;
                using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                shutdown.CancelAfter(RetireDeadline);
                await registration.Client.UnloadAsync(shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                registration.OperationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static void DemandInteractionAllowed(ClientRegistration registration)
    {
        if (registration.HostLifecycle != WidgetLifecycleState.Background) return;
        var mode = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode;
        if (mode is WidgetResidencyMode.SuspendWhenHidden or WidgetResidencyMode.UnloadAfterIdle)
            throw new BridgeProtocolException(
                "Hidden interaction is disabled by this widget's residency policy.");
    }

    private void DemandCurrent(ClientRegistration registration)
    {
        if (!IsCurrent(registration))
            throw new BridgeProtocolException(
                $"Widget '{registration.Configured.Id}' changed during the operation.");
    }

    private bool IsCurrent(ClientRegistration registration)
    {
        lock (_gate) return IsCurrentLocked(registration);
    }

    private bool IsCurrentLocked(ClientRegistration registration) =>
        !registration.IsRetiring &&
        _clients.TryGetValue(registration.Configured.Id, out var current) &&
        ReferenceEquals(current, registration);

    private async Task DisposeRegistrationAsync(ClientRegistration registration)
    {
        await registration.PublicationsDrained.ConfigureAwait(false);
        await registration.BeginTerminalAndDrainIdleUnloadAsync(OperationDeadline)
            .ConfigureAwait(false);
        var gateEntered = false;
        using var gateDeadline = new CancellationTokenSource(OperationDeadline);
        try
        {
            gateEntered = await registration.OperationGate.WaitAsync(
                OperationDeadline, gateDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Task? disposal = null;
        try
        {
            disposal = registration.Client.DisposeAsync().AsTask();
            using var retireDeadline = new CancellationTokenSource(RetireDeadline);
            await disposal.WaitAsync(retireDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (disposal is not null) _ = ObserveCompletionAsync(disposal);
        }
        catch (Exception exception) when (exception is IOException or
                                               InvalidOperationException or
                                               ObjectDisposedException)
        {
        }
        finally
        {
            if (gateEntered)
            {
                registration.OperationGate.Release();
                registration.OperationGate.Dispose();
            }
        }
    }

    private async Task DisposeClientWithDeadlineAsync(ClientRegistration registration)
    {
        var retirement = registration.Client.DisposeAsync().AsTask();
        using var retireTimeout = new CancellationTokenSource(RetireDeadline);
        try
        {
            await retirement.WaitAsync(retireTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = ObserveCompletionAsync(retirement);
            throw new BridgeProtocolException(
                $"Widget '{registration.Configured.Id}' could not be retired within the restart deadline.");
        }
    }

    private async Task DisposeUnpublishedAsync(ClientRegistration registration)
    {
        lock (_gate) registration.BeginRetirementLocked();
        try
        {
            await DisposeRegistrationAsync(registration).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) registration.CompleteRetirementLocked();
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private void DemandNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static WidgetDashboardGestureAuthority? ResolveDashboardGestureAuthority(
        ClientRegistration registration,
        ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.DashboardQuickAction) return null;
        if (registration.HostLifecycle != WidgetLifecycleState.Visible)
            throw new BridgeProtocolException(
                "Dashboard quick actions require the widget to remain Visible.");
        var snapshot = registration.CachedSnapshot ?? throw new BridgeProtocolException(
            "Dashboard quick action has no cached rendered snapshot.");
        if (snapshot.Sequence != input.SnapshotSequence)
            throw new BridgeProtocolException(
                "Dashboard quick action targets a stale snapshot sequence.");
        var quickAction = snapshot.QuickActions.SingleOrDefault(
            action => action.Button == input.Button) ?? throw new BridgeProtocolException(
                "Dashboard button is not exposed by the cached snapshot.");
        if (input.Phase != ControllerEventPhase.Pressed ||
            input.Origin != ControllerInputOrigin.PhysicalController ||
            quickAction.Capability is null)
        {
            registration.AcceptDashboardInputSequence(input.Sequence);
            return null;
        }

        var requested = quickAction.Capability;
        if (!PlatformCapabilities.TryGet(requested.CapabilityId, out var capability) ||
            capability.KindForOperation(requested.OperationId) != BrokerCapabilityKind.Control ||
            !capability.Operations.Contains(requested.OperationId))
            throw new BridgeProtocolException(
                "Dashboard quick action names an unsupported control operation.");
        if (!registration.Configured.DeclaredCapabilities.Contains(
                requested.CapabilityId, StringComparer.Ordinal))
            throw new BridgeProtocolException(
                "Dashboard quick action capability is not declared by its package.");
        registration.AcceptDashboardInputSequence(input.Sequence);
        return new WidgetDashboardGestureAuthority(
            requested.CapabilityId,
            requested.OperationId,
            input.Sequence,
            input.SnapshotSequence,
            PlatformCapabilityBroker.MaximumDashboardGestureLifetime);
    }

    private sealed class ClientRegistration(
        ConfiguredWidget configured,
        IBridgeWidgetClient client)
    {
        private ConfiguredWidget _configured = configured;
        internal ConfiguredWidget Configured
        {
            get => Volatile.Read(ref _configured);
            set => Volatile.Write(ref _configured, value);
        }

        internal IBridgeWidgetClient Client { get; } = client;
        internal SemaphoreSlim OperationGate { get; } = new(1, 1);
        private int _hostLifecycle = (int)WidgetLifecycleState.Background;
        internal WidgetLifecycleState HostLifecycle
        {
            get => (WidgetLifecycleState)Volatile.Read(ref _hostLifecycle);
            set => Volatile.Write(ref _hostLifecycle, (int)value);
        }

        internal ViewSnapshot? CachedSnapshot { get; set; }
        private long _lastDashboardInputSequence;
        private readonly object _residencyGate = new();
        private CancellationTokenSource? _idleUnloadCancellation;
        private readonly HashSet<Task> _idleUnloadTasks = [];
        private long _idleUnloadGeneration;
        private bool _terminal;
        private WorkerFailureDiagnostic? _lastFailure;
        private int _activePublications;
        private readonly TaskCompletionSource _publicationsDrained = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retirementCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal WorkerFailureDiagnostic? LastFailure => Volatile.Read(ref _lastFailure);
        internal bool IsRetiring { get; private set; }
        internal Task? RetirementTask { get; set; }
        internal Task RetirementCompletion => _retirementCompletion.Task;
        internal Task PublicationsDrained => _publicationsDrained.Task;
        internal bool MayPublishInvalidation =>
            HostLifecycle != WidgetLifecycleState.Background ||
            WidgetResidencyPolicies.Resolve(Configured.ResidencyPolicy).Mode ==
                WidgetResidencyMode.KeepAlive;

        internal void AcceptDashboardInputSequence(long sequence)
        {
            if (sequence <= _lastDashboardInputSequence)
                throw new BridgeProtocolException(
                    "Dashboard controller input sequence was replayed.");
            _lastDashboardInputSequence = sequence;
        }

        internal void RecordFailure(WidgetFailure failure) => Volatile.Write(
            ref _lastFailure,
            new WorkerFailureDiagnostic(failure.Reason.ToString(), failure.CanRestart));

        internal void BeginRetirementLocked()
        {
            if (IsRetiring) return;
            IsRetiring = true;
            if (_activePublications == 0) _publicationsDrained.TrySetResult();
        }

        internal void CompleteRetirementLocked() =>
            _retirementCompletion.TrySetResult();

        internal void AdmitPublicationLocked()
        {
            if (IsRetiring)
                throw new BridgeProtocolException(
                    $"Widget '{Configured.Id}' changed before publication.");
            _activePublications++;
        }

        internal void ReleasePublicationLocked()
        {
            if (_activePublications <= 0)
                throw new InvalidOperationException("Publication admission was released twice.");
            _activePublications--;
            if (IsRetiring && _activePublications == 0)
                _publicationsDrained.TrySetResult();
        }

        internal void ScheduleIdleUnload(
            CancellationToken bridgeCancellation,
            Func<long, CancellationToken, Task> start)
        {
            ArgumentNullException.ThrowIfNull(start);
            lock (_residencyGate)
            {
                if (_terminal) return;
                CancelIdleUnloadLocked();
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    bridgeCancellation);
                var generation = ++_idleUnloadGeneration;
                Task task;
                try
                {
                    task = start(generation, cancellation.Token);
                }
                catch
                {
                    cancellation.Dispose();
                    throw;
                }
                _idleUnloadCancellation = cancellation;
                _idleUnloadTasks.Add(task);
                _ = ObserveIdleUnloadAsync(task, cancellation);
            }
        }

        internal bool IsIdleUnloadCurrent(long generation)
        {
            lock (_residencyGate)
                return generation == _idleUnloadGeneration &&
                    _idleUnloadCancellation is { IsCancellationRequested: false };
        }

        internal void CancelIdleUnload()
        {
            lock (_residencyGate) CancelIdleUnloadLocked();
        }

        internal async Task BeginTerminalAndDrainIdleUnloadAsync(TimeSpan deadline)
        {
            Task[] tasks;
            lock (_residencyGate)
            {
                _terminal = true;
                CancelIdleUnloadLocked();
                tasks = _idleUnloadTasks.ToArray();
            }
            if (tasks.Length == 0) return;

            var drain = Task.WhenAll(tasks);
            using var cancellation = new CancellationTokenSource(deadline);
            try
            {
                await drain.WaitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _ = ObserveCompletionAsync(drain);
            }
        }

        private async Task ObserveIdleUnloadAsync(
            Task task,
            CancellationTokenSource cancellation)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
            finally
            {
                lock (_residencyGate)
                {
                    _idleUnloadTasks.Remove(task);
                    if (ReferenceEquals(_idleUnloadCancellation, cancellation))
                        _idleUnloadCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private void CancelIdleUnloadLocked()
        {
            _idleUnloadGeneration++;
            if (_idleUnloadCancellation is null) return;
            _idleUnloadCancellation.Cancel();
            _idleUnloadCancellation = null;
        }
    }

    private sealed record WorkerFailureDiagnostic(string Code, bool CanRestart);
}
