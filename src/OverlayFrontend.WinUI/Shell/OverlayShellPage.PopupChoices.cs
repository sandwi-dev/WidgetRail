using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private BrowserPopupPreferences? popupPreferences;
    private BrowserPopupPreferences PopupPreferences => popupPreferences ??= new(Path.Combine(options.SettingsRoot, "webapp-popup-placement.json"));

    private async Task<BrowserPopupRequest?> ChooseBrowserPopupPlacementAsync(BrowserSurface source, string? origin, CancellationToken token)
    {
        if (retired || !visible || !source.CanCompleteSiteDecision) return null;
        if (source.RequestPopupPin is null) return new(BrowserPopupPlacement.InWidget);
        var store = PopupPreferences;
        await store.LoadAsync(lifetime.Token).WaitAsync(token);
        var expected = pinned;
        var placement = origin is null ? BrowserPopupPlacement.Ask : store.Get(source.WidgetId, source.ProfileName, origin);
        var remember = false;
        if (placement == BrowserPopupPlacement.Ask || placement == BrowserPopupPlacement.Pinned && expected is not null)
        {
            var widget = catalogItems.FirstOrDefault(item => item.Id == source.WidgetId)?.Name ?? source.WidgetId;
            var existing = expected is null ? null : catalogItems.FirstOrDefault(item => item.Id == expected.WidgetId)?.Name ?? expected.WidgetId;
            HostChoiceDialog? dialog = null;
            var selection = await ShowBrowserChoiceCoreAsync(source, "Where should this pop-out open?",
                $"{widget}\n{origin ?? "This website"}" +
                (existing is null ? "" : $"\n\nOpening pinned will replace the current pin: {existing}."),
                (HostDialogChoice[])[new("pin", existing is null ? "Open pinned" : "Replace pin"), new("widget", "Open in widget")], token,
                configure: choice =>
                {
                    dialog = choice;
                    if (origin is not null) choice.AddRememberChoice("Remember for this website in this account");
                }, directDecisionButtons: true);
            if (selection is not ("pin" or "widget")) return null;
            placement = selection == "pin" ? BrowserPopupPlacement.Pinned : BrowserPopupPlacement.InWidget;
            remember = dialog?.RememberChoice == true;
        }
        token.ThrowIfCancellationRequested();
        if (retired || !visible || !source.CanCompleteSiteDecision || placement == BrowserPopupPlacement.Pinned && !ReferenceEquals(pinned, expected)) return null;
        if (remember && origin is not null) await SavePopupPreferenceAsync(source, origin, placement, token);
        token.ThrowIfCancellationRequested();
        if (placement == BrowserPopupPlacement.InWidget) return new(placement);
        return new(placement, async child =>
        {
            if (retired || !ReferenceEquals(pinned, expected)) return false;
            await PinBrowserPopupAsync(source.WidgetId, expected, child, enforceExpected: true);
            return pinned?.Popup is { IsCurrent: true } popup && ReferenceEquals(popup.Child, child);
        });
    }

    private async Task ManageBrowserPopupPlacementAsync(BrowserSurface source, string origin, CancellationToken token)
    {
        var store = PopupPreferences;
        await store.LoadAsync(lifetime.Token).WaitAsync(token);
        var current = store.Get(source.WidgetId, source.ProfileName, origin);
        var selection = await ShowBrowserChoiceAsync(source, "Pop-out placement",
            $"{origin}\nCurrent: {PopupPlacementLabel(current)}\n\nApplies to new hosted pop-outs from this website in this widget and account, including sign-in windows. Existing pop-outs stay where they are.",
            (HostDialogChoice[])[new("ask", "Ask each time"), new("widget", "Open in widget"), new("pin", "Open pinned")], token);
        if (selection is not ("ask" or "widget" or "pin")) return;
        await SavePopupPreferenceAsync(source, origin, selection switch
        { "pin" => BrowserPopupPlacement.Pinned, "widget" => BrowserPopupPlacement.InWidget, _ => BrowserPopupPlacement.Ask }, token);
    }
    private static string PopupPlacementLabel(BrowserPopupPlacement placement) => placement switch
    { BrowserPopupPlacement.InWidget => "Open in widget", BrowserPopupPlacement.Pinned => "Open pinned", _ => "Ask each time" };

    private async Task SavePopupPreferenceAsync(BrowserSurface source, string origin, BrowserPopupPlacement placement, CancellationToken token)
    {
        try { await PopupPreferences.SetAsync(source.WidgetId, source.ProfileName, origin, placement, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowBrowserChoiceAsync(source, "Could not remember this choice",
                "The saved preference has not changed. You can still open this pop-out and try saving the preference again later.", [], token);
        }
    }
}
