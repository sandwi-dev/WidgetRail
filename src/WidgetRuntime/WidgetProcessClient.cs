using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

/// <summary>
/// Host-side lifecycle and IPC client. Constructing it is inert; the worker is
/// launched only when the first render or action request is made.
/// </summary>
public sealed class WidgetProcessClient : IAsyncDisposable
{
    private readonly WidgetProcessOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RuntimeEnvelope>> _pending = new();
    private NamedPipeServerStream? _pipe;
    private LengthPrefixedJsonChannel? _channel;
    private Process? _process;
    private WindowsWorkerJob? _windowsJob;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _readerTask;
    private IWidgetProcessCompanionSession? _companion;
    private Task? _companionTask;
    private WidgetLifecycleState _hostLifecycle = WidgetLifecycleState.Background;
    private long _requestId;
    private int _starts;
    private int _restartAttempts;
    private int _sessionId;
    private int _failureReported;
    private bool _stopping;
    private bool _disposed;

    public WidgetProcessClient(WidgetProcessOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public event EventHandler<long>? Invalidated;
    public event EventHandler<WidgetControllerActionFailure>? ControllerActionFailed;
    public event EventHandler<WidgetFailure>? Failed;

    public bool IsRunning
    {
        get
        {
            var process = _process;
            try { return process is { HasExited: false } && _pipe?.IsConnected == true; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public int Starts => Volatile.Read(ref _starts);
    internal int? WorkerProcessId => _process?.Id;
    internal long? AppliedJobMemoryLimitBytes => _windowsJob?.MemoryLimitBytes;
    internal uint? AppliedJobActiveProcessLimit => _windowsJob?.ActiveProcessLimit;

    public async Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(MessageTypes.Render, new { }, cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Snapshot)
            throw new WidgetProtocolViolationException($"Expected snapshot, received '{response.Type}'.");
        var bytes = Encoding.UTF8.GetBytes(response.Payload.GetRawText());
        try
        {
            var snapshot = SnapshotJson.Deserialize(bytes);
            if (!string.Equals(snapshot.WidgetInstanceId, _options.WidgetInstanceId, StringComparison.Ordinal))
                throw new WidgetProtocolViolationException("Snapshot belongs to a different widget instance.");
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
        {
            ReportFailure(WidgetFailureReason.ProtocolViolation, exception);
            TerminateWorker();
            throw new WidgetProtocolViolationException("Worker returned an invalid snapshot.", exception);
        }
    }

    /// <summary>
    /// Transitions the worker to a host-owned stable lifecycle state.
    /// Requesting Background for an unstarted worker is a no-op and stays lazy.
    /// </summary>
    public async Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidateHostState(state);
        if (state == WidgetLifecycleState.Background && !IsRunning)
        {
            _hostLifecycle = state;
            return;
        }
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await SetCompanionLifecycleAsync(state, cancellationToken).ConfigureAwait(false);
        var response = await RequestConnectedAsync(
            MessageTypes.SetWidgetLifecycle,
            new WidgetLifecyclePayload(state),
            cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException(
                $"Expected lifecycle acknowledgement, received '{response.Type}'.");
        _hostLifecycle = state;
    }

    /// <summary>Compatibility API for callers using the former active/inactive model.</summary>
    public Task SetActiveAsync(bool active, CancellationToken cancellationToken = default) =>
        SetLifecycleStateAsync(
            active ? WidgetLifecycleState.Interactive : WidgetLifecycleState.Background,
            cancellationToken);

    public async Task SendActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var response = await RequestAsync(MessageTypes.Action, action, cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException($"Expected acknowledgement, received '{response.Type}'.");
    }

    public async Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var response = await RequestAsync(MessageTypes.ControllerInput, input, cancellationToken)
            .ConfigureAwait(false);
        if (response.Type != MessageTypes.ControllerInputResult)
            throw new WidgetProtocolViolationException(
                $"Expected controller input result, received '{response.Type}'.");
        return RuntimeJson.FromElement<ControllerInputResultPayload>(response.Payload).Handled;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopping = true;
        if (_companion is not null)
        {
            try
            {
                await _companion.SetLifecycleStateAsync(
                    WidgetLifecycleState.Destroying, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Worker teardown remains bounded even if its companion already disconnected.
            }
        }
        if (IsRunning)
        {
            try
            {
                await RequestAsync(MessageTypes.Stop, new { }, cancellationToken).ConfigureAwait(false);
                var process = _process;
                if (process is not null)
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or WidgetProcessException)
            {
                TerminateWorker();
            }
        }
        await DisposeSessionAsync().ConfigureAwait(false);
        _hostLifecycle = WidgetLifecycleState.Background;
    }

    public void ResetCrashLoop() => Interlocked.Exchange(ref _restartAttempts, 0);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await StopAsync(cancellation.Token).ConfigureAwait(false);
        _disposed = true;
        _lifecycleGate.Dispose();
        _writeGate.Dispose();
    }

    private async Task<RuntimeEnvelope> RequestAsync<T>(
        string type, T payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await RequestConnectedAsync(type, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RuntimeEnvelope> RequestConnectedAsync<T>(
        string type, T payload, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<RuntimeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Duplicate request ID.");

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var channel = _channel ?? throw new IOException("Widget pipe disconnected.");
                await channel.WriteAsync(new RuntimeEnvelope
                {
                    Type = type,
                    RequestId = requestId,
                    Payload = RuntimeJson.ToElement(payload),
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            try
            {
                return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var exception = new TimeoutException(
                    $"Widget request '{type}' exceeded {_options.RequestTimeout.TotalMilliseconds:0} ms.");
                ReportFailure(WidgetFailureReason.RequestTimedOut, exception);
                TerminateWorker();
                throw exception;
            }
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            var isRestart = _starts > 0;
            if (isRestart && _restartAttempts >= _options.MaximumRestartAttempts)
                throw new WidgetProcessException("Widget restart limit has been reached.");

            TerminateWorker();
            await DisposeSessionAsync().ConfigureAwait(false);
            _stopping = false;
            Interlocked.Exchange(ref _failureReported, 0);
            var currentSession = Interlocked.Increment(ref _sessionId);
            using var appContainer = _options.IsolationPolicy ==
                WidgetWorkerIsolationPolicy.RequireAppContainer
                    ? WindowsAppContainer.OpenOrCreate(_options.IsolationKey!)
                    : null;
            if (appContainer is not null)
            {
                var executableDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(_options.ExecutablePath))
                    ?? throw new WidgetProcessException("Worker executable directory is unavailable.");
                appContainer.GrantReadAndExecute(
                    new[] { executableDirectory }.Concat(_options.ReadOnlyPaths));
            }
            var pipeSuffix = $"gba-widget-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var pipeName = pipeSuffix;
            var serverPipeName = pipeName;
            _pipe = appContainer is null
                ? new NamedPipeServerStream(
                    serverPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    4096, 4096)
                : appContainer.CreatePipe(serverPipeName, 4096);
            _channel = new LengthPrefixedJsonChannel(_pipe, _options.MaximumMessageBytes);
            _sessionCancellation = new CancellationTokenSource();

            if (_options.CompanionSessionFactory is not null)
            {
                var companionContext = new WidgetProcessCompanionContext(
                    _options.IsolationPolicy,
                    _options.IsolationKey,
                    appContainer?.Sid);
                _companion = _options.CompanionSessionFactory(companionContext)
                    ?? throw new WidgetProcessException("Companion session factory returned null.");
                ValidateCompanionArguments(_companion.WorkerArguments, companionContext);
            }

            var startInfo = new ProcessStartInfo(_options.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? Environment.CurrentDirectory,
            };
            foreach (var argument in _options.Arguments)
                startInfo.ArgumentList.Add(argument);
            if (_companion is not null)
            {
                foreach (var argument in _companion.WorkerArguments)
                    startInfo.ArgumentList.Add(argument);
            }
            startInfo.ArgumentList.Add("--widget-pipe");
            startInfo.ArgumentList.Add(pipeName);
            startInfo.ArgumentList.Add("--widget-instance");
            startInfo.ArgumentList.Add(_options.WidgetInstanceId);
            startInfo.ArgumentList.Add("--max-message-bytes");
            startInfo.ArgumentList.Add(_options.MaximumMessageBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (OperatingSystem.IsWindows())
            {
                var job = WindowsWorkerJob.Create(_options.MemoryLimitBytes);
                try
                {
                    _process = job.StartProcess(startInfo, appContainer);
                    _windowsJob = job;
                }
                catch
                {
                    job.Dispose();
                    throw;
                }
            }
            else
            {
                _process = Process.Start(startInfo)
                    ?? throw new WidgetProcessException("Worker process did not start.");
            }
            if (_companion is not null)
            {
                _companion.BindWorkerProcess(_process.Id);
                _companionTask = _companion.RunAsync(_sessionCancellation.Token);
            }
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => OnProcessExited(currentSession);
            if (isRestart) Interlocked.Increment(ref _restartAttempts);
            Interlocked.Increment(ref _starts);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ConnectTimeout);
            await _pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows())
                WindowsAppContainer.VerifyPipeClientProcess(
                    _pipe,
                    _process?.Id ?? throw new WidgetProcessException("Worker process identity is unavailable."));
            var hello = await _channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (hello.Type != MessageTypes.Hello || hello.RequestId != 0)
                throw new WidgetProtocolViolationException("Worker handshake was malformed.");
            var helloPayload = RuntimeJson.FromElement<HelloPayload>(hello.Payload);
            if (!string.Equals(helloPayload.WidgetInstanceId, _options.WidgetInstanceId, StringComparison.Ordinal))
                throw new WidgetProtocolViolationException("Worker handshake used the wrong instance ID.");
            await _channel.WriteAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.HelloAccepted,
                Payload = RuntimeJson.ToElement(new { }),
            }, timeout.Token).ConfigureAwait(false);
            _readerTask = ReadResponsesAsync(currentSession, _channel, _sessionCancellation.Token);
            if (_hostLifecycle != WidgetLifecycleState.Background)
            {
                await SetCompanionLifecycleAsync(_hostLifecycle, timeout.Token).ConfigureAwait(false);
                var lifecycleResponse = await RequestConnectedAsync(
                    MessageTypes.SetWidgetLifecycle,
                    new WidgetLifecyclePayload(_hostLifecycle),
                    timeout.Token).ConfigureAwait(false);
                if (lifecycleResponse.Type != MessageTypes.Acknowledged)
                    throw new WidgetProtocolViolationException(
                        $"Expected lifecycle acknowledgement, received '{lifecycleResponse.Type}'.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            int? exitCode = null;
            try
            {
                if (_process?.HasExited == true) exitCode = _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }
            ReportFailure(exception is WidgetProtocolViolationException
                ? WidgetFailureReason.ProtocolViolation
                : WidgetFailureReason.ConnectionFailed, exception);
            TerminateWorker();
            await DisposeSessionAsync().ConfigureAwait(false);
            throw new WidgetProcessException(
                exitCode is null
                    ? "Widget worker connection failed."
                    : $"Widget worker exited with code {exitCode} before connecting.",
                exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ReadResponsesAsync(
        int session,
        LengthPrefixedJsonChannel channel,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message.Type == MessageTypes.Invalidated)
                {
                    if (message.RequestId != 0)
                        throw new WidgetProtocolViolationException("Notifications cannot have request IDs.");
                    var payload = RuntimeJson.FromElement<InvalidationPayload>(message.Payload);
                    if (payload.Revision <= 0)
                        throw new WidgetProtocolViolationException("Invalidation revision must be positive.");
                    Invalidated?.Invoke(this, payload.Revision);
                    continue;
                }

                if (message.Type == MessageTypes.ControllerActionFailed)
                {
                    if (message.RequestId != 0)
                        throw new WidgetProtocolViolationException("Notifications cannot have request IDs.");
                    var payload = RuntimeJson.FromElement<ControllerActionFailurePayload>(message.Payload);
                    ControllerActionFailed?.Invoke(this, new WidgetControllerActionFailure(
                        payload.ActionId, payload.SourceElementId, payload.Message));
                    continue;
                }

                if (message.RequestId == 0 || !_pending.TryRemove(message.RequestId, out var completion))
                    throw new WidgetProtocolViolationException("Response has an unknown request ID.");
                if (message.Type == MessageTypes.Error)
                {
                    var error = RuntimeJson.FromElement<ErrorPayload>(message.Payload);
                    completion.TrySetException(new WidgetProcessException(
                        $"Worker rejected the request ({error.Code}): {error.Message}"));
                }
                else
                {
                    completion.TrySetResult(message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or JsonException or WidgetProtocolViolationException)
        {
            if (session == Volatile.Read(ref _sessionId) && !_stopping)
            {
                ReportFailure(exception is WidgetProtocolViolationException
                    ? WidgetFailureReason.ProtocolViolation
                    : WidgetFailureReason.TransportFailure, exception);
                FailPending(exception);
            }
        }
    }

    private void OnProcessExited(int session)
    {
        if (session != Volatile.Read(ref _sessionId) || _stopping) return;
        ReportFailure(WidgetFailureReason.ProcessExited, null);
        FailPending(new WidgetProcessException("Widget worker exited unexpectedly."));
        _sessionCancellation?.Cancel();
    }

    private void ReportFailure(WidgetFailureReason reason, Exception? exception)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) != 0) return;
        int? exitCode = null;
        try
        {
            if (_process?.HasExited == true) exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }
        var restartsUsed = Volatile.Read(ref _restartAttempts);
        Failed?.Invoke(this, new WidgetFailure(
            reason, exitCode, exception, restartsUsed,
            restartsUsed < _options.MaximumRestartAttempts));
    }

    private void FailPending(Exception exception)
    {
        foreach (var entry in _pending.ToArray())
        {
            if (_pending.TryRemove(entry.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private void TerminateWorker()
    {
        if (_windowsJob is not null)
        {
            try { _windowsJob.Terminate(); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _sessionCancellation?.Cancel();
    }

    private async Task DisposeSessionAsync()
    {
        _sessionCancellation?.Cancel();
        _pipe?.Dispose();
        _process?.Dispose();
        _windowsJob?.Dispose();
        if (_companion is not null)
        {
            try { await _companion.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        if (_companionTask is not null)
        {
            try { await _companionTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _readerTask = null;
        _pipe = null;
        _channel = null;
        _process = null;
        _windowsJob = null;
        _companion = null;
        _companionTask = null;
    }

    private async Task SetCompanionLifecycleAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken)
    {
        if (_companion is null) return;
        try
        {
            await _companion.SetLifecycleStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure(WidgetFailureReason.TransportFailure, exception);
            TerminateWorker();
            throw new WidgetProcessException("Widget companion lifecycle update failed.", exception);
        }
    }

    private static void ValidateCompanionArguments(
        IReadOnlyList<string> arguments,
        WidgetProcessCompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > 32 || arguments.Any(argument =>
                argument is null || argument.Length == 0 || argument.Length > 4096))
            throw new WidgetProcessException("Companion worker arguments are invalid.");
        if (context.IsolationPolicy != WidgetWorkerIsolationPolicy.RequireAppContainer) return;
        var pipeIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--broker-pipe", StringComparison.Ordinal)) continue;
            pipeIndex = index;
            break;
        }
        if (pipeIndex < 0 || pipeIndex + 1 >= arguments.Count ||
            string.IsNullOrWhiteSpace(context.AppContainerSid) ||
            !IsPlainPipeName(arguments[pipeIndex + 1]))
            throw new WidgetProcessException(
                "An AppContainer worker companion must use a host-secured plain pipe name.");
    }

    private static bool IsPlainPipeName(string value) =>
        value.Length is > 0 and <= 200 &&
        !value.Contains('\\') &&
        !value.Contains('/') &&
        !value.Any(char.IsControl);

    private static void ValidateHostState(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "Hosts may request only Background, Visible, or Interactive.");
    }
}

public sealed class WidgetProcessException(string message, Exception? innerException = null)
    : Exception(message, innerException);
