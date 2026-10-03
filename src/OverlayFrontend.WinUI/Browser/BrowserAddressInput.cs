using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal static class BrowserAddressInput
{
    internal static string EditorValue(string source) => source == WebBrowserDocument.StartPage ? string.Empty : source;

    internal static string? Resolve(string input)
    {
        var value = input.Trim();
        if (value.Length == 0 || value.Length > 2048 || value.Any(char.IsControl)) return null;
        if (WebBrowserDocument.IsWebUrl(value)) return value;

        // Recognize bare domains, IP addresses and localhost without mistaking
        // ordinary single-word queries for intranet hosts.
        var candidate = "https://" + value;
        if (!value.Any(char.IsWhiteSpace) && WebBrowserDocument.IsWebUrl(candidate) &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var address) &&
            Uri.CheckHostName(address.Host) != UriHostNameType.Unknown &&
            (address.Host.Contains('.') || address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                address.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6))
            return candidate;

        // Address entry does not introduce file, script or OS-protocol navigation.
        // Search operators such as site: and ordinary punctuation remain queries.
        if (value.Contains("://", StringComparison.Ordinal) ||
            Uri.TryCreate(value, UriKind.Absolute, out var explicitAddress) &&
            explicitAddress.Scheme is "http" or "https" or "file" or "javascript" or "data" or "about" or "mailto")
            return null;
        var search = "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
        return WebBrowserDocument.IsWebUrl(search) ? search : null;
    }
}
