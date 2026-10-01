namespace WidgetRail.OverlayFrontend.WinUI.Previews;

internal sealed partial class WindowPreviewRenderer
{
    internal (string WindowId, NativePreviewStats Stats)[] ShutdownValidationSamples()
    {
        lock (gate) return surfaces.Select(surface => (surface.WindowId, surface.LastStats)).ToArray();
    }
}
