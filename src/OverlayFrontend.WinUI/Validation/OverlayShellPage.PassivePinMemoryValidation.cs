using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPassivePinMemoryAsync(BrowserSurface browser, string kind, Action<bool, string> check)
    {
        var core = ((WebView2)browser.CompositionRoot).CoreWebView2;
        var navigations = browser.NavigationCount;
        await browser.EvaluateGestureFixtureAsync("""
            window.pinMemoryCanvas=document.createElement('canvas');pinMemoryCanvas.width=320;pinMemoryCanvas.height=180;
            window.pinMemoryTicks=0;window.pinMemoryPaint=true;
            const ctx=pinMemoryCanvas.getContext('2d');
            function paintPin(){ctx.fillStyle=pinMemoryTicks++%2?'#007d48':'#006d38';ctx.fillRect(0,0,320,180);if(pinMemoryPaint)requestAnimationFrame(paintPin);}paintPin();
            window.pinMemoryStream=pinMemoryCanvas.captureStream(30);
            window.pinMemoryVideo=document.createElement('video');pinMemoryVideo.muted=true;pinMemoryVideo.autoplay=true;
            pinMemoryVideo.style='position:fixed;right:0;bottom:0;width:320px;height:180px';document.body.append(pinMemoryVideo);
            pinMemoryVideo.srcObject=pinMemoryStream;pinMemoryVideo.play();
            """);
        await WebAppUntilScript(browser, "pinMemoryVideo.readyState>=2 && pinMemoryVideo.getVideoPlaybackQuality().totalVideoFrames>0");
        ExitPinnedInteraction(restoreMain: true);
        await WebAppUntil(() => foreground && !PinnedInputActive);
        await Task.Delay(1000);
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal, $"{kind}: brief passive pin retains Normal");
        await WebAppUntil(() => core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low);
        await browser.EvaluateFixtureAsync("window.pinMemoryStart={frames:pinMemoryVideo.getVideoPlaybackQuality().totalVideoFrames,time:pinMemoryVideo.currentTime}");
        await Task.Delay(3000);
        check(!core.IsSuspended && await browser.EvaluateFixtureAsync("!document.hidden && pinMemoryStream.getTracks().every(t=>t.readyState==='live') && pinMemoryVideo.currentTime-pinMemoryStart.time>1 && pinMemoryVideo.getVideoPlaybackQuality().totalVideoFrames-pinMemoryStart.frames>10") == "true",
            $"{kind}: visible passive Low-memory pin keeps rendering and playing synthetic video without suspension");
        NativePickerVisibilityForValidation!(false);
        await WebAppUntil(() => !visible);
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low, $"{kind}: closing overlay preserves passive pin Low mode");
        NativePickerVisibilityForValidation(true);
        await WebAppUntil(() => visible && foreground && !switching);
        await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Released);
        await WebAppUntil(() => PinnedInputActive && browser.IsBrowsing);
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal && browser.NavigationCount == navigations,
            $"{kind}: entering restores Normal without reloading the pin");
        await browser.EvaluateFixtureAsync("pinMemoryPaint=false;pinMemoryVideo.pause();pinMemoryVideo.remove();pinMemoryStream.getTracks().forEach(t=>t.stop())");
    }
}
