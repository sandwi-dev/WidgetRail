using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Action<ControllerFrame>? NativePickerFrameForValidation { get; set; }
    internal Func<bool>? NativePickerReadyForValidation { get; set; }
    internal Func<string>? NativePickerStatusForValidation { get; set; }
    internal Func<(int X, int Y, bool Visible)>? NativePickerPositionForValidation { get; set; }
    internal Func<nint>? NativePickerWindowForValidation { get; set; }
    internal Action<bool>? NativePickerBlockCloseForValidation { get; set; }
    internal Action<bool>? NativePickerVisibilityForValidation { get; set; }
    internal void EnableWebAppValidation(string path, string? sharingSource = null)
    {
        BrowserOwner.FixtureBrowserArguments = "--use-fake-device-for-media-stream";
        BrowserSurface.FixturePage = url => "<!doctype html><title>Shell " + new Uri(url).AbsolutePath + "</title><body style='background:#203040;color:white;font:22px sans-serif'>Local webapp fixture<input id='upload' type='file'><div contenteditable='true'>Synthetic editor</div></body>";
        Loaded += async (_, _) =>
        {
            fullscreenLayer.TraceForValidation = message => expansionFocusTrace.Add(message);
            var checks = new List<string>(); string? error = null; var step = "startup";
            try
            {
                if (startup is not null) await startup;
                await WebAppUntil(() => activeWidget == "webapp-a" && interactive && foreground && !switching);
                var first = await CurrentBrowser();
                await WebAppUntil(() => first.IsBrowsing && browserOwner!.BackgroundSessionCount == 1);
                if (Environment.GetCommandLineArgs().Contains("--extensions-ui-only"))
                {
                    step = "extension management";
                    await VerifyExtensionManagementAsync(first, Check);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--webapp-notifications-only"))
                {
                    step = "notification integration";
                    await first.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications,
                        "https://shell.fixture.invalid", CoreWebView2PermissionState.Allow);
                    first.NavigateForValidation("https://shell.fixture.invalid/primary?notifications=1");
                    await WebAppUntil(() => first.IsBrowsing && !first.IsNavigationInProgress);
                    await VerifyNotificationsAsync(first, Check);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--website-settings-only"))
                {
                    step = "website settings";
                    await VerifyWebsiteSettingsAsync(first, Check);
                    await PinAsync(first.WidgetId, WidgetPinnedProjection.FullWidgetLayoutId);
                    await EnterPinnedAsync();
                    await WebAppUntil(() => PinnedInputActive && first.IsBrowsing &&
                        ReferenceEquals(first.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot));
                    await VerifyWebsiteSettingsAsync(first, Check);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--webapp-widget-stop"))
                {
                    step = "persistent widget stop";
                    await SelectAsync("webapp-b");
                    var neighbor = await CurrentBrowser();
                    await neighbor.EvaluateFixtureAsync("window.neighborStopMarker='preserved'");
                    await SelectAsync("webapp-a");
                    await WebAppUntil(() => first.IsBrowsing);
                    await first.EvaluateFixtureAsync("localStorage.setItem('widget-stop-fixture','preserved')");
                    await PopupPreferences.SetAsync(first.WidgetId, first.ProfileName, "https://shell.fixture.invalid", BrowserPopupPlacement.InWidget, lifetime.Token);
                    await first.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/popup')");
                    await WebAppUntil(() => first.PinnablePopup is { CurrentTitle: "Shell /popup", IsNavigationInProgress: false });
                    await first.PinnablePopup!.EvaluateFixtureAsync("window.canvas=document.createElement('canvas');canvas.width=160;canvas.height=90;document.body.append(canvas);window.syntheticStream=canvas.captureStream(5)");
                    await PinAsync("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId);
                    await WebAppUntil(() => pinned?.WidgetId == "webapp-a");
                    var savedPlacement = pinnedPreferences.Find("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId);
                    var stopping = StopWidgetAsync(catalogItems.Single(item => item.Id == "webapp-a"));
                    await Choose(0); await stopping;
                    await WebAppUntil(() => pinned is null && browserOwner!.PermissionTargets("webapp-a").Length == 0);
                    Check(owner!.Session.GetTarget("webapp-a").Descriptor.IsStopped && surface is null && showingStoppedWidget,
                        "Stop retires the full widget pin, hosted pop-out and live synthetic media with its worker");
                    Check(pinnedPreferences.Find("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId) == savedPlacement, "Stop preserves remembered pin geometry");
                    await SelectAsync("webapp-b");
                    Check(ReferenceEquals(await CurrentBrowser(), neighbor) && await neighbor.EvaluateFixtureAsync("neighborStopMarker==='preserved'") == "true",
                        "Stopping one webapp preserves its neighbor's live page and state");
                    await SelectAsync("webapp-a");
                    Check(surface is null && browserOwner!.PermissionTargets("webapp-a").Length == 0, "Selecting a stopped webapp creates no WebView");
                    Check(WidgetSurface.Visibility == Visibility.Collapsed, "Stopped presentation has no empty widget background panel");
                    await CapturePopupDecisionAsync(ProductionRoot, "stopped-widget");
                    await Press(ControllerButton.X);
                    var restarted = await CurrentBrowser();
                    Check(!ReferenceEquals(first, restarted) && await restarted.EvaluateFixtureAsync("localStorage.getItem('widget-stop-fixture')==='preserved'") == "true",
                        "Explicit Reload creates a fresh webapp while preserving its saved profile data");
                    Check(WidgetSurface.Visibility == Visibility.Visible, "Reload restores the live widget background panel");
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--webapp-dialog-presentation"))
                {
                    step = "dialog presentation";
                    await VerifyDialogPresentationAsync(Check);
                    await WebAppUntil(() => first.IsBrowsing);
                }
                if (Environment.GetCommandLineArgs().Contains("--webapp-passive-pin"))
                {
                    step = "passive pin visibility";
                    var cycleArgument = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--webapp-pin-cycles=", StringComparison.Ordinal));
                    var cycles = cycleArgument is null ? 1 : int.Parse(cycleArgument.Split('=', 2)[1], System.Globalization.CultureInfo.InvariantCulture);
                    if (cycles is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(cycles));
                    for (var cycle = 1; cycle <= cycles; cycle++)
                        await VerifyPassiveBrowserPinAsync(first, cycles == 1 ? path : path + ".cycle-" + cycle, Check);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--settings-updates-menus"))
                {
                    step = "tiered menus and rail Back";
                    await VerifyTieredMenusAsync(Check);
                }
                if (Environment.GetCommandLineArgs().Contains("--settings-updates-only"))
                {
                    step = "startup update notification";
                    await VerifyAppUpdateNotificationAsync(Check);
                    step = "widget notification intents";
                    await VerifyWidgetNotificationsAsync(Check);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, step, checks }));
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--webapp-new-window-flow"))
                {
                    step = "new-window flow";
                    await VerifyNewWindowFlowAsync(first, Check);
                }
                await VerifySpatialRoutingAsync(first, "main", Check);
                if (Environment.GetCommandLineArgs().Contains("--webapp-popup-placement")) await VerifyPopupPlacementChoicesAsync(first, Check);
                // Other popup lifetime tests deliberately choose the established
                // in-widget route; placement admission is covered separately.
                await PopupPreferences.SetAsync(first.WidgetId, first.ProfileName, "https://shell.fixture.invalid", BrowserPopupPlacement.InWidget, lifetime.Token);
                Check(!first.IsToolbarVisible, "Public webapp declaration opens without permanent browser chrome");
                var height = first.CompositionRoot.ActualHeight;
                await Press(ControllerButton.LeftStick);
                Check(first.IsToolbarVisible, "Shell LS opens on-demand webapp controls");
                await Press(ControllerButton.B);
                Check(!first.IsToolbarVisible && Math.Abs(first.CompositionRoot.ActualHeight - height) < 1, "B dismisses chrome without changing the viewport");

                step = "expanded browser";
                var navigation = first.NavigationCount;
                Check(await ExpandBrowserAsync(first, lifetime.Token), "Existing browser enters the shared expanded presentation");
                await WebAppUntil(() => expandedBrowser?.Surface == first && first.IsBrowsing);
                await WebAppUntil(() => first.CompositionRoot.ActualHeight > height);
                Check(first.NavigationCount == navigation, "Expansion enlarges the native viewport without navigating or creating another session");
                await VerifySpatialRoutingAsync(first, "expanded", Check);
                await Press(ControllerButton.LeftStick); await Press(ControllerButton.B);
                Check(expandedBrowser?.Surface == first && !first.IsToolbarVisible, "Expanded LS and B preserve the normal toolbar interaction");
                await first.EvaluateGestureFixtureAsync("setTimeout(()=>alert('Expanded local fixture'),0)");
                await Choose(0);
                await WebAppUntil(() => first.IsBrowsing && !HostChoiceActive);
                Check(expandedBrowser?.Surface == first, "Expanded website dialog returns focus to the same page");
                await Press(ControllerButton.B);
                await WebAppUntil(() => expandedBrowser is null && Slots(surface).Any(slot => slot.Surface == first) && first.IsBrowsing);
                Check(first.NavigationCount == navigation, "B restores the original main placement with its native session intact");

                step = "shared profile";
                FindControl<Button>(surface!, "Widget.second")!.Focus(FocusState.Keyboard);
                await Press(ControllerButton.A);
                await WebAppUntil(() => Slots(surface).Count(slot => slot.Surface is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Shell /primary" or "Shell /secondary" }) == 2 && browserOwner!.BackgroundSessionCount == 2);
                var second = Slots(surface).Select(slot => slot.Surface).Single(page => page is not null && page != first)!;
                await first.EvaluateFixtureAsync("localStorage.setItem('shared-test','present');document.cookie='shared-test=present;path=/;Secure'");
                await WebAppUntilScript(second, "localStorage.getItem('shared-test')==='present' && document.cookie.includes('shared-test=present')");
                Check(first.ProfileName == second.ProfileName, "Two declared documents use the same widget/account profile");
                await Toolbar(first, "Browser.SiteControls");
                await Choose(8);
                await Choose(0);
                await WebAppUntilScript(first, "location.origin==='https://shell.fixture.invalid' && localStorage.getItem('shared-test')===null");
                await WebAppUntilScript(second, "location.origin==='https://shell.fixture.invalid' && !document.cookie.includes('shared-test=present')");
                await WebAppUntil(() => !HostChoiceActive && !first.IsNavigationInProgress && !second.IsNavigationInProgress && first.CurrentTitle == "Shell /primary" && second.CurrentTitle == "Shell /secondary");
                Check(await first.EvaluateFixtureAsync("localStorage.getItem('shared-test')===null") == "true" &&
                    await second.EvaluateFixtureAsync("!document.cookie.includes('shared-test=present')") == "true", "Clear-data confirmation parks and resets every live view of the profile");
                FindControl<Button>(surface!, "Widget.second")!.Focus(FocusState.Keyboard);
                await Press(ControllerButton.A);
                await WebAppUntil(() => browserOwner!.ResidentCount == 1 && browserOwner.BackgroundSessionCount == 1);

                step = "main permission";
                Slots(surface).First(slot => slot.Surface == first).Focus(FocusState.Keyboard);
                await WebAppUntil(() => first.IsBrowsing);
                await first.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(p=>window.permissionResult=p)");
                await Choose(1);
                await WebAppUntilScript(first, "window.permissionResult==='granted'");
                await WebAppUntil(() => first.IsBrowsing && !HostChoiceActive);
                Check(first.IsBrowsing && !HostChoiceActive, "Native permission confirmation restores main browser interaction");
                if (Environment.GetCommandLineArgs().Contains("--webapp-notifications")) await VerifyNotificationsAsync(first, Check);
                await Toolbar(first, "Browser.SiteControls"); await Choose(2); await Choose(2);
                await WebAppUntilPermission(first, CoreWebView2PermissionKind.Notifications, CoreWebView2PermissionState.Deny);
                await WebAppUntil(() => !HostChoiceActive && !first.IsNavigationInProgress && first.CurrentTitle == "Shell /primary");
                Check(await first.EvaluateFixtureAsync("Notification.permission==='denied'") == "true", "Website controls revoke a remembered native permission");

                step = "tray microphone recovery";
                await first.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, "https://shell.fixture.invalid", CoreWebView2PermissionState.Deny);
                var descriptor = catalogItems.Single(item => item.Id == "webapp-a");
                await ShowTrayMenuAsync(descriptor);
                await WebAppUntil(() => trayMenu?.Items.Any(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Browser.Permissions") == true);
                var permissionCommand = trayMenu!.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Browser.Permissions");
                permissionCommand.Command.Execute(null);
                await Choose(0); await Choose(1);
                await WebAppUntilPermission(first, CoreWebView2PermissionKind.Microphone, CoreWebView2PermissionState.Allow);
                await WebAppUntil(() => !HostChoiceActive && !first.IsNavigationInProgress && first.CurrentTitle == "Shell /primary");
                Check((await first.NativeProfile.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == CoreWebView2PermissionKind.Microphone && p.PermissionState == CoreWebView2PermissionState.Allow),
                    "Tray Website permissions changes a denied microphone to Allow");
                await first.NativeProfile.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, "https://shell.fixture.invalid", CoreWebView2PermissionState.Default);
                await SelectAsync("webapp-a", enterWidget: true);
                await WebAppUntil(() => first.IsBrowsing);

                step = "pinned dialogs";
                await PinAsync("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId);
                await WebAppUntil(() => Slots(pinned?.Presenter).Any(slot => slot.Surface == first));
                ReconcilePeerWindowOrder();
                Check(WindowBelow(pinned!.Window.Handle, (nint)hostWindow), "Passive browser pin is behind the main overlay");
                await BeginPinnedAdjustmentAsync();
                Check(PinnedAdjustmentActive && !pinned.Window.Interactive && !first.CanRequestSiteDecision && WindowBelow((nint)hostWindow, pinned.Window.Handle),
                    "Move/resize raises the shared pin above the overlay while host adjustment retains input");
                StepPinnedPlacement(PinnedPlacementDirection.Right, resize: false);
                StepPinnedPlacement(PinnedPlacementDirection.Down, resize: true);
                ReconcilePeerWindowOrder();
                Check(WindowBelow((nint)hostWindow, pinned.Window.Handle), "Moving, resizing and shell order reconciliation keep the adjusting pin above the overlay");
                CancelPinnedAdjustment();
                Check(!PinnedAdjustmentActive && WindowBelow(pinned.Window.Handle, (nint)hostWindow), "Cancelling move/resize restores passive pin ordering");
                await BeginPinnedAdjustmentAsync(opacityOnly: true);
                StepPinnedOpacity(pinned.Window.OpacityPercent < 100 ? PinnedPlacementDirection.Right : PinnedPlacementDirection.Left);
                ReconcilePeerWindowOrder();
                Check(PinnedAdjustmentActive && !pinned.Window.Interactive && WindowBelow((nint)hostWindow, pinned.Window.Handle),
                    "Opacity adjustment raises the shared pin without enabling widget interaction");
                await FinishPinnedAdjustmentAsync(save: true);
                Check(!PinnedAdjustmentActive && WindowBelow(pinned.Window.Handle, (nint)hostWindow), "Saving opacity restores passive pin ordering");
                await EnterPinnedAsync();
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                Check(WindowBelow((nint)hostWindow, pinned.Window.Handle), "Interacting browser pin is above the main overlay without replacing its session");
                await VerifySpatialRoutingAsync(first, "pin", Check);
                await first.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://shell.fixture.invalid", CoreWebView2PermissionState.Default);
                await first.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(p=>window.permissionResult=p)");
                await WebAppUntil(() => hostChoiceDialog?.IsLoaded == true);
                Check(ReferenceEquals(hostChoiceDialog!.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot), "Pinned website consent uses the owning native window");
                Check(string.IsNullOrEmpty(hostChoiceDialog.PrimaryButtonText), "Multi-option website choices use direct actions without a redundant Open button");
                var primary = FindControl<ListView>(hostChoiceDialog, "Host.Choice.Options")!;
                var bounds = primary.TransformToVisual(pinned.Window.AutomationRoot).TransformBounds(new(0, 0, primary.ActualWidth, primary.ActualHeight));
                Check(bounds.Top >= 0 && bounds.Bottom <= hostChoiceDialog.XamlRoot.Size.Height + 1, "Pinned dialog confirmation remains inside the visible window");
                await Choose(0);
                await WebAppUntilScript(first, "window.permissionResult==='granted'");
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                Check(PinnedInputActive && first.IsBrowsing, "Pinned confirmation restores its original interaction owner");
                if (Environment.GetCommandLineArgs().Contains("--webapp-pin-dialog-lifecycle")) await VerifyPinnedDialogExitAsync(first, Check);
                if (Environment.GetCommandLineArgs().Contains("--webapp-notifications")) await VerifyPinnedNotificationAsync(first, Check);
                await first.EvaluateGestureFixtureAsync("window.open('https://shell.fixture.invalid/popup')");
                await WebAppUntil(() => first.HasPopup && first.ActiveSurface.CurrentTitle == "Shell /popup");
                Check(ReferenceEquals(first.ActiveSurface.XamlRoot, pinned.Window.AutomationRoot.XamlRoot), "Hosted popup stays in the pinned webapp window");
                await VerifySpatialRoutingAsync(first.ActiveSurface, "hosted popup", Check);
                await Press(ControllerButton.B); await WebAppUntil(() => !first.HasPopup);
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                Check(PinnedInputActive && first.IsBrowsing, "Closing a pinned popup preserves its parent and pin interaction");
                if (Environment.GetCommandLineArgs().Contains("--webapp-popup-presentation")) await VerifyPopupPresentationAsync(first, Check);
                Check(await ExpandBrowserAsync(first, lifetime.Token), "Pinned browser expands through the same placement owner");
                await WebAppUntil(() => foreground && first.IsBrowsing && expandedBrowser?.Surface == first);
                Check(ReferenceEquals(first.XamlRoot, XamlRoot), "Expanded pinned browser uses the full shell viewport");
                await Press(ControllerButton.B);
                await WebAppUntil(() => expandedBrowser is null && PinnedInputActive && Slots(pinned?.Presenter).Any(slot => slot.Surface == first) && first.IsBrowsing);
                Check(first.IsBrowsing, "Collapsing returns the live browser and controller focus to its pin");
                if (sharingSource is not null)
                {
                    step = "pinned native screen sharing";
                    await VerifyShellScreenSharingAsync(first, path, sharingSource, Choose, Check);
                }
                await UnpinAsync(save: true);
                if (sharingSource is not null && !interactive)
                {
                    var trayFocus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                    Check(foreground && trayFocus is not null && AutomationProperties.GetAutomationId(trayFocus) == "Overlay.Widget.webapp-a",
                        "After chooser reopen, unpin preserves the selected main tray item without granting widget input");
                    await Press(ControllerButton.A);
                }
                try { await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface == first) && foreground && first.IsBrowsing); }
                catch (TimeoutException unpinTimeout)
                {
                    var focus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                    throw new InvalidOperationException($"Post-sharing unpin: samePage={Slots(surface).Any(slot => slot.Surface == first)} visible={visible} interactive={interactive} foreground={foreground} browsing={first.IsBrowsing} picker={nativeBrowserPickerActive} focus={focus?.GetType().Name}/{(focus is null ? "none" : AutomationProperties.GetAutomationId(focus))}", unpinTimeout);
                }

                step = "file picker cancellation";
                await first.EvaluateGestureFixtureAsync("document.getElementById('upload').click()");
                await Choose(0);
                await WebAppUntil(() => SystemFilePickerOpen);
                Receive(new ControllerFrame { Connected = 0 });
                Receive(new ControllerFrame { Connected = 1, Primed = 1 });
                SetVisible(false);
                await WebAppUntil(() => !SystemFilePickerOpen);
                SetVisible(true); await SelectAsync("webapp-a");
                await WebAppUntil(() => foreground && first.IsBrowsing);
                Check(await first.EvaluateFixtureAsync("document.getElementById('upload').files.length===0") == "true", "Hiding cancels the Windows picker and restores the same page without uploading");

                step = "background media";
                await first.EvaluateGestureFixtureAsync("window.mediaState='pending';navigator.mediaDevices.getUserMedia({audio:true,video:true}).then(s=>{if(!s.getTracks().every(t=>t.label.toLowerCase().includes('fake'))){s.getTracks().forEach(t=>t.stop());throw Error('NonFakeDevice');}window.fakeStream=s;window.mediaState='ready';},e=>window.mediaState=e.name)");
                await Choose(0); await Choose(0);
                await WebAppUntilScript(first, "window.mediaState==='ready'");
                await PinAsync("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId);
                await WebAppUntil(() => Slots(pinned?.Presenter).Any(slot => slot.Surface == first));
                Check(await first.EvaluateFixtureAsync("fakeStream.getTracks().every(t=>t.readyState==='live')") == "true", "Pinning preserves the existing native media tracks");
                await UnpinAsync(save: true);
                await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface == first) && first.IsBrowsing);
                Check(await first.EvaluateFixtureAsync("fakeStream.getTracks().every(t=>t.readyState==='live')") == "true", "Unpinning preserves the existing native media tracks");
                var authority = surface!.CurrentBinding!.Frame.Authority;
                await first.EvaluateFixtureAsync("localStorage.setItem('widget-a-only','present')");
                await SelectAsync("webapp-idle");
                await Task.Delay(6500);
                if (Environment.GetCommandLineArgs().Contains("--webapp-memory-policy"))
                {
                    await WebAppUntil(() => ((WebView2)first.CompositionRoot).CoreWebView2.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low);
                    Check(!((WebView2)first.CompositionRoot).CoreWebView2.IsSuspended,
                        "Retained webapp uses Low memory target while hidden without suspending its document");
                    await first.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://shell.fixture.invalid", CoreWebView2PermissionState.Allow);
                    await browserOwner!.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.ListOnly, lifetime.Token);
                    await first.EvaluateFixtureAsync("new Notification('Low memory fixture',{tag:'memory-policy'})");
                    await WebAppUntil(() => browserOwner.Notifications.Entries.Any(entry => entry.Title == "Low memory fixture"));
                    Check(true, "Hidden Low-memory webapp still delivers website notifications to the shared inbox");
                    browserOwner.Notifications.Clear();
                }
                Check(owner!.Session.IsWebBrowserSessionCurrent(authority, "app.primary") &&
                    await first.EvaluateFixtureAsync("fakeStream.getTracks().every(t=>t.readyState==='live')") == "true", "Background retention survives real worker idle timeout without ending fake media");
                Check(browserOwner!.BackgroundSessionCount == 1, "The session owner retains the hidden running webapp");
                await SelectAsync("webapp-a");
                Check(ReferenceEquals((await CurrentBrowser()), first) && await first.EvaluateFixtureAsync("window.mediaState==='ready'") == "true", "Returning to the webapp preserves its native document and stream");
                if (Environment.GetCommandLineArgs().Contains("--webapp-memory-policy"))
                    Check(((WebView2)first.CompositionRoot).CoreWebView2.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal,
                        "Returning from another widget restores Normal on the retained browser");

                step = "resource limits";
                foreach (var id in new[] { "webapp-b", "webapp-c", "webapp-d" })
                {
                    await SelectAsync(id); var other = await CurrentBrowser();
                    Check(await other.EvaluateFixtureAsync("localStorage.getItem('widget-a-only')===null") == "true", "Shell profile isolation keeps another widget's website data private: " + id);
                }
                await WebAppUntil(() => browserOwner!.BackgroundSessionCount == 4);
                await SelectAsync("webapp-e");
                await WebAppUntil(() => !switching && surface?.CurrentBinding?.Frame.Descriptor.Id == "webapp-e");
                Check(browserOwner!.ResidentCount == 4 && browserOwner.BackgroundSessionCount == 4 &&
                    await first.EvaluateFixtureAsync("fakeStream.getTracks().every(t=>t.readyState==='live')") == "true", "A fifth webapp cannot evict four retained sessions or their media");
                await browserOwner.StopWidgetSessionsAsync("webapp-c");
                await CurrentBrowser();
                Check(browserOwner.BackgroundSessionCount == 4, "Stopping a session makes capacity available to the waiting webapp");

                step = "renderer recovery";
                await SelectAsync("webapp-a");
                await first.CrashRendererFixtureAsync();
                await WebAppUntil(() => first.NeedsRecovery && browserOwner.FailedBackgroundSessionCount == 1);
                Check(first.NeedsRecovery && browserOwner.FailedBackgroundSessionCount == 1, "Renderer failure remains available through shared session recovery");
                await Press(ControllerButton.A);
                await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface is { IsReady: true } page && page != first));
                Check(await (await CurrentBrowser()).EvaluateFixtureAsync("typeof window.fakeStream==='undefined'") == "true", "Recovery creates a fresh page through the placement owner");
                if (Environment.GetCommandLineArgs().Contains("--webapp-shutdown"))
                {
                    step = "shutdown";
                    var moves = browserOwner.PlacementMoveCount;
                    PrepareNativeShutdown();
                    await DisposeAsync();
                    Check(browserOwner.ResidentCount == 0 && browserOwner.PlacementMoveCount == moves,
                        "Shutdown drains recovered and retained controllers without parking them into retiring XAML roots");
                }
            }
            catch (Exception failure) { error = failure.ToString(); hostChoiceDialog?.Hide(); await Task.Delay(100); }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = error is null, step, checks, error, expansionFocusTrace, pinState = new { exists = pinned is not null, current = pinned?.IsCurrent, interactive = pinned?.Window.Interactive, focused = pinned?.HasForeground, foreground, expanded = expandedBrowser is not null }, pages = Slots(surface).Select(slot => new { ready = slot.Surface?.IsReady, loading = slot.Surface?.IsNavigationInProgress, title = slot.Surface?.CurrentTitle, profile = slot.Surface?.ProfileName, permissions = slot.Surface?.FixturePermissionTrace }).ToArray(), retained = browserOwner?.BackgroundSessionCount }));

            async Task<BrowserSurface> CurrentBrowser()
            {
                await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Shell /primary" }));
                return Slots(surface).Select(slot => slot.Surface).First(page => page is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Shell /primary" })!;
            }
            async Task Press(ControllerButton button)
            { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
            async Task Toolbar(BrowserSurface page, string id)
            {
                Slots(PinnedInputActive ? pinned?.Presenter : surface).First(slot => slot.Surface == page).Focus(FocusState.Keyboard);
                await WebAppUntil(() => page.IsInteracting);
                if (!page.IsToolbarFocused) await Press(ControllerButton.LeftStick);
                await WebAppUntil(() => FindControl<Button>(page, id)?.IsLoaded == true);
                Check(FindControl<Button>(page, id)!.Focus(FocusState.Keyboard), "Toolbar command receives real native focus: " + id);
                await Press(ControllerButton.A);
            }
            async Task Choose(int index)
            {
                await WebAppUntil(() => hostChoiceDialog?.IsLoaded == true);
                var dialog = hostChoiceDialog!;
                var list = FindControl<ListView>(dialog, "Host.Choice.Options");
                if (list is { IsLoaded: true } && list.Items.Count > 1)
                { list.SelectedIndex = index; (list.Items[index] as Control)!.Focus(FocusState.Keyboard); }
                else FindControl<Button>(dialog, "Host.Choice.Primary")!.Focus(FocusState.Keyboard);
                Receive(new ControllerFrame { Connected = 1 });
                await Press(ControllerButton.A);
                await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(path + ".progress", string.Join('\n', checks)); }
        };
    }
    private static IEnumerable<BrowserSlot> Slots(DependencyObject? root)
    {
        if (root is null) yield break;
        if (root is BrowserSlot slot) { yield return slot; yield break; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Slots(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static bool WindowBelow(nint window, nint above)
    {
        // WinUI has hidden popup HWNDs between visible windows. Assert relative
        // order, not immediate native adjacency after activation creates one.
        for (var count = 0; count < 512 && window != 0; count++)
        {
            window = NativePreviousWindow(window, 3);
            if (window == above) return true;
        }
        return false;
    }
    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetWindow")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial nint NativePreviousWindow(nint window, uint command);
    private static T? FindControl<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T control && AutomationProperties.GetAutomationId(root) == id) return control;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindControl<T>(VisualTreeHelper.GetChild(root, i), id) is { } child) return child;
        return null;
    }
    private static async Task WebAppUntil(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!condition()) { if (Environment.TickCount64 > end) throw new TimeoutException("Webapp shell state timed out."); await Task.Delay(25); }
    }
    private static async Task WebAppUntilScript(BrowserSurface page, string condition)
    {
        var end = Environment.TickCount64 + 15_000;
        while (await page.EvaluateFixtureAsync(condition) != "true")
        { if (Environment.TickCount64 > end) throw new TimeoutException("Webapp shell script timed out: " + condition); await Task.Delay(25); }
    }
    private static async Task WebAppUntilPermission(BrowserSurface page, CoreWebView2PermissionKind kind, CoreWebView2PermissionState state)
    {
        var end = Environment.TickCount64 + 15_000;
        while (!(await page.NativeProfile!.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == kind && p.PermissionState == state))
        { if (Environment.TickCount64 > end) throw new TimeoutException("Native permission update did not complete."); await Task.Delay(25); }
    }
}
