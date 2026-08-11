using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

public sealed class WindowsNetworkNativeAdapterFactory : IWindowsNetworkNativeAdapterFactory
{
    public IWindowsNetworkNativeAdapter Create(long generation) =>
        new WindowsNetworkNativeAdapter(generation);
}

internal sealed record WindowsNetworkNativeAdapterTestHooks(
    Action? BeforeDisposalGate = null,
    Action? DisposalLinearized = null,
    Action? BeforeEventPublication = null);

/// <summary>
/// Sole owner of the Windows WLAN/IP notification lifetime, adapter generation, and terminal
/// disposal. Value and operation policies receive its current handle but cannot retain it.
/// </summary>
internal sealed class WindowsNetworkNativeAdapter : IWindowsNetworkNativeAdapter
{
    private const int LifetimeActive = 0;
    private const int LifetimeDisposing = 1;
    private const int LifetimeTerminal = 2;
    private const uint ErrorSuccess = 0;
    private const uint WlanNotificationSourceNone = 0;
    private const uint WlanNotificationSourceAcm = 0x00000008;

    private readonly IWindowsNetworkNativeCalls _calls;
    private readonly WindowsNetworkWlanPolicy _wlan = new();
    private readonly NativeWifiNotificationCallback _wlanCallback;
    private readonly IpInterfaceChangeCallback _ipCallback;
    private readonly NetworkConnectivityHintChangeCallback _connectivityCallback;
    private readonly WindowsNetworkNativeAdapterTestHooks? _testHooks;
    private readonly object _lifetimeGate = new();
    private readonly Dictionary<int, int> _publicationDepthByThread = [];
    private IntPtr _wlanHandle;
    private IntPtr _ipNotificationHandle;
    private IntPtr _connectivityNotificationHandle;
    private int _lifetimeState;
    private int _degraded;
    private bool _wlanServiceAvailable;
    private bool _wlanNotificationsRegistered;
    private bool _connectivityNotificationsSupported = true;
    private bool _cleanupComplete;
    private int _activePublications;

    internal WindowsNetworkNativeAdapter(long generation)
        : this(generation, WindowsNetworkNativeCalls.Instance, null, requireWindows: true)
    {
    }

    internal WindowsNetworkNativeAdapter(
        long generation,
        IWindowsNetworkNativeCalls calls,
        WindowsNetworkNativeAdapterTestHooks? testHooks = null)
        : this(generation, calls, testHooks, requireWindows: false)
    {
    }

    private WindowsNetworkNativeAdapter(
        long generation,
        IWindowsNetworkNativeCalls calls,
        WindowsNetworkNativeAdapterTestHooks? testHooks,
        bool requireWindows)
    {
        if (requireWindows && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows network APIs require Windows.");
        _calls = calls ?? throw new ArgumentNullException(nameof(calls));
        _testHooks = testHooks;
        Generation = generation;
        _wlanCallback = OnWlanNotification;
        _ipCallback = OnIpInterfaceChanged;
        _connectivityCallback = OnConnectivityHintChanged;
        lock (_lifetimeGate)
        {
            TryOpenNativeWifi();
            TryRegisterIpNotifications();
            TryRegisterConnectivityNotifications();
        }
    }

    public event EventHandler<NativeNetworkStateChangedEventArgs>? StateChanged;
    public long Generation { get; }
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;

    public NativeNetworkSnapshot ReadSnapshot()
    {
        ThrowIfDisposed();
        var connectivity = WindowsNetworkConnectivityPolicy.Read(_calls);
        IReadOnlyList<WirelessInterface> interfaces;
        NetworkWirelessAvailability wirelessAvailability;
        IReadOnlyList<NativeSavedNetworkProfile> profiles;
        bool registrationDegraded;
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            TryRecoverEventRegistrations();
            interfaces = _wlanHandle == IntPtr.Zero
                ? []
                : _wlan.EnumerateInterfaces(_calls, _wlanHandle);
            wirelessAvailability = WindowsNetworkRadioPolicy.ResolveWirelessAvailability(
                _calls,
                _wlanHandle,
                connectivity.Interfaces,
                interfaces,
                _wlanServiceAvailable,
                _wlanNotificationsRegistered);
            profiles = _wlanHandle == IntPtr.Zero
                ? []
                : _wlan.ReadSavedProfiles(_calls, _wlanHandle, interfaces);
            registrationDegraded = HasDegradedRegistrationState(
                _ipNotificationHandle != IntPtr.Zero,
                _connectivityNotificationsSupported,
                _connectivityNotificationHandle != IntPtr.Zero,
                _wlanHandle != IntPtr.Zero,
                _wlanNotificationsRegistered);
        }

        // Current Wi-Fi connection details are precise-location gated on current Windows.
        // There is no automatic prompt path, so the adapter never publishes the active SSID.
        var restricted = wirelessAvailability == NetworkWirelessAvailability.Available;
        Volatile.Write(ref _degraded, registrationDegraded ? 1 : 0);
        return new(
            connectivity.Connectivity,
            connectivity.Interfaces.DefaultMedium,
            null,
            null,
            null,
            profiles,
            wirelessAvailability,
            restricted);
    }

