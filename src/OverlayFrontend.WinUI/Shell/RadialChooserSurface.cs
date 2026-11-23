using Microsoft.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
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
    private readonly (ShapePath Fill, ShapePath Light, ShapePath Edge)[] sectors;
    private readonly Ellipse faceEdge = Circle(184), hub = Circle(98), hubLight = Circle(98), hubEdge = Circle(98);
    private readonly Grid selectionGlow = new() { Width = 400, Height = 400 };
    private readonly ShapePath glowCaster = new() { Width = 400, Height = 400, Data = Sector(0), Stretch = Stretch.None };
    private readonly RotateTransform glowAngle = new() { CenterX = 200, CenterY = 200 };
    private CompositionBrush? glowMask;
    private DropShadow? glowShadow;
    private SpriteVisual? glowVisual;
    private readonly LinearGradientBrush selectedSheen = new() { StartPoint = new(.12, 0), EndPoint = new(.90, 1),
        GradientStops = { new() { Offset = 0 }, new() { Offset = .52 }, new() { Offset = 1 } } };
    private readonly Ellipse hubShadow = Circle(101);
    private readonly LinearGradientBrush lighting = Gradient((0, 0x31, true), (.46, 0x0c, true), (1, 0x1a, false));
    private readonly LinearGradientBrush bevel = Gradient((0, 0x85, true), (.5, 0x54, true), (1, 0x32, true));
    private readonly LinearGradientBrush wedgeRim = new() { StartPoint = new(0, 0), EndPoint = new(1, 1),
        GradientStops = { new() { Offset = 0 }, new() { Offset = .55 }, new() { Offset = 1 } } };
    private readonly SolidColorBrush normalFill = new(), chosenFill = new(), focusInk = new(), edgeInk = new(), hubFill = new();
    private ShellChromePalette? lastPalette;
    private int lastCount = -1;
    internal int VisibleSectorCount => sectors.Count(item => item.Fill.Visibility == Visibility.Visible);
    internal int SelectedSectorCount => sectors.Count(item => item.Edge.Visibility == Visibility.Visible && ReferenceEquals(item.Edge.Stroke, focusInk));
    internal Color SelectedFill { get; private set; }
    internal int SelectedSector { get; private set; } = -1;

    internal RadialChooserSurface()
    {
        Width = Height = 400; IsHitTestVisible = false;
        Children.Add(faceEdge);
        selectionGlow.RenderTransform = glowAngle;
        glowCaster.Fill = focusInk;
        selectionGlow.Children.Add(glowCaster);
        selectionGlow.Loaded += (_, _) => UpdateGlow();
        selectionGlow.Unloaded += (_, _) => ReleaseGlow();
        Children.Add(selectionGlow);
        sectors = Enumerable.Range(0, 8).Select(index =>
        {
            // WinUI Geometry is a single-parent dependency object. Each shape
            // owns its geometry even when the coordinates are identical.
            var fill = new ShapePath { Data = Sector(index) };
            var light = new ShapePath { Data = Sector(index) };
            var edge = new ShapePath { Data = Sector(index), StrokeLineJoin = PenLineJoin.Round };
            Children.Add(fill); Children.Add(light); Children.Add(edge);
            return (fill, light, edge);
        }).ToArray();
        hubShadow.Fill = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)); hubShadow.RenderTransform = new TranslateTransform { Y = 3 };
        Children.Add(hubShadow); Children.Add(hub); Children.Add(hubLight); Children.Add(hubEdge);
    }

    internal void Update(ShellChromePalette palette, int count, int selected)
    {
        if (lastPalette == palette && lastCount == count && SelectedSector == selected) return;
        if (lastPalette != palette)
        {
            wedgeRim.GradientStops[0].Color = ShellChromePalette.Blend(palette.Surface, palette.Text, .52);
            wedgeRim.GradientStops[1].Color = ShellChromePalette.Blend(palette.Surface, palette.Text, .36);
            wedgeRim.GradientStops[2].Color = ShellChromePalette.Blend(palette.Surface, palette.Text, .29);
        }
        lastPalette = palette;
        lastCount = count;
        SelectedSector = selected;
        selectionGlow.Visibility = !palette.HighContrast && selected >= 0 && selected < count ? Visibility.Visible : Visibility.Collapsed;
        glowAngle.Angle = 45 * selected;
        UpdateGlow();
        hubShadow.Visibility = palette.HighContrast ? Visibility.Collapsed : Visibility.Visible;
        edgeInk.Color = palette.Text; focusInk.Color = palette.Focus;
        faceEdge.Stroke = palette.HighContrast ? edgeInk : bevel; faceEdge.StrokeThickness = 1.5;
        var normal = palette.HighContrast ? palette.Surface : ShellChromePalette.Blend(palette.Surface, palette.Text, .055);
        var selectedBase = ShellChromePalette.Composite(palette.Surface, palette.Selected);
        SelectedFill = palette.HighContrast ? selectedBase : ShellChromePalette.Blend(selectedBase, palette.Focus, .26);
        selectedSheen.GradientStops[0].Color = ShellChromePalette.Blend(SelectedFill, palette.Focus, .45);
        selectedSheen.GradientStops[1].Color = SelectedFill;
        selectedSheen.GradientStops[2].Color = ShellChromePalette.Blend(selectedBase, palette.Focus, .12);
        normalFill.Color = normal; chosenFill.Color = SelectedFill;
        for (var index = 0; index < sectors.Length; index++)
        {
            var item = sectors[index]; var chosen = index == selected; var visible = index < count ? Visibility.Visible : Visibility.Collapsed;
            item.Fill.Visibility = item.Light.Visibility = item.Edge.Visibility = visible;
            item.Fill.Fill = chosen ? palette.HighContrast ? chosenFill : selectedSheen : normalFill;
            item.Light.Fill = palette.HighContrast ? null : lighting;
            item.Edge.Stroke = chosen ? focusInk : palette.HighContrast ? edgeInk : wedgeRim;
            item.Edge.StrokeThickness = chosen ? Math.Max(2.5, palette.FocusWidth) : 1.6;
        }
        hubFill.Color = palette.HighContrast ? palette.Surface : ShellChromePalette.Blend(palette.Surface, palette.Text, .045);
        hub.Fill = hubFill;
        hubLight.Fill = palette.HighContrast ? null : lighting;
        hubEdge.Stroke = palette.HighContrast ? edgeInk : bevel; hubEdge.StrokeThickness = 1.5;
    }

    private void UpdateGlow()
    {
        if (!selectionGlow.IsLoaded || selectionGlow.Visibility != Visibility.Visible || lastPalette is null) return;
        if (glowVisual is null)
        {
            // One mask for the top wedge. Rotate the whole caster when selection changes.
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            glowMask = glowCaster.GetAlphaMask();
            glowShadow = compositor.CreateDropShadow();
            glowShadow.Mask = glowMask;
            glowShadow.BlurRadius = 16;
            glowShadow.Opacity = .66f;
            glowVisual = compositor.CreateSpriteVisual();
            glowVisual.Size = new(400, 400);
            glowVisual.Shadow = glowShadow;
            ElementCompositionPreview.SetElementChildVisual(selectionGlow, glowVisual);
        }
        glowShadow!.Color = lastPalette.Focus;
    }

    private void ReleaseGlow()
    {
        if (glowVisual is null) return;
        ElementCompositionPreview.SetElementChildVisual(selectionGlow, null);
        glowVisual.Shadow = null;
        glowShadow!.Mask = null;
        glowVisual.Dispose(); glowVisual = null;
        glowShadow.Dispose(); glowShadow = null;
        glowMask?.Dispose(); glowMask = null;
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
    private static LinearGradientBrush Gradient(params (double Offset, byte Alpha, bool White)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        foreach (var stop in stops)
        { var channel = (byte)(stop.White ? 255 : 0); brush.GradientStops.Add(new() { Offset = stop.Offset, Color = Color.FromArgb(stop.Alpha, channel, channel, channel) }); }
        return brush;
    }
}
