using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Web;

/// <summary>Per-controller memory hint. Never suspends scripts, connections or playback.</summary>
internal sealed class HostedWebViewMemoryPolicy(CoreWebView2 core, DispatcherQueue dispatcher, Action<Exception> unavailable) : IDisposable
{
    internal static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(15);
    private DispatcherQueueTimer? timer;
    private CoreWebView2MemoryUsageTargetLevel target = CoreWebView2MemoryUsageTargetLevel.Normal;
    private bool stopped, requiresNormal = true;

    internal void Update(bool normal)
    {
        if (stopped) return;
        requiresNormal = normal;
        if (normal)
        {
            timer?.Stop();
            SetTarget(CoreWebView2MemoryUsageTargetLevel.Normal);
        }
        else if (target != CoreWebView2MemoryUsageTargetLevel.Low && timer?.IsRunning != true)
        {
            if (timer is null)
            {
                timer = dispatcher.CreateTimer();
                timer.IsRepeating = false;
                timer.Interval = IdleDelay;
                timer.Tick += OnTimer;
            }
            timer.Start();
        }
    }

    internal void CancelPending() => timer?.Stop();

    private void OnTimer(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!stopped && !requiresNormal) SetTarget(CoreWebView2MemoryUsageTargetLevel.Low);
    }

    private void SetTarget(CoreWebView2MemoryUsageTargetLevel value)
    {
        if (target == value) return;
        try { core.MemoryUsageTargetLevel = value; target = value; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or NotImplementedException or InvalidOperationException)
        {
            Dispose(); // An unsupported optimization must not interrupt the document.
            unavailable(error);
        }
    }

    public void Dispose()
    {
        stopped = true;
        if (timer is null) return;
        timer.Stop(); timer.Tick -= OnTimer; timer = null;
    }
}
