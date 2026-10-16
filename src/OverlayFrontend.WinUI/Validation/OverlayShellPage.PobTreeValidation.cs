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
                await Until("document.querySelectorAll('.node').length>0 && document.getElementById('status').textContent!=='Loading passive tree…'");
                await Until("performance.getEntriesByType('resource').some(r=>r.name.includes('nodes-')&&r.name.endsWith('.jpg'))");
                checks.Add("Real tree page loads sealed upstream icon atlases");
                await browser.HoverTreeNodeForValidationAsync();
                await Until("!document.getElementById('tooltip').hidden && document.getElementById('tooltip-name').textContent.length>0");
                checks.Add("Native WebView hover receives node details through the widget session");
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
