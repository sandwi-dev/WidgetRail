using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private sealed record ContextWindowAdmission(string Url, long Page, long Expires, CancellationToken Token, TaskCompletionSource Ready);
    private ContextWindowAdmission? contextWindowAdmission;
#if ENABLE_WIDGET_VALIDATION
    internal Action? BeforeContextCommandForValidation { get; set; }
#endif
    private ContextWindowAdmission? TakeContextWindowAdmission(string url)
    {
        var admission = contextWindowAdmission;
        if (admission is null || admission.Url != url || admission.Page != pageEpoch || admission.Token.IsCancellationRequested || Environment.TickCount64 > admission.Expires) return null;
        contextWindowAdmission = null;
        return admission;
    }
    private static bool IsNewWindowCommand(CoreWebView2ContextMenuItem item) => item.Name is "openLinkInNewWindow" or "openLinkInNewTab";

    private void ContextMenuRequested(CoreWebView2 owner, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        args.Handled = true;
        if (!CanRequestSiteDecision || HasDialog || Choose is null) return;
        var origin = BrowserSiteIdentity.Origin(owner.Source, requireHttps: false);
        if (origin is null) return;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        var deferral = args.GetDeferral();
        var commandReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webDialogPending = true; CancelPointer();
        RunSiteDecision(async () =>
        {
            try
            {
                var groups = new Stack<CoreWebView2ContextMenuItem[]>();
                groups.Push(args.MenuItems.ToArray());
                while (groups.Count > 0 && CanCompleteSiteDecision && ReferenceEquals(owner, core))
                {
                    var available = groups.Peek().Where(item => item.IsEnabled && item.Kind != CoreWebView2ContextMenuItemKind.Separator).Take(32).ToArray();
                    var hasNewWindow = available.Any(item => item.Name == "openLinkInNewWindow");
                    // Both native commands target the same hosted-page workflow.
                    var items = available.Where(item => !hasNewWindow || item.Name != "openLinkInNewTab").ToArray();
                    HostDialogChoice[] choices = [.. items.Select((item, index) => new HostDialogChoice(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        (IsNewWindowCommand(item) && document.Options.Popups == WidgetRail.WidgetProtocol.BrowserPopupMode.Hosted ? "Open link in new window…" : BoundWebsiteText(item.Label.Replace("&", ""), 120)) + (item.Kind == CoreWebView2ContextMenuItemKind.Submenu ? "…" : item.IsChecked ? " (selected)" : ""))),
                        .. (groups.Count > 1 ? (HostDialogChoice[])[new("back", "Back")] : [])];
                    var answer = await Choose!(this, "Page actions", origin, choices, token);
                    token.ThrowIfCancellationRequested();
                    if (answer == "back") { groups.Pop(); continue; }
                    if (!int.TryParse(answer, out var index) || index < 0 || index >= items.Length) break;
                    var selected = items[index];
                    if (selected.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                    { if (groups.Count < 4) groups.Push(selected.Children.ToArray()); else break; }
                    else
                    {
                        if (CanCompleteSiteDecision && ReferenceEquals(owner, core))
                        {
                            if (IsNewWindowCommand(selected) && args.ContextMenuTarget.HasLinkUri)
                                contextWindowAdmission = new(args.ContextMenuTarget.LinkUri, pageEpoch, Environment.TickCount64 + 5000, token, commandReady);
#if ENABLE_WIDGET_VALIDATION
                            BeforeContextCommandForValidation?.Invoke();
#endif
                            args.SelectedCommandId = selected.CommandId;
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                webDialogPending = false;
                try { deferral.Complete(); InteractionChanged?.Invoke(); }
                finally { commandReady.TrySetResult(); }
            }
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
