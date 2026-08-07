using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

/// <summary>
/// Event-driven Windows network provider. A dedicated MTA thread owns all native resources;
/// native callback threads only enqueue a bounded, coalesced invalidation.
/// </summary>
public sealed class WindowsNetworkPlatformBackend : INetworkPlatformBrokerBackend, IAsyncDisposable
{
    private const int MaximumProfiles = 128;
    private const int MaximumNativeKeyLength = 2048;
    private const int MaximumDisplayNameLength = 160;
    private static readonly TimeSpan DefaultConnectionAttemptTimeout = TimeSpan.FromSeconds(30);
    private readonly IWindowsNetworkNativeAdapterFactory _factory;
    private readonly TimeSpan _connectionAttemptTimeout;
    private readonly BlockingCollection<NetworkCommand> _commands = new(128);
    private readonly Channel<NetworkStatusSummary> _events = Channel.CreateBounded<NetworkStatusSummary>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _threadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateGate = new();
    private readonly object _startGate = new();
    private readonly Dictionary<string, string> _opaqueIds = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<NativeNetworkConnectionOutcome> _nativeOutcomes = new();
    private NetworkStatusSummary _status = EmptyStatus;
    private IReadOnlyList<SavedNetworkProfileSummary> _profiles = [];
    private IReadOnlyDictionary<string, string> _nativeKeysByOpaqueId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private Thread? _ownerThread;
    private Task _eventPump = Task.CompletedTask;
    private long _nextGeneration;
    private long _activeGeneration;
    private int _started;
    private int _refreshQueued;
    private int _disposeStarted;
    private volatile bool _degraded;
    private volatile bool _wirelessAccessRestricted;
    private volatile bool _ownerUnavailable;
    private volatile bool _snapshotUnavailable;
    private string? _pendingProfileId;
    private string? _pendingNativeKey;
    private NetworkConnectionAttemptState _attemptState;
    private Timer? _connectionAttemptTimer;
    private long _connectionAttemptGeneration;

    private static NetworkStatusSummary EmptyStatus { get; } =
        new(
            NetworkConnectivity.None,
            NetworkTransportKind.None,
            NetworkWirelessAvailability.NoAdapter,
            NetworkDetailsAccess.Unavailable,
            NetworkConnectionAttemptState.None,
            null,
            null,
            null,
            null);

    private static NetworkStatusSummary UnavailableStatus { get; } =
        new(
            NetworkConnectivity.None,
            NetworkTransportKind.None,
            NetworkWirelessAvailability.ServiceUnavailable,
            NetworkDetailsAccess.Unavailable,
            NetworkConnectionAttemptState.None,
            null,
            null,
            null,
            null);

    public WindowsNetworkPlatformBackend(
        IWindowsNetworkNativeAdapterFactory? factory = null,
        TimeSpan? connectionAttemptTimeout = null)
    {
        _factory = factory ?? new WindowsNetworkNativeAdapterFactory();
        _connectionAttemptTimeout = connectionAttemptTimeout ?? DefaultConnectionAttemptTimeout;
        if (_connectionAttemptTimeout <= TimeSpan.Zero ||
            _connectionAttemptTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(
                nameof(connectionAttemptTimeout), "Connection timeout must be greater than zero and at most five minutes.");
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    /// <summary>True when the provider cannot currently obtain a trustworthy snapshot.</summary>
    public bool IsDegraded => _degraded;

    /// <summary>
    /// True when Windows precise-location privacy prevents reading the active Wi-Fi profile
    /// or signal. Ethernet/local/internet status and saved-profile enumeration may still work.
    /// </summary>
    public bool IsWirelessAccessRestricted => _wirelessAccessRestricted;

    /// <summary>Construction and event subscription do not activate any Windows network API.</summary>
    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public async Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken)
    {
        await EnsureReadableAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate) return _status;
    }

