using System.Numerics;
using Microsoft.UI.Composition;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Continuous antialiased edging for uniform rounded depth surfaces. Thin
/// rectangular edge strips stop before the corners of a pill; a rounded stroke
/// follows the complete contour and blends the authored lighting at its corners.
/// All resources stay on the compositor and are reused on resize/state changes.
/// </summary>
internal sealed class WidgetRoundedDepthEdges : IDisposable
{
    private readonly CompositionRoundedRectangleGeometry geometry;
    private readonly List<(ShapeVisual Visual, CompositionSpriteShape Shape,
        CompositionLinearGradientBrush Brush, InsetClip Clip)> halves = [];

    internal WidgetRoundedDepthEdges(ContainerVisual parent)
    {
        var compositor = parent.Compositor;
        geometry = compositor.CreateRoundedRectangleGeometry();
        for (var index = 0; index < 2; index++)
        {
            var brush = compositor.CreateLinearGradientBrush();
            brush.StartPoint = Vector2.Zero; brush.EndPoint = new(0, 1);
            for (var stop = 0; stop < 4; stop++) brush.ColorStops.Add(compositor.CreateColorGradientStop());
            var shape = compositor.CreateSpriteShape(geometry); shape.StrokeBrush = brush;
            var visual = compositor.CreateShapeVisual(); visual.Shapes.Add(shape);
            var clip = compositor.CreateInsetClip(); visual.Clip = clip;
            parent.Children.InsertAtTop(visual);
            halves.Add((visual, shape, brush, clip));
        }
    }

    internal void Update(Vector2 size, float radius, float width, Color top, Color right, Color bottom, Color left, bool visible)
    {
        foreach (var half in halves) half.Visual.IsVisible = visible;
        if (!visible) return;
        width = Math.Clamp(width, 0, Math.Min(size.X, size.Y));
        geometry.Offset = new(width / 2);
        geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(width));
        var corner = Math.Clamp(radius, 0, Math.Min(size.X, size.Y) / 2);
        geometry.CornerRadius = new(Math.Max(0, corner - width / 2));
        var turn = Math.Clamp(corner / size.Y, 0, .5f);
        for (var index = 0; index < halves.Count; index++)
        {
            var half = halves[index];
            half.Visual.Size = size; half.Shape.StrokeThickness = width;
            half.Clip.LeftInset = index == 1 ? size.X / 2 : 0;
            half.Clip.RightInset = index == 0 ? size.X / 2 : 0;
            var side = index == 0 ? left : right;
            Stop(0, 0, top); Stop(1, turn, side); Stop(2, 1 - turn, side); Stop(3, 1, bottom);
            void Stop(int ordinal, float offset, Color color)
            { half.Brush.ColorStops[ordinal].Offset = offset; half.Brush.ColorStops[ordinal].Color = color; }
        }
    }

    public void Dispose()
    {
        foreach (var half in halves)
        {
            half.Visual.Shapes.Clear(); half.Visual.Clip = null; half.Shape.StrokeBrush = null;
            foreach (var stop in half.Brush.ColorStops) stop.Dispose();
            half.Brush.ColorStops.Clear(); half.Clip.Dispose(); half.Visual.Dispose();
            half.Shape.Dispose(); half.Brush.Dispose();
        }
        halves.Clear(); geometry.Dispose();
    }
}
