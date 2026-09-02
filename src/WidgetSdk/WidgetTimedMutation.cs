namespace WidgetRail.WidgetSdk;

/// <summary>Observed failure from a lifecycle-owned timed state mutation.</summary>
public sealed class WidgetTimedMutationFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>
/// One quiet, replaceable, lifecycle-owned delayed state mutation. The slot
/// never publishes busy state or invalidates; its synchronous callback updates
/// the authoritative widget state and requests any needed invalidation.
/// </summary>
public sealed class WidgetTimedMutation : IDisposable
{
    public static TimeSpan MaximumDelay { get; } = TimeSpan.FromDays(1);

    private sealed class Invocation(
        long generation,
        CancellationToken lifetimeToken,
        CancellationTokenSource cancellation,
        Action callback)
    {
        internal long Generation { get; } = generation;
        internal CancellationToken LifetimeToken { get; } = lifetimeToken;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal Action Callback { get; } = callback;
        internal bool Superseded { get; set; }
        internal TaskCompletionSource<WidgetOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly WidgetOperationLifetime _lifetime;
    private readonly TimeProvider _timeProvider;
    private readonly Func<WidgetOperationLifetime, WidgetOperationLifetimeLease>
        _resolveLifetime;
    private readonly HashSet<Invocation> _running = [];
    private Invocation? _current;
    private TaskCompletionSource _idle = CompletedIdle();
    private long _nextGeneration;
    private bool _disposed;

    internal WidgetTimedMutation(
        WidgetOperationLifetime lifetime,
        TimeProvider timeProvider,
        Func<WidgetOperationLifetime, WidgetOperationLifetimeLease> resolveLifetime)
    {
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        _lifetime = lifetime;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _resolveLifetime = resolveLifetime;
    }

    public event EventHandler<WidgetTimedMutationFailedEventArgs>? Failed;

    public bool IsScheduled
    {
        get { lock (_gate) return _current is not null; }
    }

    public WidgetOperationHandle ScheduleLatest(
        TimeSpan delay,
        Action callback,
        CancellationToken ownerCancellationToken = default)
    {
        if (delay <= TimeSpan.Zero || delay > MaximumDelay)
            throw new ArgumentOutOfRangeException(nameof(delay));
        ArgumentNullException.ThrowIfNull(callback);
        var lease = _resolveLifetime(_lifetime);
        if (!lease.IsAvailable)
            return Rejected();

        Invocation? previous;
        Invocation invocation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var cancellation = ownerCancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    lease.Token, ownerCancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            if (lease.Token.IsCancellationRequested)
            {
                cancellation.Dispose();
                return Rejected();
            }
            invocation = new(
                Interlocked.Increment(ref _nextGeneration), lease.Token,
                cancellation, callback);
            previous = _current;
            if (previous is not null) previous.Superseded = true;
            _current = invocation;
            if (_running.Count == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _running.Add(invocation);
        }
        Cancel(previous);
        _ = ExecuteAsync(invocation, delay);
        return new(previous is null
                ? WidgetOperationAdmission.Started
                : WidgetOperationAdmission.Replaced,
            invocation.Completion.Task);
    }

    public bool Cancel()
    {
        Invocation? current;
        lock (_gate)
        {
            current = _current;
            _current = null;
        }
        Cancel(current);
        return current is not null;
    }

    public Task WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        Task idle;
        lock (_gate) idle = _idle.Task;
        return idle.WaitAsync(cancellationToken);
    }

    internal async ValueTask DrainLifetimeAsync(
        IReadOnlyList<CancellationToken> lifetimes,
        CancellationToken transitionToken)
    {
        Invocation[] invocations;
        lock (_gate)
        {
            invocations = _running.Where(invocation =>
                lifetimes.Contains(invocation.LifetimeToken)).ToArray();
            if (_current is not null && invocations.Contains(_current)) _current = null;
        }
        foreach (var invocation in invocations) Cancel(invocation);
        if (invocations.Length != 0)
            await Task.WhenAll(invocations.Select(value => value.Completion.Task))
                .WaitAsync(transitionToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Invocation[] invocations;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _current = null;
            invocations = _running.ToArray();
        }
        foreach (var invocation in invocations) Cancel(invocation);
    }

    private async Task ExecuteAsync(Invocation invocation, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, invocation.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (invocation.Cancellation.IsCancellationRequested)
        {
            Finish(invocation, new(invocation.Superseded
                ? WidgetOperationStatus.Superseded
                : WidgetOperationStatus.Canceled));
            return;
        }
        catch (Exception exception)
        {
            Finish(invocation, new(WidgetOperationStatus.Failed, exception));
            return;
        }

        var admitted = false;
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(_current, invocation) &&
                !invocation.Cancellation.IsCancellationRequested)
            {
                _current = null;
                admitted = true;
            }
        }
        if (!admitted)
        {
            Finish(invocation, new(invocation.Superseded
                ? WidgetOperationStatus.Superseded
                : WidgetOperationStatus.Canceled));
            return;
        }

        try
        {
            invocation.Callback();
            Finish(invocation, new(WidgetOperationStatus.Succeeded));
        }
        catch (Exception exception)
        {
            Finish(invocation, new(WidgetOperationStatus.Failed, exception));
        }
    }

    private void Finish(Invocation invocation, WidgetOperationResult result)
    {
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            if (ReferenceEquals(_current, invocation)) _current = null;
            _running.Remove(invocation);
            if (_running.Count == 0) idle = _idle;
        }
        invocation.Cancellation.Dispose();
        invocation.Completion.TrySetResult(result);
        if (result.Status == WidgetOperationStatus.Failed && result.Exception is { } exception)
            PublishFailure(exception);
        idle?.TrySetResult();
    }

    private void PublishFailure(Exception exception)
    {
        if (Failed?.GetInvocationList() is not { } handlers) return;
        foreach (var handler in handlers)
        {
            try { ((EventHandler<WidgetTimedMutationFailedEventArgs>)handler)(
                    this, new(exception)); }
            catch { }
        }
    }

    private static void Cancel(Invocation? invocation)
    {
        if (invocation is null) return;
        try { invocation.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static WidgetOperationHandle Rejected() => new(
        WidgetOperationAdmission.RejectedInactive,
        Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Rejected)));

    private static TaskCompletionSource CompletedIdle()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        idle.SetResult();
        return idle;
    }
}

public abstract partial class Widget
{
    private readonly object _timedMutationsGate = new();
    private readonly List<WidgetTimedMutation> _timedMutations = [];

    protected WidgetTimedMutation CreateTimedMutation(
        WidgetOperationLifetime lifetime = WidgetOperationLifetime.Active,
        TimeProvider? timeProvider = null)
    {
        var mutation = new WidgetTimedMutation(
            lifetime, timeProvider ?? TimeProvider.System, ResolveOperationLifetime);
        lock (_timedMutationsGate) _timedMutations.Add(mutation);
        return mutation;
    }

    private async ValueTask DrainTimedMutationsAsync(
        IReadOnlyList<CancellationToken> lifetimes,
        CancellationToken transitionToken)
    {
        WidgetTimedMutation[] mutations;
        lock (_timedMutationsGate) mutations = _timedMutations.ToArray();
        foreach (var mutation in mutations)
            await mutation.DrainLifetimeAsync(lifetimes, transitionToken)
                .ConfigureAwait(false);
    }

    private void DisposeTimedMutations()
    {
        WidgetTimedMutation[] mutations;
        lock (_timedMutationsGate)
        {
            mutations = _timedMutations.ToArray();
            _timedMutations.Clear();
        }
        foreach (var mutation in mutations) mutation.Dispose();
    }
}
