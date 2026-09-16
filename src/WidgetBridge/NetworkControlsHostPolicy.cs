using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

internal static class NetworkControlsHostPolicy
{
    internal static bool TryResolveNetworkId(string sourceElementId,
        AvailableWifiNetworksSummary snapshot, out string networkId)
    {
        networkId = string.Empty;
        const string prefix = "network.wifi.item.";
        if (sourceElementId is null || sourceElementId.Length != prefix.Length + 64 ||
            !sourceElementId.StartsWith(prefix, StringComparison.Ordinal) ||
            snapshot.ScanState != WifiScanState.Ready || snapshot.Networks.Count > 128) return false;
        foreach (var network in snapshot.Networks)
        {
            if (network.Security != WifiSecurityKind.Personal || !network.CredentialRequired || network.IsConnected) continue;
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(network.NetworkId));
            if (sourceElementId != prefix + Convert.ToHexString(digest).ToLowerInvariant()) continue;
            networkId = network.NetworkId;
            return true;
        }
        return false;
    }
}
