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
    internal void EnableWebAppValidation(string path, string? sharingSource = null)
    {
        BrowserOwner.FixtureBrowserArguments = "--use-fake-device-for-media-stream";
        BrowserSurface.FixturePage = url => "<!doctype html><title>Shell " + new Uri(url).AbsolutePath + "</title><body style='background:#203040;color:white;font:22px sans-serif'>Local webapp fixture<input id='upload' type='file'><div contenteditable='true'>Synthetic editor</div></body>";
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var step = "startup";
            try
            {
                if (startup is not null) await startup;
                await WebAppUntil(() => activeWidget == "webapp-a" && interactive && foreground && !switching);
                var first = await CurrentBrowser();
                await WebAppUntil(() => first.IsBrowsing && browserOwner!.BackgroundSessionCount == 1);
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
                await Choose(1);
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
                await Toolbar(first, "Browser.SiteControls"); await Choose(0); await Choose(2); await Choose(2);
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
                await EnterPinnedAsync();
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                await first.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://shell.fixture.invalid", CoreWebView2PermissionState.Default);
                await first.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(p=>window.permissionResult=p)");
                await WebAppUntil(() => hostChoiceDialog?.IsLoaded == true);
                Check(ReferenceEquals(hostChoiceDialog!.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot), "Pinned website consent uses the owning native window");
                var primary = FindControl<Button>(hostChoiceDialog, "Host.Choice.Primary")!;
                var bounds = primary.TransformToVisual(pinned.Window.AutomationRoot).TransformBounds(new(0, 0, primary.ActualWidth, primary.ActualHeight));
                Check(bounds.Top >= 0 && bounds.Bottom <= hostChoiceDialog.XamlRoot.Size.Height + 1, "Pinned dialog confirmation remains inside the visible window");
                await Choose(0);
                await WebAppUntilScript(first, "window.permissionResult==='granted'");
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                Check(PinnedInputActive && first.IsBrowsing, "Pinned confirmation restores its original interaction owner");
                await first.EvaluateGestureFixtureAsync("window.open('https://shell.fixture.invalid/popup')");
                await WebAppUntil(() => first.HasPopup && first.ActiveSurface.CurrentTitle == "Shell /popup");
                Check(ReferenceEquals(first.ActiveSurface.XamlRoot, pinned.Window.AutomationRoot.XamlRoot), "Hosted popup stays in the pinned webapp window");
                await Press(ControllerButton.B); await WebAppUntil(() => !first.HasPopup);
                await WebAppUntil(() => PinnedInputActive && first.IsBrowsing);
                Check(PinnedInputActive && first.IsBrowsing, "Closing a pinned popup preserves its parent and pin interaction");
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
                await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface == first) && foreground && first.IsBrowsing);

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
                Check(owner!.Session.IsWebBrowserSessionCurrent(authority, "app.primary") &&
                    await first.EvaluateFixtureAsync("fakeStream.getTracks().every(t=>t.readyState==='live')") == "true", "Background retention survives real worker idle timeout without ending fake media");
                Check(WebappStatus.Visibility == Visibility.Visible && browserOwner!.BackgroundSessionCount == 1, "The shell reports the hidden running webapp");
                await SelectAsync("webapp-a");
                Check(ReferenceEquals((await CurrentBrowser()), first) && await first.EvaluateFixtureAsync("window.mediaState==='ready'") == "true", "Returning to the webapp preserves its native document and stream");

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
                var stopping = StopBrowserWidgetAsync(owner.Session.GetState("webapp-c")!.LastGood!.Descriptor);
                await Choose(0); await stopping;
                await CurrentBrowser();
                Check(browserOwner.BackgroundSessionCount == 4, "Stopping a session makes capacity available to the waiting webapp");

                step = "renderer recovery";
                await SelectAsync("webapp-a");
                await first.CrashRendererFixtureAsync();
                await WebAppUntil(() => first.NeedsRecovery && browserOwner.FailedBackgroundSessionCount == 1);
                Check(WebappStatus.Text.Contains("disconnected", StringComparison.Ordinal), "Renderer failure is visible in shared session status");
                await Press(ControllerButton.A);
                await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface is { IsReady: true } page && page != first));
                Check(await (await CurrentBrowser()).EvaluateFixtureAsync("typeof window.fakeStream==='undefined'") == "true", "Recovery creates a fresh page through the placement owner");
            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = error is null, step, checks, error, pages = Slots(surface).Select(slot => new { ready = slot.Surface?.IsReady, loading = slot.Surface?.IsNavigationInProgress, title = slot.Surface?.CurrentTitle, profile = slot.Surface?.ProfileName, permissions = slot.Surface?.FixturePermissionTrace }).ToArray(), retained = browserOwner?.BackgroundSessionCount }));

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
