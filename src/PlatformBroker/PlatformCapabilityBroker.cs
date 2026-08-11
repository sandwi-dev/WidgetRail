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
/// Identity-bound broker session. Every operation rechecks its host-owned
/// authority set, capability shape, and lifecycle before reaching a backend;
/// manifest capabilities additionally require durable consent.
/// </summary>
public sealed class PlatformCapabilityBroker : IAsyncDisposable
{
    public static readonly TimeSpan MaximumDashboardGestureLifetime = TimeSpan.FromSeconds(2);
    public const int MaximumDashboardGestureAuthorities = 16;
    internal const int MaximumAppLibraryPageSize = 64;
    internal const int MaximumResolvedAppLibraryItems = 64;
    private readonly BrokerWidgetIdentity _identity;
    private readonly HashSet<string> _declaredCapabilities;
    private readonly HashSet<string> _hostGrantedCapabilities;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformBrokerBackend _backend;
    private readonly AudioCapabilityDomain _audioDomain;
    private readonly NetworkCapabilityDomain _networkDomain;
    private readonly AppLibraryCapabilityDomain _appLibraryDomain;
    private readonly MediaSpotifyCapabilityDomain _mediaSpotifyDomain;
    private readonly PrivateSecretCapabilityDomain _privateSecretDomain;
    private readonly PrivateStateCapabilityDomain _privateStateDomain;
    private readonly object _gate = new();
    private readonly List<BrokerEventSubscription> _subscriptions = [];
    private readonly HashSet<RequestLease> _requestLeases = [];
    private readonly HashSet<string> _revokedCapabilities = new(StringComparer.Ordinal);
    private readonly Dictionary<DashboardGestureKey, DashboardGestureAuthority>
        _dashboardGestureAuthorities = [];
    private readonly SemaphoreSlim _loopbackGate = new(2, 2);
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
        IAppLibrarySavedIdIssuer appLibrarySavedIdIssuer,
        AppLibraryArtworkRegistry.AppLibraryArtworkSession? appLibraryArtwork = null)
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
        ArgumentNullException.ThrowIfNull(appLibrarySavedIdIssuer);
        _audioDomain = new AudioCapabilityDomain(_backend);
        _networkDomain = new NetworkCapabilityDomain(_backend);
        _appLibraryDomain = new AppLibraryCapabilityDomain(
            _backend, _identity, appLibrarySavedIdIssuer, appLibraryArtwork);
        _mediaSpotifyDomain = new MediaSpotifyCapabilityDomain(
            _backend, _identity, AuthorizeSpotifyScopesAsync);
        _privateSecretDomain = new PrivateSecretCapabilityDomain(_backend, _identity);
        _privateStateDomain = new PrivateStateCapabilityDomain(_backend, _identity);
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
        // Authorization, lease admission, lifecycle, revocation, and gesture
        // authority have already linearized in this broker. Domain handlers
        // receive only that admitted request and its exact lease token.
        lease.ThrowIfBrokerCanceled();
        var requestToken = lease.Token;
        return BrokerCapabilityDomains.Resolve(request.CapabilityId) switch
        {
            BrokerCapabilityDomain.Audio =>
                await _audioDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            BrokerCapabilityDomain.Network =>
                await _networkDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            BrokerCapabilityDomain.AppLibrary =>
                await _appLibraryDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            BrokerCapabilityDomain.MediaSpotify =>
                await _mediaSpotifyDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            BrokerCapabilityDomain.Loopback =>
                await ExecuteLoopbackAsync(request, lease).ConfigureAwait(false),
            BrokerCapabilityDomain.PrivateSecrets =>
                await _privateSecretDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            BrokerCapabilityDomain.PrivateState =>
                await _privateStateDomain.ExecuteAsync(
                    request.Operation, request.Payload, requestToken).ConfigureAwait(false),
            _ => throw new BrokerException(
                "unsupported_operation", "Broker operation is unsupported."),
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
        lease.AllowsLifecycleContinuation && lifecycle is
            BrokerLifecycleState.Background or
            BrokerLifecycleState.Visible or
            BrokerLifecycleState.Interactive
            ? true
            : lease.AllowsBackground
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
                capability.AllowsInFlightContinuationForOperation(request.Operation),
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
            bool allowsLifecycleContinuation,
            bool dashboardGestureAuthorized,
            CancellationToken callerCancellation)
        {
            _owner = owner;
            CapabilityId = capabilityId;
            Kind = kind;
            AllowsBackground = allowsBackground;
            AllowsLifecycleContinuation = allowsLifecycleContinuation;
            DashboardGestureAuthorized = dashboardGestureAuthorized;
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellation, _brokerCancellation.Token);
        }

        internal string CapabilityId { get; }
        internal BrokerCapabilityKind Kind { get; }
        internal bool AllowsBackground { get; }
        internal bool AllowsLifecycleContinuation { get; }
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

    private async Task AuthorizeSpotifyScopesAsync(
        IReadOnlyList<SpotifyAuthorizationScope> scopes,
        CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            var capabilityId = scope switch
            {
                SpotifyAuthorizationScope.PlaybackStateRead =>
                    PlatformCapabilities.SpotifyPlaybackReadV1,
                SpotifyAuthorizationScope.PlaybackStateControl =>
                    PlatformCapabilities.SpotifyPlaybackControlV1,
                SpotifyAuthorizationScope.LocalPlayback =>
                    PlatformCapabilities.SpotifyLocalPlaybackV1,
                SpotifyAuthorizationScope.PlaylistsRead =>
                    PlatformCapabilities.SpotifyPlaylistsReadV1,
                _ => throw new BrokerException(
                    "invalid_payload", "Spotify scope is invalid."),
            };
            await AuthorizeAsync(capabilityId, operation: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<JsonElement> ExecuteLoopbackAsync(
        BrokerRequestEnvelope envelope,
        RequestLease primaryLease)
    {
        var parsed = LoopbackCapabilityPolicy.Parse(
            envelope.CapabilityId, envelope.Operation, envelope.Payload);

        await _loopbackGate.WaitAsync(primaryLease.Token).ConfigureAwait(false);
        try
        {
            if (parsed.Request.BearerSecretSlot is null)
            {
                return BrokerJson.ToElement(LoopbackCapabilityPolicy.ValidateResponse(
                    await _backend.SendLoopbackJsonAsync(
                        _identity, parsed.Port, parsed.IsPost, parsed.Request,
                        primaryLease.Token)
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
                return BrokerJson.ToElement(LoopbackCapabilityPolicy.ValidateResponse(
                    await _backend.SendLoopbackJsonAsync(
                        _identity, parsed.Port, parsed.IsPost, parsed.Request,
                        secretLease.Token)
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

    private void OnBackendEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        try
        {
            if (platformEvent is null) return;
            if (!PlatformCapabilities.TryGet(platformEvent.CapabilityId, out var capability) ||
                !capability.Events.Contains(platformEvent.EventType)) return;
            var payload = BrokerCapabilityDomains.Resolve(
                platformEvent.CapabilityId) switch
            {
                BrokerCapabilityDomain.Audio =>
                    AudioCapabilityDomain.ProjectEvent(
                        platformEvent.EventType, platformEvent.Payload),
                BrokerCapabilityDomain.Network =>
                    NetworkCapabilityDomain.ProjectEvent(
                        platformEvent.EventType, platformEvent.Payload),
                BrokerCapabilityDomain.MediaSpotify =>
                    MediaSpotifyCapabilityDomain.ProjectEvent(
                        platformEvent.EventType, platformEvent.Payload),
                _ => throw new BrokerException(
                    "invalid_backend_data", "Broker event payload is invalid."),
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
        _appLibraryDomain.Dispose();
        return ValueTask.CompletedTask;
    }
}
