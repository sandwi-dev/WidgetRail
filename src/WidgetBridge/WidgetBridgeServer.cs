using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetBridge;

public sealed class WidgetBridgeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly int _maximumMessageBytes;
    private readonly PlatformAppearanceService? _appearance;
    private readonly ConsentStore? _consentStore;
    private readonly IPlatformBrokerBackend? _platformBackend;
    private readonly AppLibraryArtworkRegistry? _appLibraryArtwork;
    private readonly BridgeCatalogMonitor? _catalogMonitor;
    private readonly BridgeClientRegistry _registry;
    private readonly BridgeDiagnosticsProjection _diagnostics;
    private readonly BridgeAuthorityRecoveryProjection _authorityRecovery;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly BridgeFrameWriteBoundary _frameWriter;
    private long _hostEffectSequence;
    private BridgeFrameChannel? _channel;
    private CancellationToken _sessionCancellation;
    private CancellationTokenSource? _activeSessionCancellation;
    private bool _disposed;

    public WidgetBridgeServer(
        string pipeName,
        BridgeCatalog catalog,
        int maximumMessageBytes = BridgeProtocol.DefaultMaximumMessageBytes,
        PlatformAppearanceService? appearance = null,
        ConsentStore? consentStore = null,
        IPlatformBrokerBackend? platformBackend = null,
        BridgeCatalogMonitor? catalogMonitor = null,
        WorkerResidencyBudgetOptions? residencyBudget = null)
    {
        _pipeName = ValidatePipeName(pipeName);
        _maximumMessageBytes = maximumMessageBytes is >= 256 and <=
            BridgeProtocol.AbsoluteMaximumMessageBytes
                ? maximumMessageBytes
                : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        _appearance = appearance;
        _consentStore = consentStore;
        _platformBackend = platformBackend;
        _appLibraryArtwork = platformBackend is null ? null : new AppLibraryArtworkRegistry();
        _catalogMonitor = catalogMonitor;
        _registry = new BridgeClientRegistry(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            residencyBudget ?? new WorkerResidencyBudgetOptions(),
            CreateWidgetClient,
            PublishClientInvalidation,
            PublishClientActionFailure,
            PublishClientFailure);
        _authorityRecovery = new BridgeAuthorityRecoveryProjection(
            AppContainerAuthorityRecoveryService.Default);
        _diagnostics = new BridgeDiagnosticsProjection(
            new WidgetBridgeDiagnosticsSource(
                _registry,
                _catalogMonitor,
                _appearance,
                _consentStore,
                _platformBackend is not null),
            () => _authorityRecovery);
        _frameWriter = new BridgeFrameWriteBoundary(
            new ServerFrameWriteAdapter(this));
    }

    public int RunningWorkerCount => _registry.RunningWorkerCount;
    public WorkerResidencyBudgetSnapshot ResidencyBudget => _registry.ResidencyBudget;
    internal int ArtworkRegistrationCount => _appLibraryArtwork?.RegistrationCount ?? 0;

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
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _sessionCancellation = sessionCancellation.Token;
        Volatile.Write(ref _activeSessionCancellation, sessionCancellation);
        await using var requestDispatcher = new BridgeRequestDispatcher(
            sessionCancellation.Token,
            _ => sessionCancellation.Cancel());

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
            while (!sessionCancellation.IsCancellationRequested)
            {
                var request = await _channel.ReadAsync(sessionCancellation.Token)
                    .ConfigureAwait(false);
                requestDispatcher.DemandRequestIdAvailable(request.RequestId);
                var requestKey = BridgeRequestClassifier.Classify(request);
                if (requestKey.Kind == BridgeRequestKind.Stop)
                {
                    await ReplyAsync(
                            BridgeMessageTypes.Acknowledged,
                            request.RequestId,
                            new { },
                            sessionCancellation.Token)
                        .ConfigureAwait(false);
                    break;
                }

                var dispatch = requestDispatcher.TryDispatch(
                    request.RequestId,
                    requestKey,
                    token => DispatchRequestAsync(request, requestKey, token));
                if (dispatch.Status == BridgeRequestDispatchStatus.CapacityExceeded)
                {
                    await ReplyAsync(
                            BridgeMessageTypes.Error,
                            request.RequestId,
                            new BridgeError(
                                "bridge_busy",
                                $"The bridge already has " +
                                $"{BridgeRequestDispatcher.MaximumConcurrentRequests} requests in progress."),
                            sessionCancellation.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (
            requestDispatcher.FatalException is not null)
        {
        }
        finally
        {
            sessionCancellation.Cancel();
            await requestDispatcher.CancelAndDrainAsync().ConfigureAwait(false);
            if (_catalogMonitor is not null) _catalogMonitor.Changed -= OnCatalogChanged;
            if (_appearance is not null) _appearance.Changed -= OnAppearanceChanged;
            _channel = null;
            try
            {
                await _registry.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _activeSessionCancellation, null);
            }
        }

        if (requestDispatcher.FatalException is { } fatal)
            ExceptionDispatchInfo.Capture(fatal).Throw();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _registry.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task HandleRequestAsync(BridgeEnvelope request, CancellationToken cancellationToken)
    {
        switch (request.Type)
        {
        case BridgeMessageTypes.ListWidgets:
            var (listCatalog, listRevision) = _registry.CatalogSnapshot();
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
        {
            var snapshotRequest = BridgeJson.FromElement<WidgetIdRequest>(request.Payload);
            using var snapshotPublication = await _registry.GetSnapshotAsync(
                    snapshotRequest.WidgetId, _sessionCancellation, cancellationToken)
                .ConfigureAwait(false);
            await ReplySnapshotAsync(
                request.RequestId,
                snapshotRequest.WidgetId,
                snapshotPublication.Value,
                cancellationToken).ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.ResolveArtwork:
        {
            if (_appLibraryArtwork is null)
                throw new BridgeProtocolException("Trusted application artwork is unavailable.");
            var artworkRequest = BridgeJson.FromElement<BridgeArtworkRequest>(request.Payload);
            if (!AppLibraryArtworkRegistry.IsHandle(artworkRequest.ArtworkHandle))
                throw new BridgeProtocolException("Artwork handle is invalid.");
            using var artworkPublication = _registry.AdmitArtwork(
                artworkRequest.WidgetId);
            var configured = artworkPublication.Value;
            var identity = new BrokerWidgetIdentity(
                configured.PackageId, configured.PublisherId, configured.InstanceId);
            var pngBase64 = await _appLibraryArtwork.ResolveAsync(
                identity, artworkRequest.ArtworkHandle, cancellationToken)
                .ConfigureAwait(false);
            await ReplyAsync(
                BridgeMessageTypes.Artwork,
                request.RequestId,
                new
                {
                    widgetId = artworkRequest.WidgetId,
                    artworkHandle = artworkRequest.ArtworkHandle,
                    pngBase64,
                },
                cancellationToken).ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.RestartWidget:
        {
            var restartRequest = BridgeJson.FromElement<WidgetIdRequest>(request.Payload);
            using var restartPublication = await _registry.RestartAsync(
                restartRequest.WidgetId, cancellationToken).ConfigureAwait(false);
            var restoredState = restartPublication.Value;
            await ReplyAsync(
                BridgeMessageTypes.Acknowledged,
                request.RequestId,
                new { widgetId = restartRequest.WidgetId, state = restoredState },
                cancellationToken).ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.SetWidgetLifecycle:
        {
            var lifecycleRequest = BridgeJson.FromElement<BridgeWidgetLifecycleRequest>(request.Payload);
            ValidateHostState(lifecycleRequest.State);
            if (lifecycleRequest.AdmitSnapshot)
            {
                using var presentationPublication = await _registry
                    .EstablishPresentationAsync(
                        lifecycleRequest.WidgetId,
                        lifecycleRequest.State,
                        _sessionCancellation,
                        cancellationToken)
                    .ConfigureAwait(false);
                await ReplySnapshotAsync(
                    request.RequestId,
                    lifecycleRequest.WidgetId,
                    presentationPublication.Value,
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            using var lifecyclePublication = await _registry.SetLifecycleAsync(
                    lifecycleRequest.WidgetId,
                    lifecycleRequest.State,
                    _sessionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.Action:
        {
            var actionRequest = BridgeJson.FromElement<BridgeActionRequest>(request.Payload);
            using var actionPublication = await _registry.AdmitActionAsync(
                    actionRequest.WidgetId,
                    actionRequest.Action,
                    _sessionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
            var actionAdmission = actionPublication.Value;
            await ReplyActionAdmissionAsync(
                    request.RequestId, actionAdmission, cancellationToken)
                .ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.ControllerInput:
        {
            var controllerRequest = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            ValidateControllerInput(controllerRequest.Input);
            using var controllerPublication = await _registry.SendControllerInputAsync(
                    controllerRequest.WidgetId,
                    controllerRequest.Input,
                    _sessionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
            var handled = controllerPublication.Value;
            await ReplyAsync(BridgeMessageTypes.ControllerInputResult, request.RequestId,
                    new { handled }, cancellationToken).ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.QuickAction:
        {
            var quickRequest = BridgeJson.FromElement<BridgeQuickActionRequest>(request.Payload);
            using var quickPublication = await _registry.AdmitQuickActionAsync(
                    quickRequest.WidgetId,
                    quickRequest.QuickActionId,
                    quickRequest.Sequence,
                    quickRequest.MonotonicTimestampMicroseconds,
                    _sessionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
            var quickAdmission = quickPublication.Value;
            await ReplyActionAdmissionAsync(request.RequestId, quickAdmission, cancellationToken)
                .ConfigureAwait(false);
            break;
        }
        default:
            throw new BridgeProtocolException($"Unknown bridge request type '{request.Type}'.");
        }
    }

    private async Task DispatchRequestAsync(
        BridgeEnvelope request,
        BridgeRequestKey requestKey,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!requestKey.IsKnown)
                throw new BridgeProtocolException(requestKey.Kind == BridgeRequestKind.Unknown
                    ? $"Unknown bridge request type '{request.Type}'."
                    : $"Bridge request '{request.Type}' has an invalid payload.");
            await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                await ReplyAsync(
                        BridgeMessageTypes.Error,
                        request.RequestId,
                        new BridgeError("request_failed", SafeMessage(exception)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ReplyActionAdmissionAsync(
        long requestId,
        WidgetOperationAdmission admission,
        CancellationToken cancellationToken)
    {
        var (type, payload) = admission switch
        {
            WidgetOperationAdmission.RejectedInactive => (
                BridgeMessageTypes.Error,
                BridgeJson.ToElement(new BridgeError(
                    "action_inactive", "The widget is not active."))),
            WidgetOperationAdmission.RejectedCapacity => (
                BridgeMessageTypes.Error,
                BridgeJson.ToElement(new BridgeError(
                    "action_saturated", "The widget action queue is full."))),
            WidgetOperationAdmission.Enqueued or WidgetOperationAdmission.Replaced => (
                BridgeMessageTypes.Acknowledged,
                BridgeJson.ToElement(new { admission })),
            _ => throw new BridgeProtocolException(
                $"Worker returned invalid action admission '{admission}'."),
        };
        await SendAsync(new BridgeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = payload,
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsTrustedSettings(ConfiguredWidget configured) =>
        !configured.RequiresAppContainer &&
        configured.DeclaredCapabilities.Count == 0 &&
        string.Equals(configured.Id, "settings", StringComparison.Ordinal) &&
        string.Equals(configured.PackageId, "org.gbar.firstparty.settings", StringComparison.Ordinal) &&
        string.Equals(configured.PublisherId, "org.gbar.firstparty", StringComparison.Ordinal);

    private IBridgeWidgetClient CreateWidgetClient(
        ConfiguredWidget configured,
        Func<IDisposable> processLeaseFactory)
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
            MemoryLimitBytes = checked((long)configured.MemoryLimitMb * 1024 * 1024),
            StartupExitDiagnostics = configured.UsesGenericWorkerHost
                ? WidgetWorkerStartupDiagnostics.LoaderExitCodes
                : new Dictionary<int, string>(),
            ProcessLeaseFactory = processLeaseFactory,
            ContentLeaseFactory = configured.ContentLeaseFactory,
            IsolationPolicy = configured.RequiresAppContainer
                ? WidgetWorkerIsolationPolicy.RequireAppContainer
                : WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            CompanionSessionFactory = IsTrustedSettings(configured)
                ? context => new DiagnosticsWidgetProcessCompanion(
                    _diagnostics.CreateAsync,
                    _authorityRecovery.RetryAsync,
                    context)
                : _consentStore is null || _platformBackend is null
                    ? null
                    : CreateCompanionFactory(configured),
        });
        return new WidgetProcessBridgeClient(client);
    }

    private Task PublishClientInvalidation(
        BridgeClientInvalidation invalidation,
        CancellationToken cancellationToken) =>
        SendEventAsync(
            BridgeMessageTypes.Invalidation,
            new BridgeInvalidation(invalidation.WidgetId, invalidation.Revision),
            cancellationToken);

    private Task PublishClientActionFailure(
        BridgeClientActionFailure item,
        CancellationToken cancellationToken) =>
        SendEventAsync(
            BridgeMessageTypes.Failure,
            new
            {
                widgetId = item.WidgetId,
                runtimeGeneration = item.RuntimeGeneration,
                reason = "controllerActionFailed",
                item.Failure.ActionId,
                item.Failure.SourceElementId,
                item.Failure.Message,
                canRestart = false,
            },
            cancellationToken);

    private Task PublishClientFailure(
        BridgeClientRuntimeFailure item,
        CancellationToken cancellationToken) =>
        SendEventAsync(
            BridgeMessageTypes.Failure,
            new
            {
                widgetId = item.WidgetId,
                reason = item.Failure.Reason,
                item.Failure.ExitCode,
                item.Failure.DiagnosticCode,
                item.Failure.RestartsUsed,
                item.Failure.CanRestart,
            },
            cancellationToken);

    private async Task ReplySnapshotAsync(
        long requestId,
        string widgetId,
        BridgeClientSnapshot snapshotResult,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshotResult.Snapshot;
        var configuredForStyle = snapshotResult.Configured;
        var theme = _appearance is null
            ? configuredForStyle.CompiledTheme
            : _appearance.ResolveWidgetTheme(
                configuredForStyle.Id, configuredForStyle.StylePackage);
        var renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, theme);
        var snapshotBytes = SnapshotJson.Serialize(snapshot);
        using var document = JsonDocument.Parse(snapshotBytes);
        await SendAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Snapshot,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                widgetId,
                snapshot = document.RootElement.Clone(),
                renderStyles,
            }),
        }, cancellationToken).ConfigureAwait(false);
    }

    private Func<WidgetProcessCompanionContext, IWidgetProcessCompanionSession>
        CreateCompanionFactory(ConfiguredWidget configured)
    {
        if (_consentStore is null || _platformBackend is null)
            throw new BridgeProtocolException(
                $"Widget '{configured.Id}' requires host services, but the broker is unavailable.");
        return context => new BrokerWidgetProcessCompanion(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.DeclaredCapabilities,
            _consentStore,
            _platformBackend,
            context,
            artworkRegistry: _appLibraryArtwork!,
            hostEffectSink: effect => PublishHostEffect(
                configured.Id, configured.WorkerFingerprint, effect));
    }

    private void PublishHostEffect(
        string widgetId,
        string expectedWorkerFingerprint,
        BrokerHostEffect effect)
    {
        if (effect.Kind != BrokerHostEffectKind.CloseOverlayAfterAppLaunch) return;
        _ = PublishHostEffectAsync(widgetId, expectedWorkerFingerprint);
    }

    private async Task PublishHostEffectAsync(
        string widgetId,
        string expectedWorkerFingerprint)
    {
        using var publication = _registry.TryAdmitHostEffect(
            widgetId, expectedWorkerFingerprint);
        if (publication is null) return;
        var descriptor = publication.Value;
        await SendEventAsync(
            BridgeMessageTypes.HostEffect,
            new BridgeHostEffect(
                widgetId,
                descriptor.RuntimeGeneration,
                "closeOverlayAfterAppLaunch",
                Interlocked.Increment(ref _hostEffectSequence))).ConfigureAwait(false);
    }

    private async Task SendEventAsync<T>(string type, T payload)
    {
        await SendEventAsync(type, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SendEventAsync<T>(
        string type,
        T payload,
        CancellationToken publicationCancellation)
    {
        try
        {
            await _frameWriter.WriteAsync(new BridgeEnvelope
            {
                Type = type,
                Payload = BridgeJson.ToElement(payload),
            }, publicationCancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or
                                               OperationCanceledException or
                                               ObjectDisposedException or
                                               InvalidOperationException)
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
        if (_registry.ApplyCatalog(catalog, revision) && publishEvent) _ = SendEventAsync(
            BridgeMessageTypes.CatalogChanged,
            new BridgeCatalogChangedEvent(revision));
    }

    private Task ReplyAsync<T>(string type, long requestId, T payload, CancellationToken cancellationToken) =>
        SendAsync(new BridgeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(payload),
        }, cancellationToken);

    private async Task SendAsync(BridgeEnvelope envelope, CancellationToken cancellationToken)
    {
        await _frameWriter.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private sealed class ServerFrameWriteAdapter(WidgetBridgeServer owner)
        : IBridgeFrameWriteAdapter
    {
        public BridgeFrameChannel? Channel => owner._channel;
        public CancellationToken SessionCancellation => owner._sessionCancellation;
        public Task AcquireWriterAsync(CancellationToken cancellationToken) =>
            owner._writeGate.WaitAsync(cancellationToken);
        public void ReleaseWriter() => owner._writeGate.Release();
        public CancellationTokenSource CreateDeadline(TimeSpan timeout)
        {
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                owner._sessionCancellation);
            deadline.CancelAfter(timeout);
            return deadline;
        }
        public void AbortSession() =>
            Volatile.Read(ref owner._activeSessionCancellation)?.Cancel();
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
        if (input.RequestedValue is { } requested && !double.IsFinite(requested))
            throw new BridgeProtocolException("Requested controller value must be finite.");
        if (!Enum.IsDefined(input.Origin))
            throw new BridgeProtocolException("Controller input origin is invalid.");
        if (input.Context == ControllerInputContext.DashboardQuickAction &&
            (input.Sequence <= 0 || input.SnapshotSequence <= 0))
            throw new BridgeProtocolException(
                "Dashboard input requires positive input and snapshot sequences.");
        if (input.Context == ControllerInputContext.DashboardQuickAction && input.Button is
            ControllerButton.A or ControllerButton.B or ControllerButton.Y or
            ControllerButton.DPadUp or ControllerButton.DPadDown or
            ControllerButton.DPadLeft or ControllerButton.DPadRight)
            throw new BridgeProtocolException(
                "A, B, Y, and D-pad input are owned by the dashboard and cannot be forwarded.");
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
        var message = exception is WidgetProcessException or
            WidgetProcessAdmissionException or BridgeProtocolException
            ? exception.Message
            : "Widget request failed.";
        if (message.Length > 512) message = message[..512];
        return message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }

}
