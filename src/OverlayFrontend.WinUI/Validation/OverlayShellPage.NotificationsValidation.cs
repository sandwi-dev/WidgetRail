using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyNotificationsAsync(BrowserSurface page, Action<bool, string> check)
    {
        var inbox = browserOwner!.Notifications;
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.ListOnly, lifetime.Token);
        await page.EvaluateFixtureAsync("window.notificationEvents=[];window.n=new Notification('Synthetic notification',{body:'Local fixture only',tag:'thread'});n.onshow=()=>notificationEvents.push('shown');n.onclick=()=>notificationEvents.push('clicked');n.onclose=()=>notificationEvents.push('closed');");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        var entry = inbox.Entries[0];
        check(await page.EvaluateFixtureAsync("notificationEvents.length===0") == "true", "Intercepted list-only notification does not use Windows default UI or claim premature display");
        check(inbox.MarkShown(entry.Id) && inbox.MarkShown(entry.Id), "Custom notification accepts a single host display acknowledgement");
        await WebAppUntilScript(page, "notificationEvents.join(',')==='shown'");
        check(await inbox.ActivateAsync(entry.Id, _ => Task.FromResult(true)), "Native notification click callback completes");
        try { await WebAppUntilScript(page, "notificationEvents.join(',')==='shown,clicked'"); }
        catch (TimeoutException error) { throw new InvalidOperationException("Synthetic notification callbacks: " + await page.EvaluateFixtureAsync("notificationEvents"), error); }
        check(inbox.Entries.Count == 0, "Successful notification activation removes the pending entry and delivers click once");
        await page.EvaluateFixtureAsync("window.n=new Notification('Dismiss me');n.onclose=()=>notificationEvents.push('dismissed');");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        inbox.MarkShown(inbox.Entries[0].Id); inbox.Dismiss(inbox.Entries[0].Id);
        await WebAppUntilScript(page, "notificationEvents.includes('dismissed')");
        check(true, "Explicit custom dismissal delivers the website close event");
        await page.EvaluateFixtureAsync("window.n=new Notification('Withdraw me',{tag:'withdraw'});");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        await page.EvaluateFixtureAsync("n.close()");
        await WebAppUntil(() => inbox.Entries.Count == 0);
        check(true, "Website notification.close withdraws the custom pending notification");
        await page.EvaluateFixtureAsync("window.n=new Notification('First',{tag:'replace'});");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        var previous = inbox.Entries[0].Id;
        await page.EvaluateFixtureAsync("window.n=new Notification('Replacement',{tag:'replace'});");
        await WebAppUntil(() => inbox.Entries.Count == 1 && inbox.Entries[0].Id != previous);
        check(inbox.Entries[0].Title == "Replacement", "Website tags replace an existing entry within the same account/origin");
        page.NavigateForValidation("https://shell.fixture.invalid/primary");
        await WebAppUntil(() => !page.IsNavigationInProgress && page.CurrentTitle == "Shell /primary" && inbox.Entries[0].Owner is null);
        check(!await inbox.ActivateAsync(inbox.Entries[0].Id, _ => throw new InvalidOperationException("Must not activate another document")),
            "Navigation releases native callbacks and leaves a dismissible stale entry instead of targeting the new page");
        inbox.Clear();
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.Off, lifetime.Token);
        await page.EvaluateFixtureAsync("window.notificationShown=false;window.n=new Notification('Off');n.onshow=()=>notificationShown=true;");
        await Task.Delay(100);
        check(inbox.Entries.Count == 0 && await page.EvaluateFixtureAsync("notificationShown===false") == "true", "Off mode neither retains a native notification nor displays OS UI");
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.ListOnly, lifetime.Token);
        await VerifyNotificationControlsAsync(page, check);
        await VerifyStaleNotificationPanelAsync(check);
        if (Environment.GetCommandLineArgs().Contains("--widget-notifications")) await VerifyWidgetNotificationsAsync(check);
        GC.Collect();
        await Task.Run(GC.WaitForPendingFinalizers);
        check(true, "Retired native notification projections survive forced collection without finalizer-thread browser calls");
    }

    private async Task VerifyNotificationControlsAsync(BrowserSurface page, Action<bool, string> check)
    {
        var inbox = browserOwner!.Notifications;
        await page.EvaluateFixtureAsync("window.notificationEvents=[];window.n=new Notification('Controller notification',{body:'Synthetic list action'});n.onshow=()=>notificationEvents.push('shown');n.onclick=()=>notificationEvents.push('clicked');n.onclose=()=>notificationEvents.push('closed');");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        var pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        await WebAppUntil(() => inbox.Entries[0].WasShown);
        check(notificationBadge.Visibility == Visibility.Visible && notificationCount.Text == "1", "Bell counts pending entries rather than unread entries");
        foreach (var count in new[] { "1", "8", "99+" })
        {
            notificationCount.Text = count;
            await CapturePopupDecisionAsync(notificationBell, "notification-badge-" + count.Replace("+", "plus"));
        }
        RefreshNotificationBell();
        await CapturePopupDecisionAsync(utilityPanel!, "notifications");
        var panelBounds = utilityPanel!.TransformToVisual(null).TransformBounds(new(0, 0, utilityPanel.ActualWidth, utilityPanel.ActualHeight));
        var bellBounds = notificationBell.TransformToVisual(null).TransformBounds(new(0, 0, notificationBell.ActualWidth, notificationBell.ActualHeight));
        check(panelBounds.Width > 0 && panelBounds.Bottom <= bellBounds.Y &&
            panelBounds.X <= bellBounds.X + bellBounds.Width && panelBounds.Right >= bellBounds.X,
            $"Pending panel is anchored above the bell and stays within the native root: panel={panelBounds}, bell={bellBounds}");
        ReturnToUtilityIcon();
        check(UtilityIconFocused, "Returning to the icon leaves the panel visible");
        Receive(new ControllerFrame { Connected = 1 });
        await Press(ControllerButton.X);
        check(inbox.Entries.Count == 1, "X on the notification dialog Close control does not dismiss an unfocused entry");
        await Press(ControllerButton.B); await pending;
        await WebAppUntilScript(page, "notificationEvents.includes('shown')");
        check(inbox.Entries.Count == 1, "B closes the notification list without dismissing pending entries");

        await page.EvaluateFixtureAsync("n.onclose=()=>{notificationEvents.push('closed');window.n=new Notification('New while open');n.onclick=()=>notificationEvents.push('clicked');};");
        pending = OpenNotificationListForValidation();
        await FocusEntry(); await Press(ControllerButton.X);
        check(utilityPanel is not null, "X dismisses the focused notification and keeps the list open");
        await WebAppUntil(() => inbox.Entries.Count == 1 && inbox.Entries[0].Title == "New while open");
        await FocusEntry(); await Press(ControllerButton.A); await pending;
        await WebAppUntilScript(page, "notificationEvents.includes('closed') && notificationEvents.includes('clicked')");
        check(inbox.Entries.Count == 0 && page.IsBrowsing && activeWidget == "webapp-a", "A opens a notification appended to the live list and focuses its exact browser document");

        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.BannerAndList, lifetime.Token);
        await page.EvaluateFixtureAsync("window.n=new Notification('Visible overlay notification',{tag:'banner'});");
        await WebAppUntil(() => inbox.Entries.Count == 1 && notificationBanner?.IsVisible == true);
        check(notificationBanner!.IsVisible, "Website notifications show a passive banner with the overlay visible");
        var visibleBannerId = notificationBannerState.EntryId;
        SetVisible(false);
        await WebAppUntil(() => notificationBanner?.IsVisible == true);
        check(notificationBannerState.EntryId == visibleBannerId, "Hiding the overlay preserves the same banner and its original lifetime");
        await CapturePopupDecisionAsync(notificationBanner!.AutomationRoot, "notification-banner");
        var expires = notificationBannerState.ExpiresAt;
        var bannerWindow = notificationBanner!;
        check(bannerWindow.Bounds.Width > 0 && bannerWindow.Bounds.Height > 0 && !bannerWindow.Interactive,
            "A hidden overlay uses a passive banner through the shared pinned-window presentation");
        await page.EvaluateFixtureAsync("window.n=new Notification('Burst replacement',{tag:'banner'});");
        await WebAppUntil(() => notificationCard!.TitleText.Text == "Burst replacement");
        check(notificationBannerState.ExpiresAt == expires && inbox.Entries.Count == 1, "A tagged burst updates its banner without extending the display duration");
        await WebAppUntil(() => !bannerWindow.IsVisible);
        check(inbox.Entries.Count == 1, "Banner expiry leaves the notification pending until the user acts");
        await page.EvaluateFixtureAsync("window.n=new Notification('Quiet interval');");
        await WebAppUntil(() => inbox.Entries.Count == 2);
        check(!bannerWindow.IsVisible, "The quiet interval prevents a new notification from immediately showing another banner");
        inbox.Clear();
        SetVisible(true); await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => foreground && page.IsBrowsing);
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.ListOnly, lifetime.Token);

        async Task FocusEntry()
        {
            await WebAppUntil(() => utilityPanel is { IsLoaded: true });
            var list = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
            await WebAppUntil(() => list.Items.Count > 0);
            // The next A/X can arrive before the queued selection repaint.
            list.SelectedIndex = -1;
            check(((Control)list.Items[0]).Focus(FocusState.Keyboard), "Pending notification receives native controller focus");
            Receive(new ControllerFrame { Connected = 1 });
        }
        async Task Press(ControllerButton button)
        { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
    }

    private async Task VerifyStaleNotificationPanelAsync(Action<bool, string> check)
    {
        var inbox = browserOwner!.Notifications;
        var callbacks = new NotificationCallbacks(() => false, () => throw new InvalidOperationException("Stale shown"),
            () => throw new InvalidOperationException("Stale click"), () => { }, () => { });
        var missing = inbox.Add("removed-widget", "default", "https://fixture.invalid", "", "Old notification", "Its widget was removed", new object(), callbacks, NotificationPresentation.ListOnly)!;
        var pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        var dialog = utilityPanel!;
        var list = FindControl<ListView>(dialog, "Overlay.Utility.List")!;
        list.SelectedIndex = 0; ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        Receive(new ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        check(ReferenceEquals(utilityPanel, dialog) && inbox.Find(missing.Id) is null,
            "Missing widget notification is dismissed inside the same panel without a warning dialog");
        check(FindControl<TextBlock>(dialog, "Overlay.Utility.Status")!.Text == "This notification is no longer available.",
            "Unavailable notification explains itself inline with no extra action");
        await CapturePopupDecisionAsync(dialog, "notifications-unavailable");
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released); await pending;

        var stale = inbox.Add("webapp-a", "default", "https://fixture.invalid", "", "Session ended", "Open the webapp normally", new object(), callbacks, NotificationPresentation.ListOnly)!;
        pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        dialog = utilityPanel!; list = FindControl<ListView>(dialog, "Overlay.Utility.List")!;
        list.SelectedIndex = 0; ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        var arriving = inbox.Add("webapp-a", "default", "https://fixture.invalid", "", "New arrival", "Keep selection stable", new object(), callbacks, NotificationPresentation.ListOnly)!;
        check(ReferenceEquals(focused, Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)) && utilityPanel!.FocusedId == stale.Id.ToString(),
            "New arrivals preserve the user's selected notification and native focus");
        await WebAppUntil(() => list.Items.Count == 2 && ((Control)list.Items[1]).IsLoaded && ((Control)list.Items[1]).ActualHeight > 0);
        check(((ListViewItem)list.Items[1]).Content is NotificationCard { TitleText.Text: "New arrival" },
            "Newly appended notification has a live rich card");
        await CapturePopupDecisionAsync(dialog, "notifications-stale");
        Receive(new ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released); await pending;
        check(activeWidget == "webapp-a" && inbox.Find(stale.Id) is null && utilityPanel is null,
            "A on a stale notification opens its widget and removes the entry without requiring confirmation");
        inbox.Clear();
        SetInteractive(false); RefreshRadialChooser();
        check(notificationBell.Focus(FocusState.Keyboard), "Notification bell receives native controller focus beside the clock");
        pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        check(notificationBadge.Visibility == Visibility.Collapsed && notificationBell.Visibility == Visibility.Visible,
            "Empty notifications hide the badge while retaining the bell");
        await CapturePopupDecisionAsync(utilityPanel!, "notifications-empty");
        inbox.Add("webapp-a", "default", "https://fixture.invalid", "", "After empty", "Clear all footer", new object(), callbacks, NotificationPresentation.ListOnly);
        var footerDialog = utilityPanel!;
        check(FindControl<Button>(footerDialog, "Overlay.Utility.Command")!.Focus(FocusState.Keyboard), "Clear all is a focusable secondary footer action");
        Receive(new ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        check(ReferenceEquals(utilityPanel, footerDialog) && inbox.Entries.Count == 0,
            "Controller Clear all empties the pending panel without closing it");
        check(notificationBell.FocusState != FocusState.Unfocused, "Clearing the last entries returns focus to the bell");
        await SelectAsync("webapp-a", enterWidget: true);
    }

    private async Task VerifyPinnedNotificationAsync(BrowserSurface page, Action<bool, string> check)
    {
        var inbox = browserOwner!.Notifications;
        var pin = pinned!;
        if (await page.EvaluateFixtureAsync("Notification.permission") != "\"granted\"")
        {
            // Allow once belongs to the requesting document, not every child.
            // Grant this synthetic child through the normal permission UI.
            await page.EvaluateGestureFixtureAsync("window.popupPermission='pending';Notification.requestPermission().then(p=>popupPermission=p)");
            await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
            var permissions = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
            permissions.SelectedIndex = 0;
            ((Control)permissions.Items[0]).Focus(FocusState.Keyboard);
            Receive(new ControllerFrame { Connected = 1 });
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await WebAppUntil(() => !HostChoiceActive && page.IsBrowsing);
            await WebAppUntilScript(page, "popupPermission==='granted'");
        }
        await page.EvaluateFixtureAsync("window.pinClicked=false;window.n=new Notification('Pinned notification');n.onclick=()=>pinClicked=true;");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        ExitPinnedInteraction(restoreMain: true);
        await WebAppUntil(() => foreground && !PinnedInputActive);
        var pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        var list = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
        list.SelectedIndex = 0;
        ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        Receive(new ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        await pending;
        await WebAppUntilScript(page, "pinClicked===true");
        check(ReferenceEquals(pinned, pin) && PinnedInputActive && page.IsBrowsing && inbox.Entries.Count == 0,
            "Notification A re-enters its existing pinned page without moving it into the main widget");
    }

    private Task OpenNotificationListForValidation()
    {
        // Model a user action posted by the input pump, not a nested modal
        // opened directly from an ExecuteScript completion on WebView's stack.
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            try { await ShowNotificationsAsync(); finished.TrySetResult(); }
            catch (Exception error) { finished.TrySetException(error); }
        })) finished.TrySetException(new InvalidOperationException("Fixture dispatcher is unavailable."));
        return finished.Task;
    }
}
