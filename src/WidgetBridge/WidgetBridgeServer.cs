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
    private readonly LauncherExperienceSelectionService? _launcherExperience;
    private readonly ConsentStore? _consentStore;
    private readonly IPlatformBrokerBackend? _platformBackend;
    private readonly AppLibraryArtworkRegistry? _appLibraryArtwork;
    private readonly BridgeCatalogMonitor? _catalogMonitor;
    private readonly BridgeClientRegistry _registry;
    private readonly BridgeDiagnosticsProjection _diagnostics;
    private readonly BridgeAuthorityRecoveryProjection _authorityRecovery;
    private readonly BridgeWidgetLocalDataService _localData;
    private readonly BridgeWidgetPackageUninstallService _packageUninstall;
    private readonly BridgeLocalWidgetPackageImportService? _localPackageImport;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly BridgeFrameWriteBoundary _frameWriter;
    private readonly Action<string, BrokerCapabilityDiagnostic>? _capabilityDiagnosticSink;
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
        WorkerResidencyBudgetOptions? residencyBudget = null,
        LauncherExperienceSelectionService? launcherExperience = null)
        : this(
            pipeName,
            catalog,
            maximumMessageBytes,
            appearance,
            consentStore,
            platformBackend,
            catalogMonitor,
            residencyBudget,
            launcherExperience,
            capabilityDiagnosticSink: null)
    {
    }

    internal WidgetBridgeServer(
        string pipeName,
        BridgeCatalog catalog,
        int maximumMessageBytes,
        PlatformAppearanceService? appearance,
        ConsentStore? consentStore,
        IPlatformBrokerBackend? platformBackend,
        BridgeCatalogMonitor? catalogMonitor,
        WorkerResidencyBudgetOptions? residencyBudget,
        LauncherExperienceSelectionService? launcherExperience,
        Action<string, BrokerCapabilityDiagnostic>? capabilityDiagnosticSink)
    {
        _pipeName = ValidatePipeName(pipeName);
        _maximumMessageBytes = maximumMessageBytes is >= 256 and <=
            BridgeProtocol.AbsoluteMaximumMessageBytes
                ? maximumMessageBytes
                : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        _appearance = appearance;
        _launcherExperience = launcherExperience;
        _consentStore = consentStore;
        _platformBackend = platformBackend;
        _appLibraryArtwork = platformBackend is null ? null : new AppLibraryArtworkRegistry();
        _catalogMonitor = catalogMonitor;
        _capabilityDiagnosticSink = capabilityDiagnosticSink;
        _registry = new BridgeClientRegistry(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            residencyBudget ?? new WorkerResidencyBudgetOptions(),
            CreateWidgetClient,
            PublishClientInvalidation,
            PublishClientActionFailure,
            PublishClientFailure);
        _authorityRecovery = new BridgeAuthorityRecoveryProjection(
            AppContainerAuthorityRecoveryService.Default);
        _localData = new BridgeWidgetLocalDataService(
            _registry, _platformBackend, _catalogMonitor);
        _packageUninstall = new BridgeWidgetPackageUninstallService(
            _catalogMonitor is null
                ? null
                : new GameBarAlternative.WidgetCatalog.WidgetCatalog(
                    _catalogMonitor.InstalledCatalogRoot),
            _catalogMonitor);
        _localPackageImport = _catalogMonitor is null
            ? null
            : new BridgeLocalWidgetPackageImportService(
                _registry,
                new GameBarAlternative.WidgetCatalog.WidgetCatalog(
                    _catalogMonitor.InstalledCatalogRoot),
                _catalogMonitor,
                result => SendEventAsync(
                    BridgeMessageTypes.LocalWidgetPackageInstallCompleted,
                    result));
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
    internal ValueTask<PlatformWidgetLocalDataInspection> InspectWidgetLocalDataAsync(
        string widgetId,
        CancellationToken cancellationToken = default) =>
        _localData.InspectAsync(widgetId, cancellationToken);
    internal ValueTask<PlatformWidgetLocalDataClearResult> ClearWidgetLocalDataAsync(
        string widgetId,
        string confirmationToken,
        CancellationToken cancellationToken = default) =>
        _localData.ClearAsync(widgetId, confirmationToken, cancellationToken);
    internal ValueTask<PlatformWidgetPackageUninstallInspection>
        InspectWidgetPackageUninstallAsync(
            string widgetId,
            CancellationToken cancellationToken = default) =>
        _packageUninstall.InspectAsync(widgetId, cancellationToken);
    internal ValueTask<PlatformWidgetPackageUninstallResult> UninstallWidgetPackageAsync(
        string widgetId,
        string publisherId,
        string activeVersion,
        string confirmationToken,
        CancellationToken cancellationToken = default) =>
        _packageUninstall.UninstallAsync(
            widgetId, publisherId, activeVersion, confirmationToken, cancellationToken);

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
        if (_launcherExperience is not null)
            _launcherExperience.Changed += OnLauncherExperienceChanged;
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

                BridgeProtectedWifiRequest? protectedWifiRequest = null;
                BridgeProtectedWifiSecret? protectedWifiSecret = null;
                if (requestKey.Kind == BridgeRequestKind.ConnectProtectedWifi)
                {
                    protectedWifiRequest = BridgeJson.FromElement<BridgeProtectedWifiRequest>(
                        request.Payload);
                    protectedWifiSecret = await _channel.ReadProtectedWifiSecretAsync(
                        protectedWifiRequest.SecretLength,
                        sessionCancellation.Token).ConfigureAwait(false);
                }
                BridgeRequestDispatch dispatch;
                try
                {
                    var capturedProtectedWifiRequest = protectedWifiRequest;
                    var capturedProtectedWifiSecret = protectedWifiSecret;
                    dispatch = requestDispatcher.TryDispatch(
                        request.RequestId,
                        requestKey,
                        token => capturedProtectedWifiRequest is not null &&
                                 capturedProtectedWifiSecret is not null
                            ? DispatchProtectedWifiRequestAsync(
                                request, requestKey, capturedProtectedWifiRequest,
                                capturedProtectedWifiSecret, token)
                            : DispatchRequestAsync(request, requestKey, token));
                    if (dispatch.Status == BridgeRequestDispatchStatus.Accepted)
                        protectedWifiSecret = null;
                }
                finally
                {
                    protectedWifiSecret?.Dispose();
                }
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
            if (_launcherExperience is not null)
                _launcherExperience.Changed -= OnLauncherExperienceChanged;
            _channel = null;
            try
            {
                if (_localPackageImport is not null)
                    await _localPackageImport.DisposeAsync().ConfigureAwait(false);
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
        if (_localPackageImport is not null)
            await _localPackageImport.DisposeAsync().ConfigureAwait(false);
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
        case BridgeMessageTypes.GetLauncherExperience:
            if (_launcherExperience is null)
                throw new BridgeProtocolException("Launcher Experience selection is unavailable.");
            await ReplyAsync(
                BridgeMessageTypes.LauncherExperience,
                request.RequestId,
                _launcherExperience.CreatePayload(),
                cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.SelectLauncherExperience:
            if (_launcherExperience is null)
                throw new BridgeProtocolException("Launcher Experience selection is unavailable.");
            await ReplyAsync(
                BridgeMessageTypes.LauncherExperience,
                request.RequestId,
                await _launcherExperience.ApplySelectionAsync(
                    BridgeJson.FromElement<BridgeLauncherExperienceSelectionRequest>(
                        request.Payload),
                    cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            break;
        case BridgeMessageTypes.GetSnapshot:
        {
            var snapshotRequest = BridgeJson.FromElement<BridgePresentationRequest>(request.Payload);
            var capabilities = snapshotRequest.Capabilities ??
                PresentationUpdateCapabilities.None;
            using var snapshotPublication = await _registry.GetPresentationAsync(
                    snapshotRequest.WidgetId,
                    capabilities,
                    snapshotRequest.BaseSequence,
                    _sessionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
            await ReplyPresentationAsync(
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
            ConfiguredWidget configured;
            using (var artworkAdmission = _registry.AdmitArtwork(artworkRequest.WidgetId))
                configured = artworkAdmission.Value;
            var identity = new BrokerWidgetIdentity(
                configured.PackageId, configured.PublisherId, configured.InstanceId);
            await ReplyAsync(
                BridgeMessageTypes.Acknowledged,
                request.RequestId,
                new { },
                cancellationToken).ConfigureAwait(false);

            string? pngBase64 = null;
            try
            {
                pngBase64 = await _appLibraryArtwork.ResolveAsync(
                    identity, artworkRequest.ArtworkHandle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Publish an unavailable completion only if the exact worker
                // generation remains current. Session shutdown suppresses it.
            }

            using var artworkCompletion = _registry.TryAdmitArtwork(
                artworkRequest.WidgetId, configured.WorkerFingerprint);
            if (artworkCompletion is null ||
                !_appLibraryArtwork.IsCurrent(identity, artworkRequest.ArtworkHandle)) break;
            await SendEventAsync(
                BridgeMessageTypes.Artwork,
                new
                {
                    widgetId = artworkRequest.WidgetId,
                    artworkHandle = artworkRequest.ArtworkHandle,
                    pngBase64 = pngBase64 ?? string.Empty,
                },
                _sessionCancellation).ConfigureAwait(false);
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
        case BridgeMessageTypes.ConnectProtectedWifi:
            throw new BridgeProtocolException(
                "Protected Wi-Fi requests require the dedicated secret-frame owner.");
        case BridgeMessageTypes.InstallLocalWidgetPackage:
        {
            if (_localPackageImport is null)
                throw new BridgeProtocolException(
                    "Local widget package installation is unavailable.");
            var installRequest = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallRequest>(
                request.Payload);
            _localPackageImport.Start(installRequest, _sessionCancellation);
            await ReplyAsync(
                BridgeMessageTypes.Acknowledged,
                request.RequestId,
                new { operationId = installRequest.OperationId },
                cancellationToken).ConfigureAwait(false);
            break;
        }
        case BridgeMessageTypes.CancelLocalWidgetPackageInstall:
        {
            if (_localPackageImport is null)
                throw new BridgeProtocolException(
                    "Local widget package installation is unavailable.");
            var cancelRequest = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallCancelRequest>(
                request.Payload);
            var cancelled = _localPackageImport.Cancel(cancelRequest.OperationId);
            await ReplyAsync(
                BridgeMessageTypes.Acknowledged,
                request.RequestId,
                new { operationId = cancelRequest.OperationId, cancelled },
                cancellationToken).ConfigureAwait(false);
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

    private async Task DispatchProtectedWifiRequestAsync(
        BridgeEnvelope envelope,
        BridgeRequestKey requestKey,
        BridgeProtectedWifiRequest request,
        BridgeProtectedWifiSecret secret,
        CancellationToken cancellationToken)
    {
        using (secret)
        try
        {
            if (!requestKey.IsKnown)
                throw new BridgeProtocolException("Protected Wi-Fi request is invalid.");
            if (_platformBackend is not IProtectedWifiHostBackend protectedWifi)
                throw new BridgeProtocolException("Protected Wi-Fi service is unavailable.");
            if (!NetworkControlsHostPolicy.TryParseNetworkId(
                    request.SourceElementId, out var networkId) ||
                secret.Characters.Length != request.SecretLength)
                throw new BridgeProtocolException("Protected Wi-Fi request is invalid.");
            using var publication = _registry.AdmitProtectedWifi(
                request.WidgetId, request.RuntimeGeneration);
            var result = await protectedWifi.ConnectProtectedWifiAsync(
                networkId, secret.Characters, cancellationToken).ConfigureAwait(false);
            secret.Dispose();
            await ReplyAsync(
                BridgeMessageTypes.Acknowledged,
                envelope.RequestId,
                new { status = result.Status, code = result.Code },
                cancellationToken).ConfigureAwait(false);
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
                        envelope.RequestId,
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
            StartupExitDiagnostics = configured.UsesGenericWorkerHost
                ? WidgetWorkerStartupDiagnostics.LoaderExitCodes
                : new Dictionary<int, string>(),
            ProcessLeaseFactory = processLeaseFactory,
            ContentLeaseFactory = configured.ContentLeaseFactory,
            IsolationPolicy = configured.ExecutionTrust ==
                WidgetExecutionTrust.FullTrustCurrentUser
                    ? WidgetWorkerIsolationPolicy.FullTrustCommunity
                    : configured.RequiresAppContainer
                        ? WidgetWorkerIsolationPolicy.RequireAppContainer
                        : WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            CompanionSessionFactory = configured.ExecutionTrust ==
                WidgetExecutionTrust.FullTrustCurrentUser
                    ? null
                : IsTrustedSettings(configured)
                ? context => new DiagnosticsWidgetProcessCompanion(
                    _diagnostics.CreateAsync,
                    _authorityRecovery.RetryAsync,
                    _localData.InspectAsync,
                    _localData.ClearAsync,
                    _packageUninstall.InspectAsync,
                    _packageUninstall.UninstallAsync,
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

    private async Task ReplyPresentationAsync(
        long requestId,
        string widgetId,
        BridgeClientPresentation presentation,
        CancellationToken cancellationToken)
    {
        if (presentation.Update is null)
        {
            await ReplySnapshotAsync(
                requestId,
                widgetId,
                new BridgeClientSnapshot(presentation.Configured, presentation.Snapshot),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var theme = _appearance is null
            ? presentation.Configured.CompiledTheme
            : _appearance.ResolveWidgetTheme(
                presentation.Configured.Id,
                presentation.Configured.StylePackage);
        var renderStyles = BridgeRenderStyleResolver.Resolve(presentation.Snapshot, theme);
        var updateBytes = PresentationUpdateJson.Serialize(presentation.Update);
        using var document = JsonDocument.Parse(updateBytes);
        await SendAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.PresentationUpdate,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                widgetId,
                update = document.RootElement.Clone(),
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
                configured.Id, configured.WorkerFingerprint, effect),
            diagnosticSink: diagnostic =>
                _capabilityDiagnosticSink?.Invoke(configured.Id, diagnostic));
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

    private void OnLauncherExperienceChanged(object? sender, long revision) =>
        _ = SendEventAsync(
            BridgeMessageTypes.LauncherExperienceChanged,
            new BridgeLauncherExperienceChanged(revision));

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
