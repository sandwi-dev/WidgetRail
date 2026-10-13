using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPopupPresentationAsync(BrowserSurface parent, Action<bool, string> check)
    {
        await parent.EvaluateFixtureAsync("window.parentMarker='live opener'");
        await parent.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/popup')");
        await WebAppUntil(() => parent.PinnablePopup is { CurrentTitle: "Shell /popup", IsNavigationInProgress: false });
        var child = parent.PinnablePopup!;
        await child.EvaluateFixtureAsync("window.popupMarker='live child';window.canvas=document.createElement('canvas');canvas.width=320;canvas.height=180;document.body.append(canvas);window.syntheticStream=canvas.captureStream(5);");
        var navigations = child.NavigationCount;
        var slot = new BrowserSlot { Active = true, AcceptsInput = false };
        using var peer = new PinnedWidgetWindow("Synthetic popup transfer", (nint)hostWindow);
        peer.SetContent(slot); peer.Place(new(80, 80, 640, 420)); peer.Show();
        await WebAppUntil(() => slot.CanDisplay);
        BrowserPopupPresentation? lease = null;
        try
        {
            lease = await browserOwner!.PinPopupAsync(child, slot);
            check(lease is { IsCurrent: true } && ReferenceEquals(slot.Surface, child) && ReferenceEquals(child.XamlRoot, slot.XamlRoot),
                "A hosted pop-out transfers its existing native controller into a borrowed pin slot");
            check(ReferenceEquals(parent.ActiveSurface, parent) && parent.HasPopup,
                "The parent becomes independently presentable while retaining lifetime ownership of its detached child");
            check(await child.EvaluateFixtureAsync("popupMarker==='live child' && opener.parentMarker==='live opener' && syntheticStream.getTracks().every(t=>t.readyState==='live')") == "true" && child.NavigationCount == navigations,
                "Popup transfer preserves live media, page state and opener without navigation or cloning");
            await browserOwner.ReleasePopupPresentationAsync(lease!);
            check(ReferenceEquals(parent.ActiveSurface, child) && !lease!.IsCurrent && slot.Surface is null,
                "Releasing a borrowed popup returns that same child to its parent");
            check(await child.EvaluateFixtureAsync("popupMarker==='live child' && syntheticStream.getTracks().every(t=>t.readyState==='live')") == "true" && child.NavigationCount == navigations,
                "Returning the popup preserves its live stream and document state");
            lease = await browserOwner.PinPopupAsync(child, slot);
            check(lease is { IsCurrent: true }, "The same live popup can be pinned again after returning");
            await parent.ClosePopupAsync();
            check(!lease!.IsCurrent && slot.Surface is null && !parent.HasPopup,
                "Parent popup closure invalidates the borrowed slot before its native window is destroyed");
        }
        finally
        {
            if (lease is not null) await browserOwner!.ReleasePopupPresentationAsync(lease);
            await parent.ClosePopupAsync();
            slot.Retire();
        }
        peer.Dispose();
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && parent.IsBrowsing);
        await VerifyPopupPinFlowAsync(parent, check);
    }

    private async Task VerifyPopupPinFlowAsync(BrowserSurface parent, Action<bool, string> check)
    {
        await parent.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/popup')");
        await WebAppUntil(() => parent.PinnablePopup is { IsReady: true, CurrentTitle: "Shell /popup", IsNavigationInProgress: false });
        var child = parent.PinnablePopup!;
        await WebAppUntil(() => PinnedInputActive && child.IsBrowsing);
        await child.EvaluateFixtureAsync("window.marker='live pin';window.canvas=document.createElement('canvas');document.body.append(canvas);window.stream=canvas.captureStream(5);");
        var navigations = child.NavigationCount;
        await Press(ControllerButton.LeftStick);
        await WebAppUntil(() => child.IsToolbarFocused);
        check(FindControl<Button>(child, "Browser.PinPopup")!.Focus(FocusState.Keyboard), "Pop-out toolbar exposes a focusable pin action");
        check(child.IsToolbarFocused && child.RequestPopupPin is not null && PinnedInputActive,
            "Pop-out pin action has settled controller toolbar ownership");
        await Press(ControllerButton.A);
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        FindControl<Button>(hostChoiceDialog!, "Host.Choice.Primary")!.Focus(FocusState.Keyboard);
        Receive(new ControllerFrame { Connected = 1 });
        await Press(ControllerButton.A);
        await WebAppUntil(() => pinned?.Popup is { IsCurrent: true } popup && ReferenceEquals(popup.Child, child));
        var pin = pinned!;
        await WebAppUntil(() => foreground && parent.IsBrowsing && !switching);
        check(pin.Presenter is null && ReferenceEquals(parent.ActiveSurface, parent) && child.NavigationCount == navigations,
            "Toolbar replacement uses the single shared pin while returning independent interaction to the parent");
        await Press(ControllerButton.View);
        await WebAppUntil(() => PinnedInputActive && child.IsBrowsing);
        check(ReferenceEquals(pin.Popup!.Slot.Surface, child), "View enters the same pinned live pop-out through shared controller routing");
        await child.EvaluateFixtureAsync("window.editor=document.createElement('input');editor.id='popup-editor';editor.style='position:fixed;left:10px;top:10px;width:200px;height:50px';document.body.append(editor)");
        await child.ClickFieldForValidationAsync("popup-editor");
        await WebAppUntil(() => child.ControllerTextEntry is { IsLoaded: true });
        var keyboard = child.ControllerTextEntry!;
        check(ReferenceEquals(ActiveControllerKeyboard, keyboard), "Pinned pop-out keyboard joins the shared controller keyboard owner");
        FindControl<Button>(keyboard, "Widget.TextEntry.Key.10")!.Focus(FocusState.Keyboard);
        await Press(ControllerButton.A);
        await keyboard.WaitForLiveInputAsync();
        check(await child.EvaluateFixtureAsync("editor.value==='q'") == "true", "Controller A edits the pinned pop-out's live field");
        await Press(ControllerButton.B);
        await WebAppUntil(() => child.ControllerTextEntry is null && PinnedInputActive && child.IsBrowsing);
        check(await child.EvaluateFixtureAsync("editor.value==='q'") == "true", "Closing the pinned keyboard preserves the edit and returns input to the same pop-out");
        if (Environment.GetCommandLineArgs().Contains("--webapp-notifications")) await VerifyPinnedNotificationAsync(child, check);
        await Press(ControllerButton.B);
        await WebAppUntil(() => foreground && !PinnedInputActive);
        check(pin.IsCurrent && parent.HasPopup && await child.EvaluateFixtureAsync("stream.getTracks().every(t=>t.readyState==='live')") == "true",
            "B exits pinned interaction without closing the pop-out or its live media");
        await Press(ControllerButton.View);
        await WebAppUntil(() => PinnedInputActive && child.IsBrowsing);
        check(await ExpandBrowserAsync(child, lifetime.Token), "A pinned pop-out borrows shared browser expansion without moving its parent");
        await WebAppUntil(() => expandedBrowser?.Surface == child && child.IsBrowsing);
        check(ReferenceEquals(parent.ActiveSurface, parent) && child.NavigationCount == navigations,
            "Popup expansion preserves the parent's independent document and child navigation state");
        await Press(ControllerButton.B);
        await WebAppUntil(() => expandedBrowser is null && PinnedInputActive && child.IsBrowsing && pin.Popup.Slot.Surface == child);
        check(true, "Collapsing returns the child to its original pin and restores controller interaction");
        await Press(ControllerButton.LeftStick);
        await WebAppUntil(() => child.IsToolbarFocused);
        check(FindControl<Button>(child, "Browser.PinPopup")!.Focus(FocusState.Keyboard), "Pinned pop-out offers its return action");
        await Press(ControllerButton.A);
        await WebAppUntil(() => pinned is null && parent.ActiveSurface == child && foreground);
        check(await child.EvaluateFixtureAsync("marker==='live pin' && stream.getTracks().every(t=>t.readyState==='live')") == "true",
            "Return pop-out removes the pin and restores its live page inside the widget");
        await WebAppUntil(() => child.IsBrowsing && foreground);
        var descriptor = catalogItems.Single(widget => widget.Id == "webapp-a");
        var choice = GetPinChoices(descriptor).Single(item => item.Popup);
        await ExecutePinChoiceAsync(descriptor.Id, choice);
        await WebAppUntil(() => pinned?.Popup is { IsCurrent: true });
        check(true, "The tray pin choice uses the same live pop-out path as its toolbar");
        await browserOwner!.StopWidgetSessionsAsync("webapp-a");
        await WebAppUntil(() => pinned is null && parent.SessionStopped && !parent.HasPopup && !child.IsReady);
        check(true, "Stopping the parent session closes its live pinned pop-out and removes the shared pin without deadlock");
        await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => parent.IsBrowsing && foreground);
        await Press(ControllerButton.X);
        await WebAppUntil(() => !parent.SessionStopped && parent.CurrentTitle == "Shell /primary" && parent.IsBrowsing);
        await parent.EvaluateFixtureAsync("localStorage.setItem('profile-marker','keep')");
        await parent.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/profile-popup')");
        await WebAppUntil(() => parent.PinnablePopup is { CurrentTitle: "Shell /profile-popup", IsNavigationInProgress: false });
        var profileChild = parent.PinnablePopup!;
        await PinBrowserPopupAsync("webapp-a", expectedChild: profileChild);
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && profileChild.IsBrowsing);
        check(await profileChild.ChangeProfile!(profileChild, new(false, "https://shell.fixture.invalid",
            Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Geolocation,
            Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny), lifetime.Token),
            "A pinned pop-out can change its shared profile permission through the host");
        await WebAppUntil(() => pinned is null && !profileChild.IsReady && !parent.HasPopup &&
            !parent.IsNavigationInProgress && parent.CurrentTitle == "Shell /primary" && foreground);
        check(await parent.EvaluateFixtureAsync("localStorage.getItem('profile-marker')==='keep'") == "true",
            "Profile permission changes close the child and reload its parent without clearing saved website data");
        await PinAsync("webapp-a", WidgetPinnedProjection.FullWidgetLayoutId);
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && Slots(pinned?.Presenter).Any(slot => slot.Surface == parent) && parent.IsBrowsing);
        check(ReferenceEquals(parent.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot),
            "The subsequent whole-widget pin owns the native parent before another expansion begins");

        async Task Press(ControllerButton button)
        { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
    }
}
