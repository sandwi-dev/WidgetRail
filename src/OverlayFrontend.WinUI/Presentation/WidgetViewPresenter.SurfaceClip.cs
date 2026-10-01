using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private CompositionRoundedRectangleGeometry? surfaceGeometry;
    private CompositionGeometricClip? surfaceClip;
    private double surfaceRadius;

    // The shell background and widget tree are siblings. Rounding the background
    // alone cannot clip an opaque widget root or its compositor animations.
    internal void SetSurfaceCornerRadius(double radius)
    {
        surfaceRadius = double.IsFinite(radius) ? Math.Clamp(radius, 0, 256) : 0;
        if (surfaceGeometry is null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(this);
            surfaceGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
            surfaceClip = visual.Compositor.CreateGeometricClip(surfaceGeometry);
            visual.Clip = surfaceClip;
            SizeChanged += SurfaceClipSizeChanged;
        }
        UpdateSurfaceClip();
    }

    private void SurfaceClipSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSurfaceClip();
    private void UpdateSurfaceClip()
    {
        if (surfaceGeometry is null) return;
        var size = new Vector2((float)Math.Max(0, ActualWidth), (float)Math.Max(0, ActualHeight));
        surfaceGeometry.Size = size;
        surfaceGeometry.CornerRadius = new((float)Math.Min(surfaceRadius, Math.Min(size.X, size.Y) / 2));
    }

    private void DisposeSurfaceClip()
    {
        SizeChanged -= SurfaceClipSizeChanged;
        if (surfaceClip is not null) ElementCompositionPreview.GetElementVisual(this).Clip = null;
        surfaceClip?.Dispose(); surfaceClip = null;
        surfaceGeometry?.Dispose(); surfaceGeometry = null;
    }
}
