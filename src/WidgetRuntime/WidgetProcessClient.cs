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
    // A dormant reservation is not broker authority. It exists only long
    // enough for bounded serial widget work to reach its exact capability call.
    // The separate broker lease remains at most two seconds and starts only then.
    private static readonly TimeSpan MaximumDashboardGestureReservationLifetime =
        TimeSpan.FromSeconds(10);
    private readonly WidgetProcessOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WidgetProcessSession? _session;
    private WidgetLifecycleState _hostLifecycle = WidgetLifecycleState.Background;
    private int _starts;
    private int _restartAttempts;
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
            return Volatile.Read(ref _session)?.IsRunning == true;
        }
    }

    public int Starts => Volatile.Read(ref _starts);
    internal int? WorkerProcessId => Volatile.Read(ref _session)?.Process?.Id;
    internal long? AppliedJobMemoryLimitBytes =>
        Volatile.Read(ref _session)?.WindowsJob?.MemoryLimitBytes;
    internal uint? AppliedJobActiveProcessLimit =>
        Volatile.Read(ref _session)?.WindowsJob?.ActiveProcessLimit;

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
            Volatile.Read(ref _session)?.Terminate();
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
        if (state != _hostLifecycle)
            Volatile.Read(ref _session)?.GestureReservations.Clear();
        if (state == WidgetLifecycleState.Background && !IsRunning)
        {
            _hostLifecycle = state;
            return;
        }
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var session = Volatile.Read(ref _session) ??
            throw new IOException("Widget pipe disconnected.");
        await SetCompanionLifecycleAsync(session, state, cancellationToken).ConfigureAwait(false);
        var response = await RequestConnectedAsync(
            session, MessageTypes.SetWidgetLifecycle,
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
        if (!Enum.IsDefined(input.Origin))
            throw new ArgumentOutOfRangeException(
                nameof(input), input.Origin, "Controller input origin is invalid.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var session = Volatile.Read(ref _session) ??
            throw new IOException("Widget pipe disconnected.");
        var authorityMayRemain = false;
        if (authority is not null) session.GestureReservations.Reserve(input, authority);
        try
        {
            var response = await RequestConnectedAsync(
                    session, MessageTypes.ControllerInput, input, cancellationToken)
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
                session.GestureReservations.Remove(
                    authority.InputSequence, authority.SnapshotSequence);
                await RevokeCompanionGestureAuthorityAsync(
                        session, authority.InputSequence)
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
        var session = Volatile.Read(ref _session);
        if (session?.Companion is not null)
        {
            try
            {
                await session.Companion.SetLifecycleStateAsync(
                    WidgetLifecycleState.Destroying, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Worker teardown remains bounded even if its companion already disconnected.
            }
        }
        if (session?.IsRunning == true)
        {
            try
            {
                var response = await RequestConnectedAsync(
                        session, MessageTypes.Stop, new { }, cancellationToken)
                    .ConfigureAwait(false);
                if (response.Type != MessageTypes.Acknowledged)
                    throw new WidgetProtocolViolationException(
                        $"Expected stop acknowledgement, received '{response.Type}'.");
                stopAcknowledged = true;
                var process = session.Process;
                if (process is not null)
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                session.Terminate();
            }
        }
        await DisposeSessionAsync(session, cancellationToken).ConfigureAwait(false);
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
    }

    private async Task<RuntimeEnvelope> RequestAsync<T>(
        string type, T payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var session = Volatile.Read(ref _session) ??
            throw new IOException("Widget pipe disconnected.");
        return await RequestConnectedAsync(session, type, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RuntimeEnvelope> RequestConnectedAsync<T>(
        WidgetProcessSession session,
        string type, T payload, CancellationToken cancellationToken)
    {
        using var request = session.PendingRequests.Register();
        await session.WriteAsync(new RuntimeEnvelope
        {
            Type = type,
            RequestId = request.RequestId,
            Payload = RuntimeJson.ToElement(payload),
        }, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            return await request.Response.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var exception = new TimeoutException(
                $"Widget request '{type}' exceeded {_options.RequestTimeout.TotalMilliseconds:0} ms.");
            ReportFailure(WidgetFailureReason.RequestTimedOut, exception);
            session.Terminate();
            throw exception;
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

            var previousSession = Volatile.Read(ref _session);
            previousSession?.Terminate();
            await DisposeSessionAsync(previousSession, cancellationToken).ConfigureAwait(false);
            var currentSession = new WidgetProcessSession(
                _timeProvider,
                MaximumDashboardGestureReservationLifetime);
            Volatile.Write(ref _session, currentSession);
            if (_options.ProcessLeaseFactory is { } leaseFactory)
                currentSession.AttachProcessLease(
                    leaseFactory() ?? throw new WidgetProcessAdmissionException(
                        "Worker process admission returned no lease."));
            if (_options.ContentLeaseFactory is { } contentLeaseFactory)
            {
                using var contentTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                contentTimeout.CancelAfter(_options.ContentLeaseTimeout);
                try
                {
                    var contentLease = contentLeaseFactory(contentTimeout.Token) ??
                        throw new WidgetProcessAdmissionException(
                            "Worker content admission returned no lease.");
                    currentSession.AttachContentLease(contentLease);
                    contentTimeout.Token.ThrowIfCancellationRequested();
                    ValidateContentLease(contentLease);
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
            using var appContainer = _options.IsolationPolicy ==
                WidgetWorkerIsolationPolicy.RequireAppContainer
                    ? WindowsAppContainer.OpenOrCreate(_options.IsolationKey!)
                    : null;
            if (appContainer is not null)
            {
                var executableDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(_options.ExecutablePath))
                    ?? throw new WidgetProcessException("Worker executable directory is unavailable.");
                if (currentSession.ContentLease is not null &&
                    currentSession.ContentLease.Targets
                    .Where(target => target.Target.Kind ==
                        AppContainerAuthorityTargetKind.AuthorityRootDirectory)
                    .Select(target => target.Target.Path)
                    .Any(root =>
                        IsWithinOrEqual(executableDirectory, root) ||
                        IsWithinOrEqual(root, executableDirectory)))
                    throw new WidgetProcessAdmissionException(
                        "Worker content authority overlaps the trusted runtime directory.");
                appContainer.GrantReadAndExecute(
                    new[] { executableDirectory }.Concat(_options.ReadOnlyPaths));
                if (currentSession.ContentLease is not null)
                    appContainer.ReplaceReadAndExecuteGrant(
                        currentSession.ContentLease.Targets,
                        _options.ContentAuthorityOperations,
                        _options.ContentAuthorityJournal);
            }
            var pipeSuffix = $"gba-widget-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var pipeName = pipeSuffix;
            var serverPipeName = pipeName;
            var pipe = appContainer is null
                ? new NamedPipeServerStream(
                    serverPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    4096, 4096)
                : appContainer.CreatePipe(serverPipeName, 4096);
            var channel = new LengthPrefixedJsonChannel(pipe, _options.MaximumMessageBytes);
            currentSession.AttachTransport(pipe, channel);

            if (_options.CompanionSessionFactory is not null)
            {
                var companionContext = new WidgetProcessCompanionContext(
                    _options.IsolationPolicy,
                    _options.IsolationKey,
                    appContainer?.Sid);
                var companion = _options.CompanionSessionFactory(companionContext)
                    ?? throw new WidgetProcessException("Companion session factory returned null.");
                ValidateCompanionArguments(companion.WorkerArguments, companionContext);
                currentSession.AttachCompanion(companion);
            }

            var startInfo = new ProcessStartInfo(_options.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? Environment.CurrentDirectory,
            };
            foreach (var argument in _options.Arguments)
                startInfo.ArgumentList.Add(argument);
            if (currentSession.Companion is not null)
            {
                foreach (var argument in currentSession.Companion.WorkerArguments)
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
                    var process = job.StartProcess(startInfo, appContainer);
                    currentSession.AttachProcess(process, job);
                }
                catch
                {
                    job.Dispose();
                    throw;
                }
            }
            else
            {
                currentSession.AttachProcess(
                    Process.Start(startInfo) ??
                        throw new WidgetProcessException("Worker process did not start."),
                    windowsJob: null);
            }
            if (currentSession.Companion is not null)
            {
                currentSession.Companion.BindWorkerProcess(currentSession.Process!.Id);
                currentSession.StartCompanion();
            }
            currentSession.Process!.EnableRaisingEvents = true;
            currentSession.Process.Exited += (_, _) => OnProcessExited(currentSession);
            if (isRestart && !isResidencyResume)
                Interlocked.Increment(ref _restartAttempts);
            Interlocked.Increment(ref _starts);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ConnectTimeout);
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows())
                WindowsAppContainer.VerifyPipeClientProcess(
                    pipe,
                    currentSession.Process?.Id ??
                        throw new WidgetProcessException("Worker process identity is unavailable."));
            var hello = await channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (hello.Type != MessageTypes.Hello || hello.RequestId != 0)
                throw new WidgetProtocolViolationException("Worker handshake was malformed.");
            var helloPayload = RuntimeJson.FromElement<HelloPayload>(hello.Payload);
            if (!string.Equals(helloPayload.WidgetInstanceId, _options.WidgetInstanceId, StringComparison.Ordinal))
                throw new WidgetProtocolViolationException("Worker handshake used the wrong instance ID.");
            await currentSession.WriteAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.HelloAccepted,
                Payload = RuntimeJson.ToElement(new { }),
            }, timeout.Token).ConfigureAwait(false);
            currentSession.AttachReader(ReadResponsesAsync(
                currentSession, channel, currentSession.CancellationToken));
            if (_hostLifecycle != WidgetLifecycleState.Background)
            {
                await SetCompanionLifecycleAsync(
                    currentSession, _hostLifecycle, timeout.Token).ConfigureAwait(false);
                var lifecycleResponse = await RequestConnectedAsync(
                    currentSession, MessageTypes.SetWidgetLifecycle,
                    new WidgetLifecyclePayload(_hostLifecycle),
                    timeout.Token).ConfigureAwait(false);
                if (lifecycleResponse.Type != MessageTypes.Acknowledged)
                    throw new WidgetProtocolViolationException(
                        $"Expected lifecycle acknowledgement, received '{lifecycleResponse.Type}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var session = Volatile.Read(ref _session);
            session?.Terminate();
            await DisposeSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (WidgetProcessAdmissionException)
        {
            await DisposeSessionAsync(
                Volatile.Read(ref _session), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            int? exitCode = null;
            try
            {
                exitCode = Volatile.Read(ref _session)?.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }
            ReportFailure(exception is WidgetProtocolViolationException
                ? WidgetFailureReason.ProtocolViolation
                : WidgetFailureReason.ConnectionFailed, exception);
            var session = Volatile.Read(ref _session);
            session?.Terminate();
            await DisposeSessionAsync(session, cancellationToken).ConfigureAwait(false);
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
        WidgetProcessSession session,
        LengthPrefixedJsonChannel channel,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(session, Volatile.Read(ref _session)) || session.IsTerminal)
                    return;
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
                    await ActivatePendingDashboardGestureAsync(
                            session, activation, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!session.PendingRequests.TryComplete(message))
                    throw new WidgetProtocolViolationException("Response has an unknown request ID.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or JsonException or WidgetProtocolViolationException)
        {
            if (ReferenceEquals(session, Volatile.Read(ref _session)) && !_stopping)
            {
                ReportFailure(exception is WidgetProtocolViolationException
                    ? WidgetFailureReason.ProtocolViolation
                    : WidgetFailureReason.TransportFailure, exception);
                session.PendingRequests.FailAll(exception);
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

    private void OnProcessExited(WidgetProcessSession session)
    {
        if (!ReferenceEquals(session, Volatile.Read(ref _session)) || _stopping) return;
        session.ReleaseLeases();
        ReportFailure(WidgetFailureReason.ProcessExited, null);
        session.PendingRequests.FailAll(
            new WidgetProcessException("Widget worker exited unexpectedly."));
        session.GestureReservations.Clear();
        session.Cancel();
    }

    private void ReportFailure(WidgetFailureReason reason, Exception? exception)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) != 0) return;
        int? exitCode = null;
        try
        {
            exitCode = Volatile.Read(ref _session)?.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }
        var restartsUsed = Volatile.Read(ref _restartAttempts);
        Failed?.Invoke(this, new WidgetFailure(
            reason, exitCode, exception, restartsUsed,
            restartsUsed < _options.MaximumRestartAttempts));
    }

    private async Task DisposeSessionAsync(
        WidgetProcessSession? session,
        CancellationToken cancellationToken = default)
    {
        if (session is null) return;
        Interlocked.CompareExchange(ref _session, null, session);
        await session.DisposeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateContentLease(IWidgetProcessContentLease lease)
        => _ = WidgetProcessContentTargets.NormalizeAndValidate(lease.Targets);

    private static bool IsWithinOrEqual(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task SetCompanionLifecycleAsync(
        WidgetProcessSession session,
        WidgetLifecycleState state,
        CancellationToken cancellationToken)
    {
        if (session.Companion is null) return;
        try
        {
            await session.Companion.SetLifecycleStateAsync(state, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure(WidgetFailureReason.TransportFailure, exception);
            session.Terminate();
            throw new WidgetProcessException("Widget companion lifecycle update failed.", exception);
        }
    }

    private async Task GrantCompanionGestureAuthorityAsync(
        WidgetProcessSession session,
        WidgetDashboardGestureAuthority authority,
        CancellationToken cancellationToken)
    {
        var companion = session.Companion ?? throw new WidgetProcessException(
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

    private async Task ActivatePendingDashboardGestureAsync(
        WidgetProcessSession session,
        DashboardGestureActivationRequestPayload activation,
        CancellationToken cancellationToken)
    {
        if (activation.ActivationId <= 0 || activation.InputSequence <= 0 ||
            activation.SnapshotSequence <= 0 ||
            string.IsNullOrWhiteSpace(activation.CapabilityId) ||
            string.IsNullOrWhiteSpace(activation.OperationId))
            throw new WidgetProtocolViolationException(
                "Dashboard gesture activation payload is invalid.");

        var authority = session.GestureReservations.Take(activation);

        var authorized = false;
        if (authority is not null)
        {
            try
            {
                await GrantCompanionGestureAuthorityAsync(
                        session, authority, cancellationToken)
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
            session, MessageTypes.DashboardGestureActivationResult,
            new DashboardGestureActivationResultPayload(
                activation.ActivationId, authorized),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendConnectedNotificationAsync<T>(
        WidgetProcessSession session,
        string type,
        T payload,
        CancellationToken cancellationToken)
    {
        await session.WriteAsync(new RuntimeEnvelope
        {
            Type = type,
            Payload = RuntimeJson.ToElement(payload),
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task RevokeCompanionGestureAuthorityAsync(
        WidgetProcessSession session,
        long inputSequence)
    {
        var companion = session.Companion;
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
