namespace GameBarAlternative.WidgetBridge;

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

    private readonly int _maximumConcurrentRequests;
    private readonly Action<Exception> _fatalSession;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private readonly Dictionary<long, RequestEntry> _active = [];
    private readonly Dictionary<string, Task> _widgetTails =
        new(StringComparer.Ordinal);
    private Exception? _fatalException;
    private bool _accepting = true;
    private bool _disposed;

    internal BridgeRequestDispatcher(
        CancellationToken sessionCancellation,
        Action<Exception> fatalSession,
        int maximumConcurrentRequests = MaximumConcurrentRequests)
    {
        if (maximumConcurrentRequests is < 1 or > MaximumConcurrentRequests)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentRequests));
        _maximumConcurrentRequests = maximumConcurrentRequests;
        _fatalSession = fatalSession ?? throw new ArgumentNullException(nameof(fatalSession));
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellation);
    }

    internal int ActiveCount { get { lock (_gate) return _active.Count; } }
    internal int WidgetTailCount { get { lock (_gate) return _widgetTails.Count; } }
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
        string? widgetId,
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

            var predecessor = widgetId is not null &&
                _widgetTails.TryGetValue(widgetId, out var tail)
                ? tail
                : Task.CompletedTask;
            var entry = new RequestEntry(requestId, widgetId);
            var completion = RunAsync(entry, predecessor, handler);
            entry.Completion = completion;
            _active.Add(requestId, entry);
            if (widgetId is not null) _widgetTails[widgetId] = completion;
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
            pending = _active.Values.Select(entry => entry.Completion).ToArray();
        }
        _cancellation.Cancel();
        if (pending.Length != 0)
        {
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch (Exception) when (FatalException is not null)
            {
                // The first fatal completion is retained for the server to
                // rethrow after every admitted request has terminated.
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
        lock (_gate) _disposed = true;
        _cancellation.Dispose();
    }

    private async Task RunAsync(
        RequestEntry entry,
        Task predecessor,
        Func<CancellationToken, Task> handler)
    {
        Exception? fatal = null;
        try
        {
            // Manually completed handlers and synchronous admission phases do
            // not hold the sole pipe-read loop. The admitted count bounds work.
            await Task.Yield();
            await predecessor.ConfigureAwait(false);
            _cancellation.Token.ThrowIfCancellationRequested();
            await handler(_cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
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
            _active.Remove(entry.RequestId);
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

    private void ThrowIfDisposedLocked() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static BridgeProtocolException DuplicateRequestId() => new(
        "Bridge request IDs cannot be reused while a request is pending.");

    private sealed class RequestEntry(long requestId, string? widgetId)
    {
        internal long RequestId { get; } = requestId;
        internal string? WidgetId { get; } = widgetId;
        internal Task Completion { get; set; } = Task.CompletedTask;
    }
}
