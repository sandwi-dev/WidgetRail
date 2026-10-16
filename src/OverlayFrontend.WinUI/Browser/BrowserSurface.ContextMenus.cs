using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
#if ENABLE_WIDGET_VALIDATION
    internal Action? BeforeContextCommandForValidation { get; set; }
#endif
    internal Func<string, string, Task>? AddWebApp { get; set; }
    private static bool IsNewWindowCommand(CoreWebView2ContextMenuItem item) => item.Name is "openLinkInNewWindow" or "openLinkInNewTab";

    private void ContextMenuRequested(CoreWebView2 owner, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        args.Handled = true;
        if (!CanRequestSiteDecision || HasDialog || Choose is null) return;
        var origin = BrowserSiteIdentity.Origin(owner.Source, requireHttps: false);
        if (origin is null) return;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        var deferral = args.GetDeferral();
        var sourcePage = pageEpoch;
        var link = args.ContextMenuTarget.HasLinkUri ? args.ContextMenuTarget.LinkUri : null;
        string? selectedLink = null;
        string? addAddress = null, addTitle = null;
        BrowserPopupPlacement? selectedPlacement = null;
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
                    var hostedLink = link is not null && document.Options.Popups == WidgetRail.WidgetProtocol.BrowserPopupMode.Hosted && items.Any(IsNewWindowCommand);
                    var choices = new List<HostDialogChoice>();
                    if (hostedLink)
                    {
                        if (RequestPopupPin is not null) choices.Add(new("new.pin", "Open in pinned surface", Group: "New window"));
                        choices.Add(new("new.widget", "Open over current page", Group: choices.Count == 0 ? "New window" : null));
                    }
                    var otherActions = false;
                    for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
                    {
                        var item = items[itemIndex];
                        if (hostedLink && IsNewWindowCommand(item)) continue;
                        choices.Add(new(itemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            BoundWebsiteText(item.Label.Replace("&", ""), 120) + (item.Kind == CoreWebView2ContextMenuItemKind.Submenu ? "…" : item.IsChecked ? " (selected)" : ""),
                            Group: hostedLink && !otherActions ? "Other actions" : null));
                        otherActions = true;
                    }
                    if (groups.Count == 1 && AddWebApp is not null && !IsHostedPackage)
                        choices.Add(new("webapp.add", "Add as webapp", Group: "This page"));
                    if (groups.Count > 1) choices.Add(new("back", "Back"));
                    var answer = await Choose!(this, "Page actions", origin, choices, token, focusFirstChoice: true);
                    token.ThrowIfCancellationRequested();
                    if (answer == "webapp.add" && choices.Any(choice => choice.Id == answer))
                    { if (CanCompleteSiteDecision && ReferenceEquals(owner, core)) { addAddress = owner.Source; addTitle = owner.DocumentTitle; } break; }
                    if (answer == "back") { groups.Pop(); continue; }
                    if (answer is "new.pin" or "new.widget" && choices.Any(choice => choice.Id == answer))
                    {
                        if (CanCompleteSiteDecision && ReferenceEquals(owner, core))
                        {
                            selectedLink = link;
                            selectedPlacement = answer == "new.pin" ? BrowserPopupPlacement.Pinned : BrowserPopupPlacement.InWidget;
                            TraceNewWindow("context-link-selected");
#if ENABLE_WIDGET_VALIDATION
                            BeforeContextCommandForValidation?.Invoke();
#endif
                        }
                        break;
                    }
                    if (!int.TryParse(answer, out var index) || index < 0 || index >= items.Length) break;
                    var selected = items[index];
                    if (selected.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                    { if (groups.Count < 4) groups.Push(selected.Children.ToArray()); else break; }
                    else
                    {
                        if (CanCompleteSiteDecision && ReferenceEquals(owner, core))
                        {
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
                deferral.Complete();
                InteractionChanged?.Invoke();
                if (addAddress is { } website)
                    DispatcherQueue.TryEnqueue(() => { if (!token.IsCancellationRequested && sourcePage == pageEpoch && AddWebApp is { } add) Run(() => add(website, addTitle ?? "")); });
                if (selectedLink is { } url)
                {
                    // Finish the menu decision before opening a replacement. A
                    // requesting child can then retire without joining itself.
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested || sourcePage != pageEpoch || !ReferenceEquals(owner, core))
                        { TraceNewWindow("rejected-stale-context-link"); return; }
                        (popupParent ?? this).AdmitNewWindow(this, new NewWindowRequest(url, token, placement: selectedPlacement));
                    });
                }
            }
        });
    }

    private async Task OpenContextMenuAsync()
    {
        if (!CanOpenPageActions || !CanRequestSiteDecision || HasDialog || core is not { } current) return;
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
