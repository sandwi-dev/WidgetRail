using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void ConfigureRadialFixtureScale(double zoom, SurfaceExtent viewport, double textScale = 1)
    {
        Appearance = Appearance with { InterfaceScale = zoom, TextScale = textScale };
        ConfigureProductionViewport(viewport);
    }
    internal async Task<IReadOnlyList<string>> ValidateRadialFixtureAsync()
    {
        if (owner is not null || startup is not null || surface?.CurrentBinding is not { } binding)
            throw new InvalidOperationException("Radial fixture requires a layout-only shell.");
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var original = Appearance;
        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-neon-circuit");
        RefreshShellChrome();
        catalogItems[0] = catalogItems[0] with { Name = "YouTube Music" };
        activeWidget = requestedWidget = binding.Frame.Descriptor.Id;
        Tray.SelectedIndex = 0;
        ShowPresentationStatus("Radial fixture");
        SetInteractive(true);
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Rail };
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        Check(!RadialOpen, "rail setting preserves ordinary Back entry");
        SetInteractive(true);
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
        var contentSize = new Windows.Foundation.Size(WidgetHost.ActualWidth, WidgetHost.ActualHeight);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        UpdateLayout();
        Check(RadialOpen && radialView!.Visibility == Visibility.Visible, "unhandled widget Back opens configured radial chooser");
        Check(contentSize.Width == WidgetHost.ActualWidth && contentSize.Height == WidgetHost.ActualHeight,
            "wheel overlays the retained widget without changing its viewport");
        var deadline = Environment.TickCount64 + 5000;
        while (!catalogItems.Take(8).All(item => radialView!.ButtonFor(item.Id) is { IsLoaded: true }))
        {
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("Radial native controls did not load.");
            await Task.Delay(16);
        }
        Check(catalogItems.Take(8).All(item => radialView!.ButtonFor(item.Id) is { IsLoaded: true }), "eight native widget buttons realize the current radial page");
        Check(catalogItems.Take(8).All(item => HasVisibleIcon(radialView!.ButtonFor(item.Id)!)), "each realized radial button contains a visible native icon");
        var beforeWidget = activeWidget;
        var beforeSelection = radialSelection;
        BrowseRadialPage(1); UpdateLayout();
        Check(radialPage == 1 && activeWidget == beforeWidget && radialSelection == beforeSelection,
            "radial page browsing preserves selection and current widget authority");
        StepRadialSelection(FocusNavigationDirection.Right); UpdateLayout();
        Check(radialSelection == catalogItems[8].Id, "D-pad enters the browsed page without falling into widget input");
        BrowseRadialPage(1); UpdateLayout();
        Check(radialPage == 2 && radialView!.ButtonFor(catalogItems[16].Id) is not null && radialView.ButtonFor(catalogItems[0].Id) is null,
            "short final page has only current native targets");
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        Check(interactive && !RadialOpen && activeWidget == beforeWidget, "radial Back returns to current widget without closing overlay");
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        UpdateLayout();
        Check(RadialOpen && radialPage == 0, "fresh radial entry restores the current widget page");
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Radial, Contrast = ContrastPreference.High, Motion = MotionPreference.Reduced };
        RefreshShellChrome(); UpdateLayout();
        radialView!.Open(WidgetRail.OverlayFrontend.WinUI.Motion.WidgetMotionOptions.From(Appearance, true));
        Check(radialView.ButtonFor(catalogItems[0].Id)!.UseSystemFocusVisuals && !radialView.HasElevation,
            "high-contrast radial uses native focus and removes decorative shadow");
        Check(radialView.LastEntranceDuration == TimeSpan.Zero, "reduced motion disables radial entrance animation");
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Radial, TextScale = 1.5 };
        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-redline");
        RefreshShellChrome(); UpdateLayout();
        Check(radialView.HasElevation && radialView.ActualWidth > 0, "theme/text-scale changes retain the same elevated chooser controls");
        Appearance = original with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-neon-circuit");
        RefreshShellChrome(); UpdateLayout();
        return checks;
    }
    private static bool HasVisibleIcon(DependencyObject parent)
    {
        if (parent is Microsoft.UI.Xaml.Controls.IconElement { ActualWidth: > 0, ActualHeight: > 0 }) return true;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); ++index)
            if (HasVisibleIcon(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }
}
