using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyNewWindowFlowAsync(BrowserSurface parent, Action<bool, string> check)
    {
        const string origin = "https://shell.fixture.invalid";
        await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, origin, BrowserPopupPlacement.Ask, lifetime.Token);
        // A full-viewport link lets every cycle use the exact same controller
        // pointer position, with native context-menu targets and host Page actions.
        await parent.EvaluateFixtureAsync("document.body.insertAdjacentHTML('beforeend', '<a id=windowLink href=https://shell.fixture.invalid/new-window style=\"position:fixed;inset:0;z-index:9999;background:#203040;color:white\">Synthetic new-window link</a>')");
        for (var cycle = 0; cycle < 8; cycle++)
        {
            var pinLink = cycle % 2 == 0;
            var saved = pinLink ? BrowserPopupPlacement.InWidget : BrowserPopupPlacement.Pinned;
            await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, origin, saved, lifetime.Token);
            await WebAppUntil(() => parent.IsBrowsing && !HostChoiceActive);
            // Exercise the old silent-drop guard deterministically, in addition
            // to ordinary native timing, without a sleep in production code.
            parent.BeforeContextCommandForValidation = cycle % 2 == 0 ? () => parent.SetPresentation(true, false) : null;
            await Press(ControllerButton.Menu);
            await Dialog("Page actions");
            await WebAppUntil(() => FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options") is { IsLoaded: true, ActualWidth: > 0 });
            var list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
            var pinAction = list.Items.OfType<ListViewItem>().Single(entry => (string?)entry.Tag == "new.pin");
            var widgetAction = list.Items.OfType<ListViewItem>().Single(entry => (string?)entry.Tag == "new.widget");
            if (cycle == 7) await parent.EvaluateFixtureAsync("document.getElementById('windowLink').href='https://shell.fixture.invalid/changed-after-menu'");
            await Task.Delay(150);
            check(ReferenceEquals(FocusManager.GetFocusedElement(hostChoiceDialog!.XamlRoot), pinAction) && pinAction.FocusState == FocusState.Keyboard,
                $"Page actions initially focuses Open in pinned surface, cycle {cycle + 1}");
            if (cycle == 0) await CapturePopupDecisionAsync(hostChoiceDialog!, "page-actions-initial-focus");
            if (!pinLink)
            {
                Receive(new ControllerFrame { Connected = 1, DpadNavigation = new NavigationEvent { Direction = NavigationDirection.Down, Phase = NavigationPhase.Pressed } });
                check(ReferenceEquals(FocusManager.GetFocusedElement(hostChoiceDialog!.XamlRoot), widgetAction), "Down reaches Open over current page without focusing its section header");
            }
            await Press(ControllerButton.A);
            var child = await Child();
            await WebAppUntil(() => pinLink ? pinned?.Popup?.Child == child : parent.ActiveSurface == child);
            check(!HostChoiceActive && child.CurrentUrl?.EndsWith("/new-window", StringComparison.Ordinal) == true,
                $"Direct destination opens without another placement dialog or pointer movement, cycle {cycle + 1}");
            check(PopupPreferences.Get(parent.WidgetId, parent.ProfileName, origin) == saved,
                $"Explicit destination overrides but does not change the opposite saved preference, cycle {cycle + 1}");
            await parent.ClosePopupAsync();
            await WebAppUntil(() => pinned is null);
        }
        parent.BeforeContextCommandForValidation = null;
        await parent.EvaluateFixtureAsync("document.getElementById('windowLink').remove()");
        await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, origin, BrowserPopupPlacement.Ask, lifetime.Token);

        var pending = Open(parent); await Dialog("Open new window"); await Decide("widget"); await pending;
        var original = await Child();
        await original.EvaluateFixtureAsync("document.body.insertAdjacentHTML('beforeend', '<a href=https://shell.fixture.invalid/new-window style=\"position:fixed;inset:0;z-index:9999\">Replace from child</a>')");
        await original.EvaluateFixtureAsync("window.linkPainted=false;requestAnimationFrame(()=>requestAnimationFrame(()=>window.linkPainted=true))");
        await WebAppUntilScript(original, "window.linkPainted===true");
        await WebAppUntil(() => original.IsBrowsing);
        await Press(ControllerButton.Menu); await Dialog("Page actions");
        var childOptions = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
        var childAction = childOptions.Items.OfType<ListViewItem>().Single(entry => (string?)entry.Tag == "new.widget");
        childAction.Focus(FocusState.Keyboard);
        await Press(ControllerButton.A); await Dialog("Replace existing page?");
        check(hostChoiceDialog!.PrimaryButtonText == "Replace" && string.IsNullOrEmpty(hostChoiceDialog.SecondaryButtonText) &&
            FindControl<CheckBox>(hostChoiceDialog, "Host.Choice.Remember") is null,
            "Explicit replacement asks only Replace/Cancel, without placement or Remember choices");
        await CapturePopupDecisionAsync(hostChoiceDialog, "direct-replacement");
        await Decide("widget");
        var directReplacement = await Child(original);
        await WebAppUntil(() => !original.IsReady);
        check(!ReferenceEquals(original, directReplacement) && await directReplacement.EvaluateFixtureAsync("window.opener===null") == "true",
            "Host link replacement from a child retires its requester without joining its menu task or claiming a native opener");
        original = directReplacement;
        await original.EvaluateFixtureAsync("window.replacementProof='unchanged'");
        pending = Open(original); await Dialog("Replace existing page?");
        await Press(ControllerButton.B); await pending;
        check(ReferenceEquals(parent.PinnablePopup, original) && await original.EvaluateFixtureAsync("window.replacementProof==='unchanged'") == "true",
            "Cancel replacement from the additional page preserves its live document");
        var prepare = parent.PreparePopup;
        parent.PreparePopup = (_, _, _, _) => throw new InvalidOperationException("Synthetic new-window preparation failure");
        try
        {
            pending = Open(original); await Dialog("Replace existing page?"); await Decide("widget"); await Finish(pending);
            await WebAppUntil(() => !HostChoiceActive && original.IsBrowsing);
            check(ReferenceEquals(parent.PinnablePopup, original) && await original.EvaluateFixtureAsync("window.replacementProof==='unchanged'") == "true",
                "Failed replacement preparation preserves the existing additional page");
        }
        finally { parent.PreparePopup = prepare; }
        pending = Open(original); await Dialog("Replace existing page?");
        SetVisible(false); await Finish(pending);
        SetVisible(true); await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => original.IsBrowsing && !HostChoiceActive);
        check(ReferenceEquals(parent.PinnablePopup, original), "Hiding during replacement cancels the decision without closing the existing page");
        pending = Open(original); await Dialog("Replace existing page?"); await Decide("widget"); await Finish(pending);
        var replacement = await Child(original);
        check(!ReferenceEquals(replacement, original) && !original.IsReady && !replacement.HasPopup,
            "A request from an additional page replaces the sibling without nesting controllers");
        check(replacement.ProfileName == parent.ProfileName,
            "Replacement from the additional page retains the root account profile");
        await parent.ClosePopupAsync(); await WebAppUntil(() => parent.IsBrowsing);

        pending = Open(parent); await Dialog("Open new window"); await Decide("pin"); await pending;
        original = await Child(); await WebAppUntil(() => pinned?.Popup?.Child == original && parent.IsBrowsing);
        var bounds = pinned!.Window.Bounds;
        pending = Open(parent); await Dialog("Replace existing page?"); await Decide("pin"); await pending;
        replacement = await Child(original); await WebAppUntil(() => pinned?.Popup?.Child == replacement);
        check(!ReferenceEquals(original, replacement) && pinned!.Window.Bounds == bounds,
            "Replacing a pinned page preserves this widget's remembered pin geometry");
        await SelectAsync("webapp-b", enterWidget: true);
        await EnterPinnedAsync(); await WebAppUntil(() => replacement.IsBrowsing);
        pending = Open(replacement); await Dialog("Replace existing page?"); await Decide("widget"); await Finish(pending);
        var returned = await Child(replacement);
        await WebAppUntil(() => pinned is null && activeWidget == "webapp-a" && parent.ActiveSurface == returned && returned.IsBrowsing);
        check(true, "Replacing from the interactive pin selects its parent widget even when another widget was open");
        await SelectAsync("webapp-b", enterWidget: true);
        await PinAsync("webapp-b", WidgetRail.WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
        var otherPin = pinned;
        await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => returned.IsBrowsing && foreground);
        await DirectPinLink(returned); await Dialog("Replace existing page?");
        check(hostChoiceDialog!.PrimaryButtonText == "Replace" && string.IsNullOrEmpty(hostChoiceDialog.SecondaryButtonText),
            "Direct pinned replacement combines both affected surfaces in a single confirmation");
        await Press(ControllerButton.B);
        await WebAppUntil(() => !HostChoiceActive && ReferenceEquals(parent.PinnablePopup, returned));
        check(ReferenceEquals(pinned, otherPin) && ReferenceEquals(parent.PinnablePopup, returned),
            "Cancelling combined replacement preserves both the other widget's pin and the current page");
        await DirectPinLink(returned); await Dialog("Replace existing page?"); await Decide("pin");
        var combined = await Child(returned); await WebAppUntil(() => pinned?.Popup?.Child == combined);
        check(!ReferenceEquals(pinned, otherPin) && !returned.IsReady && !HostChoiceActive,
            "One confirmation replaces the existing page and another widget's pin");
        await UnpinAsync(save: true);
        await parent.ClosePopupAsync(); await WebAppUntil(() => parent.IsBrowsing && foreground);
        await browserOwner!.StopWidgetSessionsAsync("webapp-b");
        // The subsequent suite starts with one resident root. Restart the
        // inactive synthetic widget to retire its intentionally cached page.
        await owner!.Session.RestartAsync(owner.Session.GetTarget("webapp-b"), lifetime.Token);
        await WebAppUntil(() => browserOwner.ResidentCount == 1);

        await VerifyWebsiteSettingsAsync(parent, check);

        async Task Press(ControllerButton button)
        { Receive(new ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
        Task Open(BrowserSurface source) => source.EvaluateGestureFixtureAsync("window.next=window.open('https://shell.fixture.invalid/new-window')");
        async Task Finish(Task action)
        {
            // Closing the requesting controller can discard its pending CDP
            // reply. Assert the replacement's real state separately below.
            try { await action.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException or TimeoutException) { }
        }
        async Task<BrowserSurface> Child(BrowserSurface? previous = null)
        {
            // A replacement shares the URL and title. Wait for its identity and
            // the asynchronous retirement, not merely the still-visible old page.
            await WebAppUntil(() => parent.PinnablePopup is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Shell /new-window" } child &&
                !ReferenceEquals(child, previous) && previous?.IsReady != true);
            return parent.PinnablePopup!;
        }
        Task Dialog(string title) => WebAppUntil(() => hostChoiceDialog is { IsOpenForValidation: true } dialog && (string?)dialog.Title == title &&
            FindControl<Button>(dialog, "Host.Choice.Close") is { IsLoaded: true, ActualWidth: > 0 });
        async Task Decide(string id)
        {
            var dialog = hostChoiceDialog!;
            var primaryIsPin = dialog.PrimaryButtonText.Contains("pin", StringComparison.OrdinalIgnoreCase);
            FindControl<Button>(dialog, string.IsNullOrEmpty(dialog.SecondaryButtonText) || primaryIsPin == (id == "pin") ? "Host.Choice.Primary" : "Host.Choice.Secondary")!.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
        }
        async Task DirectPinLink(BrowserSurface page)
        {
            await page.EvaluateFixtureAsync("document.body.insertAdjacentHTML('beforeend','<a href=https://shell.fixture.invalid/new-window style=\"position:fixed;inset:0;z-index:9999;background:#203040\">Pin link</a>');window.directLinkPainted=false;requestAnimationFrame(()=>requestAnimationFrame(()=>window.directLinkPainted=true))");
            await WebAppUntilScript(page, "window.directLinkPainted===true");
            await WebAppUntil(() => page.IsBrowsing);
            await Press(ControllerButton.Menu); await Dialog("Page actions");
            var options = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
            options.Items.OfType<ListViewItem>().Single(item => (string?)item.Tag == "new.pin").Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
        }
    }
}
