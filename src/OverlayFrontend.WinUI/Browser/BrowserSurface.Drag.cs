using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private CoreWebView2DevToolsProtocolEventReceiver? dragReceiver;
    private JsonElement? dragData;
    private bool dragEntered;
    private void InitializeDrag()
    {
        dragReceiver = core!.GetDevToolsProtocolEventReceiver("Input.dragIntercepted");
        dragReceiver.DevToolsProtocolEventReceived += DragIntercepted;
    }
    private void DragIntercepted(CoreWebView2 sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        TracePointer($"drag received: down={nativePointerDown} input={input} dialog={HasDialog}");
        if (retired || !nativePointerDown || !input || HasDialog || pointerCore is not { } current) return;
        try
        {
            // Only a controller-owned drag enables interception. Keep bounded
            // webpage drag data inside this exact browser; never accept files.
            if (args.ParameterObjectAsJson.Length > 65536) throw new InvalidDataException();
            using var data = JsonDocument.Parse(args.ParameterObjectAsJson);
            var payload = data.RootElement.GetProperty("data");
            if (payload.TryGetProperty("files", out var files) && files.GetArrayLength() != 0) throw new InvalidDataException();
            dragData = payload.Clone(); dragEntered = false;
            TracePointer("drag accepted");
            cursorDirty = true; PumpPointer();
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { Run(async () => { await current.CallDevToolsProtocolMethodAsync("Input.cancelDragging", "{}"); }); }
    }
    private async Task DispatchDragAsync(CoreWebView2 current, string type, double x, double y)
    {
        TracePointer($"drag dispatch: {type} at {x},{y}");
        if (dragData is not { } data) return;
        if (!dragEntered)
        {
            dragEntered = true;
            await Send("dragEnter");
        }
        await Send(type);
        async Task Send(string action) => _ = await current.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent",
            JsonSerializer.Serialize(new BrowserDragEvent(action, x, y, data), BrowserJsonContext.Default.BrowserDragEvent));
    }
}
