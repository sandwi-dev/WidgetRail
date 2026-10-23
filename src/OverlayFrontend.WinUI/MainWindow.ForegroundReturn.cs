using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private TaskWindowTarget? foregroundReturnTarget;

    private void RememberForegroundReturn()
    {
        var native = new WindowsTaskWindowActivation();
        // Reopening during exit or returning from an owned pin must not replace
        // the external destination with one of our own windows. A genuinely new
        // external foreground replaces it, even if its identity cannot be read.
        if (native.IsOverlayForeground || nativeBrowserDialog?.HasForeground == true) return;
        var candidate = native.CaptureForegroundTarget();
        if (candidate is not null && RootFrame.Content is Shell.OverlayShellPage page && page.OwnsBrowserUiProcess(candidate.ProcessId)) return;
        foregroundReturnTarget = candidate;
    }

    private void ReturnForegroundBeforeHide()
    {
        var target = foregroundReturnTarget;
        foregroundReturnTarget = null;
        if (target is null) return;
        var native = new WindowsTaskWindowActivation(message => input?.TraceInput(message));
        var accepted = native.TryReturnForeground(target);
        input?.TraceForegroundState($"Ordinary close foreground return accepted={accepted} target={target.Handle}");
    }
}
