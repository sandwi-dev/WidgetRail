using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Web;

/// <summary>UI-thread-owned environment shared by browser and sealed media controllers.
/// Profiles and each controller's authority remain separate. Retain the existing browser
/// folder so consolidation never moves or clears saved website sessions.</summary>
internal sealed class HostedWebViewEnvironment(string userDataFolder, CoreWebView2EnvironmentOptions? options = null, bool enableEncodedSource = false)
{
    internal static bool NativeEncodingEnabled =>
        Environment.GetEnvironmentVariable("WIDGETRAIL_EXPERIMENTAL_NATIVE_H264") != "0";

    private Task<CoreWebView2Environment>? pending;

    private async Task<CoreWebView2Environment> CreateAsync()
    {
        if (enableEncodedSource && options is not null)
        {
            // Use the same profile folder and extension policy for both attempts.
            // A running browser process may already own it with ordinary options.
            var candidate = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = options.AreBrowserExtensionsEnabled,
                AdditionalBrowserArguments = "--enable-blink-features=RTCEncodedSource,RTCEncodedVideoFrameConstructor"
            };
            try { return await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, candidate); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);
    }

    internal async Task<CoreWebView2Environment> GetAsync()
    {
        var attempt = pending ??= CreateAsync();
        try { return await attempt; }
        catch
        {
            // An older failed waiter must not discard a newer retry.
            if (ReferenceEquals(pending, attempt)) pending = null;
            throw;
        }
    }
}
