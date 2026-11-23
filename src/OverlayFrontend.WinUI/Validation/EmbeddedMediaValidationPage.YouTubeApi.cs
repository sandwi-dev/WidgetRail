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
        if (!Environment.GetCommandLineArgs().Contains("--validate-signed-out-trailer")) return;
        Check(core.Profile.IsInPrivateModeEnabled && core.Profile.ProfileName.StartsWith("media-", StringComparison.Ordinal),
            "Trailer uses the isolated private media profile");
        await core.ExecuteScriptAsync("""
            const stage = document.createElement('div'); document.body.replaceChildren(stage);
            window.trailer = new YT.Player(stage, {host:'https://www.youtube-nocookie.com', width:'640', height:'360',
              videoId:'6XGeJwsUP9c', playerVars:{autoplay:1,mute:1,playsinline:1,origin:location.origin}, events:{
                onReady:e=>{e.target.mute();e.target.playVideo();},
                onError:e=>{window.trailerError=String(e.data);}
              }});
            """);
        try
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                observed = await core.ExecuteScriptAsync("""
                    window.trailerError || (trailer.getPlayerState?.() === 1 && trailer.isMuted?.() &&
                      trailer.getCurrentTime?.() > 1 && new URL(trailer.getIframe().src).hostname === 'www.youtube-nocookie.com'
                      ? 'playing' : 'waiting')
                    """);
                if (observed != "\"waiting\"") break;
                await Task.Delay(200, lifetime.Token);
            }
            Check(observed == "\"playing\"", "Publisher trailer plays muted with an advancing clock in the privacy-enhanced iframe: " + observed);
        }
        finally { await core.ExecuteScriptAsync("window.trailer?.destroy?.()"); }
    }
}
