using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPinnedDialogExitAsync(BrowserSurface browser, Action<bool, string> check)
    {
        var pin = pinned!;
        var kind = pin.Popup is null ? "full-widget" : "pop-out";
        var navigations = browser.NavigationCount;
        await VerifyChoiceViewport();
        await OpenActions();
        PressFrame(0x2000);
        await WebAppUntil(() => !HostChoiceActive && !browser.HasDialog && PinnedInputActive && browser.IsBrowsing);
        check(true, $"{kind}: B dismisses Page actions while keeping pin interaction");

        await OpenActions();
        PressFrame(0x20);
        check(!HostChoiceActive && !pin.Window.Interactive, $"{kind}: View immediately revokes dialog routing and leaves the pin");
        await Reenter();
        check(true, $"{kind}: View can reenter after Page actions teardown without stale focus restoration");

        await OpenActions();
        ReturnFromPinnedRequested?.Invoke();
        await WebAppUntil(() => foreground && !PinnedInputActive && !HostChoiceActive && !browser.HasDialog);
        await Reenter();
        check(true, $"{kind}: native foreground loss dismisses the pinned dialog and permits reentry");

        await OpenActions();
        NativePickerVisibilityForValidation!(false);
        await WebAppUntil(() => !visible && !HostChoiceActive && !browser.HasDialog);
        NativePickerVisibilityForValidation(true);
        await WebAppUntil(() => visible && foreground && !switching);
        await Reenter();
        check(true, $"{kind}: hiding and reopening the overlay cannot leave an orphaned pinned dialog");

        // Exercise the defensive input-path cleanup independently of normal
        // shell exit notifications, without a personal page or external HWND.
        await OpenActions();
        pin.Window.SetInteraction(false);
        Receive(new ControllerFrame { Connected = 1 });
        check(!HostChoiceActive, $"{kind}: stale passive-pin dialog ownership is revoked on the next frame");
        ReturnFromPinnedRequested?.Invoke();
        await Reenter();

        await browser.EvaluateFixtureAsync("setTimeout(()=>window.pinPrompt=prompt('Synthetic pin keyboard',''),0)");
        await WebAppUntil(() => browser.ControllerTextEntry is { IsLoaded: true });
        check(ReferenceEquals(ActiveControllerKeyboard, browser.ControllerTextEntry), $"{kind}: keyboard remains usable inside the pin");
        PressFrame(0x20);
        await WebAppUntil(() => !PinnedInputActive && browser.ControllerTextEntry is null && !browser.HasDialog);
        await WebAppUntilScript(browser, "window.pinPrompt===null");
        await Reenter();
        check(browser.NavigationCount == navigations && ReferenceEquals(pinned, pin),
            $"{kind}: leaving the keyboard cancels its prompt and preserves the same pinned page");

        async Task OpenActions()
        {
            await WebAppUntil(() => PinnedInputActive && browser.IsBrowsing && !browser.HasDialog);
            await RouteButtonAsync(ControllerButton.Menu, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.Menu, ControllerEventPhase.Released);
            await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true } && ReferenceEquals(hostChoicePin, pin));
            check(hostChoiceDialog!.Title?.ToString() == "Page actions", $"{kind}: native context menu uses the pin-owned host dialog");
            Receive(new ControllerFrame { Connected = 1 });
        }

        async Task VerifyChoiceViewport()
        {
            var bounds = pin.Window.Bounds;
            pin.Window.Place(bounds with { Width = 695, Height = 450 });
            var answer = ShowHostChoiceAsync("Page actions", "https://shell.fixture.invalid",
                Enumerable.Range(1, 24).Select(i => new HostDialogChoice(i.ToString(), $"Synthetic page action {i}")).ToArray(),
                lifetime.Token, pinnedOwner: pin);
            await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true, IsOpenForValidation: true });
            var dialog = hostChoiceDialog!;
            var list = FindControl<ListView>(dialog, "Host.Choice.Options")!;
            var cancel = FindControl<Button>(dialog, "Host.Choice.Close")!;
            dialog.FocusFirstChoice();
            Receive(new ControllerFrame { Connected = 1 });
            await ObserveLayout("top");
            for (var i = 1; i < 24; i++)
            {
                Receive(new ControllerFrame { Connected = 1, DpadNavigation = new() { Direction = NavigationDirection.Down, Phase = NavigationPhase.Pressed } });
                await Task.Delay(25);
            }
            var last = (ListViewItem)list.Items[^1];
            await WebAppUntil(() =>
            {
                var rect = last.TransformToVisual(list).TransformBounds(new(0, 0, last.ActualWidth, last.ActualHeight));
                return list.SelectedIndex == 23 && rect.Top >= -1 && rect.Bottom <= list.ActualHeight + 1;
            });
            var listBounds = list.TransformToVisual(dialog.XamlRoot.Content).TransformBounds(new(0, 0, list.ActualWidth, list.ActualHeight));
            var cancelBounds = cancel.TransformToVisual(dialog.XamlRoot.Content).TransformBounds(new(0, 0, cancel.ActualWidth, cancel.ActualHeight));
            check(listBounds.Top >= 0 && listBounds.Bottom <= cancelBounds.Top && cancelBounds.Bottom <= dialog.XamlRoot.Size.Height,
                $"{kind}: a small choice dialog scrolls to its last action and keeps Cancel visible");
            await ObserveLayout("bottom");
            Receive(new ControllerFrame { Connected = 1, DpadNavigation = new() { Direction = NavigationDirection.Down, Phase = NavigationPhase.Pressed } });
            check(ReferenceEquals(FocusManager.GetFocusedElement(dialog.XamlRoot), cancel), $"{kind}: Down from the last choice reaches Cancel");
            PressFrame(0x2000);
            check(await answer is null, $"{kind}: long-list dialog cancels without invoking an action");
            pin.Window.Place(bounds);
            await WebAppUntil(() => PinnedInputActive && browser.IsBrowsing);

            async Task ObserveLayout(string stage)
            {
                var argument = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--webapp-choice-raster=", StringComparison.Ordinal));
                if (argument is null) return;
                var prefix = argument.Split('=', 2)[1] + "." + kind + "." + stage;
                await File.WriteAllTextAsync(prefix + ".json", System.Text.Json.JsonSerializer.Serialize(new { hwnd = (long)pin.Window.Handle }));
                await WebAppUntil(() => File.Exists(prefix + ".captured"));
            }
        }

        async Task Reenter()
        {
            await WebAppUntil(() => foreground && !switching && !PinnedInputActive && !HostChoiceActive && !browser.HasDialog);
            PressFrame(0x20);
            await WebAppUntil(() => PinnedInputActive && browser.IsBrowsing);
        }

        void PressFrame(ushort button)
        {
            Receive(new ControllerFrame { Connected = 1 });
            Receive(new ControllerFrame { Connected = 1, PressedButtons = button, State = new() { Buttons = button } });
            Receive(new ControllerFrame { Connected = 1, ReleasedButtons = button });
        }
    }
}