    public bool TryConnectSavedProfile(string nativeProfileKey)
    {
        ThrowIfDisposed();
        if (_wlanHandle == IntPtr.Zero)
        {
            Volatile.Write(ref _degraded, 1);
            return false;
        }
        try
        {
            lock (_lifetimeGate)
            {
                ThrowIfDisposed();
                return _wlan.TryConnectSavedProfile(
                    _calls,
                    _wlanHandle,
                    nativeProfileKey);
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, 1);
            throw;
        }
    }

    public NativeAvailableWifiSnapshot ReadAvailableWifiSnapshot()
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            return _wlan.ReadAvailableSnapshot(_calls, _wlanHandle);
        }
    }

    public NativeWifiScanStartResult TryStartWifiScan()
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            return _wlan.TryStartScan(_calls, _wlanHandle);
        }
    }

    public NativeWifiConnectStartResult TryConnectAvailableWifiNetwork(
        string nativeNetworkKey)
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            return _wlan.TryConnectAvailableNetwork(
                _calls,
                _wlanHandle,
                nativeNetworkKey);
        }
    }

    public NativeProtectedWifiConnectStartResult TryConnectProtectedWifiNetwork(
        string nativeNetworkKey,
        ReadOnlySpan<char> secret)
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            return _wlan.TryConnectProtectedNetwork(
                _calls, _wlanHandle, nativeNetworkKey, secret);
        }
    }

    public void RollbackProtectedWifiConnection(string nativeNetworkKey)
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            _wlan.RollbackProtected(_calls, _wlanHandle, nativeNetworkKey);
        }
    }

    public NativeWifiRadioSnapshot ReadWifiRadio()
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            var interfaces = _wlan.EnumerateInterfaces(_calls, _wlanHandle);
            return WindowsNetworkRadioPolicy.Read(_calls, _wlanHandle, interfaces);
        }
    }

    public NativeWifiRadioSetResult TrySetWifiRadio(bool enabled)
    {
        ThrowIfDisposed();
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            var interfaces = _wlan.EnumerateInterfaces(_calls, _wlanHandle);
            return WindowsNetworkRadioPolicy.Set(_calls, _wlanHandle, interfaces, enabled);
        }
    }

    internal static bool HasDegradedRegistrationState(
        bool ipInterfaceRegistered,
        bool connectivityNotificationsSupported,
        bool connectivityHintRegistered,
        bool wlanOpen,
        bool wlanAcmRegistered) =>
        !ipInterfaceRegistered ||
        (connectivityNotificationsSupported && !connectivityHintRegistered) ||
        (wlanOpen && !wlanAcmRegistered);

    private void TryOpenNativeWifi()
    {
        var result = _calls.OpenWlan(out _wlanHandle);
        if (result != ErrorSuccess)
        {
            _wlanHandle = IntPtr.Zero;
            _wlanServiceAvailable = false;
            return;
        }
        _wlanServiceAvailable = true;
        _wlanNotificationsRegistered = _calls.RegisterWlanNotification(
            _wlanHandle,
            WlanNotificationSourceAcm,
            _wlanCallback) == ErrorSuccess;
    }

    private void TryRecoverEventRegistrations()
    {
        if (_ipNotificationHandle == IntPtr.Zero) TryRegisterIpNotifications();
        if (_connectivityNotificationsSupported && _connectivityNotificationHandle == IntPtr.Zero)
            TryRegisterConnectivityNotifications();
        if (_wlanHandle == IntPtr.Zero) TryOpenNativeWifi();
        else if (!_wlanNotificationsRegistered) TryRegisterWlanNotifications();
    }

    private void TryRegisterWlanNotifications()
    {
        if (_wlanHandle == IntPtr.Zero || _wlanNotificationsRegistered) return;
        _wlanNotificationsRegistered = _calls.RegisterWlanNotification(
            _wlanHandle,
            WlanNotificationSourceAcm,
            _wlanCallback) == ErrorSuccess;
    }

    private void TryRegisterIpNotifications()
    {
        if (_ipNotificationHandle != IntPtr.Zero) return;
        if (_calls.RegisterIpInterfaceChange(_ipCallback, out _ipNotificationHandle) != ErrorSuccess)
            _ipNotificationHandle = IntPtr.Zero;
    }

    private void TryRegisterConnectivityNotifications()
    {
        if (!_connectivityNotificationsSupported || _connectivityNotificationHandle != IntPtr.Zero)
            return;
        try
        {
            if (_calls.RegisterConnectivityHintChange(
                    _connectivityCallback,
                    out _connectivityNotificationHandle) != ErrorSuccess)
                _connectivityNotificationHandle = IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            _connectivityNotificationsSupported = false;
            _connectivityNotificationHandle = IntPtr.Zero;
        }
    }

    private void OnIpInterfaceChanged(IntPtr context, IntPtr row, int notificationType) =>
        RaiseChanged(null);

    private void OnConnectivityHintChanged(IntPtr context, NetworkConnectivityHint hint) =>
        RaiseChanged(null);

    private void OnWlanNotification(ref WlanNotificationData data, IntPtr context)
    {
        NativeWlanNotificationProjection projection;
        lock (_lifetimeGate)
        {
            if (_lifetimeState != LifetimeActive) return;
            projection = _wlan.ProcessNotification(_calls, _wlanHandle, ref data);
        }
        RaiseChanged(projection.ConnectionOutcome, projection.ScanOutcome);
    }

    private void RaiseChanged(
        NativeNetworkConnectionOutcome? outcome,
        NativeWifiScanOutcome? scanOutcome = null)
    {
        var threadId = Environment.CurrentManagedThreadId;
        lock (_lifetimeGate)
        {
            if (_lifetimeState != LifetimeActive) return;
            _activePublications++;
            _publicationDepthByThread.TryGetValue(threadId, out var depth);
            _publicationDepthByThread[threadId] = depth + 1;
        }
        try
        {
            _testHooks?.BeforeEventPublication?.Invoke();
            EventHandler<NativeNetworkStateChangedEventArgs>? handlers;
            lock (_lifetimeGate)
            {
                if (_lifetimeState != LifetimeActive) return;
                handlers = StateChanged;
            }
            handlers?.Invoke(this, new NativeNetworkStateChangedEventArgs(Generation)
            {
                ConnectionOutcome = outcome,
                WifiScanOutcome = scanOutcome,
            });
        }
        finally
        {
            lock (_lifetimeGate)
            {
                _activePublications--;
                var depth = _publicationDepthByThread[threadId] - 1;
                if (depth == 0) _publicationDepthByThread.Remove(threadId);
                else _publicationDepthByThread[threadId] = depth;
                if (_lifetimeState == LifetimeDisposing &&
                    _cleanupComplete &&
                    _activePublications == 0)
                    _lifetimeState = LifetimeTerminal;
                Monitor.PulseAll(_lifetimeGate);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _lifetimeState) != LifetimeActive)
            throw new ObjectDisposedException(nameof(WindowsNetworkNativeAdapter));
    }

    public void Dispose()
    {
        _testHooks?.BeforeDisposalGate?.Invoke();
        lock (_lifetimeGate)
        {
            _publicationDepthByThread.TryGetValue(
                Environment.CurrentManagedThreadId,
                out var reentrantDepth);
            if (_lifetimeState == LifetimeTerminal) return;
            if (_lifetimeState == LifetimeDisposing)
            {
                if (reentrantDepth != 0) return;
                while (_lifetimeState != LifetimeTerminal)
                    Monitor.Wait(_lifetimeGate);
                return;
            }

            Volatile.Write(ref _lifetimeState, LifetimeDisposing);
            _testHooks?.DisposalLinearized?.Invoke();
            if (_ipNotificationHandle != IntPtr.Zero)
            {
                _ = _calls.CancelChangeNotification(_ipNotificationHandle);
                _ipNotificationHandle = IntPtr.Zero;
            }
            if (_connectivityNotificationHandle != IntPtr.Zero)
            {
                _ = _calls.CancelChangeNotification(_connectivityNotificationHandle);
                _connectivityNotificationHandle = IntPtr.Zero;
            }
            if (_wlanHandle != IntPtr.Zero)
            {
                _wlan.RollbackPendingProtected(_calls, _wlanHandle);
                if (_wlanNotificationsRegistered)
                    _ = _calls.RegisterWlanNotification(
                        _wlanHandle,
                        WlanNotificationSourceNone,
                        null);
                _wlanNotificationsRegistered = false;
                _ = _calls.CloseWlan(_wlanHandle);
                _wlanHandle = IntPtr.Zero;
            }
            _wlan.Clear();
            _cleanupComplete = true;
            while (_activePublications > reentrantDepth)
                Monitor.Wait(_lifetimeGate);
            if (_activePublications == 0)
            {
                _lifetimeState = LifetimeTerminal;
                Monitor.PulseAll(_lifetimeGate);
            }
        }
    }
}
