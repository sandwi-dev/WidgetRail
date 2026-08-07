using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

public sealed class WidgetBridgeServer(
    string pipeName,
    BridgeCatalog catalog,
    int maximumMessageBytes = BridgeProtocol.DefaultMaximumMessageBytes,
    PlatformAppearanceService? appearance = null,
    ConsentStore? consentStore = null,
    IPlatformBrokerBackend? platformBackend = null,
    BridgeCatalogMonitor? catalogMonitor = null) : IAsyncDisposable
{
    private readonly string _pipeName = ValidatePipeName(pipeName);
    private BridgeCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly int _maximumMessageBytes = maximumMessageBytes is >= 256 and <= BridgeProtocol.AbsoluteMaximumMessageBytes
        ? maximumMessageBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
    private readonly PlatformAppearanceService? _appearance = appearance;
    private readonly ConsentStore? _consentStore = consentStore;
    private readonly IPlatformBrokerBackend? _platformBackend = platformBackend;
    private readonly BridgeCatalogMonitor? _catalogMonitor = catalogMonitor;
    private readonly ConcurrentDictionary<string, ClientRegistration> _clients = new(StringComparer.Ordinal);
    private readonly object _catalogGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private long _catalogRevision;
    private BridgeFrameChannel? _channel;
    private CancellationToken _sessionCancellation;
    private bool _disposed;

    public int RunningWorkerCount => _clients.Values.Count(registration => registration.Client.IsRunning);

    public async Task RunAsync(TimeSpan acceptTimeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (acceptTimeout <= TimeSpan.Zero || acceptTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(acceptTimeout));
        if (_catalogMonitor is not null)
        {
            _catalogMonitor.Changed += OnCatalogChanged;
            ApplyCatalog(_catalogMonitor.Current, _catalogMonitor.Revision, publishEvent: false);
        }
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
            if (_catalogMonitor is not null) _catalogMonitor.Changed -= OnCatalogChanged;
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
            BridgeCatalog listCatalog;
            long listRevision;
            lock (_catalogGate)
            {
                listCatalog = _catalog;
                listRevision = _catalogRevision;
            }
            await ReplyAsync(BridgeMessageTypes.Widgets, request.RequestId,
                    new { revision = listRevision, widgets = listCatalog.Widgets }, cancellationToken)
                .ConfigureAwait(false);
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
            var snapshotRegistration = GetClient(snapshotRequest.WidgetId);
            var snapshot = await snapshotRegistration.Client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var configuredForStyle = snapshotRegistration.Configured;
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
            ValidateHostState(lifecycleRequest.State);
            await GetClient(lifecycleRequest.WidgetId).Client
                .SetLifecycleStateAsync(lifecycleRequest.State, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.Action:
            var actionRequest = BridgeJson.FromElement<BridgeActionRequest>(request.Payload);
            await GetClient(actionRequest.WidgetId).Client.SendActionAsync(actionRequest.Action, cancellationToken)
                .ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.ControllerInput:
            var controllerRequest = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            ValidateControllerInput(controllerRequest.Input);
            var handled = await GetClient(controllerRequest.WidgetId).Client
                .SendControllerInputAsync(controllerRequest.Input, cancellationToken).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.ControllerInputResult, request.RequestId,
                    new { handled }, cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.QuickAction:
            var quickRequest = BridgeJson.FromElement<BridgeQuickActionRequest>(request.Payload);
            var quickRegistration = GetClient(quickRequest.WidgetId);
            var configured = quickRegistration.Configured;
            var quickAction = configured.QuickActions.SingleOrDefault(
                action => string.Equals(action.Id, quickRequest.QuickActionId, StringComparison.Ordinal))
                ?? throw new BridgeProtocolException($"Unknown quick action '{quickRequest.QuickActionId}'.");
            await quickRegistration.Client.SendActionAsync(new WidgetActionEvent(
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

    private ClientRegistration GetClient(string widgetId)
    {
        lock (_catalogGate)
        {
            var configured = _catalog.GetConfigured(widgetId);
            if (_clients.TryGetValue(widgetId, out var existing) &&
                string.Equals(existing.Configured.WorkerFingerprint,
                    configured.WorkerFingerprint, StringComparison.Ordinal))
                return existing;
            if (existing is not null && _clients.TryRemove(widgetId, out var replaced))
                _ = DisposeRegistrationAsync(replaced);

            var client = new WidgetProcessClient(new WidgetProcessOptions
            {
                ExecutablePath = configured.WorkerExecutable,
                Arguments = configured.WorkerArguments,
                WidgetInstanceId = configured.InstanceId,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                RequestTimeout = TimeSpan.FromSeconds(2),
                MaximumMessageBytes = _maximumMessageBytes,
                MaximumRestartAttempts = 2,
                MemoryLimitBytes = checked((long)configured.MemoryLimitMb * 1024 * 1024),
                IsolationPolicy = configured.RequiresAppContainer
                    ? WidgetWorkerIsolationPolicy.RequireAppContainer
                    : WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
                IsolationKey = configured.IsolationKey,
                ReadOnlyPaths = configured.ReadOnlyPaths,
                CompanionSessionFactory = configured.DeclaredCapabilities.Count == 0
                    ? null
                    : CreateCompanionFactory(configured),
            });
            var registration = new ClientRegistration(configured, client);
            client.Invalidated += (_, revision) =>
            {
                if (IsCurrent(registration)) _ = SendEventAsync(
                    BridgeMessageTypes.Invalidation,
                    new BridgeInvalidation(configured.Id, revision));
            };
            client.ControllerActionFailed += (_, failure) =>
            {
                if (IsCurrent(registration)) _ = SendEventAsync(
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
            };
            client.Failed += (_, failure) =>
            {
                if (IsCurrent(registration)) _ = SendEventAsync(
                    BridgeMessageTypes.Failure,
                    new
                    {
                        widgetId = configured.Id,
                        reason = failure.Reason,
                        failure.ExitCode,
                        failure.RestartsUsed,
                        failure.CanRestart,
                    });
            };
            _clients[widgetId] = registration;
            return registration;
        }
    }

    private Func<WidgetProcessCompanionContext, IWidgetProcessCompanionSession>
        CreateCompanionFactory(ConfiguredWidget configured)
    {
        if (_consentStore is null || _platformBackend is null)
            throw new BridgeProtocolException(
                $"Widget '{configured.Id}' requires platform capabilities, but the broker is unavailable.");
        return context => new BrokerWidgetProcessCompanion(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.DeclaredCapabilities,
            _consentStore,
            _platformBackend,
            context);
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

    private void OnCatalogChanged(object? sender, BridgeCatalogChanged change) =>
        ApplyCatalog(change.Catalog, change.Revision, publishEvent: true);

    internal void ApplyCatalog(BridgeCatalog catalog, long revision, bool publishEvent = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        var removed = new List<ClientRegistration>();
        lock (_catalogGate)
        {
            if (revision < _catalogRevision ||
                (revision == _catalogRevision && _catalog.IsEquivalentTo(catalog)))
                return;
            if (revision == _catalogRevision)
                return; // Equal revisions are immutable and cannot replace prior state.
            _catalog = catalog;
            _catalogRevision = revision;
            foreach (var pair in _clients.ToArray())
            {
                ConfiguredWidget? configured = null;
                try { configured = catalog.GetConfigured(pair.Key); }
                catch (BridgeProtocolException) { }
                if (configured is not null && string.Equals(
                        configured.WorkerFingerprint,
                        pair.Value.Configured.WorkerFingerprint,
                        StringComparison.Ordinal))
                {
                    // Keep the compatible process, but atomically replace its
                    // presentation and quick-action authority with the newly
                    // validated catalog entry.
                    pair.Value.Configured = configured;
                    continue;
                }
                if (_clients.TryRemove(pair.Key, out var registration)) removed.Add(registration);
            }
        }

        foreach (var registration in removed) _ = DisposeRegistrationAsync(registration);
        if (publishEvent) _ = SendEventAsync(
            BridgeMessageTypes.CatalogChanged,
            new BridgeCatalogChangedEvent(revision));
    }

    private bool IsCurrent(ClientRegistration registration) =>
        _clients.TryGetValue(registration.Configured.Id, out var current) &&
        ReferenceEquals(current, registration);

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
            if (_clients.TryRemove(entry.Key, out var registration))
                await registration.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task DisposeRegistrationAsync(ClientRegistration registration)
    {
        try { await registration.Client.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // A replaced worker is already unreachable; disposal is best effort.
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

    private sealed class ClientRegistration(
        ConfiguredWidget configured,
        WidgetProcessClient client)
    {
        private ConfiguredWidget _configured = configured;
        public ConfiguredWidget Configured
        {
            get => Volatile.Read(ref _configured);
            set => Volatile.Write(ref _configured, value);
        }
        public WidgetProcessClient Client { get; } = client;
    }
}
