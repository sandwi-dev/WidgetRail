using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifySpatialRoutingAsync(BrowserSurface browser, string placement, Action<bool, string> check)
    {
        if (!Environment.GetCommandLineArgs().Contains("--webapp-spatial")) return;
        await WebAppUntil(() => browser.IsBrowsing && !browser.IsToolbarFocused && !fullscreenLayer.IsTransitioning);
        await browser.EvaluateFixtureAsync("""
            window.spatialProbe=document.createElement('div');spatialProbe.id='spatial-probe';
            spatialProbe.style='position:fixed;left:10px;top:10px;width:180px;height:60px;z-index:2147483647;background:#234';
            spatialProbe.innerHTML='<button id="spatial-a" style="width:70px;height:40px;margin:5px">A</button><button id="spatial-b" style="width:70px;height:40px;margin:5px">B</button>';
            window.spatialClicked=false;
            spatialProbe.addEventListener('click',e=>{if(e.target.id==='spatial-b'&&e.isTrusted)window.spatialClicked=true;});
            document.body.append(spatialProbe);
            """);
        try
        {
            using var point = JsonDocument.Parse(await browser.EvaluateFixtureAsync("(()=>{const r=document.getElementById('spatial-a').getBoundingClientRect();return {x:(r.left+r.width/2-visualViewport.offsetLeft)/visualViewport.width,y:(r.top+r.height/2-visualViewport.offsetTop)/visualViewport.height}})()"));
            var current = browser.SpatialPointerForValidation;
            browser.MovePointer(point.RootElement.GetProperty("x").GetDouble() - current.X, point.RootElement.GetProperty("y").GetDouble() - current.Y);
            await Task.Delay(50);
            var before = browser.SpatialRequestsForValidation;
            Receive(new ControllerFrame { Connected = 1, DpadNavigation = new() { Direction = NavigationDirection.Right, Phase = NavigationPhase.Pressed } });
            Receive(new ControllerFrame { Connected = 1 });
            await WebAppUntil(() => !browser.SpatialBusyForValidation);
            check(browser.SpatialRequestsForValidation > before && browser.LastSpatialMoveForValidation is not null,
                "Shell D-pad reaches geometric browser navigation: " + placement + " " + browser.SpatialErrorForValidation);
            var landed = browser.SpatialPointerForValidation;
            var hitScript = FormattableString.Invariant($"document.elementFromPoint({landed.X}*visualViewport.width+visualViewport.offsetLeft,{landed.Y}*visualViewport.height+visualViewport.offsetTop)?.id");
            check(await browser.EvaluateFixtureAsync(hitScript) == "\"spatial-b\"", "Shell geometry selects the adjacent button: " + placement);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await Task.Delay(40);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await WebAppUntilScript(browser, "window.spatialClicked===true");
            check(true, "Shell A clicks the geometrically selected native control: " + placement);
        }
        finally { await browser.EvaluateFixtureAsync("document.getElementById('spatial-probe')?.remove()"); }
    }
}
