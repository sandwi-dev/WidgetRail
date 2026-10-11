using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetLineGraph
{
    internal int ValidationFigureCount => (line.Data as PathGeometry)?.Figures.Count ?? 0;
    internal bool ValidationBrushMatches => ReferenceEquals(line.Stroke, Foreground) && ReferenceEquals(upper.Foreground, Foreground);
    internal bool ValidationGeometryFits => line.Data is PathGeometry geometry && geometry.Figures.All(f =>
        f.StartPoint.X >= 0 && f.StartPoint.X <= plot.ActualWidth && f.Segments.OfType<LineSegment>().All(s =>
            s.Point.X >= 0 && s.Point.X <= plot.ActualWidth && s.Point.Y >= 0 && s.Point.Y <= plot.ActualHeight));
}
