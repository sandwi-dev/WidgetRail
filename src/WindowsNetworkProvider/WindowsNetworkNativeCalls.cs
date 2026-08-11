using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace GameBarAlternative.WindowsNetworkProvider;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeWifiNotificationCallback(
    ref WlanNotificationData data,
    IntPtr context);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void IpInterfaceChangeCallback(
    IntPtr context,
    IntPtr row,
    int notificationType);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NetworkConnectivityHintChangeCallback(
    IntPtr context,
    NetworkConnectivityHint hint);

internal sealed record ManagedNetworkInterfaceData(
    NetworkInterfaceType Type,
    OperationalStatus Status,
    bool HasGateway,
    int? Ipv4Index);

internal sealed record ManagedNetworkConnectionInterfaceData(
    NetworkInterfaceType Type,
    OperationalStatus Status,
    int? Ipv4Index,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> DefaultGateways,
    IReadOnlyList<string> DnsServers);

internal sealed record WlanConnectRequest(
    int ConnectionMode,
    string? Profile,
    byte[]? Ssid,
    int BssType);

/// <summary>
/// The adapter's single injected boundary around consentless Windows networking calls.
/// It owns no handle, callback registration, generation, or retained native allocation.
/// </summary>
internal interface IWindowsNetworkNativeCalls
{
    uint OpenWlan(out IntPtr handle);
    uint CloseWlan(IntPtr handle);
    uint RegisterWlanNotification(
        IntPtr handle,
        uint source,
        NativeWifiNotificationCallback? callback);
    uint EnumerateWlanInterfaces(IntPtr handle, out IntPtr list);
    uint EnumerateWlanProfiles(IntPtr handle, Guid interfaceId, out IntPtr list);
    uint StartWlanScan(IntPtr handle, Guid interfaceId);
    uint EnumerateAvailableNetworks(IntPtr handle, Guid interfaceId, out IntPtr list);
    uint QueryRadio(IntPtr handle, Guid interfaceId, out uint size, out IntPtr data);
    uint SetRadio(IntPtr handle, Guid interfaceId, WlanPhyRadioState state);
    uint ConnectWlan(IntPtr handle, Guid interfaceId, WlanConnectRequest request);
    uint SetWlanProfile(
        IntPtr handle,
        Guid interfaceId,
        char[] profileXml,
        out uint reasonCode)
    {
        reasonCode = 0;
        return 50;
    }
    uint DeleteWlanProfile(IntPtr handle, Guid interfaceId, string profileName) => 50;
    uint SetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        byte[] data) => 50;
    uint GetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        out byte[] data)
    {
        data = [];
        return 50;
    }
    void FreeWlanMemory(IntPtr memory);

    uint RegisterIpInterfaceChange(IpInterfaceChangeCallback callback, out IntPtr handle);
    uint RegisterConnectivityHintChange(
        NetworkConnectivityHintChangeCallback callback,
        out IntPtr handle);
    uint CancelChangeNotification(IntPtr handle);
    uint ReadConnectivityHint(out NetworkConnectivityHint hint);
    uint ReadBestInterface(uint destinationAddress, out uint interfaceIndex);
    bool IsNetworkAvailable();
    IReadOnlyList<ManagedNetworkInterfaceData> ReadManagedInterfaces();
    IReadOnlyList<ManagedNetworkConnectionInterfaceData> ReadConnectionInterfaces() => [];
}

internal sealed class WindowsNetworkNativeCalls : IWindowsNetworkNativeCalls
{
    private const int MaximumManagedInterfaces = 256;
    public static WindowsNetworkNativeCalls Instance { get; } = new();

    private WindowsNetworkNativeCalls()
    {
    }

    public uint OpenWlan(out IntPtr handle) =>
        NativeMethods.WlanOpenHandle(2, IntPtr.Zero, out _, out handle);

    public uint CloseWlan(IntPtr handle) => NativeMethods.WlanCloseHandle(handle, IntPtr.Zero);

    public uint RegisterWlanNotification(
        IntPtr handle,
        uint source,
        NativeWifiNotificationCallback? callback) =>
        NativeMethods.WlanRegisterNotification(
            handle,
            source,
            true,
            callback,
            IntPtr.Zero,
            IntPtr.Zero,
            out _);

    public uint EnumerateWlanInterfaces(IntPtr handle, out IntPtr list) =>
        NativeMethods.WlanEnumInterfaces(handle, IntPtr.Zero, out list);

