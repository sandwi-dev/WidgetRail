using WidgetRail.OverlayFrontend.WinUI.Shell;
namespace WidgetRail.OverlayFrontend.WinUI;
public sealed partial class MainWindow
{
    private async Task<bool> HideForCaptureAsync(OverlayShellPage page, CancellationToken token)
    {
        if (cleanupStarted || taskHandoff is not null || !AppWindow.IsVisible) return false;
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        using var cancelled = token.Register(() => handoff.Cancellation.Cancel());
        try
        {
            if (!await PrepareOverlayHandoffAsync(page, handoff) || !HandoffCurrent(handoff)) return false;
            handoff.Committed = true;
            input?.SetVisible(false);
            CompleteOverlayHide();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        finally { FinishOverlayHandoff(page, handoff); }
    }
}
