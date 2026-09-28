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
    private readonly HashSet<WindowPreviewRenderer> renderers = [];
    private readonly HashSet<WindowPreviewSurface> callbackRoots = [];
    private Task? disposal;
    private bool retired;

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
            await Task.Run(engine.Dispose);
            lock (gate)
            {
                foreach (var surface in callbackRoots) surface.ReleaseRootAfterOwnerStopped();
                callbackRoots.Clear(); renderers.Clear();
            }
        }
    }
}
