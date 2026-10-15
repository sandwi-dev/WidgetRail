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
    private async Task ShowNotificationPreferencesAsync(BridgeWidgetDescriptor widget, string? categoryId = null)
    {
        if (notificationCenter is not { } center || HostChoiceActive) return;
        var preferences = center.Preferences;
        var selection = selectionVersion;
        var sourcePin = PinnedInputActive ? pinned : null;
        bool Current() => !retired && visible && selectionVersion == selection &&
            (sourcePin is null ? foreground : ReferenceEquals(pinned, sourcePin) && sourcePin.IsCurrent && sourcePin.HasForeground) && catalogItems.Any(item => item.Id == widget.Id && item.RuntimeGeneration == widget.RuntimeGeneration);
        try
        {
            await center.Ready;
            await preferences.EnsureCategoriesAsync(widget.Id, widget.NotificationCategories, lifetime.Token);
            string? returnCategory = null;
            while (Current())
            {
                var category = categoryId;
                if (category is null)
                {
                    var options = new List<HostDialogChoice> { new("$master", $"All notifications · {NotificationModeLabel(preferences.Get(widget.Id))}"),
                        new("general", $"General · {NotificationModeLabel(preferences.Category(widget.Id, "general").Mode)}") };
                    options.AddRange(widget.NotificationCategories.Select(item => new HostDialogChoice(item.Id,
                        $"{item.Name} · {NotificationModeLabel(preferences.Category(widget.Id, item.Id).Mode, item.Retention == WidgetRail.WidgetProtocol.WidgetNotificationRetention.BannerOnly)}")));
                    category = await ShowHostPreferenceAsync("Notification preferences", widget.Name, options.ToArray(), lifetime.Token, pinnedOwner: sourcePin, currentChoiceId: returnCategory, closeLabel: "Done");
                    if (category is null || !Current()) return;
                    returnCategory = category;
                }
                if (category is null or "$master")
                {
                    var mode = await ChooseNotificationModeAsync(widget.Name, preferences.Get(widget.Id), sourcePin);
                    if (mode is not null && Current()) await preferences.SetAsync(widget.Id, mode.Value, lifetime.Token);
                }
                else
                {
                    var declaration = widget.NotificationCategories.FirstOrDefault(item => item.Id == category);
                    if (category != "general" && declaration is null) return;
                    var name = declaration?.Name ?? "General";
                    var bannerOnly = declaration?.Retention == WidgetRail.WidgetProtocol.WidgetNotificationRetention.BannerOnly;
                    string? returnOption = null;
                    while (Current())
                    {
                        var value = preferences.Category(widget.Id, category);
                        var master = preferences.Get(widget.Id);
                        var detail = declaration?.Description ?? "Notifications without a specific category.";
                        if (master != NotificationPresentation.BannerAndList) detail += $"\nAll notifications: {NotificationModeLabel(master)}. This setting takes priority.";
                        var choice = await ShowHostPreferenceAsync(name, detail,
                            (HostDialogChoice[])[new("mode", $"{(bannerOnly ? "Banners" : "Delivery")} · {NotificationModeLabel(value.Mode, bannerOnly)}"),
                                new("opacity", $"Banner opacity · {(value.Opacity is { } opacity ? opacity + "%" : "Widget default")}")], lifetime.Token, pinnedOwner: sourcePin, currentChoiceId: returnOption, closeLabel: categoryId is null ? "Back" : "Done");
                        if (choice is null || !Current()) break;
                        returnOption = choice;
                        if (choice == "mode")
                        {
                            var mode = await ChooseNotificationModeAsync(name, value.Mode, sourcePin, bannerOnly);
                            if (mode is not null && Current()) await preferences.SetCategoryAsync(widget.Id, category, value with { Mode = mode.Value }, lifetime.Token);
                        }
                        else
                        {
                            var choices = Enumerable.Range(0, 13).Select(index => 100 - index * 5)
                                .Append(value.Opacity ?? 100).Distinct().OrderByDescending(opacity => opacity)
                                .Select(opacity => new HostDialogChoice(opacity.ToString(CultureInfo.InvariantCulture), opacity + "%")).ToArray();
                            var selected = await ShowHostPreferenceAsync("Banner opacity", $"{name} · Current: {value.Opacity ?? 100}%", choices, lifetime.Token, pinnedOwner: sourcePin, currentChoiceId: (value.Opacity ?? 100).ToString(CultureInfo.InvariantCulture));
                            if (int.TryParse(selected, out var selectedOpacity) && Current())
                                await preferences.SetCategoryAsync(widget.Id, category, value with { Opacity = selectedOpacity }, lifetime.Token);
                        }
                        HideChangedNotificationBanner(widget.Id);
                    }
                }
                HideChangedNotificationBanner(widget.Id);
                if (categoryId is not null) return;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (Current()) await ShowHostChoiceAsync("Could not save notification preferences", "Your previous preference is still active. Try again.", [], lifetime.Token, pinnedOwner: sourcePin); }
    }
    private async Task<NotificationPresentation?> ChooseNotificationModeAsync(string name, NotificationPresentation current, PinnedSurface? sourcePin, bool bannerOnly = false)
    {
        var choices = bannerOnly ? (HostDialogChoice[])[new("banner", "On"), new("off", "Off")]
            : (HostDialogChoice[])[new("banner", "Banner + list"), new("list", "List only"), new("off", "Off")];
        var currentId = current == NotificationPresentation.BannerAndList ? "banner" : !bannerOnly && current == NotificationPresentation.ListOnly ? "list" : "off";
        var description = bannerOnly ? "Brief banners only; these notifications do not enter the list." : "List only keeps pending notifications and suppresses banners.";
        var choice = await ShowHostPreferenceAsync(bannerOnly ? "Banners" : "Notification delivery", $"{name}\n{description}",
            choices, lifetime.Token, pinnedOwner: sourcePin, currentChoiceId: currentId);
        return choice switch { "banner" => NotificationPresentation.BannerAndList, "list" => NotificationPresentation.ListOnly, "off" => NotificationPresentation.Off, _ => null };
    }
    private void HideChangedNotificationBanner(string widget)
    {
        // Re-rendering a live card would restart motion; apply new appearance on the next notice.
        if (notificationBannerState.Widget == widget) HideNotificationBanner();
    }
    private static string NotificationModeLabel(NotificationPresentation mode, bool bannerOnly = false) => bannerOnly
        ? mode == NotificationPresentation.BannerAndList ? "On" : "Off"
        : mode switch
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
