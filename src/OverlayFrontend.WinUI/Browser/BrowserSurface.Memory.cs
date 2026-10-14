using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    // Avoid trimming during ordinary widget switches and fullscreen transfers.
    internal static readonly TimeSpan HiddenMemoryDelay = TimeSpan.FromSeconds(15);
    private DispatcherQueueTimer? memoryTimer;
    private CoreWebView2MemoryUsageTargetLevel memoryTarget = CoreWebView2MemoryUsageTargetLevel.Normal;
    private bool memoryTargetUnavailable;
#if ENABLE_WIDGET_VALIDATION
    internal bool ManualMemoryTargetForValidation { get; set; }
#endif

    private void UpdateMemoryPolicy()
    {
        if (retired || faulted || core is null || memoryTargetUnavailable)
        { memoryTimer?.Stop(); return; }
#if ENABLE_WIDGET_VALIDATION
        if (ManualMemoryTargetForValidation) { memoryTimer?.Stop(); return; }
#endif
        if (visible && AttachedPopup is null)
        {
            memoryTimer?.Stop();
            SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Normal);
            return;
        }
        if (memoryTarget == CoreWebView2MemoryUsageTargetLevel.Low || memoryTimer?.IsRunning == true) return;
        if (memoryTimer is null)
        {
            memoryTimer = DispatcherQueue.CreateTimer();
            memoryTimer.IsRepeating = false;
            memoryTimer.Interval = HiddenMemoryDelay;
            memoryTimer.Tick += OnMemoryTimer;
        }
        memoryTimer.Start();
    }

    private void OnMemoryTimer(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!retired && !faulted && (!visible || AttachedPopup is not null))
            SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Low);
    }

    private void SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel target)
    {
        if (core is null || memoryTargetUnavailable || memoryTarget == target) return;
        try
        {
            // Low keeps scripts, connections and media running. Do not combine
            // this policy with TrySuspend/Resume or infer activity from permission grants.
            core.MemoryUsageTargetLevel = target;
            memoryTarget = target;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or NotImplementedException or ObjectDisposedException)
        {
            // Optimization failure must never fault a page or interrupt a call.
            memoryTargetUnavailable = true;
            memoryTimer?.Stop();
            Diagnostics.FrontendFailureLog.Current.Write("browser-memory-target", null, $"Unavailable: {error.GetType().Name}");
        }
    }

    private void DisposeMemoryPolicy()
    {
        if (memoryTimer is null) return;
        memoryTimer.Stop(); memoryTimer.Tick -= OnMemoryTimer; memoryTimer = null;
    }
}
