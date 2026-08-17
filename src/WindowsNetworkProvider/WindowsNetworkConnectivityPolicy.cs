using System.Net.NetworkInformation;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

internal readonly record struct ManagedInterfaceState(
    bool HasWireless,
    bool WirelessUp,
    NativeNetworkMedium DefaultMedium);

internal readonly record struct NativeConnectivityProjection(
    NetworkConnectivity Connectivity,
    ManagedInterfaceState Interfaces);

/// <summary>Value projection over connectivity hints and bounded managed interface facts.</summary>
internal static class WindowsNetworkConnectivityPolicy
{
    private const uint ErrorSuccess = 0;

    public static NativeConnectivityProjection Read(IWindowsNetworkNativeCalls calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        NetworkConnectivity connectivity;
        try
        {
            connectivity = calls.ReadConnectivityHint(out var hint) == ErrorSuccess
                ? ProjectConnectivityHint(hint.ConnectivityLevel, calls.IsNetworkAvailable())
                : calls.IsNetworkAvailable()
                    ? NetworkConnectivity.Local
                    : NetworkConnectivity.None;
        }
        catch (EntryPointNotFoundException)
        {
            connectivity = calls.IsNetworkAvailable()
                ? NetworkConnectivity.Local
                : NetworkConnectivity.None;
        }

        var destination = BitConverter.ToUInt32([1, 1, 1, 1], 0);
        var bestIndex = calls.ReadBestInterface(destination, out var index) == ErrorSuccess
            ? checked((int)index)
            : (int?)null;
        return new(connectivity, ProjectInterfaces(calls.ReadManagedInterfaces(), bestIndex));
    }

    internal static NetworkConnectivity ProjectConnectivityHint(
        NetworkConnectivityLevelHint hint,
        bool managedAvailability) => hint switch
        {
            NetworkConnectivityLevelHint.InternetAccess => NetworkConnectivity.Internet,
            NetworkConnectivityLevelHint.LocalAccess or
                NetworkConnectivityLevelHint.ConstrainedInternetAccess => NetworkConnectivity.Local,
            NetworkConnectivityLevelHint.None => NetworkConnectivity.None,
            _ => managedAvailability ? NetworkConnectivity.Local : NetworkConnectivity.None,
        };

    internal static ManagedInterfaceState ProjectInterfaces(
        IReadOnlyList<ManagedNetworkInterfaceData> interfaces,
        int? bestInterfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var hasWireless = false;
        var wirelessUp = false;
        var wirelessWithGateway = false;
        var ethernetWithGateway = false;
        var otherWithGateway = false;
        var bestMedium = NativeNetworkMedium.None;
        foreach (var adapter in interfaces)
        {
            var wireless = adapter.Type == NetworkInterfaceType.Wireless80211;
            hasWireless |= wireless;
            if (adapter.Status != OperationalStatus.Up) continue;
            if (wireless) wirelessUp = true;
            if (!adapter.HasGateway) continue;
            var medium = wireless
                ? NativeNetworkMedium.WiFi
                : IsEthernet(adapter.Type)
                    ? NativeNetworkMedium.Ethernet
                    : adapter.Type is not NetworkInterfaceType.Loopback and
                        not NetworkInterfaceType.Tunnel
                        ? NativeNetworkMedium.Other
                        : NativeNetworkMedium.None;
            if (adapter.Ipv4Index is not null && adapter.Ipv4Index == bestInterfaceIndex)
                bestMedium = medium;
            if (medium == NativeNetworkMedium.WiFi) wirelessWithGateway = true;
            else if (medium == NativeNetworkMedium.Ethernet) ethernetWithGateway = true;
            else if (medium == NativeNetworkMedium.Other) otherWithGateway = true;
        }
        var defaultMedium = bestMedium != NativeNetworkMedium.None
            ? bestMedium
            : ethernetWithGateway
                ? NativeNetworkMedium.Ethernet
                : wirelessWithGateway
                    ? NativeNetworkMedium.WiFi
                    : otherWithGateway
                        ? NativeNetworkMedium.Other
                        : NativeNetworkMedium.None;
        return new(hasWireless, wirelessUp, defaultMedium);
    }

    private static bool IsEthernet(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or
        NetworkInterfaceType.Ethernet3Megabit or
        NetworkInterfaceType.FastEthernetFx or
        NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.GigabitEthernet;
}