    public async Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(
        CancellationToken cancellationToken)
    {
        await EnsureReadableAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate) return _profiles.ToArray();
    }

    private async Task EnsureReadableAsync(CancellationToken cancellationToken)
    {
        var explicitRetry = IsStarted;
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (explicitRetry && _degraded && !_ownerUnavailable)
            await EnqueueRetryRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (_snapshotUnavailable)
            throw new BrokerException(
                "platform_unavailable", "Windows networking is temporarily unavailable.");
    }

    private async Task EnqueueRetryRefreshAsync(CancellationToken cancellationToken)
    {
        var command = new RetryRefreshCommand(cancellationToken);
        try
        {
            if (!_commands.TryAdd(command)) return;
        }
        catch (InvalidOperationException)
        {
            ThrowIfDisposed();
            return;
        }
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SwitchSavedNetworkProfileAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        // The broker validates the public contract too, but the trusted provider defends its seam.
        if (string.IsNullOrEmpty(profileId) || profileId.Length > 128 ||
            !profileId.StartsWith("network_", StringComparison.Ordinal) ||
            profileId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new BrokerException("invalid_payload", "The saved network identifier is invalid.");

        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows networking is temporarily unavailable.");

        var command = new ConnectCommand(profileId, cancellationToken);
        try
        {
            if (!_commands.TryAdd(command))
                throw new BrokerException("provider_busy", "The network provider is busy.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend), exception);
        }
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OwnerThreadMain()
    {
        IWindowsNetworkNativeAdapter? adapter = null;
        var apartmentInitialized = false;
        try
        {
            apartmentInitialized = NetworkInterop.InitializeMta();
            var generation = Interlocked.Increment(ref _nextGeneration);
            adapter = _factory.Create(generation);
            Volatile.Write(ref _activeGeneration, adapter.Generation);
            adapter.StateChanged += OnNativeStateChanged;
            Refresh(adapter, publish: false);
            _ready.TrySetResult();

            foreach (var command in _commands.GetConsumingEnumerable())
            {
                switch (command)
                {
                    case RefreshCommand:
                        Interlocked.Exchange(ref _refreshQueued, 0);
                        Refresh(adapter, publish: true);
                        break;
                    case RetryRefreshCommand retry:
                        if (retry.CancellationToken.IsCancellationRequested)
                            retry.Completion.TrySetCanceled(retry.CancellationToken);
                        else
                        {
                            Refresh(adapter, publish: true);
                            retry.Completion.TrySetResult();
                        }
                        break;
                    case ConnectCommand connect:
                        ExecuteConnect(adapter, connect);
                        break;
                }
            }
        }
        catch (Exception)
        {
            _ownerUnavailable = true;
            SetDegradedState(publish: _ready.Task.IsCompleted);
            _ready.TrySetResult();
        }
        finally
        {
            Volatile.Write(ref _activeGeneration, 0);
            try { _commands.CompleteAdding(); }
            catch (ObjectDisposedException) { }
            if (adapter is not null)
            {
                adapter.StateChanged -= OnNativeStateChanged;
                try { adapter.Dispose(); }
                catch { }
            }
            if (apartmentInitialized) NetworkInterop.Uninitialize();
            while (_commands.TryTake(out var pending))
            {
                switch (pending)
                {
                    case ConnectCommand connect:
                        connect.Completion.TrySetException(
                            new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend)));
                        break;
                    case RetryRefreshCommand retry:
                        retry.Completion.TrySetResult();
                        break;
                }
            }
            _events.Writer.TryComplete();
            lock (_stateGate) CancelConnectionAttemptTimerLocked();
            _ready.TrySetResult();
            _threadExited.TrySetResult();
        }
    }

    private void EnsureStarted()
    {
        if (Volatile.Read(ref _started) != 0) return;
        lock (_startGate)
        {
            ThrowIfDisposed();
            if (_started != 0) return;
            _eventPump = DispatchEventsAsync();
            _ownerThread = new Thread(OwnerThreadMain)
            {
                IsBackground = true,
                Name = "GameBarAlternative.Network.MTA",
            };
            Volatile.Write(ref _started, 1);
            _ownerThread.Start();
        }
    }

    private void OnNativeStateChanged(object? sender, NativeNetworkStateChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 ||
            args.Generation != Volatile.Read(ref _activeGeneration)) return;
        if (args.ConnectionOutcome is { } outcome)
        {
            _nativeOutcomes.Enqueue(outcome);
            while (_nativeOutcomes.Count > 32) _nativeOutcomes.TryDequeue(out _);
        }
        if (Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
        try
        {
            if (!_commands.TryAdd(RefreshCommand.Instance))
                Interlocked.Exchange(ref _refreshQueued, 0);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void ExecuteConnect(IWindowsNetworkNativeAdapter adapter, ConnectCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
            return;
        }

        try
        {
            string nativeKey;
            lock (_stateGate)
            {
                if (_attemptState == NetworkConnectionAttemptState.Connecting)
                    throw new BrokerException(
                        "provider_busy", "A saved network connection is already in progress.");
                if (!_nativeKeysByOpaqueId.TryGetValue(command.ProfileId, out nativeKey!))
                    throw new BrokerException(
                        "resource_not_found", "The saved network is no longer available.");
            }
            if (!adapter.TryConnectSavedProfile(nativeKey))
            {
                if (adapter.IsDegraded)
                {
                    SetDegradedState(publish: true);
                    throw new BrokerException(
                        "platform_unavailable", "Windows networking is temporarily unavailable.");
                }
                throw new BrokerException(
                    "resource_not_found", "The saved network is no longer available.");
            }
            NetworkStatusSummary connecting;
            lock (_stateGate)
            {
                _pendingProfileId = command.ProfileId;
                _pendingNativeKey = nativeKey;
                _attemptState = NetworkConnectionAttemptState.Connecting;
                var attemptGeneration = ++_connectionAttemptGeneration;
                CancelConnectionAttemptTimerLocked();
                _connectionAttemptTimer = new Timer(
                    OnConnectionAttemptTimeout,
                    attemptGeneration,
                    _connectionAttemptTimeout,
                    Timeout.InfiniteTimeSpan);
                connecting = _status = _status with
                {
                    ConnectionAttemptState = NetworkConnectionAttemptState.Connecting,
                    AttemptProfileId = command.ProfileId,
                };
            }
            _events.Writer.TryWrite(connecting);
            // WlanConnect is asynchronous. ACM completion/failure refreshes the pending attempt.
            command.Completion.TrySetResult();
        }
        catch (BrokerException exception)
        {
            command.Completion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            SetDegradedState(publish: true);
            command.Completion.TrySetException(new BrokerException(
                "platform_unavailable", "Windows networking is temporarily unavailable.", exception));
        }
    }

    private void OnConnectionAttemptTimeout(object? state)
    {
        if (state is not long generation || Volatile.Read(ref _disposeStarted) != 0) return;
        NetworkStatusSummary? failed = null;
        lock (_stateGate)
        {
            if (generation != _connectionAttemptGeneration ||
                _attemptState != NetworkConnectionAttemptState.Connecting) return;
            CancelConnectionAttemptTimerLocked();
            _attemptState = NetworkConnectionAttemptState.Failed;
            failed = _status = _status with
            {
                ConnectionAttemptState = NetworkConnectionAttemptState.Failed,
                AttemptProfileId = _pendingProfileId,
            };
        }
        _events.Writer.TryWrite(failed);
    }

    private void Refresh(IWindowsNetworkNativeAdapter adapter, bool publish)
    {
        try
        {
            var snapshot = adapter.ReadSnapshot() ?? throw new InvalidOperationException();
            var seenNativeKeys = new HashSet<string>(StringComparer.Ordinal);
            var profiles = new List<SavedNetworkProfileSummary>(
                Math.Min(snapshot.SavedProfiles?.Count ?? 0, MaximumProfiles));
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            string? activeOpaqueId = null;
            var allowWirelessDetails = !snapshot.IsWirelessAccessRestricted;
            foreach (var profile in snapshot.SavedProfiles ?? [])
            {
                if (profiles.Count >= MaximumProfiles) break;
                if (!IsValidProfile(profile) || !seenNativeKeys.Add(profile.NativeProfileKey)) continue;
                if (!_opaqueIds.TryGetValue(profile.NativeProfileKey, out var opaqueId))
                {
                    opaqueId = $"network_{Guid.NewGuid():N}";
                    _opaqueIds.Add(profile.NativeProfileKey, opaqueId);
                }
                reverse.Add(opaqueId, profile.NativeProfileKey);
                var isConnected = allowWirelessDetails && (profile.IsConnected ||
                    string.Equals(snapshot.ActiveProfileNativeKey, profile.NativeProfileKey,
                        StringComparison.Ordinal));
                if (isConnected) activeOpaqueId = opaqueId;
                profiles.Add(new SavedNetworkProfileSummary(
                    opaqueId,
                    SanitizeDisplayName(profile.DisplayName, "Saved Wi-Fi network"),
                    isConnected,
                    ClampPercent(profile.SignalPercent)));
            }
            foreach (var missing in _opaqueIds.Keys.Where(key => !seenNativeKeys.Contains(key)).ToArray())
                _opaqueIds.Remove(missing);

            profiles.Sort(static (left, right) =>
            {
                var connected = right.IsConnected.CompareTo(left.IsConnected);
                return connected != 0 ? connected :
                    string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            var status = BuildStatus(snapshot, activeOpaqueId);
            status = ApplyConnectionOutcome(snapshot, status);
            bool changed;
            lock (_stateGate)
            {
                changed = _status != status || !ProfilesEqual(_profiles, profiles);
                _status = status;
                _profiles = profiles.ToArray();
                _nativeKeysByOpaqueId = reverse;
            }
            _degraded = adapter.IsDegraded;
            _snapshotUnavailable = false;
            _wirelessAccessRestricted = snapshot.IsWirelessAccessRestricted;
            if (publish && changed) _events.Writer.TryWrite(status);
        }
        catch
        {
            SetDegradedState(publish);
        }
    }

    private static NetworkStatusSummary BuildStatus(
        NativeNetworkSnapshot snapshot,
        string? activeOpaqueId)
    {
        if (!Enum.IsDefined(snapshot.Connectivity))
            throw new InvalidOperationException("Invalid native connectivity.");
        var details = snapshot.IsWirelessAccessRestricted
            ? NetworkDetailsAccess.PrivacyRestricted
            : NetworkDetailsAccess.Available;
        var wireless = snapshot.WirelessAvailability;
        var transport = snapshot.ActiveMedium switch
        {
            NativeNetworkMedium.Ethernet => NetworkTransportKind.Ethernet,
            NativeNetworkMedium.WiFi => NetworkTransportKind.Wifi,
            NativeNetworkMedium.Other => NetworkTransportKind.Other,
            _ => NetworkTransportKind.None,
        };
        return snapshot.ActiveMedium switch
        {
            NativeNetworkMedium.Ethernet => new(
                snapshot.Connectivity, transport, wireless, details,
                NetworkConnectionAttemptState.None, null, null, null, null),
            NativeNetworkMedium.WiFi => new(
                snapshot.Connectivity, transport, wireless, details,
                NetworkConnectionAttemptState.None, null,
                snapshot.IsWirelessAccessRestricted ? null : activeOpaqueId,
                snapshot.IsWirelessAccessRestricted || activeOpaqueId is null
                    ? null
                    : SanitizeDisplayName(snapshot.ActiveProfileName, "Saved Wi-Fi network"),
                snapshot.IsWirelessAccessRestricted ? null : ClampPercent(snapshot.SignalPercent)),
            _ => new(
                snapshot.Connectivity, transport, wireless, details,
                NetworkConnectionAttemptState.None, null, null, null, null),
        };
    }

    private NetworkStatusSummary ApplyConnectionOutcome(
        NativeNetworkSnapshot snapshot,
        NetworkStatusSummary status)
    {
        lock (_stateGate)
        {
            while (_nativeOutcomes.TryDequeue(out var outcome))
            {
                if (_pendingNativeKey is null ||
                    !string.Equals(outcome.NativeProfileKey, _pendingNativeKey, StringComparison.Ordinal))
                    continue;
                _attemptState = outcome.Result == NativeNetworkConnectionResult.Failed
                    ? NetworkConnectionAttemptState.Failed
                    : NetworkConnectionAttemptState.None;
                CancelConnectionAttemptTimerLocked();
                if (_attemptState == NetworkConnectionAttemptState.None)
                {
                    _pendingProfileId = null;
                    _pendingNativeKey = null;
                }
            }

            if (!snapshot.IsWirelessAccessRestricted && _pendingNativeKey is not null &&
                string.Equals(snapshot.ActiveProfileNativeKey, _pendingNativeKey, StringComparison.Ordinal))
            {
                _attemptState = NetworkConnectionAttemptState.None;
                CancelConnectionAttemptTimerLocked();
                _pendingProfileId = null;
                _pendingNativeKey = null;
            }

            if (_attemptState == NetworkConnectionAttemptState.Connecting &&
                _pendingNativeKey is not null &&
                !snapshot.SavedProfiles.Any(profile =>
                    string.Equals(profile.NativeProfileKey, _pendingNativeKey, StringComparison.Ordinal)))
            {
                _attemptState = NetworkConnectionAttemptState.Failed;
                CancelConnectionAttemptTimerLocked();
            }

            return status with
            {
                ConnectionAttemptState = _attemptState,
                AttemptProfileId = _attemptState == NetworkConnectionAttemptState.None
                    ? null
                    : _pendingProfileId,
            };
        }
    }

    private void SetDegradedState(bool publish)
    {
        bool changed;
        lock (_stateGate)
        {
            changed = _status != UnavailableStatus || _profiles.Count != 0;
            _status = UnavailableStatus;
            _profiles = [];
            _nativeKeysByOpaqueId = new Dictionary<string, string>(StringComparer.Ordinal);
            _pendingProfileId = null;
            _pendingNativeKey = null;
            _attemptState = NetworkConnectionAttemptState.None;
            ++_connectionAttemptGeneration;
            CancelConnectionAttemptTimerLocked();
        }
        _degraded = true;
        _snapshotUnavailable = true;
        _wirelessAccessRestricted = false;
        if (publish && changed) _events.Writer.TryWrite(UnavailableStatus);
    }

    private async Task DispatchEventsAsync()
    {
        await foreach (var status in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var platformEvent = new BrokerPlatformEvent(
                PlatformCapabilities.NetworkReadV1,
                PlatformCapabilities.NetworkStatusChanged,
                new NetworkStatusChangedEvent(status));
            var handlers = EventPublished;
            if (handlers is null) continue;
            foreach (EventHandler<BrokerPlatformEvent> handler in handlers.GetInvocationList())
            {
                try { handler(this, platformEvent); }
                catch { }
            }
        }
    }

    private static bool IsValidProfile(NativeSavedNetworkProfile profile) =>
        !string.IsNullOrEmpty(profile.NativeProfileKey) &&
        profile.NativeProfileKey.Length <= MaximumNativeKeyLength;

    private static int? ClampPercent(int? value) => value is null ? null : Math.Clamp(value.Value, 0, 100);

    private static string SanitizeDisplayName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (LooksLikePath(value)) return fallback;
        var builder = new StringBuilder(Math.Min(value.Length, MaximumDisplayNameLength));
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length != 0;
                continue;
            }
            var needed = rune.Utf16SequenceLength + (pendingSpace ? 1 : 0);
            if (builder.Length + needed > MaximumDisplayNameLength) break;
            if (pendingSpace) builder.Append(' ');
            builder.Append(rune.ToString());
            pendingSpace = false;
        }
        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static bool LooksLikePath(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value[0] == '/' ||
        value.Contains(":\\", StringComparison.Ordinal) ||
        value.Contains(":/", StringComparison.Ordinal);

    private static bool ProfilesEqual(
        IReadOnlyList<SavedNetworkProfileSummary> left,
        IReadOnlyList<SavedNetworkProfileSummary> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First == pair.Second);

    private void CancelConnectionAttemptTimerLocked()
    {
        _connectionAttemptTimer?.Dispose();
        _connectionAttemptTimer = null;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend));
    }

    public async ValueTask DisposeAsync()
    {
        Task threadExited;
        Task eventPump;
        lock (_startGate)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
            _commands.CompleteAdding();
            if (_started == 0)
            {
                _events.Writer.TryComplete();
                _ready.TrySetResult();
                _threadExited.TrySetResult();
            }
            threadExited = _threadExited.Task;
            eventPump = _eventPump;
        }
        await threadExited.ConfigureAwait(false);
        await eventPump.ConfigureAwait(false);
        _commands.Dispose();
    }

    private abstract record NetworkCommand;
    private sealed record RefreshCommand : NetworkCommand
    {
        public static RefreshCommand Instance { get; } = new();
    }
    private sealed record RetryRefreshCommand(CancellationToken CancellationToken) : NetworkCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record ConnectCommand(string ProfileId, CancellationToken CancellationToken) : NetworkCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
