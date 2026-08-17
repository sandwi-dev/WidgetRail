namespace WidgetRail.WidgetBridge;

internal enum BridgeRequestDispatchStatus
{
    Accepted,
    CapacityExceeded,
}

internal readonly record struct BridgeRequestDispatch(
    BridgeRequestDispatchStatus Status,
    Task? Completion)
{
    internal static BridgeRequestDispatch CapacityExceeded { get; } =
        new(BridgeRequestDispatchStatus.CapacityExceeded, null);
}

/// <summary>
/// Owns request admission and scheduling after a frame has been strictly
/// decoded. It has no channel, catalog, client, Stop, or response-write
/// authority; the server supplies one already-decoded handler per request.
/// </summary>
internal sealed class BridgeRequestDispatcher : IAsyncDisposable
{
    internal const int MaximumConcurrentRequests = 16;
    internal static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly int _maximumConcurrentRequests;
    private readonly Action<Exception> _fatalSession;
    private readonly Func<CancellationToken, Task> _drainDeadline;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private readonly Dictionary<long, RequestEntry> _active = [];
    private readonly Dictionary<string, Task> _widgetTails =
        new(StringComparer.Ordinal);
    private readonly HashSet<Task> _quarantined = [];
    private TaskCompletionSource? _quarantineDrained;
    private Exception? _fatalException;
    private bool _accepting = true;
    private bool _draining;
    private bool _disposed;
    private bool _cancellationDisposed;

