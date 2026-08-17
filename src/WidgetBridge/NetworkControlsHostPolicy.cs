namespace WidgetRail.WidgetBridge;

internal static class NetworkControlsHostPolicy
{
    private const string Prefix = "network.wifi.item.";

    internal static bool TryParseNetworkId(string sourceElementId, out string networkId)
    {
        networkId = string.Empty;
        if (sourceElementId is null ||
            !sourceElementId.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var candidate = sourceElementId[Prefix.Length..];
        if (candidate.Length is <= 0 or > 128 ||
            !candidate.StartsWith("wifi_", StringComparison.Ordinal) ||
            candidate.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            return false;
        networkId = candidate;
        return true;
    }
}
