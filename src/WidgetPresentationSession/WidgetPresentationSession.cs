using System.Collections.ObjectModel;
using System.Text.Json;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetPresentationSession;

public sealed class WidgetPresentationSession : IAsyncDisposable
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
    private readonly Dictionary<(string WidgetId, string ArtworkHandle), Queue<PendingArtwork>>
        _artwork = [];
    private readonly Queue<WidgetPresentationDiagnostic> _diagnostics = [];
    private long _catalogRevision = -1;
    private int _pendingArtworkCount;
    private bool _disposed;
    private Exception? _terminalFailure;

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
        RequireObjectProperties(response.Payload, "revision", "widgets");
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
        lock (_gate)
        {
            if (revision < _catalogRevision)
                throw new BridgeProtocolException("WidgetBridge returned a stale widget catalog.");
            _catalogRevision = revision;
            foreach (var pair in byId)
            {
                if (!_descriptors.TryGetValue(pair.Key, out var prior))
                    _sessionGenerations[pair.Key] =
                        _sessionGenerations.GetValueOrDefault(pair.Key) + 1;
                else if (prior.InstanceId != pair.Value.InstanceId ||
                         prior.RuntimeGeneration != pair.Value.RuntimeGeneration ||
                         prior.PresentationGeneration != pair.Value.PresentationGeneration)
                {
                    _sessionGenerations[pair.Key] =
                        _sessionGenerations.GetValueOrDefault(pair.Key) + 1;
                    _states.Remove(pair.Key);
                }
            }
            foreach (var removed in _descriptors.Keys.Where(id => !byId.ContainsKey(id)).ToArray())
            {
                _sessionGenerations[removed] =
                    _sessionGenerations.GetValueOrDefault(removed) + 1;
                _states.Remove(removed);
            }
            _descriptors.Clear();
            foreach (var pair in byId) _descriptors.Add(pair.Key, pair.Value);
        }
        return new WidgetPresentationCatalog(revision, Array.AsReadOnly(widgets));
    }

    public async Task<WidgetPresentationFrame> EstablishPresentationAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidatePresentationLifecycle(state);
        var sessionGeneration = ValidateTarget(target);
        var response = await RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(target.Descriptor.Id, state, AdmitSnapshot: true),
            BridgeMessageTypes.Snapshot,
            cancellationToken).ConfigureAwait(false);
        return PublishSnapshot(target.Descriptor, sessionGeneration, response.Payload);
    }

    public async Task SetLifecycleAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ValidatePresentationLifecycle(state);
        _ = ValidateTarget(target);
        await RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(target.Descriptor.Id, state),
            BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        if (state == WidgetLifecycleState.Background)
        {
            lock (_gate)
            {
                if (_states.TryGetValue(target.Descriptor.Id, out var current))
                    _states[target.Descriptor.Id] = current with { Failure = null };
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
            new WidgetIdRequest(authority.WidgetId),
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

    public async Task<WidgetPresentationArtwork> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string artworkHandle,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ValidateAuthority(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(artworkHandle);
        WidgetPresentationFrame frame;
        lock (_gate) frame = _states[descriptor.Id].LastGood!;
        if (!ContainsArtwork(frame.Snapshot.Root, artworkHandle))
            throw new WidgetPresentationSessionException(
                "unknown_artwork", "The artwork handle is not declared by the current snapshot.");

        var completion = new TaskCompletionSource<WidgetPresentationArtwork>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var key = (authority.WidgetId, artworkHandle);
        lock (_gate)
        {
            ThrowIfTerminalLocked();
            if (_pendingArtworkCount >= _options.MaximumPendingArtworkRequests)
                throw new WidgetPresentationSessionException(
                    "artwork_saturated", "The presentation artwork request queue is full.");
            if (!_artwork.TryGetValue(key, out var queue))
                _artwork.Add(key, queue = new Queue<PendingArtwork>());
            queue.Enqueue(new PendingArtwork(authority, completion));
            _pendingArtworkCount++;
        }

        try
        {
            await RequestAsync(
                BridgeMessageTypes.ResolveArtwork,
                new BridgeArtworkRequest(authority.WidgetId, artworkHandle),
                BridgeMessageTypes.Acknowledged,
                cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemovePendingArtwork(key, completion);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        Task[] refreshes;
        lock (_gate) refreshes = _refreshes.Values.ToArray();
        try { await Task.WhenAll(refreshes).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        PendingArtwork[] artwork;
        lock (_gate)
        {
            artwork = _artwork.Values.SelectMany(queue => queue).ToArray();
            _artwork.Clear();
            _pendingArtworkCount = 0;
        }
        var disposed = new ObjectDisposedException(nameof(WidgetPresentationSession));
        foreach (var item in artwork) item.Completion.TrySetException(disposed);
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
                    changed = current with { InvalidationRevision = invalidation.Revision };
                    _states[invalidation.WidgetId] = changed;
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
            RequireObjectProperties(envelope.Payload, "revision");
            RecordDiagnostic(
                "catalog_changed",
                $"Widget catalog revision {ReadInt64(envelope.Payload, "revision", 0)} is available.");
            break;
        case BridgeMessageTypes.HostEffect:
            RequireObjectProperties(
                envelope.Payload, "widgetId", "runtimeGeneration", "effect", "sequence");
            RecordDiagnostic(
                "host_effect",
                $"Host effect '{Bound(ReadString(envelope.Payload, "effect"))}' is available.",
                ReadString(envelope.Payload, "widgetId"));
            break;
        case BridgeMessageTypes.AppearanceChanged:
        case BridgeMessageTypes.LauncherExperienceChanged:
            RequireObjectProperties(envelope.Payload, "revision");
            RecordDiagnostic(
                envelope.Type,
                $"Revision {ReadInt64(envelope.Payload, "revision", 0)} is available.");
            break;
        default:
            throw new BridgeProtocolException(
                $"WidgetBridge sent unknown event '{envelope.Type}'.");
        }
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
        WidgetPresentationFrame? lastGood;
        lock (_gate)
        {
            lastGood = _states.GetValueOrDefault(widgetId)?.LastGood;
            if (runtimeGeneration is null)
                _sessionGenerations[widgetId] =
                    _sessionGenerations.GetValueOrDefault(widgetId) + 1;
        }
        if (runtimeGeneration is not null && lastGood is not null &&
            !string.Equals(runtimeGeneration, lastGood.Authority.RuntimeGeneration,
                StringComparison.Ordinal))
        {
            RecordDiagnostic("stale_failure", "A stale worker failure was rejected.", widgetId);
            return;
        }
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
        PublishState(new WidgetPresentationState(
            widgetId,
            lastGood,
            failure,
            GetState(widgetId)?.InvalidationRevision ?? 0));
    }

    private void HandleArtwork(JsonElement payload)
    {
        RequireObjectProperties(payload, "widgetId", "artworkHandle", "pngBase64");
        var widgetId = ReadString(payload, "widgetId");
        var handle = ReadString(payload, "artworkHandle");
        var encoded = ReadString(payload, "pngBase64", allowEmpty: true);
        byte[] bytes;
        try { bytes = encoded.Length == 0 ? [] : Convert.FromBase64String(encoded); }
        catch (FormatException exception)
        {
            throw new BridgeProtocolException("WidgetBridge returned malformed artwork.", exception);
        }
        if (bytes.Length > PresentationContractLimits.MaximumArtworkBytes)
            throw new BridgeProtocolException("WidgetBridge returned oversized artwork.");

        PendingArtwork? pending = null;
        lock (_gate)
        {
            var key = (widgetId, handle);
            if (_artwork.TryGetValue(key, out var queue) && queue.Count != 0)
            {
                pending = queue.Dequeue();
                _pendingArtworkCount--;
                if (queue.Count == 0) _artwork.Remove(key);
            }
        }
        if (pending is null)
        {
            RecordDiagnostic("unmatched_artwork", "An unmatched artwork result was discarded.", widgetId);
            return;
        }
        try
        {
            _ = ValidateAuthority(pending.Authority);
            var result = new WidgetPresentationArtwork(pending.Authority, handle, bytes);
            pending.Completion.TrySetResult(result);
            ArtworkResolved?.Invoke(this, new WidgetPresentationArtworkEventArgs(result));
        }
        catch (WidgetPresentationSessionException exception)
        {
            pending.Completion.TrySetException(exception);
        }
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
                WidgetPresentationState? state;
                lock (_gate) state = _states.GetValueOrDefault(widgetId);
                if (state?.LastGood is null || state.InvalidationRevision <= appliedRevision) break;
                appliedRevision = state.InvalidationRevision;
                try
                {
                    await RefreshAsync(state.LastGood.Authority, _lifetime.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    PublishRefreshFailure(widgetId, state.LastGood, exception);
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

    private void PublishRefreshFailure(
        string widgetId,
        WidgetPresentationFrame lastGood,
        Exception exception)
    {
        var code = exception is WidgetPresentationSessionException sessionException
            ? sessionException.Code
            : "refresh_failed";
        var failure = new WidgetPresentationFailure(
            widgetId, code, Bound(exception.Message), lastGood.Authority.RuntimeGeneration);
        var invalidation = GetState(widgetId)?.InvalidationRevision ?? 0;
        PublishState(new WidgetPresentationState(widgetId, lastGood, failure, invalidation));
    }

    private WidgetPresentationFrame PublishSnapshot(
        BridgeWidgetDescriptor descriptor,
        long sessionGeneration,
        JsonElement payload)
    {
        RequireObjectProperties(payload, "widgetId", "snapshot", "renderStyles");
        if (!string.Equals(ReadString(payload, "widgetId"), descriptor.Id, StringComparison.Ordinal))
            throw new BridgeProtocolException("WidgetBridge returned a snapshot for another widget.");
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
            committed = new WidgetPresentationState(
                descriptor.Id, frame, null, prior?.InvalidationRevision ?? 0);
            _states[descriptor.Id] = committed;
        }
        PresentationChanged?.Invoke(this, new WidgetPresentationChangedEventArgs(committed));
        return frame;
    }

    private void PublishState(WidgetPresentationState state)
    {
        lock (_gate) _states[state.WidgetId] = state;
        PresentationChanged?.Invoke(this, new WidgetPresentationChangedEventArgs(state));
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
            cleared = new WidgetPresentationState(target.Descriptor.Id, null, null, 0);
            _states[target.Descriptor.Id] = cleared;
        }
        PresentationChanged?.Invoke(this, new WidgetPresentationChangedEventArgs(cleared));
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
            artwork = _artwork.Values.SelectMany(queue => queue).ToArray();
            _artwork.Clear();
            _pendingArtworkCount = 0;
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

    private void RemovePendingArtwork(
        (string WidgetId, string ArtworkHandle) key,
        TaskCompletionSource<WidgetPresentationArtwork> completion)
    {
        lock (_gate)
        {
            if (!_artwork.TryGetValue(key, out var queue)) return;
            var retained = queue.Where(item => !ReferenceEquals(item.Completion, completion)).ToArray();
            if (retained.Length == queue.Count) return;
            _pendingArtworkCount--;
            if (retained.Length == 0) _artwork.Remove(key);
            else _artwork[key] = new Queue<PendingArtwork>(retained);
        }
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
        string.Equals(node.ArtworkHandle, handle, StringComparison.Ordinal) ||
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
