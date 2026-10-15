using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private NotificationCenter? notificationCenter;
    private void InitializeNotifications()
    {
        InitializeNotificationBell();
        owner!.Session.NotificationReceived += ReceiveWidgetNotification;
        owner.Session.ActivityReceived += ReceiveWidgetActivity;
        owner.Session.AppUpdateChanged += ReceiveAppUpdate;
        startupUpdateCheck = CheckAppUpdatesAfterStartupAsync();
        browserOwner!.Notifications.Changed += RefreshWebappStatus;
        browserOwner.NotificationBannerRequested += QueueNotificationBanner;
    }
    private async Task ShowNotificationPreferencesAsync(BridgeWidgetDescriptor widget)
    {
        if (browserOwner is not { } browsers) return;
        var current = browsers.NotificationPreferences.Get(widget.Id);
        var choice = await ShowHostChoiceAsync("Notifications", $"{widget.Name}\nCurrent: {NotificationModeLabel(current)}\nWebsite permission, when applicable, is managed separately.",
            (HostDialogChoice[])[new("banner", "Banner + list"), new("list", "List only"), new("off", "Off")], lifetime.Token);
        if (choice is null || retired) return;
        var mode = choice switch { "banner" => NotificationPresentation.BannerAndList, "list" => NotificationPresentation.ListOnly, _ => NotificationPresentation.Off };
        try
        {
            await browsers.NotificationPreferences.SetAsync(widget.Id, mode, lifetime.Token);
            if (notificationBannerState.Widget == widget.Id && mode != NotificationPresentation.BannerAndList) HideNotificationBanner();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { await ShowHostChoiceAsync("Could not save notification preferences", "Your previous preference is still active. Try again.", [], lifetime.Token); }
    }
    private static string NotificationModeLabel(NotificationPresentation mode) => mode switch
    { NotificationPresentation.BannerAndList => "Banner + list", NotificationPresentation.ListOnly => "List only", _ => "Off" };

    private Task ShowNotificationsAsync() => ShowNotificationsAsync(null);

    private async Task ShowNotificationsAsync(string? status)
    {
        if (browserOwner is not { } browsers || HostChoiceActive) return;
        var inbox = browsers.Notifications;
        var snapshot = inbox.Entries.ToArray();
        var ids = snapshot.Select(entry => entry.Id).ToHashSet();
        var choices = snapshot.Select(NotificationChoice).ToArray();
        var selected = await ShowHostChoiceAsync("Notifications", "", choices, lifetime.Token, configure: dialog =>
        {
            dialog.ConfigurePendingPanel(notificationBell, Appearance.InterfaceScale);
            dialog.SetChoiceContent(choice =>
            {
                if (!long.TryParse(choice.Id, out var number) || inbox.Find(number) is not { } entry) return new TextBlock { Text = choice.Label };
                var card = new NotificationCard(ResolveTrayIconAsync);
                card.Update(entry, catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget), Appearance.TextScale * Appearance.InterfaceScale);
                return card;
            });
            dialog.SecondaryButtonText = "Clear all";
            dialog.ChoiceIsActionable = id => long.TryParse(id, out var number) && inbox.Find(number)?.Interactive == true;
            dialog.SelectionContextChanged += PublishControllerGuide;
            dialog.Closed += (_, _) => dialog.SelectionContextChanged -= PublishControllerGuide;
            dialog.SecondaryButtonClick += (_, args) => { args.Cancel = true; inbox.Clear(); HideNotificationBanner(); };
            dialog.DismissChoiceRequested += id => { if (long.TryParse(id, out var number)) inbox.Dismiss(number); };
            dialog.ChoiceShown += id => { if (long.TryParse(id, out var number)) inbox.MarkShown(number); };
            dialog.KeepOpenOnChoice = id =>
            {
                if (!long.TryParse(id, out var number)) return true;
                if (inbox.Find(number) is { } entry)
                {
                    if (!entry.Interactive) return true;
                    if (catalogItems.Any(widget => widget.Id == entry.Widget)) return false;
                }
                inbox.Dismiss(number);
                dialog.ShowBriefStatus("This notification is no longer available.");
                return true;
            };
            void Update()
            {
                foreach (var id in ids.ToArray())
                    if (inbox.Find(id) is null) { dialog.RemoveChoice(id.ToString(CultureInfo.InvariantCulture)); ids.Remove(id); }
                foreach (var entry in inbox.Entries)
                {
                    if (ids.Add(entry.Id)) dialog.AppendChoice(NotificationChoice(entry));
                    else dialog.UpdateChoice(NotificationChoice(entry));
                }
                dialog.Title = inbox.Entries.Count == 0 ? "Notifications" : $"Notifications · {inbox.Entries.Count}";
                dialog.IsSecondaryButtonEnabled = inbox.Entries.Count > 0 || inbox.OverflowCount > 0;
                dialog.SetPendingStatus(inbox.Entries.Count == 0, inbox.OverflowCount == 0 ? null :
                    $"{inbox.OverflowCount} additional notifications. Open the webapp for its full history.");
                if (notificationBannerState.EntryId is { } banner && inbox.Find(banner) is null) HideNotificationBanner();
            }
            inbox.Changed += Update;
            dialog.Closed += (_, _) => inbox.Changed -= Update;
            Update();
            if (status is not null) dialog.ShowBriefStatus(status);
        });
        if (!long.TryParse(selected, out var id) || retired) return;
        var opened = await inbox.OpenAsync(id, async entry =>
        {
            if (ReferenceEquals(entry.Owner, appUpdateNotificationOwner) && owner is { } updateOwner)
                return await InvokeIntentCoreAsync(prepareNotification: updateOwner.Session.PrepareAppUpdateIntentAsync);
            if (entry.Available && entry.Owner is WidgetRail.WidgetPresentationSession.WidgetPresentationNotification { IntentToken: not null } notification && owner is { } current)
                return await InvokeIntentCoreAsync(prepareNotification: token => current.Session.PrepareNotificationIntentAsync(notification, token));
            if (!entry.Available || entry.Owner is not BrowserSurface source || retired) return false;
            if (pinned is { } pin && pin.WidgetId == entry.Widget && pin.IsCurrent && ReferenceEquals(source.XamlRoot, pin.Window.AutomationRoot.XamlRoot))
            {
                await EnterPinnedAsync();
                return PinnedInputActive && await browsers.FocusNotificationSourceAsync(source);
            }
            await SelectAsync(entry.Widget, enterWidget: true);
            return !retired && foreground && await browsers.FocusNotificationSourceAsync(source);
        }, async entry =>
        {
            if (retired || !visible || !catalogItems.Any(widget => widget.Id == entry.Widget)) { inbox.Dismiss(entry.Id); return false; }
            await SelectAsync(entry.Widget, enterWidget: true);
            return !retired && activeWidget == entry.Widget && !RecoveryVisible;
        });
        if (!opened && !retired && visible && !HostChoiceActive)
            await ShowNotificationsAsync(inbox.Find(id) is null ? "This notification is no longer available." : "Couldn't open the widget. You can try again later.");
    }
    private HostDialogChoice NotificationChoice(NotificationEntry entry)
    {
        var name = catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget)?.Name ?? entry.Widget;
        return new(entry.Id.ToString(CultureInfo.InvariantCulture), $"{name}{(entry.Available ? "" : " · Session ended")}\n{entry.Title}\n{entry.Body}\n{entry.Content?.Metadata}", entry.ContentRevision);
    }
}
