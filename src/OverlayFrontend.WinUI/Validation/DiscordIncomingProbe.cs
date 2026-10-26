using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// A native floating receiver for the WebView2 feasibility gate. Production will
// use WidgetRail's existing WebSession + pinned-layout lifetime instead.
internal sealed class DiscordIncomingProbe : IAsyncDisposable
{
    private readonly Window window = new() { Title = "Discord incoming stream · prototype" };
    private readonly WebView2 web = new();
    private bool disposed;
    internal event Action? Closed;
    internal DiscordIncomingProbe()
    {
        window.Content = web; window.AppWindow.Resize(new(640, 400));
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        window.Closed += (_, _) => { if (!disposed) { disposed = true; web.Close(); Closed?.Invoke(); } };
    }
    internal async Task<JsonElement> OpenAsync(string assets, CoreWebView2Environment environment, JsonElement offer)
    {
        window.Activate();
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("discord-incoming.widgetrail.test", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != "https://discord-incoming.widgetrail.test/incoming.html";
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.NavigationCompleted += (_, e) => ready.TrySetResult(e.IsSuccess);
        core.Navigate("https://discord-incoming.widgetrail.test/incoming.html");
        if (!await ready.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new InvalidOperationException("Receiver navigation failed.");
        return await EvaluateAsync("incoming.connect(" + offer.GetRawText() + ")");
    }
    private async Task<JsonElement> EvaluateAsync(string expression)
    {
        using var result = JsonDocument.Parse(await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })));
        if (result.RootElement.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Receiver negotiation failed.");
        return result.RootElement.GetProperty("result").GetProperty("value").Clone();
    }
    internal Task<JsonElement> StatusAsync() => EvaluateAsync("incoming.status()");
    public ValueTask DisposeAsync()
    {
        if (!disposed) { disposed = true; web.Close(); window.Close(); }
        return ValueTask.CompletedTask;
    }
}
