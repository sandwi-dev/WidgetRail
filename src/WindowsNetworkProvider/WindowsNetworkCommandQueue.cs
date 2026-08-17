using System.Collections.Concurrent;

namespace WidgetRail.WindowsNetworkProvider;

/// <summary>
/// Bounded admission and terminal draining for the provider's single owner-thread queue.
/// Deadline overflow retains only the latest generation per operation and promotes it at
/// a new FIFO tail position; it never replaces an older queued command in place.
/// </summary>
internal sealed class WindowsNetworkCommandQueue : IDisposable
{
    private const int AdmissionClosedMask = 1 << 30;
    private const int AdmissionCountMask = AdmissionClosedMask - 1;
    private const int MaximumQueuedCommands = 128;
    private const int ReservedDeadlineCommands = 4;
    internal const int MaximumOrdinaryQueuedCommands =
        MaximumQueuedCommands - ReservedDeadlineCommands;

    private readonly BlockingCollection<NetworkCommand> _commands = new(MaximumQueuedCommands);
    private readonly INetworkCommandAdmissionObserver? _observer;
    private readonly TaskCompletionSource _admissionsDrained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DeadlineOverflow? _connectionOverflow;
    private DeadlineOverflow? _scanOverflow;
    private long _nextOverflowSequence;
    private int _ordinaryCommandsQueued;
    private int _deadlineCommandsQueued;
    private int _admissionState;

    public WindowsNetworkCommandQueue(INetworkCommandAdmissionObserver? observer) =>
        _observer = observer;

    public int OrdinaryCommandsQueued => Volatile.Read(ref _ordinaryCommandsQueued);
    public int DeadlineCommandsQueued => Volatile.Read(ref _deadlineCommandsQueued);
    public int PendingDeadlineOverflowCount =>
        (Volatile.Read(ref _connectionOverflow) is null ? 0 : 1) +
        (Volatile.Read(ref _scanOverflow) is null ? 0 : 1);
    public bool IsClosed =>
        (Volatile.Read(ref _admissionState) & AdmissionClosedMask) != 0;

    public IReadOnlyList<string> QueuedCommandKinds
    {
        get
        {
            try
            {
                return _commands.ToArray()
                    .Select(static command => command switch
                    {
                        ConnectionTimeoutCommand timeout => $"connection:{timeout.Generation}",
                        WifiScanTimeoutCommand timeout => $"scan:{timeout.Generation}",
                        SetWifiRadioCommand => "radio",
                        _ => command.GetType().Name,
                    })
                    .ToArray();
            }
            catch (ObjectDisposedException)
            {
                return [];
            }
        }
    }

    public bool TryEnqueueOrdinary(NetworkCommand command)
    {
        if (!BeginAdmission()) throw new InvalidOperationException("The command queue is closed.");
        var reserved = false;
        try
        {
            while (true)
            {
                var current = Volatile.Read(ref _ordinaryCommandsQueued);
                if (current >= MaximumOrdinaryQueuedCommands) return false;
                if (Interlocked.CompareExchange(
                        ref _ordinaryCommandsQueued, current + 1, current) != current) continue;
                reserved = true;
                break;
            }
            _observer?.AfterReservation(command);
            if (IsClosed) throw new InvalidOperationException("The command queue is closed.");
            if (!_commands.TryAdd(command)) return false;
            reserved = false;
            return true;
        }
        finally
        {
            if (reserved) Interlocked.Decrement(ref _ordinaryCommandsQueued);
            EndAdmission();
        }
    }

    public void EnqueueDeadline(NetworkCommand command)
    {
        if (!BeginAdmission()) return;
        var queued = false;
        try
        {
            queued = TryEnqueueDeadlineDirect(command, observeReservation: true);
            if (!queued && !IsClosed) OfferOverflow(CreateOverflow(command));
        }
        finally
        {
            EndAdmission();
        }
        if (!queued) TryPromoteOverflow();
    }

    public IEnumerable<NetworkCommand> GetConsumingEnumerable() =>
        _commands.GetConsumingEnumerable();

    public bool TryTake(out NetworkCommand command) => _commands.TryTake(out command!);

    public void Release(NetworkCommand command)
    {
        if (command is ConnectionTimeoutCommand or WifiScanTimeoutCommand)
        {
            Interlocked.Decrement(ref _deadlineCommandsQueued);
            TryPromoteOverflow();
        }
        else
        {
            Interlocked.Decrement(ref _ordinaryCommandsQueued);
        }
    }

