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
    private static readonly TimeSpan WifiScanTimeout = TimeSpan.FromSeconds(6);
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
    private readonly Channel<AvailableWifiNetworksSummary> _wifiEvents =
        Channel.CreateBounded<AvailableWifiNetworksSummary>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    private readonly Channel<WifiRadioSummary> _radioEvents =
        Channel.CreateBounded<WifiRadioSummary>(new BoundedChannelOptions(1)
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
    private AvailableWifiNetworksSummary _availableWifi =
        new(WifiScanState.NotScanned, []);
    private WifiRadioSummary _wifiRadio = new(WifiRadioState.Unavailable, false);
    private IReadOnlyDictionary<string, string> _wifiNativeKeysByOpaqueId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _wifiOpaqueIds = new(StringComparer.Ordinal);
    private long _mappedWifiScanGeneration = -1;
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
    private Timer? _wifiScanTimer;
    private long _wifiScanTimerGeneration;

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

    public async Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(
        CancellationToken cancellationToken)
    {
        await EnsureReadableAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
            return _availableWifi with { Networks = _availableWifi.Networks.ToArray() };
    }

    public async Task RequestWifiScanAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows networking is temporarily unavailable.");
        var command = new WifiScanCommand(cancellationToken);
        EnqueueCommand(command);
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectAvailableWifiNetworkAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        if (!IsValidPublicWifiId(networkId))
            throw new BrokerException("invalid_payload", "The available Wi-Fi identifier is invalid.");
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows networking is temporarily unavailable.");
        var command = new ConnectAvailableWifiCommand(networkId, cancellationToken);
        EnqueueCommand(command);
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken)
    {
        await EnsureReadableAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate) return _wifiRadio;
    }

    public async Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken)
    {
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows Wi-Fi radio control is unavailable.");
        var command = new SetWifiRadioCommand(enabled, cancellationToken);
        EnqueueCommand(command);
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnqueueCommand(NetworkCommand command)
    {
        try
        {
            if (!_commands.TryAdd(command))
                throw new BrokerException("provider_busy", "The network provider is busy.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend), exception);
        }
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
            RefreshWifiRadio(adapter, publish: false);
            _ready.TrySetResult();

            foreach (var command in _commands.GetConsumingEnumerable())
            {
                switch (command)
                {
                    case RefreshCommand:
                        Interlocked.Exchange(ref _refreshQueued, 0);
                        Refresh(adapter, publish: true);
                        RefreshAvailableWifi(adapter, publish: true);
                        RefreshWifiRadio(adapter, publish: true);
                        break;
                    case RetryRefreshCommand retry:
                        if (retry.CancellationToken.IsCancellationRequested)
                            retry.Completion.TrySetCanceled(retry.CancellationToken);
                        else
                        {
                            Refresh(adapter, publish: true);
                            RefreshAvailableWifi(adapter, publish: true);
                            RefreshWifiRadio(adapter, publish: true);
                            retry.Completion.TrySetResult();
                        }
                        break;
                    case ConnectCommand connect:
                        ExecuteConnect(adapter, connect);
                        break;
                    case WifiScanCommand scan:
                        ExecuteWifiScan(adapter, scan);
                        break;
                    case WifiScanTimeoutCommand timeout:
                        ExecuteWifiScanTimeout(timeout.Generation);
                        break;
                    case ConnectAvailableWifiCommand connectWifi:
                        ExecuteConnectAvailableWifi(adapter, connectWifi);
                        break;
                    case SetWifiRadioCommand radio:
                        ExecuteSetWifiRadio(adapter, radio);
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
                    case WifiScanCommand scan:
                        scan.Completion.TrySetException(
                            new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend)));
                        break;
                    case ConnectAvailableWifiCommand connectWifi:
                        connectWifi.Completion.TrySetException(
                            new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend)));
                        break;
                    case SetWifiRadioCommand radio:
                        radio.Completion.TrySetException(
                            new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend)));
                        break;
                }
            }
            _events.Writer.TryComplete();
            _wifiEvents.Writer.TryComplete();
            _radioEvents.Writer.TryComplete();
            lock (_stateGate)
            {
                CancelConnectionAttemptTimerLocked();
                CancelWifiScanTimerLocked();
            }
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
            _eventPump = Task.WhenAll(
                DispatchEventsAsync(), DispatchWifiEventsAsync(), DispatchRadioEventsAsync());
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

    private void ExecuteWifiScan(IWindowsNetworkNativeAdapter adapter, WifiScanCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
            return;
        }
        try
        {
            var result = adapter.TryStartWifiScan();
            if (result == NativeWifiScanStartResult.AlreadyScanning)
            {
                command.Completion.TrySetResult();
                return;
            }
            var state = result switch
            {
                NativeWifiScanStartResult.Started => WifiScanState.Scanning,
                NativeWifiScanStartResult.PreciseLocationDenied =>
                    WifiScanState.PreciseLocationDenied,
                _ => WifiScanState.Unavailable,
            };
            AvailableWifiNetworksSummary snapshot;
            lock (_stateGate)
            {
                _wifiOpaqueIds.Clear();
                _wifiNativeKeysByOpaqueId =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                _mappedWifiScanGeneration = -1;
                snapshot = _availableWifi = new AvailableWifiNetworksSummary(state, []);
                CancelWifiScanTimerLocked();
                if (state == WifiScanState.Scanning)
                {
                    var generation = ++_wifiScanTimerGeneration;
                    _wifiScanTimer = new Timer(
                        OnWifiScanTimeout,
                        generation,
                        WifiScanTimeout,
                        Timeout.InfiniteTimeSpan);
                }
            }
            _wifiEvents.Writer.TryWrite(snapshot);
            command.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            command.Completion.TrySetException(new BrokerException(
                "platform_unavailable", "Windows Wi-Fi scanning is temporarily unavailable.", exception));
        }
    }

    private void OnWifiScanTimeout(object? state)
    {
        if (state is not long generation || Volatile.Read(ref _disposeStarted) != 0) return;
        try { _commands.TryAdd(new WifiScanTimeoutCommand(generation)); }
        catch (InvalidOperationException) { }
    }

    private void ExecuteWifiScanTimeout(long generation)
    {
        AvailableWifiNetworksSummary? snapshot = null;
        lock (_stateGate)
        {
            if (generation != _wifiScanTimerGeneration ||
                _availableWifi.ScanState != WifiScanState.Scanning) return;
            CancelWifiScanTimerLocked();
            snapshot = _availableWifi = new AvailableWifiNetworksSummary(
                WifiScanState.Unavailable, []);
        }
        _wifiEvents.Writer.TryWrite(snapshot);
    }

    private void ExecuteConnectAvailableWifi(
        IWindowsNetworkNativeAdapter adapter,
        ConnectAvailableWifiCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
            return;
        }
        try
        {
            string nativeKey;
            bool alreadyConnected;
            lock (_stateGate)
            {
                if (_attemptState == NetworkConnectionAttemptState.Connecting)
                    throw new BrokerException(
                        "provider_busy", "A network connection is already in progress.");
                if (_availableWifi.ScanState != WifiScanState.Ready ||
                    !_wifiNativeKeysByOpaqueId.TryGetValue(command.NetworkId, out nativeKey!))
                    throw new BrokerException(
                        "resource_not_found", "The visible Wi-Fi network is no longer available.");
                alreadyConnected = _availableWifi.Networks.Any(network =>
                    network.NetworkId == command.NetworkId && network.IsConnected);
            }
            if (alreadyConnected)
            {
                command.Completion.TrySetResult();
                return;
            }

            var result = adapter.TryConnectAvailableWifiNetwork(nativeKey);
            switch (result)
            {
                case NativeWifiConnectStartResult.CredentialRequired:
                    throw new BrokerException(
                        "credential_required", "This Wi-Fi network requires a credential.");
                case NativeWifiConnectStartResult.UnsupportedAuthentication:
                    throw new BrokerException(
                        "unsupported_authentication", "This Wi-Fi authentication method is unsupported.");
                case NativeWifiConnectStartResult.NotFound:
                    throw new BrokerException(
                        "resource_not_found", "The visible Wi-Fi network is no longer available.");
                case NativeWifiConnectStartResult.Unavailable:
                    throw new BrokerException(
                        "platform_unavailable", "Windows Wi-Fi connection control is unavailable.");
                case NativeWifiConnectStartResult.Started:
                    break;
                default:
                    throw new BrokerException(
                        "platform_unavailable", "Windows returned an invalid Wi-Fi connection state.");
            }

            NetworkStatusSummary connecting;
            lock (_stateGate)
            {
                _pendingProfileId = command.NetworkId;
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
                    AttemptProfileId = command.NetworkId,
                };
            }
            _events.Writer.TryWrite(connecting);
            command.Completion.TrySetResult();
        }
        catch (BrokerException exception)
        {
            command.Completion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            command.Completion.TrySetException(new BrokerException(
                "platform_unavailable", "Windows Wi-Fi connection control is unavailable.", exception));
        }
    }

    private void ExecuteSetWifiRadio(IWindowsNetworkNativeAdapter adapter, SetWifiRadioCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
            return;
        }
        Exception? failure = null;
        try
        {
            var result = adapter.TrySetWifiRadio(command.Enabled);
            switch (result)
            {
                case NativeWifiRadioSetResult.NoAdapter:
                    throw new BrokerException("wifi_no_adapter", "No Wi-Fi adapter is available.");
                case NativeWifiRadioSetResult.HardwareDisabled:
                    throw new BrokerException(
                        "wifi_hardware_disabled", "Wi-Fi is disabled by a hardware switch.");
                case NativeWifiRadioSetResult.PolicyDenied:
                    throw new BrokerException(
                        "wifi_radio_policy_denied", "Windows policy denied Wi-Fi radio control.");
                case NativeWifiRadioSetResult.Unavailable:
                    throw new BrokerException(
                        "platform_unavailable", "Windows Wi-Fi radio control is unavailable.");
                case NativeWifiRadioSetResult.PartialFailure:
                    throw new BrokerException(
                        "wifi_radio_partial_failure",
                        "Windows changed only part of the Wi-Fi radio state and restoration could not be guaranteed.");
            }
        }
        catch (Exception exception)
        {
            failure = exception is BrokerException
                ? exception
                : new BrokerException(
                    "platform_unavailable", "Windows Wi-Fi radio control failed.", exception);
        }
        finally
        {
            // Once Native Wi-Fi was reached, success, failure, partial failure,
            // and caller cancellation all reconcile from Windows. Never restore
            // a pre-command cache after a non-atomic multi-PHY operation.
            Refresh(adapter, publish: true);
            RefreshWifiRadio(adapter, publish: true);
            RefreshAvailableWifi(adapter, publish: true);
        }
        if (failure is null) command.Completion.TrySetResult();
        else command.Completion.TrySetException(failure);
    }

    private void RefreshWifiRadio(IWindowsNetworkNativeAdapter adapter, bool publish)
    {
        try
        {
            var native = adapter.ReadWifiRadio();
            var state = native.State switch
            {
                NativeWifiRadioState.On => WifiRadioState.On,
                NativeWifiRadioState.Off => WifiRadioState.Off,
                NativeWifiRadioState.HardwareDisabled => WifiRadioState.HardwareDisabled,
                NativeWifiRadioState.NoAdapter => WifiRadioState.NoAdapter,
                _ => WifiRadioState.Unavailable,
            };
            var snapshot = new WifiRadioSummary(state,
                native.CanControl && state is WifiRadioState.On or WifiRadioState.Off);
            bool changed;
            lock (_stateGate)
            {
                changed = snapshot != _wifiRadio;
                _wifiRadio = snapshot;
            }
            if (publish && changed) _radioEvents.Writer.TryWrite(snapshot);
        }
        catch
        {
            var unavailable = new WifiRadioSummary(WifiRadioState.Unavailable, false);
            bool changed;
            lock (_stateGate)
            {
                changed = _wifiRadio != unavailable;
                _wifiRadio = unavailable;
            }
            if (publish && changed) _radioEvents.Writer.TryWrite(unavailable);
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

    private void RefreshAvailableWifi(IWindowsNetworkNativeAdapter adapter, bool publish)
    {
        try
        {
            var native = adapter.ReadAvailableWifiSnapshot() ?? throw new InvalidOperationException();
            var state = native.ScanState switch
            {
                NativeWifiScanState.NotScanned => WifiScanState.NotScanned,
                NativeWifiScanState.Scanning => WifiScanState.Scanning,
                NativeWifiScanState.Ready => WifiScanState.Ready,
                NativeWifiScanState.PreciseLocationDenied => WifiScanState.PreciseLocationDenied,
                _ => WifiScanState.Unavailable,
            };
            var networks = new List<AvailableWifiNetworkSummary>();
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            lock (_stateGate)
            {
                if (state == WifiScanState.Ready &&
                    native.ScanGeneration != _mappedWifiScanGeneration)
                {
                    _wifiOpaqueIds.Clear();
                    _mappedWifiScanGeneration = native.ScanGeneration;
                }
                if (state != WifiScanState.Ready)
                {
                    _wifiOpaqueIds.Clear();
                    _mappedWifiScanGeneration = -1;
                }
                foreach (var item in native.Networks ?? [])
                {
                    if (networks.Count >= MaximumProfiles ||
                        string.IsNullOrEmpty(item.NativeNetworkKey) ||
                        item.NativeNetworkKey.Length > MaximumNativeKeyLength ||
                        !Enum.IsDefined(item.Security)) continue;
                    if (!_wifiOpaqueIds.TryGetValue(item.NativeNetworkKey, out var opaqueId))
                    {
                        opaqueId = $"wifi_{Guid.NewGuid():N}";
                        _wifiOpaqueIds.Add(item.NativeNetworkKey, opaqueId);
                    }
                    if (!reverse.TryAdd(opaqueId, item.NativeNetworkKey)) continue;
                    networks.Add(new AvailableWifiNetworkSummary(
                        opaqueId,
                        SanitizeDisplayName(item.DisplayName, "Hidden network"),
                        Math.Clamp(item.SignalPercent, 0, 100),
                        item.Security,
                        item.CredentialRequired,
                        item.IsConnected,
                        item.HasSavedProfile));
                }
                networks.Sort(static (left, right) =>
                {
                    var connected = right.IsConnected.CompareTo(left.IsConnected);
                    if (connected != 0) return connected;
                    var signal = right.SignalPercent.CompareTo(left.SignalPercent);
                    return signal != 0 ? signal :
                        string.Compare(left.DisplayName, right.DisplayName,
                            StringComparison.OrdinalIgnoreCase);
                });
                var snapshot = new AvailableWifiNetworksSummary(
                    state,
                    state == WifiScanState.Ready ? networks.ToArray() : []);
                var changed = !AvailableWifiEqual(_availableWifi, snapshot);
                _availableWifi = snapshot;
                _wifiNativeKeysByOpaqueId = state == WifiScanState.Ready
                    ? reverse
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                if (state != WifiScanState.Scanning) CancelWifiScanTimerLocked();
                if (publish && changed) _wifiEvents.Writer.TryWrite(snapshot);
            }
        }
        catch
        {
            AvailableWifiNetworksSummary snapshot;
            lock (_stateGate)
            {
                _wifiOpaqueIds.Clear();
                _mappedWifiScanGeneration = -1;
                _wifiNativeKeysByOpaqueId =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                snapshot = _availableWifi = new AvailableWifiNetworksSummary(
                    WifiScanState.Unavailable, []);
                CancelWifiScanTimerLocked();
            }
            if (publish) _wifiEvents.Writer.TryWrite(snapshot);
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
            _availableWifi = new AvailableWifiNetworksSummary(WifiScanState.Unavailable, []);
            _wifiRadio = new WifiRadioSummary(WifiRadioState.Unavailable, false);
            _wifiNativeKeysByOpaqueId = new Dictionary<string, string>(StringComparer.Ordinal);
            _wifiOpaqueIds.Clear();
            _mappedWifiScanGeneration = -1;
            _pendingProfileId = null;
            _pendingNativeKey = null;
            _attemptState = NetworkConnectionAttemptState.None;
            ++_connectionAttemptGeneration;
            CancelConnectionAttemptTimerLocked();
            CancelWifiScanTimerLocked();
        }
        _degraded = true;
        _snapshotUnavailable = true;
        _wirelessAccessRestricted = false;
        if (publish && changed) _events.Writer.TryWrite(UnavailableStatus);
        if (publish) _wifiEvents.Writer.TryWrite(
            new AvailableWifiNetworksSummary(WifiScanState.Unavailable, []));
        if (publish) _radioEvents.Writer.TryWrite(
            new WifiRadioSummary(WifiRadioState.Unavailable, false));
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

    private async Task DispatchWifiEventsAsync()
    {
        await foreach (var snapshot in _wifiEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var platformEvent = new BrokerPlatformEvent(
                PlatformCapabilities.NetworkWifiReadV1,
                PlatformCapabilities.NetworkAvailableWifiChanged,
                new AvailableWifiNetworksChangedEvent(snapshot));
            var handlers = EventPublished;
            if (handlers is null) continue;
            foreach (EventHandler<BrokerPlatformEvent> handler in handlers.GetInvocationList())
            {
                try { handler(this, platformEvent); }
                catch { }
            }
        }
    }

    private async Task DispatchRadioEventsAsync()
    {
        await foreach (var radio in _radioEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var platformEvent = new BrokerPlatformEvent(
                PlatformCapabilities.NetworkWifiRadioReadV1,
                PlatformCapabilities.NetworkWifiRadioChanged,
                new WifiRadioChangedEvent(radio));
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

    private static bool AvailableWifiEqual(
        AvailableWifiNetworksSummary left,
        AvailableWifiNetworksSummary right) =>
        left.ScanState == right.ScanState &&
        left.Networks.Count == right.Networks.Count &&
        left.Networks.Zip(right.Networks).All(pair => pair.First == pair.Second);

    private static bool IsValidPublicWifiId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.StartsWith("wifi_", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private void CancelConnectionAttemptTimerLocked()
    {
        _connectionAttemptTimer?.Dispose();
        _connectionAttemptTimer = null;
    }

    private void CancelWifiScanTimerLocked()
    {
        _wifiScanTimer?.Dispose();
        _wifiScanTimer = null;
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
                _radioEvents.Writer.TryComplete();
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
    private sealed record WifiScanCommand(CancellationToken CancellationToken) : NetworkCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record WifiScanTimeoutCommand(long Generation) : NetworkCommand;
    private sealed record ConnectAvailableWifiCommand(
        string NetworkId,
        CancellationToken CancellationToken) : NetworkCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record SetWifiRadioCommand(bool Enabled, CancellationToken CancellationToken)
        : NetworkCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
