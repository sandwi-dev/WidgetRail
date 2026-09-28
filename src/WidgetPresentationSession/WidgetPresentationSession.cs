using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession : IAsyncDisposable
{
    private readonly BridgePresentationTransport _transport;
    private readonly WidgetPresentationSessionOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, BridgeWidgetDescriptor> _descriptors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WidgetPresentationState> _states =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sessionGenerations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _refreshes = new(StringComparer.Ordinal);
    private readonly Queue<WidgetPresentationState> _statePublications = [];
    private readonly Dictionary<string, PendingArtwork> _artwork = new(StringComparer.Ordinal);
    private readonly Queue<WidgetPresentationDiagnostic> _diagnostics = [];
    private long _catalogRevision = -1;
    private long _notifiedCatalogRevision = -1;
    private long _notifiedAppearanceRevision = -1;
    private long _hostEffectSequence;
    private readonly Dictionary<string, long> _hostEffectAdmissionBoundaries = new(StringComparer.Ordinal);
    private long _publicationRevision;
    private bool _publicationOwner;
    private bool _disposed;
    private Exception? _terminalFailure;

    private sealed record RefreshPublication(
        long CatalogRevision,
        BridgeWidgetDescriptor Descriptor,
        long SessionGeneration,
        WidgetPresentationFrame LastGood,
        long InvalidationRevision,
        long StatePublicationRevision);

    private sealed record BridgeFailurePublication(
        long CatalogRevision,
        BridgeWidgetDescriptor Descriptor,
        long SessionGeneration,
        long? StatePublicationRevision,
        WidgetPresentationAuthority? LastGoodAuthority,
        WidgetPresentationFailure Failure);

    private WidgetPresentationSession(
        BridgePresentationTransport transport,
        WidgetPresentationSessionOptions options)
    {
        _transport = transport;
        _options = options;
        _transport.EventReceived += HandleEvent;
        _transport.Failed += FailTerminal;
        _transport.Start();
    }

    public event EventHandler<WidgetPresentationChangedEventArgs>? PresentationChanged;
    public event EventHandler<WidgetPresentationInvalidatedEventArgs>? Invalidated;
    public event EventHandler<WidgetPresentationArtworkEventArgs>? ArtworkResolved;
    public event EventHandler<WidgetPresentationDiagnosticEventArgs>? DiagnosticPublished;
    /// <summary>Raised on the transport thread. Marshal to the UI thread and recheck authority before acting.</summary>
    public event EventHandler<WidgetHostEffectEventArgs>? HostEffectReceived;
    /// <summary>Fetch the new catalog explicitly; notifications never mutate the admitted catalog.</summary>
    public event EventHandler<WidgetRevisionChangedEventArgs>? CatalogChanged;
    /// <summary>Signals a newer appearance revision; does not contain theme/settings values.</summary>
    public event EventHandler<WidgetRevisionChangedEventArgs>? AppearanceChanged;

    public long LatestCatalogNotificationRevision { get { lock (_gate) return _notifiedCatalogRevision; } }
    public long LatestAppearanceNotificationRevision { get { lock (_gate) return _notifiedAppearanceRevision; } }

    /// <summary>
    /// Rechecks worker membership after dispatching an effect to another thread. This
    /// does not consume the effect or validate foreground/visible-session/window identity.
    /// Hosts must execute each delivered sequence at most once.
    /// </summary>
    public bool IsHostEffectAuthorityCurrent(WidgetHostEffectAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        lock (_gate)
            return !_disposed && _terminalFailure is null &&
                _descriptors.TryGetValue(authority.WidgetId, out var descriptor) &&
                descriptor.InstanceId == authority.WidgetInstanceId &&
                descriptor.RuntimeGeneration == authority.RuntimeGeneration &&
                _sessionGenerations.GetValueOrDefault(authority.WidgetId) == authority.SessionGeneration;
    }

    internal Func<Task>? BridgeFailureCapturedForTesting { get; set; }

    public static async Task<WidgetPresentationSession> ConnectAsync(
        string pipeName,
        WidgetPresentationSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WidgetPresentationSessionOptions();
        options.Validate();
        var transport = await BridgePresentationTransport.ConnectAsync(
            pipeName, options, cancellationToken).ConfigureAwait(false);
        return new WidgetPresentationSession(transport, options);
    }

    public IReadOnlyList<WidgetPresentationDiagnostic> Diagnostics
    {
        get
        {
            lock (_gate) return _diagnostics.ToArray();
        }
    }

    public WidgetPresentationState? GetState(string widgetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        lock (_gate) return _states.GetValueOrDefault(widgetId);
    }

    public WidgetPresentationTarget GetTarget(string widgetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        lock (_gate)
        {
            ThrowIfTerminalLocked();
            if (!_descriptors.TryGetValue(widgetId, out var descriptor))
                throw new WidgetPresentationSessionException(
                    "unknown_widget", $"Widget '{Bound(widgetId)}' is not in the current catalog.");
            return new WidgetPresentationTarget(_catalogRevision, descriptor);
        }
    }

    public async Task<WidgetPresentationCatalog> ListWidgetsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            BridgeMessageTypes.ListWidgets,
            new { },
            BridgeMessageTypes.Widgets,
            cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "revision", "isComplete", "widgets");
        var completeness = response.Payload.GetProperty("isComplete");
        if (completeness.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new BridgeProtocolException("WidgetBridge returned invalid catalog completeness.");
        var revision = ReadInt64(response.Payload, "revision", minimum: 0);
        var widgets = response.Payload.GetProperty("widgets")
            .Deserialize<BridgeWidgetDescriptor[]>(BridgeJson.Options)
            ?? throw new BridgeProtocolException("WidgetBridge returned a null widget catalog.");
        if (widgets.Length > 256 || widgets.Any(widget => widget is null))
            throw new BridgeProtocolException("WidgetBridge returned an invalid widget catalog.");
        var byId = new Dictionary<string, BridgeWidgetDescriptor>(StringComparer.Ordinal);
        foreach (var widget in widgets)
        {
            ValidateDescriptor(widget);
            if (!byId.TryAdd(widget.Id, widget))
                throw new BridgeProtocolException("WidgetBridge returned duplicate widget IDs.");
        }
        var startPublications = false;
        lock (_gate)
        {
            if (revision < _catalogRevision)
                throw new BridgeProtocolException("WidgetBridge returned a stale widget catalog.");
            _catalogRevision = revision;
            foreach (var pair in byId)
            {
                if (!_descriptors.TryGetValue(pair.Key, out var prior))
                {
                    if (_sessionGenerations.ContainsKey(pair.Key))
                        _hostEffectAdmissionBoundaries[pair.Key] = Environment.TickCount64;
                    _sessionGenerations[pair.Key] =
                        _sessionGenerations.GetValueOrDefault(pair.Key) + 1;
                }
                else if (prior.InstanceId != pair.Value.InstanceId ||
                         prior.RuntimeGeneration != pair.Value.RuntimeGeneration ||
                         prior.PresentationGeneration != pair.Value.PresentationGeneration)
                {
                    _hostEffectAdmissionBoundaries[pair.Key] = Environment.TickCount64;
                    _sessionGenerations[pair.Key] =
                        _sessionGenerations.GetValueOrDefault(pair.Key) + 1;
                    startPublications |= RetireStateLocked(pair.Key);
                }
            }
            foreach (var removed in _descriptors.Keys.Where(id => !byId.ContainsKey(id)).ToArray())
            {
                _hostEffectAdmissionBoundaries[removed] = Environment.TickCount64;
                _sessionGenerations[removed] =
                    _sessionGenerations.GetValueOrDefault(removed) + 1;
                startPublications |= RetireStateLocked(removed);
            }
            _descriptors.Clear();
            foreach (var pair in byId) _descriptors.Add(pair.Key, pair.Value);
        }
        if (startPublications) DrainStatePublications();
        return new WidgetPresentationCatalog(revision, Array.AsReadOnly(widgets)) { IsComplete = completeness.GetBoolean() };
    }

    public async Task<WidgetPresentationFrame> EstablishPresentationAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidatePresentationLifecycle(state);
        var sessionGeneration = ValidateTarget(target);
        var lifecycleVersion = BeginIndexedLifecycle(target, state);
        var response = await RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                target.Descriptor.Id,
                state,
                new BridgePresentationEstablishment(
                    PresentationUpdateCapabilities.None,
                    BaseSequence: 0,
                    TransactionKind:
                        WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                    RecoveryOriginSequence: 0)),
            BridgeMessageTypes.Snapshot,
            cancellationToken).ConfigureAwait(false);
        var frame = PublishSnapshot(target.Descriptor, sessionGeneration, response.Payload);
        CompleteIndexedLifecycle(target, sessionGeneration, lifecycleVersion, state);
        return frame;
    }

    public async Task SetLifecycleAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidatePresentationLifecycle(state);
        var lifecycleGeneration = ValidateTarget(target);
        var lifecycleVersion = BeginIndexedLifecycle(target, state);
        await RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(target.Descriptor.Id, state),
            BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        CompleteIndexedLifecycle(target, lifecycleGeneration, lifecycleVersion, state);
        if (state == WidgetLifecycleState.Background)
        {
            lock (_gate)
            {
                if (_states.TryGetValue(target.Descriptor.Id, out var current))
                    _ = CommitStateLocked(current with { Failure = null }, publish: false);
            }
        }
    }

    public async Task<WidgetPresentationFrame> RefreshAsync(
        WidgetPresentationAuthority authority,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ValidateAuthority(authority);
        var response = await RequestAsync(
            BridgeMessageTypes.GetSnapshot,
            new BridgePresentationRequest(
                authority.WidgetId,
                PresentationUpdateCapabilities.None,
                BaseSequence: 0,
                TransactionKind:
                    WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                RecoveryOriginSequence: 0),
            BridgeMessageTypes.Snapshot,
            cancellationToken).ConfigureAwait(false);
        return PublishSnapshot(descriptor, authority.SessionGeneration, response.Payload);
    }

    public async Task<WidgetLifecycleState> RestartAsync(
        WidgetPresentationTarget target,
        CancellationToken cancellationToken = default)
    {
        var sessionGeneration = BeginRestart(target);
        var response = await RequestAsync(
            BridgeMessageTypes.RestartWidget,
            new WidgetIdRequest(target.Descriptor.Id),
            BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "widgetId", "state");
        if (!string.Equals(ReadString(response.Payload, "widgetId"), target.Descriptor.Id,
                StringComparison.Ordinal))
            throw new BridgeProtocolException("WidgetBridge restarted a different widget.");
        var state = response.Payload.GetProperty("state")
            .Deserialize<WidgetLifecycleState>(BridgeJson.Options);
        ValidatePresentationLifecycle(state);
        lock (_gate)
        {
            if (_sessionGenerations.GetValueOrDefault(target.Descriptor.Id) != sessionGeneration)
                throw Stale("presentation_stale", target.Descriptor.Id,
                    "A newer presentation session replaced the restart.");
        }
        return state;
    }

    public async Task<WidgetOperationAdmission> SendActionAsync(
        WidgetPresentationAuthority authority,
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = ValidateAuthority(authority);
        if (!string.Equals(action.InputScopeId, authority.ActiveInputScopeId,
                StringComparison.Ordinal))
            throw Stale("input_scope_stale", authority.WidgetId,
                "The action input scope is not the active presentation scope.");
        var response = await RequestAsync(
            BridgeMessageTypes.Action,
            new BridgeActionRequest(authority.WidgetId, action),
            BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        return ReadAdmission(response.Payload);
    }

    public async Task<bool> SendControllerInputAsync(
        WidgetPresentationAuthority authority,
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        _ = ValidateAuthority(authority);
        if (input.SnapshotSequence != authority.SnapshotSequence)
            throw Stale("snapshot_stale", authority.WidgetId,
                "The controller input snapshot is not current.");
        if (!string.Equals(input.ActiveInputScopeId, authority.ActiveInputScopeId,
                StringComparison.Ordinal))
            throw Stale("input_scope_stale", authority.WidgetId,
                "The controller input scope is not current.");
        var response = await RequestAsync(
            BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest(authority.WidgetId, input),
            BridgeMessageTypes.ControllerInputResult,
            cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "handled");
        return response.Payload.GetProperty("handled").GetBoolean();
    }

    public async Task<WidgetOperationAdmission> InvokeQuickActionAsync(
        WidgetPresentationTarget target,
        string quickActionId,
        long sequence,
        long monotonicTimestampMicroseconds,
        CancellationToken cancellationToken = default)
    {
        _ = ValidateTarget(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(quickActionId);
        if (sequence < 0 || monotonicTimestampMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!target.Descriptor.QuickActions.Any(action =>
                string.Equals(action.Id, quickActionId, StringComparison.Ordinal)))
            throw new WidgetPresentationSessionException(
                "unknown_quick_action", "The quick action is not declared by the current widget.");
        var response = await RequestAsync(
            BridgeMessageTypes.QuickAction,
            new BridgeQuickActionRequest(
                target.Descriptor.Id, quickActionId, sequence, monotonicTimestampMicroseconds),
            BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        return ReadAdmission(response.Payload);
    }

    /// <summary>
    /// Resolves one demand against exact presentation authority. Cancellation and timeout
    /// abandon only this local demand; the existing bridge has no per-demand cancellation
    /// command. Late results are discarded by ID and cannot complete a replacement demand.
    /// </summary>
    public async Task<WidgetPresentationArtwork> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string artworkHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artworkHandle);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<WidgetPresentationArtwork>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var demandId = Guid.NewGuid().ToString("N");
        CancellationTokenSource demandLifetime;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var descriptor = ValidateAuthority(authority);
            if (!ContainsArtwork(_states[descriptor.Id].LastGood!.Snapshot.Root, artworkHandle))
                throw new WidgetPresentationSessionException(
                    "unknown_artwork", "The artwork handle is not declared by the current snapshot.");
            if (_artwork.Count >= _options.MaximumPendingArtworkRequests)
                throw new WidgetPresentationSessionException(
                    "artwork_saturated", "The presentation artwork request queue is full.");
            demandLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            demandLifetime.CancelAfter(_options.ArtworkTimeout);
            _artwork.Add(demandId, new(authority, artworkHandle, demandLifetime.Token, completion));
        }
        var demandToken = demandLifetime.Token;
        var cancellationRegistration = demandToken.Register(() =>
        {
            lock (_gate)
                if (_artwork.Remove(demandId, out var retired))
                    retired.Completion.TrySetCanceled(demandToken);
        });
        // Completion can fail before the acknowledgement arrives. Always observe it,
        // including when an admission failure prevents the caller from awaiting it.
        _ = completion.Task.ContinueWith(static task => { _ = task.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            var admission = RequestAsync(
                BridgeMessageTypes.ResolveArtwork,
                new BridgeArtworkRequest(authority.WidgetId, artworkHandle,
                    authority.RuntimeGeneration, authority.PresentationGeneration, demandId),
                BridgeMessageTypes.Acknowledged,
                demandToken);
            _ = admission.ContinueWith(static task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            // An already-started framed pipe write cannot be retracted without
            // closing the transport. Bound the caller separately and observe its
            // eventual completion under the transport's own pending-request limit.
            await admission.WaitAsync(demandToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new WidgetPresentationSessionException("artwork_timeout", "The artwork demand timed out.");
        }
        finally
        {
            lock (_gate) _artwork.Remove(demandId);
            completion.TrySetCanceled();
            cancellationRegistration.Dispose();
            demandLifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Task[] indexed;
        lock (_gate)
        {
            RetireIndexedRangesLocked();
            indexed = _indexedDemands.Values.Select(item => item.Done.Task)
                .Concat(_indexedArtworkDemands.Values.Select(item => item.Done.Task))
                .Concat(_indexedLeaseRetirements).ToArray();
        }
        var indexedDrain = Task.WhenAll(indexed);
        try { await indexedDrain.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // A stalled ordinary control can own admission ahead of lease releases.
            // Session shutdown must still reach the bounded transport stop boundary.
            await _transport.DisposeAsync().ConfigureAwait(false);
            await indexedDrain.ConfigureAwait(false);
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
        Task[] refreshes;
        lock (_gate) refreshes = _refreshes.Values.ToArray();
        try { await Task.WhenAll(refreshes).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        PendingArtwork[] artwork;
        lock (_gate)
        {
            artwork = _artwork.Values.ToArray();
            _artwork.Clear();
        }
        var disposed = new ObjectDisposedException(nameof(WidgetPresentationSession));
        foreach (var item in artwork) item.Completion.TrySetException(disposed);
        _indexedReleaseCapacity.Dispose();
        _lifetime.Dispose();
    }

    private async Task<BridgeEnvelope> RequestAsync<T>(
        string type,
        T payload,
        string expectedType,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var response = await _transport.RequestAsync(type, payload, cancellationToken)
            .ConfigureAwait(false);
        if (response.Type == BridgeMessageTypes.Error)
        {
            var failure = ReadRequestFailure(response.Payload);
            throw new WidgetPresentationSessionException(failure.Code, failure.Message);
        }
        if (!string.Equals(response.Type, expectedType, StringComparison.Ordinal))
            throw new BridgeProtocolException(
                $"WidgetBridge returned '{response.Type}' for '{type}'.");
        return response;
    }

    private void HandleEvent(BridgeEnvelope envelope)
    {
        switch (envelope.Type)
        {
        case BridgeMessageTypes.Invalidation:
            RequireObjectProperties(envelope.Payload, "widgetId", "revision");
            var invalidation = new WidgetPresentationInvalidation(
                ReadString(envelope.Payload, "widgetId"),
                ReadInt64(envelope.Payload, "revision", minimum: 0));
            WidgetPresentationState? changed = null;
            lock (_gate)
            {
                if (_states.TryGetValue(invalidation.WidgetId, out var current) &&
                    invalidation.Revision > current.InvalidationRevision)
                {
                    changed = CommitStateLocked(
                        current with { InvalidationRevision = invalidation.Revision },
                        publish: false);
                }
            }
            Invalidated?.Invoke(this, new WidgetPresentationInvalidatedEventArgs(invalidation));
            if (changed?.LastGood is not null) StartInvalidationRefresh(invalidation.WidgetId);
            break;
        case BridgeMessageTypes.Failure:
            HandleFailure(envelope.Payload);
            break;
        case BridgeMessageTypes.Artwork:
            HandleArtwork(envelope.Payload);
            break;
        case BridgeMessageTypes.CatalogChanged:
            HandleRevisionChanged(envelope.Payload, catalog: true);
            break;
        case BridgeMessageTypes.HostEffect:
            HandleHostEffect(envelope.Payload);
            break;
        case BridgeMessageTypes.AppearanceChanged:
            HandleRevisionChanged(envelope.Payload, catalog: false);
            break;
        default:
            throw new BridgeProtocolException(
                $"WidgetBridge sent unknown event '{envelope.Type}'.");
        }
    }

    private void HandleRevisionChanged(JsonElement payload, bool catalog)
    {
        RequireObjectProperties(payload, "revision");
        var revision = ReadInt64(payload, "revision", 0);
        lock (_gate)
        {
            if (_disposed || _terminalFailure is not null) return;
            if (catalog)
            {
                if (revision <= Math.Max(_catalogRevision, _notifiedCatalogRevision)) return;
                _notifiedCatalogRevision = revision;
            }
            else
            {
                if (revision <= _notifiedAppearanceRevision) return;
                _notifiedAppearanceRevision = revision;
            }
        }
        (catalog ? CatalogChanged : AppearanceChanged)?.Invoke(this, new(revision));
    }

    private void HandleHostEffect(JsonElement payload)
    {
        RequireAllowedProperties(payload, new HashSet<string>(StringComparer.Ordinal)
        {
            "widgetId", "runtimeGeneration", "effect", "sequence", "initiatedAtMilliseconds", "windowPreviews",
        }, "widgetId", "runtimeGeneration", "effect", "sequence", "initiatedAtMilliseconds");
        var widgetId = ReadHostIdentifier(payload, "widgetId");
        var runtime = ReadHostIdentifier(payload, "runtimeGeneration");
        var name = ReadHostIdentifier(payload, "effect");
        var sequence = ReadInt64(payload, "sequence", 1);
        var initiatedAt = ReadInt64(payload, "initiatedAtMilliseconds", 0);
        var kind = name switch
        {
            "closeOverlayAfterAppLaunch" => WidgetHostEffectKind.CloseOverlayAfterAppLaunch,
            "activateTaskWindow" => WidgetHostEffectKind.ActivateTaskWindow,
            _ => WidgetHostEffectKind.Unsupported,
        };
        WidgetHostWindowTarget? target = null;
        if (kind == WidgetHostEffectKind.ActivateTaskWindow)
            target = ReadHostWindowTarget(payload);
        else if (kind == WidgetHostEffectKind.CloseOverlayAfterAppLaunch && payload.TryGetProperty("windowPreviews", out _))
            throw new BridgeProtocolException("An app-launch close effect cannot carry window targets.");

        WidgetHostEffectAuthority? authority = null;
        lock (_gate)
        {
            if (_disposed || _terminalFailure is not null) return;
            if (sequence > _hostEffectSequence)
            {
                // Sequence is global, including unsupported effects and retired workers.
                _hostEffectSequence = sequence;
                if (_descriptors.TryGetValue(widgetId, out var descriptor) &&
                    descriptor.RuntimeGeneration == runtime &&
                    (!_hostEffectAdmissionBoundaries.TryGetValue(widgetId, out var boundary) || initiatedAt > boundary))
                    authority = new(widgetId, descriptor.InstanceId, runtime,
                        _sessionGenerations.GetValueOrDefault(widgetId));
            }
        }
        if (authority is null)
        {
            RecordDiagnostic("stale_host_effect", "A stale or replayed host effect was rejected.", widgetId);
            return;
        }
        HostEffectReceived?.Invoke(this, new(new(authority, kind, name, sequence, initiatedAt, target)));
    }

    private static string ReadHostIdentifier(JsonElement payload, string name)
    {
        var value = ReadString(payload, name);
        if (value.Length > 128 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new BridgeProtocolException("A host effect identifier is invalid.");
        return value;
    }

    private static WidgetHostWindowTarget ReadHostWindowTarget(JsonElement payload)
    {
        if (!payload.TryGetProperty("windowPreviews", out var targets) || targets.ValueKind != JsonValueKind.Object)
            throw new BridgeProtocolException("A task activation effect requires one window target.");
        var entries = targets.EnumerateObject().ToArray();
        if (entries.Length != 1)
            throw new BridgeProtocolException("A task activation effect requires one window target.");
        var entry = entries[0];
        return ReadHostWindowTarget(entry.Name, entry.Value);
    }

    private static WidgetHostWindowTarget ReadHostWindowTarget(string id, JsonElement target)
    {
        if (id.Length is 0 or > 128 || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new BridgeProtocolException("A task activation window identifier is invalid.");
        RequireObjectProperties(target, "handle", "processId", "processCreated", "className");
        var handle = ReadHex(target, "handle");
        var created = ReadHex(target, "processCreated");
        var pid = ReadInt64(target, "processId", 1);
        var className = ReadString(target, "className");
        if (pid > uint.MaxValue || className.Length > 256 || className.Any(char.IsControl))
            throw new BridgeProtocolException("A task activation window identity is invalid.");
        return new(id, handle, (uint)pid, created, className);
    }

    private static ulong ReadHex(JsonElement payload, string property)
    {
        var value = ReadString(payload, property);
        if (value.Length > 16 || value.Any(character => !char.IsAsciiHexDigit(character)) ||
            !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number) || number == 0)
            throw new BridgeProtocolException("A task activation native identity is invalid.");
        return number;
    }

    private void HandleFailure(JsonElement payload)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "widgetId", "runtimeGeneration", "reason", "actionId", "sourceElementId",
            "message", "exitCode", "diagnosticCode", "restartsUsed", "canRestart",
        };
        RequireAllowedProperties(payload, allowed, "widgetId", "reason");
        var widgetId = ReadString(payload, "widgetId");
        var reason = ReadString(payload, "reason");
        var runtimeGeneration = ReadOptionalString(payload, "runtimeGeneration");
        var failure = new WidgetPresentationFailure(
            widgetId,
            reason,
            Bound(ReadOptionalString(payload, "message") ?? "Widget runtime failure."),
            runtimeGeneration,
            ReadOptionalString(payload, "actionId"),
            ReadOptionalString(payload, "sourceElementId"),
            ReadOptionalInt32(payload, "exitCode"),
            ReadOptionalString(payload, "diagnosticCode"),
            ReadOptionalInt32(payload, "restartsUsed"),
            ReadOptionalBoolean(payload, "canRestart"));
        if (runtimeGeneration is null)
        {
            PublishTerminalBridgeFailure(failure);
            return;
        }

        BridgeFailurePublication? publication;
        lock (_gate)
        {
            var state = _states.GetValueOrDefault(widgetId);
            if (!_descriptors.TryGetValue(widgetId, out var descriptor) ||
                !string.Equals(
                    descriptor.RuntimeGeneration,
                    runtimeGeneration,
                    StringComparison.Ordinal) ||
                (state?.LastGood is { } lastGood &&
                 !string.Equals(
                     lastGood.Authority.RuntimeGeneration,
                     runtimeGeneration,
                     StringComparison.Ordinal)))
            {
                publication = null;
            }
            else
            {
                publication = new BridgeFailurePublication(
                    _catalogRevision,
                    descriptor,
                    _sessionGenerations.GetValueOrDefault(widgetId),
                    state?.PublicationRevision,
                    state?.LastGood?.Authority,
                    failure);
            }
        }
        if (publication is null)
        {
            RecordDiagnostic("stale_failure", "A stale worker failure was rejected.", widgetId);
            return;
        }
        var captured = BridgeFailureCapturedForTesting?.Invoke();
        if (captured is null)
            PublishBridgeFailure(publication);
        else
            _ = PublishBridgeFailureAfterCaptureAsync(publication, captured);
    }

    private async Task PublishBridgeFailureAfterCaptureAsync(
        BridgeFailurePublication publication,
        Task captured)
    {
        try
        {
            await captured.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            PublishBridgeFailure(publication);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordDiagnostic(
                "failure_publication_aborted",
                "A bridge failure publication was aborted.",
                publication.Descriptor.Id);
        }
    }

    private void PublishBridgeFailure(BridgeFailurePublication publication)
    {
        WidgetPresentationState? committed = null;
        var startPublications = false;
        lock (_gate)
        {
            var hasState = _states.TryGetValue(publication.Descriptor.Id, out var current);
            var stateMatches = publication.StatePublicationRevision is { } stateRevision
                ? hasState &&
                  current!.PublicationRevision == stateRevision &&
                  current.LastGood?.Authority == publication.LastGoodAuthority
                : !hasState;
            if (_catalogRevision == publication.CatalogRevision &&
                _descriptors.TryGetValue(publication.Descriptor.Id, out var descriptor) &&
                descriptor == publication.Descriptor &&
                _sessionGenerations.GetValueOrDefault(publication.Descriptor.Id) ==
                    publication.SessionGeneration &&
                stateMatches)
            {
                committed = CommitStateLocked(
                    new WidgetPresentationState(
                        publication.Descriptor.Id,
                        current?.LastGood,
                        publication.Failure,
                        current?.InvalidationRevision ?? 0),
                    publish: true);
                startPublications = TakePublicationOwnershipLocked();
            }
        }
        if (committed is null)
        {
            RecordDiagnostic(
                "stale_failure",
                "A stale worker failure was rejected.",
                publication.Descriptor.Id);
            return;
        }
        if (startPublications) DrainStatePublications();
    }

    private void PublishTerminalBridgeFailure(WidgetPresentationFailure failure)
    {
        var startPublications = false;
        var committed = false;
        lock (_gate)
        {
            if (_descriptors.ContainsKey(failure.WidgetId))
            {
                var current = _states.GetValueOrDefault(failure.WidgetId);
                _hostEffectAdmissionBoundaries[failure.WidgetId] = Environment.TickCount64;
                _sessionGenerations[failure.WidgetId] = checked(
                    _sessionGenerations.GetValueOrDefault(failure.WidgetId) + 1);
                _ = CommitStateLocked(
                    new WidgetPresentationState(
                        failure.WidgetId,
                        current?.LastGood,
                        failure,
                        current?.InvalidationRevision ?? 0),
                    publish: true);
                startPublications = TakePublicationOwnershipLocked();
                committed = true;
            }
        }
        if (!committed)
        {
            RecordDiagnostic(
                "stale_failure",
                "A stale worker failure was rejected.",
                failure.WidgetId);
            return;
        }
        if (startPublications) DrainStatePublications();
    }

    private void HandleArtwork(JsonElement payload)
    {
        RequireAllowedProperties(payload, new HashSet<string>(StringComparer.Ordinal)
        {
            "widgetId", "artworkHandle", "runtimeGeneration", "presentationGeneration",
            "demandId", "contentType", "contentBase64",
        }, "widgetId", "artworkHandle", "contentType", "contentBase64");
        var widgetId = ReadString(payload, "widgetId");
        var handle = ReadString(payload, "artworkHandle");
        var demandId = ReadOptionalString(payload, "demandId");
        PendingArtwork? pending;
        lock (_gate)
            pending = demandId is not null ? _artwork.GetValueOrDefault(demandId) : null;
        if (pending is null)
        {
            // Never fall back to FIFO widget/handle matching, including for legacy
            // replies without an ID. That would let an old demand satisfy a new one.
            RecordDiagnostic("unmatched_artwork", "An unmatched artwork result was discarded.", widgetId);
            return;
        }
        var runtime = ReadOptionalString(payload, "runtimeGeneration");
        var presentation = ReadOptionalString(payload, "presentationGeneration");
        if (widgetId != pending.Authority.WidgetId || handle != pending.ArtworkHandle ||
            runtime != pending.Authority.RuntimeGeneration || presentation != pending.Authority.PresentationGeneration)
        {
            RecordDiagnostic("stale_artwork", "Artwork with mismatched demand authority was discarded.", widgetId);
            return;
        }
        lock (_gate)
        {
            if (!TryAdmitArtworkCompletionLocked(demandId!, pending)) return;
        }
        var contentTypeValue = ReadString(payload, "contentType", allowEmpty: true);
        if (!payload.TryGetProperty("contentBase64", out var encodedValue) || encodedValue.ValueKind != JsonValueKind.String)
            throw new BridgeProtocolException("WidgetBridge returned invalid artwork encoding.");
        var encoded = encodedValue.GetString()!;
        if (encoded.Length > ((ProtocolConstants.MaximumEncodedArtworkBytes + 2) / 3) * 4)
            throw new BridgeProtocolException("WidgetBridge returned oversized artwork.");
        byte[] bytes;
        try { bytes = encoded.Length == 0 ? [] : Convert.FromBase64String(encoded); }
        catch (FormatException exception)
        {
            throw new BridgeProtocolException("WidgetBridge returned malformed artwork.", exception);
        }
        if (bytes.Length > ProtocolConstants.MaximumEncodedArtworkBytes)
            throw new BridgeProtocolException("WidgetBridge returned oversized artwork.");
        var contentType = WidgetEncodedArtworkContract.ParseContentType(contentTypeValue);
        if (bytes.Length != 0 &&
            (contentType is null ||
             !WidgetEncodedArtworkContract.IsValid(
                 new WidgetEncodedArtwork(contentType.Value, bytes))))
            throw new BridgeProtocolException("WidgetBridge returned invalid artwork.");

        WidgetPresentationArtwork result;
        lock (_gate)
        {
            // Decode happened outside the state lock. Cancellation or a replacement
            // snapshot may have retired the demand in the meantime.
            if (!TryAdmitArtworkCompletionLocked(demandId!, pending)) return;
            _artwork.Remove(demandId!);
            result = new WidgetPresentationArtwork(
                pending.Authority,
                handle,
                contentType ?? WidgetArtworkContentType.Png,
                bytes);
            if (!pending.Completion.TrySetResult(result)) return;
        }
        ArtworkResolved?.Invoke(this, new WidgetPresentationArtworkEventArgs(result));
    }

    private bool TryAdmitArtworkCompletionLocked(string demandId, PendingArtwork pending)
    {
        if (!_artwork.TryGetValue(demandId, out var current) || !ReferenceEquals(current, pending)) return false;
        if (pending.CancellationToken.IsCancellationRequested || _disposed)
        {
            _artwork.Remove(demandId);
            pending.Completion.TrySetCanceled();
            return false;
        }
        try { _ = ValidateAuthority(pending.Authority); }
        catch (WidgetPresentationSessionException exception)
        {
            _artwork.Remove(demandId);
            pending.Completion.TrySetException(exception);
            return false;
        }
        return true;
    }

    private void StartInvalidationRefresh(string widgetId)
    {
        lock (_gate)
        {
            if (_refreshes.ContainsKey(widgetId)) return;
            var task = RefreshInvalidationsAsync(widgetId);
            _refreshes.Add(widgetId, task);
        }
    }

    private async Task RefreshInvalidationsAsync(string widgetId)
    {
        long appliedRevision = 0;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                RefreshPublication? publication;
                lock (_gate)
                {
                    var state = _states.GetValueOrDefault(widgetId);
                    if (state?.LastGood is null ||
                        state.InvalidationRevision <= appliedRevision ||
                        !_descriptors.TryGetValue(widgetId, out var descriptor))
                    {
                        publication = null;
                    }
                    else
                    {
                        publication = new RefreshPublication(
                            _catalogRevision,
                            descriptor,
                            _sessionGenerations.GetValueOrDefault(widgetId),
                            state.LastGood,
                            state.InvalidationRevision,
                            state.PublicationRevision);
                    }
                }
                if (publication is null) break;
                appliedRevision = publication.InvalidationRevision;
                try
                {
                    await RefreshAsync(publication.LastGood.Authority, _lifetime.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    PublishRefreshFailure(publication, exception);
                    break;
                }
            }
        }
        finally
        {
            var restart = false;
            lock (_gate)
            {
                _refreshes.Remove(widgetId);
                var current = _states.GetValueOrDefault(widgetId);
                restart = current?.LastGood is not null &&
                          current.InvalidationRevision > appliedRevision &&
                          !_lifetime.IsCancellationRequested;
            }
            if (restart) StartInvalidationRefresh(widgetId);
        }
    }

    private void PublishRefreshFailure(RefreshPublication publication, Exception exception)
    {
        var code = exception is WidgetPresentationSessionException sessionException
            ? sessionException.Code
            : "refresh_failed";
        var failure = new WidgetPresentationFailure(
            publication.Descriptor.Id,
            code,
            Bound(exception.Message),
            publication.LastGood.Authority.RuntimeGeneration);
        WidgetPresentationState? committed = null;
        var startPublications = false;
        lock (_gate)
        {
            var capturedAuthority = publication.LastGood.Authority;
            if (_catalogRevision == publication.CatalogRevision &&
                _descriptors.TryGetValue(publication.Descriptor.Id, out var descriptor) &&
                descriptor == publication.Descriptor &&
                _sessionGenerations.GetValueOrDefault(publication.Descriptor.Id) ==
                    publication.SessionGeneration &&
                _states.TryGetValue(publication.Descriptor.Id, out var current) &&
                current.LastGood is { } currentLastGood &&
                currentLastGood.Authority == capturedAuthority &&
                current.InvalidationRevision >= publication.InvalidationRevision &&
                current.PublicationRevision == publication.StatePublicationRevision)
            {
                committed = CommitStateLocked(current with { Failure = failure }, publish: true);
                startPublications = TakePublicationOwnershipLocked();
            }
        }
        if (committed is null)
        {
            RecordDiagnostic(
                "stale_refresh_failure",
                "A refresh failure for retired presentation authority was discarded.",
                publication.Descriptor.Id);
            return;
        }
        if (startPublications) DrainStatePublications();
    }

    private WidgetPresentationFrame PublishSnapshot(
        BridgeWidgetDescriptor descriptor,
        long sessionGeneration,
        JsonElement payload)
    {
        RequireObjectProperties(
            payload, "widgetId", "transactionKind", "baseSequence",
            "recoveryOriginSequence", "snapshot", "renderStyles");
        if (!string.Equals(ReadString(payload, "widgetId"), descriptor.Id, StringComparison.Ordinal))
            throw new BridgeProtocolException("WidgetBridge returned a snapshot for another widget.");
        if (!string.Equals(
                ReadString(payload, "transactionKind"), "ordinaryCheckpoint",
                StringComparison.Ordinal) ||
            payload.GetProperty("baseSequence").GetInt64() != 0 ||
            payload.GetProperty("recoveryOriginSequence").GetInt64() != 0)
            throw new BridgeProtocolException(
                "WidgetBridge returned different checkpoint transaction authority.");
        var snapshot = SnapshotJson.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(payload.GetProperty("snapshot").GetRawText()));
        if (!string.Equals(snapshot.WidgetInstanceId, descriptor.InstanceId, StringComparison.Ordinal))
            throw new BridgeProtocolException("WidgetBridge returned a mismatched widget instance.");
        var renderStyles = payload.GetProperty("renderStyles")
            .Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(BridgeJson.Options)
            ?? throw new BridgeProtocolException("WidgetBridge returned null render styles.");
        if (renderStyles.Count > BridgeRenderStyleLimits.MaximumNodes)
            throw new BridgeProtocolException("WidgetBridge returned oversized render styles.");
        var authority = new WidgetPresentationAuthority(
            descriptor.Id,
            descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration,
            sessionGeneration,
            descriptor.InstanceId,
            snapshot.Sequence,
            snapshot.ActiveInputScopeId);
        var frame = new WidgetPresentationFrame(
            authority,
            descriptor,
            snapshot,
            new ReadOnlyDictionary<string, BridgeNodeRenderStyles>(renderStyles));
        WidgetPresentationState committed;
        var startPublications = false;
        lock (_gate)
        {
            if (!_descriptors.TryGetValue(descriptor.Id, out var currentDescriptor) ||
                currentDescriptor != descriptor ||
                _sessionGenerations.GetValueOrDefault(descriptor.Id) != sessionGeneration)
                throw Stale("presentation_stale", descriptor.Id,
                    "The snapshot belongs to a retired presentation session.");
            var prior = _states.GetValueOrDefault(descriptor.Id);
            if (prior?.LastGood is { } current &&
                current.Authority.SessionGeneration == sessionGeneration)
            {
                if (current.Authority.SnapshotSequence > snapshot.Sequence)
                    throw Stale("snapshot_stale", descriptor.Id,
                        "An older snapshot completed after the current snapshot.");
                if (current.Authority.SnapshotSequence == snapshot.Sequence)
                    return current;
            }
            committed = CommitStateLocked(
                new WidgetPresentationState(
                    descriptor.Id, frame, null, prior?.InvalidationRevision ?? 0),
                publish: true);
            startPublications = TakePublicationOwnershipLocked();
        }
        if (startPublications) DrainStatePublications();
        return frame;
    }

    private WidgetPresentationState CommitStateLocked(
        WidgetPresentationState state,
        bool publish)
    {
        var committed = state with { PublicationRevision = NextPublicationRevisionLocked() };
        _states[state.WidgetId] = committed;
        RetireIndexedRangesLocked(state.WidgetId, committed.LastGood?.Authority);
        if (publish) _statePublications.Enqueue(committed);
        return committed;
    }

    private bool RetireStateLocked(string widgetId)
    {
        RetireIndexedRangesLocked(widgetId);
        if (!_states.Remove(widgetId)) return false;
        var retired = new WidgetPresentationState(widgetId, null, null, 0)
        {
            PublicationRevision = NextPublicationRevisionLocked(),
        };
        _statePublications.Enqueue(retired);
        return TakePublicationOwnershipLocked();
    }

    private long NextPublicationRevisionLocked() =>
        _publicationRevision = checked(_publicationRevision + 1);

    private bool TakePublicationOwnershipLocked()
    {
        if (_publicationOwner || _statePublications.Count == 0) return false;
        _publicationOwner = true;
        return true;
    }

    private void DrainStatePublications()
    {
        while (true)
        {
            WidgetPresentationState state;
            lock (_gate)
            {
                if (_statePublications.Count == 0)
                {
                    _publicationOwner = false;
                    return;
                }
                state = _statePublications.Dequeue();
            }
            try
            {
                PresentationChanged?.Invoke(this, new WidgetPresentationChangedEventArgs(state));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                try
                {
                    RecordDiagnostic(
                        "presentation_subscriber_failed",
                        "A presentation state subscriber failed.",
                        state.WidgetId);
                }
                catch (Exception diagnosticException)
                    when (diagnosticException is not OutOfMemoryException)
                {
                    // Subscriber failures cannot take ownership of state publication.
                }
            }
        }
    }

    private BridgeWidgetDescriptor ValidateAuthority(WidgetPresentationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        lock (_gate)
        {
            ThrowIfTerminalLocked();
            if (!_descriptors.TryGetValue(authority.WidgetId, out var descriptor) ||
                !_states.TryGetValue(authority.WidgetId, out var state) ||
                state.LastGood is not { } lastGood ||
                lastGood.Authority != authority ||
                !string.Equals(descriptor.RuntimeGeneration, authority.RuntimeGeneration,
                    StringComparison.Ordinal) ||
                !string.Equals(descriptor.PresentationGeneration, authority.PresentationGeneration,
                    StringComparison.Ordinal) ||
                _sessionGenerations.GetValueOrDefault(authority.WidgetId) !=
                    authority.SessionGeneration)
                throw Stale("presentation_stale", authority.WidgetId,
                    "The presentation authority is no longer current.");
            return descriptor;
        }
    }

    private long ValidateTarget(WidgetPresentationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.Descriptor);
        lock (_gate)
        {
            ThrowIfTerminalLocked();
            if (target.CatalogRevision != _catalogRevision ||
                !_descriptors.TryGetValue(target.Descriptor.Id, out var descriptor) ||
                descriptor != target.Descriptor)
                throw Stale("catalog_stale", target.Descriptor.Id,
                    "The widget descriptor is no longer current.");
            return _sessionGenerations.GetValueOrDefault(target.Descriptor.Id);
        }
    }

    private long BeginRestart(WidgetPresentationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.Descriptor);
        WidgetPresentationState cleared;
        var startPublications = false;
        long generation;
        lock (_gate)
        {
            ThrowIfTerminalLocked();
            if (target.CatalogRevision != _catalogRevision ||
                !_descriptors.TryGetValue(target.Descriptor.Id, out var descriptor) ||
                descriptor != target.Descriptor)
                throw Stale("catalog_stale", target.Descriptor.Id,
                    "The widget descriptor is no longer current.");
            generation = _sessionGenerations.GetValueOrDefault(target.Descriptor.Id) + 1;
            _sessionGenerations[target.Descriptor.Id] = generation;
            // Runtime fingerprints can survive restart. An effect initiated before
            // retiring this local incarnation must not acquire the new authority.
            _hostEffectAdmissionBoundaries[target.Descriptor.Id] = Environment.TickCount64;
            cleared = CommitStateLocked(
                new WidgetPresentationState(target.Descriptor.Id, null, null, 0),
                publish: true);
            startPublications = TakePublicationOwnershipLocked();
        }
        if (startPublications) DrainStatePublications();
        return generation;
    }

    private void RecordDiagnostic(string code, string message, string? widgetId = null)
    {
        var diagnostic = new WidgetPresentationDiagnostic(
            DateTimeOffset.UtcNow,
            Bound(code),
            Bound(message),
            widgetId is null ? null : Bound(widgetId));
        lock (_gate)
        {
            while (_diagnostics.Count >= _options.MaximumRetainedDiagnostics)
                _diagnostics.Dequeue();
            _diagnostics.Enqueue(diagnostic);
        }
        DiagnosticPublished?.Invoke(this, new WidgetPresentationDiagnosticEventArgs(diagnostic));
    }

    private void FailTerminal(Exception exception)
    {
        PendingArtwork[] artwork;
        lock (_gate)
        {
            if (_terminalFailure is not null) return;
            _terminalFailure = exception;
            RetireIndexedRangesLocked();
            artwork = _artwork.Values.ToArray();
            _artwork.Clear();
        }
        var failure = new WidgetPresentationSessionException(
            "transport_closed", "The WidgetBridge presentation session closed.", exception);
        foreach (var item in artwork) item.Completion.TrySetException(failure);
        RecordDiagnostic("transport_closed", exception.Message);
    }

    private void ThrowIfTerminalLocked()
    {
        if (_terminalFailure is not null)
            throw new WidgetPresentationSessionException(
                "transport_closed", "The WidgetBridge presentation session is closed.",
                _terminalFailure);
    }

    private static WidgetOperationAdmission ReadAdmission(JsonElement payload)
    {
        RequireObjectProperties(payload, "admission");
        var admission = payload.GetProperty("admission")
            .Deserialize<WidgetOperationAdmission>(BridgeJson.Options);
        if (admission is not (WidgetOperationAdmission.Enqueued or
                              WidgetOperationAdmission.Replaced))
            throw new BridgeProtocolException("WidgetBridge returned an invalid action admission.");
        return admission;
    }

    private static BridgeRequestFailure ReadRequestFailure(JsonElement payload)
    {
        RequireObjectProperties(payload, "code", "message");
        return new BridgeRequestFailure(
            Bound(ReadString(payload, "code")),
            Bound(ReadString(payload, "message")));
    }

    private static void ValidateDescriptor(BridgeWidgetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || descriptor.Id.Length > 128 ||
            string.IsNullOrWhiteSpace(descriptor.InstanceId) || descriptor.InstanceId.Length > 128 ||
            descriptor.RuntimeGeneration.Length != 32 ||
            descriptor.PresentationGeneration.Length != 32 ||
            descriptor.QuickActions.Count > 16)
            throw new BridgeProtocolException("WidgetBridge returned an invalid widget descriptor.");
    }

    private static void ValidatePresentationLifecycle(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
                          WidgetLifecycleState.Visible or
                          WidgetLifecycleState.Interactive))
            throw new ArgumentOutOfRangeException(nameof(state));
    }

    private static bool ContainsArtwork(ViewNode node, string handle) =>
        (string.Equals(node.ArtworkHandle, handle, StringComparison.Ordinal) ||
         string.Equals(node.FocusBackgroundArtworkHandle, handle, StringComparison.Ordinal)) ||
        node.Children.Any(child => ContainsArtwork(child, handle));

    private static WidgetPresentationSessionException Stale(
        string code,
        string widgetId,
        string message) => new(code, $"Widget '{Bound(widgetId)}': {message}");

    private static string ReadString(JsonElement payload, string name, bool allowEmpty = false)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new BridgeProtocolException($"WidgetBridge payload property '{name}' is invalid.");
        var text = value.GetString()!;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text)) ||
            text.Length > PresentationContractLimits.MaximumDiagnosticTextLength)
            throw new BridgeProtocolException($"WidgetBridge payload property '{name}' is invalid.");
        return text;
    }

    private static string? ReadOptionalString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? ReadString(payload, name, allowEmpty: true)
            : null;

    private static int? ReadOptionalInt32(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt32()
            : null;

    private static bool ReadOptionalBoolean(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null &&
        value.GetBoolean();

    private static long ReadInt64(JsonElement payload, string name, long minimum)
    {
        if (!payload.TryGetProperty(name, out var value) ||
            !value.TryGetInt64(out var result) || result < minimum)
            throw new BridgeProtocolException($"WidgetBridge payload property '{name}' is invalid.");
        return result;
    }

    private static void RequireObjectProperties(JsonElement payload, params string[] properties) =>
        RequireObjectProperties(payload, properties.ToHashSet(StringComparer.Ordinal));

    private static void RequireObjectProperties(JsonElement payload, IReadOnlySet<string> properties)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new BridgeProtocolException("WidgetBridge payload is not an object.");
        var actual = payload.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(properties))
            throw new BridgeProtocolException("WidgetBridge payload shape is invalid.");
    }

    private static void RequireAllowedProperties(
        JsonElement payload,
        IReadOnlySet<string> allowed,
        params string[] required)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new BridgeProtocolException("WidgetBridge payload is not an object.");
        var actual = payload.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (actual.Any(property => !allowed.Contains(property)) ||
            required.Any(property => !actual.Contains(property)))
            throw new BridgeProtocolException("WidgetBridge payload shape is invalid.");
    }

    private static string Bound(string value)
    {
        var bounded = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        return bounded.Length > PresentationContractLimits.MaximumDiagnosticTextLength
            ? bounded[..PresentationContractLimits.MaximumDiagnosticTextLength]
            : bounded;
    }

}
