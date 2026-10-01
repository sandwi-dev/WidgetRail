using Microsoft.UI.Xaml;
using WidgetRail.OverlayPlatformClient;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Runs only in the existing isolated pinned-placement fixture. Navigation
    // changes focus; it never invokes a Settings or provider action.
    private async Task ValidatePinnedAdjustmentFocusAsync(PinnedSurface current, Action<bool, string> check)
    {
        var originalSwitcher = Appearance.WidgetSwitcher;
        try
        {
            Appearance = Appearance with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
            await SelectAsync(current.WidgetId, enterWidget: false);
            PrepareRadialBackEntry(); RefreshRadialChooser(); FocusTray();
            await Until(() => RadialOpen && radialView?.Visibility == Visibility.Visible && !switching);
            var selected = radialSelection;
            var page = radialPage;
            foreach (var opacity in new[] { false, true })
            foreach (var save in new[] { false, true })
            {
                var label = (opacity ? "opacity" : "move/resize") + (save ? " Save" : " Cancel");
                Receive(Sample());
                await BeginPinnedAdjustmentAsync(opacity);
                check(RadialOpen && radialView is { Visibility: Visibility.Visible, IsHitTestVisible: false } &&
                    !WidgetHost.IsHitTestVisible && !Tray.IsHitTestVisible,
                    label + " keeps the wheel visible while adjustment exclusively owns pointer and controller input");
                var frame = Sample(rightX: save ? (short)0 : (short)-32767);
                frame.DpadNavigation = new() { Direction = NavigationDirection.Left, Phase = NavigationPhase.Pressed };
                Receive(frame); Receive(Sample());
                check(radialSelection == selected && radialPage == page,
                    label + " adjustment stick/D-pad input cannot navigate the retained radial selection");
                Finish(save);
                await Until(() => !PinnedAdjustmentActive &&
                    ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), radialView!.ContextAnchor(selected!)));
                check(RadialOpen && radialSelection == selected && radialPage == page &&
                    radialView is { Visibility: Visibility.Visible, IsHitTestVisible: true },
                    label + " returns to the same visible radial selection");
                check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), radialView!.ContextAnchor(selected!)),
                    label + " restores native focus to that radial item without entering its widget");
            }

            await SelectAsync("settings", enterWidget: true);
            await Until(() => !switching && interactive && FocusId().StartsWith("Widget.", StringComparison.Ordinal));
            surface!.MoveFocus(FocusNavigationDirection.Down);
            var focused = FocusId();
            foreach (var opacity in new[] { false, true })
            foreach (var save in new[] { false, true })
            {
                var label = (opacity ? "opacity" : "move/resize") + (save ? " Save" : " Cancel");
                Receive(Sample());
                await BeginPinnedAdjustmentAsync(opacity);
                check(interactive && !MainFocusEnabled && !WidgetHost.IsHitTestVisible && !RadialOpen,
                    label + " preserves the originating widget context while revoking its input and automatic focus");
                Finish(save);
                await Until(() => !PinnedAdjustmentActive && MainFocusEnabled && FocusId() == focused);
                check(activeWidget == "settings" && interactive && !RadialOpen,
                    label + " restores the remembered widget control instead of moving focus to the tray");
            }
        }
        finally
        {
            CancelPinnedAdjustment();
            Appearance = Appearance with { WidgetSwitcher = originalSwitcher };
            radialRequested = false;
            await SelectAsync(current.WidgetId, enterWidget: false);
            FocusTray();
        }

        void Finish(bool save)
        {
            var mask = (ushort)(save ? 0x1000 : 0x2000);
            Receive(Sample(mask, pressed: mask));
            Receive(Sample(released: mask));
        }
        string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
            ? AutomationProperties.GetAutomationId(focused) : string.Empty;
        async Task Until(Func<bool> predicate)
        {
            for (var i = 0; i < 400; ++i)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (predicate()) return;
                await Task.Delay(20, lifetime.Token);
            }
            throw new TimeoutException("Pinned adjustment focus did not settle: " + FocusId());
        }
    }
}
