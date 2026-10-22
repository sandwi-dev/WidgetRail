using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private Func<WindowsTaskViewResult> systemTaskViewLauncher = WindowsTaskView.Open;

    private async Task<WindowsTaskViewResult> OpenSystemTaskViewAsync(OverlayShellPage page)
    {
        if (cleanupStarted || taskHandoff is not null || !overlayRequestedVisible || !page.HasForeground)
            return WindowsTaskViewResult.Cancelled;
        if (WindowsTaskView.KeyboardBusy()) return WindowsTaskViewResult.KeyboardBusy;
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (!await PrepareOverlayHandoffAsync(page, handoff) || !HandoffCurrent(handoff)) return WindowsTaskViewResult.Cancelled;
            if (WindowsTaskView.KeyboardBusy()) return WindowsTaskViewResult.KeyboardBusy;
            handoff.Committed = true;
            input?.SetVisible(false);
            CompleteOverlayHide();
            var result = systemTaskViewLauncher();
            input?.TraceInput("Windows Task View shortcut result=" + result);
            return result;
        }
        catch (OperationCanceledException) when (handoff.Cancellation.IsCancellationRequested)
        { return WindowsTaskViewResult.Cancelled; }
        finally { FinishOverlayHandoff(page, handoff); }
    }
}
