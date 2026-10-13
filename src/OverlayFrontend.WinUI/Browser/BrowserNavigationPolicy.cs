using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal static class BrowserNavigationPolicy
{
    internal static bool OpenExternally(WebBrowserDocument document, string url) =>
        document.Options.ExternalLinks == BrowserExternalLinkMode.OtherOriginsInWindows && WebBrowserDocument.IsWebUrl(url) &&
        !SameOrigin(document.Url, url);

    internal static bool SameOrigin(string first, string second) =>
        Uri.TryCreate(first, UriKind.Absolute, out var a) && Uri.TryCreate(second, UriKind.Absolute, out var b) &&
        a.Scheme == b.Scheme && a.Port == b.Port && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase);
}
