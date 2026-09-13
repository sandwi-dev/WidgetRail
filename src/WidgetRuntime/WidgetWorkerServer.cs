using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetWorkerServer
{
    private readonly Widget _widget;
    private readonly string _widgetInstanceId;
    private readonly string _pipeName;
    private readonly string _sessionNonce;
    private readonly int _maximumMessageBytes;
    private readonly WidgetWorkerDiagnosticLog? _diagnostics;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>>
        _dashboardGestureActivations = new();
    private LengthPrefixedJsonChannel? _channel;
    private WidgetWorkerNotificationLane? _notificationLane;
    private CancellationToken _runCancellation;
    private long _sequence;
    private long _dashboardGestureActivationId;
    private bool _actionTerminalsEnabled;

    public WidgetWorkerServer(
        Widget widget,
        string widgetInstanceId,
        string pipeName,
        int maximumMessageBytes = WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
        IWidgetCapabilityClient? capabilityClient = null,
        string sessionNonce = "",
        WidgetWorkerDiagnosticLog? diagnostics = null)
        : this(
            widget,
            widgetInstanceId,
            pipeName,
            maximumMessageBytes,
            new WidgetHostServices(
                capabilityClient ?? UnavailableWidgetCapabilityClient.Instance),
            sessionNonce,
            diagnostics)
    {
    }

    internal WidgetWorkerServer(
        Widget widget,
        string widgetInstanceId,
        string pipeName,
        int maximumMessageBytes,
        WidgetHostServices hostServices,
        string sessionNonce,
        WidgetWorkerDiagnosticLog? diagnostics = null)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        _widgetInstanceId = ValidateIdentifier(widgetInstanceId);
        _pipeName = ValidatePipeName(pipeName);
        _sessionNonce = ValidateSessionNonce(sessionNonce);
        _maximumMessageBytes = maximumMessageBytes is >= 256 and <= WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes
            ? maximumMessageBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        _diagnostics = diagnostics;
        _widget.AttachHostServices(hostServices ?? throw new ArgumentNullException(nameof(hostServices)));
        if (hostServices.Capabilities is IDashboardGestureActivatingCapabilityClient activatingClient)
            activatingClient.SetDashboardGestureActivator(ActivateDashboardGestureAuthorityAsync);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        WidgetWorkerNotificationLane? notifications = null;
        await using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _channel = new LengthPrefixedJsonChannel(pipe, _maximumMessageBytes);
        _runCancellation = cancellationToken;

        await SendAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Hello,
            Payload = RuntimeJson.ToElement(new HelloPayload(_widgetInstanceId, _sessionNonce)),
        }, cancellationToken).ConfigureAwait(false);
        var acceptance = await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (acceptance.Type != MessageTypes.HelloAccepted || acceptance.RequestId != 0)
            throw new WidgetProtocolViolationException("Host did not accept the worker handshake.");
        _actionTerminalsEnabled = RuntimeJson.FromElement<HelloAcceptedPayload>(
            acceptance.Payload).SupportsActionTerminals;

        notifications = new WidgetWorkerNotificationLane(SendAsync, cancellationToken);
        _notificationLane = notifications;
        _widget.Invalidated += OnInvalidated;
        _widget.ActionFailed += OnActionFailed;
        _widget.ActionTerminated += OnActionTerminated;
        try
        {
            await _widget.InitializeAsync(cancellationToken).ConfigureAwait(false);
            using var requestLoopCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var requests = Channel.CreateBounded<RuntimeEnvelope>(new BoundedChannelOptions(
                Widget.ControllerActionQueueCapacity + 1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
            Exception? readerFailure = null;
            var pendingRequests = 0;
            var stopQueued = false;
            var processor = ProcessRequestsAsync(
                requests.Reader, requestLoopCancellation);
            try
            {
                while (!requestLoopCancellation.IsCancellationRequested)
                {
                    var request = await ReadRequestAsync(
                            _channel, notifications, requestLoopCancellation.Token)
                        .ConfigureAwait(false);
                    if (request.RequestId == 0 &&
                        request.Type == MessageTypes.DashboardGestureActivationResult)
                    {
                        CompleteDashboardGestureActivation(request.Payload);
                        continue;
                    }
                    if (request.RequestId == 0)
                        throw new WidgetProtocolViolationException(
                            "Host requests require a non-zero request ID.");

                    if (request.Type == MessageTypes.Stop)
                    {
                        if (stopQueued)
                        {
                            await ReplyAsync(MessageTypes.Error, request.RequestId,
                                    new ErrorPayload("worker_stopping", "Worker shutdown is already queued."),
                                    requestLoopCancellation.Token)
                                .ConfigureAwait(false);
                            continue;
                        }
                        stopQueued = true;
                        if (!requests.Writer.TryWrite(request))
                            throw new WidgetProtocolViolationException(
                                "The bounded worker request queue rejected its reserved stop slot.");
                        continue;
                    }

                    if (stopQueued)
                    {
                        await ReplyAsync(MessageTypes.Error, request.RequestId,
                                new ErrorPayload("worker_stopping", "Worker shutdown is already queued."),
                                requestLoopCancellation.Token)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (Interlocked.Increment(ref pendingRequests) >
                        Widget.ControllerActionQueueCapacity)
                    {
                        Interlocked.Decrement(ref pendingRequests);
                        await ReplyAsync(MessageTypes.Error, request.RequestId,
                                new ErrorPayload("worker_busy", "The worker request queue is full."),
                                requestLoopCancellation.Token)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (!requests.Writer.TryWrite(request))
                    {
                        Interlocked.Decrement(ref pendingRequests);
                        throw new WidgetProtocolViolationException(
                            "The bounded worker request queue rejected an admitted request.");
                    }
                }
            }
            catch (OperationCanceledException) when (requestLoopCancellation.IsCancellationRequested)
            {
                // Graceful Stop and caller cancellation both wake the sole pipe reader.
            }
            catch (Exception exception)
            {
                readerFailure = exception;
                requestLoopCancellation.Cancel();
            }
            finally
            {
                requests.Writer.TryComplete(readerFailure);
            }

            try
            {
                await processor.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readerFailure is not null)
            {
                // Preserve the protocol or transport failure that stopped the reader.
            }
            if (readerFailure is not null)
                throw readerFailure;

            async Task ProcessRequestsAsync(
                ChannelReader<RuntimeEnvelope> reader,
                CancellationTokenSource loopCancellation)
            {
                try
                {
                    await foreach (var request in reader.ReadAllAsync(loopCancellation.Token)
                                       .ConfigureAwait(false))
                    {
                        if (request.Type == MessageTypes.Stop)
                        {
                            await ReplyAsync(
                                    MessageTypes.Acknowledged, request.RequestId, new { },
                                    loopCancellation.Token)
                                .ConfigureAwait(false);
                            return;
                        }

                        try
                        {
                            await HandleRequestAsync(request, loopCancellation.Token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            _diagnostics?.RecordRequestFailure(
                                request.RequestId, request.Type, exception);
                            await ReplyAsync(MessageTypes.Error, request.RequestId,
                                    new ErrorPayload(
                                        ErrorCode(exception), SafeMessage(exception)),
                                    loopCancellation.Token)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref pendingRequests);
                        }
                    }
                }
                finally
                {
                    loopCancellation.Cancel();
                }
            }
        }
        finally
        {
            try
            {
                using var shutdownTimeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _widget.DestroyAsync(shutdownTimeout.Token).AsTask()
                        .WaitAsync(shutdownTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdownTimeout.IsCancellationRequested)
                {
                    // Destruction is bounded so a faulty widget cannot hang worker shutdown.
                }
                finally
                {
                    notifications?.Close();
                    try
                    {
                        if (notifications is not null)
                        {
                            try
                            {
                                await notifications.DrainAsync(TimeSpan.FromSeconds(2))
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                // Caller cancellation is the existing worker terminal authority.
                            }
                        }
                    }
                    finally
                    {
                        _widget.Invalidated -= OnInvalidated;
                        _widget.ActionFailed -= OnActionFailed;
                        _widget.ActionTerminated -= OnActionTerminated;
                    }
                }
            }
            finally
            {
                foreach (var activation in _dashboardGestureActivations.Values)
                    activation.TrySetResult(false);
                _dashboardGestureActivations.Clear();
                _channel = null;
            }
        }
    }

    private static async Task<RuntimeEnvelope> ReadRequestAsync(
        LengthPrefixedJsonChannel channel,
        WidgetWorkerNotificationLane notifications,
        CancellationToken cancellationToken)
    {
        using var readCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var read = channel.ReadAsync(readCancellation.Token).AsTask();
        var completed = await Task.WhenAny(read, notifications.Completion)
            .ConfigureAwait(false);
        if (completed == read)
            return await read.ConfigureAwait(false);

        readCancellation.Cancel();
        try
        {
            await read.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (readCancellation.IsCancellationRequested)
        {
            // The notification pump owns the terminal result selected below.
        }

        await notifications.Completion.ConfigureAwait(false);
        throw new IOException("The worker notification lane ended unexpectedly.");
    }

    private static string ValidateSessionNonce(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit))
            throw new ArgumentException("The session nonce is invalid.", nameof(value));
        return value;
    }

    private async ValueTask<bool> ActivateDashboardGestureAuthorityAsync(
        WidgetCapabilityGestureContext gesture,
        string capabilityId,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (!gesture.IsActive) return false;
        var activationId = Interlocked.Increment(ref _dashboardGestureActivationId);
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dashboardGestureActivations.TryAdd(activationId, completion))
            throw new WidgetProtocolViolationException(
                "Duplicate dashboard gesture activation ID.");
        try
        {
            await SendAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.DashboardGestureActivationRequested,
                Payload = RuntimeJson.ToElement(new DashboardGestureActivationRequestPayload(
                    activationId,
                    capabilityId,
                    operationId,
                    gesture.InputSequence,
                    gesture.SnapshotSequence)),
            }, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _runCancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            _dashboardGestureActivations.TryRemove(activationId, out _);
        }
    }

    private void CompleteDashboardGestureActivation(JsonElement payload)
    {
        var result = RuntimeJson.FromElement<DashboardGestureActivationResultPayload>(payload);
        if (result.ActivationId <= 0)
            throw new WidgetProtocolViolationException(
                "Dashboard gesture activation ID must be positive.");
        if (_dashboardGestureActivations.TryGetValue(result.ActivationId, out var completion))
            completion.TrySetResult(result.Authorized);
    }

    private async Task HandleRequestAsync(RuntimeEnvelope request, CancellationToken cancellationToken)
    {
        switch (request.Type)
        {
        case MessageTypes.SetWidgetLifecycle:
            var lifecycle = RuntimeJson.FromElement<WidgetLifecyclePayload>(request.Payload);
            ValidateHostState(lifecycle.State);
            await _widget.SetLifecycleStateAsync(lifecycle.State, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(MessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case MessageTypes.Render:
            var render = RuntimeJson.FromElement<RenderPayload>(request.Payload);
            var presentationRequest = ValidateRenderRequest(render);
            var publication = _widget.RenderPublication(
                _widgetInstanceId,
                presentationRequest.TransactionKind ==
                    WidgetPresentationTransactionKind.IncrementalUpdate
                    ? presentationRequest.PresentationGeneration!
                    : new string('0', 32),
                NextPresentationSequence(
                    presentationRequest.RecoveryOriginSequence),
                presentationRequest.BaseSequence,
                presentationRequest.UpdateCapabilities,
                presentationRequest.TransactionKind,
                presentationRequest.RecoveryOriginSequence);
            if (publication.Update is { } update)
            {
                var updateBytes = PresentationUpdateJson.Serialize(update);
                using var document = JsonDocument.Parse(updateBytes);
                await SendAsync(new RuntimeEnvelope
                {
                    Type = MessageTypes.PresentationUpdate,
                    RequestId = request.RequestId,
                    Payload = document.RootElement.Clone(),
                }, cancellationToken).ConfigureAwait(false);
                break;
            }
            var snapshotBytes = SnapshotJson.Serialize(publication.Snapshot);
            using (var document = JsonDocument.Parse(snapshotBytes))
            {
                await SendAsync(new RuntimeEnvelope
                {
                    Type = MessageTypes.Snapshot,
                    RequestId = request.RequestId,
                    Payload = document.RootElement.Clone(),
                }, cancellationToken).ConfigureAwait(false);
            }
            break;
        case MessageTypes.Action:
            var action = RuntimeJson.FromElement<WidgetActionEvent>(request.Payload);
            ValidateAction(action);
            var admission = _widget.AdmitAction(action);
            await ReplyAsync(
                    MessageTypes.Acknowledged,
                    request.RequestId,
                    new ActionAdmissionPayload(admission),
                    cancellationToken)
                .ConfigureAwait(false);
            break;
        case MessageTypes.ResolveArtwork:
            var artworkRequest = RuntimeJson.FromElement<ResolveArtworkPayload>(request.Payload);
            var artwork = await _widget.ResolveArtworkAsync(
                artworkRequest.ArtworkHandle, cancellationToken).ConfigureAwait(false);
            if (artwork is not null && !WidgetEncodedArtworkContract.IsValid(artwork))
                throw new WidgetProtocolViolationException(
                    "Widget returned invalid trusted encoded artwork.");
            if (artwork is null)
            {
                await ReplyAsync(
                    MessageTypes.Artwork,
                    request.RequestId,
                    new EncodedArtworkPayload(null, null),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SendArtworkAsync(
                    request.RequestId,
                    WidgetEncodedArtworkContract.ContentTypeValue(artwork.ContentType),
                    artwork.Bytes,
                    cancellationToken).ConfigureAwait(false);
            }
            break;
        case MessageTypes.RevalidatedControllerInput:
            var revalidatedRequest = RuntimeJson.FromElement<RevalidatedControllerInputPayload>(request.Payload);
            ValidateControllerInput(revalidatedRequest.Input);
            var revalidatedResult = await AdmitRevalidatedControllerInputAsync(
                _widget, revalidatedRequest.Input, cancellationToken, revalidatedRequest.ActionId).ConfigureAwait(false);
            await ReplyAsync(MessageTypes.RevalidatedControllerInputResult, request.RequestId,
                new RevalidatedControllerInputResultPayload(revalidatedResult), cancellationToken)
                .ConfigureAwait(false);
            break;
        case MessageTypes.ControllerInput:
            var input = RuntimeJson.FromElement<ControllerInputEvent>(request.Payload);
            ValidateControllerInput(input);
            using (input is
                {
                    Context: ControllerInputContext.DashboardQuickAction,
                    Origin: ControllerInputOrigin.PhysicalController,
                }
                ? WidgetCapabilityInvocationContext.Enter(
                    new WidgetCapabilityGestureContext(
                        input.Sequence, input.SnapshotSequence))
                : null)
            {
            var handled = await _widget.OnControllerInputAsync(input, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(MessageTypes.ControllerInputResult, request.RequestId,
                    new ControllerInputResultPayload(handled), cancellationToken)
                .ConfigureAwait(false);
            }
            break;
        case MessageTypes.EmbeddedMediaPlaybackEvent:
            var playbackEvent = RuntimeJson.FromElement<EmbeddedMediaPlaybackEvent>(request.Payload);
            await _widget.ApplyEmbeddedMediaPlaybackEventAsync(
                playbackEvent, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(
                MessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        default:
            throw new WidgetProtocolViolationException($"Unknown request type '{request.Type}'.");
        }
    }

    internal static async ValueTask<bool?> AdmitRevalidatedControllerInputAsync(
        Widget widget, ControllerInputEvent input, CancellationToken cancellationToken,
        string? admittedActionId = null)
    {
        // The bridge proved origin/current declarative bindings compatible.
        // An explicit action binding keeps the existing declared-action contract.
        // Unbound private override semantics cannot be established by focus alone.
        // Check the actual virtual slot, including inherited overrides.
        var baseHandler = typeof(Widget).GetMethod(nameof(Widget.OnControllerInputAsync),
            [typeof(ControllerInputEvent), typeof(CancellationToken)])!;
        var hasOverride = widget.GetType().GetMethods().Any(method =>
            method.IsVirtual && method.GetBaseDefinition() == baseHandler &&
            method.DeclaringType != typeof(Widget));
        if (input.Context is not (ControllerInputContext.OpenWidget or
                ControllerInputContext.PinnedSurface) ||
            (hasOverride && string.IsNullOrWhiteSpace(admittedActionId)) ||
            !widget.HasControllerInputSnapshot(input.SnapshotSequence))
            return null;
        // Worker requests are serialized: a render cannot slip between this
        // check and the SDK's synchronous routing/admission. Invoke only once.
        return await widget.OnControllerInputAsync(input, cancellationToken).ConfigureAwait(false);
    }

    private void OnInvalidated(object? sender, WidgetInvalidatedEventArgs args)
    {
        _ = sender;
        _ = GetNotificationLane().EnqueueInvalidation(args.Revision);
    }

    private void OnActionFailed(object? sender, WidgetActionFailedEventArgs args)
    {
        _ = sender;
        _ = GetNotificationLane().EnqueueActionFailure(
            new ControllerActionFailurePayload(
                args.Action.ActionId,
                args.Action.SourceElementId,
                "Action failed."));
    }

    private void OnActionTerminated(object? sender, WidgetActionExecutionTerminal terminal)
    {
        _ = sender;
        if (!_actionTerminalsEnabled) return;
        _ = GetNotificationLane().EnqueueActionTerminal(
            new ActionTerminalPayload(terminal.ExecutionId, terminal.Outcome));
    }

    private WidgetWorkerNotificationLane GetNotificationLane() =>
        _notificationLane ?? throw new InvalidOperationException(
            "Worker notification admission is not active.");

    private Task ReplyAsync<T>(string type, long requestId, T payload, CancellationToken cancellationToken) =>
        SendAsync(new RuntimeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = RuntimeJson.ToElement(payload),
        }, cancellationToken);

    private async Task SendArtworkAsync(
        long requestId,
        string contentType,
        ReadOnlyMemory<byte> artworkBytes,
        CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("Worker is not connected.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.WriteArtworkAsync(
                requestId, contentType, artworkBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SendAsync(RuntimeEnvelope envelope, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("Worker is not connected.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void ValidateAction(WidgetActionEvent action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.ActionId) || action.ActionId.Length > 128)
            throw new WidgetProtocolViolationException("Action ID is missing or too long.");
        if (string.IsNullOrWhiteSpace(action.SourceElementId) || action.SourceElementId.Length > 128)
            throw new WidgetProtocolViolationException("Action source ID is missing or too long.");
        if (action.Sequence < 0 || action.MonotonicTimestampMicroseconds < 0)
            throw new WidgetProtocolViolationException("Action sequence and timestamp cannot be negative.");
        if (action.RequestedValue is { } requested && !double.IsFinite(requested))
            throw new WidgetProtocolViolationException("Requested action value must be finite.");
        if (action.InputScopeId is { } actionScope &&
            (actionScope.Length > 128 ||
             !actionScope.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new WidgetProtocolViolationException("Action input scope ID is invalid.");
        if (action.FocusedElementId is { } focusedElementId &&
            (focusedElementId.Length is 0 or > 128 ||
             !focusedElementId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new WidgetProtocolViolationException("Action focused element ID is invalid.");
        if (action.CommittedText is { } committed &&
            (committed.Length > ProtocolConstants.MaximumTextEntryLength ||
             committed.Any(char.IsControl)))
            throw new WidgetProtocolViolationException("Committed text is invalid or too long.");
    }

    private static void ValidateControllerInput(ControllerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Origin))
            throw new WidgetProtocolViolationException(
                "Controller input origin is not supported.");
        if (input.FocusedElementId is { Length: > 128 })
            throw new WidgetProtocolViolationException("Focused element ID is too long.");
        if (input.Sequence < 0 || input.MonotonicTimestampMicroseconds < 0 || input.SnapshotSequence < 0)
            throw new WidgetProtocolViolationException("Input sequence and timestamp cannot be negative.");
        if (input.RequestedValue is { } requested && !double.IsFinite(requested))
            throw new WidgetProtocolViolationException("Requested controller value must be finite.");
        if (input.Context == ControllerInputContext.DashboardQuickAction && input.SnapshotSequence <= 0)
            throw new WidgetProtocolViolationException(
                "Dashboard input requires a positive snapshot sequence.");
        if (input.Context == ControllerInputContext.OpenWidget &&
            (input.SnapshotSequence <= 0 || string.IsNullOrWhiteSpace(input.ActiveInputScopeId) ||
             input.ActiveInputScopeId.Length > 128 ||
             !input.ActiveInputScopeId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new WidgetProtocolViolationException(
                "Open-widget input requires a valid active scope ID and positive snapshot sequence.");
        if (input.Context == ControllerInputContext.PinnedSurface &&
            (input.SnapshotSequence <= 0 ||
             !IsBoundedIdentifier(input.ActiveInputScopeId) ||
             !IsBoundedIdentifier(input.FocusedElementId) ||
             !IsBoundedIdentifier(input.PinnedLayoutId) ||
             input.IsPinnedLayoutSelected is not null))
            throw new WidgetProtocolViolationException(
                "Pinned-surface input requires exact layout, scope, focus, and snapshot authority.");
        if (input.Context == ControllerInputContext.PinnedLayoutSelection &&
            (input.IsPinnedLayoutSelected is null ||
             (input.IsPinnedLayoutSelected == true &&
              (string.IsNullOrWhiteSpace(input.PinnedLayoutId) ||
               input.PinnedLayoutId.Length > 128 ||
               !input.PinnedLayoutId.All(ch =>
                   char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))))
            throw new WidgetProtocolViolationException(
                "Pinned layout selection input is invalid.");
    }

    private static bool IsBoundedIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static void ValidateHostState(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))
            throw new WidgetProtocolViolationException(
                "Hosts may request only Background, Visible, or Interactive.");
    }

    internal static RuntimeRenderRequest ValidateRenderRequest(
        RenderPayload render)
    {
        var capabilities = render.UpdateCapabilities;
        capabilities ??= PresentationUpdateCapabilities.None;
        var none = capabilities.MaximumProtocolVersion == 0 &&
            capabilities.MaximumOperationsPerBatch == 0 &&
            capabilities.MaximumBatchBytes == 0;
        var bounded = capabilities.MaximumProtocolVersion ==
                ProtocolConstants.AtomicPresentationUpdateVersion &&
            capabilities.MaximumOperationsPerBatch is > 0 and
                <= ProtocolConstants.MaximumPresentationUpdateOperations &&
            capabilities.MaximumBatchBytes is > 0 and
                <= ProtocolConstants.MaximumPresentationUpdateBytes;
        if (!none && !bounded)
            throw new WidgetProtocolViolationException(
                "Presentation update capabilities are malformed or unsupported.");
        var transactionKind = !render.RequireCheckpoint
            ? WidgetPresentationTransactionKind.IncrementalUpdate
            : render.BaseSequence > 0
                ? WidgetPresentationTransactionKind.RecoveryCheckpoint
                : WidgetPresentationTransactionKind.OrdinaryCheckpoint;
        if (bounded && (render.RequireCheckpoint || render.BaseSequence <= 0 ||
                render.PresentationGeneration is not { Length: 32 or 64 } ||
                !render.PresentationGeneration.All(char.IsAsciiHexDigit)))
            throw new WidgetProtocolViolationException(
                "Presentation update admission is incomplete or malformed.");
        if (none && (!render.RequireCheckpoint || render.BaseSequence < 0 ||
                render.PresentationGeneration is not null))
            throw new WidgetProtocolViolationException(
                "Checkpoint render admission is malformed.");
        return new RuntimeRenderRequest(
            transactionKind,
            transactionKind == WidgetPresentationTransactionKind.IncrementalUpdate
                ? render.BaseSequence
                : 0,
            transactionKind == WidgetPresentationTransactionKind.RecoveryCheckpoint
                ? render.BaseSequence
                : 0,
            render.PresentationGeneration,
            capabilities);
    }

    private long NextPresentationSequence(long recoveryOriginSequence)
    {
        while (true)
        {
            var localSequence = Volatile.Read(ref _sequence);
            var origin = Math.Max(localSequence, recoveryOriginSequence);
            if (origin == long.MaxValue)
                throw new WidgetProtocolViolationException(
                    "Presentation sequence authority is exhausted.");
            var next = origin + 1;
            if (Interlocked.CompareExchange(ref _sequence, next, localSequence) ==
                localSequence)
                return next;
        }
    }


    private static string ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new ArgumentException("Widget instance ID is invalid.", nameof(value));
        return value;
    }

    private static string ValidatePipeName(string value)
    {
        const string localPrefix = "LOCAL\\";
        var suffix = value.StartsWith(localPrefix, StringComparison.Ordinal)
            ? value[localPrefix.Length..]
            : value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            string.IsNullOrWhiteSpace(suffix) || suffix.Contains('\\') || value.Any(char.IsControl))
            throw new ArgumentException("Pipe name is invalid.", nameof(value));
        return value;
    }

    private static string SafeMessage(Exception exception)
    {
        if (exception is ProtocolValidationException validationException)
        {
            var firstError = validationException.Errors.FirstOrDefault();
            if (firstError is null) return "Widget protocol validation failed.";
            if (!WidgetWorkerDiagnosticLog.IsSafeValidationPath(firstError.Path) ||
                !WidgetWorkerDiagnosticLog.IsSafeValidationCode(firstError.Code))
                return "Widget protocol validation failed.";
            var (field, state, identifier) =
                WidgetWorkerDiagnosticLog.NormalizeIdentifierContext(
                    firstError.IdentifierContext);
            var context = field is not null && state is not null
                ? $"; field={field}; state={state}" +
                  (identifier is not null ? $"; identifier={identifier}" : string.Empty)
                : string.Empty;
            var diagnostic = $"Widget protocol validation failed at {firstError.Path} " +
                $"({firstError.Code}{context}).";
            return diagnostic.Length <=
                WidgetRuntimeProtocol.MaximumProtocolValidationDiagnosticMessageLength
                ? diagnostic
                : "Widget protocol validation failed.";
        }

        var message = exception.Message;
        if (message.Length > WidgetRuntimeProtocol.MaximumWorkerDiagnosticMessageLength)
            message = message[..WidgetRuntimeProtocol.MaximumWorkerDiagnosticMessageLength];
        return message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }

    private static string ErrorCode(Exception exception) =>
        exception is ProtocolValidationException
            ? WorkerErrorCodes.ProtocolValidationFailed
            : WorkerErrorCodes.RequestFailed;
}
