using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly ulong hostWindow;
    private WindowPreviewCaptureService? previewCaptures;
    private WindowPreviewRenderer? previewRenderer;

    private async Task InitializePreviewCapturesAsync()
    {
        // The capture device/dispatcher is independent of XAML. Only negotiate
        // this optional host capability after its renderer is actually available.
        if (hostWindow == 0) return;
        try { previewCaptures = await Task.Run(() => new WindowPreviewCaptureService()); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or System.Runtime.InteropServices.COMException)
        { System.Diagnostics.Trace.WriteLine("WinUI window previews unavailable: " + error.GetType().Name); }
    }

    private WindowPreviewRenderer? CreatePreviewRenderer()
    {
        if (previewCaptures is null || owner is null) return null;
        var renderer = previewCaptures.CreateRenderer(owner.Session, hostWindow);
        renderer.SetVisible(visible && !retired && !switching);
        // Expired/denied capture remains a local placeholder; do not replace the
        // widget's status with a transient capture or permission-refresh failure.
        renderer.Failed += error => System.Diagnostics.Trace.WriteLine("WinUI preview refresh: " + error.GetType().Name);
        return renderer;
    }

    private void ReconcilePreviewVisibility() => previewRenderer?.SetVisible(visible && !retired && !switching);

    private async Task DisposeWidgetSurfaceAsync()
    {
        previewRenderer?.SetVisible(false);
        try { if (surface is not null) await surface.DisposeAsync(); }
        finally
        {
            surface = null;
            var renderer = previewRenderer;
            previewRenderer = null;
            if (renderer is not null) await renderer.DisposeAsync();
        }
    }
}
