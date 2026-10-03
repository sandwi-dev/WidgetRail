using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private bool pointerPressed, nativePointerDown;
    private long pointerEpoch, pointerGesture, nativePointerGesture;
    private CoreWebView2? pointerCore;
    private double pointerStartX, pointerStartY;
    private bool pointerMoved;
    private void PressPointer()
    {
        TracePointer($"press: held={pointerPressed} input={input} browsing={browsing} dialog={HasDialog}");
        if (pointerPressed || !input || !browsing || HasDialog) return;
        pointerPressed = true;
        pointerMoved = false;
        pointerStartX = cursorX; pointerStartY = cursorY;
        var gesture = ++pointerGesture; var epoch = pointerEpoch;
        var x = cursorX; var y = cursorY;
        Run(() => SetPointerButtonAsync(true, gesture, epoch, x, y));
    }
    private void ReleasePointer()
    {
        TracePointer($"release: held={pointerPressed}");
        if (!pointerPressed) return; // The A that entered interaction never pressed the page.
        pointerPressed = false;
        var gesture = pointerGesture; var epoch = pointerEpoch;
        var x = cursorX; var y = cursorY;
        Run(() => SetPointerButtonAsync(false, gesture, epoch, x, y));
    }
    private void CancelPointer()
    {
        TracePointer($"cancel: epoch={pointerEpoch} down={nativePointerDown}");
        ++pointerEpoch; pointerPressed = false;
        if (!nativePointerDown) return;
        var gesture = nativePointerGesture;
        Run(() => SetPointerButtonAsync(false, gesture, -1, 0, 0));
    }
    private async Task SetPointerButtonAsync(bool down, long gesture, long epoch, double ratioX, double ratioY)
    {
        AutomaticFieldClick? click = null;
        await pointer.WaitAsync(lifetime.Token);
        try
        {
            TracePointer($"dispatch: down={down} gesture={gesture}/{nativePointerGesture} epoch={epoch}/{pointerEpoch} native={nativePointerDown} input={input}");
            if (retired) return;
            if (!down && (!nativePointerDown || nativePointerGesture != gesture)) return;
            if (down && (!input || !browsing || HasDialog || epoch != pointerEpoch)) return;
            var current = down ? core : pointerCore;
            if (current is null) return;
            var clickPage = pageEpoch;
            var clickEdit = editGeneration;
            var wasDrag = dragData is not null;
            var x = -1d; var y = -1d;
            if (input && browsing && !HasDialog && epoch == pointerEpoch)
            {
                using var metrics = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
                var size = metrics.RootElement.GetProperty("cssVisualViewport");
                x = ratioX * size.GetProperty("clientWidth").GetDouble();
                y = ratioY * size.GetProperty("clientHeight").GetDouble();
            }
            if (retired) return;
            if (!input || !browsing || HasDialog || epoch != pointerEpoch)
            {
                if (down) return;
                x = y = -1; // Cancel outside the page, never click an unrelated control.
            }
            if (down)
            {
                dragData = null; dragEntered = false;
                await current.CallDevToolsProtocolMethodAsync("Input.setInterceptDrags", "{\"enabled\":true}");
                if (!input || HasDialog || epoch != pointerEpoch || retired)
                {
                    if (!retired) await current.CallDevToolsProtocolMethodAsync("Input.setInterceptDrags", "{\"enabled\":false}");
                    return;
                }
            }
            else if (dragData is not null)
            {
                if (x < 0 || y < 0) await current.CallDevToolsProtocolMethodAsync("Input.cancelDragging", "{}");
                else await DispatchDragAsync(current, "drop", x, y);
                dragData = null; dragEntered = false;
            }
            if (!down && (!input || !browsing || HasDialog || epoch != pointerEpoch)) x = y = -1;
            nativePointerDown = down;
            pointerCore = down ? current : null;
            nativePointerGesture = gesture;
            TracePointer($"send: down={down} x={x} y={y}");
            await current.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(
                new BrowserPointerEvent(down ? "mousePressed" : "mouseReleased", x, y, button: "left", clickCount: 1, buttons: down ? 1 : 0),
                BrowserJsonContext.Default.BrowserPointerEvent));
            if (!down) await current.CallDevToolsProtocolMethodAsync("Input.setInterceptDrags", "{\"enabled\":false}");
            if (!down && !wasDrag && !pointerMoved && !IsProviderContent && x >= 0 && y >= 0)
                click = new(current, gesture, epoch, clickPage, clickEdit, ratioX, ratioY, x, y);
        }
        finally { pointer.Release(); }
        // Release the input semaphore first: opening an editor cancels any
        // remaining pointer work and must never wait on its own mouse-up.
        if (click is not null) await TryEditClickedFieldAsync(click);
    }

    private void TrackPointerMovement()
    {
        if (!pointerPressed && !nativePointerDown) return;
        var x = (cursorX - pointerStartX) * viewport.ActualWidth;
        var y = (cursorY - pointerStartY) * viewport.ActualHeight;
        if (x * x + y * y > 16) pointerMoved = true;
    }
}
