namespace GameBarAlternative.WidgetSdk;

/// <summary>Runtime lifetime that owns an admitted widget operation.</summary>
public enum WidgetOperationLifetime
{
    /// <summary>Visible and Interactive; canceled when the widget returns to Background.</summary>
    Active,
    /// <summary>The exact current Background, Visible, or Interactive state.</summary>
    State,
    /// <summary>The complete widget lifetime, including intentional Background work.</summary>
    Widget,
}

public enum WidgetOperationAdmission
{
    /// <summary>The operation started immediately.</summary>
    Started,
    /// <summary>A single-flight request joined the already-running operation.</summary>
    Joined,
    /// <summary>The newest request superseded older latest-wins work.</summary>
    Replaced,
    /// <summary>The operation entered a bounded serial queue.</summary>
    Enqueued,
    /// <summary>The selected lifecycle is not currently available.</summary>
    RejectedInactive,
    /// <summary>The per-key or per-widget operation bound was reached.</summary>
    RejectedCapacity,
}

/// <summary>Terminal outcome returned by an observed operation completion.</summary>
public enum WidgetOperationStatus
{
    Succeeded,
    Canceled,
    Superseded,
    Failed,
    Rejected,
}

public readonly record struct WidgetOperationResult(
    WidgetOperationStatus Status,
    Exception? Exception = null);

public readonly record struct WidgetOperationHandle(
    WidgetOperationAdmission Admission,
    Task<WidgetOperationResult> Completion)
{
    /// <summary>Whether the runtime accepted or joined this request.</summary>
    public bool IsAccepted => Admission is not (
        WidgetOperationAdmission.RejectedInactive or
        WidgetOperationAdmission.RejectedCapacity);
}

public sealed class WidgetOperationBusyChangedEventArgs(string key, bool isBusy) : EventArgs
{
    /// <summary>The widget-local operation key.</summary>
    public string Key { get; } = key;
    /// <summary>Whether the key has running or queued work.</summary>
    public bool IsBusy { get; } = isBusy;
}

public sealed class WidgetOperationFailedEventArgs(
    string key,
    WidgetOperationLifetime lifetime,
    Exception exception) : EventArgs
{
    /// <summary>The widget-local operation key.</summary>
    public string Key { get; } = key;
    /// <summary>The lifecycle which owned the failed operation.</summary>
    public WidgetOperationLifetime Lifetime { get; } = lifetime;
    /// <summary>The exception observed by the runtime.</summary>
    public Exception Exception { get; } = exception;
}

/// <summary>
/// Context for one admitted operation. Latest-work contexts become non-current
/// synchronously when a newer replacement is admitted.
/// </summary>
public sealed class WidgetOperationContext
{
    private readonly Func<bool> _isCurrent;

    internal WidgetOperationContext(
        CancellationToken cancellationToken,
        long generation,
        Func<bool> isCurrent)
    {
        CancellationToken = cancellationToken;
        Generation = generation;
        _isCurrent = isCurrent;
    }

    /// <summary>Runtime-owned cancellation for the selected lifecycle and policy.</summary>
    public CancellationToken CancellationToken { get; }
    /// <summary>Monotonic widget-local generation assigned at admission.</summary>
    public long Generation { get; }
    /// <summary>False after cancellation or a newer latest-wins request is admitted.</summary>
    public bool IsCurrent => _isCurrent();
}

internal readonly record struct WidgetOperationLifetimeLease(
    bool IsAvailable,
    CancellationToken Token);

/// <summary>
/// Bounded runtime-owned async coordination for widget authors. Completion
/// tasks never fault: failures are returned as results and raised once through
/// <see cref="OperationFailed"/>.
/// </summary>
public sealed class WidgetOperations
{
    public const int MaximumTrackedKeys = 32;
    public const int MaximumTrackedOperations = 64;
    public const int MaximumSerialPendingPerKey = 16;

    private enum Policy
    {
        SingleFlight,
        Latest,
        Serial,
    }

