using System.Net.NetworkInformation;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

internal static class WindowsNetworkConnectionDetailsPolicy
{
    private const uint ErrorSuccess = 0;

    internal static NativeNetworkConnectionDetails Read(IWindowsNetworkNativeCalls calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var connectivity = ReadConnectivity(calls);
        if (connectivity == NetworkConnectionDetailsConnectivity.None)
            return Empty(NetworkConnectionDetailsState.Offline, connectivity);

        var destination = BitConverter.ToUInt32([1, 1, 1, 1], 0);
        var bestIndex = calls.ReadBestInterface(destination, out var index) == ErrorSuccess
            ? checked((int)index)
            : (int?)null;
        var candidates = calls.ReadConnectionInterfaces()
            .Where(item => item.Status == OperationalStatus.Up &&
                item.Type is not NetworkInterfaceType.Loopback &&
                item.DefaultGateways.Count != 0)
            .ToArray();
        var selected = bestIndex is null
            ? candidates.Length == 1 ? candidates[0] : null
            : candidates.FirstOrDefault(item => item.Ipv4Index == bestIndex);
        if (selected is null)
            return Empty(candidates.Length > 1
                ? NetworkConnectionDetailsState.Ambiguous
                : NetworkConnectionDetailsState.Unavailable, connectivity);

        return new(
            NetworkConnectionDetailsState.Available,
            connectivity,
            Transport(selected.Type),
            selected.IpAddresses.Take(8).ToArray(),
            selected.DefaultGateways.Take(4).ToArray(),
            selected.DnsServers.Take(8).ToArray());
    }

    internal static NetworkConnectionDetailsConnectivity ReadConnectivity(
        IWindowsNetworkNativeCalls calls)
    {
        try
        {
            if (calls.ReadConnectivityHint(out var hint) == ErrorSuccess)
                return hint.ConnectivityLevel switch
                {
                    NetworkConnectivityLevelHint.InternetAccess =>
                        NetworkConnectionDetailsConnectivity.Internet,
                    NetworkConnectivityLevelHint.ConstrainedInternetAccess =>
                        NetworkConnectionDetailsConnectivity.Constrained,
                    NetworkConnectivityLevelHint.LocalAccess =>
                        NetworkConnectionDetailsConnectivity.Local,
                    NetworkConnectivityLevelHint.None =>
                        NetworkConnectionDetailsConnectivity.None,
                    _ => calls.IsNetworkAvailable()
                        ? NetworkConnectionDetailsConnectivity.Local
                        : NetworkConnectionDetailsConnectivity.None,
                };
        }
        catch (EntryPointNotFoundException)
        {
        }
        return calls.IsNetworkAvailable()
            ? NetworkConnectionDetailsConnectivity.Local
            : NetworkConnectionDetailsConnectivity.None;
    }

    private static NativeNetworkConnectionDetails Empty(
        NetworkConnectionDetailsState state,
        NetworkConnectionDetailsConnectivity connectivity) =>
        new(state, connectivity, NativeNetworkMedium.None, [], [], []);

    private static NativeNetworkMedium Transport(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NativeNetworkMedium.WiFi,
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or
            NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.GigabitEthernet => NativeNetworkMedium.Ethernet,
        _ => NativeNetworkMedium.Other,
    };
}
