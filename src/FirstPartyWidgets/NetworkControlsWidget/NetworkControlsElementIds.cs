using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal static class NetworkControlsElementIds
{
    internal static string Wifi(string opaqueId) => Hash("network.wifi.item", opaqueId);

    internal static string SavedProfile(string opaqueId) => Hash("network.saved.item", opaqueId);

    internal static string Bluetooth(string opaqueId) =>
        Hash("network.bluetooth.item", opaqueId);

    private static string Hash(string prefix, string opaqueId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return $"{prefix}.{Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
