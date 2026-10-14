using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Web;

/// <summary>UI-thread-owned environment shared by browser and sealed media controllers.
/// Profiles and each controller's authority remain separate. Retain the existing browser
/// folder so consolidation never moves or clears saved website sessions.</summary>
internal sealed class HostedWebViewEnvironment(string userDataFolder, CoreWebView2EnvironmentOptions? options = null)
{
    private Task<CoreWebView2Environment>? pending;

    internal async Task<CoreWebView2Environment> GetAsync()
    {
        var attempt = pending ??= CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options).AsTask();
        try { return await attempt; }
        catch
        {
            // An older failed waiter must not discard a newer retry.
            if (ReferenceEquals(pending, attempt)) pending = null;
            throw;
        }
    }
}
