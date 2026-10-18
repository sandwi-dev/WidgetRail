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
        // A just-opened startup notice can be dismissed before its original
        // expiry; quiet time still runs from that expiry by design.
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, notificationBannerState.ExpiresAt - Environment.TickCount64)) +
            NotificationBannerState.QuietInterval + TimeSpan.FromSeconds(1));
        await Trigger("notify");
        try { await WebAppUntil(() => notificationBanner?.IsVisible == true && notificationCard?.TitleText.Text == "Synthetic track"); }
        catch (TimeoutException error)
        {
            throw new InvalidOperationException($"Widget notification did not show: command={(FindControl<Button>(surface!, "Widget.notify")?.Content as TextBlock)?.Text}, focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}, ownedA={shellGestures.Owns(ControllerButton.A)}, mode={center.Mode("webapp-a")}, active={activeWidget}, interactive={interactive}", error);
        }
        check(visible && center.Inbox.Entries.Count == 0 && notificationBadge.Visibility == Visibility.Collapsed,
            "Ordinary widget SDK notification traverses the broker and bridge while the overlay is visible without adding pending entries");
        check(!notificationBanner!.Interactive && notificationBannerState.EntryId is not null &&
            center.Inbox.Find(notificationBannerState.EntryId.Value)?.Content is { ShowWidgetIcon: false, BackgroundOpacityPercent: 80 },
            "Song notification is passive and retains rich content and background-opacity options");
        VerifyNotificationWindowOrder(check);
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
        await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface?.CurrentTitle == "Shell /secondary"));
        check(true, "Notification Open delivers its declared intent through Bridge and changes the receiver page");
        await Trigger("second");
        await WebAppUntil(() => Slots(surface).Count() == 1);
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

    private void VerifyNotificationWindowOrder(Action<bool, string> check)
    {
        var banner = notificationBanner!;
        var main = (nint)hostWindow;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        var foregroundBefore = NotificationForegroundWindow();
        using var backdrop = new OverlayDesktopBackdrop();
        using var peer = new PinnedWidgetWindow("layer fixture", main);
        peer.Place(new(0, 0, 32, 32));
        peer.SetContent(new Grid());
        peer.Show();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            // Force the reported bad order, then run the same reconciliation
            // used after main-window activation, resizing and backdrop refresh.
            OverlayWindowFrame.PlaceBelow(banner.Handle, main);
            backdrop.Apply(main, new(0, 0, 64, 64), Microsoft.UI.Colors.Black, .6, true);
            check(WindowBelow(banner.Handle, backdrop.Handle), "Layer fixture reproduces banner behind backdrop " + cycle);
            peer.ReconcileOrder();
            ReconcilePeerWindowOrder();
            check(WindowBelow(backdrop.Handle, main) && WindowBelow(main, banner.Handle) && WindowBelow(peer.Handle, main),
                "Banner stays above main and backdrop while passive pin remains below main " + cycle);
            peer.SetPlacementActive(true);
            ReconcilePeerWindowOrder();
            check(WindowBelow(main, peer.Handle) && WindowBelow(main, banner.Handle) && WindowBelow(backdrop.Handle, main),
                "Pin adjustment and banner ordering preserve the main-over-backdrop invariant " + cycle);
            peer.SetPlacementActive(false);
            // Represent an already-raised peer (e.g. popup or pointer). Repair
            // the banner without raising it over that peer or lowering main.
            OverlayWindowFrame.PlaceAbove(peer.Handle, main);
            OverlayWindowFrame.PlaceBelow(banner.Handle, backdrop.Handle);
            ReconcilePeerWindowOrder();
            check(WindowBelow(banner.Handle, peer.Handle) && WindowBelow(main, banner.Handle) && WindowBelow(backdrop.Handle, main),
                "Banner repair preserves already-raised sibling ordering " + cycle);
            banner.Hide(); banner.Show();
            check(WindowBelow(main, banner.Handle), "Banner reopens on its declared layer " + cycle);
        }
        var passive = WinUIEx.ExtendedWindowStyle.NoActivate | WinUIEx.ExtendedWindowStyle.Transparent;
        check((banner.ExtendedStyle & passive) == passive && !banner.Interactive &&
            NotificationForegroundWindow() == foregroundBefore &&
            ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), focused),
            "Layer repair preserves click-through, no-activate styles, foreground and controller focus");
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial nint NotificationForegroundWindow();
}
