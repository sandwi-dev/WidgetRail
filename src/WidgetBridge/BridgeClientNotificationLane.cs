namespace WidgetRail.WidgetBridge;

internal enum BridgeClientNotificationKind
{
    Invalidation,
    Failure,
}

internal enum BridgeClientNotificationAdmission
{
    Accepted,
    Coalesced,
    RejectedClosed,
    RejectedFull,
}

internal sealed class BridgeClientNotificationLane(
    Action<Exception> recordFailure)
{
    internal const int MaximumPendingFailures = 32;
    private readonly object _gate = new();
    private readonly Queue<Notification> _failures = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Notification? _invalidation;
    private long _sequence;
    private Task? _pump;
    private bool _pumpScheduled;
    private bool _closed;
    private int _droppedFailures;

    internal int PendingCount
    {
        get
        {
            lock (_gate) return _failures.Count + (_invalidation is null ? 0 : 1);
        }
    }

    internal int DroppedFailures
    {
        get { lock (_gate) return _droppedFailures; }
    }

    internal bool IsGateHeldByCurrentThread => Monitor.IsEntered(_gate);

    internal Task DrainAsync()
    {
        lock (_gate) return _pump ?? Task.CompletedTask;
    }

    internal BridgeClientNotificationAdmission Enqueue(
        BridgeClientNotificationKind kind,
        Func<CancellationToken, Task> publish,
        Action release,
        out bool startPump)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(release);
        lock (_gate)
        {
            startPump = false;
            if (_closed) return BridgeClientNotificationAdmission.RejectedClosed;
            var sequence = ++_sequence;
            if (kind == BridgeClientNotificationKind.Invalidation)
            {
                if (_invalidation is not null)
                {
                    _invalidation = _invalidation with
                    {
                        Sequence = sequence,
                        Publish = publish,
                    };
                    return BridgeClientNotificationAdmission.Coalesced;
                }
                _invalidation = new Notification(sequence, publish, release);
            }
            else
            {
                if (_failures.Count >= MaximumPendingFailures)
                {
                    if (_droppedFailures < int.MaxValue) _droppedFailures++;
                    return BridgeClientNotificationAdmission.RejectedFull;
                }
                _failures.Enqueue(new Notification(sequence, publish, release));
            }

            if (!_pumpScheduled)
            {
                _pumpScheduled = true;
                startPump = true;
            }
            return BridgeClientNotificationAdmission.Accepted;
        }
    }

    internal void StartPump()
    {
        lock (_gate)
        {
            if (_pump is not null || !_pumpScheduled) return;
            _pump = RunAsync();
        }
    }

    internal async Task CloseAndDrainAsync()
    {
        Notification[] dropped;
        Task? pump;
        var cancel = false;
        lock (_gate)
        {
            if (!_closed)
            {
                _closed = true;
                cancel = true;
            }
            dropped = DrainPendingLocked();
            pump = _pump;
            if (pump is null) _pumpScheduled = false;
        }
        if (cancel) _cancellation.Cancel();
        foreach (var item in dropped) item.Release();
        if (pump is not null) await pump.ConfigureAwait(false);
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        // StartPump is called after registry admission. This boundary prevents
        // external publication or registry release while the lane gate is held.
        await Task.Yield();
        while (true)
        {
            Notification? item;
            lock (_gate)
            {
                item = TakeNextLocked();
                if (item is null)
                {
                    _pumpScheduled = false;
                    _pump = null;
                    return;
                }
            }

            try
            {
                await item.Publish(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                recordFailure(exception);
            }
            finally
            {
                item.Release();
            }
        }
    }

    private Notification? TakeNextLocked()
    {
        if (_invalidation is null)
            return _failures.Count == 0 ? null : _failures.Dequeue();
        if (_failures.Count == 0)
        {
            var invalidation = _invalidation;
            _invalidation = null;
            return invalidation;
        }
        if (_invalidation.Sequence < _failures.Peek().Sequence)
        {
            var invalidation = _invalidation;
            _invalidation = null;
            return invalidation;
        }
        return _failures.Dequeue();
    }

    private Notification[] DrainPendingLocked()
    {
        var pending = new List<Notification>(_failures.Count + 1);
        if (_invalidation is not null)
        {
            pending.Add(_invalidation);
            _invalidation = null;
        }
        while (_failures.Count != 0) pending.Add(_failures.Dequeue());
        return pending.ToArray();
    }

    private sealed record Notification(
        long Sequence,
        Func<CancellationToken, Task> Publish,
        Action Release);
}
