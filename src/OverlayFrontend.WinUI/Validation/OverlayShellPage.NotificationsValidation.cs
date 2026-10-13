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
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", WebNotificationPresentation.ListOnly, lifetime.Token);
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
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", WebNotificationPresentation.Off, lifetime.Token);
        await page.EvaluateFixtureAsync("window.notificationShown=false;window.n=new Notification('Off');n.onshow=()=>notificationShown=true;");
        await Task.Delay(100);
        check(inbox.Entries.Count == 0 && await page.EvaluateFixtureAsync("notificationShown===false") == "true", "Off mode neither retains a native notification nor displays OS UI");
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", WebNotificationPresentation.ListOnly, lifetime.Token);
        await VerifyNotificationControlsAsync(page, check);
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
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        await WebAppUntil(() => inbox.Entries[0].WasShown);
        FindControl<Button>(hostChoiceDialog!, "Host.Choice.Close")!.Focus(FocusState.Keyboard);
        Receive(new ControllerFrame { Connected = 1 });
        await Press(ControllerButton.X);
        check(inbox.Entries.Count == 1, "X on the notification dialog Close control does not dismiss an unfocused entry");
        await Press(ControllerButton.B); await pending;
        await WebAppUntilScript(page, "notificationEvents.includes('shown')");
        check(inbox.Entries.Count == 1, "B closes the notification list without dismissing pending entries");

        await page.EvaluateFixtureAsync("n.onclose=()=>{notificationEvents.push('closed');window.n=new Notification('New while open');n.onclick=()=>notificationEvents.push('clicked');};");
        pending = OpenNotificationListForValidation();
        await FocusEntry(); await Press(ControllerButton.X);
        check(HostChoiceActive, "X dismisses the focused notification and keeps the list open");
        await WebAppUntil(() => inbox.Entries.Count == 1 && inbox.Entries[0].Title == "New while open");
        await FocusEntry(); await Press(ControllerButton.A); await pending;
        await WebAppUntilScript(page, "notificationEvents.includes('closed') && notificationEvents.includes('clicked')");
        check(inbox.Entries.Count == 0 && page.IsBrowsing && activeWidget == "webapp-a", "A opens a notification appended to the live list and focuses its exact browser document");

        await browserOwner.NotificationPreferences.SetAsync("webapp-a", WebNotificationPresentation.BannerAndList, lifetime.Token);
        await page.EvaluateFixtureAsync("window.n=new Notification('Visible overlay notification');");
        await WebAppUntil(() => inbox.Entries.Count == 1);
        check(notificationBanner?.IsVisible != true, "A visible overlay receives a quiet pending indicator without a banner");
        inbox.Clear();
        SetVisible(false);
        await page.EvaluateFixtureAsync("window.n=new Notification('Quiet banner',{tag:'banner'});");
        await WebAppUntil(() => notificationBanner?.IsVisible == true);
        var expires = notificationBannerState.ExpiresAt;
        var bannerWindow = notificationBanner!;
        check(bannerWindow.Bounds.Width > 0 && bannerWindow.Bounds.Height > 0 && !bannerWindow.Interactive,
            "A hidden overlay uses a passive banner through the shared pinned-window presentation");
        await page.EvaluateFixtureAsync("window.n=new Notification('Burst replacement',{tag:'banner'});");
        await WebAppUntil(() => notificationTitle.Text == "Burst replacement");
        check(notificationBannerState.ExpiresAt == expires && inbox.Entries.Count == 1, "A tagged burst updates its banner without extending the display duration");
        await WebAppUntil(() => !bannerWindow.IsVisible);
        check(inbox.Entries.Count == 1, "Banner expiry leaves the notification pending until the user acts");
        await page.EvaluateFixtureAsync("window.n=new Notification('Quiet interval');");
        await WebAppUntil(() => inbox.Entries.Count == 2);
        check(!bannerWindow.IsVisible, "The quiet interval prevents a new notification from immediately showing another banner");
        inbox.Clear();
        SetVisible(true); await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => foreground && page.IsBrowsing);
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", WebNotificationPresentation.ListOnly, lifetime.Token);

        async Task FocusEntry()
        {
            await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
            var list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
            await WebAppUntil(() => list.Items.Count > 1);
            list.SelectedIndex = 0;
            check(((Control)list.Items[0]).Focus(FocusState.Keyboard), "Pending notification receives native controller focus");
            Receive(new ControllerFrame { Connected = 1 });
        }
        async Task Press(ControllerButton button)
        { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
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
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        var list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
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
