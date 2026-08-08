using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

public sealed class WindowsNetworkNativeAdapterFactory : IWindowsNetworkNativeAdapterFactory
{
    public IWindowsNetworkNativeAdapter Create(long generation) =>
        new WindowsNetworkNativeAdapter(generation);
}

/// <summary>
/// Windows desktop adapter using Native Wi-Fi (wlanapi) and IP Helper connectivity/interface
/// notifications. Scans are explicit and bounded; public identities are assigned by the provider.
/// </summary>
internal sealed class WindowsNetworkNativeAdapter : IWindowsNetworkNativeAdapter
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const uint WlanNotificationSourceNone = 0;
    private const uint WlanNotificationSourceAcm = 0x00000008;
    private const uint WlanNotificationAcmScanComplete = 7;
    private const uint WlanNotificationAcmScanFail = 8;
    private const uint WlanNotificationAcmConnectionComplete = 10;
    private const uint WlanNotificationAcmConnectionAttemptFail = 11;
    private const int WlanIntfOpcodeRadioState = 4;
    private const int Dot11RadioStateUnknown = 0;
    private const int Dot11RadioStateOn = 1;
    private const int Dot11RadioStateOff = 2;
    private const int WlanConnectionModeProfile = 0;
    private const int WlanConnectionModeDiscoveryUnsecure = 3;
    private const int Dot11BssTypeAny = 3;
    private const int MaximumInterfaces = 32;
    private const int MaximumProfilesPerInterface = 128;
    private const int MaximumAvailableNetworks = 256;
    private const uint WlanAvailableNetworkConnected = 0x00000001;
    private const uint WlanAvailableNetworkHasProfile = 0x00000002;
    private readonly NativeWifiNotificationCallback _wlanCallback;
    private readonly IpInterfaceChangeCallback _ipCallback;
    private readonly NetworkConnectivityHintChangeCallback _connectivityCallback;
    private readonly Dictionary<string, NativeProfileTarget> _connectableProfiles =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeAvailableNetworkTarget> _connectableNetworks =
        new(StringComparer.Ordinal);
    private readonly object _scanGate = new();
    private readonly HashSet<Guid> _pendingScanInterfaces = [];
    private IntPtr _wlanHandle;
    private IntPtr _ipNotificationHandle;
    private IntPtr _connectivityNotificationHandle;
    private int _disposed;
    private int _degraded;
    private bool _wlanServiceAvailable;
    private bool _wlanNotificationsRegistered;
    private bool _connectivityNotificationsSupported = true;
    private NativeWifiScanState _scanState = NativeWifiScanState.NotScanned;
    private long _scanGeneration;
    private bool _scanHadSuccess;
    private PendingNativeConnection? _pendingConnection;
    private long _cachedAvailableGeneration = -1;
    private IReadOnlyList<NativeAvailableWifiNetwork> _cachedAvailableNetworks = [];

    internal WindowsNetworkNativeAdapter(long generation)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows network APIs require Windows.");
        Generation = generation;
        _wlanCallback = OnWlanNotification;
        _ipCallback = OnIpInterfaceChanged;
        _connectivityCallback = OnConnectivityHintChanged;
        TryOpenNativeWifi();
        TryRegisterIpNotifications();
        TryRegisterConnectivityNotifications();
    }

    public event EventHandler<NativeNetworkStateChangedEventArgs>? StateChanged;
    public long Generation { get; }
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;

    public NativeNetworkSnapshot ReadSnapshot()
    {
        ThrowIfDisposed();
        TryRecoverEventRegistrations();
        var connectivity = ReadConnectivity();
        var interfaces = ReadManagedInterfaces();
        var profiles = new List<NativeSavedNetworkProfile>();
        NativeNetworkMedium medium = interfaces.DefaultMedium;
        string? activeNativeKey = null;
        string? activeName = null;
        int? signal = null;
        var nativeInterfaces = _wlanHandle == IntPtr.Zero
            ? []
            : EnumerateWirelessInterfaces();
        var wirelessAvailability = ResolveWirelessAvailability(interfaces, nativeInterfaces);
        _connectableProfiles.Clear();

        if (_wlanHandle != IntPtr.Zero)
        {
            foreach (var wireless in nativeInterfaces)
            {
                foreach (var profileName in EnumerateProfiles(wireless.InterfaceId))
                {
                    if (profiles.Count >= MaximumProfilesPerInterface) break;
                    var nativeKey = NativeKey(wireless.InterfaceId, profileName);
                    if (!_connectableProfiles.TryAdd(
                            nativeKey, new NativeProfileTarget(wireless.InterfaceId, profileName))) continue;
                    var connected = string.Equals(nativeKey, activeNativeKey, StringComparison.Ordinal);
                    profiles.Add(new NativeSavedNetworkProfile(
                        nativeKey,
                        profileName,
                        connected,
                        connected ? signal : null));
                }
            }
        }

        // Current Wi-Fi connection details are precise-location gated on current Windows.
        // The provider has no automatic prompt path, so usable Wi-Fi is restricted by default.
        var restricted = wirelessAvailability == NetworkWirelessAvailability.Available;
        // Privacy-restricted reads cannot identify Wi-Fi, but managed
        // interface state can still report the transport without exposing an adapter identity.
        if (medium == NativeNetworkMedium.WiFi && activeName is null) signal = null;
        var registrationDegraded = HasDegradedRegistrationState(
            _ipNotificationHandle != IntPtr.Zero,
            _connectivityNotificationsSupported,
            _connectivityNotificationHandle != IntPtr.Zero,
            _wlanHandle != IntPtr.Zero,
            _wlanNotificationsRegistered);
        Volatile.Write(ref _degraded, registrationDegraded ? 1 : 0);
        return new NativeNetworkSnapshot(
            connectivity,
            medium,
            activeNativeKey,
            activeName,
            signal,
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
        // Only a target retained from the latest WlanGetProfileList result is accepted.
        if (!_connectableProfiles.TryGetValue(nativeProfileKey, out var target)) return false;
        var parameters = new WlanConnectionParameters
        {
            ConnectionMode = WlanConnectionModeProfile,
            Profile = target.ProfileName,
            Dot11Ssid = IntPtr.Zero,
            DesiredBssidList = IntPtr.Zero,
            Dot11BssType = Dot11BssTypeAny,
            Flags = 0,
        };
        var interfaceId = target.InterfaceId;
        var result = NativeMethods.WlanConnect(
            _wlanHandle, ref interfaceId, ref parameters, IntPtr.Zero);
        if (result == ErrorSuccess)
        {
            lock (_scanGate)
                _pendingConnection = new PendingNativeConnection(
                    nativeProfileKey, target.InterfaceId, target.ProfileName, []);
            return true;
        }
        Volatile.Write(ref _degraded, 1);
        throw new Win32Exception((int)result, "Windows could not start the saved Wi-Fi connection.");
    }

    public NativeAvailableWifiSnapshot ReadAvailableWifiSnapshot()
    {
        ThrowIfDisposed();
        NativeWifiScanState state;
        long generation;
        lock (_scanGate)
        {
            state = _scanState;
            generation = _scanGeneration;
        }
        if (state != NativeWifiScanState.Ready)
            return new NativeAvailableWifiSnapshot(generation, state, []);
        lock (_scanGate)
        {
            if (_cachedAvailableGeneration == generation)
                return new NativeAvailableWifiSnapshot(
                    generation, NativeWifiScanState.Ready, _cachedAvailableNetworks.ToArray());
        }

        var networks = new List<NativeAvailableWifiNetwork>();
        var targets = new Dictionary<string, NativeAvailableNetworkTarget>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accessDenied = false;
        var anyInterfaceRead = false;
        foreach (var wireless in EnumerateWirelessInterfaces())
        {
            var result = EnumerateAvailableNetworks(wireless.InterfaceId);
            if (result.AccessDenied)
            {
                accessDenied = true;
                continue;
            }
            if (!result.Succeeded) continue;
            anyInterfaceRead = true;
            foreach (var item in result.Networks)
            {
                if (networks.Count >= MaximumAvailableNetworks) break;
                var connected = (item.Flags & WlanAvailableNetworkConnected) != 0;
                var hasProfile = (item.Flags & WlanAvailableNetworkHasProfile) != 0 &&
                    !string.IsNullOrWhiteSpace(item.ProfileName);
                if (!ShouldExposeAvailableNetwork(
                        item.IsConnectable, connected, hasProfile, item.Ssid.Length)) continue;
                var deduplicationKey = AvailableNetworkDeduplicationKey(
                    wireless.InterfaceId,
                    item.Ssid,
                    item.AuthenticationAlgorithm,
                    item.CipherAlgorithm);
                if (!seen.Add(deduplicationKey)) continue;
                var nativeKey = $"wifi_native_{Guid.NewGuid():N}";
                var security = ClassifySecurity(item.SecurityEnabled, item.AuthenticationAlgorithm);
                var credentialRequired = !connected && !hasProfile && item.SecurityEnabled;
                targets.Add(nativeKey, new NativeAvailableNetworkTarget(
                    wireless.InterfaceId,
                    item.Ssid,
                    item.BssType,
                    hasProfile ? item.ProfileName : null,
                    security,
                    credentialRequired));
                networks.Add(new NativeAvailableWifiNetwork(
                    nativeKey,
                    DecodeSsid(item.Ssid),
                    checked((int)Math.Min(item.SignalQuality, 100u)),
                    security,
                    credentialRequired,
                    connected,
                    hasProfile));
            }
        }

        lock (_scanGate)
        {
            if (_scanGeneration != generation || _scanState != NativeWifiScanState.Ready)
                return new NativeAvailableWifiSnapshot(_scanGeneration, _scanState, []);
            _connectableNetworks.Clear();
            if (!anyInterfaceRead)
            {
                _scanState = accessDenied
                    ? NativeWifiScanState.PreciseLocationDenied
                    : NativeWifiScanState.Unavailable;
                return new NativeAvailableWifiSnapshot(_scanGeneration, _scanState, []);
            }
            foreach (var target in targets) _connectableNetworks.Add(target.Key, target.Value);
            _cachedAvailableGeneration = generation;
            _cachedAvailableNetworks = networks.ToArray();
            return new NativeAvailableWifiSnapshot(
                generation, NativeWifiScanState.Ready, _cachedAvailableNetworks.ToArray());
        }
    }

    public NativeWifiScanStartResult TryStartWifiScan()
    {
        ThrowIfDisposed();
        if (_wlanHandle == IntPtr.Zero)
            return SetScanStartFailure(NativeWifiScanState.Unavailable,
                NativeWifiScanStartResult.Unavailable);
        var interfaces = EnumerateWirelessInterfaces();
        if (interfaces.Count == 0)
            return SetScanStartFailure(NativeWifiScanState.Unavailable,
                NativeWifiScanStartResult.Unavailable);

        lock (_scanGate)
        {
            if (_scanState == NativeWifiScanState.Scanning)
                return NativeWifiScanStartResult.AlreadyScanning;
            _pendingScanInterfaces.Clear();
            _connectableNetworks.Clear();
            _cachedAvailableGeneration = -1;
            _cachedAvailableNetworks = [];
            _scanHadSuccess = false;
            _scanState = NativeWifiScanState.Scanning;
            var denied = false;
            foreach (var wireless in interfaces)
            {
                _pendingScanInterfaces.Add(wireless.InterfaceId);
                var interfaceId = wireless.InterfaceId;
                var result = NativeMethods.WlanScan(
                    _wlanHandle, ref interfaceId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (result == ErrorSuccess) continue;
                _pendingScanInterfaces.Remove(wireless.InterfaceId);
                denied |= result == ErrorAccessDenied;
            }
            if (_pendingScanInterfaces.Count != 0) return NativeWifiScanStartResult.Started;
            _scanState = denied
                ? NativeWifiScanState.PreciseLocationDenied
                : NativeWifiScanState.Unavailable;
            return denied
                ? NativeWifiScanStartResult.PreciseLocationDenied
                : NativeWifiScanStartResult.Unavailable;
        }
    }

    public NativeWifiConnectStartResult TryConnectAvailableWifiNetwork(string nativeNetworkKey)
    {
        ThrowIfDisposed();
        if (_wlanHandle == IntPtr.Zero) return NativeWifiConnectStartResult.Unavailable;
        NativeAvailableNetworkTarget target;
        lock (_scanGate)
        {
            if (!_connectableNetworks.TryGetValue(nativeNetworkKey, out target!))
                return NativeWifiConnectStartResult.NotFound;
        }
        if (target.CredentialRequired)
            return target.Security == WifiSecurityKind.Enterprise
                ? NativeWifiConnectStartResult.UnsupportedAuthentication
                : NativeWifiConnectStartResult.CredentialRequired;

        IntPtr ssidPointer = IntPtr.Zero;
        try
        {
            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = target.ProfileName is null
                    ? WlanConnectionModeDiscoveryUnsecure
                    : WlanConnectionModeProfile,
                Profile = target.ProfileName,
                Dot11Ssid = IntPtr.Zero,
                DesiredBssidList = IntPtr.Zero,
                Dot11BssType = target.ProfileName is null ? target.BssType : Dot11BssTypeAny,
                Flags = 0,
            };
            if (target.ProfileName is null)
            {
                ssidPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Dot11Ssid>());
                Marshal.StructureToPtr(ToNativeSsid(target.Ssid), ssidPointer, false);
                parameters.Dot11Ssid = ssidPointer;
            }
            var interfaceId = target.InterfaceId;
            var result = NativeMethods.WlanConnect(
                _wlanHandle, ref interfaceId, ref parameters, IntPtr.Zero);
            if (result != ErrorSuccess)
                return result == ErrorAccessDenied
                    ? NativeWifiConnectStartResult.Unavailable
                    : NativeWifiConnectStartResult.NotFound;
            lock (_scanGate)
                _pendingConnection = new PendingNativeConnection(
                    nativeNetworkKey,
                    target.InterfaceId,
                    target.ProfileName,
                    target.Ssid.ToArray());
            return NativeWifiConnectStartResult.Started;
        }
        finally
        {
            if (ssidPointer != IntPtr.Zero) Marshal.FreeHGlobal(ssidPointer);
        }
    }

    private void TryOpenNativeWifi()
    {
        var result = NativeMethods.WlanOpenHandle(2, IntPtr.Zero, out _, out _wlanHandle);
        if (result != ErrorSuccess)
        {
            _wlanHandle = IntPtr.Zero;
            _wlanServiceAvailable = false;
            return;
        }
        _wlanServiceAvailable = true;
        result = NativeMethods.WlanRegisterNotification(
            _wlanHandle,
            WlanNotificationSourceAcm,
            true,
            _wlanCallback,
            IntPtr.Zero,
            IntPtr.Zero,
            out _);
        _wlanNotificationsRegistered = result == ErrorSuccess;
    }

    private void TryRecoverEventRegistrations()
    {
        if (_ipNotificationHandle == IntPtr.Zero) TryRegisterIpNotifications();
        if (_connectivityNotificationsSupported && _connectivityNotificationHandle == IntPtr.Zero)
            TryRegisterConnectivityNotifications();
        if (_wlanHandle == IntPtr.Zero)
            TryOpenNativeWifi();
        else if (!_wlanNotificationsRegistered)
            TryRegisterWlanNotifications();
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

    private void TryRegisterWlanNotifications()
    {
        if (_wlanHandle == IntPtr.Zero || _wlanNotificationsRegistered) return;
        var result = NativeMethods.WlanRegisterNotification(
            _wlanHandle,
            WlanNotificationSourceAcm,
            true,
            _wlanCallback,
            IntPtr.Zero,
            IntPtr.Zero,
            out _);
        _wlanNotificationsRegistered = result == ErrorSuccess;
    }

    private void TryRegisterIpNotifications()
    {
        if (_ipNotificationHandle != IntPtr.Zero) return;
        var result = NativeMethods.NotifyIpInterfaceChange(
            0,
            _ipCallback,
            IntPtr.Zero,
            false,
            out _ipNotificationHandle);
        if (result != ErrorSuccess) _ipNotificationHandle = IntPtr.Zero;
    }

    private void TryRegisterConnectivityNotifications()
    {
        if (!_connectivityNotificationsSupported || _connectivityNotificationHandle != IntPtr.Zero) return;
        try
        {
            var result = NativeMethods.NotifyNetworkConnectivityHintChange(
                _connectivityCallback,
                IntPtr.Zero,
                false,
                out _connectivityNotificationHandle);
            if (result != ErrorSuccess) _connectivityNotificationHandle = IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            // Windows before 10 2004 still has IP-interface and ACM notifications.
            _connectivityNotificationsSupported = false;
            _connectivityNotificationHandle = IntPtr.Zero;
        }
    }

    private NetworkConnectivity ReadConnectivity()
    {
        try
        {
            if (NativeMethods.GetNetworkConnectivityHint(out var hint) == ErrorSuccess)
            {
                switch (hint.ConnectivityLevel)
                {
                    case NetworkConnectivityLevelHint.InternetAccess:
                        return NetworkConnectivity.Internet;
                    case NetworkConnectivityLevelHint.LocalAccess:
                    case NetworkConnectivityLevelHint.ConstrainedInternetAccess:
                        return NetworkConnectivity.Local;
                    case NetworkConnectivityLevelHint.None:
                        return NetworkConnectivity.None;
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Windows before 10 2004 falls back to consentless managed availability.
        }
        return NetworkInterface.GetIsNetworkAvailable()
            ? NetworkConnectivity.Local
            : NetworkConnectivity.None;
    }

    private static ManagedInterfaceState ReadManagedInterfaces()
    {
        var hasWireless = false;
        var wirelessUp = false;
        var wirelessWithGateway = false;
        var ethernetWithGateway = false;
        var otherWithGateway = false;
        var bestInterfaceIndex = TryGetBestInterfaceIndex();
        var bestMedium = NativeNetworkMedium.None;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var wireless = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                hasWireless |= wireless;
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (wireless) wirelessUp = true;
                var hasGateway = false;
                try { hasGateway = adapter.GetIPProperties().GatewayAddresses.Count != 0; }
                catch (NetworkInformationException) { }
                if (!hasGateway) continue;
                int? interfaceIndex = null;
                try { interfaceIndex = adapter.GetIPProperties().GetIPv4Properties()?.Index; }
                catch (NetworkInformationException) { }
                if (interfaceIndex is not null && interfaceIndex.Value == bestInterfaceIndex)
                    bestMedium = wireless
                        ? NativeNetworkMedium.WiFi
                        : IsEthernet(adapter.NetworkInterfaceType)
                            ? NativeNetworkMedium.Ethernet
                            : NativeNetworkMedium.Other;
                if (wireless) wirelessWithGateway = true;
                else if (IsEthernet(adapter.NetworkInterfaceType)) ethernetWithGateway = true;
                else if (adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                         not NetworkInterfaceType.Tunnel) otherWithGateway = true;
            }
        }
        catch (NetworkInformationException) { }
        var medium = bestMedium != NativeNetworkMedium.None
            ? bestMedium
            : ethernetWithGateway
            ? NativeNetworkMedium.Ethernet
            : wirelessWithGateway
                ? NativeNetworkMedium.WiFi
                : otherWithGateway ? NativeNetworkMedium.Other : NativeNetworkMedium.None;
        return new ManagedInterfaceState(hasWireless, wirelessUp, medium);
    }

    private static int? TryGetBestInterfaceIndex()
    {
        // Route-table lookup only; this does not send traffic or contact the destination.
        var destinationBytes = new byte[] { 1, 1, 1, 1 };
        var destination = BitConverter.ToUInt32(destinationBytes, 0);
        return NativeMethods.GetBestInterface(destination, out var index) == ErrorSuccess
            ? checked((int)index)
            : null;
    }

    private NetworkWirelessAvailability ResolveWirelessAvailability(
        ManagedInterfaceState state,
        IReadOnlyList<WirelessInterface> nativeInterfaces)
    {
        if (!_wlanServiceAvailable) return state.HasWireless
            ? NetworkWirelessAvailability.ServiceUnavailable
            : NetworkWirelessAvailability.NoAdapter;
        if (!_wlanNotificationsRegistered)
            return NetworkWirelessAvailability.ServiceUnavailable;
        if (nativeInterfaces.Count == 0) return state.HasWireless
            ? NetworkWirelessAvailability.ServiceUnavailable
            : NetworkWirelessAvailability.NoAdapter;
        var radioStates = nativeInterfaces
            .Select(item => QueryRadioEnabled(item.InterfaceId))
            .ToArray();
        return ResolveRadioAvailability(radioStates);
    }

    internal static NetworkWirelessAvailability ResolveRadioAvailability(
        IReadOnlyList<bool?> radioStates)
    {
        if (radioStates.Any(enabled => enabled == true))
            return NetworkWirelessAvailability.Available;
        // Report RadioOff only when every enumerated interface explicitly says
        // all of its hardware/software PHY radios are off. A disconnected but
        // enabled interface is therefore still Available.
        return radioStates.All(enabled => enabled == false)
            ? NetworkWirelessAvailability.RadioOff
            : NetworkWirelessAvailability.Available;
    }

    private bool? QueryRadioEnabled(Guid interfaceId)
    {
        var id = interfaceId;
        var result = NativeMethods.WlanQueryInterface(
            _wlanHandle,
            ref id,
            WlanIntfOpcodeRadioState,
            IntPtr.Zero,
            out var dataSize,
            out var data,
            IntPtr.Zero);
        if (result != ErrorSuccess || data == IntPtr.Zero) return null;
        try
        {
            if (dataSize < sizeof(uint)) return null;
            var count = Math.Min(Marshal.ReadInt32(data), 64);
            if (count <= 0 || dataSize < 4u + (uint)count * 12u) return null;
            var allExplicitlyOff = true;
            for (var index = 0; index < count; index++)
            {
                var offset = sizeof(uint) + index * 12;
                var software = Marshal.ReadInt32(data, offset + 4);
                var hardware = Marshal.ReadInt32(data, offset + 8);
                if (software == 1 && hardware == 1) return true;
                allExplicitlyOff &= software == 2 || hardware == 2;
            }
            return allExplicitlyOff ? false : null;
        }
        finally { NativeMethods.WlanFreeMemory(data); }
    }

    public NativeWifiRadioSnapshot ReadWifiRadio()
    {
        ThrowIfDisposed();
        if (_wlanHandle == IntPtr.Zero)
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.Unavailable, false);
        var interfaces = EnumerateWirelessInterfaces();
        if (interfaces.Count == 0)
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.NoAdapter, false);
        var phys = interfaces.SelectMany(item => QueryPhyRadioStates(item.InterfaceId)).ToArray();
        if (phys.Length == 0)
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.Unavailable, false);
        if (phys.Any(item => item.HardwareState == Dot11RadioStateOn &&
                             item.SoftwareState == Dot11RadioStateOn))
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.On, true);
        if (phys.Any(item => item.HardwareState == Dot11RadioStateOn))
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.Off, true);
        if (phys.All(item => item.HardwareState == Dot11RadioStateOff))
            return new NativeWifiRadioSnapshot(NativeWifiRadioState.HardwareDisabled, false);
        return new NativeWifiRadioSnapshot(NativeWifiRadioState.Unavailable, false);
    }

    public NativeWifiRadioSetResult TrySetWifiRadio(bool enabled)
    {
        ThrowIfDisposed();
        if (_wlanHandle == IntPtr.Zero) return NativeWifiRadioSetResult.Unavailable;
        var interfaces = EnumerateWirelessInterfaces();
        if (interfaces.Count == 0) return NativeWifiRadioSetResult.NoAdapter;
        var targets = interfaces.SelectMany(wireless =>
                QueryPhyRadioStates(wireless.InterfaceId)
                    .Select(phy => (wireless.InterfaceId, Phy: phy)))
            .ToArray();
        return ApplySoftwareRadioTransaction(
            targets,
            enabled,
            target => target.Phy.SoftwareState,
            target => target.Phy.HardwareState,
            (target, desired) => SetSoftwareRadioState(
                target.InterfaceId, target.Phy.PhyIndex, desired));
    }

    internal static NativeWifiRadioSetResult ApplySoftwareRadioTransaction<T>(
        IReadOnlyList<T> targets,
        bool enabled,
        Func<T, int> softwareState,
        Func<T, int> hardwareState,
        Func<T, int, NativeWifiRadioSetResult> apply)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(softwareState);
        ArgumentNullException.ThrowIfNull(hardwareState);
        ArgumentNullException.ThrowIfNull(apply);
        if (targets.Count == 0) return NativeWifiRadioSetResult.Unavailable;

        var originals = new (T Target, int Software, int Hardware)[targets.Count];
        for (var index = 0; index < targets.Count; index++)
        {
            var software = softwareState(targets[index]);
            var hardware = hardwareState(targets[index]);
            // Compensation must never write an unknown original state. Reject
            // the whole transaction before its first mutation instead.
            if (software is not Dot11RadioStateOn and not Dot11RadioStateOff ||
                hardware is not Dot11RadioStateOn and not Dot11RadioStateOff)
                return NativeWifiRadioSetResult.Unavailable;
            originals[index] = (targets[index], software, hardware);
        }

        if (enabled && originals.All(target => target.Hardware == Dot11RadioStateOff))
            return NativeWifiRadioSetResult.HardwareDisabled;

        var desired = enabled ? Dot11RadioStateOn : Dot11RadioStateOff;
        var changed = new List<(T Target, int Software)>();
        foreach (var target in originals)
        {
            if (target.Software == desired) continue;
            var result = apply(target.Target, desired);
            if (result == NativeWifiRadioSetResult.Succeeded)
            {
                changed.Add((target.Target, target.Software));
                continue;
            }

            var restored = true;
            for (var index = changed.Count - 1; index >= 0; index--)
                restored &= apply(changed[index].Target, changed[index].Software) ==
                    NativeWifiRadioSetResult.Succeeded;
            return restored ? result : NativeWifiRadioSetResult.PartialFailure;
        }
        return NativeWifiRadioSetResult.Succeeded;
    }

    private NativeWifiRadioSetResult SetSoftwareRadioState(
        Guid interfaceId, uint phyIndex, int softwareState)
    {
        var state = new WlanPhyRadioState
        {
            PhyIndex = phyIndex,
            SoftwareRadioState = softwareState,
            HardwareRadioState = Dot11RadioStateUnknown,
        };
        var id = interfaceId;
        var result = NativeMethods.WlanSetInterface(
            _wlanHandle, ref id, WlanIntfOpcodeRadioState,
            (uint)Marshal.SizeOf<WlanPhyRadioState>(), ref state, IntPtr.Zero);
        if (result == ErrorAccessDenied) return NativeWifiRadioSetResult.PolicyDenied;
        return result == ErrorSuccess
            ? NativeWifiRadioSetResult.Succeeded
            : NativeWifiRadioSetResult.Unavailable;
    }

    private IReadOnlyList<PhyRadioState> QueryPhyRadioStates(Guid interfaceId)
    {
        var id = interfaceId;
        var result = NativeMethods.WlanQueryInterface(
            _wlanHandle, ref id, WlanIntfOpcodeRadioState, IntPtr.Zero,
            out var dataSize, out var data, IntPtr.Zero);
        if (result != ErrorSuccess || data == IntPtr.Zero) return [];
        try
        {
            if (dataSize < sizeof(uint)) return [];
            var count = Math.Min(Marshal.ReadInt32(data), 64);
            if (count <= 0 || dataSize < 4u + (uint)count * 12u) return [];
            var states = new List<PhyRadioState>(count);
            for (var index = 0; index < count; index++)
            {
                var offset = sizeof(uint) + index * 12;
                states.Add(new PhyRadioState(
                    checked((uint)Marshal.ReadInt32(data, offset)),
                    Marshal.ReadInt32(data, offset + 4),
                    Marshal.ReadInt32(data, offset + 8)));
            }
            return states;
        }
        finally { NativeMethods.WlanFreeMemory(data); }
    }

    private IReadOnlyList<WirelessInterface> EnumerateWirelessInterfaces()
    {
        var result = NativeMethods.WlanEnumInterfaces(_wlanHandle, IntPtr.Zero, out var listPointer);
        if (result != ErrorSuccess || listPointer == IntPtr.Zero) return [];
        try
        {
            var count = Math.Min(Marshal.ReadInt32(listPointer), MaximumInterfaces);
            var offset = 8;
            var size = Marshal.SizeOf<WlanInterfaceInfo>();
            var resultList = new List<WirelessInterface>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanInterfaceInfo>(listPointer + offset + index * size);
                resultList.Add(new WirelessInterface(item.InterfaceGuid, item.State));
            }
            return resultList;
        }
        finally { NativeMethods.WlanFreeMemory(listPointer); }
    }

    private IReadOnlyList<string> EnumerateProfiles(Guid interfaceId)
    {
        var id = interfaceId;
        var result = NativeMethods.WlanGetProfileList(_wlanHandle, ref id, IntPtr.Zero, out var listPointer);
        if (result != ErrorSuccess || listPointer == IntPtr.Zero) return [];
        try
        {
            var count = Math.Min(Marshal.ReadInt32(listPointer), MaximumProfilesPerInterface);
            var offset = 8;
            var size = Marshal.SizeOf<WlanProfileInfo>();
            var profiles = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanProfileInfo>(listPointer + offset + index * size);
                if (!string.IsNullOrEmpty(item.ProfileName)) profiles.Add(item.ProfileName);
            }
            return profiles;
        }
        finally { NativeMethods.WlanFreeMemory(listPointer); }
    }

    private AvailableNetworkReadResult EnumerateAvailableNetworks(Guid interfaceId)
    {
        var id = interfaceId;
        var result = NativeMethods.WlanGetAvailableNetworkList(
            _wlanHandle, ref id, 0, IntPtr.Zero, out var listPointer);
        if (result == ErrorAccessDenied)
            return new AvailableNetworkReadResult(false, true, []);
        if (result != ErrorSuccess || listPointer == IntPtr.Zero)
            return new AvailableNetworkReadResult(false, false, []);
        try
        {
            var count = Math.Min(Marshal.ReadInt32(listPointer), MaximumAvailableNetworks);
            var offset = 8;
            var size = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<AvailableNetworkData>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanAvailableNetwork>(
                    listPointer + offset + index * size);
                var ssidLength = checked((int)Math.Min(item.Dot11Ssid.SsidLength, 32u));
                var source = item.Dot11Ssid.Ssid ?? [];
                if (ssidLength > source.Length) continue;
                var ssid = source.AsSpan(0, ssidLength).ToArray();
                networks.Add(new AvailableNetworkData(
                    item.ProfileName ?? string.Empty,
                    ssid,
                    item.BssType,
                    item.NetworkConnectable != 0,
                    item.SignalQuality,
                    item.SecurityEnabled != 0,
                    item.DefaultAuthenticationAlgorithm,
                    item.DefaultCipherAlgorithm,
                    item.Flags));
            }
            return new AvailableNetworkReadResult(true, false, networks);
        }
        finally { NativeMethods.WlanFreeMemory(listPointer); }
    }

    private NativeWifiScanStartResult SetScanStartFailure(
        NativeWifiScanState state,
        NativeWifiScanStartResult result)
    {
        lock (_scanGate)
        {
            _pendingScanInterfaces.Clear();
            _connectableNetworks.Clear();
            _cachedAvailableGeneration = -1;
            _cachedAvailableNetworks = [];
            _scanState = state;
        }
        return result;
    }

    private static string AvailableNetworkDeduplicationKey(
        Guid interfaceId,
        byte[] ssid,
        uint authentication,
        uint cipher)
    {
        var material = new byte[16 + ssid.Length + 8];
        interfaceId.TryWriteBytes(material);
        ssid.CopyTo(material, 16);
        BitConverter.TryWriteBytes(material.AsSpan(16 + ssid.Length, 4), authentication);
        BitConverter.TryWriteBytes(material.AsSpan(20 + ssid.Length, 4), cipher);
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string DecodeSsid(byte[] ssid)
    {
        if (ssid.Length == 0) return "Hidden network";
        var value = Encoding.UTF8.GetString(ssid).Trim();
        return string.IsNullOrWhiteSpace(value) ? "Hidden network" : value;
    }

    private static WifiSecurityKind ClassifySecurity(bool enabled, uint authentication)
    {
        if (!enabled) return WifiSecurityKind.Open;
        return authentication switch
        {
            4 or 7 or 10 or 11 => WifiSecurityKind.Personal,
            3 or 6 or 8 or 12 or 13 or 14 => WifiSecurityKind.Enterprise,
            _ => WifiSecurityKind.Unknown,
        };
    }

    private static Dot11Ssid ToNativeSsid(byte[] ssid)
    {
        var bytes = new byte[32];
        ssid.AsSpan(0, Math.Min(ssid.Length, bytes.Length)).CopyTo(bytes);
        return new Dot11Ssid
        {
            SsidLength = checked((uint)Math.Min(ssid.Length, bytes.Length)),
            Ssid = bytes,
        };
    }

    private void OnIpInterfaceChanged(IntPtr context, IntPtr row, int notificationType) =>
        RaiseChanged(null);

    private void OnConnectivityHintChanged(IntPtr context, NetworkConnectivityHint hint) =>
        RaiseChanged(null);

    private void OnWlanNotification(ref WlanNotificationData data, IntPtr context)
    {
        if (data.NotificationSource == WlanNotificationSourceAcm &&
            data.NotificationCode is WlanNotificationAcmScanComplete or WlanNotificationAcmScanFail)
        {
            NativeWifiScanOutcome? scanOutcome = null;
            lock (_scanGate)
            {
                if (_scanState == NativeWifiScanState.Scanning &&
                    _pendingScanInterfaces.Remove(data.InterfaceGuid))
                {
                    _scanHadSuccess |= data.NotificationCode == WlanNotificationAcmScanComplete;
                    if (_pendingScanInterfaces.Count == 0)
                    {
                        _scanState = _scanHadSuccess
                            ? NativeWifiScanState.Ready
                            : NativeWifiScanState.Unavailable;
                        _scanGeneration++;
                        scanOutcome = _scanHadSuccess
                            ? NativeWifiScanOutcome.Completed
                            : NativeWifiScanOutcome.Failed;
                    }
                }
            }
            if (scanOutcome is not null)
            {
                RaiseChanged(null, scanOutcome);
                return;
            }
        }

        NativeNetworkConnectionOutcome? outcome = null;
        if (data.NotificationSource == WlanNotificationSourceAcm &&
            (data.NotificationCode is WlanNotificationAcmConnectionComplete or
                WlanNotificationAcmConnectionAttemptFail) &&
            data.DataPointer != IntPtr.Zero && data.DataSize >= 516)
        {
            var profileName = Marshal.PtrToStringUni(data.DataPointer + 4, 256)?.TrimEnd('\0');
            var notificationSsid = data.DataSize >= 516 + Marshal.SizeOf<Dot11Ssid>()
                ? ReadSsid(Marshal.PtrToStructure<Dot11Ssid>(data.DataPointer + 516))
                : [];
            PendingNativeConnection? matchedPending = null;
            lock (_scanGate)
            {
                if (_pendingConnection is { } pending && ConnectionNotificationMatches(
                        pending.InterfaceId,
                        data.InterfaceGuid,
                        pending.ProfileName,
                        pending.Ssid,
                        profileName,
                        notificationSsid))
                {
                    matchedPending = pending;
                    _pendingConnection = null;
                }
                if (matchedPending is not null &&
                    data.NotificationCode == WlanNotificationAcmConnectionComplete)
                    _cachedAvailableNetworks = _cachedAvailableNetworks
                        .Select(network => network with
                        {
                            IsConnected = string.Equals(
                                network.NativeNetworkKey,
                                matchedPending.NativeKey,
                                StringComparison.Ordinal),
                        })
                        .ToArray();
            }
            if (matchedPending is not null || !string.IsNullOrEmpty(profileName))
            {
                outcome = new NativeNetworkConnectionOutcome(
                    matchedPending?.NativeKey ?? NativeKey(data.InterfaceGuid, profileName!),
                    data.NotificationCode == WlanNotificationAcmConnectionComplete
                        ? NativeNetworkConnectionResult.Succeeded
                        : NativeNetworkConnectionResult.Failed);
            }
        }
        RaiseChanged(outcome, null);
    }

    private void RaiseChanged(
        NativeNetworkConnectionOutcome? outcome,
        NativeWifiScanOutcome? scanOutcome = null)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        StateChanged?.Invoke(this, new NativeNetworkStateChangedEventArgs(Generation)
        {
            ConnectionOutcome = outcome,
            WifiScanOutcome = scanOutcome,
        });
    }

    internal static bool ShouldExposeAvailableNetwork(
        bool isConnectable,
        bool isConnected,
        bool hasSavedProfile,
        int ssidLength) =>
        (isConnectable || isConnected) && (ssidLength > 0 || hasSavedProfile);

    internal static bool ConnectionNotificationMatches(
        Guid expectedInterface,
        Guid actualInterface,
        string? expectedProfile,
        byte[]? expectedSsid,
        string? actualProfile,
        byte[]? actualSsid)
    {
        if (expectedInterface != actualInterface) return false;
        if (!string.IsNullOrEmpty(expectedProfile))
            return string.Equals(expectedProfile, actualProfile, StringComparison.Ordinal);
        return expectedSsid is { Length: > 0 } && actualSsid is not null &&
            expectedSsid.AsSpan().SequenceEqual(actualSsid);
    }

    private static byte[] ReadSsid(Dot11Ssid value)
    {
        var source = value.Ssid ?? [];
        var length = checked((int)Math.Min(value.SsidLength, (uint)source.Length));
        return source.AsSpan(0, length).ToArray();
    }

    private static string NativeKey(Guid interfaceId, string profileName) =>
        $"{interfaceId:N}|{profileName}";

    private static bool IsEthernet(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or
        NetworkInterfaceType.Ethernet3Megabit or
        NetworkInterfaceType.FastEthernetFx or
        NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.GigabitEthernet;

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsNetworkNativeAdapter));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ipNotificationHandle != IntPtr.Zero)
        {
            _ = NativeMethods.CancelMibChangeNotify2(_ipNotificationHandle);
            _ipNotificationHandle = IntPtr.Zero;
        }
        if (_connectivityNotificationHandle != IntPtr.Zero)
        {
            _ = NativeMethods.CancelMibChangeNotify2(_connectivityNotificationHandle);
            _connectivityNotificationHandle = IntPtr.Zero;
        }
        if (_wlanHandle != IntPtr.Zero)
        {
            if (_wlanNotificationsRegistered)
                _ = NativeMethods.WlanRegisterNotification(
                    _wlanHandle,
                    WlanNotificationSourceNone,
                    true,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out _);
            _ = NativeMethods.WlanCloseHandle(_wlanHandle, IntPtr.Zero);
            _wlanHandle = IntPtr.Zero;
        }
        _connectableProfiles.Clear();
        lock (_scanGate)
        {
            _connectableNetworks.Clear();
            _pendingScanInterfaces.Clear();
            _pendingConnection = null;
            _cachedAvailableGeneration = -1;
            _cachedAvailableNetworks = [];
        }
    }

    private sealed record NativeProfileTarget(Guid InterfaceId, string ProfileName);
    private sealed record PendingNativeConnection(
        string NativeKey,
        Guid InterfaceId,
        string? ProfileName,
        byte[] Ssid);
    private sealed record NativeAvailableNetworkTarget(
        Guid InterfaceId,
        byte[] Ssid,
        int BssType,
        string? ProfileName,
        WifiSecurityKind Security,
        bool CredentialRequired);
    private sealed record AvailableNetworkData(
        string ProfileName,
        byte[] Ssid,
        int BssType,
        bool IsConnectable,
        uint SignalQuality,
        bool SecurityEnabled,
        uint AuthenticationAlgorithm,
        uint CipherAlgorithm,
        uint Flags);
    private sealed record AvailableNetworkReadResult(
        bool Succeeded,
        bool AccessDenied,
        IReadOnlyList<AvailableNetworkData> Networks);
    private sealed record WirelessInterface(Guid InterfaceId, int State);
    private sealed record PhyRadioState(uint PhyIndex, int SoftwareState, int HardwareState);
    private sealed record ManagedInterfaceState(
        bool HasWireless,
        bool WirelessUp,
        NativeNetworkMedium DefaultMedium);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void NativeWifiNotificationCallback(
        ref WlanNotificationData data,
        IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void IpInterfaceChangeCallback(
        IntPtr context,
        IntPtr row,
        int notificationType);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void NetworkConnectivityHintChangeCallback(
        IntPtr context,
        NetworkConnectivityHint hint);

    private static class NativeMethods
    {
        [DllImport("wlanapi.dll")]
        internal static extern uint WlanOpenHandle(
            uint clientVersion,
            IntPtr reserved,
            out uint negotiatedVersion,
            out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanEnumInterfaces(
            IntPtr clientHandle,
            IntPtr reserved,
            out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanGetProfileList(
            IntPtr clientHandle,
            ref Guid interfaceId,
            IntPtr reserved,
            out IntPtr profileList);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanScan(
            IntPtr clientHandle,
            ref Guid interfaceId,
            IntPtr dot11Ssid,
            IntPtr informationElements,
            IntPtr reserved);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanGetAvailableNetworkList(
            IntPtr clientHandle,
            ref Guid interfaceId,
            uint flags,
            IntPtr reserved,
            out IntPtr availableNetworkList);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanQueryInterface(
            IntPtr clientHandle,
            ref Guid interfaceId,
            int opcode,
            IntPtr reserved,
            out uint dataSize,
            out IntPtr data,
            IntPtr opcodeValueType);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanSetInterface(
            IntPtr clientHandle,
            ref Guid interfaceId,
            int opcode,
            uint dataSize,
            ref WlanPhyRadioState data,
            IntPtr reserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint WlanConnect(
            IntPtr clientHandle,
            ref Guid interfaceId,
            ref WlanConnectionParameters connectionParameters,
            IntPtr reserved);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanRegisterNotification(
            IntPtr clientHandle,
            uint notificationSource,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate,
            NativeWifiNotificationCallback? callback,
            IntPtr callbackContext,
            IntPtr reserved,
            out uint previousNotificationSource);

        [DllImport("wlanapi.dll")]
        internal static extern void WlanFreeMemory(IntPtr memory);

        [DllImport("iphlpapi.dll")]
        internal static extern uint NotifyIpInterfaceChange(
            ushort family,
            IpInterfaceChangeCallback callback,
            IntPtr callerContext,
            [MarshalAs(UnmanagedType.U1)] bool initialNotification,
            out IntPtr notificationHandle);

        [DllImport("iphlpapi.dll")]
        internal static extern uint GetNetworkConnectivityHint(
            out NetworkConnectivityHint connectivityHint);

        [DllImport("iphlpapi.dll")]
        internal static extern uint NotifyNetworkConnectivityHintChange(
            NetworkConnectivityHintChangeCallback callback,
            IntPtr callerContext,
            [MarshalAs(UnmanagedType.U1)] bool initialNotification,
            out IntPtr notificationHandle);

        [DllImport("iphlpapi.dll")]
        internal static extern uint CancelMibChangeNotify2(IntPtr notificationHandle);

        [DllImport("iphlpapi.dll")]
        internal static extern uint GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);
    }
}

internal enum NetworkConnectivityLevelHint
{
    Unknown = 0,
    None = 1,
    LocalAccess = 2,
    InternetAccess = 3,
    ConstrainedInternetAccess = 4,
    Hidden = 5,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetworkConnectivityHint
{
    internal NetworkConnectivityLevelHint ConnectivityLevel;
    internal int ConnectivityCost;
    internal byte ApproachingDataLimit;
    internal byte OverDataLimit;
    internal byte Roaming;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanInterfaceInfo
{
    internal Guid InterfaceGuid;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Description;
    internal int State;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanProfileInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string ProfileName;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanConnectionParameters
{
    internal int ConnectionMode;
    [MarshalAs(UnmanagedType.LPWStr)] internal string? Profile;
    internal IntPtr Dot11Ssid;
    internal IntPtr DesiredBssidList;
    internal int Dot11BssType;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Dot11Ssid
{
    internal uint SsidLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Ssid;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanAvailableNetwork
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string ProfileName;
    internal Dot11Ssid Dot11Ssid;
    internal int BssType;
    internal uint NumberOfBssids;
    internal int NetworkConnectable;
    internal uint NotConnectableReason;
    internal uint NumberOfPhyTypes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] internal int[] PhyTypes;
    internal int MorePhyTypes;
    internal uint SignalQuality;
    internal int SecurityEnabled;
    internal uint DefaultAuthenticationAlgorithm;
    internal uint DefaultCipherAlgorithm;
    internal uint Flags;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanNotificationData
{
    internal uint NotificationSource;
    internal uint NotificationCode;
    internal Guid InterfaceGuid;
    internal uint DataSize;
    internal IntPtr DataPointer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanPhyRadioState
{
    internal uint PhyIndex;
    internal int SoftwareRadioState;
    internal int HardwareRadioState;
}
