using System.Globalization;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private void InitializeNotifications()
    {
        browserOwner!.Notifications.Changed += RefreshWebappStatus;
        browserOwner.NotificationBannerRequested += QueueNotificationBanner;
    }
    private async Task ShowNotificationPreferencesAsync(BridgeWidgetDescriptor widget)
    {
        if (browserOwner is not { } browsers) return;
        var current = browsers.NotificationPreferences.Get(widget.Id);
        var choice = await ShowHostChoiceAsync("Notifications", $"{widget.Name}\nCurrent: {NotificationModeLabel(current)}\nWebsite permission is managed separately.",
            (HostDialogChoice[])[new("banner", "Banner + list"), new("list", "List only"), new("off", "Off")], lifetime.Token);
        if (choice is null || retired) return;
        var mode = choice switch { "banner" => WebNotificationPresentation.BannerAndList, "list" => WebNotificationPresentation.ListOnly, _ => WebNotificationPresentation.Off };
        try
        {
            await browsers.NotificationPreferences.SetAsync(widget.Id, mode, lifetime.Token);
            if (notificationBannerState.Widget == widget.Id && mode != WebNotificationPresentation.BannerAndList) HideNotificationBanner();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { await ShowHostChoiceAsync("Could not save notification preferences", "Your previous preference is still active. Try again.", [], lifetime.Token); }
    }
    private static string NotificationModeLabel(WebNotificationPresentation mode) => mode switch
    { WebNotificationPresentation.BannerAndList => "Banner + list", WebNotificationPresentation.ListOnly => "List only", _ => "Off" };

    private async Task ShowNotificationsAsync()
    {
        if (browserOwner is not { } browsers) return;
        var inbox = browsers.Notifications;
        var snapshot = inbox.Entries.ToArray();
        var ids = snapshot.Select(entry => entry.Id).ToHashSet();
        var choices = snapshot.Select(NotificationChoice).Append(new("clear-all", "Clear all")).ToArray();
        var selected = await ShowHostChoiceAsync("Notifications", inbox.OverflowCount == 0
            ? "A opens · X dismisses · B keeps notifications for later."
            : $"{inbox.OverflowCount} additional notifications could not be kept. Open the webapp for its full history.",
            choices, lifetime.Token, configure: dialog =>
            {
                dialog.UseActionList();
                dialog.DismissChoiceRequested += id => { if (long.TryParse(id, out var number)) inbox.Dismiss(number); };
                dialog.ChoiceShown += id => { if (long.TryParse(id, out var number)) inbox.MarkShown(number); };
                void Update()
                {
                    foreach (var id in ids.ToArray())
                        if (inbox.Find(id) is null) { dialog.RemoveChoice(id.ToString(CultureInfo.InvariantCulture)); ids.Remove(id); }
                    foreach (var entry in inbox.Entries)
                    {
                        if (ids.Add(entry.Id)) dialog.AppendChoice(NotificationChoice(entry), "clear-all");
                        else dialog.UpdateChoice(NotificationChoice(entry));
                    }
                    dialog.Title = $"Notifications ({inbox.Entries.Count})";
                    if (notificationBannerState.EntryId is { } banner && inbox.Find(banner) is null) HideNotificationBanner();
                }
                inbox.Changed += Update;
                dialog.Closed += (_, _) => inbox.Changed -= Update;
                Update();
            });
        if (selected == "clear-all") { inbox.Clear(); HideNotificationBanner(); return; }
        if (!long.TryParse(selected, out var id) || retired) return;
        var opened = await inbox.ActivateAsync(id, async entry =>
        {
            if (!entry.Available || entry.Owner is not BrowserSurface source || retired) return false;
            if (pinned is { } pin && pin.WidgetId == entry.Widget && pin.IsCurrent && ReferenceEquals(source.XamlRoot, pin.Window.AutomationRoot.XamlRoot))
            {
                await EnterPinnedAsync();
                return PinnedInputActive && await browsers.FocusNotificationSourceAsync(source);
            }
            await SelectAsync(entry.Widget, enterWidget: true);
            return !retired && foreground && await browsers.FocusNotificationSourceAsync(source);
        });
        if (!opened && !retired && visible)
            await ShowHostChoiceAsync("Notification unavailable", "Its page may have closed or changed. The notification is still in the list; dismiss it or open the widget from the tray.", [], lifetime.Token);
    }
    private HostDialogChoice NotificationChoice(NotificationEntry entry)
    {
        var name = catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget)?.Name ?? entry.Widget;
        return new(entry.Id.ToString(CultureInfo.InvariantCulture), $"{name}{(entry.Available ? "" : " · session ended")}\n{entry.Title}\n{entry.Body}");
    }
}
