using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task CheckFrameLifetimeAsync()
    {
#pragma warning disable WUI4001 // The sealed local fixture awaits surface.IsReady before accessing its core.
        var core = ((WebView2)surface!.Element).CoreWebView2;
#pragma warning restore WUI4001
        var denied = 0;
        const string blockedDocument = "https://r1.googlevideo.com/frame-probe";
        void Navigation(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        { if (args.Uri == blockedDocument && args.Cancel) ++denied; }
        void Resource(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        { if (args.Request.Uri == blockedDocument && args.Response?.StatusCode == 404) ++denied; }
        core.FrameNavigationStarting += Navigation;
        core.WebResourceRequested += Resource;
        try
        {
          for (var cycle = 0; cycle < 3; ++cycle)
          {
            // Local fixture only. No third-party page or provider script is evaluated.
            var before = denied;
            var created = await core.ExecuteScriptAsync("(()=>{const frame=document.createElement('iframe'); frame.id='frame-lifetime'; document.body.appendChild(frame); const child=frame.contentDocument.createElement('iframe'); child.src='https://r1.googlevideo.com/frame-probe'; frame.contentDocument.body.appendChild(child); return true;})()");
            Check(created == "true", "local fixture creates an actual nested iframe, cycle " + cycle);
            await Until(() => denied > before);
            await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });
            Check(surface.IsReady,
                "nested iframe navigation is blocked despite its media-resource allowlist and forced GC, cycle " + cycle);
            await core.ExecuteScriptAsync("document.getElementById('frame-lifetime').remove();");
            await Task.Delay(50);
            await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
            Check(surface.IsReady, "iframe removal and forced finalization preserve the admitted player, cycle " + cycle);
          }
        }
        finally { core.FrameNavigationStarting -= Navigation; core.WebResourceRequested -= Resource; }
    }
}
