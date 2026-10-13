using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private Func<Uri, Task<bool>> externalWebLauncher = uri => Windows.System.Launcher.LaunchUriAsync(uri).AsTask();

    private async Task<bool> OpenExternalWebPageAsync(OverlayShellPage page, Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        return await OpenExternalAsync(page,()=>externalWebLauncher(uri),cancellationToken);
    }
    private Task<bool> OpenCaptureFolderAsync(OverlayShellPage page,string path,Func<Task<bool>> current,CancellationToken token)
    {
        var known=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"WidgetRail","Captures");
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path),Path.GetFullPath(known),StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
        return OpenExternalAsync(page,async()=>
        {
            var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            if(!await current()) return false;
            return await Windows.System.Launcher.LaunchFolderAsync(folder);
        },token);
    }
    private async Task<bool> OpenExternalAsync(OverlayShellPage page,Func<Task<bool>> launch,CancellationToken cancellationToken)
    {
        if (cleanupStarted || taskHandoff is not null || !overlayRequestedVisible || !page.HasForeground) return false;
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        using var cancelled = cancellationToken.Register(() => handoff.Cancellation.Cancel());
        try
        {
            if (!await PrepareOverlayHandoffAsync(page, handoff) || !HandoffCurrent(handoff)) return false;
            // The OS launch may immediately transfer foreground. Input is already
            // neutral; do not recheck old foreground after this deliberate effect.
            var opened = await launch();
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
