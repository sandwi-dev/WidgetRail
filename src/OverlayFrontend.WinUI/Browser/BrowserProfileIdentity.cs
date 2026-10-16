using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Maps logical widget/account identity to host-owned storage, never to an author-supplied path.</summary>
internal static class BrowserProfileIdentity
{
    internal static string Name(string widgetId, WebBrowserDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        if (!document.IsWellFormed()) throw new ArgumentException("Invalid browser document.", nameof(document));
        var scope = document.ProviderDocument is not null ? "provider" :
            document.Options.Storage == BrowserStorage.Persistent ? "persistent" : "private";
        // Length-delimited fields avoid collisions between separators in names.
        var key = $"{widgetId.Length}:{widgetId}{document.Options.ProfileKey.Length}:{document.Options.ProfileKey}";
        return scope + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..48];
    }

    internal static bool SameProfile(WebBrowserDocument first, WebBrowserDocument second) =>
        first.Options.Storage == second.Options.Storage && first.Options.ProfileKey == second.Options.ProfileKey &&
        (first.ProviderDocument is null) == (second.ProviderDocument is null);

    internal static string LibraryFileName(string widgetId, WebBrowserDocument document) =>
        // Keep the existing default browser's bookmarks/history available.
        // All newly named accounts and persistent profiles get distinct files.
        (document.Options.Storage == BrowserStorage.Private || widgetId is "browser" or "widgetrail.samples.browser") && document.Options.ProfileKey == "default" && document.ProviderDocument is null
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(widgetId))) + ".json"
            : Name(widgetId, document) + ".json";
}
