using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
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
        FocusDirectionalTray();
        UpdateLayout();
        Check(!interactive && !RadialOpen && Tray.IsEnabled && RailControls.IsHitTestVisible,
            "directional root exit enters the rail with the radial setting enabled");
        SetInteractive(true);
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
        Check(radialView!.SectorSurface.VisibleSectorCount == 8 && radialView.SectorSurface.SelectedSectorCount == 1,
            "radial chrome has eight separated sectors and one outer selection arc");
        var hint = Descendants(radialView).OfType<FontIcon>().Single(element => AutomationProperties.GetAutomationId(element) == "Overlay.Radial.PagingHint");
        Check(hint.FontSize == 48 && hint.Width == 48 && hint.Height == 48, "center controller hint uses a 48-DIP optical box");
        Check(!Descendants(radialView).OfType<TextBlock>().Any(text => text.Text == catalogItems[0].Name) &&
            ToolTipService.GetToolTip(radialView.ButtonFor(catalogItems[0].Id)!) is not null,
            "center omits widget name while widget button retains its tooltip");
        var beforeWidget = activeWidget;
        var beforeSelection = radialSelection;
        PageWithStick(24000); UpdateLayout();
        Check(radialPage == 1 && activeWidget == beforeWidget && radialSelection == beforeSelection,
            "radial page browsing preserves selection and current widget authority");
        Check(NeutralPagingFocus(), "right-stick forward paging focuses the neutral hub, never an arrow");
        PageWithStick(-24000); UpdateLayout();
        Check(radialPage == 0 && NeutralPagingFocus(), "right-stick backward paging also preserves neutral hub focus");
        PageWithStick(24000); UpdateLayout();
        StepRadialSelection(FocusNavigationDirection.Right); UpdateLayout();
        Check(radialSelection == catalogItems[8].Id, "D-pad enters the browsed page without falling into widget input");
        BrowseRadialPage(1); UpdateLayout();
        Check(radialPage == 2 && radialView!.ButtonFor(catalogItems[16].Id) is not null && radialView.ButtonFor(catalogItems[0].Id) is null,
            "short final page has only current native targets");
        Check(radialView!.SectorSurface.VisibleSectorCount == catalogItems.Count - 16,
            "short final radial page paints no empty decorative sectors");
        // This fixture has no worker/session. Runtime B/A admission and exact
        // focus restoration are covered by the actual-package SharedUx checks.
        SetInteractive(true);
        Check(interactive && !RadialOpen && activeWidget == beforeWidget, "entering widget presentation closes radial chrome without changing its identity");
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
        radialView.Update(catalogItems.Take(8).ToArray(), catalogItems[0].Id, 0, ShellPalette, Appearance, true);
        UpdateLayout();
        Check(AutomationProperties.GetName(hint) == "Left stick chooses widget" &&
            Descendants(radialView).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) is "Overlay.Radial.Previous" or "Overlay.Radial.Next")
                .All(button => button.Visibility == Visibility.Collapsed),
            "single-page wheel keeps a useful large selection hint without paging arrows");
        RefreshRadialChooser(); UpdateLayout();
        // Pixel qualification is performed by Test-WinUiRadial.ps1 against
        // the composed window. RenderTargetBitmap omits the Z-translated chrome.
        return checks;

        void PageWithStick(short horizontal)
        {
            var frame = ControllerFrame.Create(); frame.Connected = 1;
            ReceiveRadialNavigation(frame);
            frame.State.RightThumbX = horizontal;
            ReceiveRadialNavigation(frame);
        }
        bool NeutralPagingFocus() => FocusManager.GetFocusedElement(XamlRoot) is Control focused &&
            AutomationProperties.GetAutomationId(focused) == "Overlay.Radial.Paging" && !focused.UseSystemFocusVisuals &&
            Descendants(radialView!).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) is "Overlay.Radial.Previous" or "Overlay.Radial.Next")
                .All(button => button.FocusState == FocusState.Unfocused);
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
    private static bool HasVisibleIcon(DependencyObject parent)
    {
        if (parent is Microsoft.UI.Xaml.Controls.IconElement { ActualWidth: > 0, ActualHeight: > 0 }) return true;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); ++index)
            if (HasVisibleIcon(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }
}