    public void Close()
    {
        while (true)
        {
            var state = Volatile.Read(ref _admissionState);
            if ((state & AdmissionClosedMask) != 0) break;
            if (Interlocked.CompareExchange(
                    ref _admissionState, state | AdmissionClosedMask, state) == state) break;
        }
        ClearOverflow();
        if ((Volatile.Read(ref _admissionState) & AdmissionCountMask) == 0)
            _admissionsDrained.TrySetResult();
        try { _commands.CompleteAdding(); }
        catch (ObjectDisposedException) { }
        if ((Volatile.Read(ref _admissionState) & AdmissionCountMask) != 0)
            _admissionsDrained.Task.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => _commands.Dispose();

    private bool TryEnqueueDeadlineDirect(NetworkCommand command, bool observeReservation)
    {
        if (!TryReserveDeadlineSlot()) return false;
        var reserved = true;
        try
        {
            if (observeReservation) _observer?.AfterReservation(command);
            if (IsClosed) return false;
            try
            {
                if (!_commands.TryAdd(command)) return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            reserved = false;
            return true;
        }
        finally
        {
            if (reserved) Interlocked.Decrement(ref _deadlineCommandsQueued);
        }
    }

    private bool TryReserveDeadlineSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _deadlineCommandsQueued);
            if (current >= ReservedDeadlineCommands) return false;
            if (Interlocked.CompareExchange(
                    ref _deadlineCommandsQueued, current + 1, current) == current) return true;
        }
    }

    private DeadlineOverflow CreateOverflow(NetworkCommand command)
    {
        var generation = command switch
        {
            ConnectionTimeoutCommand timeout => timeout.Generation,
            WifiScanTimeoutCommand timeout => timeout.Generation,
            _ => throw new ArgumentException("Only deadline commands can overflow.", nameof(command)),
        };
        return new DeadlineOverflow(
            command,
            generation,
            Interlocked.Increment(ref _nextOverflowSequence));
    }

    private void OfferOverflow(DeadlineOverflow candidate)
    {
        ref var slot = ref candidate.Command is ConnectionTimeoutCommand
            ? ref _connectionOverflow
            : ref _scanOverflow;
        while (true)
        {
            var current = Volatile.Read(ref slot);
            if (current is not null && current.Generation >= candidate.Generation) return;
            if (Interlocked.CompareExchange(ref slot, candidate, current) == current) return;
        }
    }

    private void TryPromoteOverflow()
    {
        if (!BeginAdmission()) return;
        DeadlineOverflow? overflow = null;
        var queued = false;
        try
        {
            overflow = TakeOldestOverflow();
            if (overflow is null) return;
            queued = TryEnqueueDeadlineDirect(overflow.Command, observeReservation: false);
            if (!queued && !IsClosed) OfferOverflow(overflow);
        }
        finally
        {
            EndAdmission();
        }
    }

    private DeadlineOverflow? TakeOldestOverflow()
    {
        while (true)
        {
            var connection = Volatile.Read(ref _connectionOverflow);
            var scan = Volatile.Read(ref _scanOverflow);
            if (connection is null && scan is null) return null;
            if (scan is null ||
                connection is not null && connection.Sequence <= scan.Sequence)
            {
                if (Interlocked.CompareExchange(ref _connectionOverflow, null, connection) == connection)
                    return connection;
            }
            else if (Interlocked.CompareExchange(ref _scanOverflow, null, scan) == scan)
            {
                return scan;
            }
        }
    }

    private bool BeginAdmission()
    {
        while (true)
        {
            var state = Volatile.Read(ref _admissionState);
            if ((state & AdmissionClosedMask) != 0) return false;
            if ((state & AdmissionCountMask) == AdmissionCountMask)
                throw new InvalidOperationException("Too many command admissions are active.");
            if (Interlocked.CompareExchange(ref _admissionState, state + 1, state) == state)
                return true;
        }
    }

    private void EndAdmission()
    {
        var state = Interlocked.Decrement(ref _admissionState);
        if ((state & AdmissionClosedMask) == 0) return;
        ClearOverflow();
        if (state == AdmissionClosedMask) _admissionsDrained.TrySetResult();
    }

    private void ClearOverflow()
    {
        Interlocked.Exchange(ref _connectionOverflow, null);
        Interlocked.Exchange(ref _scanOverflow, null);
    }

    private sealed record DeadlineOverflow(NetworkCommand Command, long Generation, long Sequence);
}
