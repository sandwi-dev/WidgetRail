using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

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
    private readonly WidgetProcessClientTestHooks? _testHooks;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private WidgetProcessSession? _session;
    private ViewSnapshot? _materializedSnapshot;
    private WidgetLifecycleState _hostLifecycle = WidgetLifecycleState.Background;
    private int _starts;
    private int _restartAttempts;
    private int _failureReported;
    private bool _residencyUnloaded;
    private bool _stopping;
    private bool _disposed;

    public WidgetProcessClient(WidgetProcessOptions options)
        : this(options, TimeProvider.System, testHooks: null)
    {
    }

    internal WidgetProcessClient(
        WidgetProcessOptions options,
        TimeProvider timeProvider,
        WidgetProcessClientTestHooks? testHooks = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHooks = testHooks;
        _options.Validate();
    }

    public event EventHandler<long>? Invalidated;
    public event EventHandler<WidgetActionFailure>? ActionFailed;
    public event EventHandler<WidgetControllerActionFailure>? ControllerActionFailed;
    public event EventHandler<WidgetFailure>? Failed;
    internal event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged;

    public bool IsRunning
    {
        get
        {
            return Volatile.Read(ref _session)?.IsRunning == true;
        }
    }

    public int Starts => Volatile.Read(ref _starts);
    internal int? WorkerProcessId => Volatile.Read(ref _session)?.Process?.Id;
    internal WindowsWorkerJobAccounting? AppliedJobAccounting =>
        Volatile.Read(ref _session)?.WindowsJob?.Accounting;
    internal long? AppliedJobMemoryLimitBytes =>
        Volatile.Read(ref _session)?.WindowsJob?.MemoryLimitBytes;
    internal uint? AppliedJobActiveProcessLimit =>
        Volatile.Read(ref _session)?.WindowsJob?.ActiveProcessLimit;

    public async Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        (await GetPresentationAsync(
            PresentationUpdateCapabilities.None,
            presentationGeneration: null,
            baseSequence: 0,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            recoveryOriginSequence: 0,
            cancellationToken).ConfigureAwait(false)).Snapshot;

    internal async Task<WidgetRuntimePresentation> GetPresentationAsync(
        PresentationUpdateCapabilities capabilities,
        string? presentationGeneration,
        long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long recoveryOriginSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        await _presentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = _materializedSnapshot;
            var incrementalCurrent = transactionKind ==
                WidgetPresentationTransactionKind.IncrementalUpdate &&
                capabilities.SupportsAtomicUpdates &&
                cached is not null && cached.Sequence == baseSequence &&
                IsPresentationGeneration(presentationGeneration);
            var checkpoint = transactionKind is
                WidgetPresentationTransactionKind.OrdinaryCheckpoint or
                WidgetPresentationTransactionKind.RecoveryCheckpoint;
            if ((!incrementalCurrent && !checkpoint) ||
                (transactionKind == WidgetPresentationTransactionKind.OrdinaryCheckpoint &&
                    recoveryOriginSequence != 0) ||
                (transactionKind == WidgetPresentationTransactionKind.RecoveryCheckpoint &&
                    recoveryOriginSequence <= 0))
                throw new WidgetProtocolViolationException(
                    "Presentation transaction authority is incomplete or malformed.");
            var request = await RequestWithSessionAsync(
                MessageTypes.Render,
                new RenderPayload
                {
                    UpdateCapabilities = incrementalCurrent
                        ? capabilities : PresentationUpdateCapabilities.None,
                    BaseSequence = incrementalCurrent
                        ? baseSequence
                        : transactionKind ==
                            WidgetPresentationTransactionKind.RecoveryCheckpoint
                            ? recoveryOriginSequence
                            : 0,
                    PresentationGeneration = incrementalCurrent
                        ? presentationGeneration : null,
                    RequireCheckpoint = !incrementalCurrent,
                },
                cancellationToken).ConfigureAwait(false);
            var response = request.Response;
            try
            {
                if (response.Type == MessageTypes.Snapshot)
                {
                    var snapshot = SnapshotJson.Deserialize(
                        Encoding.UTF8.GetBytes(response.Payload.GetRawText()));
                    DemandWidgetInstance(snapshot.WidgetInstanceId);
                    _materializedSnapshot = snapshot;
                    return new(
                        transactionKind, incrementalCurrent ? baseSequence : 0,
                        recoveryOriginSequence, snapshot, null);
                }
                if (response.Type == MessageTypes.PresentationUpdate && incrementalCurrent)
                {
                    var update = PresentationUpdateJson.Deserialize(
                        Encoding.UTF8.GetBytes(response.Payload.GetRawText()));
                    var snapshot = PresentationUpdateMaterializer.Apply(
                        cached!, update, presentationGeneration!);
                    DemandWidgetInstance(snapshot.WidgetInstanceId);
                    _materializedSnapshot = snapshot;
                    return new(
                        transactionKind, baseSequence, recoveryOriginSequence,
                        snapshot, update);
                }
                throw new WidgetProtocolViolationException(
                    $"Expected snapshot or negotiated update, received '{response.Type}'.");
            }
            catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
            {
                if (TryBeginCurrentPublication(
                        request.Session, "snapshot-invalid", out var publication))
                {
                    using (publication)
                        ReportFailure(WidgetFailureReason.ProtocolViolation, exception);
                }
                request.Session.Terminate();
                throw new WidgetProtocolViolationException(
                    "Worker returned an invalid presentation.", exception);
            }
        }
        finally
        {
            _presentationGate.Release();
        }

        void DemandWidgetInstance(string widgetInstanceId)
        {
            if (!string.Equals(widgetInstanceId, _options.WidgetInstanceId, StringComparison.Ordinal))
                throw new WidgetProtocolViolationException(
                    "Presentation belongs to a different widget instance.");
        }
    }

    private static bool IsPresentationGeneration(string? value) =>
        value is { Length: 32 or 64 } && value.All(char.IsAsciiHexDigit);

    /// <summary>
    /// Transitions the worker to a host-owned stable lifecycle state.
    /// Requesting Background for an unstarted worker is a no-op and stays lazy.
    /// </summary>
    public async Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidateHostState(state);
        RecordLifetime(WidgetProcessLifetimeEventKind.LifecycleRequested, state);
        try
        {
            if (state != _hostLifecycle)
                Volatile.Read(ref _session)?.GestureReservations.Clear();
            if (state == WidgetLifecycleState.Background && !IsRunning)
            {
                _hostLifecycle = state;
                RecordLifetime(WidgetProcessLifetimeEventKind.LifecycleCompleted, state);
                return;
            }
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var session = Volatile.Read(ref _session) ??
                throw new IOException("Widget pipe disconnected.");
            await SetLifecycleStateOnSessionAsync(session, state, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordLifetime(
                WidgetProcessLifetimeEventKind.LifecycleFailed,
                state,
                failureCode: ClassifyLifecycleFailure(exception));
            throw;
        }
    }

    /// <summary>
    /// Best-effort transaction compensation against an exact still-running worker.
    /// This never starts or updates a replacement process.
    /// </summary>
    internal async Task<bool> TryRestoreLifecycleStateAsync(
        WidgetLifecycleState state,
        int expectedStartOrdinal,
        CancellationToken cancellationToken = default)
    {
        ValidateHostState(state);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _stopping || Starts != expectedStartOrdinal)
                return false;
            var session = Volatile.Read(ref _session);
            if (session?.IsRunning != true)
                return false;

            RecordLifetime(WidgetProcessLifetimeEventKind.LifecycleRequested, state, session);
            try
            {
                if (state != _hostLifecycle)
                    session.GestureReservations.Clear();
                await SetLifecycleStateOnSessionAsync(session, state, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                RecordLifetime(
                    WidgetProcessLifetimeEventKind.LifecycleFailed,
                    state,
                    session,
                    failureCode: ClassifyLifecycleFailure(exception));
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
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

    internal async Task<WidgetEncodedArtwork?> ResolveArtworkAsync(
        string artworkHandle,
        CancellationToken cancellationToken = default)
    {
        StableIdentifier.Validate(artworkHandle, nameof(artworkHandle));
        var response = await RequestAsync(
            MessageTypes.ResolveArtwork,
            new ResolveArtworkPayload(artworkHandle),
            cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Artwork)
            throw new WidgetProtocolViolationException(
                $"Expected artwork, received '{response.Type}'.");
        var payload = RuntimeJson.FromElement<EncodedArtworkPayload>(response.Payload);
        if (payload.ContentType is null && payload.ContentBase64 is null) return null;
        if (payload.ContentType is null || payload.ContentBase64 is null ||
            WidgetEncodedArtworkContract.ParseContentType(payload.ContentType) is not { } contentType)
            throw new WidgetProtocolViolationException("Worker returned invalid artwork metadata.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(payload.ContentBase64); }
        catch (FormatException exception)
        {
            throw new WidgetProtocolViolationException(
                "Worker returned malformed artwork bytes.", exception);
        }
        var artwork = new WidgetEncodedArtwork(contentType, bytes);
        if (!WidgetEncodedArtworkContract.IsValid(artwork))
            throw new WidgetProtocolViolationException("Worker returned invalid artwork bytes.");
        return artwork;
    }

    public async Task SendEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playbackEvent);
        var response = await RequestAsync(
            MessageTypes.EmbeddedMediaPlaybackEvent, playbackEvent, cancellationToken)
            .ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException(
                $"Expected acknowledgement, received '{response.Type}'.");
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
        if (markResidencyUnload)
            RecordLifetime(WidgetProcessLifetimeEventKind.CooperativeUnloadRequested);
        _stopping = true;
        var lifecycleEntered = false;
        var lifecycleDrainTimeout = _testHooks?.LifecycleDrainTimeout ?? TimeSpan.FromSeconds(6);
        using var lifecycleDeadline = new CancellationTokenSource(lifecycleDrainTimeout);
        try
        {
            lifecycleEntered = await _lifecycleGate.WaitAsync(
                lifecycleDrainTimeout, lifecycleDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        var stopAcknowledged = false;
        var session = Volatile.Read(ref _session);
        if (lifecycleEntered && session?.Companion is not null)
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
        if (lifecycleEntered && session?.IsRunning == true)
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
        try
        {
            if (markResidencyUnload && stopAcknowledged)
                RecordLifetime(
                    WidgetProcessLifetimeEventKind.CooperativeUnloadCompleted,
                    WidgetLifecycleState.Background,
                    session);
            await DisposeSessionAsync(session, cancellationToken).ConfigureAwait(false);
            _hostLifecycle = WidgetLifecycleState.Background;
            _residencyUnloaded = markResidencyUnload && stopAcknowledged;
        }
        finally
        {
            if (lifecycleEntered) _lifecycleGate.Release();
        }
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
        => (await RequestWithSessionAsync(type, payload, cancellationToken)
            .ConfigureAwait(false)).Response;

    private async Task<(WidgetProcessSession Session, RuntimeEnvelope Response)>
        RequestWithSessionAsync<T>(
            string type, T payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var session = Volatile.Read(ref _session) ??
            throw new IOException("Widget pipe disconnected.");
        var response = await RequestConnectedAsync(session, type, payload, cancellationToken)
            .ConfigureAwait(false);
        return (session, response);
    }

    private async Task<RuntimeEnvelope> RequestConnectedAsync<T>(
        WidgetProcessSession session,
        string type, T payload, CancellationToken cancellationToken)
    {
        using var request = session.PendingRequests.Register(type);
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
            if (TryBeginCurrentPublication(
                    session, "request-timeout", out var publication))
            {
                using (publication)
                    ReportFailure(WidgetFailureReason.RequestTimedOut, exception);
            }
            session.Terminate();
            throw exception;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? startupDeadline = null;
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
            _stopping = false;
            Interlocked.Exchange(ref _failureReported, 0);
            startupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupDeadline.CancelAfter(_options.ConnectTimeout);
            var startupToken = startupDeadline.Token;
            var currentSession = new WidgetProcessSession(
                _timeProvider,
                MaximumDashboardGestureReservationLifetime);
            Volatile.Write(ref _session, currentSession);
            var workerDiagnosticPath = WidgetWorkerDiagnosticPath.TryPrepare(
                _options.WorkerDiagnosticRoot,
                _options.WidgetInstanceId);
            if (_options.ProcessLeaseFactory is { } leaseFactory)
            {
                currentSession.AttachProcessLease(
                    leaseFactory() ?? throw new WidgetProcessAdmissionException(
                        "Worker process admission returned no lease."));
                startupToken.ThrowIfCancellationRequested();
            }
            if (_options.ContentLeaseFactory is { } contentLeaseFactory)
            {
                using var contentTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    startupToken);
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
                    !startupToken.IsCancellationRequested &&
                    contentTimeout.IsCancellationRequested)
                {
                    throw new WidgetProcessAdmissionException(
                        "Worker content admission exceeded its time limit.");
                }
            }
            startupToken.ThrowIfCancellationRequested();
            using var appContainer = _options.IsolationPolicy ==
                WidgetWorkerIsolationPolicy.RequireAppContainer
                    ? WindowsAppContainer.OpenOrCreate(_options.IsolationKey!)
                    : null;
            startupToken.ThrowIfCancellationRequested();
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
                if (workerDiagnosticPath is not null)
                {
                    try
                    {
                        appContainer.GrantModify(Path.GetDirectoryName(workerDiagnosticPath)!);
                    }
                    catch (Exception exception) when (exception is IOException or
                        UnauthorizedAccessException or NotSupportedException)
                    {
                        workerDiagnosticPath = null;
                    }
                }
                startupToken.ThrowIfCancellationRequested();
                if (currentSession.ContentLease is not null)
                {
                    appContainer.ReplaceReadAndExecuteGrant(
                        currentSession.ContentLease.Targets,
                        _options.ContentAuthorityOperations,
                        _options.ContentAuthorityJournal);
                    startupToken.ThrowIfCancellationRequested();
                }
            }
            var pipeSuffix = $"wrail-widget-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var pipeName = pipeSuffix;
            var sessionNonce = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var serverPipeName = pipeName;
            var pipe = appContainer is null
                ? new NamedPipeServerStream(
                    serverPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    4096, 4096)
                : appContainer.CreatePipe(serverPipeName, 4096);
            var channel = new LengthPrefixedJsonChannel(pipe, _options.MaximumMessageBytes);
            currentSession.AttachTransport(pipe, channel);
            startupToken.ThrowIfCancellationRequested();

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
                startupToken.ThrowIfCancellationRequested();
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
            startInfo.ArgumentList.Add("--widget-session-nonce");
            startInfo.ArgumentList.Add(sessionNonce);
            startInfo.ArgumentList.Add("--max-message-bytes");
            startInfo.ArgumentList.Add(_options.MaximumMessageBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (workerDiagnosticPath is not null)
            {
                startInfo.ArgumentList.Add("--worker-diagnostics-path");
                startInfo.ArgumentList.Add(workerDiagnosticPath);
            }

            if (_testHooks?.BeforeProcessStartAsync is { } beforeProcessStart)
                await beforeProcessStart(startupToken).ConfigureAwait(false);
            startupToken.ThrowIfCancellationRequested();
            currentSession.StartProcess(() =>
            {
                if (!OperatingSystem.IsWindows())
                    return (
                        Process.Start(startInfo) ??
                            throw new WidgetProcessException("Worker process did not start."),
                        null);
                var job = WindowsWorkerJob.Create(
                    restrictUi: _options.IsolationPolicy !=
                        WidgetWorkerIsolationPolicy.FullTrustCommunity);
                try { return (job.StartProcess(startInfo, appContainer), job); }
                catch
                {
                    job.Dispose();
                    throw;
                }
            });
            startupToken.ThrowIfCancellationRequested();
            if (currentSession.Companion is not null)
            {
                currentSession.Companion.BindWorkerProcess(currentSession.Process!.Id);
                currentSession.StartCompanion();
            }
            currentSession.Process!.EnableRaisingEvents = true;
            currentSession.Process.Exited += (_, _) => OnProcessExited(currentSession);
            if (isRestart && !isResidencyResume)
                Interlocked.Increment(ref _restartAttempts);
            var startOrdinal = Interlocked.Increment(ref _starts);
            RecordLifetime(
                WidgetProcessLifetimeEventKind.WorkerStarted,
                session: currentSession,
                startOrdinal: startOrdinal);

            await pipe.WaitForConnectionAsync(startupToken).ConfigureAwait(false);
            if (OperatingSystem.IsWindows())
                WindowsAppContainer.VerifyPipeClientProcess(
                    pipe,
                    currentSession.Process?.Id ??
                        throw new WidgetProcessException("Worker process identity is unavailable."));
            var hello = await channel.ReadAsync(startupToken).ConfigureAwait(false);
            if (hello.Type != MessageTypes.Hello || hello.RequestId != 0)
                throw new WidgetProtocolViolationException("Worker handshake was malformed.");
            var helloPayload = RuntimeJson.FromElement<HelloPayload>(hello.Payload);
            if (!string.Equals(helloPayload.WidgetInstanceId, _options.WidgetInstanceId, StringComparison.Ordinal))
                throw new WidgetProtocolViolationException("Worker handshake used the wrong instance ID.");
            if (helloPayload.SessionNonce is not { Length: 64 } ||
                !helloPayload.SessionNonce.All(char.IsAsciiHexDigit))
                throw new WidgetProtocolViolationException(
                    "Worker handshake used an invalid session nonce.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(helloPayload.SessionNonce),
                    Encoding.ASCII.GetBytes(sessionNonce)))
                throw new WidgetProtocolViolationException("Worker handshake used the wrong session nonce.");
            await currentSession.WriteAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.HelloAccepted,
                Payload = RuntimeJson.ToElement(new { }),
            }, startupToken).ConfigureAwait(false);
            currentSession.MarkHandshakeCompleted();
            currentSession.StartReader(token =>
                ReadResponsesAsync(currentSession, channel, token));
            if (_hostLifecycle != WidgetLifecycleState.Background)
            {
                await SetCompanionLifecycleAsync(
                    currentSession, _hostLifecycle, startupToken).ConfigureAwait(false);
                var lifecycleResponse = await RequestConnectedAsync(
                    currentSession, MessageTypes.SetWidgetLifecycle,
                    new WidgetLifecyclePayload(_hostLifecycle), startupToken)
                    .ConfigureAwait(false);
                if (lifecycleResponse.Type != MessageTypes.Acknowledged)
                    throw new WidgetProtocolViolationException(
                        $"Expected lifecycle acknowledgement, received '{lifecycleResponse.Type}'.");
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            startupDeadline?.IsCancellationRequested == true)
        {
            var exception = new WidgetProcessException(
                "Widget worker startup exceeded its time limit.");
            if (!_stopping)
                ReportFailure(WidgetFailureReason.ConnectionFailed, exception);
            var session = Volatile.Read(ref _session);
            session?.Terminate();
            await DisposeSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            throw exception;
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
            var startupDiagnostic = ResolveStartupDiagnostic(exitCode);
            var reportedException = startupDiagnostic is null
                ? exception
                : new WidgetProcessException(
                    $"Widget worker startup failed ({startupDiagnostic}).");
            if (!_stopping)
                ReportFailure(exception is WidgetProtocolViolationException
                    ? WidgetFailureReason.ProtocolViolation
                    : WidgetFailureReason.ConnectionFailed,
                    reportedException,
                    startupDiagnostic);
            var session = Volatile.Read(ref _session);
            session?.Terminate();
            await DisposeSessionAsync(session, cancellationToken).ConfigureAwait(false);
            throw new WidgetProcessException(
                startupDiagnostic is not null
                    ? $"Widget worker startup failed ({startupDiagnostic})."
                    : exitCode is null
                    ? "Widget worker connection failed."
                    : $"Widget worker exited with code {exitCode} before connecting.",
                exception);
        }
        finally
        {
            startupDeadline?.Dispose();
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
                    if (!TryBeginCurrentPublication(
                            session, "invalidated", out var publication)) return;
                    using (publication) Invalidated?.Invoke(this, payload.Revision);
                    continue;
                }

                if (message.Type == MessageTypes.ControllerActionFailed)
                {
                    if (message.RequestId != 0)
                        throw new WidgetProtocolViolationException("Notifications cannot have request IDs.");
                    var payload = RuntimeJson.FromElement<ControllerActionFailurePayload>(message.Payload);
                    var actionFailure = new WidgetActionFailure(
                        payload.ActionId, payload.SourceElementId, payload.Message);
                    if (!TryBeginCurrentPublication(
                            session, "action-failed", out var publication)) return;
                    using (publication)
                    {
                        ActionFailed?.Invoke(this, actionFailure);
                        ControllerActionFailed?.Invoke(this, new WidgetControllerActionFailure(
                            actionFailure.ActionId, actionFailure.SourceElementId,
                            actionFailure.Message));
                    }
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

                _testHooks?.BeforeResponseCorrelation?.Invoke(message.Type);
                if (!ReferenceEquals(session, Volatile.Read(ref _session)) ||
                    session.IsTerminal)
                {
                    _testHooks?.ResponseCorrelationCompleted?.Invoke(message.Type, false);
                    return;
                }
                var completed = session.PendingRequests.TryComplete(message);
                _testHooks?.ResponseCorrelationCompleted?.Invoke(message.Type, completed);
                if (!completed)
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
                if (TryBeginCurrentPublication(
                        session, "transport-failed", out var publication))
                {
                    using (publication)
                        ReportFailure(exception is WidgetProtocolViolationException
                            ? WidgetFailureReason.ProtocolViolation
                            : WidgetFailureReason.TransportFailure, exception);
                }
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
        if (!ReferenceEquals(session, Volatile.Read(ref _session))) return;
        var exitCode = TryGetExitCode(session);
        RecordLifetime(
            WidgetProcessLifetimeEventKind.ProcessExited,
            session: session,
            exitCode: exitCode,
            failureCode: _stopping ? "cooperative-stop" : "unexpected-exit");
        if (_stopping) return;
        session.ReleaseLeases();
        if (TryBeginCurrentPublication(session, "process-exited", out var publication))
        {
            var startupDiagnostic = session.HandshakeCompleted
                ? null
                : ResolveStartupDiagnostic(session.ExitCode);
            using (publication) ReportFailure(
                session.HandshakeCompleted
                    ? WidgetFailureReason.ProcessExited
                    : WidgetFailureReason.ConnectionFailed,
                startupDiagnostic is null
                    ? null
                    : new WidgetProcessException(
                        $"Widget worker startup failed ({startupDiagnostic})."),
                startupDiagnostic);
        }
        session.PendingRequests.FailAll(
            new WidgetProcessException("Widget worker exited unexpectedly."));
        session.GestureReservations.Clear();
        session.Cancel();
    }

    private void ReportFailure(
        WidgetFailureReason reason,
        Exception? exception,
        string? diagnosticCode = null)
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
            restartsUsed < _options.MaximumRestartAttempts,
            diagnosticCode));
    }

    private string? ResolveStartupDiagnostic(int? exitCode) =>
        exitCode is { } value &&
        _options.StartupExitDiagnostics.TryGetValue(value, out var diagnostic)
            ? diagnostic
            : null;

    private void RecordLifetime(
        WidgetProcessLifetimeEventKind kind,
        WidgetLifecycleState? state = null,
        WidgetProcessSession? session = null,
        int? exitCode = null,
        string? failureCode = null,
        int? startOrdinal = null)
    {
        session ??= Volatile.Read(ref _session);
        WidgetProcessLifetimeDiagnostic diagnostic;
        try
        {
            diagnostic = new WidgetProcessLifetimeDiagnostic(
                kind,
                startOrdinal ?? Volatile.Read(ref _starts),
                session?.Process?.Id,
                state,
                exitCode,
                failureCode);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }
        PublishLifetimeDiagnostic(diagnostic, value => LifetimeChanged?.Invoke(this, value));
    }

    private static void PublishLifetimeDiagnostic(
        WidgetProcessLifetimeDiagnostic diagnostic,
        Action<WidgetProcessLifetimeDiagnostic>? sink)
    {
        if (sink is null) return;
        try { sink(diagnostic); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Diagnostics are observational and never own worker lifetime.
        }
    }

    private static int? TryGetExitCode(WidgetProcessSession session)
    {
        try { return session.ExitCode; }
        catch (InvalidOperationException) { return null; }
    }

    private static string ClassifyLifecycleFailure(Exception exception) => exception switch
    {
        OperationCanceledException => "request-cancelled",
        TimeoutException => "request-timeout",
        WidgetProtocolViolationException => "protocol-violation",
        IOException => "transport-failure",
        WidgetProcessAdmissionException => "admission-failure",
        WidgetProcessException => "runtime-failure",
        _ => "lifecycle-failure",
    };

    private async Task DisposeSessionAsync(
        WidgetProcessSession? session,
        CancellationToken cancellationToken = default)
    {
        if (session is null) return;
        Interlocked.CompareExchange(ref _session, null, session);
        var terminal = session.DisposeAsync(cancellationToken);
        _testHooks?.SessionTerminalStarted?.Invoke();
        await terminal.ConfigureAwait(false);
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
            if (TryBeginCurrentPublication(
                    session, "companion-failed", out var publication))
            {
                using (publication)
                    ReportFailure(WidgetFailureReason.TransportFailure, exception);
            }
            session.Terminate();
            throw new WidgetProcessException("Widget companion lifecycle update failed.", exception);
        }
    }

    private async Task SetLifecycleStateOnSessionAsync(
        WidgetProcessSession session,
        WidgetLifecycleState state,
        CancellationToken cancellationToken)
    {
        await SetCompanionLifecycleAsync(session, state, cancellationToken).ConfigureAwait(false);
        var response = await RequestConnectedAsync(
            session, MessageTypes.SetWidgetLifecycle,
            new WidgetLifecyclePayload(state),
            cancellationToken).ConfigureAwait(false);
        if (response.Type != MessageTypes.Acknowledged)
            throw new WidgetProtocolViolationException(
                $"Expected lifecycle acknowledgement, received '{response.Type}'.");
        _hostLifecycle = state;
        RecordLifetime(WidgetProcessLifetimeEventKind.LifecycleCompleted, state, session);
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

        if (!TryBeginCurrentPublication(session, "gesture", out var publication)) return;
        using (publication)
        {
            var authority = session.GestureReservations.Take(activation);

            var authorized = false;
            if (authority is not null)
            {
                var grantCompanion = session.Companion;
                var grant = GrantCompanionGestureAuthorityAsync(
                    session, authority, cancellationToken);
                try
                {
                    await grant.WaitAsync(cancellationToken).ConfigureAwait(false);
                    authorized = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _ = RevokeLateGestureGrantAsync(
                        grant, grantCompanion, authority.InputSequence);
                    return;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
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
    }

    private async Task RevokeLateGestureGrantAsync(
        Task grant,
        IWidgetProcessCompanionSession? companion,
        long inputSequence)
    {
        try { await grant.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return; }
        await RevokeCompanionGestureAuthorityAsync(companion, inputSequence).ConfigureAwait(false);
    }

    private bool TryBeginCurrentPublication(
        WidgetProcessSession session,
        string kind,
        out IDisposable publication)
    {
        publication = null!;
        _testHooks?.BeforePublicationAdmission?.Invoke(kind);
        if (!ReferenceEquals(session, Volatile.Read(ref _session)) ||
            !session.TryBeginPublication(out publication))
        {
            _testHooks?.PublicationAdmissionCompleted?.Invoke(kind, false);
            return false;
        }
        if (ReferenceEquals(session, Volatile.Read(ref _session)))
        {
            _testHooks?.PublicationAdmissionCompleted?.Invoke(kind, true);
            return true;
        }
        publication.Dispose();
        publication = null!;
        _testHooks?.PublicationAdmissionCompleted?.Invoke(kind, false);
        return false;
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
        long inputSequence) =>
        await RevokeCompanionGestureAuthorityAsync(session.Companion, inputSequence)
            .ConfigureAwait(false);

    private static async Task RevokeCompanionGestureAuthorityAsync(
        IWidgetProcessCompanionSession? companion,
        long inputSequence)
    {
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

internal sealed class WidgetProcessClientTestHooks
{
    internal TimeSpan LifecycleDrainTimeout { get; init; } = TimeSpan.FromSeconds(6);
    internal Func<CancellationToken, Task>? BeforeProcessStartAsync { get; init; }
    internal Action? SessionTerminalStarted { get; init; }
    internal Action<string>? BeforePublicationAdmission { get; init; }
    internal Action<string, bool>? PublicationAdmissionCompleted { get; init; }
    internal Action<string>? BeforeResponseCorrelation { get; init; }
    internal Action<string, bool>? ResponseCorrelationCompleted { get; init; }
}

public sealed class WidgetProcessException : Exception
{
    public WidgetProcessException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    internal WidgetProcessException(
        string requestType,
        string workerErrorCode,
        string workerDiagnosticMessage)
        : base(FormatWorkerRejection(requestType, workerErrorCode, workerDiagnosticMessage))
    {
        RequestType = ValidateToken(requestType, nameof(requestType));
        WorkerErrorCode = ValidateToken(workerErrorCode, nameof(workerErrorCode));
        WorkerDiagnosticMessage = ValidateDiagnostic(workerDiagnosticMessage);
    }

    internal WidgetProcessException(
        long workerRequestId,
        string requestType,
        string workerErrorCode,
        string workerDiagnosticMessage)
        : base(FormatWorkerRejection(requestType, workerErrorCode, workerDiagnosticMessage))
    {
        WorkerRequestId = workerRequestId > 0
            ? workerRequestId
            : throw new WidgetProtocolViolationException(
                "Worker error request ID is invalid.");
        RequestType = ValidateToken(requestType, nameof(requestType));
        WorkerErrorCode = ValidateToken(workerErrorCode, nameof(workerErrorCode));
        WorkerDiagnosticMessage = ValidateDiagnostic(workerDiagnosticMessage);
    }

    internal string? RequestType { get; }
    internal long? WorkerRequestId { get; }
    internal string? WorkerErrorCode { get; }
    internal string? WorkerDiagnosticMessage { get; }

    private static string FormatWorkerRejection(
        string requestType,
        string workerErrorCode,
        string workerDiagnosticMessage)
    {
        _ = ValidateDiagnostic(workerDiagnosticMessage);
        return $"Worker rejected request '{ValidateToken(requestType, nameof(requestType))}' " +
            $"({ValidateToken(workerErrorCode, nameof(workerErrorCode))}).";
    }

    private static string ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.'))
            throw new WidgetProtocolViolationException(
                $"Worker error {parameterName} is invalid.");
        return value;
    }

    private static string ValidateDiagnostic(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            value.Any(character => char.IsControl(character) && character is not '\t'))
            throw new WidgetProtocolViolationException(
                "Worker error message is invalid.");
        return value;
    }
}
