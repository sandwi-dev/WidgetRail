using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal Func<string, string, byte[], Task>? SaveWebAppIcon { get; set; }
    private bool faviconReading, faviconDirty;

    private async Task CaptureWebAppIconAsync()
    {
        if (faviconReading) { faviconDirty = true; return; }
        if (!UserWebAppStore.IsId(widgetId) || isPopup || core is null || SaveWebAppIcon is null) return;
        faviconReading = true;
        try
        {
            do
            {
                faviconDirty = false;
                var source = core; var epoch = pageEpoch;
                // A sign-in redirect or a link to another site must not replace the app's identity.
                if (retired || loading || source is null || !Uri.TryCreate(source.Source, UriKind.Absolute, out var current) ||
                    !Uri.TryCreate(document.Url, UriKind.Absolute, out var start) || IconHost(current) != IconHost(start) ||
                    current.Scheme is not ("https" or "http") || string.IsNullOrEmpty(source.FaviconUri)) break;
                using var nativeStream = await source.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                using var stream = nativeStream.AsStreamForRead();
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, lifetime.Token)) != 0)
                {
                    if (bytes.Length + count > UserWebAppStore.MaximumIconBytes) return;
                    bytes.Write(buffer, 0, count);
                }
                if (!retired && epoch == pageEpoch && ReferenceEquals(core, source) && bytes.Length > 0)
                    await SaveWebAppIcon(widgetId, document.Url, bytes.ToArray());
            } while (faviconDirty && !retired);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        { Diagnostics.FrontendFailureLog.Current.Write("webapp-favicon", error); }
        finally { faviconReading = false; }
    }
    private static string IconHost(Uri uri) => uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost;
}
