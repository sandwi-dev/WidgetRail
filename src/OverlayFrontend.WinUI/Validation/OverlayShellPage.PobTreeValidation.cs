using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

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
                var before=await browser.EvaluateFixtureAsync("visualViewport.scale");
                browser.ZoomForEditingValidation(.5);
                await Until("visualViewport.scale>1.2");
                checks.Add("Host mouse-sourced pinch zoom increases the real tree visual viewport scale from "+before);
                browser.ZoomForEditingValidation(-.5);
                await Until("visualViewport.scale<1.05");
                checks.Add("Host zoom out restores the initial visual viewport scale");
                async Task Until(string script)
                {
                    var deadline=Environment.TickCount64+15000;
                    while(await browser.EvaluateFixtureAsync(script)!="true")
                    {if(Environment.TickCount64>deadline)throw new TimeoutException(script);await Task.Delay(50);}
                }
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
    }
}
