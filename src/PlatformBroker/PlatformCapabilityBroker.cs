using System.Text.Json;
using System.Text;
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
/// Identity-bound broker session. Every operation rechecks its host-owned
/// authority set, capability shape, and lifecycle before reaching a backend;
/// manifest capabilities additionally require durable consent.
/// </summary>
public sealed class PlatformCapabilityBroker : IAsyncDisposable
{
    public static readonly TimeSpan MaximumDashboardGestureLifetime = TimeSpan.FromSeconds(2);
    public const int MaximumDashboardGestureAuthorities = 16;
    internal const int MaximumAppLibraryItems = 512;
    internal const int MaximumAppLibraryPageSize = 64;
    internal const int MaximumResolvedAppLibraryItems = 64;
    private readonly BrokerWidgetIdentity _identity;
    private readonly HashSet<string> _declaredCapabilities;
    private readonly HashSet<string> _hostGrantedCapabilities;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformBrokerBackend _backend;
    private readonly IAppLibrarySavedIdIssuer _appLibrarySavedIdIssuer;
    private readonly object _gate = new();
    private readonly List<BrokerEventSubscription> _subscriptions = [];
    private readonly HashSet<RequestLease> _requestLeases = [];
    private readonly HashSet<string> _revokedCapabilities = new(StringComparer.Ordinal);
    private readonly Dictionary<DashboardGestureKey, DashboardGestureAuthority>
        _dashboardGestureAuthorities = [];
    private readonly SemaphoreSlim _appLibraryGate = new(1, 1);
    private readonly SemaphoreSlim _loopbackGate = new(2, 2);
    private readonly Dictionary<string, string> _appLibraryPublicIdsByBackendId =
        new(StringComparer.Ordinal);
    private IReadOnlyList<AppLibraryItemSummary>? _appLibrarySnapshot;
    private Dictionary<string, string> _appLibraryBackendIdsByPublicId =
        new(StringComparer.Ordinal);
    private long _lastDashboardGestureSequence;
    private BrokerLifecycleState _lifecycle = BrokerLifecycleState.Created;
    private long _eventSequence;
    private bool _disposed;

    public PlatformCapabilityBroker(
        BrokerWidgetIdentity authenticatedIdentity,
        IEnumerable<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend,
        IEnumerable<string>? hostGrantedCapabilities = null)
        : this(
            authenticatedIdentity, declaredCapabilities, consentStore, backend,
            hostGrantedCapabilities, AppLibrarySavedIdIssuer.Shared)
    {
    }

    internal PlatformCapabilityBroker(
        BrokerWidgetIdentity authenticatedIdentity,
        IEnumerable<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend,
        IEnumerable<string>? hostGrantedCapabilities,
        IAppLibrarySavedIdIssuer appLibrarySavedIdIssuer)
    {
        _identity = authenticatedIdentity ?? throw new ArgumentNullException(nameof(authenticatedIdentity));
        _identity.Validate();
        ArgumentNullException.ThrowIfNull(declaredCapabilities);
        _declaredCapabilities = new HashSet<string>(declaredCapabilities, StringComparer.Ordinal);
        _hostGrantedCapabilities = new HashSet<string>(
            hostGrantedCapabilities ?? [], StringComparer.Ordinal);
        if (_declaredCapabilities.Any(capability =>
                !PlatformCapabilities.IsManifestDeclarable(capability)) ||
            _hostGrantedCapabilities.Any(capability =>
                !PlatformCapabilities.IsHostGranted(capability)) ||
            _declaredCapabilities.Count + _hostGrantedCapabilities.Count == 0)
            throw new BrokerException(
                "invalid_declaration", "Broker capability authorities are empty or unsupported.");
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _appLibrarySavedIdIssuer = appLibrarySavedIdIssuer ??
            throw new ArgumentNullException(nameof(appLibrarySavedIdIssuer));
        _backend.EventPublished += OnBackendEvent;
    }

    public BrokerLifecycleState Lifecycle { get { lock (_gate) return _lifecycle; } }

