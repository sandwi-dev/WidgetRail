using System.Diagnostics;
using System.IO.Pipes;

namespace GameBarAlternative.WidgetRuntime;

internal sealed class WidgetProcessSession(
    TimeProvider timeProvider,
    TimeSpan maximumGestureReservationLifetime)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _terminalGate = new();
    private Task? _terminalTask;

    internal WidgetPendingRequests PendingRequests { get; } = new();
    internal WidgetDashboardGestureReservations GestureReservations { get; } =
        new(timeProvider, maximumGestureReservationLifetime);
    internal NamedPipeServerStream? Pipe { get; private set; }
    internal LengthPrefixedJsonChannel? Channel { get; private set; }
    internal Process? Process { get; private set; }
    internal WindowsWorkerJob? WindowsJob { get; private set; }
    internal IWidgetProcessCompanionSession? Companion { get; private set; }
    internal CancellationToken CancellationToken =>
        _cancellation?.Token ?? CancellationToken.None;
    internal bool IsTerminal
    {
        get { lock (_terminalGate) return _terminalTask is not null; }
    }

    private IDisposable? _processLease;
    private IWidgetProcessContentLease? _contentLease;
    private CancellationTokenSource? _cancellation;
    private Task? _readerTask;
    private Task? _companionTask;

    internal bool IsRunning
    {
        get
        {
            try
            {
                return !IsTerminal && Process is { HasExited: false } && Pipe?.IsConnected == true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    internal int? ExitCode
    {
        get
        {
            try { return Process?.HasExited == true ? Process.ExitCode : null; }
            catch (InvalidOperationException) { return null; }
        }
    }

    internal void AttachProcessLease(IDisposable lease) =>
        _processLease = lease ?? throw new ArgumentNullException(nameof(lease));

    internal void AttachContentLease(IWidgetProcessContentLease lease) =>
        _contentLease = lease ?? throw new ArgumentNullException(nameof(lease));

    internal IWidgetProcessContentLease? ContentLease => _contentLease;

    internal void AttachTransport(
        NamedPipeServerStream pipe,
        LengthPrefixedJsonChannel channel)
    {
        Pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _cancellation = new CancellationTokenSource();
    }

    internal void AttachCompanion(IWidgetProcessCompanionSession companion) =>
        Companion = companion ?? throw new ArgumentNullException(nameof(companion));

    internal void AttachProcess(Process process, WindowsWorkerJob? windowsJob)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        WindowsJob = windowsJob;
    }

    internal void StartCompanion() =>
        _companionTask = Companion?.RunAsync(CancellationToken);

    internal void AttachReader(Task readerTask) =>
        _readerTask = readerTask ?? throw new ArgumentNullException(nameof(readerTask));

    internal async Task WriteAsync(RuntimeEnvelope message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsTerminal, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, CancellationToken);
        await _writeGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsTerminal, this);
            var channel = Channel ?? throw new IOException("Widget pipe disconnected.");
            await channel.WriteAsync(message, linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal void Terminate()
    {
        if (WindowsJob is not null)
        {
            try { WindowsJob.Terminate(); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        try
        {
            if (Process is { HasExited: false }) Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        Cancel();
    }

    internal void Cancel()
    {
        try { _cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal void ReleaseLeases()
    {
        var contentLease = Interlocked.Exchange(ref _contentLease, null);
        try { contentLease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        var processLease = Interlocked.Exchange(ref _processLease, null);
        try { processLease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    internal Task DisposeAsync(CancellationToken cancellationToken = default)
    {
        lock (_terminalGate)
            return _terminalTask ??= DisposeCoreAsync(cancellationToken);
    }

    private async Task DisposeCoreAsync(CancellationToken cancellationToken)
    {
        GestureReservations.Clear();
        PendingRequests.FailAll(new WidgetProcessException("Widget session ended."));
        Cancel();
        Pipe?.Dispose();
        Process?.Dispose();
        WindowsJob?.Dispose();
        ReleaseLeases();

        Task? companionDisposeTask = null;
        if (Companion is not null)
        {
            try { companionDisposeTask = Companion.DisposeAsync().AsTask(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        var cleanupTasks = new[] { companionDisposeTask, _companionTask, _readerTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .Select(ObserveCleanupAsync)
            .ToArray();

        Pipe = null;
        Channel = null;
        Process = null;
        WindowsJob = null;
        Companion = null;
        _readerTask = null;
        _companionTask = null;

        if (cleanupTasks.Length != 0)
        {
            var cleanup = Task.WhenAll(cleanupTasks);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CleanupTimeout);
            try { await cleanup.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _ = ObserveCleanupAsync(cleanup);
            }
        }

        _cancellation?.Dispose();
        _cancellation = null;
        using var writeDrain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        writeDrain.CancelAfter(CleanupTimeout);
        try
        {
            await _writeGate.WaitAsync(writeDrain.Token).ConfigureAwait(false);
            _writeGate.Release();
            _writeGate.Dispose();
        }
        catch (OperationCanceledException) when (writeDrain.IsCancellationRequested)
        {
            // The pipe and session token are already terminal. A transport
            // implementation which ignores both cannot publish into a later
            // session; leave its private semaphore undisposed so a late
            // continuation can release without an unobserved disposal fault.
        }
    }

    private static async Task ObserveCleanupAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
