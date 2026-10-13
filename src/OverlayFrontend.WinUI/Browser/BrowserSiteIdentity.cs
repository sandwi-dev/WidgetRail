using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal static class BrowserSiteIdentity
{
    // Use an ASCII origin in consent UI, without path/query/user info that could
    // disguise the recipient or expose an authentication token.
    internal static string? Origin(string url)
    {
        if (!WebBrowserDocument.IsWebUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        var host = uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.IdnHost.Trim('[', ']') + "]" : uri.IdnHost;
        return "https://" + host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