    private sealed class Invocation
    {
        internal required string Key { get; init; }
        internal required WidgetOperationLifetime Lifetime { get; init; }
        internal required CancellationToken LifetimeToken { get; init; }
        internal required CancellationTokenSource Cancellation { get; init; }
        internal required Func<WidgetOperationContext, ValueTask> Operation { get; init; }
        internal required long Generation { get; init; }
        internal TaskCompletionSource<WidgetOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Superseded { get; set; }
    }

    private sealed class Lane(
        Policy policy,
        WidgetOperationLifetime lifetime,
        CancellationToken lifetimeToken)
    {
        internal Policy Policy { get; } = policy;
        internal WidgetOperationLifetime Lifetime { get; } = lifetime;
        internal CancellationToken LifetimeToken { get; } = lifetimeToken;
        internal Invocation? Active { get; set; }
        internal Queue<Invocation> Pending { get; } = [];
        internal long CurrentGeneration { get; set; }
        internal TaskCompletionSource Idle { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Lane> _lanes = new(StringComparer.Ordinal);
    private readonly Func<WidgetOperationLifetime, WidgetOperationLifetimeLease> _resolveLifetime;
    private readonly Action _invalidate;
    private int _trackedOperations;
    private long _nextGeneration;

    internal WidgetOperations(
        Func<WidgetOperationLifetime, WidgetOperationLifetimeLease> resolveLifetime,
        Action invalidate)
    {
        _resolveLifetime = resolveLifetime;
        _invalidate = invalidate;
    }

    public event EventHandler<WidgetOperationBusyChangedEventArgs>? BusyChanged;
    public event EventHandler<WidgetOperationFailedEventArgs>? OperationFailed;

    /// <summary>Returns whether a key currently has running or queued work.</summary>
    public bool IsBusy(string key)
    {
        ValidateKey(key);
        lock (_gate) return _lanes.ContainsKey(key);
    }

    /// <summary>
    /// Starts at most one operation for a key. Duplicate requests join the
    /// incumbent and receive its exact completion task.
    /// </summary>
    public WidgetOperationHandle RunSingleFlight(
        string key,
        Func<WidgetOperationContext, ValueTask> operation,
        WidgetOperationLifetime lifetime = WidgetOperationLifetime.Active)
    {
        ValidateRequest(key, operation, lifetime);
        var lease = _resolveLifetime(lifetime);
        if (!lease.IsAvailable)
            return Rejected(WidgetOperationAdmission.RejectedInactive);

        Invocation? start = null;
        Task<WidgetOperationResult>? joined = null;
        var becameBusy = false;
        lock (_gate)
        {
            if (_lanes.TryGetValue(key, out var existing))
            {
                DemandCompatible(existing, Policy.SingleFlight, lifetime);
                if (existing.LifetimeToken != lease.Token)
                    return Rejected(WidgetOperationAdmission.RejectedInactive);
                joined = existing.Active!.Completion.Task;
            }
            else if (!TryCreateLane(
                         key, Policy.SingleFlight, lifetime, lease.Token, out var lane))
            {
                return Rejected(WidgetOperationAdmission.RejectedCapacity);
            }
            else
            {
                start = CreateInvocation(key, lifetime, lease.Token, operation);
                lane.Active = start;
                lane.CurrentGeneration = start.Generation;
                _trackedOperations++;
                becameBusy = true;
            }
        }
        if (joined is not null)
            return new(WidgetOperationAdmission.Joined, joined);
        if (becameBusy) PublishBusy(key, true);
        Start(start!);
        return new(WidgetOperationAdmission.Started, start!.Completion.Task);
    }

    /// <summary>
    /// Makes older work stale immediately, cancels the running operation, and
    /// retains only the newest pending replacement without same-key overlap.
    /// </summary>
    public WidgetOperationHandle RunLatest(
        string key,
        Func<WidgetOperationContext, ValueTask> operation,
        WidgetOperationLifetime lifetime = WidgetOperationLifetime.Active)
    {
        ValidateRequest(key, operation, lifetime);
        var lease = _resolveLifetime(lifetime);
        if (!lease.IsAvailable)
            return Rejected(WidgetOperationAdmission.RejectedInactive);

        Invocation? start = null;
        Invocation? cancelActive = null;
        Invocation? supersededPending = null;
        Invocation admitted;
        var admission = WidgetOperationAdmission.Started;
        var becameBusy = false;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(key, out var lane))
            {
                if (!TryCreateLane(key, Policy.Latest, lifetime, lease.Token, out lane))
                    return Rejected(WidgetOperationAdmission.RejectedCapacity);
                admitted = CreateInvocation(key, lifetime, lease.Token, operation);
                lane.Active = admitted;
                lane.CurrentGeneration = admitted.Generation;
                _trackedOperations++;
                start = admitted;
                becameBusy = true;
            }
            else
            {
                DemandCompatible(lane, Policy.Latest, lifetime);
                if (lane.LifetimeToken != lease.Token)
                    return Rejected(WidgetOperationAdmission.RejectedInactive);
                var replacingPending = lane.Pending.Count != 0;
                if (!replacingPending && _trackedOperations >= MaximumTrackedOperations)
                    return Rejected(WidgetOperationAdmission.RejectedCapacity);

                admitted = CreateInvocation(key, lifetime, lease.Token, operation);
                if (replacingPending)
                {
                    supersededPending = lane.Pending.Dequeue();
                    supersededPending.Superseded = true;
                    _trackedOperations--;
                }
                lane.Pending.Enqueue(admitted);
                _trackedOperations++;
                lane.CurrentGeneration = admitted.Generation;
                if (lane.Active is { } active)
                {
                    active.Superseded = true;
                    cancelActive = active;
                }
                admission = WidgetOperationAdmission.Replaced;
            }
        }

        if (becameBusy) PublishBusy(key, true);
        CompleteWithoutRunning(supersededPending, WidgetOperationStatus.Superseded);
        Cancel(cancelActive);
        if (start is not null) Start(start);
        return new(admission, admitted.Completion.Task);
    }

