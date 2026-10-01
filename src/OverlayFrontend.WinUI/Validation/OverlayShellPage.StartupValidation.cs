using Microsoft.UI.Xaml.Automation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ValidateStartupPresentationAsync(Action<bool, string> check)
    {
        check(!startupPresentationPending && owner is not null && activeWidget is not null,
            "startup reveals content only after the bridge and initial presentation are available");
        BeginStartupPresentation();
        check(startupPresentationPending && StartupContentStage.Opacity == 0 && !StartupContentStage.IsHitTestVisible && startupIndicator!.IsShowing,
            "cold startup masks incomplete content with the passive branded indicator");
        check(AutomationProperties.GetName(startupIndicator!) == "Starting WidgetRail", "startup exposes an accurate accessible status");
        var previousWidget = activeWidget;
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        check(startupPresentationPending && activeWidget == previousWidget && !shellOwnedReleases.Contains(ControllerButton.A),
            "startup consumes activation without retaining its release or dispatching to the widget");
        CompleteStartupPresentation();
        check(!startupPresentationPending && StartupContentStage.Opacity == 1 && StartupContentStage.IsHitTestVisible,
            "ready presentation enables content without an artificial minimum splash hold");
        await Task.Delay(650);
        check(startupReveal is null && !startupIndicator!.IsShowing,
            "startup transition retires both the content animation and loading indicator");
        check(Math.Abs(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ProductionLayout).Opacity - 1) < .001 &&
            StartupContentStage.Opacity == 1,
            "startup completion leaves both compositor and XAML content fully visible");
        BeginStartupPresentation();
        CompleteStartupPresentation(failed: true);
        check(!startupPresentationPending && StartupContentStage.IsHitTestVisible && !startupIndicator!.IsShowing,
            "startup failure reveals the recoverable status instead of trapping input behind the splash");
        startupIndicator!.ApplyAppearance(ShellPalette, Appearance with { Motion = MotionPreference.Reduced }, true);
        startupIndicator.BeginApplication(-50);
        check(startupIndicator.IsShowing && !startupIndicator.IsPulsing,
            "reduced motion keeps startup identity visible without a pulse");
        startupIndicator.Clear();
        RefreshStartupPresentation();
    }
}
