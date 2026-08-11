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
internal sealed record BridgeClientNotificationStatus(
    int Pending,
    int DroppedFailures,
    int ActivePublications,
    bool IsRetiring);

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
    private static readonly TimeSpan RestartDeadline = TimeSpan.FromSeconds(12);
    private readonly object _gate = new();
    private readonly Dictionary<string, ClientRegistration> _clients =
        new(StringComparer.Ordinal);
    private readonly WorkerResidencyBudget _residentBudget;
    private readonly Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> _clientFactory;
    private readonly Func<BridgeClientInvalidation, CancellationToken, Task> _invalidated;
    private readonly Func<BridgeClientActionFailure, CancellationToken, Task> _actionFailed;
    private readonly Func<BridgeClientRuntimeFailure, CancellationToken, Task> _failed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<ClientRegistration> _activeRetirements = [];
    private int _terminalFailureCount;
    private Exception? _firstTerminalFailure;
    private int _activeRestarts;
    private TaskCompletionSource? _restartsDrained;
    private BridgeCatalog _catalog;
    private long _catalogRevision;
    private bool _disposed;
    private readonly TaskCompletionSource _terminal = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal BridgeClientRegistry(
        BridgeCatalog catalog,
        WorkerResidencyBudgetOptions residencyBudget,
        Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> clientFactory,
        Func<BridgeClientInvalidation, CancellationToken, Task> invalidated,
        Func<BridgeClientActionFailure, CancellationToken, Task> actionFailed,
        Func<BridgeClientRuntimeFailure, CancellationToken, Task> failed,
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

    internal bool IsGateHeldByCurrentThread => Monitor.IsEntered(_gate);

    internal BridgeClientNotificationStatus NotificationStatus(string widgetId)
    {
        lock (_gate)
        {
            var registration = _clients.TryGetValue(widgetId, out var current)
                ? current
                : throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
            return new BridgeClientNotificationStatus(
                registration.NotificationLane.PendingCount,
                registration.NotificationLane.DroppedFailures,
                registration.ActivePublications,
                registration.IsRetiring);
        }
    }

    internal Task DrainNotificationsAsync(string widgetId)
    {
        lock (_gate)
        {
            var registration = _clients.TryGetValue(widgetId, out var current)
                ? current
                : throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
            return registration.NotificationLane.DrainAsync();
        }
    }

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

    internal async Task<BridgeClientPublication<BridgeClientSnapshot>>
        EstablishPresentationAsync(
            string widgetId,
            WidgetLifecycleState state,
            CancellationToken sessionCancellation,
            CancellationToken cancellationToken)
    {
        if (state == WidgetLifecycleState.Background)
            throw new BridgeProtocolException(
                "A background widget cannot establish a visible presentation.");
        var registration = await GetOrCreateAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startRetirement = false;
        try
        {
            DemandCurrent(registration);
            registration.CancelIdleUnload();
            await registration.Client.SetLifecycleStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            var snapshot = await registration.Client.GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);

            // Lifecycle and its first render-facing revision commit together.
            // No other operation can observe a Visible/Interactive registration
            // whose first snapshot failed admission.
            registration.CachedSnapshot = snapshot;
            registration.HostLifecycle = state;
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(
                registration,
                new BridgeClientSnapshot(registration.Configured, snapshot));
        }
        catch
        {
            lock (_gate)
            {
                if (_clients.TryGetValue(widgetId, out var current) &&
                    ReferenceEquals(current, registration))
                    startRetirement = ReserveRetirementLocked(
                        registration, restartReserved: false);
            }
            if (startRetirement) StartRetirement(registration);
            throw;
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
        AdmitRestart();
        try
        {
            return await RestartCoreAsync(widgetId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseRestart();
        }
    }

    private async Task<BridgeClientPublication<WidgetLifecycleState>> RestartCoreAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var startRetirement = false;
            ClientRegistration? freshRegistration = null;
            WidgetLifecycleState previousState;
            try
            {
                lock (_gate)
                {
                    DemandNotDisposed();
                    _ = _catalog.GetConfigured(widgetId);
                    if (!_clients.TryGetValue(widgetId, out var current) ||
                        !ReferenceEquals(current, oldRegistration) ||
                        oldRegistration.IsRetiring)
                        continue;
                    previousState = oldRegistration.HostLifecycle;
                    startRetirement = ReserveRetirementLocked(
                        oldRegistration, restartReserved: true);
                    reserved = true;
                }
            }
            finally
            {
                oldRegistration.OperationGate.Release();
            }

            if (startRetirement) StartRetirement(oldRegistration);
            try
            {
                using var restartDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                restartDeadline.CancelAfter(RestartDeadline);
                try
                {
                    await oldRegistration.ResourceRetired.WaitAsync(restartDeadline.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new BridgeProtocolException(
                        $"Widget '{widgetId}' did not retire within the restart deadline.");
                }
                if (oldRegistration.RetirementFailure is { } retirementFailure)
                    throw new AggregateException(
                        $"Widget '{widgetId}' failed retirement.", retirementFailure);

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
                        !oldRegistration.RestartReserved ||
                        !string.Equals(
                            latest.WorkerFingerprint,
                            freshRegistration.Configured.WorkerFingerprint,
                            StringComparison.Ordinal))
                        throw new BridgeProtocolException(
                            $"Widget '{widgetId}' changed while its fresh worker was prepared.");
                    _clients[widgetId] = freshRegistration;
                    result = AdmitPublicationLocked(freshRegistration, previousState);
                    oldRegistration.RestartReserved = false;
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
                    lock (_gate) AbandonRestartLocked(oldRegistration);
                }
            }
        }
    }

    internal bool ApplyCatalog(BridgeCatalog catalog, long revision)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        List<ClientRegistration>? starts = null;
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
                if (ReserveRetirementLocked(pair.Value, restartReserved: false))
                    (starts ??= []).Add(pair.Value);
            }
        }
        if (starts is not null)
            foreach (var registration in starts) StartRetirement(registration);
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
        List<ClientRegistration>? starts = null;
        Task? restartsDrained = null;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                registrations = _clients.Values.ToArray();
                foreach (var registration in registrations)
                {
                    registration.RestartReserved = false;
                    if (ReserveRetirementLocked(registration, restartReserved: false))
                        (starts ??= []).Add(registration);
                    if (registration.ResourceRetired.IsCompleted)
                        CompleteRegistrationRetirementLocked(registration);
                }
                restartsDrained = _restartsDrained?.Task ?? Task.CompletedTask;
            }
        }
        if (registrations is null)
        {
            await _terminal.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (starts is not null)
                foreach (var registration in starts) StartRetirement(registration);
            await Task.WhenAll(registrations.Select(item => item.RetirementCompletion))
                .ConfigureAwait(false);
            await restartsDrained!.ConfigureAwait(false);
            while (true)
            {
                Task[] active;
                lock (_gate)
                    active = _activeRetirements.Select(item => item.ResourceRetired).ToArray();
                if (active.Length == 0) break;
                await Task.WhenAll(active).ConfigureAwait(false);
            }

            Exception? firstFailure;
            int failureCount;
            lock (_gate)
            {
                firstFailure = _firstTerminalFailure;
                failureCount = _terminalFailureCount;
            }
            if (firstFailure is not null)
                throw new AggregateException(
                    $"{failureCount} bridge client terminal operation(s) failed; only the first failure is retained.",
                    firstFailure);
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
            ClientRegistration? start = null;
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
                    if (ReserveRetirementLocked(existing, restartReserved: false))
                        start = existing;
                    retirement = existing.RetirementCompletion;
                }
                else
                {
                    var registration = CreateRegistration(configured);
                    _clients.Add(widgetId, registration);
                    return registration;
                }
            }

            if (start is not null) StartRetirement(start);
            await retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ReserveRetirementLocked(
        ClientRegistration registration,
        bool restartReserved)
    {
        registration.BeginRetirementLocked();
        if (restartReserved) registration.RestartReserved = true;
        if (registration.RetirementStarted) return false;
        registration.RetirementStarted = true;
        _activeRetirements.Add(registration);
        return true;
    }

    private void StartRetirement(ClientRegistration registration)
    {
        // The method catches every terminal exception and publishes completion
        // through ResourceRetired/RetirementCompletion. _activeRetirements is
        // the bounded owner used by terminal registry disposal.
        _ = RetireRegistrationAsync(registration);
    }

    private async Task RetireRegistrationAsync(ClientRegistration registration)
    {
        Exception? failure = null;
        try
        {
            await registration.NotificationLane.CloseAndDrainAsync().ConfigureAwait(false);
            await DisposeRegistrationAsync(registration).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            lock (_gate)
            {
                registration.RetirementFailure = failure;
                if (failure is not null) RecordTerminalFailureLocked(failure);
                _activeRetirements.Remove(registration);
                registration.CompleteResourceRetirementLocked();
                if (!registration.RestartReserved)
                    CompleteRegistrationRetirementLocked(registration);
            }
        }
    }

    private void CompleteRegistrationRetirementLocked(ClientRegistration registration)
    {
        if (_clients.TryGetValue(registration.Configured.Id, out var current) &&
            ReferenceEquals(current, registration))
            _clients.Remove(registration.Configured.Id);
        registration.CompleteRetirementLocked();
    }

    private void AbandonRestartLocked(ClientRegistration registration)
    {
        registration.RestartReserved = false;
        if (registration.ResourceRetired.IsCompleted)
            CompleteRegistrationRetirementLocked(registration);
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
        var registration = new ClientRegistration(configured, client, RecordTerminalFailure);
        var runtimeGeneration = configured.PublicDescriptor().RuntimeGeneration;
        client.Invalidated += (_, revision) =>
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Invalidation,
                cancellationToken => _invalidated(
                    new BridgeClientInvalidation(configured.Id, revision), cancellationToken),
                requireInvalidationAuthority: true);
        client.ActionFailed += (_, failure) =>
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Failure,
                cancellationToken => _actionFailed(new BridgeClientActionFailure(
                    configured.Id, runtimeGeneration, failure), cancellationToken));
        client.Failed += (_, failure) =>
        {
            registration.RecordFailure(failure);
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Failure,
                cancellationToken => _failed(
                    new BridgeClientRuntimeFailure(configured.Id, failure), cancellationToken));
        };
        return registration;
    }

    private void EnqueueNotification(
        ClientRegistration registration,
        BridgeClientNotificationKind kind,
        Func<CancellationToken, Task> publish,
        bool requireInvalidationAuthority = false)
    {
        var startPump = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(registration) ||
                (requireInvalidationAuthority && !registration.MayPublishInvalidation))
                return;
            var admission = registration.NotificationLane.Enqueue(
                kind,
                publish,
                () => ReleasePublication(registration),
                out startPump);
            if (admission == BridgeClientNotificationAdmission.Accepted)
                registration.AdmitPublicationLocked();
        }
        if (startPump) registration.NotificationLane.StartPump();
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

    private void RecordTerminalFailure(Exception failure)
    {
        lock (_gate) RecordTerminalFailureLocked(failure);
    }

    private void RecordTerminalFailureLocked(Exception failure)
    {
        _firstTerminalFailure ??= failure;
        if (_terminalFailureCount < int.MaxValue) _terminalFailureCount++;
    }

    private void AdmitRestart()
    {
        lock (_gate)
        {
            DemandNotDisposed();
            if (_activeRestarts++ == 0)
                _restartsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ReleaseRestart()
    {
        lock (_gate)
        {
            if (_activeRestarts <= 0)
                throw new InvalidOperationException("Restart admission was released twice.");
            if (--_activeRestarts == 0) _restartsDrained!.TrySetResult();
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
            RecordTerminalFailure(new TimeoutException(
                $"Widget '{registration.Configured.Id}' client disposal exceeded its deadline."));
            if (disposal is not null) ObserveLateCompletion(disposal);
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

    private async Task DisposeUnpublishedAsync(ClientRegistration registration)
    {
        bool start;
        lock (_gate) start = ReserveRetirementLocked(registration, restartReserved: false);
        if (start) StartRetirement(registration);
        await registration.ResourceRetired.ConfigureAwait(false);
        if (registration.RetirementFailure is { } failure)
            throw new AggregateException("An unpublished bridge client failed disposal.", failure);
    }

    private void ObserveLateCompletion(Task task)
    {
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Exception is { } failure) RecordTerminalFailure(failure);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
        IBridgeWidgetClient client,
        Action<Exception> recordFailure)
    {
        private ConfiguredWidget _configured = configured;
        internal ConfiguredWidget Configured
        {
            get => Volatile.Read(ref _configured);
            set => Volatile.Write(ref _configured, value);
        }

        internal IBridgeWidgetClient Client { get; } = client;
        internal BridgeClientNotificationLane NotificationLane { get; } = new(recordFailure);
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
        private readonly TaskCompletionSource _resourceRetired = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal WorkerFailureDiagnostic? LastFailure => Volatile.Read(ref _lastFailure);
        internal bool IsRetiring { get; private set; }
        internal bool RetirementStarted { get; set; }
        internal bool RestartReserved { get; set; }
        internal Exception? RetirementFailure { get; set; }
        internal Task RetirementCompletion => _retirementCompletion.Task;
        internal Task ResourceRetired => _resourceRetired.Task;
        internal Task PublicationsDrained => _publicationsDrained.Task;
        internal int ActivePublications => _activePublications;
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
            new WorkerFailureDiagnostic(
                failure.DiagnosticCode ?? failure.Reason.ToString(),
                failure.CanRestart));

        internal void BeginRetirementLocked()
        {
            if (IsRetiring) return;
            IsRetiring = true;
            if (_activePublications == 0) _publicationsDrained.TrySetResult();
        }

        internal void CompleteRetirementLocked() =>
            _retirementCompletion.TrySetResult();

        internal void CompleteResourceRetirementLocked() =>
            _resourceRetired.TrySetResult();

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
                recordFailure(new TimeoutException(
                    $"Widget '{Configured.Id}' idle-unload drain exceeded its deadline."));
                _ = drain.ContinueWith(
                    completed =>
                    {
                        if (completed.Exception is { } failure) recordFailure(failure);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
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
            catch (Exception exception)
            {
                recordFailure(exception);
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
