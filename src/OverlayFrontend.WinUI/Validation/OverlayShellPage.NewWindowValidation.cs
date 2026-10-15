using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        // pointer position, with genuine native Page actions command dispatch.
        await parent.EvaluateFixtureAsync("document.body.insertAdjacentHTML('beforeend', '<a id=windowLink href=https://shell.fixture.invalid/new-window style=\"position:fixed;inset:0;z-index:9999;background:#203040;color:white\">Synthetic new-window link</a>')");
        for (var cycle = 0; cycle < 8; cycle++)
        {
            await WebAppUntil(() => parent.IsBrowsing && !HostChoiceActive);
            // Exercise the old silent-drop guard deterministically, in addition
            // to ordinary native timing, without a sleep in production code.
            parent.BeforeContextCommandForValidation = cycle % 2 == 0 ? () => parent.SetPresentation(true, false) : null;
            await Press(ControllerButton.Menu);
            await Dialog("Page actions");
            await WebAppUntil(() => FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options") is { IsLoaded: true, ActualWidth: > 0 });
            var list = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
            var item = list.Items.OfType<ListViewItem>().First(entry => entry.Content is TextBlock text &&
                text.Text.StartsWith("Open link in new window", StringComparison.OrdinalIgnoreCase));
            list.SelectedItem = item; item.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await Dialog("Where should this page open?");
            await Decide("widget");
            var child = await Child();
            check(child.CurrentUrl?.EndsWith("/new-window", StringComparison.Ordinal) == true, $"Native Page actions hands off to placement without pointer movement, cycle {cycle + 1}");
            await parent.ClosePopupAsync();
        }
        parent.BeforeContextCommandForValidation = null;
        await parent.EvaluateFixtureAsync("document.getElementById('windowLink').remove()");

        var pending = Open(parent); await Dialog("Where should this page open?"); await Decide("widget"); await pending;
        var original = await Child();
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
        var replacement = await Child();
        check(!ReferenceEquals(replacement, original) && !original.IsReady && !replacement.HasPopup,
            "A request from an additional page replaces the sibling without nesting controllers");
        check(replacement.ProfileName == parent.ProfileName,
            "Replacement from the additional page retains the root account profile");
        await parent.ClosePopupAsync(); await WebAppUntil(() => parent.IsBrowsing);

        pending = Open(parent); await Dialog("Where should this page open?"); await Decide("pin"); await pending;
        original = await Child(); await WebAppUntil(() => pinned?.Popup?.Child == original && parent.IsBrowsing);
        var bounds = pinned!.Window.Bounds;
        pending = Open(parent); await Dialog("Replace existing page?"); await Decide("pin"); await pending;
        replacement = await Child(); await WebAppUntil(() => pinned?.Popup?.Child == replacement);
        check(!ReferenceEquals(original, replacement) && pinned!.Window.Bounds == bounds,
            "Replacing a pinned page preserves this widget's remembered pin geometry");
        await SelectAsync("webapp-b", enterWidget: true);
        await EnterPinnedAsync(); await WebAppUntil(() => replacement.IsBrowsing);
        pending = Open(replacement); await Dialog("Replace existing page?"); await Decide("widget"); await Finish(pending);
        var returned = await Child();
        await WebAppUntil(() => pinned is null && activeWidget == "webapp-a" && parent.ActiveSurface == returned && returned.IsBrowsing);
        check(true, "Replacing from the interactive pin selects its parent widget even when another widget was open");
        await SelectAsync("webapp-b", enterWidget: true);
        await PinAsync("webapp-b", WidgetRail.WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
        var otherPin = pinned;
        await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => returned.IsBrowsing && foreground);
        pending = Open(returned); await Dialog("Replace existing page?");
        await Press(ControllerButton.B); await Finish(pending);
        check(ReferenceEquals(pinned, otherPin) && ReferenceEquals(parent.PinnablePopup, returned),
            "Cancelling combined replacement preserves both the other widget's pin and the current page");
        pending = Open(returned); await Dialog("Replace existing page?"); await Decide("pin"); await Finish(pending);
        var combined = await Child(); await WebAppUntil(() => pinned?.Popup?.Child == combined);
        check(!ReferenceEquals(pinned, otherPin) && !returned.IsReady && !HostChoiceActive,
            "One confirmation replaces the existing page and another widget's pin");
        await UnpinAsync(save: true);
        await parent.ClosePopupAsync(); await WebAppUntil(() => parent.IsBrowsing && foreground);
        await browserOwner!.StopWidgetSessionsAsync("webapp-b");
        // The subsequent suite starts with one resident root. Restart the
        // inactive synthetic widget to retire its intentionally cached page.
        await owner!.Session.RestartAsync(owner.Session.GetTarget("webapp-b"), lifetime.Token);
        await WebAppUntil(() => browserOwner.ResidentCount == 1);

        await Press(ControllerButton.LeftStick);
        FindControl<Button>(parent, "Browser.SiteControls")!.Focus(FocusState.Keyboard);
        await Press(ControllerButton.A); await Dialog("Website settings");
        var options = FindControl<ListView>(hostChoiceDialog!, "Host.Choice.Options")!;
        check(options.Items.OfType<ListViewItem>().Any(item => (string?)item.Tag == "permission.0") &&
            options.Items.OfType<ListViewItem>().Any(item => (string?)item.Tag == "windows.pin"),
            "Website settings exposes permissions and all new-window choices together");
        await CapturePopupDecisionAsync(hostChoiceDialog!, "website-settings");
        var ask = options.Items.OfType<ListViewItem>().Single(item => (string?)item.Tag == "windows.ask");
        options.SelectedItem = ask; ask.Focus(FocusState.Keyboard); await Press(ControllerButton.A);
        await WebAppUntil(() => !HostChoiceActive);
        check(PopupPreferences.Get(parent.WidgetId, parent.ProfileName, origin) == BrowserPopupPlacement.Ask,
            "Website settings saves the choice directly without a placement submenu");
        if (parent.IsToolbarFocused) await Press(ControllerButton.LeftStick);

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
        async Task<BrowserSurface> Child()
        {
            await WebAppUntil(() => parent.PinnablePopup is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Shell /new-window" });
            return parent.PinnablePopup!;
        }
        Task Dialog(string title) => WebAppUntil(() => hostChoiceDialog is { IsOpenForValidation: true } dialog && (string?)dialog.Title == title &&
            FindControl<Button>(dialog, "Host.Choice.Close") is { IsLoaded: true, ActualWidth: > 0 });
        async Task Decide(string id)
        {
            var dialog = hostChoiceDialog!;
            var primaryIsPin = dialog.PrimaryButtonText.Contains("pin", StringComparison.OrdinalIgnoreCase);
            FindControl<Button>(dialog, primaryIsPin == (id == "pin") ? "Host.Choice.Primary" : "Host.Choice.Secondary")!.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
        }
    }
}
