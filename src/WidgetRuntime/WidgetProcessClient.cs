using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

/// <summary>
/// Host-side lifecycle and IPC client. Constructing it is inert; the worker is
/// launched only when the first render or action request is made.
/// </summary>
public sealed class WidgetProcessClient : IAsyncDisposable
{
    private static readonly TimeSpan CompanionCleanupTimeout = TimeSpan.FromSeconds(1);
    // A dormant reservation is not broker authority. It exists only long
    // enough for bounded serial widget work to reach its exact capability call.
    // The separate broker lease remains at most two seconds and starts only then.
    private static readonly TimeSpan MaximumDashboardGestureReservationLifetime =
        TimeSpan.FromSeconds(10);
    private readonly WidgetProcessOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RuntimeEnvelope>> _pending = new();
    private readonly object _dashboardGestureGate = new();
    private readonly Dictionary<DashboardGestureKey, PendingDashboardGesture>
        _pendingDashboardGestures = [];
    private NamedPipeServerStream? _pipe;
    private LengthPrefixedJsonChannel? _channel;
    private Process? _process;
    private WindowsWorkerJob? _windowsJob;
    private IDisposable? _processLease;
    private IWidgetProcessContentLease? _contentLease;
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
    private bool _residencyUnloaded;
    private bool _stopping;
    private bool _disposed;

    public WidgetProcessClient(WidgetProcessOptions options)
        : this(options, TimeProvider.System)
    {
    }

    internal WidgetProcessClient(WidgetProcessOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options.Validate();
    }

    public event EventHandler<long>? Invalidated;
    public event EventHandler<WidgetActionFailure>? ActionFailed;
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
        if (state != _hostLifecycle) ClearPendingDashboardGestures();
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

    /// <summary>
    /// Admits an action to the worker's active-lifetime queue. The returned
    /// status describes admission, not action completion.
    /// </summary>
    public async Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var response = await RequestAsync(MessageTypes.Action, action, cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException($"Expected acknowledgement, received '{response.Type}'.");
        return ParseActionAdmission(response.Payload);
    }

