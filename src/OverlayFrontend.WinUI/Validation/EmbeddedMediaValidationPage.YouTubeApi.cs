using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    // Opt-in real-network check. Load the public API through the production host
    // policy, without creating a video, starting playback or sending widget actions.
    private async Task CheckYouTubeApiAsync()
    {
#pragma warning disable WUI4001 // The production surface has completed native initialization above.
        var core = ((WebView2)surface!.Element).CoreWebView2;
#pragma warning restore WUI4001
        await core.ExecuteScriptAsync("""
            globalThis.providerApiStatus='loading';
            globalThis.onYouTubeIframeAPIReady=()=>{globalThis.providerApiStatus=typeof YT.Player==='function'?'ready':'invalid'};
            const script=document.createElement('script');script.src='https://www.youtube.com/iframe_api';
            script.onerror=()=>{globalThis.providerApiStatus='failed'};document.head.appendChild(script);
            """);
        var observed = "";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            observed = await core.ExecuteScriptAsync("globalThis.providerApiStatus");
            if (observed is "\"ready\"" or "\"failed\"" or "\"invalid\"") break;
            await Task.Delay(150, lifetime.Token);
        }
        Check(observed == "\"ready\"", "real YouTube iframe API and its dependent player script load through the unpackaged host policy: " + observed);
    }
}
