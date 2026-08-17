using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>
/// Process-wide bounded, message-pumped STA lane for Shell namespace COM.
/// Cancellation stops queued work and prevents result publication. A native
/// call that exceeds the watchdog permanently poisons this lane: callers fail
/// promptly and the process never accumulates replacement COM apartments.
/// </summary>
internal sealed class ShellStaExecutor : IShellStaExecutor
{
    internal const int MaximumQueuedOperations = 32;
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(15);

    private const uint WaitObject0 = 0;
    private const uint WaitFailed = uint.MaxValue;
    private const uint Infinite = uint.MaxValue;
    private const uint QsAllInput = 0x04ff;
    private const uint MwmoAlertable = 0x0002;
    private const uint MwmoInputAvailable = 0x0004;
    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;
    private const uint CoinitApartmentThreaded = 0x0002;
    private const uint CoinitDisableOle1Dde = 0x0004;

    private readonly Channel<IWorkItem> _queue;
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly TimeSpan _operationTimeout;
    private readonly object _stateGate = new();
    private IWorkItem? _active;
    private Exception? _poisonReason;

    internal static ShellStaExecutor Shared { get; } = new(DefaultOperationTimeout);

    internal ShellStaExecutor(TimeSpan operationTimeout)
    {
        if (operationTimeout <= TimeSpan.Zero || operationTimeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        _operationTimeout = operationTimeout;
        _queue = Channel.CreateBounded<IWorkItem>(new BoundedChannelOptions(
            MaximumQueuedOperations)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WidgetRail Shell STA",
        };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfPoisoned();

        var item = new WorkItem<T>(operation, cancellationToken);
        try
        {
            await _queue.Writer.WriteAsync(item, cancellationToken).AsTask()
                .WaitAsync(_operationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            Poison(new TimeoutException(
                "The Shell STA queue did not accept work within its bounded deadline.",
                exception));
            ThrowIfPoisoned();
            throw;
        }
        catch (ChannelClosedException)
        {
            ThrowIfPoisoned();
            throw;
        }

        _workAvailable.Set();
        _ = WatchAsync(item);
        return await item.TypedCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WatchAsync(IWorkItem item)
    {
        await Task.Delay(_operationTimeout).ConfigureAwait(false);
        if (!item.Completion.IsCompleted)
        {
            Poison(new TimeoutException(
                "A Windows Shell operation exceeded its bounded deadline."));
        }
    }

    private void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            RunPortable();
            return;
        }

        var initialized = false;
        try
        {
            var result = CoInitializeEx(
                0, CoinitApartmentThreaded | CoinitDisableOle1Dde);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            initialized = true;
            RunWindowsMessagePump();
        }
        catch (Exception exception)
        {
            Poison(exception);
        }
        finally
        {
            if (initialized) CoUninitialize();
        }
    }

    private void RunPortable()
    {
        try
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                if (_queue.Reader.TryRead(out var item)) Execute(item);
                if (IsPoisoned()) return;
            }
        }
        catch (Exception exception)
        {
            Poison(exception);
        }
    }

    private void RunWindowsMessagePump()
    {
        var handles = new[] { _workAvailable.SafeWaitHandle.DangerousGetHandle() };
        while (!IsPoisoned())
        {
            var result = MsgWaitForMultipleObjectsEx(
                1, handles, Infinite, QsAllInput,
                MwmoAlertable | MwmoInputAvailable);
            if (result == WaitObject0)
            {
                if (_queue.Reader.TryRead(out var item)) Execute(item);
                if (!IsPoisoned() && _queue.Reader.TryPeek(out _))
                    _workAvailable.Set();
                continue;
            }
            if (result == WaitObject0 + 1)
            {
                if (!PumpMessages()) return;
                continue;
            }
            if (result == WaitFailed)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

    private bool PumpMessages()
    {
        while (PeekMessage(out var message, 0, 0, 0, PmRemove))
        {
            if (message.Id == WmQuit)
            {
                Poison(new InvalidOperationException(
                    "The Windows Shell STA message loop stopped unexpectedly."));
                return false;
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        return true;
    }

    private void Execute(IWorkItem item)
    {
        lock (_stateGate)
        {
            if (_poisonReason is not null)
            {
                item.Fail(CreateUnavailableException(_poisonReason));
                return;
            }
            _active = item;
        }
        item.Execute();
        lock (_stateGate) _active = null;
    }

    private void Poison(Exception reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        IWorkItem? active;
        lock (_stateGate)
        {
            if (_poisonReason is not null) return;
            _poisonReason = reason;
            active = _active;
        }

        var unavailable = CreateUnavailableException(reason);
        active?.Fail(unavailable);
        _queue.Writer.TryComplete(unavailable);
        while (_queue.Reader.TryRead(out var queued)) queued.Fail(unavailable);
        _workAvailable.Set();
    }

    private bool IsPoisoned()
    {
        lock (_stateGate) return _poisonReason is not null;
    }

    private void ThrowIfPoisoned()
    {
        Exception? reason;
        lock (_stateGate) reason = _poisonReason;
        if (reason is not null) throw CreateUnavailableException(reason);
    }

    private static InvalidOperationException CreateUnavailableException(Exception reason) =>
        new("The Windows Shell execution lane is unavailable.", reason);

    private interface IWorkItem
    {
        Task Completion { get; }
        void Execute();
        void Fail(Exception exception);
    }

    private sealed class WorkItem<T>(
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken) : IWorkItem
    {
        internal Task<T> TypedCompletion => CompletionSource.Task;
        private TaskCompletionSource<T> CompletionSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => CompletionSource.Task;

        public void Execute()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CompletionSource.TrySetCanceled(cancellationToken);
                return;
            }
            try
            {
                var result = operation(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    CompletionSource.TrySetCanceled(cancellationToken);
                else
                    CompletionSource.TrySetResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CompletionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                CompletionSource.TrySetException(exception);
            }
        }

        public void Fail(Exception exception) =>
            CompletionSource.TrySetException(exception);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint count, nint[] handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out Message message, nint window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);
}