    /// <summary>Queues bounded FIFO work with at most one delegate running per key.</summary>
    public WidgetOperationHandle RunSerial(
        string key,
        Func<WidgetOperationContext, ValueTask> operation,
        WidgetOperationLifetime lifetime = WidgetOperationLifetime.Active)
    {
        ValidateRequest(key, operation, lifetime);
        var lease = _resolveLifetime(lifetime);
        if (!lease.IsAvailable)
            return Rejected(WidgetOperationAdmission.RejectedInactive);

        Invocation? start = null;
        Invocation admitted;
        var admission = WidgetOperationAdmission.Started;
        var becameBusy = false;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(key, out var lane))
            {
                if (!TryCreateLane(key, Policy.Serial, lifetime, lease.Token, out lane))
                    return Rejected(WidgetOperationAdmission.RejectedCapacity);
                admitted = CreateInvocation(key, lifetime, lease.Token, operation);
                lane.Active = admitted;
                lane.CurrentGeneration = admitted.Generation;
                _trackedOperations++;
                start = admitted;
                becameBusy = true;
            }
            else
            {
                DemandCompatible(lane, Policy.Serial, lifetime);
                if (lane.LifetimeToken != lease.Token)
                    return Rejected(WidgetOperationAdmission.RejectedInactive);
                if (lane.Pending.Count >= MaximumSerialPendingPerKey ||
                    _trackedOperations >= MaximumTrackedOperations)
                    return Rejected(WidgetOperationAdmission.RejectedCapacity);
                admitted = CreateInvocation(key, lifetime, lease.Token, operation);
                lane.Pending.Enqueue(admitted);
                _trackedOperations++;
                admission = WidgetOperationAdmission.Enqueued;
            }
        }

        if (becameBusy) PublishBusy(key, true);
        if (start is not null) Start(start);
        return new(admission, admitted.Completion.Task);
    }

    /// <summary>Cancels all running and queued work for a key.</summary>
    public bool Cancel(string key)
    {
        ValidateKey(key);
        Invocation[] invocations;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(key, out var lane)) return false;
            invocations = EnumerateLane(lane).ToArray();
        }
        foreach (var invocation in invocations) Cancel(invocation);
        return true;
    }

    /// <summary>Waits until one key has no running or queued work.</summary>
    public Task WhenIdleAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        Task idle;
        lock (_gate)
            idle = _lanes.TryGetValue(key, out var lane)
                ? lane.Idle.Task
                : Task.CompletedTask;
        return idle.WaitAsync(cancellationToken);
    }

    /// <summary>Waits for the operations present at the time of this call to drain.</summary>
    public Task WhenAllIdleAsync(CancellationToken cancellationToken = default)
    {
        Task[] idle;
        lock (_gate) idle = _lanes.Values.Select(lane => lane.Idle.Task).ToArray();
        return Task.WhenAll(idle).WaitAsync(cancellationToken);
    }

    internal async ValueTask DrainLifetimesAsync(
        IReadOnlyList<CancellationToken> lifetimes,
        CancellationToken transitionToken)
    {
        if (lifetimes.Count == 0) return;
        Invocation[] invocations;
        lock (_gate)
        {
            invocations = _lanes.Values
                .SelectMany(EnumerateLane)
                .Where(invocation => lifetimes.Contains(invocation.LifetimeToken))
                .Distinct()
                .ToArray();
        }
        foreach (var invocation in invocations) Cancel(invocation);
        if (invocations.Length != 0)
            await Task.WhenAll(invocations.Select(invocation => invocation.Completion.Task))
                .WaitAsync(transitionToken).ConfigureAwait(false);
    }

    private bool TryCreateLane(
        string key,
        Policy policy,
        WidgetOperationLifetime lifetime,
        CancellationToken lifetimeToken,
        out Lane lane)
    {
        if (_lanes.Count >= MaximumTrackedKeys ||
            _trackedOperations >= MaximumTrackedOperations)
        {
            lane = null!;
            return false;
        }
        lane = new(policy, lifetime, lifetimeToken);
        _lanes.Add(key, lane);
        return true;
    }

    private Invocation CreateInvocation(
        string key,
        WidgetOperationLifetime lifetime,
        CancellationToken lifetimeToken,
        Func<WidgetOperationContext, ValueTask> operation) => new()
        {
            Key = key,
            Lifetime = lifetime,
            LifetimeToken = lifetimeToken,
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken),
            Operation = operation,
            Generation = Interlocked.Increment(ref _nextGeneration),
        };

    private void Start(Invocation invocation) => _ = ExecuteAsync(invocation);

    private async Task ExecuteAsync(Invocation invocation)
    {
        WidgetOperationResult result;
        if (invocation.Cancellation.IsCancellationRequested)
        {
            result = new(invocation.Superseded
                ? WidgetOperationStatus.Superseded
                : WidgetOperationStatus.Canceled);
        }
        else
        {
            var context = new WidgetOperationContext(
                invocation.Cancellation.Token,
                invocation.Generation,
                () => IsCurrent(invocation));
            try
            {
                await invocation.Operation(context).ConfigureAwait(false);
                result = new(invocation.Superseded
                    ? WidgetOperationStatus.Superseded
                    : invocation.Cancellation.IsCancellationRequested
                        ? WidgetOperationStatus.Canceled
                        : WidgetOperationStatus.Succeeded);
            }
            catch (OperationCanceledException) when (
                invocation.Cancellation.IsCancellationRequested)
            {
                result = new(invocation.Superseded
                    ? WidgetOperationStatus.Superseded
                    : WidgetOperationStatus.Canceled);
            }
            catch (Exception exception)
            {
                result = new(WidgetOperationStatus.Failed, exception);
            }
        }
        Finish(invocation, result);
    }

    private bool IsCurrent(Invocation invocation)
    {
        lock (_gate)
            return !invocation.Superseded &&
                !invocation.Cancellation.IsCancellationRequested &&
                _lanes.TryGetValue(invocation.Key, out var lane) &&
                lane.CurrentGeneration == invocation.Generation;
    }

    private void Finish(Invocation invocation, WidgetOperationResult result)
    {
        Invocation? next = null;
        var becameIdle = false;
        Lane? lane;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(invocation.Key, out lane) ||
                !ReferenceEquals(lane.Active, invocation))
                throw new InvalidOperationException("Widget operation lane state was corrupted.");
            lane.Active = null;
            _trackedOperations--;
            if (lane.Pending.Count != 0)
            {
                next = lane.Pending.Dequeue();
                lane.Active = next;
                if (lane.Policy is not Policy.Latest)
                    lane.CurrentGeneration = next.Generation;
            }
            else
            {
                _lanes.Remove(invocation.Key);
                becameIdle = true;
            }
        }

        invocation.Cancellation.Dispose();
        invocation.Completion.TrySetResult(result);
        if (result.Status == WidgetOperationStatus.Failed && result.Exception is { } exception)
            PublishFailure(invocation, exception);
        if (becameIdle)
        {
            lane!.Idle.TrySetResult();
            PublishBusy(invocation.Key, false);
        }
        if (next is not null) Start(next);
    }

    private void CompleteWithoutRunning(
        Invocation? invocation,
        WidgetOperationStatus status)
    {
        if (invocation is null) return;
        invocation.Cancellation.Cancel();
        invocation.Cancellation.Dispose();
        invocation.Completion.TrySetResult(new(status));
    }

    private static void Cancel(Invocation? invocation)
    {
        if (invocation is null) return;
        try { invocation.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void PublishBusy(string key, bool isBusy)
    {
        _invalidate();
        Publish(BusyChanged, new WidgetOperationBusyChangedEventArgs(key, isBusy));
    }

    private void PublishFailure(Invocation invocation, Exception exception) =>
        Publish(OperationFailed,
            new WidgetOperationFailedEventArgs(invocation.Key, invocation.Lifetime, exception));

    private void Publish<T>(EventHandler<T>? handlers, T args) where T : EventArgs
    {
        if (handlers?.GetInvocationList() is not { } subscriptions) return;
        foreach (var subscription in subscriptions)
        {
            try { ((EventHandler<T>)subscription)(this, args); }
            catch { }
        }
    }

    private static WidgetOperationHandle Rejected(WidgetOperationAdmission admission) =>
        new(admission, Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Rejected)));

    private static IEnumerable<Invocation> EnumerateLane(Lane lane)
    {
        if (lane.Active is not null) yield return lane.Active;
        foreach (var invocation in lane.Pending) yield return invocation;
    }

    private static void DemandCompatible(
        Lane lane,
        Policy policy,
        WidgetOperationLifetime lifetime)
    {
        if (lane.Policy != policy)
            throw new InvalidOperationException(
                "An operation key cannot mix coordination policies while it is busy.");
        if (lane.Lifetime != lifetime)
            throw new InvalidOperationException(
                "An operation key cannot mix runtime lifetime kinds while it is busy.");
    }

    private static void ValidateRequest(
        string key,
        Func<WidgetOperationContext, ValueTask> operation,
        WidgetOperationLifetime lifetime)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
    }

    private static void ValidateKey(string key) => StableIdentifier.Validate(key, nameof(key));
}

public abstract partial class Widget
{
    private readonly WidgetOperations _operations;

    protected Widget() => _operations = new(ResolveOperationLifetime, () =>
    {
        if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
    });

    /// <summary>
    /// Runtime-owned bounded async coordination. Prefer this to widget-owned
    /// task fields, cancellation sources, and semaphores for user-triggered work.
    /// </summary>
    protected WidgetOperations Operations => _operations;

    private WidgetOperationLifetimeLease ResolveOperationLifetime(
        WidgetOperationLifetime lifetime)
    {
        if (Volatile.Read(ref _created) == 0 ||
            LifecycleState == WidgetLifecycleState.Destroying)
            return new(false, default);
        if (lifetime == WidgetOperationLifetime.State &&
            LifecycleState == WidgetLifecycleState.Created)
            return new(false, default);
        var token = lifetime switch
        {
            WidgetOperationLifetime.Active => ActiveCancellationToken,
            WidgetOperationLifetime.State => StateLifetimeToken,
            WidgetOperationLifetime.Widget => WidgetLifetimeToken,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime)),
        };
        return new(!token.IsCancellationRequested, token);
    }
}
