using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private void ContextMenuRequested(CoreWebView2 owner, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        args.Handled = true;
        if (!CanRequestSiteDecision || HasDialog || Choose is null) return;
        var origin = BrowserSiteIdentity.Origin(owner.Source, requireHttps: false);
        if (origin is null) return;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        var deferral = args.GetDeferral();
        webDialogPending = true; CancelPointer();
        RunSiteDecision(async () =>
        {
            try
            {
                var groups = new Stack<CoreWebView2ContextMenuItem[]>();
                groups.Push(args.MenuItems.ToArray());
                while (groups.Count > 0 && CanCompleteSiteDecision && ReferenceEquals(owner, core))
                {
                    var items = groups.Peek().Where(item => item.IsEnabled && item.Kind != CoreWebView2ContextMenuItemKind.Separator).Take(32).ToArray();
                    HostDialogChoice[] choices = [.. items.Select((item, index) => new HostDialogChoice(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        BoundWebsiteText(item.Label.Replace("&", ""), 120) + (item.Kind == CoreWebView2ContextMenuItemKind.Submenu ? "…" : item.IsChecked ? " (selected)" : ""))),
                        .. (groups.Count > 1 ? (HostDialogChoice[])[new("back", "Back")] : [])];
                    var answer = await Choose!(this, "Page actions", $"{document.AccessibleName}\n{origin}", choices, token);
                    token.ThrowIfCancellationRequested();
                    if (answer == "back") { groups.Pop(); continue; }
                    if (!int.TryParse(answer, out var index) || index < 0 || index >= items.Length) break;
                    var selected = items[index];
                    if (selected.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                    { if (groups.Count < 4) groups.Push(selected.Children.ToArray()); else break; }
                    else
                    { if (CanCompleteSiteDecision && ReferenceEquals(owner, core)) args.SelectedCommandId = selected.CommandId; break; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { webDialogPending = false; deferral.Complete(); InteractionChanged?.Invoke(); }
        });
    }

    private async Task OpenContextMenuAsync()
    {
        if (!CanRequestSiteDecision || HasDialog || core is not { } current) return;
        CancelPointer();
        using var metrics = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
        if (!CanRequestSiteDecision || HasDialog || !ReferenceEquals(current, core)) return;
        var viewport = metrics.RootElement.GetProperty("cssVisualViewport");
        var x = cursorX * viewport.GetProperty("clientWidth").GetDouble();
        var y = cursorY * viewport.GetProperty("clientHeight").GetDouble();
        try
        {
            await current.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(
                new BrowserPointerEvent("mousePressed", x, y, button: "right", clickCount: 1, buttons: 2), BrowserJsonContext.Default.BrowserPointerEvent));
        }
        finally
        {
            // Always release even when the down event opens a site-owned menu.
            if (!retired && ReferenceEquals(current, core))
                await current.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(
                    new BrowserPointerEvent("mouseReleased", x, y, button: "right", clickCount: 1), BrowserJsonContext.Default.BrowserPointerEvent));
        }
    }
}
