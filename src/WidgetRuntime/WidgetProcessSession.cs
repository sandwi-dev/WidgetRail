using System.Diagnostics;
using System.IO.Pipes;

namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetProcessSession(
    TimeProvider timeProvider,
    TimeSpan maximumGestureReservationLifetime)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _terminalGate = new();
    private Task? _terminalTask;
    private int _activePublications;
    private int _handshakeCompleted;
    private int? _exitCode;
    private TaskCompletionSource? _publicationsDrained;

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
    internal bool HandshakeCompleted => Volatile.Read(ref _handshakeCompleted) != 0;

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
            lock (_terminalGate)
            {
                try
                {
                    if (_exitCode is null && Process is { HasExited: true } process)
                        _exitCode = process.ExitCode;
                }
                catch (InvalidOperationException) { }
                return _exitCode;
            }
        }
    }

    internal void AttachProcessLease(IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_terminalGate)
        {
            if (_terminalTask is null)
            {
                _processLease = lease;
                return;
            }
        }
        lease.Dispose();
        throw new ObjectDisposedException(nameof(WidgetProcessSession));
    }

    internal void AttachContentLease(IWidgetProcessContentLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_terminalGate)
        {
            if (_terminalTask is null)
            {
                _contentLease = lease;
                return;
            }
        }
        lease.Dispose();
        throw new ObjectDisposedException(nameof(WidgetProcessSession));
    }

    internal IWidgetProcessContentLease? ContentLease => _contentLease;

    internal void AttachTransport(
        NamedPipeServerStream pipe,
        LengthPrefixedJsonChannel channel)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(channel);
        lock (_terminalGate)
        {
            if (_terminalTask is null)
            {
                Pipe = pipe;
                Channel = channel;
                _cancellation = new CancellationTokenSource();
                return;
            }
        }
        pipe.Dispose();
        throw new ObjectDisposedException(nameof(WidgetProcessSession));
    }

    internal void AttachCompanion(IWidgetProcessCompanionSession companion)
    {
        ArgumentNullException.ThrowIfNull(companion);
        lock (_terminalGate)
        {
            if (_terminalTask is null)
            {
                Companion = companion;
                return;
            }
        }
        _ = ObserveCleanupAsync(companion.DisposeAsync().AsTask());
        throw new ObjectDisposedException(nameof(WidgetProcessSession));
    }

    internal void StartProcess(Func<(Process Process, WindowsWorkerJob? WindowsJob)> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (_terminalGate)
        {
            ObjectDisposedException.ThrowIf(_terminalTask is not null, this);
            var started = start();
            Process = started.Process ??
                throw new WidgetProcessException("Worker process did not start.");
            WindowsJob = started.WindowsJob;
        }
    }

    internal void StartCompanion()
    {
        lock (_terminalGate)
        {
            ObjectDisposedException.ThrowIf(_terminalTask is not null, this);
            _companionTask = Companion?.RunAsync(CancellationToken);
        }
    }

    internal void StartReader(Func<CancellationToken, Task> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (_terminalGate)
        {
            ObjectDisposedException.ThrowIf(_terminalTask is not null, this);
            _readerTask = start(CancellationToken);
        }
    }

    internal void MarkHandshakeCompleted() =>
        Interlocked.Exchange(ref _handshakeCompleted, 1);

    internal bool TryBeginPublication(out IDisposable admission)
    {
        lock (_terminalGate)
        {
            if (_terminalTask is not null)
            {
                admission = null!;
                return false;
            }
            _activePublications++;
            admission = new PublicationAdmission(this);
            return true;
        }
    }

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
        var job = WindowsJob;
        if (job is not null)
        {
            try { job.Terminate(); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        try
        {
            if (Process is { HasExited: false } process) process.Kill(entireProcessTree: true);
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
        TaskCompletionSource completion;
        lock (_terminalGate)
        {
            if (_terminalTask is not null) return _terminalTask;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _terminalTask = completion.Task;
        }
        _ = CompleteDisposeAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task CompleteDisposeAsync(
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            await DisposeCoreAsync(cancellationToken).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DisposeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DisposeOwnedResourcesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseLeases();
        }
    }

    private async Task DisposeOwnedResourcesAsync(CancellationToken cancellationToken)
    {
        GestureReservations.Clear();
        PendingRequests.FailAll(new WidgetProcessException("Widget session ended."));
        Cancel();
        Pipe?.Dispose();
        lock (_terminalGate)
        {
            // Startup failure handling can resume after the exit callback has
            // retired the process. Preserve its diagnostic through that cleanup.
            _ = ExitCode;
            Process?.Dispose();
            Process = null;
        }
        WindowsJob?.Dispose();

        Task? companionDisposeTask = null;
        if (Companion is not null)
        {
            try { companionDisposeTask = Companion.DisposeAsync().AsTask(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        Task publicationDrain;
        lock (_terminalGate)
        {
            publicationDrain = _activePublications == 0
                ? Task.CompletedTask
                : (_publicationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        var cleanupTasks = new[]
            { companionDisposeTask, _companionTask, _readerTask, publicationDrain }
            .Where(task => task is not null)
            .Cast<Task>()
            .Select(ObserveCleanupAsync)
            .ToArray();

        Pipe = null;
        Channel = null;
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

    private void EndPublication()
    {
        TaskCompletionSource? drained = null;
        lock (_terminalGate)
        {
            if (--_activePublications == 0)
            {
                drained = _publicationsDrained;
                _publicationsDrained = null;
            }
        }
        drained?.TrySetResult();
    }

    private sealed class PublicationAdmission(WidgetProcessSession owner) : IDisposable
    {
        private WidgetProcessSession? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndPublication();
    }
}
