namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetWorkerNotificationLane
{
    private const int MaximumQueuedActionFailures = 8;
    private const int MaximumQueuedActionTerminals =
        WidgetRail.WidgetSdk.Widget.ActionQueueCapacity + 1;
    private readonly object _gate = new();
    private readonly LinkedList<RuntimeEnvelope> _queue = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _pumpCancellation;
    private readonly Func<RuntimeEnvelope, CancellationToken, Task> _send;
    private readonly Task _pump;
    private LinkedListNode<RuntimeEnvelope>? _queuedInvalidation;
    private Exception? _terminalFailure;
    private long _latestInvalidationRevision = long.MinValue;
    private int _queuedActionFailures;
    private int _queuedActionTerminals;
    private bool _closed;

    internal WidgetWorkerNotificationLane(
        Func<RuntimeEnvelope, CancellationToken, Task> send,
        CancellationToken runCancellation)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _pumpCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(runCancellation);
        _pump = PumpAsync(_pumpCancellation.Token);
    }

    internal Task Completion => _pump;

    internal Admission EnqueueInvalidation(long revision)
    {
        var release = false;
        Admission admission;
        lock (_gate)
        {
            if (_closed)
                return Admission.RejectedClosed;
            if (revision <= _latestInvalidationRevision)
                return Admission.RejectedStale;

            _latestInvalidationRevision = revision;
            var envelope = new RuntimeEnvelope
            {
                Type = MessageTypes.Invalidated,
                Payload = RuntimeJson.ToElement(new InvalidationPayload(revision)),
            };
            if (_queuedInvalidation is not null)
            {
                _queue.Remove(_queuedInvalidation);
                _queuedInvalidation = _queue.AddLast(envelope);
                admission = Admission.Coalesced;
            }
            else
            {
                release = _queue.Count == 0;
                _queuedInvalidation = _queue.AddLast(envelope);
                admission = Admission.Enqueued;
            }
        }
        if (release) _available.Release();
        return admission;
    }

    internal Admission EnqueueActionFailure(ControllerActionFailurePayload failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var release = false;
        lock (_gate)
        {
            if (_closed)
                return Admission.RejectedClosed;
            if (_queuedActionFailures >= MaximumQueuedActionFailures)
            {
                _closed = true;
                _terminalFailure = new IOException(
                    "The bounded worker action-failure notification queue is full.");
                release = true;
            }
            else
            {
                release = _queue.Count == 0;
                _queue.AddLast(new RuntimeEnvelope
                {
                    Type = MessageTypes.ControllerActionFailed,
                    Payload = RuntimeJson.ToElement(failure),
                });
                _queuedActionFailures++;
                if (release) _available.Release();
                return Admission.Enqueued;
            }
        }
        if (release) _available.Release();
        return Admission.RejectedFull;
    }

    internal Admission EnqueueActionTerminal(ActionTerminalPayload terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var release = false;
        lock (_gate)
        {
            if (_closed) return Admission.RejectedClosed;
            if (_queuedActionTerminals >= MaximumQueuedActionTerminals)
            {
                _closed = true;
                _terminalFailure = new IOException(
                    "The bounded worker action-terminal notification queue is full.");
                release = true;
            }
            else
            {
                release = _queue.Count == 0;
                _queue.AddLast(new RuntimeEnvelope
                {
                    Type = MessageTypes.ActionTerminal,
                    Payload = RuntimeJson.ToElement(terminal),
                });
                _queuedActionTerminals++;
                if (release) _available.Release();
                return Admission.Enqueued;
            }
        }
        if (release) _available.Release();
        return Admission.RejectedFull;
    }

    internal void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
        }
        _available.Release();
    }

    internal async Task DrainAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Close();
        try
        {
            await _pump.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _pumpCancellation.Cancel();
            try
            {
                await _pump.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    "The worker notification lane did not stop after cancellation.",
                    exception);
            }
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
                while (TryTake(out var envelope))
                    await _send(envelope, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_terminalFailure is not null)
                        throw _terminalFailure;
                    if (_closed && _queue.Count == 0)
                        return;
                }
            }
        }
        catch
        {
            lock (_gate) _closed = true;
            throw;
        }
    }

    private bool TryTake(out RuntimeEnvelope envelope)
    {
        lock (_gate)
        {
            if (_terminalFailure is not null)
                throw _terminalFailure;
            var first = _queue.First;
            if (first is null)
            {
                envelope = null!;
                return false;
            }
            _queue.RemoveFirst();
            if (ReferenceEquals(first, _queuedInvalidation))
                _queuedInvalidation = null;
            else if (first.Value.Type == MessageTypes.ControllerActionFailed)
                _queuedActionFailures--;
            else if (first.Value.Type == MessageTypes.ActionTerminal)
                _queuedActionTerminals--;
            envelope = first.Value;
            return true;
        }
    }

    internal enum Admission
    {
        Enqueued,
        Coalesced,
        RejectedClosed,
        RejectedFull,
        RejectedStale,
    }
}