    public uint EnumerateWlanProfiles(IntPtr handle, Guid interfaceId, out IntPtr list) =>
        NativeMethods.WlanGetProfileList(handle, ref interfaceId, IntPtr.Zero, out list);

    public uint StartWlanScan(IntPtr handle, Guid interfaceId) =>
        NativeMethods.WlanScan(
            handle,
            ref interfaceId,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

    public uint EnumerateAvailableNetworks(
        IntPtr handle,
        Guid interfaceId,
        out IntPtr list) =>
        NativeMethods.WlanGetAvailableNetworkList(
            handle,
            ref interfaceId,
            0,
            IntPtr.Zero,
            out list);

    public uint QueryRadio(IntPtr handle, Guid interfaceId, out uint size, out IntPtr data) =>
        NativeMethods.WlanQueryInterface(
            handle,
            ref interfaceId,
            4,
            IntPtr.Zero,
            out size,
            out data,
            IntPtr.Zero);

    public uint SetRadio(IntPtr handle, Guid interfaceId, WlanPhyRadioState state) =>
        NativeMethods.WlanSetInterface(
            handle,
            ref interfaceId,
            4,
            (uint)Marshal.SizeOf<WlanPhyRadioState>(),
            ref state,
            IntPtr.Zero);

    public uint ConnectWlan(IntPtr handle, Guid interfaceId, WlanConnectRequest request)
    {
        var ssidPointer = IntPtr.Zero;
        try
        {
            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = request.ConnectionMode,
                Profile = request.Profile,
                Dot11Ssid = IntPtr.Zero,
                DesiredBssidList = IntPtr.Zero,
                Dot11BssType = request.BssType,
                Flags = 0,
            };
            if (request.Ssid is not null)
            {
                ssidPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Dot11Ssid>());
                Marshal.StructureToPtr(ToNativeSsid(request.Ssid), ssidPointer, false);
                parameters.Dot11Ssid = ssidPointer;
            }
            return NativeMethods.WlanConnect(
                handle,
                ref interfaceId,
                ref parameters,
                IntPtr.Zero);
        }
        finally
        {
            if (ssidPointer != IntPtr.Zero) Marshal.FreeHGlobal(ssidPointer);
        }
    }

    public uint SetWlanProfile(
        IntPtr handle,
        Guid interfaceId,
        char[] profileXml,
        out uint reasonCode)
    {
        ArgumentNullException.ThrowIfNull(profileXml);
        var pinned = GCHandle.Alloc(profileXml, GCHandleType.Pinned);
        try
        {
            return NativeMethods.WlanSetProfile(
                handle,
                ref interfaceId,
                0x00000002,
                pinned.AddrOfPinnedObject(),
                IntPtr.Zero,
                false,
                IntPtr.Zero,
                out reasonCode);
        }
        finally
        {
            pinned.Free();
        }
    }

    public uint DeleteWlanProfile(IntPtr handle, Guid interfaceId, string profileName) =>
        NativeMethods.WlanDeleteProfile(handle, ref interfaceId, profileName, IntPtr.Zero);

    public uint SetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return NativeMethods.WlanSetProfileCustomUserData(
                handle,
                ref interfaceId,
                profileName,
                0,
                IntPtr.Zero,
                IntPtr.Zero);
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return NativeMethods.WlanSetProfileCustomUserData(
                handle,
                ref interfaceId,
                profileName,
                checked((uint)data.Length),
                pinned.AddrOfPinnedObject(),
                IntPtr.Zero);
        }
        finally
        {
            pinned.Free();
        }
    }

    public uint GetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        out byte[] data)
    {
        data = [];
        var result = NativeMethods.WlanGetProfileCustomUserData(
            handle,
            ref interfaceId,
            profileName,
            out var size,
            out var pointer,
            IntPtr.Zero);
        if (result != 0 || pointer == IntPtr.Zero) return result;
        try
        {
            if (size is 0 or > 256) return 13;
            data = new byte[checked((int)size)];
            Marshal.Copy(pointer, data, 0, data.Length);
            return 0;
        }
        finally
        {
            NativeMethods.WlanFreeMemory(pointer);
        }
    }

    public void FreeWlanMemory(IntPtr memory) => NativeMethods.WlanFreeMemory(memory);

    public uint RegisterIpInterfaceChange(IpInterfaceChangeCallback callback, out IntPtr handle) =>
        NativeMethods.NotifyIpInterfaceChange(0, callback, IntPtr.Zero, false, out handle);

    public uint RegisterConnectivityHintChange(
        NetworkConnectivityHintChangeCallback callback,
        out IntPtr handle) =>
        NativeMethods.NotifyNetworkConnectivityHintChange(
            callback,
            IntPtr.Zero,
            false,
            out handle);

    public uint CancelChangeNotification(IntPtr handle) =>
        NativeMethods.CancelMibChangeNotify2(handle);

    public uint ReadConnectivityHint(out NetworkConnectivityHint hint) =>
        NativeMethods.GetNetworkConnectivityHint(out hint);

    public uint ReadBestInterface(uint destinationAddress, out uint interfaceIndex) =>
        NativeMethods.GetBestInterface(destinationAddress, out interfaceIndex);

    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

    public IReadOnlyList<ManagedNetworkInterfaceData> ReadManagedInterfaces()
    {
        var values = new List<ManagedNetworkInterfaceData>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (values.Count >= MaximumManagedInterfaces) break;
                var hasGateway = false;
                int? ipv4Index = null;
                if (adapter.OperationalStatus == OperationalStatus.Up)
                {
                    try
                    {
                        var properties = adapter.GetIPProperties();
                        hasGateway = properties.GatewayAddresses.Count != 0;
                        ipv4Index = properties.GetIPv4Properties()?.Index;
                    }
                    catch (NetworkInformationException)
                    {
                    }
                }
                values.Add(new(
                    adapter.NetworkInterfaceType,
                    adapter.OperationalStatus,
                    hasGateway,
                    ipv4Index));
            }
        }
        catch (NetworkInformationException)
        {
        }
        return values;
    }

    public IReadOnlyList<ManagedNetworkConnectionInterfaceData> ReadConnectionInterfaces()
    {
        var values = new List<ManagedNetworkConnectionInterfaceData>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (values.Count >= MaximumManagedInterfaces) break;
                var addresses = new List<string>();
                var gateways = new List<string>();
                var dns = new List<string>();
                int? ipv4Index = null;
                if (adapter.OperationalStatus == OperationalStatus.Up)
                {
                    try
                    {
                        var properties = adapter.GetIPProperties();
                        ipv4Index = properties.GetIPv4Properties()?.Index;
                        foreach (var item in properties.UnicastAddresses)
                        {
                            var address = item.Address;
                            if (address.IsIPv6LinkLocal || System.Net.IPAddress.IsLoopback(address) ||
                                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                address.GetAddressBytes() is [169, 254, ..] ||
                                OperatingSystem.IsWindows() &&
                                item.SuffixOrigin == SuffixOrigin.Random)
                                continue;
                            if (addresses.Count < 8) addresses.Add(address.ToString());
                        }
                        foreach (var item in properties.GatewayAddresses)
                            if (gateways.Count < 4 && !item.Address.Equals(System.Net.IPAddress.Any) &&
                                !item.Address.Equals(System.Net.IPAddress.IPv6Any))
                                gateways.Add(item.Address.ToString());
                        foreach (var address in properties.DnsAddresses)
                            if (dns.Count < 8 && !System.Net.IPAddress.IsLoopback(address) &&
                                !address.IsIPv6LinkLocal)
                                dns.Add(address.ToString());
                    }
                    catch (NetworkInformationException)
                    {
                    }
                }
                values.Add(new(
                    adapter.NetworkInterfaceType,
                    adapter.OperationalStatus,
                    ipv4Index,
                    addresses.Distinct(StringComparer.Ordinal).ToArray(),
                    gateways.Distinct(StringComparer.Ordinal).ToArray(),
                    dns.Distinct(StringComparer.Ordinal).ToArray()));
            }
        }
        catch (NetworkInformationException)
        {
        }
        return values;
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

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint WlanSetProfile(
            IntPtr clientHandle,
            ref Guid interfaceId,
            uint flags,
            IntPtr profileXml,
            IntPtr allUserProfileSecurity,
            [MarshalAs(UnmanagedType.Bool)] bool overwrite,
            IntPtr reserved,
            out uint reasonCode);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint WlanDeleteProfile(
            IntPtr clientHandle,
            ref Guid interfaceId,
            string profileName,
            IntPtr reserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint WlanSetProfileCustomUserData(
            IntPtr clientHandle,
            ref Guid interfaceId,
            string profileName,
            uint dataSize,
            IntPtr data,
            IntPtr reserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        internal static extern uint WlanGetProfileCustomUserData(
            IntPtr clientHandle,
            ref Guid interfaceId,
            string profileName,
            out uint dataSize,
            out IntPtr data,
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
        internal static extern uint GetBestInterface(
            uint destinationAddress,
            out uint bestInterfaceIndex);
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
