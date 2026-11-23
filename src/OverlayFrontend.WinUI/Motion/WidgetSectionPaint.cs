using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Draws a mounted section in its exit animation without moving the native controls.</summary>
internal sealed class WidgetSectionPaint : IDisposable
{
    private readonly Grid target = new();
    private readonly CompositionVisualSurface surface;
    private readonly CompositionSurfaceBrush brush;
    private readonly SpriteVisual visual;
    internal WidgetMotionHost Host { get; }

    internal WidgetSectionPaint(WidgetMotionHost source)
    {
        var sourceVisual = ElementCompositionPreview.GetElementVisual(source.Layer);
        var compositor = sourceVisual.Compositor;
        var size = new Vector2((float)source.ActualWidth, (float)source.ActualHeight);
        Host = new(target);
        surface = compositor.CreateVisualSurface();
        surface.SourceVisual = sourceVisual;
        surface.SourceSize = size;
        brush = compositor.CreateSurfaceBrush(surface);
        visual = compositor.CreateSpriteVisual();
        visual.Brush = brush;
        visual.Size = size;
        ElementCompositionPreview.SetElementChildVisual(target, visual);
    }

    public void Dispose()
    {
        ElementCompositionPreview.SetElementChildVisual(target, null);
        visual.Brush = null;
        surface.SourceVisual = null;
        visual.Dispose();
        brush.Dispose();
        surface.Dispose();
    }
}
