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
    private CancellationTokenSource? _sessionCancellation;
    private Task? _readerTask;
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
        if (state == WidgetLifecycleState.Background && !IsRunning) return;
        var response = await RequestAsync(
            MessageTypes.SetWidgetLifecycle,
            new WidgetLifecyclePayload(state),
            cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException(
                $"Expected lifecycle acknowledgement, received '{response.Type}'.");
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
        DisposeSession();
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
            DisposeSession();
            _stopping = false;
            Interlocked.Exchange(ref _failureReported, 0);
            var currentSession = Interlocked.Increment(ref _sessionId);
            var pipeName = $"gba-widget-{Environment.ProcessId}-{Guid.NewGuid():N}";
            _pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                4096, 4096);
            _channel = new LengthPrefixedJsonChannel(_pipe, _options.MaximumMessageBytes);
            _sessionCancellation = new CancellationTokenSource();

            var startInfo = new ProcessStartInfo(_options.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? Environment.CurrentDirectory,
            };
            foreach (var argument in _options.Arguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add("--widget-pipe");
            startInfo.ArgumentList.Add(pipeName);
            startInfo.ArgumentList.Add("--widget-instance");
            startInfo.ArgumentList.Add(_options.WidgetInstanceId);
            startInfo.ArgumentList.Add("--max-message-bytes");
            startInfo.ArgumentList.Add(_options.MaximumMessageBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

            _process = Process.Start(startInfo) ?? throw new WidgetProcessException("Worker process did not start.");
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => OnProcessExited(currentSession);
            if (isRestart) Interlocked.Increment(ref _restartAttempts);
            Interlocked.Increment(ref _starts);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ConnectTimeout);
            await _pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
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
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or WidgetProtocolViolationException)
        {
            ReportFailure(exception is WidgetProtocolViolationException
                ? WidgetFailureReason.ProtocolViolation
                : WidgetFailureReason.ConnectionFailed, exception);
            TerminateWorker();
            throw new WidgetProcessException("Widget worker connection failed.", exception);
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
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _sessionCancellation?.Cancel();
    }

    private void DisposeSession()
    {
        _sessionCancellation?.Cancel();
        _pipe?.Dispose();
        _process?.Dispose();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _readerTask = null;
        _pipe = null;
        _channel = null;
        _process = null;
    }

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
