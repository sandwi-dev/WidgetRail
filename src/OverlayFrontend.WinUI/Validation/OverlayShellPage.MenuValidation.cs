using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyTieredMenusAsync(Action<bool, string> check)
    {
        var original = Appearance.WidgetSwitcher;
        try
        {
            Appearance = Appearance with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
            await SelectAsync("webapp-a", enterWidget: false);
            radialRequested = false; RefreshRadialChooser(); FocusTray();
            await WebAppUntil(() => !interactive && FocusedTrayWidget()?.Id == "webapp-a");
            await Press(ControllerButton.B);
            await WebAppUntil(() => RadialOpen);
            check(visible && radialSelection == "webapp-a", "B from rail opens radial on the current widget");
            await Press(ControllerButton.B);
            await WebAppUntil(() => interactive && !RadialOpen);
            check(true, "B from radial retains its existing return-to-widget behavior");

            await SelectAsync("webapp-a", enterWidget: false);
            radialRequested = false; RefreshRadialChooser(); FocusTray();
            await WebAppUntil(() => !interactive && FocusedTrayWidget()?.Id == "webapp-a");
            await Press(ControllerButton.Menu);
            await WebAppUntil(() => trayMenu is { } current && current.Flyout.Items.OfType<MenuFlyoutSubItem>().Any(item => item.IsLoaded));
            var menu = trayMenu!;
            var group = menu.Flyout.Items.OfType<MenuFlyoutSubItem>().Single(item => item.Text == "Widget");
            check(menu.Flyout.Items.OfType<MenuFlyoutSubItem>().Any(item => item.Text == "Notifications"), "Tray groups notification and widget commands");
            group.Focus(FocusState.Keyboard);
            Move(FocusNavigationDirection.Right);
            await WebAppUntil(() => group.Items.Any(item => item.FocusState == FocusState.Keyboard));
            check(true, "Controller Right opens a native submenu and focuses an action");
            var first = group.Items.First(item => item.IsLoaded);
            var presenter = Ancestor<MenuFlyoutPresenter>(first);
            check(presenter is { Background: not null } && presenter.MaxHeight <= XamlRoot.Size.Height,
                "Native submenu inherits themed chrome and bounded height");
            if (presenter is not null) await CapturePopupDecisionAsync(presenter, "tray-widget-submenu");
            await Press(ControllerButton.B);
            await WebAppUntil(() => group.FocusState == FocusState.Keyboard);
            check(ReferenceEquals(trayMenu, menu), "B closes only the submenu and restores its parent focus");
            await Press(ControllerButton.A);
            await WebAppUntil(() => group.Items.Any(item => item.FocusState == FocusState.Keyboard));
            Move(FocusNavigationDirection.Left);
            await WebAppUntil(() => group.FocusState == FocusState.Keyboard);
            check(ReferenceEquals(trayMenu, menu), "A opens and Left returns from a submenu without closing the root");
            await Press(ControllerButton.A);
            await WebAppUntil(() => group.Items.Any(item => item.FocusState == FocusState.Keyboard));
            var reorder = group.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Reorder");
            reorder.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await WebAppUntil(() => trayMenu is null && reordering);
            check(true, "Submenu activation invokes the existing guarded tray command");
            await Press(ControllerButton.B);
            check(!reordering && !RadialOpen && visible, "B finishes reorder before applying rail Back behavior");
            await VerifyLongMenuAsync(check);
        }
        finally
        {
            CloseTrayMenu(false); FinishTrayReorder();
            Appearance = Appearance with { WidgetSwitcher = original };
            radialRequested = false; RefreshRadialChooser();
            await SelectAsync("webapp-a", enterWidget: true);
            // Restore this fixture's browser starting point. Reorder deliberately
            // leaves focus in the rail, and selection alone preserves focus history.
            var slot = Slots(surface).First(slot => slot.Surface?.CurrentTitle == "Shell /primary");
            slot.Focus(FocusState.Keyboard);
            await WebAppUntil(() => slot.Surface?.IsBrowsing == true);
        }
        async Task Press(ControllerButton button)
        { Receive(new ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
        void Move(FocusNavigationDirection direction) => NavigateTray(direction);
        static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is T match) return match;
            return null;
        }
    }

    private async Task VerifyLongMenuAsync(Action<bool, string> check)
    {
        var flyout = new MenuFlyout();
        var group = new MenuFlyoutSubItem { Text = "Synthetic long menu" };
        for (var index = 0; index < 40; index++) group.Items.Add(new MenuFlyoutItem { Text = "Action " + index });
        flyout.Items.Add(group);
        using var navigator = new NativeMenuNavigator(flyout, () => visible && !retired);
        using var theme = NativePopupTheme.Menu(flyout, this);
        try
        {
            flyout.ShowAt(this, new Windows.Foundation.Point(Math.Max(0, ActualWidth - 8), Math.Max(0, ActualHeight - 8)));
            await WebAppUntil(() => group.IsLoaded);
            navigator.FocusFirst(); navigator.Move(FocusNavigationDirection.Right);
            await WebAppUntil(() => group.Items[0].FocusState == FocusState.Keyboard);
            for (var index = 1; index < group.Items.Count; index++) navigator.Move(FocusNavigationDirection.Down);
            await WebAppUntil(() => group.Items[^1].FocusState == FocusState.Keyboard);
            await Task.Delay(250);
            var item = group.Items[^1];
            MenuFlyoutPresenter? presenter = null;
            for (DependencyObject? node = item; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is MenuFlyoutPresenter found) { presenter = found; break; }
            var bounds = item.TransformToVisual(presenter).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight));
            check(presenter is not null && bounds.Top >= -1 && bounds.Bottom <= presenter.ActualHeight + 1,
                $"A long native submenu scrolls its last controller-focused action into view (top={bounds.Top:0}, bottom={bounds.Bottom:0}, height={presenter?.ActualHeight:0})");
            var location = presenter!.TransformToVisual(this).TransformBounds(new(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            check(location.Left >= -1 && location.Top >= -1 && location.Right <= ActualWidth + 1 && location.Bottom <= ActualHeight + 1,
                "Native submenu placement fits at the bottom-right screen edge");
            navigator.Move(FocusNavigationDirection.Down);
            check(group.Items[0].FocusState == FocusState.Keyboard, "Controller menu wraparound returns to the first action");
            check(navigator.Back() && group.FocusState == FocusState.Keyboard, "Long submenu returns focus to its parent");
        }
        finally
        {
            if (group.IsLoaded)
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                flyout.Closed += (_, _) => closed.TrySetResult();
                flyout.Hide();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
