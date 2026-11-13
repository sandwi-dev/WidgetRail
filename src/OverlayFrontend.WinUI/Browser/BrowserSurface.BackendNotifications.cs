using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private static async Task<bool> ApplyNotificationPermissionAsync(CoreWebView2 browser, Func<bool> current, CancellationToken token)
    {
        async Task<JsonDocument> Call(string method, JsonObject arguments) =>
            JsonDocument.Parse(await browser.CallDevToolsProtocolMethodAsync(method, arguments.ToJsonString())
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));

        // The page cannot replace Notification.requestPermission in the host's isolated world.
        using var tree = await Call("Page.getFrameTree", new());
        if (!current()) return false;
        var frame = tree.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        using var world = await Call("Page.createIsolatedWorld", new() { ["frameId"] = frame, ["worldName"] = "WidgetRailNotifications" });
        if (!current()) return false;
        using var response = await Call("Runtime.evaluate", new()
        {
            ["contextId"] = world.RootElement.GetProperty("executionContextId").GetInt32(),
            ["expression"] = "Notification.requestPermission()",
            ["awaitPromise"] = true, ["returnByValue"] = true, ["userGesture"] = true
        });
        return !response.RootElement.TryGetProperty("exceptionDetails", out _) &&
            response.RootElement.GetProperty("result").TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.String && value.GetString() == "granted";
    }
}
