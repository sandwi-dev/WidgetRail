using System.Collections.Concurrent;
using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

/// <summary>
/// Event-driven Windows network provider. A dedicated MTA thread owns all native resources;
/// native callback threads only enqueue a bounded, coalesced invalidation.
/// </summary>
public sealed class WindowsNetworkPlatformBackend : INetworkPlatformBrokerBackend,
    IProtectedWifiHostBackend, IAsyncDisposable
{
    internal const int MaximumOrdinaryQueuedCommands =
        WindowsNetworkCommandQueue.MaximumOrdinaryQueuedCommands;
    private static readonly TimeSpan DefaultConnectionAttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WifiScanTimeout = TimeSpan.FromSeconds(6);
    private readonly IWindowsNetworkNativeAdapterFactory _factory;
    private readonly INetworkDeadlineScheduler _deadlineScheduler;
    private readonly TimeSpan _connectionAttemptTimeout;
    private readonly WindowsNetworkOperationPolicy _operations = new();
    private readonly WindowsNetworkStateReconciler _reconciler = new();
    private readonly WindowsNetworkCommandQueue _commands;
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
    private Thread? _ownerThread;
    private Task _eventPump = Task.CompletedTask;
    private long _nextGeneration;
    private long _activeGeneration;
    private int _started;
    private int _refreshQueued;
    private int _disposeStarted;
    private int _ownerStopped;
    private volatile bool _degraded;
    private volatile bool _wirelessAccessRestricted;
    private volatile bool _ownerUnavailable;
    private volatile bool _snapshotUnavailable;
    private IDisposable? _connectionAttemptTimer;
    private IDisposable? _wifiScanTimer;

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
        : this(
            factory ?? new WindowsNetworkNativeAdapterFactory(),
            connectionAttemptTimeout ?? DefaultConnectionAttemptTimeout,
            new NetworkDeadlineScheduler())
    {
    }

    internal WindowsNetworkPlatformBackend(
        IWindowsNetworkNativeAdapterFactory factory,
        TimeSpan connectionAttemptTimeout,
        INetworkDeadlineScheduler deadlineScheduler,
        INetworkCommandAdmissionObserver? admissionObserver = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _deadlineScheduler = deadlineScheduler ?? throw new ArgumentNullException(nameof(deadlineScheduler));
        _commands = new(admissionObserver);
        _connectionAttemptTimeout = connectionAttemptTimeout;
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

    internal int OrdinaryCommandsQueued => _commands.OrdinaryCommandsQueued;
    internal int DeadlineCommandsQueued => _commands.DeadlineCommandsQueued;
    internal int PendingDeadlineOverflowCount => _commands.PendingDeadlineOverflowCount;
    internal bool CommandAdmissionClosed => _commands.IsClosed;
    internal IReadOnlyList<string> QueuedCommandKinds => _commands.QueuedCommandKinds;

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
        if (!WindowsNetworkCommandPolicy.IsValidAvailableWifiId(networkId))
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

    public async Task<ProtectedWifiConnectionResult> ConnectProtectedWifiAsync(
        string networkId,
        char[] secret,
        CancellationToken cancellationToken)
    {
        if (!WindowsNetworkCommandPolicy.IsValidAvailableWifiId(networkId))
            throw new BrokerException("invalid_payload", "The available Wi-Fi identifier is invalid.");
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length is < 8 or > 63)
            return new(ProtectedWifiConnectionStatus.Rejected, "invalid_credential");
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            return new(ProtectedWifiConnectionStatus.Rejected, "platform_unavailable");
        var command = new ConnectProtectedWifiCommand(
            networkId, secret.ToArray(), cancellationToken);
        try
        {
            EnqueueCommand(command);
            return await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            command.Dispose();
        }
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
            if (!_commands.TryEnqueueOrdinary(command))
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
            if (!_commands.TryEnqueueOrdinary(command)) return;
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
        if (!WindowsNetworkCommandPolicy.IsValidSavedProfileId(profileId))
            throw new BrokerException("invalid_payload", "The saved network identifier is invalid.");

        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows networking is temporarily unavailable.");

        var command = new ConnectCommand(profileId, cancellationToken);
        EnqueueCommand(command);
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
                _commands.Release(command);
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
                    case ConnectionTimeoutCommand timeout:
                        ExecuteConnectionTimeout(adapter, timeout.Generation);
                        break;
                    case ConnectAvailableWifiCommand connectWifi:
                        ExecuteConnectAvailableWifi(adapter, connectWifi);
                        break;
                    case ConnectProtectedWifiCommand protectedWifi:
                        ExecuteConnectProtectedWifi(adapter, protectedWifi);
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
            Volatile.Write(ref _ownerStopped, 1);
            Volatile.Write(ref _activeGeneration, 0);
            _commands.Close();
            if (adapter is not null)
            {
                adapter.StateChanged -= OnNativeStateChanged;
                try { adapter.Dispose(); }
                catch { }
            }
            if (apartmentInitialized) NetworkInterop.Uninitialize();
            while (_commands.TryTake(out var pending))
            {
                _commands.Release(pending);
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
                    case ConnectProtectedWifiCommand protectedWifi:
                        protectedWifi.Completion.TrySetException(
                            new ObjectDisposedException(nameof(WindowsNetworkPlatformBackend)));
                        protectedWifi.Dispose();
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
            if (!_commands.TryEnqueueOrdinary(RefreshCommand.Instance))
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
                _operations.EnsureConnectionCanStart();
                if (!_nativeKeysByOpaqueId.TryGetValue(command.ProfileId, out nativeKey!))
                    throw new BrokerException(
                        "resource_not_found", "The saved network is no longer available.");
            }
            WindowsNetworkCommandPolicy.StartSavedConnection(adapter, nativeKey);
            NetworkStatusSummary connecting;
            lock (_stateGate)
            {
                var attempt = _operations.BeginConnection(command.ProfileId, nativeKey);
                CancelConnectionAttemptTimerLocked();
                _connectionAttemptTimer = _deadlineScheduler.Schedule(
                    _connectionAttemptTimeout,
                    () => OnConnectionAttemptTimeout(attempt.Generation));
                connecting = _status = _operations.ApplyTo(_status);
            }
            _events.Writer.TryWrite(connecting);
            // WlanConnect is asynchronous. ACM completion/failure refreshes the pending attempt.
            command.Completion.TrySetResult();
        }
        catch (BrokerException exception)
        {
            if (exception.Code == "platform_unavailable") SetDegradedState(publish: true);
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
            var decision = WindowsNetworkCommandPolicy.StartWifiScan(adapter);
            if (!decision.ChangesState)
            {
                command.Completion.TrySetResult();
                return;
            }
            AvailableWifiNetworksSummary snapshot;
            lock (_stateGate)
            {
                var projection = _reconciler.ResetAvailableWifi(decision.State);
                snapshot = _availableWifi = projection.Snapshot;
                _wifiNativeKeysByOpaqueId = projection.NativeKeysByOpaqueId;
                CancelWifiScanTimerLocked();
                if (decision.StartsDeadline)
                {
                    var generation = _operations.BeginScanDeadline();
                    _wifiScanTimer = _deadlineScheduler.Schedule(
                        WifiScanTimeout,
                        () => OnWifiScanTimeout(generation));
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

    private void OnWifiScanTimeout(long generation)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 ||
            Volatile.Read(ref _ownerStopped) != 0) return;
        _commands.EnqueueDeadline(new WifiScanTimeoutCommand(generation));
    }

    private void ExecuteWifiScanTimeout(long generation)
    {
        AvailableWifiNetworksSummary? snapshot = null;
        lock (_stateGate)
        {
            if (!_operations.IsCurrentScanDeadline(generation, _availableWifi.ScanState)) return;
            CancelWifiScanTimerLocked();
            _operations.InvalidateScanDeadline();
            var projection = _reconciler.ResetAvailableWifi(WifiScanState.Unavailable);
            snapshot = _availableWifi = projection.Snapshot;
            _wifiNativeKeysByOpaqueId = projection.NativeKeysByOpaqueId;
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
                _operations.EnsureConnectionCanStart();
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

            WindowsNetworkCommandPolicy.StartAvailableWifiConnection(adapter, nativeKey);

            NetworkStatusSummary connecting;
            lock (_stateGate)
            {
                var attempt = _operations.BeginConnection(command.NetworkId, nativeKey);
                CancelConnectionAttemptTimerLocked();
                _connectionAttemptTimer = _deadlineScheduler.Schedule(
                    _connectionAttemptTimeout,
                    () => OnConnectionAttemptTimeout(attempt.Generation));
                connecting = _status = _operations.ApplyTo(_status);
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

    private void ExecuteConnectProtectedWifi(
        IWindowsNetworkNativeAdapter adapter,
        ConnectProtectedWifiCommand command)
    {
        ProtectedWifiConnectionResult result;
        try
        {
            if (command.CancellationToken.IsCancellationRequested)
            {
                command.Dispose();
                command.Completion.TrySetCanceled(command.CancellationToken);
                return;
            }
            string nativeKey;
            lock (_stateGate)
            {
                _operations.EnsureConnectionCanStart();
                if (_availableWifi.ScanState != WifiScanState.Ready ||
                    !_wifiNativeKeysByOpaqueId.TryGetValue(command.NetworkId, out nativeKey!))
                    throw new BrokerException(
                        "resource_not_found", "The visible Wi-Fi network is no longer available.");
            }
            WindowsNetworkCommandPolicy.StartProtectedWifiConnection(
                adapter, nativeKey, command.Secret);
            NetworkStatusSummary connecting;
            lock (_stateGate)
            {
                var attempt = _operations.BeginConnection(command.NetworkId, nativeKey);
                CancelConnectionAttemptTimerLocked();
                _connectionAttemptTimer = _deadlineScheduler.Schedule(
                    _connectionAttemptTimeout,
                    () => OnConnectionAttemptTimeout(attempt.Generation));
                connecting = _status = _operations.ApplyTo(_status);
            }
            _events.Writer.TryWrite(connecting);
            result = new(ProtectedWifiConnectionStatus.Connecting, "connecting");
        }
        catch (BrokerException exception)
        {
            result = new(ProtectedWifiConnectionStatus.Rejected, exception.Code);
        }
        catch
        {
            result = new(
                ProtectedWifiConnectionStatus.Rejected, "platform_unavailable");
        }
        finally
        {
            command.Dispose();
        }
        command.Completion.TrySetResult(result);
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
            WindowsNetworkCommandPolicy.SetWifiRadio(adapter, command.Enabled);
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
            var snapshot = WindowsNetworkStateReconciler.ProjectRadio(adapter.ReadWifiRadio());
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

    private void OnConnectionAttemptTimeout(long generation)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 ||
            Volatile.Read(ref _ownerStopped) != 0) return;
        _commands.EnqueueDeadline(new ConnectionTimeoutCommand(generation));
    }

    private void ExecuteConnectionTimeout(
        IWindowsNetworkNativeAdapter adapter,
        long generation)
    {
        NetworkStatusSummary? failed = null;
        string? nativeKey = null;
        lock (_stateGate)
        {
            nativeKey = _operations.Connection.NativeKey;
            if (!_operations.ApplyConnectionTimeout(generation)) return;
            CancelConnectionAttemptTimerLocked();
            failed = _status = _operations.ApplyTo(_status);
        }
        if (nativeKey is not null)
        {
            _ = adapter.RollbackProtectedWifiConnection(nativeKey);
            _degraded = adapter.IsDegraded;
        }
        _events.Writer.TryWrite(failed);
    }

    private void Refresh(IWindowsNetworkNativeAdapter adapter, bool publish)
    {
        try
        {
            var snapshot = adapter.ReadSnapshot() ?? throw new InvalidOperationException();
            var outcomes = new List<NativeNetworkConnectionOutcome>();
            while (_nativeOutcomes.TryDequeue(out var outcome)) outcomes.Add(outcome);
            var projection = _reconciler.ReconcileNetwork(snapshot, _operations, outcomes);
            bool changed;
            lock (_stateGate)
            {
                changed = _status != projection.Status ||
                    !WindowsNetworkStateReconciler.ProfilesEqual(_profiles, projection.Profiles);
                _status = projection.Status;
                _profiles = projection.Profiles;
                _nativeKeysByOpaqueId = projection.NativeKeysByOpaqueId;
                if (_operations.Connection.State != NetworkConnectionAttemptState.Connecting)
                    CancelConnectionAttemptTimerLocked();
            }
            _degraded = adapter.IsDegraded;
            _snapshotUnavailable = false;
            _wirelessAccessRestricted = projection.WirelessAccessRestricted;
            if (publish && changed) _events.Writer.TryWrite(projection.Status);
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
            var projection = _reconciler.ReconcileAvailableWifi(
                adapter.ReadAvailableWifiSnapshot() ?? throw new InvalidOperationException());
            lock (_stateGate)
            {
                var changed = !WindowsNetworkStateReconciler.AvailableWifiEqual(
                    _availableWifi, projection.Snapshot);
                _availableWifi = projection.Snapshot;
                _wifiNativeKeysByOpaqueId = projection.NativeKeysByOpaqueId;
                if (projection.Snapshot.ScanState != WifiScanState.Scanning)
                {
                    _operations.InvalidateScanDeadline();
                    CancelWifiScanTimerLocked();
                }
                if (publish && changed) _wifiEvents.Writer.TryWrite(projection.Snapshot);
            }
        }
        catch
        {
            AvailableWifiNetworksSummary snapshot;
            lock (_stateGate)
            {
                var projection = _reconciler.ResetAvailableWifi(WifiScanState.Unavailable);
                snapshot = _availableWifi = projection.Snapshot;
                _wifiNativeKeysByOpaqueId = projection.NativeKeysByOpaqueId;
                _operations.InvalidateScanDeadline();
                CancelWifiScanTimerLocked();
            }
            if (publish) _wifiEvents.Writer.TryWrite(snapshot);
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
            _reconciler.Reset();
            _operations.Reset();
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
            Publish(WindowsNetworkEventProjection.FromStatus(status));
        }
    }

    private async Task DispatchWifiEventsAsync()
    {
        await foreach (var snapshot in _wifiEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Publish(WindowsNetworkEventProjection.FromAvailableWifi(snapshot));
        }
    }

    private async Task DispatchRadioEventsAsync()
    {
        await foreach (var radio in _radioEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Publish(WindowsNetworkEventProjection.FromRadio(radio));
        }
    }

    private void Publish(BrokerPlatformEvent platformEvent)
    {
        var handlers = EventPublished;
        if (handlers is null) return;
        foreach (EventHandler<BrokerPlatformEvent> handler in handlers.GetInvocationList())
        {
            try { handler(this, platformEvent); }
            catch { }
        }
    }

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
            _commands.Close();
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

}
