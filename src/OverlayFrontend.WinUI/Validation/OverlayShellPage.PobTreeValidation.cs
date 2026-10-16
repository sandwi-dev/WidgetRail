using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePobTreeValidation(string path)
    {
        Loaded += async (_,_) =>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>surface is not null && FindControl<Button>(surface,"Widget.pob.tab.1") is not null);
                var button=FindControl<Button>(surface!,"Widget.pob.tab.1")!;
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await WebAppUntil(()=>FindSlot(surface)?.Surface is {IsReady:true,IsNavigationInProgress:false});
                var browser=FindSlot(surface)!.Surface!;
                browser.SetPresentation(true,true);browser.Enter();
                var hints=Presentation.WidgetViewPresenter.CaptureBrowserGuide(FindSlot(surface)!);
                if(hints.Any(h=>h.Button is ControllerButton.Y or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.Menu))
                    throw new InvalidOperationException("Packaged tree advertises unavailable address/history/page actions");
                if(!new[]{ControllerButton.A,ControllerButton.X,ControllerButton.LeftTrigger,ControllerButton.RightTrigger,ControllerButton.LeftStick}
                    .All(button=>hints.Any(h=>h.Button==button)))throw new InvalidOperationException("Packaged tree lost supported controller hints");
                checks.Add("Packaged tree hides unavailable address/history/page actions while retaining select, reload, zoom and toolbar");
                browser.HandleButton(ControllerButton.Y,ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.Y,ControllerEventPhase.Released);
                browser.HandleButton(ControllerButton.Menu,ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.Menu,ControllerEventPhase.Released);
                if(browser.HasDialog)throw new InvalidOperationException("Unsupported packaged-page shortcut opened a dialog");
                checks.Add("Hidden packaged-page shortcuts remain unavailable when dispatched through controller handling");
                await Until("document.querySelectorAll('.node').length>0 && document.getElementById('status').textContent!=='Loading passive tree…'");
                // Large builds may initially fit below the icon-detail threshold.
                await browser.EvaluateFixtureAsync("document.getElementById('in').click();document.getElementById('in').click();");
                await Until("performance.getEntriesByType('resource').some(r=>r.name.includes('nodes-')&&r.name.endsWith('.webp'))");
                checks.Add("Real tree page loads sealed upstream icon atlases");
                await browser.EvaluateFixtureAsync("window.artDecoded=false;Promise.all(performance.getEntriesByType('resource').filter(r=>r.name.endsWith('.webp')).map(r=>new Promise((resolve,reject)=>{const image=new Image();image.onload=()=>image.naturalWidth>0?resolve():reject();image.onerror=reject;image.src=r.name;}))).then(()=>window.artDecoded=true);");
                await Until("window.artDecoded===true");
                checks.Add("WebP atlas pixels decode successfully in the native WebView");
                await Until("![...document.querySelectorAll('.node')].some(n=>/OnlyImage|Class start|Ascendancy start/.test(n.getAttribute('aria-label')))");
                checks.Add("Visible hit targets exclude decorative artwork and start landmarks");
                await browser.HoverTreeNodeForValidationAsync();
                await Until("!document.getElementById('tooltip').hidden && document.getElementById('tooltip-name').textContent.length>0");
                checks.Add("Native WebView hover receives node details through the widget session");
                // Leave headroom below the map's maximum zoom for the 1.5x check.
                await browser.EvaluateFixtureAsync("document.getElementById('out').click();document.getElementById('out').click();");
                await Task.Delay(100);
                await browser.EvaluateFixtureAsync("(()=>{const m=document.getElementById('map'),n=[...document.querySelectorAll('.node')].find(n=>Math.abs(parseFloat(n.style.left)-m.clientWidth/2)>10&&Math.abs(parseFloat(n.style.left)-m.clientWidth/2)<m.clientWidth/4&&Math.abs(parseFloat(n.style.top)-m.clientHeight/2)<m.clientHeight/4);if(!n)throw Error('No central probe node');window.zoomProbe={id:n.id,x:parseFloat(n.style.left),w:m.clientWidth,h:m.clientHeight,bar:document.querySelector('nav').getBoundingClientRect().height};})()");
                browser.HandleButton(ControllerButton.RightTrigger,ControllerEventPhase.Pressed);
                await Until("(()=>{const p=zoomProbe,n=document.getElementById(p.id);return n&&Math.abs((parseFloat(n.style.left)-p.w/2)/(p.x-p.w/2)-1.5)<.01;})()");
                await Until("visualViewport.scale===1 && document.getElementById('map').clientWidth===zoomProbe.w && document.getElementById('map').clientHeight===zoomProbe.h && document.querySelector('nav').getBoundingClientRect().height===zoomProbe.bar");
                checks.Add("R2 changes the map scale by the plus-button factor without magnifying the page or toolbar");
                browser.HandleButton(ControllerButton.LeftTrigger,ControllerEventPhase.Pressed);
                await Until("Math.abs(parseFloat(document.getElementById(zoomProbe.id)?.style.left)-zoomProbe.x)<.1 && visualViewport.scale===1");
                checks.Add("L2 reverses the internal zoom step while the visual viewport remains at 100 percent");
                async Task Until(string script)
                {
                    var deadline=Environment.TickCount64+15000;
                    while(await browser.EvaluateFixtureAsync(script)!="true")
                    {if(Environment.TickCount64>deadline)throw new TimeoutException(script+"; "+browser.ZoomStateForValidation);await Task.Delay(50);}
                }
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
    }
}
