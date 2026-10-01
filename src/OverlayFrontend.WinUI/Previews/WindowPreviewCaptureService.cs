using WidgetRail.WidgetPresentationSession;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

/// <summary>
/// One shell-wide GPU owner and capture budget, shared by every widget renderer.
/// Dispose it on the WinUI dispatcher after retiring shell presentations.
/// </summary>
internal sealed class WindowPreviewCaptureService : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly NativePreviewEngine engine = new();
    private readonly NativePreviewHealthReader health;
    private readonly Timer healthTimer;
    private readonly PreviewCloseWatchdog watchdog = new();
    private int readingHealth, healthFailed;
    private readonly HashSet<WindowPreviewRenderer> renderers = [];
    private readonly HashSet<WindowPreviewSurface> callbackRoots = [];
    private Task? disposal;
    private bool retired;

    internal WindowPreviewCaptureService()
    {
        try
        {
            health = new(engine);
            healthTimer = new(_ => ObserveHealth(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch { health?.Dispose(); engine.Dispose(); throw; }
    }

    private void ObserveHealth()
    {
        if (Interlocked.Exchange(ref readingHealth, 1) != 0) return;
        try
        {
            var sample = NativePreviewHealth.Empty;
            // E_PENDING means the owner changed phases during this bounded read.
            // Retry next tick; never wait on the capture queue or spin here.
            if (PreviewNative.ReadHealth(health, ref sample) != 0) return;
            if (watchdog.Observe(sample, System.Diagnostics.Stopwatch.GetTimestamp()) is { } message)
                Diagnostics.FrontendFailureLog.Current.Write("preview-close", null, message);
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref healthFailed, 1) == 0)
                Diagnostics.FrontendFailureLog.Current.Write("preview-health", error);
        }
        finally { Volatile.Write(ref readingHealth, 0); }
    }

    internal WindowPreviewRenderer CreateRenderer(PresentationSession session, ulong hostWindow)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(retired, this);
            var renderer = new WindowPreviewRenderer(this, engine, session, hostWindow);
            renderers.Add(renderer);
            return renderer;
        }
    }
    internal void Retire(WindowPreviewRenderer renderer, IEnumerable<WindowPreviewSurface> surfaces)
    {
        lock (gate)
        {
            renderers.Remove(renderer);
            foreach (var surface in surfaces)
                if (surface.HasNativeCallbackRoot) callbackRoots.Add(surface);
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        WindowPreviewRenderer[] owned;
        lock (gate) { retired = true; owned = renderers.ToArray(); }
        try { await Task.WhenAll(owned.Select(renderer => renderer.DisposeAsync().AsTask())); }
        finally
        {
            try
            {
                await Task.Run(engine.Dispose);
                lock (gate)
                {
                    foreach (var surface in callbackRoots) surface.ReleaseRootAfterOwnerStopped();
                    callbackRoots.Clear(); renderers.Clear();
                }
            }
            finally
            {
                await healthTimer.DisposeAsync();
                ObserveHealth();
                health.Dispose();
            }
        }
    }
}
