using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private sealed record AutomaticFieldClick(CoreWebView2 Core, long Gesture, long PointerEpoch,
        long PageEpoch, long EditGeneration, double RatioX, double RatioY, double X, double Y);
    private sealed record AutomaticFieldTarget(AutomaticFieldClick Click, string ObjectId, string? SessionId);

    // Only host-generated numeric coordinates enter this fixed script. A click
    // elsewhere must not reopen a previously focused field. Open shadow roots
    // and associated labels follow the same exact-field editor contract.
    private const string ClickedField = """
        (p => {
          let hit = document.elementFromPoint(p.x, p.y);
          while (hit?.shadowRoot) {
            const child = hit.shadowRoot.elementFromPoint(p.x, p.y);
            if (!child || child === hit) break;
            hit = child;
          }
          let field = document.activeElement;
          while (field?.shadowRoot?.activeElement) field = field.shadowRoot.activeElement;
          if (field instanceof HTMLIFrameElement && hit===field) return field;
          const supported = field instanceof HTMLTextAreaElement ||
            field instanceof HTMLInputElement && ['text','search','url','email','tel','password'].includes(field.type) || field?.isContentEditable;
          if (!supported || !field.isConnected || field.disabled || field.readOnly) return null;
          return hit === field || field.contains(hit) || hit?.closest('label')?.control === field ? field : null;
        })
        """;

    private bool IsCurrentClick(AutomaticFieldClick click) => !retired && input && browsing && !HasDialog && !loading &&
        ReferenceEquals(core, click.Core) && pointerGesture == click.Gesture && pointerEpoch == click.PointerEpoch &&
        pageEpoch == click.PageEpoch && editGeneration == click.EditGeneration && !pointerPressed && !pointerMoved &&
        cursorX == click.RatioX && cursorY == click.RatioY;

    private async Task TryEditClickedFieldAsync(AutomaticFieldClick click)
    {
        if (!IsCurrentClick(click)) { TracePointer($"edit-rejected loading={loading} dialog={HasDialog} epoch={pageEpoch}/{click.PageEpoch} edit={editGeneration}/{click.EditGeneration}"); return; }
        var references = new List<(string? Session, string Object)>();
        var sessions = new List<string>();
        string? targetSession = null;
        int? contextId = null;
        var point = new BrowserHitPoint(click.X, click.Y);
        var handedToEditor = false;
        try
        {
            for (var depth = 0; depth < 8 && IsCurrentClick(click); depth++)
            {
                var pointJson = JsonSerializer.Serialize(point, BrowserJsonContext.Default.BrowserHitPoint);
                using var result = JsonDocument.Parse(await Call(targetSession, "Runtime.evaluate",
                    JsonSerializer.Serialize(new BrowserEvaluation(ClickedField + "(" + pointJson + ")", contextId), BrowserJsonContext.Default.BrowserEvaluation)));
                if (!result.RootElement.TryGetProperty("result", out var remote) ||
                    !remote.TryGetProperty("objectId", out var reference))
                {
                    TracePointer("edit-hit no supported focused field");
#if ENABLE_WIDGET_VALIDATION
                    if (FixturePage is not null)
                    {
                        TracePointer("synthetic-field-state=" + await Call(targetSession, "Runtime.evaluate",
                            JsonSerializer.Serialize(new BrowserEvaluation("JSON.stringify({url:location.href,inside:document.getElementById('inside')?.getBoundingClientRect().toJSON(),size:[innerWidth,innerHeight],active:document.activeElement?.id,tag:document.activeElement?.tagName,focused:document.hasFocus(),hit:document.elementFromPoint(" +
                                point.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + point.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")?.tagName,pointer:document.documentElement.dataset.pointer})", contextId), BrowserJsonContext.Default.BrowserEvaluation)));
                    }
#endif
                    return;
                }
                var objectId = reference.GetString();
                if (objectId is null || !IsCurrentClick(click)) return;
                references.Add((targetSession, objectId));
                using var node = JsonDocument.Parse(await Call(targetSession, "DOM.describeNode",
                    JsonSerializer.Serialize(new BrowserObject(objectId), BrowserJsonContext.Default.BrowserObject)));
                if (node.RootElement.GetProperty("node").GetProperty("nodeName").GetString() == "IFRAME")
                {
                    var frameId = node.RootElement.GetProperty("node").GetProperty("frameId").GetString()!;
                    using var mapped = JsonDocument.Parse(await Call(targetSession, "Runtime.callFunctionOn",
                        JsonSerializer.Serialize(new BrowserFieldCall(objectId, FramePoint, [new(pointJson)]), BrowserJsonContext.Default.BrowserFieldCall)));
                    var value = mapped.RootElement.GetProperty("result").GetProperty("value");
                    if (value.ValueKind != JsonValueKind.Object || !IsCurrentClick(click)) return;
                    point = new(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble());
                    // In-process frames have their own execution context. OOPIFs
                    // require a session attached to this exact owned iframe ID.
                    // Never enumerate or attach unrelated browser targets.
                    try { contextId = await World(targetSession, frameId); }
                    catch (Exception error) when (IsCurrentClick(click) && error is System.Runtime.InteropServices.COMException or ArgumentException)
                    {
                        using var attached = JsonDocument.Parse(await Call(targetSession, "Target.attachToTarget",
                            JsonSerializer.Serialize(new BrowserAttachTarget(frameId), BrowserJsonContext.Default.BrowserAttachTarget)));
                        targetSession = attached.RootElement.GetProperty("sessionId").GetString()!;
                        sessions.Add(targetSession);
                        contextId = await World(targetSession, frameId);
                    }
                    continue;
                }
                handedToEditor = true;
                await EditLiveFieldAsync(new(click, objectId, targetSession));
                return;
            }
        }
        catch (Exception) when (!handedToEditor && !IsCurrentClick(click)) { /* A newer gesture, page or input owner superseded this click. */ }
        finally
        {
            foreach (var reference in references.AsEnumerable().Reverse())
                try
                {
                    if (!retired) await Call(reference.Session, "Runtime.releaseObject",
                        JsonSerializer.Serialize(new BrowserObject(reference.Object), BrowserJsonContext.Default.BrowserObject));
                }
                catch (Exception) { /* Document/process loss already retired the object. */ }
            foreach (var session in sessions.AsEnumerable().Reverse())
                try { if (!retired) await Call(null, "Target.detachFromTarget", JsonSerializer.Serialize(new BrowserDetachTarget(session), BrowserJsonContext.Default.BrowserDetachTarget)); }
                catch (Exception) { /* Navigation/teardown already removed the child session. */ }
        }

        async Task<string> Call(string? session, string method, string args) => await click.Core.CallDevToolsProtocolMethodForSessionAsync(session ?? "", method, args)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        async Task<int> World(string? session, string frame)
        {
            using var world = JsonDocument.Parse(await Call(session, "Page.createIsolatedWorld",
                JsonSerializer.Serialize(new BrowserIsolatedWorld(frame), BrowserJsonContext.Default.BrowserIsolatedWorld)));
            return world.RootElement.GetProperty("executionContextId").GetInt32();
        }
    }

    private const string FramePoint = """
        function(raw) {
          let active=this.ownerDocument.activeElement;
          while(active?.shadowRoot?.activeElement)active=active.shadowRoot.activeElement;
          if(!this.isConnected || active!==this)return null;
          for(let e=this,n=0;e;e=e.parentElement){
            if(++n>64)return null;
            const t=getComputedStyle(e).transform;
            if(t!=='none'){const m=new DOMMatrix(t);if(!m.is2D||m.b!==0||m.c!==0||m.a<=0||m.d<=0)return null;}
          }
          const p=JSON.parse(raw), r=this.getBoundingClientRect();
          if(!r.width||!r.height||!this.offsetWidth||!this.offsetHeight)return null;
          return {x:(p.x-r.left)*this.offsetWidth/r.width-this.clientLeft,
                  y:(p.y-r.top)*this.offsetHeight/r.height-this.clientTop};
        }
        """;
}
