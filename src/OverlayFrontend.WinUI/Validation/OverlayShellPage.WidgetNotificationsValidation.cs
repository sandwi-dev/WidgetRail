using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;
internal sealed partial class OverlayShellPage
{
    private async Task VerifyWidgetNotificationsAsync(Action<bool, string> check)
    {
        var center = notificationCenter!;
        await center.Preferences.SetAsync("webapp-a", NotificationPresentation.BannerAndList, lifetime.Token);
        center.Inbox.Clear();
        // The existing quiet interval is a deliberate policy, not fixture flakiness.
        await Task.Delay(NotificationBannerState.QuietInterval + TimeSpan.FromSeconds(1));
        await Trigger("notify");
        await WebAppUntil(() => notificationBanner?.IsVisible == true && notificationCard?.TitleText.Text == "Synthetic track");
        check(visible && center.Inbox.Entries.Count == 0 && notificationBadge.Visibility == Visibility.Collapsed,
            "Ordinary widget SDK notification traverses the broker and bridge while the overlay is visible without adding pending entries");
        check(!notificationBanner!.Interactive && notificationBannerState.EntryId is not null &&
            center.Inbox.Find(notificationBannerState.EntryId.Value)?.Content is { ShowWidgetIcon: false, BackgroundOpacityPercent: 80 },
            "Song notification is passive and retains rich content and background-opacity options");
        var expires = notificationBannerState.ExpiresAt;
        var originalId = notificationBannerState.EntryId;
        await CapturePopupDecisionAsync(notificationBanner.AutomationRoot, "widget-song");
        await Trigger("notify-update");
        await WebAppUntil(() => center.Inbox.Find(notificationBannerState.EntryId ?? -1)?.Content?.Metadata == "3:46");
        check(notificationBannerState.ExpiresAt == expires && notificationBannerState.EntryId == originalId,
            "Metadata refresh preserves identity and does not extend the active song banner");
        await WebAppUntil(() => notificationBanner?.IsVisible != true);
        await Trigger("notify-update"); await Task.Delay(600);
        check(notificationBanner?.IsVisible != true && center.Inbox.Entries.Count == 0, "Late metadata cannot resurrect an expired banner");
        await Task.Delay(NotificationBannerState.QuietInterval + TimeSpan.FromSeconds(1));
        await Trigger("notify"); SetVisible(false);
        await WebAppUntil(() => notificationBanner?.IsVisible == true);
        check(center.Inbox.Entries.Count == 0, "Background widget notification uses the same passive banner path");
        SetVisible(true); await SelectAsync("webapp-a", enterWidget: true);
        await Trigger("notify-withdraw");
        await WebAppUntil(() => notificationBanner?.IsVisible != true);
        await Trigger("notify-pending");
        await WebAppUntil(() => center.Inbox.Entries.Count == 1);
        check(center.Inbox.Entries[0].Interactive, "Ordinary widget can independently request pending retention and open-widget interaction");
        var pending = OpenNotificationListForValidation();
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        var list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
        list.SelectedIndex = 0; ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released); await pending;
        check(activeWidget == "webapp-a" && center.Inbox.Entries.Count == 0, "Pending ordinary notification opens its owning widget and is removed");
        center.Publish("webapp-a", new object(), new("information", new("information", "Informational fixture")
        { Interaction = WidgetNotificationInteraction.Informational }), () => true);
        pending = OpenNotificationListForValidation();
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
        list.SelectedIndex = 0; ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        check(!hostChoiceDialog!.FocusedChoiceIsActionable, "Informational pending entries do not advertise an Open action");
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        check(HostChoiceActive && center.Inbox.Entries.Count == 1, "A on an informational entry does not activate or dismiss it");
        await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released); await pending;
        async Task Trigger(string id)
        {
            FindControl<Button>(surface!, "Widget." + id)!.Focus(FocusState.Keyboard);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        }
    }
}
