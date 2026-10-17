using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal bool HasInputForValidation=>input;
    internal bool HasVisiblePresentationForValidation=>visible;
    internal object PageActionDiagnostics()=>new{visible,input,loading,lastAction=pageAdapter?.LastAction};
    internal async Task ClickPageActionFixtureAsync(int index=0,string action="compare-item")
    {
        if(action is not("compare-item"or"evaluate-items"))throw new ArgumentException("Unknown fixture action");
        using var point=JsonDocument.Parse(await EvaluateFixtureAsync("(()=>{const b=document.querySelectorAll('[data-widgetrail-action=\""+action+"\"]')["+index+"];b.scrollIntoView({block:'center',behavior:'instant'});const r=b.getBoundingClientRect();const x=(Math.max(0,r.left)+Math.min(innerWidth,r.right))/2,y=(Math.max(0,r.top)+Math.min(innerHeight,r.bottom))/2;if(!b.contains(document.elementFromPoint(x,y)))throw new Error('Action is not visible');return {x,y}})()"));
        var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
        foreach(var type in new[]{"mousePressed","mouseReleased"})
            await core!.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new BrowserPointerEvent(type,x,y,button:"left",clickCount:1),BrowserJsonContext.Default.BrowserPointerEvent));
    }
}
