using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

public sealed class WidgetBridgeServer(
    string pipeName,
    BridgeCatalog catalog,
    int maximumMessageBytes = BridgeProtocol.DefaultMaximumMessageBytes,
    PlatformAppearanceService? appearance = null) : IAsyncDisposable
{
    private readonly string _pipeName = ValidatePipeName(pipeName);
    private readonly BridgeCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly int _maximumMessageBytes = maximumMessageBytes is >= 256 and <= BridgeProtocol.AbsoluteMaximumMessageBytes
        ? maximumMessageBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
    private readonly PlatformAppearanceService? _appearance = appearance;
    private readonly ConcurrentDictionary<string, WidgetProcessClient> _clients = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private BridgeFrameChannel? _channel;
    private CancellationToken _sessionCancellation;
    private bool _disposed;

    public int RunningWorkerCount => _clients.Values.Count(client => client.IsRunning);

    public async Task RunAsync(TimeSpan acceptTimeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (acceptTimeout <= TimeSpan.Zero || acceptTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(acceptTimeout));
        await using var pipe = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        using var acceptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acceptCancellation.CancelAfter(acceptTimeout);
        await pipe.WaitForConnectionAsync(acceptCancellation.Token).ConfigureAwait(false);
        _channel = new BridgeFrameChannel(pipe, _maximumMessageBytes);
        _sessionCancellation = cancellationToken;

        var hello = await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (hello.Type != BridgeMessageTypes.Hello || hello.RequestId == 0)
            throw new BridgeProtocolException("The first bridge message must be a correlated hello request.");
        var helloPayload = BridgeJson.FromElement<BridgeHello>(hello.Payload);
        if (string.IsNullOrWhiteSpace(helloPayload.ClientName) || helloPayload.ClientName.Length > 128)
            throw new BridgeProtocolException("Bridge client name is invalid.");
        await SendAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.HelloAccepted,
            RequestId = hello.RequestId,
            Payload = BridgeJson.ToElement(new { }),
        }, cancellationToken).ConfigureAwait(false);

        if (_appearance is not null) _appearance.Changed += OnAppearanceChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var request = await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (request.RequestId == 0)
                    throw new BridgeProtocolException("Bridge requests require a non-zero request ID.");
                if (request.Type == BridgeMessageTypes.Stop)
                {
                    await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }
                try
                {
                    await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await ReplyAsync(BridgeMessageTypes.Error, request.RequestId,
                            new BridgeError("request_failed", SafeMessage(exception)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (_appearance is not null) _appearance.Changed -= OnAppearanceChanged;
            _channel = null;
            await DisposeClientsAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisposeClientsAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task HandleRequestAsync(BridgeEnvelope request, CancellationToken cancellationToken)
    {
        switch (request.Type)
        {
        case BridgeMessageTypes.ListWidgets:
            await ReplyAsync(BridgeMessageTypes.Widgets, request.RequestId,
                    new { widgets = _catalog.Widgets }, cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.GetPlatformAppearance:
            if (_appearance is null)
                throw new BridgeProtocolException("Platform appearance service is unavailable.");
            await ReplyAsync(
                BridgeMessageTypes.PlatformAppearance,
                request.RequestId,
                _appearance.CreatePayload(),
                cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.GetSnapshot:
            var snapshotRequest = BridgeJson.FromElement<WidgetIdRequest>(request.Payload);
            var snapshotClient = GetClient(snapshotRequest.WidgetId);
            var snapshot = await snapshotClient.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var configuredForStyle = _catalog.GetConfigured(snapshotRequest.WidgetId);
            var theme = _appearance is null
                ? configuredForStyle.CompiledTheme
                : _appearance.ResolveWidgetTheme(configuredForStyle.Id, configuredForStyle.StylePackage);
            var renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, theme);
            var snapshotBytes = SnapshotJson.Serialize(snapshot);
            using (var document = JsonDocument.Parse(snapshotBytes))
            {
                await SendAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageTypes.Snapshot,
                    RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new
                    {
                        widgetId = snapshotRequest.WidgetId,
                        snapshot = document.RootElement.Clone(),
                        renderStyles,
                    }),
                }, cancellationToken).ConfigureAwait(false);
            }
            break;
        case BridgeMessageTypes.SetWidgetLifecycle:
            var lifecycleRequest = BridgeJson.FromElement<BridgeWidgetLifecycleRequest>(request.Payload);
            _ = _catalog.GetConfigured(lifecycleRequest.WidgetId);
            ValidateHostState(lifecycleRequest.State);
            await GetClient(lifecycleRequest.WidgetId)
                .SetLifecycleStateAsync(lifecycleRequest.State, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.Action:
            var actionRequest = BridgeJson.FromElement<BridgeActionRequest>(request.Payload);
            _ = _catalog.GetConfigured(actionRequest.WidgetId);
            await GetClient(actionRequest.WidgetId).SendActionAsync(actionRequest.Action, cancellationToken)
                .ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.ControllerInput:
            var controllerRequest = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            _ = _catalog.GetConfigured(controllerRequest.WidgetId);
            ValidateControllerInput(controllerRequest.Input);
            var handled = await GetClient(controllerRequest.WidgetId)
                .SendControllerInputAsync(controllerRequest.Input, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.ControllerInputResult, request.RequestId,
                    new { handled }, cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.QuickAction:
            var quickRequest = BridgeJson.FromElement<BridgeQuickActionRequest>(request.Payload);
            var configured = _catalog.GetConfigured(quickRequest.WidgetId);
            var quickAction = configured.QuickActions.SingleOrDefault(
                action => string.Equals(action.Id, quickRequest.QuickActionId, StringComparison.Ordinal))
                ?? throw new BridgeProtocolException($"Unknown quick action '{quickRequest.QuickActionId}'.");
            await GetClient(quickRequest.WidgetId).SendActionAsync(new WidgetActionEvent(
                quickAction.ActionId,
                quickAction.SourceElementId,
                quickAction.ControllerButton,
                ControllerEventPhase.Pressed,
                quickRequest.Sequence,
                quickRequest.MonotonicTimestampMicroseconds), cancellationToken).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        default:
            throw new BridgeProtocolException($"Unknown bridge request type '{request.Type}'.");
        }
    }

    private WidgetProcessClient GetClient(string widgetId)
    {
        var configured = _catalog.GetConfigured(widgetId);
        return _clients.GetOrAdd(widgetId, _ =>
        {
            var client = new WidgetProcessClient(new WidgetProcessOptions
            {
                ExecutablePath = configured.WorkerExecutable,
                Arguments = configured.WorkerArguments,
                WidgetInstanceId = configured.InstanceId,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                RequestTimeout = TimeSpan.FromSeconds(2),
                MaximumMessageBytes = _maximumMessageBytes,
                MaximumRestartAttempts = 2,
            });
            client.Invalidated += (_, revision) => _ = SendEventAsync(
                BridgeMessageTypes.Invalidation,
                new BridgeInvalidation(configured.Id, revision));
            client.ControllerActionFailed += (_, failure) => _ = SendEventAsync(
                BridgeMessageTypes.Failure,
                new
                {
                    widgetId = configured.Id,
                    reason = "controllerActionFailed",
                    failure.ActionId,
                    failure.SourceElementId,
                    failure.Message,
                    canRestart = false,
                });
            client.Failed += (_, failure) => _ = SendEventAsync(
                BridgeMessageTypes.Failure,
                new
                {
                    widgetId = configured.Id,
                    reason = failure.Reason,
                    failure.ExitCode,
                    failure.RestartsUsed,
                    failure.CanRestart,
                });
            return client;
        });
    }

    private async Task SendEventAsync<T>(string type, T payload)
    {
        try
        {
            await SendAsync(new BridgeEnvelope
            {
                Type = type,
                Payload = BridgeJson.ToElement(payload),
            }, _sessionCancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The main request loop owns native-host disconnect handling.
        }
    }

    private void OnAppearanceChanged(object? sender, ThemeSnapshot snapshot) =>
        _ = SendEventAsync(
            BridgeMessageTypes.AppearanceChanged,
            new BridgeAppearanceChanged(snapshot.Revision));

    private Task ReplyAsync<T>(string type, long requestId, T payload, CancellationToken cancellationToken) =>
        SendAsync(new BridgeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(payload),
        }, cancellationToken);

    private async Task SendAsync(BridgeEnvelope envelope, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("Native host is not connected.");
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

    private async Task DisposeClientsAsync()
    {
        foreach (var entry in _clients.ToArray())
        {
            if (_clients.TryRemove(entry.Key, out var client))
                await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string ValidatePipeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Contains('\\'))
            throw new ArgumentException("Bridge pipe name is invalid.", nameof(value));
        return value;
    }

    private static void ValidateControllerInput(ControllerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Sequence < 0 || input.MonotonicTimestampMicroseconds < 0)
            throw new BridgeProtocolException("Controller input sequence and timestamp cannot be negative.");
        if (input.FocusedElementId is { Length: > 128 })
            throw new BridgeProtocolException("Focused element ID is too long.");
        if (input.Context == ControllerInputContext.DashboardQuickAction && input.Button is
            ControllerButton.A or ControllerButton.Y or
            ControllerButton.DPadUp or ControllerButton.DPadDown or
            ControllerButton.DPadLeft or ControllerButton.DPadRight)
            throw new BridgeProtocolException(
                "A, Y, and D-pad input are owned by the dashboard and cannot be forwarded.");
    }

    private static void ValidateHostState(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))
            throw new BridgeProtocolException(
                "Hosts may request only Background, Visible, or Interactive.");
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message;
        if (message.Length > 512) message = message[..512];
        return message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}
