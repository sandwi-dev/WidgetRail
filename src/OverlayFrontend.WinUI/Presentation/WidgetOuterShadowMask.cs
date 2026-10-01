using System.Numerics;
using Microsoft.UI.Composition;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Renders only the shadow outside the rounded control, preserving native item
/// templates and translucent content. Uses the native mask-brush technique from
/// CommunityToolkit Windows AttachedCardShadow, without its Win2D dependency.
/// </summary>
internal sealed class WidgetOuterShadowMask : IDisposable
{
    private readonly CompositionRoundedRectangleGeometry outlineGeometry;
    private readonly CompositionColorBrush outlineBrush;
    private readonly CompositionSpriteShape outline;
    private readonly ShapeVisual outlineVisual;
    private readonly CompositionVisualSurface outlineSurface;
    private readonly CompositionSurfaceBrush outlineMask;
    private readonly CompositionVisualSurface shadowSurface;
    private readonly CompositionSurfaceBrush shadowBrush;
    private readonly CompositionMaskBrush maskedBrush;
    internal SpriteVisual Visual { get; }
    internal float Padding { get; private set; }

    internal WidgetOuterShadowMask(SpriteVisual source)
    {
        var compositor = source.Compositor;
        outlineGeometry = compositor.CreateRoundedRectangleGeometry();
        outlineBrush = compositor.CreateColorBrush(Microsoft.UI.Colors.White);
        outline = compositor.CreateSpriteShape(outlineGeometry); outline.StrokeBrush = outlineBrush;
        outlineVisual = compositor.CreateShapeVisual(); outlineVisual.Shapes.Add(outline);
        outlineSurface = compositor.CreateVisualSurface(); outlineSurface.SourceVisual = outlineVisual;
        outlineMask = compositor.CreateSurfaceBrush(outlineSurface);
        shadowSurface = compositor.CreateVisualSurface(); shadowSurface.SourceVisual = source;
        shadowBrush = compositor.CreateSurfaceBrush(shadowSurface); shadowBrush.Stretch = CompositionStretch.None;
        maskedBrush = compositor.CreateMaskBrush(); maskedBrush.Source = shadowBrush; maskedBrush.Mask = outlineMask;
        Visual = compositor.CreateSpriteVisual(); Visual.Brush = maskedBrush;
    }

    internal void Resize(Vector2 size, float radius, float blur, Vector2 offset)
    {
        // Include the signed offset and blur halo in the sampled texture. The
        // outline's inner boundary always stays at the unchanged control bounds.
        Padding = MathF.Ceiling(Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) + 2 * blur + 1);
        var inset = new Vector2(Padding);
        var extent = size + inset * 2;
        outlineGeometry.Offset = inset / 2;
        outlineGeometry.Size = size + inset;
        outlineGeometry.CornerRadius = new(Padding / 2 + Math.Clamp(radius, 0, Math.Min(size.X, size.Y) / 2));
        outline.StrokeThickness = Padding;
        outlineVisual.Size = outlineSurface.SourceSize = extent;
        shadowSurface.SourceOffset = -inset; shadowSurface.SourceSize = extent;
        Visual.Size = extent; Visual.Offset = new(-inset, 0);
    }

    public void Dispose()
    {
        Visual.Brush = null; maskedBrush.Source = null; maskedBrush.Mask = null;
        shadowSurface.SourceVisual = null; outlineSurface.SourceVisual = null;
        outlineVisual.Shapes.Clear(); outline.StrokeBrush = null;
        Visual.Dispose(); maskedBrush.Dispose(); shadowBrush.Dispose(); shadowSurface.Dispose();
        outlineMask.Dispose(); outlineSurface.Dispose(); outlineVisual.Dispose();
        outline.Dispose(); outlineBrush.Dispose(); outlineGeometry.Dispose();
    }
}
