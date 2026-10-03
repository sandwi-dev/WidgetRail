using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly IReadOnlyList<string>? providerHtml;
    private Task? providerAuthorityPump;
    internal Func<CancellationToken, Task>? CheckProviderAuthority { get; set; }
    internal bool IsProviderContent => document.ProviderDocument is not null;
    internal WidgetRail.WidgetProtocol.ProviderDocumentReference? ProviderReference => document.ProviderDocument;
    internal double ProviderContentHeight { get; private set; } = 72;
    internal Action? ProviderHeightChanged { get; set; }
    private bool measuringProviderHeight, measureProviderAgain;

    private async Task RefreshProviderHeightAsync()
    {
        if (!IsProviderContent || core is null || retired || faulted || loading) return;
        if (measuringProviderHeight) { measureProviderAgain = true; return; }
        measuringProviderHeight = true;
        try
        {
            do
            {
                measureProviderAgain = false;
                var current = core;
                // Native DOM geometry only: do not enable page scripts or expose a JS bridge.
                using var tree = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":2}"));
                if (retired || faulted || current != core) return;
                var body = FindBody(tree.RootElement.GetProperty("root"));
                if (body is null) return;
                using var box = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("DOM.getBoxModel",
                    "{\"nodeId\":" + body.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"));
                if (retired || faulted || current != core) return;
                var points = box.RootElement.GetProperty("model").GetProperty("margin");
                var ys = new[] { points[1].GetDouble(), points[3].GetDouble(), points[5].GetDouble(), points[7].GetDouble() };
                var measured = ys.Max() - ys.Min();
                if (double.IsFinite(measured))
                {
                    var height = Math.Clamp(Math.Ceiling(measured) + 2, 48, 4096);
                    if (Math.Abs(height - ProviderContentHeight) > 1)
                    { ProviderContentHeight = height; ProviderHeightChanged?.Invoke(); }
                }
            } while (measureProviderAgain && !retired);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { } // Keep the bounded fallback viewport if layout is unavailable.
        finally { measuringProviderHeight = false; }

        static int? FindBody(JsonElement node)
        {
            if (node.TryGetProperty("nodeName", out var name) && name.GetString() == "BODY") return node.GetProperty("nodeId").GetInt32();
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray()) if (FindBody(child) is { } id) return id;
            return null;
        }
    }
    private void ConfigureProviderDocument(CoreWebView2Environment env)
    {
        if (!IsProviderContent) return;
        if (providerHtml is not { Count: > 0 and <= 5 } || providerHtml.Sum(html => (long)html.Length) > 64 * 1024)
            throw new InvalidOperationException("Provider document content is missing.");
        core!.Settings.IsScriptEnabled = false;
        header.Visibility = Visibility.Collapsed;
        const string policy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-src 'none'";
        var html = "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
            "<title>Document</title></head><body style='margin:8px'>" + string.Join("\n", providerHtml) + "</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            var allowed = !retired && !faulted && args.Request.Uri == document.Url && args.ResourceContext == CoreWebView2WebResourceContext.Document;
            args.Response = env.CreateWebResourceResponse(new MemoryStream(allowed ? bytes : []).AsRandomAccessStream(),
                allowed ? 200 : 403, allowed ? "OK" : "Forbidden",
                "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: " + policy);
        };
        providerAuthorityPump = CheckProviderLifetimeAsync();
    }
    private async Task CheckProviderLifetimeAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (!visible || CheckProviderAuthority is null) continue;
                await CheckProviderAuthority(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (retired) return;
            faulted = true; CancelPointer(); browsing = interacting = false; web.Visibility = Visibility.Collapsed;
            ShowStatus("This document is no longer available. Refresh its content to try again.");
            UpdateChrome();
        }
    }
    private async Task OpenProviderLinkAsync(string url)
    {
        if (!input || !IsProviderContent || !WidgetRail.WidgetProtocol.WebBrowserDocument.IsWebUrl(url) ||
            new Uri(url).Host.Equals("widgetrail-provider.invalid", StringComparison.OrdinalIgnoreCase)) return;
        if (OpenExternal is { } open && !await open(new Uri(url), lifetime.Token) && input)
            ShowStatus("The link could not be opened. Try again.");
    }
}
