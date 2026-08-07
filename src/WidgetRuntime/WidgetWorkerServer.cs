using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

public sealed class WidgetWorkerServer
{
    private readonly Widget _widget;
    private readonly string _widgetInstanceId;
    private readonly string _pipeName;
    private readonly int _maximumMessageBytes;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private LengthPrefixedJsonChannel? _channel;
    private CancellationToken _runCancellation;
    private long _sequence;

    public WidgetWorkerServer(
        Widget widget,
        string widgetInstanceId,
        string pipeName,
        int maximumMessageBytes = WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
        IWidgetCapabilityClient? capabilityClient = null)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        _widgetInstanceId = ValidateIdentifier(widgetInstanceId);
        _pipeName = ValidatePipeName(pipeName);
        _maximumMessageBytes = maximumMessageBytes is >= 256 and <= WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes
            ? maximumMessageBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        _widget.AttachHostServices(new WidgetHostServices(
            capabilityClient ?? UnavailableWidgetCapabilityClient.Instance));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _channel = new LengthPrefixedJsonChannel(pipe, _maximumMessageBytes);
        _runCancellation = cancellationToken;

        await SendAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Hello,
            Payload = RuntimeJson.ToElement(new HelloPayload(_widgetInstanceId)),
        }, cancellationToken).ConfigureAwait(false);
        var acceptance = await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (acceptance.Type != MessageTypes.HelloAccepted || acceptance.RequestId != 0)
            throw new WidgetProtocolViolationException("Host did not accept the worker handshake.");

        _widget.Invalidated += OnInvalidated;
        _widget.ControllerActionFailed += OnControllerActionFailed;
        try
        {
            await _widget.InitializeAsync(cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var request = await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (request.RequestId == 0)
                    throw new WidgetProtocolViolationException("Host requests require a non-zero request ID.");

                if (request.Type == MessageTypes.Stop)
                {
                    await ReplyAsync(MessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }

                try
                {
                    await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await ReplyAsync(MessageTypes.Error, request.RequestId,
                            new ErrorPayload("worker_request_failed", SafeMessage(exception)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _widget.DestroyAsync(shutdownTimeout.Token).AsTask()
                    .WaitAsync(shutdownTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdownTimeout.IsCancellationRequested)
            {
                // Destruction is bounded so a faulty widget cannot hang worker shutdown.
            }
            _widget.Invalidated -= OnInvalidated;
            _widget.ControllerActionFailed -= OnControllerActionFailed;
            _channel = null;
        }
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
            var snapshot = _widget.RenderSnapshot(
                _widgetInstanceId, Interlocked.Increment(ref _sequence));
            var snapshotBytes = SnapshotJson.Serialize(snapshot);
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
            await _widget.OnActionAsync(action, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(MessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case MessageTypes.ControllerInput:
            var input = RuntimeJson.FromElement<ControllerInputEvent>(request.Payload);
            ValidateControllerInput(input);
            var handled = await _widget.OnControllerInputAsync(input, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(MessageTypes.ControllerInputResult, request.RequestId,
                    new ControllerInputResultPayload(handled), cancellationToken)
                .ConfigureAwait(false);
            break;
        default:
            throw new WidgetProtocolViolationException($"Unknown request type '{request.Type}'.");
        }
    }

    private void OnInvalidated(object? sender, WidgetInvalidatedEventArgs args)
    {
        _ = SendInvalidationAsync(args.Revision);
    }

    private void OnControllerActionFailed(object? sender, WidgetControllerActionFailedEventArgs args)
    {
        _ = SendControllerActionFailureAsync(args);
    }

    private async Task SendControllerActionFailureAsync(WidgetControllerActionFailedEventArgs args)
    {
        try
        {
            await SendAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.ControllerActionFailed,
                Payload = RuntimeJson.ToElement(new ControllerActionFailurePayload(
                    args.Action.ActionId,
                    args.Action.SourceElementId,
                    SafeMessage(args.Exception))),
            }, _runCancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The request loop owns connection failure reporting.
        }
    }

    private async Task SendInvalidationAsync(long revision)
    {
        try
        {
            await SendAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.Invalidated,
                Payload = RuntimeJson.ToElement(new InvalidationPayload(revision)),
            }, _runCancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The request loop owns connection failure reporting.
        }
    }

    private Task ReplyAsync<T>(string type, long requestId, T payload, CancellationToken cancellationToken) =>
        SendAsync(new RuntimeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = RuntimeJson.ToElement(payload),
        }, cancellationToken);

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
    }

    private static void ValidateControllerInput(ControllerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.FocusedElementId is { Length: > 128 })
            throw new WidgetProtocolViolationException("Focused element ID is too long.");
        if (input.Sequence < 0 || input.MonotonicTimestampMicroseconds < 0 || input.SnapshotSequence < 0)
            throw new WidgetProtocolViolationException("Input sequence and timestamp cannot be negative.");
        if (input.Context == ControllerInputContext.OpenWidget &&
            (input.SnapshotSequence <= 0 || string.IsNullOrWhiteSpace(input.ActiveInputScopeId) ||
             input.ActiveInputScopeId.Length > 128 ||
             !input.ActiveInputScopeId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new WidgetProtocolViolationException(
                "Open-widget input requires a valid active scope ID and positive snapshot sequence.");
    }

    private static void ValidateHostState(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))
            throw new WidgetProtocolViolationException(
                "Hosts may request only Background, Visible, or Interactive.");
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
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Contains('\\'))
            throw new ArgumentException("Pipe name is invalid.", nameof(value));
        return value;
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message;
        if (message.Length > 512) message = message[..512];
        return message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}
