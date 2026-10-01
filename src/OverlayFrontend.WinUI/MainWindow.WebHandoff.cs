using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private Func<Uri, Task<bool>> externalWebLauncher = uri => Windows.System.Launcher.LaunchUriAsync(uri).AsTask();

    private async Task<bool> OpenExternalWebPageAsync(OverlayShellPage page, Uri uri, CancellationToken cancellationToken)
    {
        if (cleanupStarted || taskHandoff is not null || !overlayRequestedVisible || !page.HasForeground ||
            !uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        using var cancelled = cancellationToken.Register(() => handoff.Cancellation.Cancel());
        try
        {
            if (!await PrepareOverlayHandoffAsync(page, handoff) || !HandoffCurrent(handoff)) return false;
            // The OS launch may immediately transfer foreground. Input is already
            // neutral; do not recheck old foreground after this deliberate effect.
            var opened = await externalWebLauncher(uri);
            if (!opened) return false;
            handoff.Committed = true;
            if (handoff.Version == overlayVisibilityVersion && !overlayRequestedVisible)
            {
                input?.SetVisible(false);
                CompleteOverlayHide();
            }
            return true;
        }
        catch (OperationCanceledException) when (handoff.Cancellation.IsCancellationRequested) { return false; }
        finally { FinishOverlayHandoff(page, handoff); }
    }
}