    internal BridgeRequestDispatcher(
        CancellationToken sessionCancellation,
        Action<Exception> fatalSession,
        int maximumConcurrentRequests = MaximumConcurrentRequests,
        Func<CancellationToken, Task>? drainDeadline = null)
    {
        if (maximumConcurrentRequests is < 1 or > MaximumConcurrentRequests)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentRequests));
        _maximumConcurrentRequests = maximumConcurrentRequests;
        _fatalSession = fatalSession ?? throw new ArgumentNullException(nameof(fatalSession));
        _drainDeadline = drainDeadline ??
            (token => Task.Delay(DrainTimeout, token));
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellation);
    }

    internal int ActiveCount { get { lock (_gate) return _active.Count; } }
    internal int WidgetTailCount { get { lock (_gate) return _widgetTails.Count; } }
    internal int QuarantinedCount { get { lock (_gate) return _quarantined.Count; } }
    internal Task QuarantineDrained
    {
        get { lock (_gate) return _quarantineDrained?.Task ?? Task.CompletedTask; }
    }
    internal int AvailableSlots
    {
        get { lock (_gate) return _maximumConcurrentRequests - _active.Count; }
    }
    internal Exception? FatalException
    {
        get { lock (_gate) return _fatalException; }
    }

    internal void DemandRequestIdAvailable(long requestId)
    {
        if (requestId == 0)
            throw new BridgeProtocolException(
                "Bridge requests require a non-zero request ID.");
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            if (_active.ContainsKey(requestId))
                throw DuplicateRequestId();
        }
    }

    internal BridgeRequestDispatch TryDispatch(
        long requestId,
        BridgeRequestKey requestKey,
        Func<CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (requestId == 0)
            throw new BridgeProtocolException(
                "Bridge requests require a non-zero request ID.");

        lock (_gate)
        {
            ThrowIfDisposedLocked();
            if (!_accepting)
                throw new InvalidOperationException(
                    "Bridge request dispatcher is draining.");
            if (_active.ContainsKey(requestId))
                throw DuplicateRequestId();
            if (_active.Count >= _maximumConcurrentRequests)
                return BridgeRequestDispatch.CapacityExceeded;

            var predecessor = requestKey.WidgetId is not null &&
                _widgetTails.TryGetValue(requestKey.WidgetId, out var tail)
                ? tail
                : Task.CompletedTask;
            var entry = new RequestEntry(requestId, requestKey);
            var completion = RunAsync(entry, predecessor, handler);
            entry.Completion = completion;
            _active.Add(requestId, entry);
            if (requestKey.WidgetId is not null)
                _widgetTails[requestKey.WidgetId] = completion;
            return new BridgeRequestDispatch(
                BridgeRequestDispatchStatus.Accepted, completion);
        }
    }

    internal async Task CancelAndDrainAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            _accepting = false;
            _draining = true;
            pending = _active.Values.Select(entry => entry.Completion).ToArray();
        }
        _cancellation.Cancel();
        if (pending.Length != 0)
        {
            var drain = Task.WhenAll(pending);
            using var deadlineCancellation = new CancellationTokenSource();
            var deadline = _drainDeadline(deadlineCancellation.Token);
            var completed = await Task.WhenAny(drain, deadline).ConfigureAwait(false);
            if (ReferenceEquals(completed, drain))
            {
                deadlineCancellation.Cancel();
                await AwaitDrainAsync(drain).ConfigureAwait(false);
            }
            else
            {
                await deadline.ConfigureAwait(false);
                _ = ObserveAggregateAsync(drain);
                QuarantineRemaining();
            }
        }
        lock (_gate)
        {
            if (_active.Count != 0 || _widgetTails.Count != 0)
                throw new InvalidOperationException(
                    "Bridge request dispatcher did not drain completely.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        await CancelAndDrainAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _disposed = true;
            if (_quarantined.Count == 0) DisposeCancellationLocked();
        }
    }

    private async Task RunAsync(
        RequestEntry entry,
        Task predecessor,
        Func<CancellationToken, Task> handler)
    {
        Exception? fatal = null;
        var handlerStarted = false;
        try
        {
            // Manually completed handlers and synchronous admission phases do
            // not hold the sole pipe-read loop. The admitted count bounds work.
            await Task.Yield();
            await predecessor.ConfigureAwait(false);
            _cancellation.Token.ThrowIfCancellationRequested();
            handlerStarted = true;
            await handler(_cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            IsDraining && handlerStarted && exception is not OutOfMemoryException)
        {
            // Once session drain owns cancellation, a handler's late failure
            // belongs to shutdown observation, not session-fatal publication.
        }
        catch (Exception exception)
        {
            fatal = exception;
            throw;
        }
        finally
        {
            Complete(entry, fatal);
        }
    }

    private void Complete(RequestEntry entry, Exception? fatal)
    {
        Exception? publishFatal = null;
        lock (_gate)
        {
            // A deadline-detached entry is observed by the quarantine path.
            // It no longer owns request IDs, FIFO tails, or session-fatal
            // publication into a closed or replacement bridge session.
            if (!_active.Remove(entry.RequestId)) return;
            if (entry.WidgetId is not null &&
                _widgetTails.TryGetValue(entry.WidgetId, out var tail) &&
                ReferenceEquals(tail, entry.Completion))
                _widgetTails.Remove(entry.WidgetId);
            if (fatal is not null && _fatalException is null)
            {
                _fatalException = fatal;
                publishFatal = fatal;
            }
        }
        if (publishFatal is not null)
        {
            _cancellation.Cancel();
            _fatalSession(publishFatal);
        }
    }

    private async Task AwaitDrainAsync(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (Exception) when (FatalException is not null)
        {
            // The first fatal completion is retained for the server to
            // rethrow after every request that ended before the deadline.
        }
    }

    private void QuarantineRemaining()
    {
        Task[] late;
        lock (_gate)
        {
            late = _active.Values.Select(entry => entry.Completion).ToArray();
            _active.Clear();
            _widgetTails.Clear();
            foreach (var completion in late) _quarantined.Add(completion);
            if (late.Length != 0)
                _quarantineDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }
        foreach (var completion in late) _ = ObserveQuarantinedAsync(completion);
    }

    private async Task ObserveQuarantinedAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception)
        {
            // The session is closed and the completion is deliberately
            // quarantined. Observe its failure without publishing it.
        }
        finally
        {
            TaskCompletionSource? drained = null;
            lock (_gate)
            {
                _quarantined.Remove(completion);
                if (_quarantined.Count == 0)
                {
                    drained = _quarantineDrained;
                    _quarantineDrained = null;
                }
                if (_disposed && _quarantined.Count == 0)
                    DisposeCancellationLocked();
            }
            drained?.TrySetResult();
        }
    }

    private static async Task ObserveAggregateAsync(Task drain)
    {
        try { await drain.ConfigureAwait(false); }
        catch (Exception)
        {
            // Individual quarantined completions retain cleanup ownership;
            // this observer consumes Task.WhenAll's aggregate fault.
        }
    }

    private void DisposeCancellationLocked()
    {
        if (_cancellationDisposed) return;
        _cancellationDisposed = true;
        _cancellation.Dispose();
    }

    private void ThrowIfDisposedLocked() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private bool IsDraining
    {
        get { lock (_gate) return _draining; }
    }

    private static BridgeProtocolException DuplicateRequestId() => new(
        "Bridge request IDs cannot be reused while a request is pending.");

    private sealed class RequestEntry(long requestId, BridgeRequestKey requestKey)
    {
        internal long RequestId { get; } = requestId;
        internal string? WidgetId { get; } = requestKey.WidgetId;
        internal Task Completion { get; set; } = Task.CompletedTask;
    }
}
