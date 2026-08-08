using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
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
    private long _diagnosticsRevision;
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
            ViewSnapshot snapshot;
            await snapshotRegistration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var residencyMode = WidgetResidencyPolicies.Resolve(
                    snapshotRegistration.Configured.ResidencyPolicy).Mode;
                var hiddenAndRestricted =
                    snapshotRegistration.HostLifecycle == WidgetLifecycleState.Background &&
                    residencyMode is WidgetResidencyMode.SuspendWhenHidden or
                        WidgetResidencyMode.UnloadAfterIdle;
                if (hiddenAndRestricted)
                {
                    snapshot = snapshotRegistration.CachedSnapshot ??
                        throw new BridgeProtocolException(
                            "A hidden suspended widget has no cached snapshot. Make it Visible before rendering.");
                }
                else
                {
                    snapshotRegistration.CancelIdleUnload();
                    snapshot = await snapshotRegistration.Client.GetSnapshotAsync(cancellationToken)
                        .ConfigureAwait(false);
                    snapshotRegistration.CachedSnapshot = snapshot;
                    ScheduleIdleUnload(snapshotRegistration);
                }
            }
            finally
            {
                snapshotRegistration.OperationGate.Release();
            }
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
            var lifecycleRegistration = GetClient(lifecycleRequest.WidgetId);
            await lifecycleRegistration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lifecycleRegistration.CancelIdleUnload();
                await lifecycleRegistration.Client
                    .SetLifecycleStateAsync(lifecycleRequest.State, cancellationToken).ConfigureAwait(false);
                lifecycleRegistration.HostLifecycle = lifecycleRequest.State;
                ScheduleIdleUnload(lifecycleRegistration);
            }
            finally
            {
                lifecycleRegistration.OperationGate.Release();
            }
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.Action:
            var actionRequest = BridgeJson.FromElement<BridgeActionRequest>(request.Payload);
            var actionRegistration = GetClient(actionRequest.WidgetId);
            await WithResidentClientAsync(actionRegistration, cancellationToken,
                client => client.SendActionAsync(actionRequest.Action, cancellationToken)).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        case BridgeMessageTypes.ControllerInput:
            var controllerRequest = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            ValidateControllerInput(controllerRequest.Input);
            var controllerRegistration = GetClient(controllerRequest.WidgetId);
            var handled = await WithResidentClientAsync(controllerRegistration, cancellationToken,
                client => client.SendControllerInputAsync(
                    controllerRequest.Input,
                    ResolveDashboardGestureAuthority(
                        controllerRegistration, controllerRequest.Input),
                    cancellationToken))
                .ConfigureAwait(false);
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
            await WithResidentClientAsync(quickRegistration, cancellationToken,
                client => client.SendActionAsync(new WidgetActionEvent(
                    quickAction.ActionId,
                    quickAction.SourceElementId,
                    quickAction.ControllerButton,
                    ControllerEventPhase.Pressed,
                    quickRequest.Sequence,
                    quickRequest.MonotonicTimestampMicroseconds), cancellationToken)).ConfigureAwait(false);
            await ReplyAsync(BridgeMessageTypes.Acknowledged, request.RequestId, new { }, cancellationToken)
                .ConfigureAwait(false);
            break;
        default:
            throw new BridgeProtocolException($"Unknown bridge request type '{request.Type}'.");
        }
    }

    private async Task WithResidentClientAsync(
        ClientRegistration registration,
        CancellationToken cancellationToken,
        Func<WidgetProcessClient, Task> operation)
    {
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandInteractionAllowed(registration);
            registration.CancelIdleUnload();
            await operation(registration.Client).ConfigureAwait(false);
            ScheduleIdleUnload(registration);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private async Task<T> WithResidentClientAsync<T>(
        ClientRegistration registration,
        CancellationToken cancellationToken,
        Func<WidgetProcessClient, Task<T>> operation)
    {
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandInteractionAllowed(registration);
            registration.CancelIdleUnload();
            var result = await operation(registration.Client).ConfigureAwait(false);
            ScheduleIdleUnload(registration);
            return result;
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private static void DemandInteractionAllowed(ClientRegistration registration)
    {
        if (registration.HostLifecycle != WidgetLifecycleState.Background) return;
        var mode = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode;
        if (mode is WidgetResidencyMode.SuspendWhenHidden or WidgetResidencyMode.UnloadAfterIdle)
            throw new BridgeProtocolException(
                "Hidden interaction is disabled by this widget's residency policy.");
    }

    private void ScheduleIdleUnload(ClientRegistration registration)
    {
        var policy = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy);
        if (policy.Mode != WidgetResidencyMode.UnloadAfterIdle ||
            registration.HostLifecycle != WidgetLifecycleState.Background ||
            !registration.Client.IsRunning || policy.IdleDuration is not { } delay)
            return;

        var (generation, cancellation) = registration.BeginIdleUnload(_sessionCancellation);
        _ = RunIdleUnloadAsync(registration, generation, delay, cancellation.Token);
    }

    private async Task RunIdleUnloadAsync(
        ClientRegistration registration,
        long generation,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!registration.IsIdleUnloadCurrent(generation) ||
                    !IsCurrent(registration) ||
                    registration.HostLifecycle != WidgetLifecycleState.Background ||
                    !registration.Client.IsRunning ||
                    WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode !=
                        WidgetResidencyMode.UnloadAfterIdle)
                    return;

                using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                shutdown.CancelAfter(TimeSpan.FromSeconds(3));
                await registration.Client.UnloadAsync(shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                registration.OperationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Visibility, a new operation, catalog replacement, or bridge
            // shutdown canceled the eviction. None is a worker failure.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Unload is best effort and bounded. WidgetProcessClient terminates
            // an uncooperative process tree; the cached snapshot remains valid.
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
                CompanionSessionFactory = IsTrustedSettings(configured)
                    ? context => new DiagnosticsWidgetProcessCompanion(
                        CreateDiagnosticsSnapshotAsync, context)
                    : _consentStore is null || _platformBackend is null
                        ? null
                        : CreateCompanionFactory(configured),
            });
            var registration = new ClientRegistration(configured, client);
            client.Invalidated += (_, revision) =>
            {
                if (IsCurrent(registration) && registration.MayPublishInvalidation) _ = SendEventAsync(
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
                registration.RecordFailure(failure);
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

    internal static bool IsTrustedSettings(ConfiguredWidget configured) =>
        !configured.RequiresAppContainer &&
        configured.DeclaredCapabilities.Count == 0 &&
        string.Equals(configured.Id, "settings", StringComparison.Ordinal) &&
        string.Equals(configured.PackageId, "org.gbar.firstparty.settings", StringComparison.Ordinal) &&
        string.Equals(configured.PublisherId, "org.gbar.firstparty", StringComparison.Ordinal);

    private async ValueTask<PlatformDiagnosticsSnapshot> CreateDiagnosticsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        BridgeCatalog catalog;
        long catalogRevision;
        lock (_catalogGate)
        {
            catalog = _catalog;
            catalogRevision = _catalogRevision;
        }

        var workers = catalog.Widgets.Select(descriptor =>
        {
            _clients.TryGetValue(descriptor.Id, out var registration);
            var failure = registration?.LastFailure;
            return new PlatformWorkerDiagnostic(
                descriptor.Id,
                descriptor.Name,
                registration?.Client.IsRunning == true,
                registration?.Client.Starts ?? 0,
                failure?.Code,
                failure?.CanRestart ?? false);
        }).ToArray();

        PlatformDiagnosticArea consent;
        if (_consentStore is null)
        {
            consent = Area("consent", "Permissions", PlatformDiagnosticState.Unavailable,
                "Permission service is not configured");
        }
        else
        {
            try
            {
                var document = await _consentStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var denied = document.Entries.Count(entry => entry.Decision == ConsentDecision.Deny);
                consent = Area("consent", "Permissions", PlatformDiagnosticState.Healthy,
                    $"Revision {document.Revision}; {document.Entries.Count} decisions; {denied} denied");
            }
            catch (Exception exception) when (exception is BrokerException or
                                                   IOException or UnauthorizedAccessException)
            {
                var code = exception is BrokerException broker ? broker.Code :
                    exception is UnauthorizedAccessException ? "access_denied" : "io_error";
                consent = Area("consent", "Permissions", PlatformDiagnosticState.Degraded,
                    $"Permission state is unavailable ({SafeCode(code)})");
            }
        }

        var catalogDiagnostics = _catalogMonitor?.LastDiagnostics ?? [];
        var retained = _catalogMonitor?.RetainedLastGood == true;
        var catalogState = retained || catalogDiagnostics.Count != 0
            ? PlatformDiagnosticState.Degraded
            : PlatformDiagnosticState.Healthy;
        var catalogSummary = retained
            ? $"Revision {catalogRevision}; retained last good after a rejected reload"
            : catalogDiagnostics.Count == 0
                ? $"Revision {catalogRevision}; {catalog.Widgets.Count} widgets validated"
                : $"Revision {catalogRevision}; {catalogDiagnostics.Count} bounded warnings";

        var appearanceErrors = _appearance?.LastReloadDiagnostics.Count(item =>
            item.Severity == GameBarAlternative.WidgetStyling.GbssDiagnosticSeverity.Error) ?? 0;
        var appearance = _appearance is null
            ? Area("appearance", "Appearance", PlatformDiagnosticState.Unavailable,
                "Appearance service is not configured")
            : appearanceErrors == 0
                ? Area("appearance", "Appearance", PlatformDiagnosticState.Healthy,
                    $"Revision {_appearance.Current.Revision}; active theme validated")
                : Area("appearance", "Appearance", PlatformDiagnosticState.Degraded,
                    $"Revision {_appearance.Current.Revision}; retained last good after {appearanceErrors} errors");

        return new PlatformDiagnosticsSnapshot(
            PlatformDiagnosticsSnapshot.CurrentSchemaVersion,
            Interlocked.Increment(ref _diagnosticsRevision),
            Area("bridge", "Bridge", PlatformDiagnosticState.Healthy,
                "Native host session is connected"),
            Area("catalog", "Widget catalog", catalogState, catalogSummary),
            appearance,
            _platformBackend is null
                ? Area("providers", "Platform providers", PlatformDiagnosticState.Unavailable,
                    "Audio and network providers are not configured")
                : Area("providers", "Platform providers", PlatformDiagnosticState.Healthy,
                    "Audio and network providers are available on demand"),
            consent,
            Area("overlay", "Overlay host", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            Area("guide", "Guide input", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            workers);
    }

    private static PlatformDiagnosticArea Area(
        string id,
        string label,
        PlatformDiagnosticState state,
        string summary) => PlatformDiagnosticsSnapshot.Area(id, label, state, summary);

    private static string SafeCode(string value)
    {
        var safe = new string(value.Take(64).Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-').ToArray());
        return safe.Length == 0 ? "unavailable" : safe;
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
                await DisposeRegistrationAsync(registration).ConfigureAwait(false);
        }
    }

    private static async Task DisposeRegistrationAsync(ClientRegistration registration)
    {
        registration.CancelIdleUnload();
        await registration.OperationGate.WaitAsync().ConfigureAwait(false);
        try { await registration.Client.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // A replaced worker is already unreachable; disposal is best effort.
        }
        finally
        {
            registration.OperationGate.Release();
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
        if (input.RequestedValue is { } requested && !double.IsFinite(requested))
            throw new BridgeProtocolException("Requested controller value must be finite.");
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

    private static WidgetDashboardGestureAuthority? ResolveDashboardGestureAuthority(
        ClientRegistration registration,
        ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.DashboardQuickAction) return null;
        if (registration.HostLifecycle != WidgetLifecycleState.Visible)
            throw new BridgeProtocolException(
                "Dashboard quick actions require the widget to remain Visible.");
        var snapshot = registration.CachedSnapshot ?? throw new BridgeProtocolException(
            "Dashboard quick action has no cached rendered snapshot.");
        if (snapshot.Sequence != input.SnapshotSequence)
            throw new BridgeProtocolException(
                "Dashboard quick action targets a stale snapshot sequence.");
        var quickAction = snapshot.QuickActions.SingleOrDefault(
            action => action.Button == input.Button) ?? throw new BridgeProtocolException(
                "Dashboard button is not exposed by the cached snapshot.");
        if (input.Phase != ControllerEventPhase.Pressed || quickAction.Capability is null)
        {
            registration.AcceptDashboardInputSequence(input.Sequence);
            return null;
        }

        var requested = quickAction.Capability;
        if (!PlatformCapabilities.TryGet(requested.CapabilityId, out var capability) ||
            capability.KindForOperation(requested.OperationId) != BrokerCapabilityKind.Control ||
            !capability.Operations.Contains(requested.OperationId))
            throw new BridgeProtocolException(
                "Dashboard quick action names an unsupported control operation.");
        if (!registration.Configured.DeclaredCapabilities.Contains(
                requested.CapabilityId, StringComparer.Ordinal))
            throw new BridgeProtocolException(
                "Dashboard quick action capability is not declared by its package.");

        registration.AcceptDashboardInputSequence(input.Sequence);
        return new WidgetDashboardGestureAuthority(
            requested.CapabilityId,
            requested.OperationId,
            input.Sequence,
            input.SnapshotSequence,
            PlatformCapabilityBroker.MaximumDashboardGestureLifetime);
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
        public SemaphoreSlim OperationGate { get; } = new(1, 1);
        private int _hostLifecycle = (int)WidgetLifecycleState.Background;
        public WidgetLifecycleState HostLifecycle
        {
            get => (WidgetLifecycleState)Volatile.Read(ref _hostLifecycle);
            set => Volatile.Write(ref _hostLifecycle, (int)value);
        }
        public ViewSnapshot? CachedSnapshot { get; set; }
        private long _lastDashboardInputSequence;

        public void AcceptDashboardInputSequence(long sequence)
        {
            if (sequence <= _lastDashboardInputSequence)
                throw new BridgeProtocolException(
                    "Dashboard controller input sequence was replayed.");
            _lastDashboardInputSequence = sequence;
        }
        public bool MayPublishInvalidation =>
            HostLifecycle != WidgetLifecycleState.Background ||
            WidgetResidencyPolicies.Resolve(Configured.ResidencyPolicy).Mode ==
                WidgetResidencyMode.KeepAlive;
        private readonly object _residencyGate = new();
        private CancellationTokenSource? _idleUnloadCancellation;
        private long _idleUnloadGeneration;
        private WorkerFailureDiagnostic? _lastFailure;
        public WorkerFailureDiagnostic? LastFailure => Volatile.Read(ref _lastFailure);

        public void RecordFailure(WidgetFailure failure) => Volatile.Write(
            ref _lastFailure,
            new WorkerFailureDiagnostic(failure.Reason.ToString(), failure.CanRestart));

        public (long Generation, CancellationTokenSource Cancellation) BeginIdleUnload(
            CancellationToken bridgeCancellation)
        {
            lock (_residencyGate)
            {
                CancelIdleUnloadLocked();
                _idleUnloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    bridgeCancellation);
                return (++_idleUnloadGeneration, _idleUnloadCancellation);
            }
        }

        public bool IsIdleUnloadCurrent(long generation)
        {
            lock (_residencyGate)
                return generation == _idleUnloadGeneration &&
                    _idleUnloadCancellation is { IsCancellationRequested: false };
        }

        public void CancelIdleUnload()
        {
            lock (_residencyGate) CancelIdleUnloadLocked();
        }

        private void CancelIdleUnloadLocked()
        {
            _idleUnloadGeneration++;
            if (_idleUnloadCancellation is null) return;
            _idleUnloadCancellation.Cancel();
            _idleUnloadCancellation.Dispose();
            _idleUnloadCancellation = null;
        }

    }

    private sealed record WorkerFailureDiagnostic(string Code, bool CanRestart);
}