    /// <summary>
    /// Compatibility wrapper which throws when the active action queue rejects
    /// admission. Successful return does not mean the action has completed.
    /// </summary>
    public async Task SendActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        var admission = await AdmitActionAsync(action, cancellationToken).ConfigureAwait(false);
        if (admission == WidgetOperationAdmission.RejectedInactive)
            throw new WidgetProcessException("Worker rejected the action because the widget is inactive.");
        if (admission == WidgetOperationAdmission.RejectedCapacity)
            throw new WidgetProcessException("Worker rejected the action because its action queue is full.");
    }

    public async Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default) =>
        await SendControllerInputAsync(input, authority: null, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Sends controller input and, when supplied by the trusted host, reserves
    /// one exact dashboard gesture authority. The broker lifetime starts only
    /// if that exact queued action later executes a matching capability call.
    /// </summary>
    public async Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var authorityMayRemain = false;
        if (authority is not null) ReserveDashboardGesture(input, authority);
        try
        {
            var response = await RequestConnectedAsync(
                    MessageTypes.ControllerInput, input, cancellationToken)
                .ConfigureAwait(false);
            if (response.Type != MessageTypes.ControllerInputResult)
                throw new WidgetProtocolViolationException(
                    $"Expected controller input result, received '{response.Type}'.");
            var handled = RuntimeJson.FromElement<ControllerInputResultPayload>(response.Payload).Handled;
            authorityMayRemain = handled;
            return handled;
        }
        finally
        {
            if (authority is not null && !authorityMayRemain)
            {
                RemovePendingDashboardGesture(
                    authority.InputSequence, authority.SnapshotSequence);
                await RevokeCompanionGestureAuthorityAsync(authority.InputSequence)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopCoreAsync(markResidencyUnload: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cooperatively destroys a Background worker for an explicit host
    /// residency policy while retaining this reusable process client. This is
    /// not a crash and the next lazy launch does not consume restart budget.
    /// </summary>
    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        if (_hostLifecycle != WidgetLifecycleState.Background)
            throw new InvalidOperationException("Only a Background worker may be unloaded.");
        await StopCoreAsync(markResidencyUnload: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(
        bool markResidencyUnload,
        CancellationToken cancellationToken)
    {
        _stopping = true;
        var stopAcknowledged = false;
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
                var response = await RequestAsync(MessageTypes.Stop, new { }, cancellationToken)
                    .ConfigureAwait(false);
                if (response.Type != MessageTypes.Acknowledged)
                    throw new WidgetProtocolViolationException(
                        $"Expected stop acknowledgement, received '{response.Type}'.");
                stopAcknowledged = true;
                var process = _process;
                if (process is not null)
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                TerminateWorker();
            }
        }
        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);
        _hostLifecycle = WidgetLifecycleState.Background;
        _residencyUnloaded = markResidencyUnload && stopAcknowledged;
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
            var isResidencyResume = _residencyUnloaded;
            _residencyUnloaded = false;
            if (isRestart && !isResidencyResume &&
                _restartAttempts >= _options.MaximumRestartAttempts)
                throw new WidgetProcessException("Widget restart limit has been reached.");

            TerminateWorker();
            await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);
            if (_options.ProcessLeaseFactory is { } leaseFactory)
                _processLease = leaseFactory() ?? throw new WidgetProcessAdmissionException(
                    "Worker process admission returned no lease.");
            if (_options.ContentLeaseFactory is { } contentLeaseFactory)
            {
                using var contentTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                contentTimeout.CancelAfter(_options.ContentLeaseTimeout);
                try
                {
                    _contentLease = contentLeaseFactory(contentTimeout.Token) ??
                        throw new WidgetProcessAdmissionException(
                            "Worker content admission returned no lease.");
                    contentTimeout.Token.ThrowIfCancellationRequested();
                    ValidateContentLease(_contentLease);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    contentTimeout.IsCancellationRequested)
                {
                    throw new WidgetProcessAdmissionException(
                        "Worker content admission exceeded its time limit.");
                }
            }
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
                if (_contentLease is not null && _contentLease.AuthorityRoots.Any(root =>
                        IsWithinOrEqual(executableDirectory, root) ||
                        IsWithinOrEqual(root, executableDirectory)))
                    throw new WidgetProcessAdmissionException(
                        "Worker content authority overlaps the trusted runtime directory.");
                appContainer.GrantReadAndExecute(
                    new[] { executableDirectory }.Concat(_options.ReadOnlyPaths));
                if (_contentLease is not null)
                    appContainer.ReplaceReadAndExecuteGrant(
                        _contentLease.AuthorityRoots,
                        _contentLease.ReadOnlyDirectories,
                        _contentLease.ReadOnlyFiles);
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
            if (isRestart && !isResidencyResume)
                Interlocked.Increment(ref _restartAttempts);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TerminateWorker();
            await DisposeSessionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (WidgetProcessAdmissionException)
        {
            await DisposeSessionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
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
            await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);
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
                    var actionFailure = new WidgetActionFailure(
                        payload.ActionId, payload.SourceElementId, payload.Message);
                    ActionFailed?.Invoke(this, actionFailure);
                    ControllerActionFailed?.Invoke(this, new WidgetControllerActionFailure(
                        actionFailure.ActionId, actionFailure.SourceElementId, actionFailure.Message));
                    continue;
                }

                if (message.Type == MessageTypes.DashboardGestureActivationRequested)
                {
                    if (message.RequestId != 0)
                        throw new WidgetProtocolViolationException(
                            "Dashboard gesture activation must be a notification.");
                    var activation = RuntimeJson.FromElement<
                        DashboardGestureActivationRequestPayload>(message.Payload);
                    await ActivatePendingDashboardGestureAsync(activation, cancellationToken)
                        .ConfigureAwait(false);
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

    internal static WidgetOperationAdmission ParseActionAdmission(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            !payload.TryGetProperty("admission", out _))
        {
            if (!payload.EnumerateObject().Any()) return WidgetOperationAdmission.Enqueued;
            throw new WidgetProtocolViolationException(
                "Worker returned an action acknowledgement without admission.");
        }

        var admission = RuntimeJson.FromElement<ActionAdmissionPayload>(payload).Admission;
        if (admission is not (
            WidgetOperationAdmission.Enqueued or
            WidgetOperationAdmission.Replaced or
            WidgetOperationAdmission.RejectedInactive or
            WidgetOperationAdmission.RejectedCapacity))
            throw new WidgetProtocolViolationException(
                $"Worker returned invalid action admission '{admission}'.");
        return admission;
    }

    private void OnProcessExited(int session)
    {
        if (session != Volatile.Read(ref _sessionId) || _stopping) return;
        ReleaseSessionLeases();
        ReportFailure(WidgetFailureReason.ProcessExited, null);
        FailPending(new WidgetProcessException("Widget worker exited unexpectedly."));
        ClearPendingDashboardGestures();
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

    private async Task DisposeSessionAsync(CancellationToken cancellationToken = default)
    {
        ClearPendingDashboardGestures();
        var sessionCancellation = _sessionCancellation;
        var pipe = _pipe;
        var process = _process;
        var windowsJob = _windowsJob;
        var processLease = Interlocked.Exchange(ref _processLease, null);
        var contentLease = Interlocked.Exchange(ref _contentLease, null);
        var companion = _companion;
        var companionTask = _companionTask;

        // Detach first so a timed-out companion cleanup cannot retain or
        // mutate the next lazy worker session.
        _sessionCancellation = null;
        _readerTask = null;
        _pipe = null;
        _channel = null;
        _process = null;
        _windowsJob = null;
        _companion = null;
        _companionTask = null;

        sessionCancellation?.Cancel();
        pipe?.Dispose();
        process?.Dispose();
        windowsJob?.Dispose();
        try { contentLease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        try { processLease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }

        Task? disposeTask = null;
        if (companion is not null)
        {
            try { disposeTask = companion.DisposeAsync().AsTask(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        var cleanupTasks = new[] { disposeTask, companionTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .Select(ObserveCompanionCleanupAsync)
            .ToArray();
        if (cleanupTasks.Length != 0)
        {
            var cleanup = Task.WhenAll(cleanupTasks);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CompanionCleanupTimeout);
            try { await cleanup.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // The host-owned session has already been detached and its
                // process/pipe/Job resources released. Observe late completion
                // without allowing faulty companion cleanup to block teardown.
                _ = ObserveCompanionCleanupAsync(cleanup);
            }
        }
        sessionCancellation?.Dispose();
    }

    private void ReleaseSessionLeases()
    {
        var contentLease = Interlocked.Exchange(ref _contentLease, null);
        try { contentLease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        var lease = Interlocked.Exchange(ref _processLease, null);
        try { lease?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static void ValidateContentLease(IWidgetProcessContentLease lease)
    {
        if (lease.AuthorityRoots is null || lease.ReadOnlyDirectories is null ||
            lease.ReadOnlyFiles is null || lease.AuthorityRoots.Count is < 1 or > 8 ||
            lease.ReadOnlyDirectories.Count is < 1 or
                > WidgetProcessContentLimits.MaximumDirectories ||
            lease.ReadOnlyFiles.Count is < 1 or > WidgetProcessContentLimits.MaximumFiles)
            throw new WidgetProcessAdmissionException(
                "Worker content admission returned invalid authority bounds.");

        var roots = lease.AuthorityRoots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roots.Length != lease.AuthorityRoots.Count ||
            roots.Any(root => !Directory.Exists(root) || IsReparsePoint(root)))
            throw new WidgetProcessAdmissionException(
                "Worker content admission returned an invalid authority root.");

        ValidateExactPaths(lease.ReadOnlyDirectories, roots, expectDirectory: true);
        ValidateExactPaths(lease.ReadOnlyFiles, roots, expectDirectory: false);
    }

    private static void ValidateExactPaths(
        IReadOnlyList<string> paths,
        IReadOnlyList<string> roots,
        bool expectDirectory)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in paths)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned a blank path.");
            var fullPath = Path.GetFullPath(value);
            if (!distinct.Add(fullPath) ||
                !roots.Any(root => IsWithinOrEqual(root, fullPath)) ||
                (expectDirectory ? !Directory.Exists(fullPath) : !File.Exists(fullPath)) ||
                IsReparsePoint(fullPath))
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned an invalid exact path.");
        }
    }

    private static bool IsWithinOrEqual(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task ObserveCompanionCleanupAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
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

    private async Task GrantCompanionGestureAuthorityAsync(
        WidgetDashboardGestureAuthority authority,
        CancellationToken cancellationToken)
    {
        var companion = _companion ?? throw new WidgetProcessException(
            "Dashboard capability authority requires a broker companion.");
        try
        {
            await companion.GrantDashboardGestureAuthorityAsync(authority, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new WidgetProcessException(
                "Widget companion rejected dashboard gesture authority.", exception);
        }
    }

    private void ReserveDashboardGesture(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority authority)
    {
        if (input.Context != ControllerInputContext.DashboardQuickAction ||
            input.Sequence != authority.InputSequence ||
            input.SnapshotSequence != authority.SnapshotSequence ||
            authority.InputSequence <= 0 || authority.SnapshotSequence <= 0 ||
            string.IsNullOrWhiteSpace(authority.CapabilityId) ||
            string.IsNullOrWhiteSpace(authority.OperationId) ||
            authority.ValidFor <= TimeSpan.Zero ||
            authority.ValidFor > PlatformCapabilityBroker.MaximumDashboardGestureLifetime)
            throw new WidgetProcessException(
                "Dashboard gesture authority does not match its controller input.");
        var key = new DashboardGestureKey(
            authority.InputSequence, authority.SnapshotSequence);
        lock (_dashboardGestureGate)
        {
            var nowTimestamp = _timeProvider.GetTimestamp();
            PurgeExpiredDashboardGesturesNoLock(nowTimestamp);
            if (_pendingDashboardGestures.Count >= Widget.ControllerActionQueueCapacity)
                throw new WidgetProcessException(
                    "Dashboard gesture reservation capacity was reached.");
            if (!_pendingDashboardGestures.TryAdd(
                    key,
                    new PendingDashboardGesture(
                        authority, nowTimestamp)))
                throw new WidgetProcessException(
                    "Dashboard gesture authority was already reserved.");
        }
    }

    private async Task ActivatePendingDashboardGestureAsync(
        DashboardGestureActivationRequestPayload activation,
        CancellationToken cancellationToken)
    {
        if (activation.ActivationId <= 0 || activation.InputSequence <= 0 ||
            activation.SnapshotSequence <= 0 ||
            string.IsNullOrWhiteSpace(activation.CapabilityId) ||
            string.IsNullOrWhiteSpace(activation.OperationId))
            throw new WidgetProtocolViolationException(
                "Dashboard gesture activation payload is invalid.");

        WidgetDashboardGestureAuthority? authority = null;
        var key = new DashboardGestureKey(
            activation.InputSequence, activation.SnapshotSequence);
        lock (_dashboardGestureGate)
        {
            PurgeExpiredDashboardGesturesNoLock(_timeProvider.GetTimestamp());
            if (_pendingDashboardGestures.TryGetValue(key, out var pending) &&
                string.Equals(
                    pending.Authority.CapabilityId, activation.CapabilityId, StringComparison.Ordinal) &&
                string.Equals(
                    pending.Authority.OperationId, activation.OperationId, StringComparison.Ordinal))
            {
                _pendingDashboardGestures.Remove(key);
                authority = pending.Authority;
            }
        }

        var authorized = false;
        if (authority is not null)
        {
            try
            {
                await GrantCompanionGestureAuthorityAsync(authority, cancellationToken)
                    .ConfigureAwait(false);
                authorized = true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException &&
                                               !cancellationToken.IsCancellationRequested)
            {
                authorized = false;
            }
        }
        await SendConnectedNotificationAsync(
            MessageTypes.DashboardGestureActivationResult,
            new DashboardGestureActivationResultPayload(
                activation.ActivationId, authorized),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendConnectedNotificationAsync<T>(
        string type,
        T payload,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = _channel ?? throw new IOException("Widget pipe disconnected.");
            await channel.WriteAsync(new RuntimeEnvelope
            {
                Type = type,
                Payload = RuntimeJson.ToElement(payload),
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void RemovePendingDashboardGesture(
        long inputSequence,
        long snapshotSequence)
    {
        lock (_dashboardGestureGate)
            _pendingDashboardGestures.Remove(
                new DashboardGestureKey(inputSequence, snapshotSequence));
    }

    private void ClearPendingDashboardGestures()
    {
        lock (_dashboardGestureGate) _pendingDashboardGestures.Clear();
    }

    private void PurgeExpiredDashboardGesturesNoLock(long nowTimestamp)
    {
        foreach (var expired in _pendingDashboardGestures
                     .Where(item => _timeProvider.GetElapsedTime(
                         item.Value.ReservedAtTimestamp, nowTimestamp) >=
                         MaximumDashboardGestureReservationLifetime)
                     .Select(item => item.Key)
                     .ToArray())
            _pendingDashboardGestures.Remove(expired);
    }

    private readonly record struct DashboardGestureKey(
        long InputSequence,
        long SnapshotSequence);

    private sealed record PendingDashboardGesture(
        WidgetDashboardGestureAuthority Authority,
        long ReservedAtTimestamp);

    private async Task RevokeCompanionGestureAuthorityAsync(long inputSequence)
    {
        var companion = _companion;
        if (companion is null) return;
        try
        {
            await companion.RevokeDashboardGestureAuthorityAsync(inputSequence)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Revocation is defense in depth after an unhandled/failed action;
            // companion disposal and the broker's bounded expiry remain the
            // fail-closed backstop if this best-effort call races teardown.
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
