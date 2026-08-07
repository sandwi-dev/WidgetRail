using System.Text.Json;
using System.Threading.Channels;

namespace GameBarAlternative.PlatformBroker;

public sealed class BrokerEventSubscription : IAsyncDisposable
{
    private readonly Channel<BrokerEventEnvelope> _events = Channel.CreateBounded<BrokerEventEnvelope>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object _gate = new();
    private BrokerLifecycleState _lifecycle;
    private BrokerEventEnvelope? _pending;
    private bool _revoked;

    internal BrokerEventSubscription(
        string capabilityId,
        string eventType,
        BrokerLifecycleState lifecycle)
    {
        CapabilityId = capabilityId;
        EventType = eventType;
        _lifecycle = lifecycle;
    }

    public string CapabilityId { get; }
    public string EventType { get; }
    public bool IsRevoked { get { lock (_gate) return _revoked; } }

    public async ValueTask<BrokerEventEnvelope> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_revoked)
                throw new BrokerException("capability_revoked", "Capability subscription was revoked.");
        }
        try
        {
            return await _events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            throw new BrokerException("capability_revoked", "Capability subscription was revoked.", exception);
        }
    }

    internal void Publish(BrokerEventEnvelope envelope)
    {
        lock (_gate)
        {
            if (_revoked) return;
            if (_lifecycle is BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive)
                _events.Writer.TryWrite(envelope);
            else
                _pending = envelope;
        }
    }

    internal void SetLifecycle(BrokerLifecycleState state)
    {
        lock (_gate)
        {
            if (_revoked) return;
            _lifecycle = state;
            if (state is BrokerLifecycleState.Background or BrokerLifecycleState.Destroying)
            {
                while (_events.Reader.TryRead(out var queued)) _pending = queued;
                if (state == BrokerLifecycleState.Destroying) RevokeLocked();
            }
            else if (_pending is not null)
            {
                _events.Writer.TryWrite(_pending);
                _pending = null;
            }
        }
    }

    internal void Revoke()
    {
        lock (_gate) RevokeLocked();
    }

    private void RevokeLocked()
    {
        if (_revoked) return;
        _revoked = true;
        _pending = null;
        while (_events.Reader.TryRead(out _)) { }
        _events.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Revoke();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Identity-bound broker session. Every operation rechecks declaration,
/// durable consent, capability shape, and lifecycle before reaching a backend.
/// </summary>
public sealed class PlatformCapabilityBroker : IAsyncDisposable
{
    private readonly BrokerWidgetIdentity _identity;
    private readonly HashSet<string> _declaredCapabilities;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformBrokerBackend _backend;
    private readonly object _gate = new();
    private readonly List<BrokerEventSubscription> _subscriptions = [];
    private BrokerLifecycleState _lifecycle = BrokerLifecycleState.Background;
    private long _eventSequence;
    private bool _disposed;

    public PlatformCapabilityBroker(
        BrokerWidgetIdentity authenticatedIdentity,
        IEnumerable<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend)
    {
        _identity = authenticatedIdentity ?? throw new ArgumentNullException(nameof(authenticatedIdentity));
        _identity.Validate();
        ArgumentNullException.ThrowIfNull(declaredCapabilities);
        _declaredCapabilities = new HashSet<string>(declaredCapabilities, StringComparer.Ordinal);
        if (_declaredCapabilities.Count == 0 ||
            _declaredCapabilities.Any(capability => !PlatformCapabilities.TryGet(capability, out _)))
            throw new BrokerException("invalid_declaration", "Declared capabilities are empty or unsupported.");
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.EventPublished += OnBackendEvent;
    }

    public BrokerLifecycleState Lifecycle { get { lock (_gate) return _lifecycle; } }

    public void SetLifecycle(BrokerLifecycleState lifecycle)
    {
        if (!Enum.IsDefined(lifecycle))
            throw new BrokerException("invalid_lifecycle", "Broker lifecycle is invalid.");
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_lifecycle == BrokerLifecycleState.Destroying)
                throw new BrokerException("invalid_lifecycle", "Destroying broker cannot transition.");
            _lifecycle = lifecycle;
            foreach (var subscription in _subscriptions) subscription.SetLifecycle(lifecycle);
        }
    }

    public async Task<BrokerResponseEnvelope> HandleAsync(
        ReadOnlyMemory<byte> requestUtf8,
        CancellationToken cancellationToken = default)
    {
        BrokerRequestEnvelope? request = null;
        try
        {
            request = BrokerJson.ParseRequest(requestUtf8.Span);
            var payload = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return new(BrokerJson.ProtocolVersion, request.RequestId, true, payload, null);
        }
        catch (BrokerException exception)
        {
            return new(BrokerJson.ProtocolVersion, request?.RequestId ?? 0, false, null, exception.Code);
        }
    }

    public async Task<JsonElement> ExecuteAsync(
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BrokerLifecycleState lifecycle;
        lock (_gate)
        {
            ThrowIfDisposed();
            lifecycle = _lifecycle;
        }
        if (request.Widget != _identity)
            throw new BrokerException("identity_mismatch", "Broker request identity does not match its channel.");
        var capability = await AuthorizeAsync(request.CapabilityId, request.Operation,
            cancellationToken).ConfigureAwait(false);
        DemandLifecycle(capability.Kind, lifecycle);

        return request.Operation switch
        {
            PlatformCapabilities.AudioSessionsList =>
                BrokerJson.ToElement(ValidateAudioSessions(DemandEmptyPayload(request.Payload),
                    await _backend.GetAudioSessionsAsync(cancellationToken).ConfigureAwait(false))),
            PlatformCapabilities.AudioSessionSetVolume =>
                await SetAudioVolumeAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            PlatformCapabilities.AudioSessionSetMuted =>
                await SetAudioMutedAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkStatusGet =>
                BrokerJson.ToElement(ValidateNetworkStatus(DemandEmptyPayload(request.Payload),
                    await _backend.GetNetworkStatusAsync(cancellationToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkSavedProfilesList =>
                BrokerJson.ToElement(ValidateNetworkProfiles(DemandEmptyPayload(request.Payload),
                    await _backend.GetSavedNetworkProfilesAsync(cancellationToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkSavedProfileSwitch =>
                await SwitchNetworkAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            _ => throw new BrokerException("unsupported_operation", "Broker operation is unsupported."),
        };
    }

    public async Task<BrokerEventSubscription> SubscribeAsync(
        string capabilityId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        BrokerLifecycleState lifecycle;
        lock (_gate)
        {
            ThrowIfDisposed();
            lifecycle = _lifecycle;
        }
        var capability = await AuthorizeAsync(capabilityId, operation: null,
            cancellationToken).ConfigureAwait(false);
        if (capability.Kind != BrokerCapabilityKind.Read || !capability.Events.Contains(eventType))
            throw new BrokerException("unsupported_event", "Capability event is unsupported.");
        DemandLifecycle(BrokerCapabilityKind.Read, lifecycle);
        var subscription = new BrokerEventSubscription(capabilityId, eventType, lifecycle);
        lock (_gate)
        {
            ThrowIfDisposed();
            _subscriptions.Add(subscription);
        }
        return subscription;
    }

    /// <summary>Reconciles durable consent after a UI or another process changes it.</summary>
    public async Task RefreshConsentAsync(CancellationToken cancellationToken = default)
    {
        BrokerEventSubscription[] subscriptions;
        lock (_gate) subscriptions = _subscriptions.ToArray();
        ConsentDocument document;
        try
        {
            document = await _consentStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is BrokerException or IOException or
                                          UnauthorizedAccessException or System.Security.SecurityException)
        {
            RevokeSubscriptions();
            return;
        }

        foreach (var subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = document.Entries.FirstOrDefault(entry =>
                entry.PackageId == _identity.PackageId &&
                entry.PublisherId == _identity.PublisherId &&
                entry.CapabilityId == subscription.CapabilityId)?.Decision;
            if (decision != ConsentDecision.Grant) subscription.Revoke();
        }
    }

    internal void RevokeSubscriptions()
    {
        lock (_gate)
            foreach (var subscription in _subscriptions) subscription.Revoke();
    }

    private async Task<BrokerCapabilityDefinition> AuthorizeAsync(
        string capabilityId,
        string? operation,
        CancellationToken cancellationToken)
    {
        if (!PlatformCapabilities.TryGet(capabilityId, out var capability) ||
            (operation is not null && !capability.Operations.Contains(operation)))
            throw new BrokerException("unsupported_capability", "Capability or operation is unsupported.");
        if (!_declaredCapabilities.Contains(capabilityId))
            throw new BrokerException("capability_not_declared", "Capability was not declared by the widget.");
        var decision = await _consentStore.GetDecisionAsync(
            _identity, capabilityId, cancellationToken).ConfigureAwait(false);
        if (decision != ConsentDecision.Grant)
            throw new BrokerException("permission_denied", "Capability permission was denied.");
        return capability;
    }

    private static void DemandLifecycle(BrokerCapabilityKind kind, BrokerLifecycleState lifecycle)
    {
        var allowed = kind == BrokerCapabilityKind.Control
            ? lifecycle == BrokerLifecycleState.Interactive
            : lifecycle is BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive;
        if (!allowed)
            throw new BrokerException("lifecycle_denied", "Capability is unavailable in this lifecycle.");
    }

    private async Task<JsonElement> SetAudioVolumeAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioSessionVolumeRequest>(payload);
        ContractValidation.OpaqueId(request.SessionId);
        if (!double.IsFinite(request.Volume) || request.Volume is < 0 or > 1)
            throw new BrokerException("invalid_payload", "Audio volume must be between zero and one.");
        await _backend.SetAudioSessionVolumeAsync(
            request.SessionId, request.Volume, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetAudioMutedAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioSessionMutedRequest>(payload);
        ContractValidation.OpaqueId(request.SessionId);
        await _backend.SetAudioSessionMutedAsync(
            request.SessionId, request.IsMuted, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SwitchNetworkAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SwitchSavedNetworkProfileRequest>(payload);
        ContractValidation.OpaqueId(request.ProfileId);
        await _backend.SwitchSavedNetworkProfileAsync(
            request.ProfileId, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private static bool DemandEmptyPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Any())
            throw new BrokerException("invalid_payload", "Operation payload must be an empty object.");
        return true;
    }

    private static IReadOnlyList<AudioSessionSummary> ValidateAudioSessions(
        IReadOnlyList<AudioSessionSummary>? sessions) => ValidateAudioSessions(true, sessions);

    private static IReadOnlyList<AudioSessionSummary> ValidateAudioSessions(
        bool _, IReadOnlyList<AudioSessionSummary>? sessions)
    {
        if (sessions is null)
            throw new BrokerException("invalid_backend_data", "Audio session result is invalid.");
        if (sessions.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Too many audio sessions.");
        foreach (var session in sessions)
        {
            if (session is null)
                throw new BrokerException("invalid_backend_data", "Audio session result is invalid.");
            ContractValidation.OpaqueId(session.SessionId, "invalid_backend_data");
            ContractValidation.DisplayName(session.DisplayName);
            if (!double.IsFinite(session.Volume) || session.Volume is < 0 or > 1)
                throw new BrokerException("invalid_backend_data", "Audio volume is invalid.");
        }
        return sessions.ToArray();
    }

    private static NetworkStatusSummary ValidateNetworkStatus(
        NetworkStatusSummary status) => ValidateNetworkStatus(true, status);

    private static NetworkStatusSummary ValidateNetworkStatus(bool _, NetworkStatusSummary? status)
    {
        if (status is null)
            throw new BrokerException("invalid_backend_data", "Network status is invalid.");
        if (!Enum.IsDefined(status.Connectivity))
            throw new BrokerException("invalid_backend_data", "Network connectivity is invalid.");
        if (status.ActiveProfileId is not null) ContractValidation.OpaqueId(
            status.ActiveProfileId, "invalid_backend_data");
        if (status.ActiveProfileName is not null) ContractValidation.DisplayName(status.ActiveProfileName);
        if ((status.ActiveProfileId is null) != (status.ActiveProfileName is null))
            throw new BrokerException("invalid_backend_data", "Network profile summary is incomplete.");
        ContractValidation.Percent(status.SignalPercent);
        return status;
    }

    private static IReadOnlyList<SavedNetworkProfileSummary> ValidateNetworkProfiles(
        IReadOnlyList<SavedNetworkProfileSummary>? profiles) => ValidateNetworkProfiles(true, profiles);

    private static IReadOnlyList<SavedNetworkProfileSummary> ValidateNetworkProfiles(
        bool _, IReadOnlyList<SavedNetworkProfileSummary>? profiles)
    {
        if (profiles is null)
            throw new BrokerException("invalid_backend_data", "Network profile result is invalid.");
        if (profiles.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Too many saved network profiles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null)
                throw new BrokerException("invalid_backend_data", "Network profile result is invalid.");
            ContractValidation.OpaqueId(profile.ProfileId, "invalid_backend_data");
            ContractValidation.DisplayName(profile.DisplayName);
            ContractValidation.Percent(profile.SignalPercent);
            if (!ids.Add(profile.ProfileId))
                throw new BrokerException("invalid_backend_data", "Network profile IDs are duplicated.");
        }
        return profiles.ToArray();
    }

    private void OnBackendEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        try
        {
            if (platformEvent is null) return;
            if (!PlatformCapabilities.TryGet(platformEvent.CapabilityId, out var capability) ||
                !capability.Events.Contains(platformEvent.EventType)) return;
            var payload = platformEvent.EventType switch
            {
                PlatformCapabilities.AudioSessionsChanged when
                    platformEvent.Payload is AudioSessionsChangedEvent audio =>
                    BrokerJson.ToElement(new AudioSessionsChangedEvent(
                        ValidateAudioSessions(audio.Sessions))),
                PlatformCapabilities.NetworkStatusChanged when
                    platformEvent.Payload is NetworkStatusChangedEvent network =>
                    BrokerJson.ToElement(new NetworkStatusChangedEvent(
                        ValidateNetworkStatus(network.Status))),
                _ => throw new BrokerException("invalid_backend_data", "Broker event payload is invalid."),
            };
            var envelope = new BrokerEventEnvelope(BrokerJson.ProtocolVersion,
                Interlocked.Increment(ref _eventSequence), platformEvent.CapabilityId,
                platformEvent.EventType, payload);
            if (JsonSerializer.SerializeToUtf8Bytes(envelope, BrokerJson.StrictOptions).Length >
                BrokerJson.MaximumEventBytes) return;
            BrokerEventSubscription[] subscriptions;
            lock (_gate)
            {
                if (_disposed) return;
                subscriptions = _subscriptions.Where(subscription =>
                    subscription.CapabilityId == platformEvent.CapabilityId &&
                    subscription.EventType == platformEvent.EventType).ToArray();
            }
            foreach (var subscription in subscriptions) subscription.Publish(envelope);
        }
        catch (BrokerException)
        {
            // Backend data is untrusted at this boundary. Invalid events are
            // dropped without disclosing their contents to widgets.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlatformCapabilityBroker));
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _lifecycle = BrokerLifecycleState.Destroying;
            _backend.EventPublished -= OnBackendEvent;
            foreach (var subscription in _subscriptions) subscription.Revoke();
            _subscriptions.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
