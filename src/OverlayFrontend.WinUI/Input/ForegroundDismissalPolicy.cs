namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Mirrors the native host's valid, current, external foreground rule.</summary>
internal static class ForegroundDismissalPolicy
{
    internal static bool ShouldDismiss(bool visible, nint observed, nint current, bool valid,
        uint foregroundProcess, uint hostProcess) => visible && observed != 0 && observed == current &&
        valid && foregroundProcess != 0 && foregroundProcess != hostProcess;
}
