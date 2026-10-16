using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private BrowserPopupPreferences? popupPreferences;
    private BrowserPopupPreferences PopupPreferences => popupPreferences ??= new(Path.Combine(options.SettingsRoot, "webapp-popup-placement.json"));

    private async Task<BrowserPopupRequest?> ChooseBrowserPopupPlacementAsync(BrowserSurface source, BrowserSurface? replacing, string? origin, BrowserPopupPlacement? requestedPlacement, CancellationToken token)
    {
        if (retired || !visible || !source.CanCompleteSiteDecision) return null;
        var expected = pinned;
        var replacingPin = replacing is not null && ReferenceEquals(expected?.Popup?.Child, replacing);
        var canPin = source.RequestPopupPin is not null;
        if ((requestedPlacement is not null && requestedPlacement is not (BrowserPopupPlacement.Pinned or BrowserPopupPlacement.InWidget)) ||
            requestedPlacement == BrowserPopupPlacement.Pinned && !canPin) return null;
        var placement = requestedPlacement ?? (replacing is not null ? (replacingPin ? BrowserPopupPlacement.Pinned : BrowserPopupPlacement.InWidget) :
            origin is null ? BrowserPopupPlacement.Ask : await ReadBrowserPopupPlacementAsync(source, origin, token));
        if (!canPin) placement = BrowserPopupPlacement.InWidget;
        if (replacing is not null || placement == BrowserPopupPlacement.Ask || placement == BrowserPopupPlacement.Pinned && expected is not null)
        {
            var widget = catalogItems.FirstOrDefault(item => item.Id == source.WidgetId)?.Name ?? source.WidgetId;
            var pinnedName = expected is null ? null : catalogItems.FirstOrDefault(item => item.Id == expected.WidgetId)?.Name ?? expected.WidgetId;
            var message = $"{widget}\n{origin ?? "This website"}";
            if (replacing is not null)
            {
                var title = string.IsNullOrWhiteSpace(replacing.CurrentTitle) ? "the existing additional page" : replacing.CurrentTitle;
                message += $"\n\nReplace {title[..Math.Min(title.Length, 160)]}? This closes that page and ends its activity.";
            }
            if (pinnedName is not null && !replacingPin && canPin && requestedPlacement != BrowserPopupPlacement.InWidget)
                message += requestedPlacement == BrowserPopupPlacement.Pinned ? $"\n\nThis also replaces the current pin: {pinnedName}." :
                    $"\n\nChoosing pinned also replaces the current pin: {pinnedName}.";
            HostChoiceDialog? dialog = null;
            HostDialogChoice pinChoice = new("pin", replacing is null && expected is null ? "Open in pinned surface" : "Replace in pinned surface");
            HostDialogChoice widgetChoice = new("widget", replacing is null ? "Open over current page" : "Replace over current page");
            HostDialogChoice[] choices = requestedPlacement is not null
                ? [new(requestedPlacement == BrowserPopupPlacement.Pinned ? "pin" : "widget", "Replace")]
                : !canPin ? [widgetChoice] : replacing is not null && !replacingPin ? [widgetChoice, pinChoice] : [pinChoice, widgetChoice];
            if (requestedPlacement is not null) message += placement == BrowserPopupPlacement.Pinned ? "\n\nThe new window will open in the pinned surface." : "\n\nThe new window will open over the current page.";
            var selection = await ShowBrowserChoiceCoreAsync(source, replacing is not null ? "Replace existing page?" : requestedPlacement is not null ? "Replace pin?" : "Open new window", message,
                choices, token, configure: choice =>
                {
                    dialog = choice;
                    if (requestedPlacement is null && replacing is null && origin is not null && canPin) choice.AddRememberChoice("Remember for this website");
                }, directDecisionButtons: choices.Length == 2);
            if (selection is not ("pin" or "widget")) return null;
            placement = selection == "pin" ? BrowserPopupPlacement.Pinned : BrowserPopupPlacement.InWidget;
            token.ThrowIfCancellationRequested();
            if (dialog?.RememberChoice == true && origin is not null) await SavePopupPreferenceAsync(source, origin, placement, token);
        }
        token.ThrowIfCancellationRequested();
        if (retired || !visible || !source.CanCompleteSiteDecision || !ReferenceEquals(pinned, expected)) return null;
        return new(placement, Pin: async child =>
        {
            if (retired || !ReferenceEquals(pinned, expected)) return false;
            await PinBrowserPopupAsync(source.WidgetId, expected, child, enforceExpected: true);
            return pinned?.Popup is { IsCurrent: true } popup && ReferenceEquals(popup.Child, child);
        }, BeforeReplace: async () =>
        {
            if (retired || !visible || !ReferenceEquals(pinned, expected)) return false;
            // Return the old borrowed controller before it closes, avoiding a
            // queued stale-pin teardown racing the replacement's pin transaction.
            if (replacingPin && expected is { } previous)
            {
                await SavePinnedAsync(previous);
                await UnpinAsync(save: true, previous);
                if (pinned is not null) return false;
                expected = null;
                if (placement == BrowserPopupPlacement.InWidget && activeWidget != source.WidgetId)
                    await SelectAsync(source.WidgetId, enterWidget: true);
            }
            return !retired && visible;
        });
    }

    private async Task<BrowserPopupPlacement> ReadBrowserPopupPlacementAsync(BrowserSurface source, string origin, CancellationToken token)
    {
        await PopupPreferences.LoadAsync(lifetime.Token).WaitAsync(token);
        return PopupPreferences.Get(source.WidgetId, source.ProfileName, origin);
    }

    private async Task SavePopupPreferenceAsync(BrowserSurface source, string origin, BrowserPopupPlacement placement, CancellationToken token)
    {
        try { await PopupPreferences.SetAsync(source.WidgetId, source.ProfileName, origin, placement, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowBrowserChoiceAsync(source, "Could not remember this choice",
                "The saved preference has not changed. You can still open this page and try saving the preference again later.", [], token);
        }
    }
}
