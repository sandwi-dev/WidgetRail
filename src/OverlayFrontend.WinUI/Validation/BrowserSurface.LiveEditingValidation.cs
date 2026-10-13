using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private AutomaticFieldTarget? fixtureLiveTarget;
    internal async Task<string> ReadLiveFrameForValidationAsync()
    {
        if (FixturePage is null || fixtureLiveTarget is not { } target) throw new InvalidOperationException("Synthetic live frame required.");
        return await core!.CallDevToolsProtocolMethodForSessionAsync(target.SessionId ?? "", "Runtime.callFunctionOn",
            JsonSerializer.Serialize(new BrowserFieldCall(target.ObjectId,
                "function(){return {value:this.value,origin:this.ownerDocument.location.origin,inputs:this.ownerDocument.documentElement.dataset.inputs};}"), BrowserJsonContext.Default.BrowserFieldCall))
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal async Task ClickFrameFieldForValidationAsync()
    {
        if (FixturePage is null) throw new InvalidOperationException("Synthetic page required.");
        await EvaluateFixtureAsync("frame.scrollIntoView({block:'center'})");
        await core!.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))\",\"awaitPromise\":true}");
        using var point = JsonDocument.Parse(await EvaluateFixtureAsync("(()=>{const r=frame.getBoundingClientRect();return {x:(r.left+80)/visualViewport.width,y:(r.top+40)/visualViewport.height};})()"));
        cursorX = point.RootElement.GetProperty("x").GetDouble(); cursorY = point.RootElement.GetProperty("y").GetDouble();
        await SendPointerAsync("mouseMoved");
        HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(80);
        HandleButton(ControllerButton.A, ControllerEventPhase.Released);
    }

    internal async Task<string> ProbeFrameNodeForValidationAsync()
    {
        if (FixturePage is null) throw new InvalidOperationException("Synthetic page required.");
        await EvaluateFixtureAsync("frame.scrollIntoView({block:'center'})");
        await core!.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))\",\"awaitPromise\":true}");
        using var point = JsonDocument.Parse(await EvaluateFixtureAsync("(()=>{const r=frame.getBoundingClientRect();return {x:r.left+80,y:r.top+40,width:visualViewport.width,height:visualViewport.height,scrollX,scrollY,devicePixelRatio,hit:document.elementFromPoint(r.left+80,r.top+40)?.tagName}})()"));
        var x = point.RootElement.GetProperty("x").GetDouble(); var y = point.RootElement.GetProperty("y").GetDouble();
        cursorX = x / point.RootElement.GetProperty("width").GetDouble(); cursorY = y / point.RootElement.GetProperty("height").GetDouble();
        async Task<string> Call(string method, string args) => await core!.CallDevToolsProtocolMethodAsync(method, args).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var hit = await Call("DOM.getNodeForLocation", JsonSerializer.Serialize(new { x = (int)x, y = (int)y }));
        var documentHit = await Call("DOM.getNodeForLocation", JsonSerializer.Serialize(new
        { x = (int)(x + point.RootElement.GetProperty("scrollX").GetDouble()), y = (int)(y + point.RootElement.GetProperty("scrollY").GetDouble()) }));
        using var documentNode = JsonDocument.Parse(documentHit);
        using var node = JsonDocument.Parse(hit);
        var resolved = await Call("DOM.resolveNode", JsonSerializer.Serialize(new { backendNodeId = node.RootElement.GetProperty("backendNodeId").GetInt32() }));
        using var reference = JsonDocument.Parse(resolved);
        var id = reference.RootElement.GetProperty("object").GetProperty("objectId").GetString()!;
        try
        {
            HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(80);
            HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(50);
            var state = await Call("Runtime.callFunctionOn", JsonSerializer.Serialize(new
            { objectId = id, functionDeclaration = "function(){return {tag:this.tagName,active:this.ownerDocument.activeElement===this,origin:this.ownerDocument.location.origin}}", returnByValue = true }));
            return JsonSerializer.Serialize(new { kind = "cross-origin-frame-probe", point = point.RootElement, hit, documentHit, resolved, state,
                documentNode = await Call("DOM.describeNode", JsonSerializer.Serialize(new { backendNodeId = documentNode.RootElement.GetProperty("backendNodeId").GetInt32() })),
                targets = await Call("Target.getTargets", "{}"), frameTree = await Call("Page.getFrameTree", "{}"), keyboardOpened = FixtureEditor is not null });
        }
        finally { await Call("Runtime.releaseObject", JsonSerializer.Serialize(new { objectId = id })); }
    }

    internal async Task ClickFieldForValidationAsync(string id)
    {
        if (FixturePage is null) throw new InvalidOperationException("Synthetic page required.");
        // NavigationCompleted precedes native layout/paint. Drive A only after
        // the synthetic page has had two real animation frames to settle.
        await core!.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))\",\"awaitPromise\":true}");
        await EvaluateFixtureAsync("window.__fixturePointer=[];for(const type of ['mousedown','mouseup','click'])document.addEventListener(type,e=>__fixturePointer.push({type,target:e.target.id,x:e.clientX,y:e.clientY,trusted:e.isTrusted}),{once:true,capture:true})");
        using var point = JsonDocument.Parse(await EvaluateFixtureAsync("(()=>{const e=document.getElementById(" + JsonSerializer.Serialize(id) + ");e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:(r.left+r.width/2)/visualViewport.width,y:(r.top+r.height/2)/visualViewport.height};})()"));
        cursorX = point.RootElement.GetProperty("x").GetDouble(); cursorY = point.RootElement.GetProperty("y").GetDouble();
        HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
        await Task.Delay(80);
        HandleButton(ControllerButton.A, ControllerEventPhase.Released);
    }
}
