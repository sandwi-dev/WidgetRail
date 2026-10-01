using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Retained native vector geometry for the existing eight-sector shell design.
/// Selection only changes brushes/visibility; no render callback, bitmap capture
/// or custom drawing loop is involved.
/// </summary>
internal sealed partial class RadialChooserSurface : Canvas
{
    private readonly (ShapePath Fill, ShapePath Light, ShapePath Edge, ShapePath Accent)[] sectors;
    private readonly Ellipse faceLight = Circle(184), faceEdge = Circle(184), hub = Circle(98), hubLight = Circle(98), hubEdge = Circle(98);
    private readonly ShapePath selectionShadow = new() { Fill = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
        Stroke = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)), StrokeThickness = 5, RenderTransform = new TranslateTransform { Y = 3 } };
    private readonly Ellipse hubShadow = Circle(101);
    private readonly LinearGradientBrush lighting = Gradient((0, 0x1c, true), (.45, 0, true), (1, 0x38, false));
    private readonly LinearGradientBrush bevel = Gradient((0, 0x52, true), (.48, 0x0a, true), (1, 0x70, false));
    private readonly SolidColorBrush normalFill = new(), chosenFill = new(), focusInk = new(), edgeInk = new(), hubFill = new();
    internal int VisibleSectorCount => sectors.Count(item => item.Fill.Visibility == Visibility.Visible);
    internal int SelectedSectorCount => sectors.Count(item => item.Accent.Visibility == Visibility.Visible);
    internal Color SelectedFill { get; private set; }
    internal int SelectedSector { get; private set; } = -1;

    internal RadialChooserSurface()
    {
        Width = Height = 400; IsHitTestVisible = false;
        Children.Add(faceLight); Children.Add(faceEdge);
        Children.Add(selectionShadow);
        sectors = Enumerable.Range(0, 8).Select(index =>
        {
            // WinUI Geometry is a single-parent dependency object. Each shape
            // owns its geometry even when the coordinates are identical.
            var fill = new ShapePath { Data = Sector(index) };
            var light = new ShapePath { Data = Sector(index) };
            var edge = new ShapePath { Data = Sector(index), StrokeThickness = .8 };
            var accent = new ShapePath { Data = Arc(index), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Children.Add(fill); Children.Add(light); Children.Add(edge); Children.Add(accent);
            return (fill, light, edge, accent);
        }).ToArray();
        hubShadow.Fill = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)); hubShadow.RenderTransform = new TranslateTransform { Y = 3 };
        Children.Add(hubShadow); Children.Add(hub); Children.Add(hubLight); Children.Add(hubEdge);
    }

    internal void Update(ShellChromePalette palette, int count, int selected)
    {
        if (SelectedSector != selected)
            selectionShadow.Data = selected >= 0 && selected < sectors.Length ? Sector(selected) : null;
        SelectedSector = selected;
        selectionShadow.Visibility = !palette.HighContrast && selected >= 0 && selected < count ? Visibility.Visible : Visibility.Collapsed;
        hubShadow.Visibility = palette.HighContrast ? Visibility.Collapsed : Visibility.Visible;
        faceLight.Fill = palette.HighContrast ? null : lighting;
        edgeInk.Color = palette.Text; focusInk.Color = palette.Focus;
        faceEdge.Stroke = palette.HighContrast ? edgeInk : bevel; faceEdge.StrokeThickness = 1.5;
        var normal = palette.HighContrast ? palette.Surface : ShellChromePalette.Blend(palette.Surface, palette.Text, .055);
        SelectedFill = ShellChromePalette.Composite(palette.Surface, palette.Selected);
        normalFill.Color = normal; chosenFill.Color = SelectedFill;
        for (var index = 0; index < sectors.Length; index++)
        {
            var item = sectors[index]; var chosen = index == selected; var visible = index < count ? Visibility.Visible : Visibility.Collapsed;
            item.Fill.Visibility = item.Light.Visibility = item.Edge.Visibility = visible;
            item.Fill.Fill = chosen ? chosenFill : normalFill;
            item.Light.Fill = palette.HighContrast ? null : lighting;
            item.Edge.Stroke = palette.HighContrast ? edgeInk : bevel;
            item.Edge.StrokeThickness = chosen ? 1.5 : .8;
            item.Accent.Visibility = chosen && index < count ? Visibility.Visible : Visibility.Collapsed;
            item.Accent.Stroke = focusInk; item.Accent.StrokeThickness = Math.Max(3, palette.FocusWidth);
        }
        hubFill.Color = palette.HighContrast ? palette.Surface : ShellChromePalette.Blend(palette.Surface, palette.Text, .045);
        hub.Fill = hubFill;
        hubLight.Fill = palette.HighContrast ? null : lighting;
        hubEdge.Stroke = palette.HighContrast ? edgeInk : bevel; hubEdge.StrokeThickness = 1.5;
    }

    private static Ellipse Circle(double radius)
    {
        var ellipse = new Ellipse { Width = 2 * radius, Height = 2 * radius };
        SetLeft(ellipse, 200 - radius); SetTop(ellipse, 200 - radius); return ellipse;
    }
    private static Point Point(double angle, double radius) => new(200 + Math.Sin(angle) * radius, 200 - Math.Cos(angle) * radius);
    private static ArcSegment Curve(double angle, double radius, SweepDirection direction) => new()
    { Point = Point(angle, radius), Size = new(radius, radius), SweepDirection = direction, IsLargeArc = false };
    private static PathGeometry Sector(int index)
    {
        var start = index * Math.PI / 4 - Math.PI / 8 + .025; var end = index * Math.PI / 4 + Math.PI / 8 - .025;
        var figure = new PathFigure { StartPoint = Point(start, 110), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment { Point = Point(start, 179) });
        figure.Segments.Add(Curve(end, 179, SweepDirection.Clockwise));
        figure.Segments.Add(new LineSegment { Point = Point(end, 110) });
        figure.Segments.Add(Curve(start, 110, SweepDirection.Counterclockwise));
        return new PathGeometry { Figures = { figure } };
    }
    private static PathGeometry Arc(int index)
    {
        var start = index * Math.PI / 4 - Math.PI / 8 + .05; var end = index * Math.PI / 4 + Math.PI / 8 - .05;
        var figure = new PathFigure { StartPoint = Point(start, 177), IsClosed = false, IsFilled = false };
        figure.Segments.Add(Curve(end, 177, SweepDirection.Clockwise));
        return new PathGeometry { Figures = { figure } };
    }
    private static LinearGradientBrush Gradient(params (double Offset, byte Alpha, bool White)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        foreach (var stop in stops)
        { var channel = (byte)(stop.White ? 255 : 0); brush.GradientStops.Add(new() { Offset = stop.Offset, Color = Color.FromArgb(stop.Alpha, channel, channel, channel) }); }
        return brush;
    }
}
