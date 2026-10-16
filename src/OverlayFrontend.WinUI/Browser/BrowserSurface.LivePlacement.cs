using System.Text.Json;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    // Read only geometry from the retained field/frame objects. Never walk the
    // whole DOM, read typed values, or attach to unrelated renderer targets.
    private const string LiveFieldGeometry = """
        function() {
          let active=document.activeElement;
          while(active?.shadowRoot?.activeElement)active=active.shadowRoot.activeElement;
          if(!this.isConnected||active!==this||this.disabled||this.readOnly)return null;
          const frame=this instanceof HTMLIFrameElement;
          if(!frame&&!(this instanceof HTMLInputElement||this instanceof HTMLTextAreaElement||this.isContentEditable))return null;
          if(!this.checkVisibility({checkVisibilityCSS:true}))return null;
          for(let e=this,n=0;e;e=e.parentElement){
            if(++n>64)return null;
            const t=getComputedStyle(e).transform;
            if(t!=='none'){const m=new DOMMatrix(t);if(!m.is2D||m.b!==0||m.c!==0||m.a<=0||m.d<=0)return null;}
          }
          let r=this.getBoundingClientRect();
          if(!r.width||!r.height)return null;
          // Restore visibility only when scrolling/layout has actually hidden
          // the editor. A visible field is never scrolled on each refresh.
          if(r.bottom<=0||r.top>=innerHeight||r.right<=0||r.left>=innerWidth){
            this.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'});
            r=this.getBoundingClientRect();
          }
          const v=visualViewport;
          return {x:r.left,y:r.top,w:r.width,h:r.height,
            sx:frame?r.width/this.offsetWidth:1,sy:frame?r.height/this.offsetHeight:1,
            bx:this.clientLeft,by:this.clientTop,
            vx:v?.offsetLeft??0,vy:v?.offsetTop??0,vw:v?.width??innerWidth,vh:v?.height??innerHeight};
        }
        """;

    private async Task TrackLiveKeyboardAsync(AutomaticFieldTarget target, WidgetTextEntryDialog dialog,
        Func<bool> current, CancellationToken token)
    {
        try
        {
            // One bounded request chain at a time, only for the lifetime of an
            // open editor. Covers script-driven movement as well as scroll and
            // resize without continuous DOM observers or a per-frame bridge.
            while (current())
            {
                await Task.Delay(150, token);
                await PositionLiveKeyboardAsync(target, dialog, current, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Detached fields, replaced frames and unsupported transforms retire
            // the exact editing session instead of retaining a stale keyboard.
            if (!token.IsCancellationRequested && ReferenceEquals(editDialog, dialog)) dialog.Hide();
        }
    }

    private async Task PositionLiveKeyboardAsync(AutomaticFieldTarget target, WidgetTextEntryDialog dialog,
        Func<bool> current, CancellationToken token)
    {
        var field = await Read(target.ObjectId, target.SessionId);
        var rect = new Rect(Number(field, "x"), Number(field, "y"), Number(field, "w"), Number(field, "h"));
        var top = field;
        foreach (var frame in target.Frames.Reverse())
        {
            top = await Read(frame.ObjectId, frame.SessionId);
            var sx = Number(top, "sx"); var sy = Number(top, "sy");
            rect = new(Number(top, "x") + (rect.X + Number(top, "bx")) * sx,
                Number(top, "y") + (rect.Y + Number(top, "by")) * sy, rect.Width * sx, rect.Height * sy);
        }
        token.ThrowIfCancellationRequested();
        if (!current() || XamlRoot is not { } root || !web.IsLoaded) throw new OperationCanceledException();
        // CSS viewport -> WebView DIPs -> owning XamlRoot DIPs. This includes
        // browser zoom, visual viewport scale and the host's interface scaling;
        // OS DPI is already represented by XAML, so it must not be applied twice.
        var scaleX = web.ActualWidth / Number(top, "vw");
        var scaleY = web.ActualHeight / Number(top, "vh");
        var local = new Rect((rect.X - Number(top, "vx")) * scaleX, (rect.Y - Number(top, "vy")) * scaleY,
            rect.Width * scaleX, rect.Height * scaleY);
        var transform = web.TransformToVisual(root.Content);
        var anchor = transform.TransformBounds(local);
        if (!double.IsFinite(anchor.X + anchor.Y + anchor.Width + anchor.Height) || anchor.Width <= 0 || anchor.Height <= 0)
            throw new OperationCanceledException();
        // Ignore subpixel motion to avoid visible jitter and repeated layout.
        if (dialog.FieldAnchor is not { } old || Math.Abs(old.X - anchor.X) >= 2 || Math.Abs(old.Y - anchor.Y) >= 2 ||
            Math.Abs(old.Width - anchor.Width) >= 2 || Math.Abs(old.Height - anchor.Height) >= 2)
            dialog.FieldAnchor = anchor;
        dialog.RefreshKeyboardPlacement();

        async Task<JsonElement> Read(string objectId, string? session)
        {
            token.ThrowIfCancellationRequested();
            if (!current() || core is not { } browser) throw new OperationCanceledException();
            using var result = JsonDocument.Parse(await browser.CallDevToolsProtocolMethodForSessionAsync(session ?? "", "Runtime.callFunctionOn",
                JsonSerializer.Serialize(new BrowserFieldCall(objectId, LiveFieldGeometry), BrowserJsonContext.Default.BrowserFieldCall))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(2), token));
            if (!result.RootElement.TryGetProperty("result", out var response) || !response.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                throw new OperationCanceledException();
            return value.Clone();
        }
        static double Number(JsonElement value, string name) => value.GetProperty(name).GetDouble();
    }
}
