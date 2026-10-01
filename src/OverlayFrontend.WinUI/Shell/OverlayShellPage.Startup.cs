using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private WidgetOpeningIndicator? startupIndicator;
    private bool startupPresentationPending;
    private long startupPresentationVersion;
    private WidgetCompositionMotion? startupReveal;
    private WidgetCompositionTarget? startupRevealTarget;

    private void InitializeStartupPresentation()
    {
        startupIndicator = new(ResolveTrayIconAsync)
        { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(startupIndicator, "Overlay.Startup");
        Canvas.SetZIndex(startupIndicator, 20);
        ProductionRoot.Children.Add(startupIndicator);
        PreviewKeyDown += (_, args) =>
        {
            if (!startupPresentationPending) return;
            args.Handled = true;
            if (args.Key == Windows.System.VirtualKey.Escape) HideRequested?.Invoke();
        };
        BeginStartupPresentation();
    }

    private void BeginStartupPresentation()
    {
        if (startupIndicator is null || retired) return;
        StopStartupReveal();
        startupIndicator.Clear();
        startupPresentationPending = true;
        ++startupPresentationVersion;
        StartupContentStage.Opacity = 0;
        StartupContentStage.IsHitTestVisible = false;
        RefreshStartupPresentation();
    }

    private void RefreshStartupPresentation()
    {
        startupIndicator?.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        if (!visible) startupIndicator?.Clear();
        if (startupPresentationPending)
        {
            if (visible && startupIndicator is { IsShowing: false }) startupIndicator.BeginApplication(startupPresentationVersion);
        }
        // Reduced motion or hidden windows must not retain compositor work.
        if (!visible || WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled).Reduced ||
            ShellChromePalette.Resolve(ShellPalette, Appearance).HighContrast)
            StopStartupReveal();
    }

    private void CompleteStartupPresentation(bool failed = false)
    {
        if (!startupPresentationPending) return;
        startupPresentationPending = false;
        StartupContentStage.Opacity = 1;
        StartupContentStage.IsHitTestVisible = true;
        if (failed || !visible) startupIndicator?.Clear();
        else startupIndicator?.Complete(startupPresentationVersion);
        var options = WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled);
        if (!failed && visible && !options.Reduced && !ShellChromePalette.Resolve(ShellPalette, Appearance).HighContrast)
        {
            startupRevealTarget = WidgetCompositionTarget.ForClippedDialog(ProductionLayout,
                new((float)Math.Max(1, ProductionLayout.ActualWidth), (float)Math.Max(1, ProductionLayout.ActualHeight)));
            startupReveal = new(ElementCompositionPreview.GetElementVisual(ProductionLayout).Compositor, DispatcherQueue);
            _ = RevealStartupAsync(startupPresentationVersion);
        }
        else StopStartupReveal();
    }

    private async Task RevealStartupAsync(long version)
    {
        try
        {
            var motion = WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled);
            await startupReveal!.PlayAsync((WidgetMotionPlayback[])[new(startupRevealTarget!,
                new(WidgetMotionPose.Identity with { Opacity = 0 }, WidgetMotionPose.Identity, motion.Duration(220)))]);
        }
        catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("startup-reveal", error); }
        finally { if (version == startupPresentationVersion) StopStartupReveal(); }
    }

    private void StopStartupReveal()
    {
        startupReveal?.Dispose(); startupReveal = null;
        startupRevealTarget?.Dispose(); startupRevealTarget = null;
    }

    private bool ReceiveStartupInput(ControllerButton button, ControllerEventPhase phase)
    {
        if (!startupPresentationPending) return false;
        if (phase == ControllerEventPhase.Pressed)
        {
            shellOwnedReleases.Add(button);
            if (button == ControllerButton.B) HideRequested?.Invoke();
        }
        return true;
    }
}
