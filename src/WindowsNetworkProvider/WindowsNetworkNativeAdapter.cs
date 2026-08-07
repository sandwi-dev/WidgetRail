using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

public sealed class WindowsNetworkNativeAdapterFactory : IWindowsNetworkNativeAdapterFactory
{
    public IWindowsNetworkNativeAdapter Create(long generation) =>
        new WindowsNetworkNativeAdapter(generation);
}

/// <summary>
/// Windows desktop adapter using Native Wi-Fi (wlanapi) and IP Helper connectivity/interface
/// notifications. It never calls WlanGetProfile, scans, or returns native identities.
/// </summary>
internal sealed class WindowsNetworkNativeAdapter : IWindowsNetworkNativeAdapter
{
    private const uint ErrorSuccess = 0;
    private const uint WlanNotificationSourceNone = 0;
    private const uint WlanNotificationSourceAcm = 0x00000008;
    private const uint WlanNotificationAcmConnectionComplete = 10;
    private const uint WlanNotificationAcmConnectionAttemptFail = 11;
    private const int WlanIntfOpcodeRadioState = 4;
    private const int WlanConnectionModeProfile = 0;
    private const int Dot11BssTypeAny = 3;
    private const int MaximumInterfaces = 32;
    private const int MaximumProfilesPerInterface = 128;
    private readonly NativeWifiNotificationCallback _wlanCallback;
    private readonly IpInterfaceChangeCallback _ipCallback;
    private readonly NetworkConnectivityHintChangeCallback _connectivityCallback;
    private readonly Dictionary<string, NativeProfileTarget> _connectableProfiles =
        new(StringComparer.Ordinal);
    private IntPtr _wlanHandle;
    private IntPtr _ipNotificationHandle;
    private IntPtr _connectivityNotificationHandle;
    private int _disposed;
    private int _degraded;
    private bool _wlanServiceAvailable;
    private bool _wlanNotificationsRegistered;
    private bool _connectivityNotificationsSupported = true;

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
        if (result == ErrorSuccess) return true;
        Volatile.Write(ref _degraded, 1);
        throw new Win32Exception((int)result, "Windows could not start the saved Wi-Fi connection.");
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

    private void OnIpInterfaceChanged(IntPtr context, IntPtr row, int notificationType) =>
        RaiseChanged(null);

    private void OnConnectivityHintChanged(IntPtr context, NetworkConnectivityHint hint) =>
        RaiseChanged(null);

    private void OnWlanNotification(ref WlanNotificationData data, IntPtr context)
    {
        NativeNetworkConnectionOutcome? outcome = null;
        if (data.NotificationSource == WlanNotificationSourceAcm &&
            data.NotificationCode is WlanNotificationAcmConnectionComplete or
                WlanNotificationAcmConnectionAttemptFail &&
            data.DataPointer != IntPtr.Zero && data.DataSize >= 516)
        {
            var profileName = Marshal.PtrToStringUni(data.DataPointer + 4, 256)?.TrimEnd('\0');
            if (!string.IsNullOrEmpty(profileName))
            {
                outcome = new NativeNetworkConnectionOutcome(
                    NativeKey(data.InterfaceGuid, profileName),
                    data.NotificationCode == WlanNotificationAcmConnectionComplete
                        ? NativeNetworkConnectionResult.Succeeded
                        : NativeNetworkConnectionResult.Failed);
            }
        }
        RaiseChanged(outcome);
    }

    private void RaiseChanged(NativeNetworkConnectionOutcome? outcome)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        StateChanged?.Invoke(this, new NativeNetworkStateChangedEventArgs(Generation)
        {
            ConnectionOutcome = outcome,
        });
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
    }

    private sealed record NativeProfileTarget(Guid InterfaceId, string ProfileName);
    private sealed record WirelessInterface(Guid InterfaceId, int State);
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
        internal static extern uint WlanQueryInterface(
            IntPtr clientHandle,
            ref Guid interfaceId,
            int opcode,
            IntPtr reserved,
            out uint dataSize,
            out IntPtr data,
            IntPtr opcodeValueType);

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
    [MarshalAs(UnmanagedType.LPWStr)] internal string Profile;
    internal IntPtr Dot11Ssid;
    internal IntPtr DesiredBssidList;
    internal int Dot11BssType;
    internal uint Flags;
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
