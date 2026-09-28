using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private void InitializeSemanticGuide()
    {
        trayGuide.Invoked += async hint =>
        {
            if (hint.Prompt == ControllerPrompt.Guide) { HideRequested?.Invoke(); return; }
            if (hint.Button is not { } button) return;
            // Existing routing validates current widget/scope/row authority. The
            // guide never dispatches a captured action or acquires native focus.
            await RouteButtonAsync(button, ControllerEventPhase.Pressed);
            await RouteButtonAsync(button, ControllerEventPhase.Released);
        };
    }
}