    public void SetLifecycle(BrokerLifecycleState lifecycle)
    {
        if (!Enum.IsDefined(lifecycle))
            throw new BrokerException("invalid_lifecycle", "Broker lifecycle is invalid.");
        List<RequestLease> canceled = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_lifecycle == BrokerLifecycleState.Destroying)
                throw new BrokerException("invalid_lifecycle", "Destroying broker cannot transition.");
            if (_lifecycle != lifecycle) ClearDashboardGestureAuthoritiesLocked();
            _lifecycle = lifecycle;
            foreach (var subscription in _subscriptions) subscription.SetLifecycle(lifecycle);
            foreach (var lease in _requestLeases)
            {
                if (!IsLifecycleAllowed(lease, lifecycle))
                {
                    lease.MarkCanceledLocked("lifecycle_denied");
                    canceled.Add(lease);
                }
            }
        }
        foreach (var lease in canceled) lease.SignalCancellation();
    }

    /// <summary>
    /// Installs a trusted-host-only, single-use authority for one exact control
    /// operation while the widget remains Visible. Workers cannot reach this
    /// method through broker IPC.
    /// </summary>
    public void GrantDashboardGestureAuthority(
        string capabilityId,
        string operationId,
        long inputSequence,
        long snapshotSequence,
        TimeSpan validFor)
    {
        if (!PlatformCapabilities.TryGet(capabilityId, out var capability) ||
            capability.KindForOperation(operationId) != BrokerCapabilityKind.Control ||
            !capability.Operations.Contains(operationId) ||
            !capability.AllowsDashboardGestureForOperation(operationId))
            throw new BrokerException(
                "unsupported_capability", "Dashboard authority requires a supported control operation.");
        if (!_declaredCapabilities.Contains(capabilityId))
            throw new BrokerException(
                "capability_not_declared", "Dashboard authority capability was not declared.");
        if (inputSequence <= 0 || snapshotSequence <= 0)
            throw new BrokerException("invalid_gesture", "Dashboard gesture sequences must be positive.");
        if (validFor <= TimeSpan.Zero || validFor > MaximumDashboardGestureLifetime)
            throw new BrokerException("invalid_gesture", "Dashboard gesture lifetime is invalid.");

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_lifecycle != BrokerLifecycleState.Visible)
                throw new BrokerException(
                    "lifecycle_denied", "Dashboard authority is available only while Visible.");
            if (inputSequence <= _lastDashboardGestureSequence)
                throw new BrokerException("gesture_replayed", "Dashboard gesture sequence was replayed.");
            _lastDashboardGestureSequence = inputSequence;
            RemoveExpiredDashboardGestureAuthoritiesLocked();
            if (_dashboardGestureAuthorities.Count >= MaximumDashboardGestureAuthorities)
                throw new BrokerException(
                    "gesture_limit", "Too many dashboard gesture authorities are pending.");
            var expiresAt = System.Diagnostics.Stopwatch.GetTimestamp() +
                (long)Math.Ceiling(validFor.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
            var key = new DashboardGestureKey(inputSequence, snapshotSequence);
            var authority = new DashboardGestureAuthority(
                capabilityId, operationId, expiresAt);
            authority.ExpiryTimer = new Timer(
                _ => ExpireDashboardGestureAuthority(key), null,
                validFor, Timeout.InfiniteTimeSpan);
            _dashboardGestureAuthorities.Add(key, authority);
        }
    }

    public void RevokeDashboardGestureAuthority(long inputSequence)
    {
        lock (_gate)
        {
            var key = _dashboardGestureAuthorities.Keys.FirstOrDefault(
                candidate => candidate.InputSequence == inputSequence);
            if (key.InputSequence == inputSequence)
                RemoveDashboardGestureAuthorityLocked(key);
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
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        if (request.Widget != _identity)
            throw new BrokerException("identity_mismatch", "Broker request identity does not match its channel.");
        var capability = await AuthorizeAsync(request.CapabilityId, request.Operation,
            cancellationToken).ConfigureAwait(false);
        using var lease = CreateRequestLease(capability, request, cancellationToken);
        try
        {
            lease.ThrowIfBrokerCanceled();
            return await ExecuteAuthorizedAsync(request, lease).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The broker request was canceled by its caller.", exception, cancellationToken);
        }
        catch (OperationCanceledException exception) when (lease.CancellationCode is { } code)
        {
            throw new BrokerException(code,
                code == "capability_revoked"
                    ? "Capability permission was revoked while the request was running."
                    : "Capability became unavailable in this lifecycle while the request was running.",
                exception);
        }
    }

    private async Task<JsonElement> ExecuteAuthorizedAsync(
        BrokerRequestEnvelope request,
        RequestLease lease)
    {
        // This is the dispatch linearization point. A host transition or
        // consent refresh that won the broker lock first has already marked
        // the lease, even if its cancellation callbacks are still draining.
        lease.ThrowIfBrokerCanceled();
        var requestToken = lease.Token;
        return request.Operation switch
        {
            PlatformCapabilities.AudioSessionsList =>
                BrokerJson.ToElement(ValidateAudioSessions(DemandEmptyPayload(request.Payload),
                    await _backend.GetAudioSessionsAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.AudioSessionSetVolume =>
                await SetAudioVolumeAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.AudioSessionSetMuted =>
                await SetAudioMutedAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.AudioOutputGet =>
                BrokerJson.ToElement(ValidateAudioOutput(DemandEmptyPayload(request.Payload),
                    await _backend.GetAudioOutputAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.AudioOutputSetVolume =>
                await SetAudioOutputVolumeAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.AudioOutputSetMuted =>
                await SetAudioOutputMutedAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.AudioDevicesList =>
                BrokerJson.ToElement(ValidateAudioDevices(DemandEmptyPayload(request.Payload),
                    await _backend.GetAudioDevicesAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.AudioInputGet =>
                BrokerJson.ToElement(ValidateAudioInput(DemandEmptyPayload(request.Payload),
                    await _backend.GetAudioInputAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.AudioInputSetVolume =>
                await SetAudioInputVolumeAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.AudioInputSetMuted =>
                await SetAudioInputMutedAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkStatusGet =>
                BrokerJson.ToElement(ValidateNetworkStatus(DemandEmptyPayload(request.Payload),
                    await _backend.GetNetworkStatusAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkSavedProfilesList =>
                BrokerJson.ToElement(ValidateNetworkProfiles(DemandEmptyPayload(request.Payload),
                    await _backend.GetSavedNetworkProfilesAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkSavedProfileSwitch =>
                await SwitchNetworkAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkAvailableWifiGet =>
                BrokerJson.ToElement(ValidateAvailableWifiNetworks(
                    DemandEmptyPayload(request.Payload),
                    await _backend.GetAvailableWifiNetworksAsync(requestToken)
                        .ConfigureAwait(false))),
            PlatformCapabilities.NetworkWifiScan =>
                await RequestWifiScanAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkAvailableWifiConnect =>
                await ConnectAvailableWifiAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkWifiRadioGet =>
                BrokerJson.ToElement(ValidateWifiRadio(DemandEmptyPayload(request.Payload),
                    await _backend.GetWifiRadioAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkWifiRadioSet =>
                await SetWifiRadioAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkBluetoothGet =>
                BrokerJson.ToElement(ValidateBluetooth(DemandEmptyPayload(request.Payload),
                    await _backend.GetBluetoothAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.NetworkBluetoothRadioSet =>
                await SetBluetoothRadioAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.NetworkBluetoothDevicePair =>
                BrokerJson.ToElement(await PairBluetoothDeviceAsync(
                    request.Payload, requestToken).ConfigureAwait(false)),
            PlatformCapabilities.NetworkBluetoothDeviceSettingsOpen =>
                await OpenBluetoothDeviceSettingsAsync(
                    request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.RecentActivitiesList =>
                BrokerJson.ToElement(ValidateRecentActivities(DemandEmptyPayload(request.Payload),
                    await _backend.GetRecentActivitiesAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.AppLibraryList =>
                BrokerJson.ToElement(await GetAppLibraryPageAsync(
                    request.Payload, requestToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryResolveSaved =>
                BrokerJson.ToElement(await ResolveSavedAppLibraryItemsAsync(
                    request.Payload, requestToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryLaunch =>
                await LaunchAppLibraryItemAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.MediaSessionsGet =>
                BrokerJson.ToElement(ValidateMediaSessions(DemandEmptyPayload(request.Payload),
                    await _backend.GetMediaSessionsAsync(requestToken).ConfigureAwait(false))),
            PlatformCapabilities.MediaSessionControl =>
                await ControlMediaSessionAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.LoopbackHttpGetJson =>
                await SendLoopbackJsonAsync(request, lease, isPost: false).ConfigureAwait(false),
            PlatformCapabilities.LoopbackHttpPostJson =>
                await SendLoopbackJsonAsync(request, lease, isPost: true).ConfigureAwait(false),
            PlatformCapabilities.PrivateSecretExists =>
                await GetPrivateSecretExistsAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateSecretMetadata =>
                await GetPrivateSecretMetadataAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateSecretSave =>
                await SavePrivateSecretAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateSecretDelete =>
                await DeletePrivateSecretAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateStateRead =>
                await ReadPrivateStateAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateStateWrite =>
                await WritePrivateStateAsync(request.Payload, requestToken).ConfigureAwait(false),
            PlatformCapabilities.PrivateStateClear =>
                await ClearPrivateStateAsync(request.Payload, requestToken).ConfigureAwait(false),
            _ => throw new BrokerException("unsupported_operation", "Broker operation is unsupported."),
        };
    }

    public async Task<BrokerEventSubscription> SubscribeAsync(
        string capabilityId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        var capability = await AuthorizeAsync(capabilityId, operation: null,
            cancellationToken).ConfigureAwait(false);
        if (capability.Kind != BrokerCapabilityKind.Read || !capability.Events.Contains(eventType))
            throw new BrokerException("unsupported_event", "Capability event is unsupported.");
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_revokedCapabilities.Contains(capabilityId))
                throw new BrokerException("capability_revoked", "Capability permission was revoked.");
            DemandLifecycle(capability, BrokerCapabilityKind.Read, _lifecycle);
            var subscription = new BrokerEventSubscription(capabilityId, eventType, _lifecycle);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <summary>Reconciles durable consent after a UI or another process changes it.</summary>
    public async Task RefreshConsentAsync(CancellationToken cancellationToken = default)
    {
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

        var granted = document.Entries.Where(entry =>
                entry.PackageId == _identity.PackageId &&
                entry.PublisherId == _identity.PublisherId &&
                entry.Decision == ConsentDecision.Grant)
            .Select(entry => entry.CapabilityId)
            .ToHashSet(StringComparer.Ordinal);
        List<RequestLease> canceled = [];
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _revokedCapabilities.Clear();
            foreach (var capabilityId in _declaredCapabilities)
            {
                if (!granted.Contains(capabilityId)) _revokedCapabilities.Add(capabilityId);
            }
            foreach (var subscription in _subscriptions)
            {
                if (_revokedCapabilities.Contains(subscription.CapabilityId)) subscription.Revoke();
            }
            foreach (var lease in _requestLeases)
            {
                if (_revokedCapabilities.Contains(lease.CapabilityId))
                {
                    lease.MarkCanceledLocked("capability_revoked");
                    canceled.Add(lease);
                }
            }
            foreach (var entry in _dashboardGestureAuthorities.ToArray())
            {
                if (_revokedCapabilities.Contains(entry.Value.CapabilityId))
                    RemoveDashboardGestureAuthorityLocked(entry.Key);
            }
        }
        foreach (var lease in canceled) lease.SignalCancellation();
    }

    internal void RevokeSubscriptions()
    {
        List<RequestLease> canceled = [];
        lock (_gate)
        {
            foreach (var capabilityId in _declaredCapabilities)
                _revokedCapabilities.Add(capabilityId);
            foreach (var subscription in _subscriptions) subscription.Revoke();
            foreach (var lease in _requestLeases)
            {
                lease.MarkCanceledLocked("capability_revoked");
                canceled.Add(lease);
            }
            ClearDashboardGestureAuthoritiesLocked();
        }
        foreach (var lease in canceled) lease.SignalCancellation();
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
        {
            if (!_hostGrantedCapabilities.Contains(capabilityId) ||
                capability.AccessPolicy != BrokerCapabilityAccessPolicy.HostGranted)
                throw new BrokerException(
                    "capability_not_declared", "Capability was not authorized for this widget channel.");
            return capability;
        }
        if (capability.AccessPolicy != BrokerCapabilityAccessPolicy.ManifestConsent)
            throw new BrokerException(
                "invalid_declaration", "Host-granted services cannot be manifest-declared.");
        var decision = await _consentStore.GetDecisionAsync(
            _identity, capabilityId, cancellationToken).ConfigureAwait(false);
        if (decision != ConsentDecision.Grant)
            throw new BrokerException("permission_denied", "Capability permission was denied.");
        return capability;
    }

    private static void DemandLifecycle(
        BrokerCapabilityDefinition capability,
        BrokerCapabilityKind kind,
        BrokerLifecycleState lifecycle)
    {
        var allowed = capability.AllowsBackground
            ? lifecycle is BrokerLifecycleState.Background or
                BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive
            : kind == BrokerCapabilityKind.Control
            ? lifecycle == BrokerLifecycleState.Interactive
            : lifecycle is BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive;
        if (!allowed)
            throw new BrokerException("lifecycle_denied", "Capability is unavailable in this lifecycle.");
    }

    private static bool IsLifecycleAllowed(
        RequestLease lease, BrokerLifecycleState lifecycle) =>
        lease.AllowsBackground
            ? lifecycle is BrokerLifecycleState.Background or
                BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive
            : lease.Kind == BrokerCapabilityKind.Control
            ? lifecycle == BrokerLifecycleState.Interactive ||
                lease.DashboardGestureAuthorized && lifecycle == BrokerLifecycleState.Visible
            : lifecycle is BrokerLifecycleState.Visible or BrokerLifecycleState.Interactive;

    private RequestLease CreateRequestLease(
        BrokerCapabilityDefinition capability,
        BrokerRequestEnvelope request,
        CancellationToken callerCancellation)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_revokedCapabilities.Contains(capability.Id))
                throw new BrokerException("capability_revoked", "Capability permission was revoked.");
            var dashboardGestureAuthorized = false;
            var operationKind = capability.KindForOperation(request.Operation);
            if (operationKind == BrokerCapabilityKind.Control &&
                capability.AllowsDashboardGestureForOperation(request.Operation) &&
                _lifecycle == BrokerLifecycleState.Visible)
            {
                if (request.GestureInputSequence is not { } inputSequence ||
                    request.GestureSnapshotSequence is not { } snapshotSequence)
                    throw new BrokerException(
                        "lifecycle_denied", "Capability is unavailable in this lifecycle.");
                var key = new DashboardGestureKey(inputSequence, snapshotSequence);
                if (!_dashboardGestureAuthorities.TryGetValue(key, out var authority) ||
                    authority.IsExpired ||
                    !string.Equals(authority.CapabilityId, capability.Id, StringComparison.Ordinal) ||
                    !string.Equals(authority.OperationId, request.Operation, StringComparison.Ordinal))
                {
                    if (authority?.IsExpired == true)
                        RemoveDashboardGestureAuthorityLocked(key);
                    throw new BrokerException(
                        "lifecycle_denied", "Capability is unavailable in this lifecycle.");
                }
                dashboardGestureAuthorized = true;
                RemoveDashboardGestureAuthorityLocked(key);
            }
            else
            {
                DemandLifecycle(capability, operationKind, _lifecycle);
            }
            var lease = new RequestLease(
                this, capability.Id, operationKind, capability.AllowsBackground,
                dashboardGestureAuthorized,
                callerCancellation);
            _requestLeases.Add(lease);
            return lease;
        }
    }

    private void ReleaseRequestLease(RequestLease lease)
    {
        lock (_gate) _requestLeases.Remove(lease);
    }

    private sealed class RequestLease : IDisposable
    {
        private readonly PlatformCapabilityBroker _owner;
        private readonly CancellationTokenSource _brokerCancellation = new();
        private readonly CancellationTokenSource _linkedCancellation;
        private string? _cancellationCode;
        private int _disposed;

        internal RequestLease(
            PlatformCapabilityBroker owner,
            string capabilityId,
            BrokerCapabilityKind kind,
            bool allowsBackground,
            bool dashboardGestureAuthorized,
            CancellationToken callerCancellation)
        {
            _owner = owner;
            CapabilityId = capabilityId;
            Kind = kind;
            AllowsBackground = allowsBackground;
            DashboardGestureAuthorized = dashboardGestureAuthorized;
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellation, _brokerCancellation.Token);
        }

        internal string CapabilityId { get; }
        internal BrokerCapabilityKind Kind { get; }
        internal bool AllowsBackground { get; }
        internal bool DashboardGestureAuthorized { get; }
        internal CancellationToken Token => _linkedCancellation.Token;
        internal string? CancellationCode => Volatile.Read(ref _cancellationCode);

        internal void ThrowIfBrokerCanceled()
        {
            Token.ThrowIfCancellationRequested();
            if (CancellationCode is { } code)
                throw new BrokerException(code,
                    code == "capability_revoked"
                        ? "Capability permission was revoked before backend dispatch."
                        : "Capability is unavailable in this lifecycle.");
        }

        internal void MarkCanceledLocked(string code)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Interlocked.CompareExchange(ref _cancellationCode, code, null) is not null)
                return;
        }

        internal void SignalCancellation()
        {
            if (CancellationCode is null) return;
            try { _brokerCancellation.Cancel(); }
            catch (ObjectDisposedException)
            {
                // A request may complete between being marked under the broker
                // lock and signaling outside it. Completion wins safely.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.ReleaseRequestLease(this);
            _linkedCancellation.Dispose();
            _brokerCancellation.Dispose();
        }
    }

    private void ExpireDashboardGestureAuthority(DashboardGestureKey key)
    {
        lock (_gate)
        {
            RemoveDashboardGestureAuthorityLocked(key);
        }
    }

    private void RemoveExpiredDashboardGestureAuthoritiesLocked()
    {
        foreach (var entry in _dashboardGestureAuthorities.ToArray())
        {
            if (entry.Value.IsExpired) RemoveDashboardGestureAuthorityLocked(entry.Key);
        }
    }

    private void RemoveDashboardGestureAuthorityLocked(DashboardGestureKey key)
    {
        if (!_dashboardGestureAuthorities.Remove(key, out var authority)) return;
        authority.ExpiryTimer?.Dispose();
        authority.ExpiryTimer = null;
    }

    private void ClearDashboardGestureAuthoritiesLocked()
    {
        foreach (var authority in _dashboardGestureAuthorities.Values)
            authority.ExpiryTimer?.Dispose();
        _dashboardGestureAuthorities.Clear();
    }

    private readonly record struct DashboardGestureKey(
        long InputSequence,
        long SnapshotSequence);

    private sealed class DashboardGestureAuthority(
        string capabilityId,
        string operationId,
        long expiresAtTimestamp)
    {
        internal string CapabilityId { get; } = capabilityId;
        internal string OperationId { get; } = operationId;
        internal long ExpiresAtTimestamp { get; } = expiresAtTimestamp;
        internal Timer? ExpiryTimer { get; set; }
        internal bool IsExpired =>
            System.Diagnostics.Stopwatch.GetTimestamp() >= ExpiresAtTimestamp;
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

    private async Task<JsonElement> SetAudioOutputVolumeAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioOutputVolumeRequest>(payload);
        if (!double.IsFinite(request.Volume) || request.Volume is < 0 or > 1)
            throw new BrokerException("invalid_payload", "Audio output volume must be between zero and one.");
        await _backend.SetAudioOutputVolumeAsync(request.Volume, cancellationToken)
            .ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetAudioOutputMutedAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioOutputMutedRequest>(payload);
        await _backend.SetAudioOutputMutedAsync(request.IsMuted, cancellationToken)
            .ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetAudioInputVolumeAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioInputVolumeRequest>(payload);
        if (!double.IsFinite(request.Volume) || request.Volume is < 0 or > 1)
            throw new BrokerException("invalid_payload", "Audio input volume must be between zero and one.");
        await _backend.SetAudioInputVolumeAsync(request.Volume, cancellationToken)
            .ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetAudioInputMutedAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetAudioInputMutedRequest>(payload);
        await _backend.SetAudioInputMutedAsync(request.IsMuted, cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<JsonElement> RequestWifiScanAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        DemandEmptyPayload(payload);
        await _backend.RequestWifiScanAsync(cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> ConnectAvailableWifiAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ConnectAvailableWifiNetworkRequest>(payload);
        ContractValidation.OpaqueId(request.NetworkId);
        await _backend.ConnectAvailableWifiNetworkAsync(request.NetworkId, cancellationToken)
            .ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetWifiRadioAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetWifiRadioStateRequest>(payload);
        await _backend.SetWifiRadioAsync(request.Enabled, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SetBluetoothRadioAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SetBluetoothRadioStateRequest>(payload);
        await _backend.SetBluetoothRadioAsync(request.Enabled, cancellationToken)
            .ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<PairBluetoothDeviceRequest>(payload);
        ContractValidation.OpaqueId(request.DeviceId);
        var result = await _backend.PairBluetoothDeviceAsync(
            request.DeviceId, cancellationToken).ConfigureAwait(false);
        if (result is null || !Enum.IsDefined(result.Outcome))
            throw new BrokerException(
                "invalid_backend_data", "Bluetooth pairing result is invalid.");
        return result;
    }

    private async Task<JsonElement> OpenBluetoothDeviceSettingsAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<OpenBluetoothDeviceSettingsRequest>(payload);
        ContractValidation.OpaqueId(request.DeviceId);
        await _backend.OpenBluetoothDeviceSettingsAsync(
            request.DeviceId, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private static IReadOnlyList<RecentActivitySummary> ValidateRecentActivities(
        bool _, IReadOnlyList<RecentActivitySummary>? activities) =>
        ValidateRecentActivities(activities);

    private static IReadOnlyList<RecentActivitySummary> ValidateRecentActivities(
        IReadOnlyList<RecentActivitySummary>? activities)
    {
        if (activities is null || activities.Count > 32)
            throw new BrokerException("invalid_backend_data", "Recent activity result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var mostRecentCount = 0;
        foreach (var activity in activities)
        {
            if (activity is null || !Enum.IsDefined(activity.Kind))
                throw new BrokerException("invalid_backend_data", "Recent activity entry is invalid.");
            ContractValidation.OpaqueId(activity.ActivityId, "invalid_backend_data");
            ContractValidation.DisplayName(activity.DisplayName);
            if (!ids.Add(activity.ActivityId) ||
                activity.IsMostRecent && ++mostRecentCount > 1 ||
                activity.IsMostRecent && !activity.IsRunning)
                throw new BrokerException("invalid_backend_data", "Recent activity entries are inconsistent.");
        }
        return activities.ToArray();
    }

    private async Task<AppLibraryPageSummary> GetAppLibraryPageAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<AppLibraryPageRequest>(payload);
        if (request.Offset < 0 || request.Offset > MaximumAppLibraryItems ||
            request.Limit is < 1 or > MaximumAppLibraryPageSize)
            throw new BrokerException("invalid_payload", "App library page bounds are invalid.");

        await _appLibraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Offset zero is the explicit refresh boundary. Later pages use the
            // broker-session snapshot, even if another widget refreshes the
            // shared Windows provider in the meantime.
            if (request.Offset == 0 || _appLibrarySnapshot is null)
            {
                var backendItems = ValidateAppLibrary(
                    await _backend.RefreshAppLibraryAsync(cancellationToken)
                        .ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
                _appLibrarySnapshot = ProjectAppLibrarySnapshot(backendItems);
            }

            var items = _appLibrarySnapshot;
            var page = items.Skip(request.Offset).Take(request.Limit).ToArray();
            var consumed = request.Offset + page.Length;
            return new AppLibraryPageSummary(
                page,
                consumed < items.Count ? consumed : null);
        }
        finally
        {
            _appLibraryGate.Release();
        }
    }

    private IReadOnlyList<AppLibraryItemSummary> ProjectAppLibrarySnapshot(
        IReadOnlyList<AppLibraryBackendItemSummary> backendItems)
    {
        var liveBackendIds = backendItems.Select(item => item.ProviderAppId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _appLibraryPublicIdsByBackendId.Keys
                     .Where(id => !liveBackendIds.Contains(id)).ToArray())
            _appLibraryPublicIdsByBackendId.Remove(stale);

        var byPublicId = new Dictionary<string, string>(StringComparer.Ordinal);
        var projected = new AppLibraryItemSummary[backendItems.Count];
        for (var index = 0; index < backendItems.Count; index++)
        {
            var item = backendItems[index];
            if (!_appLibraryPublicIdsByBackendId.TryGetValue(
                    item.ProviderAppId, out var publicId))
            {
                publicId = "app-" + Guid.NewGuid().ToString("N");
                _appLibraryPublicIdsByBackendId.Add(item.ProviderAppId, publicId);
            }
            byPublicId.Add(publicId, item.ProviderAppId);
            projected[index] = new AppLibraryItemSummary(
                publicId, item.DisplayName, item.Kind)
            {
                SavedId = _appLibrarySavedIdIssuer.Issue(
                    _identity, item.StableProviderIdentity),
            };
        }
        _appLibraryBackendIdsByPublicId = byPublicId;
        return Array.AsReadOnly(projected);
    }

    private static IReadOnlyList<AppLibraryBackendItemSummary> ValidateAppLibrary(
        IReadOnlyList<AppLibraryBackendItemSummary>? items)
    {
        if (items is null || items.Count > MaximumAppLibraryItems)
            throw new BrokerException("invalid_backend_data", "App library result is invalid.");
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        var stableIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || !Enum.IsDefined(item.Kind))
                throw new BrokerException("invalid_backend_data", "App library entry is invalid.");
            ContractValidation.OpaqueId(item.ProviderAppId, "invalid_backend_data");
            AppLibrarySavedIdIssuer.ValidateStableProviderIdentity(item.StableProviderIdentity);
            ContractValidation.DisplayName(item.DisplayName);
            if (!providerIds.Add(item.ProviderAppId) ||
                !stableIdentities.Add(item.StableProviderIdentity))
                throw new BrokerException(
                    "invalid_backend_data", "App library identities are duplicated.");
        }
        return items.ToArray();
    }

    private async Task<ResolveSavedAppLibraryItemsSummary> ResolveSavedAppLibraryItemsAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ResolveSavedAppLibraryItemsRequest>(payload);
        if (request.SavedIds is null ||
            request.SavedIds.Count > MaximumResolvedAppLibraryItems)
            throw new BrokerException(
                "invalid_payload", "Saved app-library identifier bounds are invalid.");
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in request.SavedIds)
        {
            ContractValidation.OpaqueId(savedId);
            if (!savedId.StartsWith("saved-", StringComparison.Ordinal) ||
                !requested.Add(savedId))
                throw new BrokerException(
                    "invalid_payload", "Saved app-library identifiers are invalid.");
        }
        if (request.SavedIds.Count == 0)
            return new ResolveSavedAppLibraryItemsSummary([]);

        await _appLibraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backendItems = ValidateAppLibrary(
                await _backend.RefreshAppLibraryAsync(cancellationToken)
                    .ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            _appLibrarySnapshot = ProjectAppLibrarySnapshot(backendItems);
            var currentBySavedId = _appLibrarySnapshot.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var resolved = request.SavedIds
                .Where(currentBySavedId.ContainsKey)
                .Select(savedId => currentBySavedId[savedId])
                .ToArray();
            return new ResolveSavedAppLibraryItemsSummary(resolved);
        }
        finally
        {
            _appLibraryGate.Release();
        }
    }

    private async Task<JsonElement> LaunchAppLibraryItemAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<LaunchAppLibraryItemRequest>(payload);
        ContractValidation.OpaqueId(request.AppId);
        await _appLibraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_appLibraryBackendIdsByPublicId.TryGetValue(
                    request.AppId, out var backendAppId))
                throw new BrokerException(
                    "app_not_found", "The selected app is no longer available.");
            await _backend.LaunchAppLibraryItemAsync(backendAppId, cancellationToken)
                .ConfigureAwait(false);
            return BrokerJson.ToElement(new { acknowledged = true });
        }
        finally
        {
            _appLibraryGate.Release();
        }
    }

    private async Task<JsonElement> ControlMediaSessionAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ControlMediaSessionRequest>(payload);
        ContractValidation.OpaqueId(request.SessionId);
        if (!Enum.IsDefined(request.Command))
            throw new BrokerException("invalid_payload", "Media session command is invalid.");
        await _backend.ControlMediaSessionAsync(
            request.SessionId, request.Command, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> SendLoopbackJsonAsync(
        BrokerRequestEnvelope envelope,
        RequestLease primaryLease,
        bool isPost)
    {
        if (!PlatformCapabilities.TryGetLoopbackPort(envelope.CapabilityId, out var port))
            throw new BrokerException("invalid_declaration", "Loopback port declaration is invalid.");
        var request = BrokerJson.ParsePayload<LoopbackJsonRequest>(envelope.Payload);
        ValidateLoopbackRequest(request, isPost);

        await _loopbackGate.WaitAsync(primaryLease.Token).ConfigureAwait(false);
        try
        {
            if (request.BearerSecretSlot is null)
            {
                return BrokerJson.ToElement(ValidateLoopbackResponse(
                    await _backend.SendLoopbackJsonAsync(
                        _identity, port, isPost, request, primaryLease.Token)
                        .ConfigureAwait(false)));
            }

            var secretCapability = await AuthorizeAsync(
                PlatformCapabilities.PrivateSecretsV1,
                PlatformCapabilities.PrivateSecretMetadata,
                primaryLease.Token).ConfigureAwait(false);
            var secretLeaseRequest = envelope with
            {
                CapabilityId = PlatformCapabilities.PrivateSecretsV1,
                Operation = PlatformCapabilities.PrivateSecretMetadata,
                Payload = envelope.Payload,
                GestureInputSequence = null,
                GestureSnapshotSequence = null,
            };
            using var secretLease = CreateRequestLease(
                secretCapability, secretLeaseRequest, primaryLease.Token);
            try
            {
                return BrokerJson.ToElement(ValidateLoopbackResponse(
                    await _backend.SendLoopbackJsonAsync(
                        _identity, port, isPost, request, secretLease.Token)
                        .ConfigureAwait(false)));
            }
            catch (OperationCanceledException exception)
                when (secretLease.CancellationCode is { } code)
            {
                throw new BrokerException(code,
                    code == "capability_revoked"
                        ? "Private secret permission was revoked while the request was running."
                        : "Private secret access became unavailable in this lifecycle.",
                    exception);
            }
        }
        finally
        {
            _loopbackGate.Release();
        }
    }

    private async Task<JsonElement> GetPrivateSecretExistsAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<PrivateSecretSlotRequest>(payload);
        ValidatePrivateSecretSlot(request.Slot);
        var metadata = ValidatePrivateSecretMetadata(
            await _backend.GetPrivateSecretMetadataAsync(
                _identity, request.Slot, cancellationToken).ConfigureAwait(false));
        return BrokerJson.ToElement(new PrivateSecretExistsSummary(metadata.Exists));
    }

    private async Task<JsonElement> GetPrivateSecretMetadataAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<PrivateSecretSlotRequest>(payload);
        ValidatePrivateSecretSlot(request.Slot);
        return BrokerJson.ToElement(ValidatePrivateSecretMetadata(
            await _backend.GetPrivateSecretMetadataAsync(
                _identity, request.Slot, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<JsonElement> SavePrivateSecretAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<SavePrivateSecretRequest>(payload);
        ValidatePrivateSecretSlot(request.Slot);
        ValidatePrivateSecret(request.Secret);
        await _backend.SavePrivateSecretAsync(
            _identity, request.Slot, request.Secret, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> DeletePrivateSecretAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<PrivateSecretSlotRequest>(payload);
        ValidatePrivateSecretSlot(request.Slot);
        await _backend.DeletePrivateSecretAsync(
            _identity, request.Slot, cancellationToken).ConfigureAwait(false);
        return BrokerJson.ToElement(new { acknowledged = true });
    }

    private async Task<JsonElement> ReadPrivateStateAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        DemandEmptyPayload(payload);
        return BrokerJson.ToElement(ValidatePrivateStateSnapshot(
            await _backend.ReadPrivateStateAsync(_identity, cancellationToken)
                .ConfigureAwait(false)));
    }

    private async Task<JsonElement> WritePrivateStateAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<WritePrivateStateRequest>(payload);
        ValidateExpectedRevision(request.ExpectedRevision);
        _ = PrivateStateJsonCodec.DecodeCanonicalBase64(
            request.CanonicalJsonBase64, "invalid_payload");
        return BrokerJson.ToElement(ValidatePrivateStateMutation(
            await _backend.WritePrivateStateAsync(_identity, request, cancellationToken)
                .ConfigureAwait(false)));
    }

    private async Task<JsonElement> ClearPrivateStateAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ClearPrivateStateRequest>(payload);
        ValidateExpectedRevision(request.ExpectedRevision);
        return BrokerJson.ToElement(ValidatePrivateStateMutation(
            await _backend.ClearPrivateStateAsync(_identity, request, cancellationToken)
                .ConfigureAwait(false)));
    }

    private static PrivateStateSnapshotSummary ValidatePrivateStateSnapshot(
        PrivateStateSnapshotSummary? snapshot)
    {
        if (snapshot is null || snapshot.Revision < 0 ||
            snapshot.Exists != (snapshot.CanonicalJsonBase64 is not null))
            throw new BrokerException("invalid_backend_data", "Private state result is invalid.");
        if (snapshot.CanonicalJsonBase64 is not null)
            _ = PrivateStateJsonCodec.DecodeCanonicalBase64(
                snapshot.CanonicalJsonBase64, "invalid_backend_data");
        return snapshot;
    }

    private static PrivateStateMutationSummary ValidatePrivateStateMutation(
        PrivateStateMutationSummary? mutation)
    {
        if (mutation is null || mutation.Revision <= 0)
            throw new BrokerException("invalid_backend_data", "Private state revision is invalid.");
        return mutation;
    }

    private static void ValidateExpectedRevision(long? revision)
    {
        if (revision < 0)
            throw new BrokerException("invalid_payload", "Expected state revision is invalid.");
    }

    private static void ValidateLoopbackRequest(LoopbackJsonRequest? request, bool isPost)
    {
        if (request is null || request.Headers is null ||
            request.TimeoutMilliseconds is < 1 or
                > CommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds ||
            !IsSafeOriginFormPath(request.Path) ||
            request.BearerSecretSlot is { } slot && !IsValidPrivateSecretSlot(slot) ||
            request.InvalidateBearerSecretOnUnauthorized && request.BearerSecretSlot is null ||
            isPost != (request.JsonBody is not null))
            throw new BrokerException("invalid_payload", "Loopback HTTP request is invalid.");
        ValidateLoopbackHeaders(request.Headers, "invalid_payload", requestHeaders: true);
        if (request.JsonBody is { } json)
            ValidateJsonBody(json, CommunityPlatformLimits.MaximumLoopbackRequestBodyUtf8Bytes,
                "invalid_payload");
    }

    private static LoopbackJsonResponse ValidateLoopbackResponse(LoopbackJsonResponse? response)
    {
        if (response is null || response.StatusCode is < 100 or > 599 ||
            response.Headers is null)
            throw new BrokerException("invalid_backend_data", "Loopback HTTP response is invalid.");
        var canonicalJsonBody = ValidateAndCanonicalizeJsonBody(response.JsonBody,
            CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes,
            "invalid_backend_data", allowEmpty: response.StatusCode is 204 or 205);
        ValidateLoopbackHeaders(response.Headers, "invalid_backend_data", requestHeaders: false);
        return response with
        {
            JsonBody = canonicalJsonBody,
            Headers = Array.AsReadOnly(response.Headers.ToArray()),
        };
    }

    private static void ValidateLoopbackHeaders(
        IReadOnlyList<LoopbackHttpHeader> headers,
        string errorCode,
        bool requestHeaders)
    {
        if (headers.Count > CommunityPlatformLimits.MaximumLoopbackHeaderCount)
            throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characters = 0;
        foreach (var header in headers)
        {
            if (header is null || string.IsNullOrEmpty(header.Name) ||
                header.Name.Length > CommunityPlatformLimits.MaximumLoopbackHeaderNameCharacters ||
                header.Value is null ||
                header.Value.Length > CommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters ||
                !header.Name.All(IsHttpTokenCharacter) ||
                header.Value.Any(character => character is '\r' or '\n' || char.IsControl(character)) ||
                !names.Add(header.Name) ||
                requestHeaders && IsRestrictedRequestHeader(header.Name) ||
                !requestHeaders && !IsProjectedResponseHeader(header.Name))
                throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
            characters += header.Name.Length + header.Value.Length;
            if (characters > CommunityPlatformLimits.MaximumLoopbackHeaderCharacters)
                throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
        }
    }

    private static bool IsSafeOriginFormPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path.Length <= CommunityPlatformLimits.MaximumLoopbackPathCharacters &&
        path[0] == '/' &&
        !path.StartsWith("//", StringComparison.Ordinal) &&
        !path.Contains('\\') &&
        !path.Contains('#') &&
        !path.Any(character => character is '\r' or '\n' || char.IsControl(character)) &&
        Uri.TryCreate(path, UriKind.Relative, out _);

    private static bool IsRestrictedRequestHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectedResponseHeader(string name) =>
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ETag", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or
            '^' or '_' or '`' or '|' or '~';

    private static void ValidateJsonBody(
        string? json, int maximumUtf8Bytes, string errorCode, bool allowEmpty = false)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maximumUtf8Bytes ||
            !allowEmpty && json.Length == 0)
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
        if (allowEmpty && json.Length == 0) return;
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
        }
        catch (JsonException exception)
        {
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.", exception);
        }
    }

    private static string ValidateAndCanonicalizeJsonBody(
        string? json, int maximumUtf8Bytes, string errorCode, bool allowEmpty = false)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maximumUtf8Bytes ||
            !allowEmpty && json.Length == 0)
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
        if (allowEmpty && json.Length == 0) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
            // Broker envelopes reject raw control characters inside strings.
            // Compact serialization removes insignificant pretty-print
            // whitespace while preserving escaped controls in JSON values.
            var canonical = JsonSerializer.Serialize(document.RootElement, BrokerJson.StrictOptions);
            if (Encoding.UTF8.GetByteCount(canonical) > maximumUtf8Bytes)
                throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
            return canonical;
        }
        catch (JsonException exception)
        {
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.", exception);
        }
    }

    private static void ValidatePrivateSecretSlot(string? slot)
    {
        if (!IsValidPrivateSecretSlot(slot))
            throw new BrokerException("invalid_payload", "Private secret slot is invalid.");
    }

    private static bool IsValidPrivateSecretSlot(string? slot) =>
        !string.IsNullOrEmpty(slot) &&
        slot.Length <= CommunityPlatformLimits.MaximumPrivateSecretSlotCharacters &&
        slot.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');

    private static void ValidatePrivateSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Contains('\0'))
            throw new BrokerException("invalid_payload", "Private secret is invalid.");
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(secret) >
                CommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes)
                throw new BrokerException("invalid_payload", "Private secret is invalid.");
        }
        catch (EncoderFallbackException exception)
        {
            throw new BrokerException("invalid_payload", "Private secret is invalid.", exception);
        }
    }

    private static PrivateSecretMetadataSummary ValidatePrivateSecretMetadata(
        PrivateSecretMetadataSummary? metadata)
    {
        if (metadata is null || metadata.Exists != metadata.LastWrittenUnixMilliseconds.HasValue ||
            metadata.LastWrittenUnixMilliseconds is < 0)
            throw new BrokerException("invalid_backend_data", "Private secret metadata is invalid.");
        return metadata;
    }

    private static IReadOnlyList<MediaSessionSummary> ValidateMediaSessions(
        bool _, IReadOnlyList<MediaSessionSummary>? sessions) => ValidateMediaSessions(sessions);

    private static IReadOnlyList<MediaSessionSummary> ValidateMediaSessions(
        IReadOnlyList<MediaSessionSummary>? sessions)
    {
        if (sessions is null || sessions.Count > 32)
            throw new BrokerException("invalid_backend_data", "Media session result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var currentCount = 0;
        foreach (var session in sessions)
        {
            if (session is null || !Enum.IsDefined(session.PlaybackStatus) ||
                !double.IsFinite(session.PlaybackRate) || session.PlaybackRate is < 0 or > 16 ||
                session.PositionMilliseconds < 0 || session.DurationMilliseconds < 0 ||
                session.PositionMilliseconds > session.DurationMilliseconds ||
                session.DurationMilliseconds > TimeSpan.FromDays(7).TotalMilliseconds ||
                session.CapturedAtUnixMilliseconds < 0 ||
                session.IsCurrent && ++currentCount > 1)
                throw new BrokerException("invalid_backend_data", "Media session entry is inconsistent.");
            ContractValidation.OpaqueId(session.SessionId, "invalid_backend_data");
            ContractValidation.DisplayName(session.AppName);
            ContractValidation.DisplayName(session.Title);
            ContractValidation.DisplayName(session.Artist);
            if (!ids.Add(session.SessionId))
                throw new BrokerException("invalid_backend_data", "Media session IDs are duplicated.");
        }
        return sessions.ToArray();
    }

    private static WifiRadioSummary ValidateWifiRadio(bool _, WifiRadioSummary? radio)
    {
        if (radio is null || !Enum.IsDefined(radio.State))
            throw new BrokerException("invalid_backend_data", "Wi-Fi radio state is invalid.");
        if (radio.CanControl && radio.State is WifiRadioState.HardwareDisabled or
                WifiRadioState.NoAdapter or WifiRadioState.Unavailable)
            throw new BrokerException("invalid_backend_data", "Wi-Fi radio control availability is invalid.");
        return radio;
    }

    private static BluetoothSummary ValidateBluetooth(bool _, BluetoothSummary? snapshot) =>
        ValidateBluetooth(snapshot);

    private static BluetoothSummary ValidateBluetooth(BluetoothSummary? snapshot)
    {
        if (snapshot is null || !Enum.IsDefined(snapshot.RadioState) ||
            !Enum.IsDefined(snapshot.DiscoveryState) || snapshot.Devices is null ||
            snapshot.Devices.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Bluetooth result is invalid.");
        if (snapshot.CanControlRadio && snapshot.RadioState is BluetoothRadioState.HardwareDisabled or
                BluetoothRadioState.NoAdapter or BluetoothRadioState.Unavailable)
            throw new BrokerException(
                "invalid_backend_data", "Bluetooth radio control availability is invalid.");
        if (snapshot.DiscoveryState != BluetoothDiscoveryState.Ready && snapshot.Devices.Count != 0)
            throw new BrokerException(
                "invalid_backend_data", "Bluetooth discovery state contains stale devices.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in snapshot.Devices)
        {
            if (device is null)
                throw new BrokerException("invalid_backend_data", "Bluetooth device result is invalid.");
            ContractValidation.OpaqueId(device.DeviceId, "invalid_backend_data");
            ContractValidation.DisplayName(device.DisplayName);
            if (!ids.Add(device.DeviceId) || device.IsConnected && !device.IsPaired ||
                !device.IsPaired && !device.IsPresent)
                throw new BrokerException(
                    "invalid_backend_data", "Bluetooth device state is inconsistent.");
        }
        return snapshot with { Devices = snapshot.Devices.ToArray() };
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

    private static AudioOutputSummary ValidateAudioOutput(AudioOutputSummary? output)
        => ValidateAudioOutput(true, output);

    private static AudioOutputSummary ValidateAudioOutput(bool _, AudioOutputSummary? output)
    {
        if (output is null || !double.IsFinite(output.Volume) || output.Volume is < 0 or > 1)
            throw new BrokerException("invalid_backend_data", "Audio output result is invalid.");
        return output;
    }

    private static AudioOutputChangedEvent ValidateAudioOutputEvent(AudioOutputChangedEvent change)
    {
        if (change.IsAvailable != (change.Output is not null))
            throw new BrokerException(
                "invalid_backend_data", "Audio output availability is inconsistent.");
        return new AudioOutputChangedEvent(
            change.Output is null ? null : ValidateAudioOutput(change.Output),
            change.IsAvailable);
    }

    private static IReadOnlyList<AudioDeviceSummary> ValidateAudioDevices(
        bool _, IReadOnlyList<AudioDeviceSummary>? devices)
    {
        if (devices is null || devices.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Audio device result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var defaults = new HashSet<AudioDeviceDirection>();
        foreach (var device in devices)
        {
            if (device is null || !Enum.IsDefined(device.Direction))
                throw new BrokerException("invalid_backend_data", "Audio device result is invalid.");
            ContractValidation.OpaqueId(device.DeviceId, "invalid_backend_data");
            ContractValidation.DisplayName(device.DisplayName);
            if (!ids.Add(device.DeviceId) || device.IsDefault && !defaults.Add(device.Direction))
                throw new BrokerException("invalid_backend_data", "Audio device result is inconsistent.");
        }
        return devices.ToArray();
    }

    private static AudioInputSummary ValidateAudioInput(bool _, AudioInputSummary? input) =>
        ValidateAudioInput(input);

    private static AudioInputSummary ValidateAudioInput(AudioInputSummary? input)
    {
        if (input is null || !double.IsFinite(input.Volume) || input.Volume is < 0 or > 1)
            throw new BrokerException("invalid_backend_data", "Audio input result is invalid.");
        return input;
    }

    private static AudioDevicesChangedEvent ValidateAudioDevicesEvent(AudioDevicesChangedEvent change)
    {
        if (!change.IsAvailable && change.Devices.Count != 0)
            throw new BrokerException("invalid_backend_data", "Audio device availability is inconsistent.");
        return new AudioDevicesChangedEvent(ValidateAudioDevices(true, change.Devices), change.IsAvailable);
    }

    private static AudioInputChangedEvent ValidateAudioInputEvent(AudioInputChangedEvent change)
    {
        if (change.IsAvailable != (change.Input is not null))
            throw new BrokerException("invalid_backend_data", "Audio input availability is inconsistent.");
        return new AudioInputChangedEvent(
            change.Input is null ? null : ValidateAudioInput(change.Input), change.IsAvailable);
    }

    private static NetworkStatusSummary ValidateNetworkStatus(
        NetworkStatusSummary status) => ValidateNetworkStatus(true, status);

    private static NetworkStatusSummary ValidateNetworkStatus(bool _, NetworkStatusSummary? status)
    {
        if (status is null)
            throw new BrokerException("invalid_backend_data", "Network status is invalid.");
        if (!Enum.IsDefined(status.Connectivity))
            throw new BrokerException("invalid_backend_data", "Network connectivity is invalid.");
        if (!Enum.IsDefined(status.Transport) ||
            !Enum.IsDefined(status.WirelessAvailability) ||
            !Enum.IsDefined(status.DetailsAccess) ||
            !Enum.IsDefined(status.ConnectionAttemptState))
            throw new BrokerException("invalid_backend_data", "Network status state is invalid.");
        if ((status.ConnectionAttemptState == NetworkConnectionAttemptState.None) !=
            (status.AttemptProfileId is null))
            throw new BrokerException("invalid_backend_data", "Network attempt state is inconsistent.");
        if (status.AttemptProfileId is not null)
            ContractValidation.OpaqueId(status.AttemptProfileId, "invalid_backend_data");
        if (status.ActiveProfileId is not null) ContractValidation.OpaqueId(
            status.ActiveProfileId, "invalid_backend_data");
        if (status.ActiveProfileName is not null) ContractValidation.DisplayName(status.ActiveProfileName);
        if ((status.ActiveProfileId is null) != (status.ActiveProfileName is null))
            throw new BrokerException("invalid_backend_data", "Network profile summary is incomplete.");
        if ((status.Transport != NetworkTransportKind.Wifi ||
             status.DetailsAccess != NetworkDetailsAccess.Available) &&
            (status.ActiveProfileId is not null || status.ActiveProfileName is not null ||
             status.SignalPercent is not null))
            throw new BrokerException("invalid_backend_data", "Network details state is inconsistent.");
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

    private static AvailableWifiNetworksSummary ValidateAvailableWifiNetworks(
        bool _, AvailableWifiNetworksSummary? snapshot) => ValidateAvailableWifiNetworks(snapshot);

    private static AvailableWifiNetworksSummary ValidateAvailableWifiNetworks(
        AvailableWifiNetworksSummary? snapshot)
    {
        if (snapshot is null || !Enum.IsDefined(snapshot.ScanState) || snapshot.Networks is null)
            throw new BrokerException("invalid_backend_data", "Available Wi-Fi result is invalid.");
        if (snapshot.Networks.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Too many available Wi-Fi networks.");
        if (snapshot.ScanState != WifiScanState.Ready && snapshot.Networks.Count != 0)
            throw new BrokerException(
                "invalid_backend_data", "Available Wi-Fi state contains stale networks.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var network in snapshot.Networks)
        {
            if (network is null || !Enum.IsDefined(network.Security))
                throw new BrokerException("invalid_backend_data", "Available Wi-Fi entry is invalid.");
            ContractValidation.OpaqueId(network.NetworkId, "invalid_backend_data");
            ContractValidation.DisplayName(network.DisplayName);
            ContractValidation.Percent(network.SignalPercent);
            if (network.SignalPercent is < 0 or > 100 ||
                network.Security == WifiSecurityKind.Open && network.CredentialRequired ||
                network.IsConnected && network.CredentialRequired)
                throw new BrokerException("invalid_backend_data", "Available Wi-Fi entry is inconsistent.");
            if (!ids.Add(network.NetworkId))
                throw new BrokerException("invalid_backend_data", "Available Wi-Fi IDs are duplicated.");
        }
        return snapshot with { Networks = snapshot.Networks.ToArray() };
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
                        ValidateAudioSessions(audio.Sessions), audio.IsAvailable)),
                PlatformCapabilities.AudioOutputChanged when
                    platformEvent.Payload is AudioOutputChangedEvent output =>
                    BrokerJson.ToElement(ValidateAudioOutputEvent(output)),
                PlatformCapabilities.AudioDevicesChanged when
                    platformEvent.Payload is AudioDevicesChangedEvent devices =>
                    BrokerJson.ToElement(ValidateAudioDevicesEvent(devices)),
                PlatformCapabilities.AudioInputChanged when
                    platformEvent.Payload is AudioInputChangedEvent input =>
                    BrokerJson.ToElement(ValidateAudioInputEvent(input)),
                PlatformCapabilities.NetworkStatusChanged when
                    platformEvent.Payload is NetworkStatusChangedEvent network =>
                    BrokerJson.ToElement(new NetworkStatusChangedEvent(
                        ValidateNetworkStatus(network.Status))),
                PlatformCapabilities.NetworkAvailableWifiChanged when
                    platformEvent.Payload is AvailableWifiNetworksChangedEvent wifi =>
                    BrokerJson.ToElement(new AvailableWifiNetworksChangedEvent(
                        ValidateAvailableWifiNetworks(wifi.Snapshot))),
                PlatformCapabilities.NetworkWifiRadioChanged when
                    platformEvent.Payload is WifiRadioChangedEvent radio =>
                    BrokerJson.ToElement(new WifiRadioChangedEvent(
                        ValidateWifiRadio(true, radio.Radio))),
                PlatformCapabilities.NetworkBluetoothChanged when
                    platformEvent.Payload is BluetoothChangedEvent bluetooth =>
                    BrokerJson.ToElement(new BluetoothChangedEvent(
                        ValidateBluetooth(bluetooth.Snapshot))),
                PlatformCapabilities.RecentActivitiesChanged when
                    platformEvent.Payload is RecentActivitiesChangedEvent activities =>
                    BrokerJson.ToElement(new RecentActivitiesChangedEvent(
                        ValidateRecentActivities(activities.Activities))),
                PlatformCapabilities.MediaSessionsChanged when
                    platformEvent.Payload is MediaSessionsChangedEvent media =>
                    BrokerJson.ToElement(new MediaSessionsChangedEvent(
                        ValidateMediaSessions(media.Sessions))),
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
        List<RequestLease> canceled;
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _lifecycle = BrokerLifecycleState.Destroying;
            ClearDashboardGestureAuthoritiesLocked();
            _backend.EventPublished -= OnBackendEvent;
            foreach (var subscription in _subscriptions) subscription.Revoke();
            _subscriptions.Clear();
            canceled = _requestLeases.ToList();
            foreach (var lease in canceled) lease.MarkCanceledLocked("lifecycle_denied");
        }
        foreach (var lease in canceled) lease.SignalCancellation();
        return ValueTask.CompletedTask;
    }
}
