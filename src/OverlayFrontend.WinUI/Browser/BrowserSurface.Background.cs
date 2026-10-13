using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal static bool RequiresPageRecovery(Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind kind) =>
        kind is Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.BrowserProcessExited or
            Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.RenderProcessExited;
    private bool sessionStopped;
    private string? pendingRestartUrl;
    internal bool RequestsBackgroundSession => !IsProviderContent && document.Options.Background == BrowserBackgroundMode.KeepAlive;
    internal bool KeepsBackgroundSession => RequestsBackgroundSession && !sessionStopped && !faulted;
    internal bool SessionStopped => sessionStopped;
    internal bool BackgroundFaulted => RequestsBackgroundSession && faulted && !sessionStopped;
    internal bool NeedsRecovery => AttachedPopup?.NeedsRecovery ?? faulted;
    internal string SessionName => document.AccessibleName;
    internal Func<BrowserSurface, bool, Task<bool>>? ChangeRunning { get; set; }
    internal Action? ActivityChanged { get; set; }
    internal Func<BrowserSurface, Task>? Recover { get; set; }
    internal async Task StopDocumentAsync(CancellationToken token)
    {
        await DrainPopupAsync();
        try { await ParkForProfileChangeAsync(token); sessionStopped = true; }
        finally { profileChanging = false; SetPresentation(visible, presentationInputRequested); }
        welcome.Text = faulted ? "Webapp session stopped.\nPress A to reopen it." : "Webapp session stopped.\nPress X or choose Reload to open it again.";
        welcome.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        ShowStatus(null);
        UpdateChrome();
    }

    internal void RestartStoppedDocument()
    {
        if (!sessionStopped || retired || faulted) return;
        sessionStopped = false;
        welcome.Text = EmptyBrowserMessage;
        welcome.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        appliedNavigation = document.NavigationId;
        core?.Navigate(pendingRestartUrl ?? document.Url);
        UpdateChrome();
    }

    private async Task NavigateRequestedAsync(string url)
    {
        if (retired || faulted || core is null || !WebBrowserDocument.IsWebUrl(url)) return;
        if (!sessionStopped) { core.Navigate(url); return; }
        if (ChangeRunning is not { } change) return;
        pendingRestartUrl = url;
        try { if (!await change(this, true)) ShowStatus("This webapp could not restart. Stop another background session and try again."); }
        finally { pendingRestartUrl = null; }
    }
}
