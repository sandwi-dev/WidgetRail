using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using Windows.Foundation;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Paints the wheel surround from the final layout, independently of sector selection and input.</summary>
internal sealed partial class RadialContourStage : Grid
{
    internal const double Gutter = 14;
    private const double Radius = 195;
    private readonly Grid backdrop = new() { IsHitTestVisible = false };
    private readonly SolidColorBrush tint = new();
    private readonly RadialGradientBrush rimFade = new() { GradientStops =
        { new() { Offset = .94 }, new() { Offset = .97 }, new() { Offset = 1 } } };
    private readonly Ellipse circle = new() { Width = Radius * 2, Height = Radius * 2,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas silhouette = new() { IsHitTestVisible = false };
    // Adjacent, non-overlapping bands share the same native backdrop brush. The blur is
    // uniform; the coverage tapers off across the last 12 DIPs of the measured contour.
    private const double Feather = 12;
    private static readonly double[] Coverage = [.035, .12, .26, .43, .61, .73];
    private readonly ShapePath[] bands = Coverage.Select(alpha => new ShapePath { Stretch = Stretch.None, Opacity = alpha }).ToArray();
    private readonly ShapePath solidContour = new() { Stretch = Stretch.None };
    private AcrylicBrush? acrylic;
    private (Windows.UI.Color Surface, bool Opaque, bool Acrylic)? materialKey;
    private readonly Button[] hints = new Button[4];
    private readonly ScalarTransition fade = new() { Duration = TimeSpan.FromMilliseconds(140) };
    private readonly record struct Layout(Size Size, Rect Up, Rect Right, Rect Down, Rect Left);
    private Layout? cached;
    internal bool Reordering { get; set; }
#if ENABLE_WIDGET_VALIDATION
    internal int GeometryBuilds { get; private set; }
#endif

    internal RadialContourStage()
    {
        circle.Fill = tint;
        solidContour.Fill = tint;
        silhouette.Children.Add(solidContour);
        foreach (var band in bands) silhouette.Children.Add(band);
        circle.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(circle, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(silhouette, AccessibilityView.Raw);
        backdrop.Children.Add(circle);
        backdrop.Children.Add(silhouette);
        SetColumnSpan(backdrop, 3);
        SetRowSpan(backdrop, 3);
        Children.Add(backdrop);
    }

    internal void AddHint(TrayDirection direction, Button hint)
    {
        hints[(int)direction] = hint;
        Children.Add(hint);
    }

    internal void ApplyAppearance(ShellChromePalette palette, AppearanceSettings appearance, bool animations)
    {
        var opaque = palette.HighContrast || appearance.Transparency == TransparencyPreference.Reduced;
        var transition = WidgetMotionOptions.From(appearance, animations).Reduced ? null : fade;
        if (silhouette.OpacityTransition != transition) circle.OpacityTransition = silhouette.OpacityTransition = transition;
        var key = (palette.Surface, opaque, !opaque && appearance.AcrylicEffects);
        if (materialKey == key) return;
        materialKey = key;
        tint.Color = palette.Surface;
        if (!opaque && appearance.AcrylicEffects)
        {
            acrylic ??= new AcrylicBrush { TintOpacity = .40, TintLuminosityOpacity = .37, TintTransitionDuration = TimeSpan.Zero };
            acrylic.TintColor = acrylic.FallbackColor = palette.Surface;
        }
        else acrylic = null;
        Brush material = (Brush?)acrylic ?? tint;
        // With no visible lobes only a narrow rim is exposed around the opaque wheel.
        rimFade.GradientStops[0].Color = palette.Surface with { A = 185 };
        rimFade.GradientStops[1].Color = palette.Surface with { A = 77 };
        rimFade.GradientStops[2].Color = palette.Surface with { A = 0 };
        circle.Fill = opaque ? tint : rimFade;
        solidContour.Visibility = opaque ? Visibility.Visible : Visibility.Collapsed;
        foreach (var band in bands)
        {
            band.Visibility = opaque ? Visibility.Collapsed : Visibility.Visible;
            band.Fill = opaque ? null : material;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (!Reordering)
        {
            var layout = new Layout(size, Bounds(TrayDirection.Up), Bounds(TrayDirection.Right),
                Bounds(TrayDirection.Down), Bounds(TrayDirection.Left));
            if (layout != cached)
            {
                cached = layout;
                solidContour.Data = BuildSilhouette(layout, 0);
                for (var i = 0; i < bands.Length; i++)
                {
                    var geometry = BuildSilhouette(layout, Feather * i / (bands.Length - 1));
                    if (geometry is not null && i < bands.Length - 1)
                        geometry.Figures.Add(Outline(layout, Feather * (i + 1) / (bands.Length - 1)));
                    bands[i].Data = geometry;
                }
#if ENABLE_WIDGET_VALIDATION
                GeometryBuilds++;
#endif
            }
        }
        // During reorder the hint controls collapse. Keep the last path for its fade
        // and reuse it on return when the arranged bounds still match.
        var opacity = Reordering || solidContour.Data is null ? 0d : 1d;
        if (silhouette.Opacity != opacity) silhouette.Opacity = opacity;
        if (circle.Opacity != 1 - opacity) circle.Opacity = 1 - opacity;
        return size;
    }

    private Rect Bounds(TrayDirection direction)
    {
        var hint = hints[(int)direction];
        if (hint is not { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 }) return default;
        var offset = hint.ActualOffset;
        return new(offset.X, offset.Y, hint.ActualWidth, hint.ActualHeight);
    }

    private static PathGeometry? BuildSilhouette(Layout layout, double inset) =>
        layout.Up.Width == 0 && layout.Right.Width == 0 && layout.Down.Width == 0 && layout.Left.Width == 0 ? null :
            new PathGeometry { FillRule = FillRule.EvenOdd, Figures = { Outline(layout, inset) } };

    private static PathFigure Outline(Layout layout, double inset)
    {
        // Each extension replaces a short arc of the circle. It follows the
        // rounded outside of its hint and meets the wheel tangentially at both ends.
        var radius = Radius - inset;
        var cx = layout.Size.Width / 2;
        var cy = layout.Size.Height / 2;
        var diagonal = radius / Math.Sqrt(2);
        var figure = new PathFigure { StartPoint = new(cx - diagonal, cy - diagonal), IsClosed = true, IsFilled = true };
        Add(layout.Up, TrayDirection.Up);
        Add(layout.Right, TrayDirection.Right);
        Add(layout.Down, TrayDirection.Down);
        Add(layout.Left, TrayDirection.Left);
        return figure;

        void Add(Rect bounds, TrayDirection direction)
        {
            // Local coordinates are clockwise tangent and outward radius.
            Point P(double tangent, double radius) => direction switch
            {
                TrayDirection.Up => new(cx + tangent, cy - radius),
                TrayDirection.Right => new(cx + radius, cy + tangent),
                TrayDirection.Down => new(cx - tangent, cy + radius),
                _ => new(cx - radius, cy - tangent)
            };
            void CircleTo(Point point) => figure.Segments.Add(new ArcSegment
                { Size = new(radius, radius), SweepDirection = SweepDirection.Clockwise, Point = point });
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                CircleTo(P(diagonal, diagonal));
                return;
            }
            var vertical = direction is TrayDirection.Up or TrayDirection.Down;
            var half = (vertical ? bounds.Width : bounds.Height) / 2 + Gutter - inset;
            var outer = Gutter - inset + (direction switch
            {
                TrayDirection.Up => cy - bounds.Top,
                TrayDirection.Right => bounds.Right - cx,
                TrayDirection.Down => bounds.Bottom - cy,
                _ => cx - bounds.Left
            });
            var shoulder = Math.Min(half + 50, diagonal);
            var radial = Math.Sqrt(radius * radius - shoulder * shoulder);
            var handle = (shoulder - half) * .55;
            var tangentRise = handle * shoulder / radial;
            void Curve(Point startControl, Point endControl, Point end) =>
                figure.Segments.Add(new BezierSegment { Point1 = startControl, Point2 = endControl, Point3 = end });

            CircleTo(P(-shoulder, radial));
            if (vertical)
            {
                // A shallow ellipse pinches at its ends as the label gets wider.
                // Give each corner its own turning distance and use a cubic spline
                // so curvature changes continuously from the shoulder across the crown.
                var turn = Math.Min(42, half * .75);
                ReadOnlySpan<Point> guide =
                [
                    P(-shoulder, radial), P(-shoulder + handle, radial + tangentRise),
                    P(-half - 3, outer - turn), P(-half + 2, outer - turn * .40),
                    P(-half + turn * .45, outer - 3), P(0, outer + 1.5),
                    P(half - turn * .45, outer - 3), P(half - 2, outer - turn * .40),
                    P(half + 3, outer - turn), P(shoulder - handle, radial + tangentRise),
                    P(shoulder, radial), P(shoulder + handle, radial - tangentRise)
                ];
                for (var i = 0; i + 2 < guide.Length; i++)
                {
                    // Convert one uniform cubic B-spline span to a native Bezier.
                    var a = guide[i];
                    var b = guide[i + 1];
                    var c = guide[i + 2];
                    Curve(new((2 * a.X + b.X) / 3, (2 * a.Y + b.Y) / 3),
                        new((a.X + 2 * b.X) / 3, (a.Y + 2 * b.Y) / 3),
                        new((a.X + 4 * b.X + c.X) / 6, (a.Y + 4 * b.Y + c.Y) / 6));
                }
                CircleTo(P(diagonal, diagonal));
                return;
            }
            var capDepth = Math.Min(half, 30);
            var capBase = outer - capDepth;
            var sideHandle = (capBase - radial) * .45;
            const double quarterEllipseHandle = .55228475;
            Curve(P(-shoulder + handle, radial + tangentRise), P(-half, capBase - sideHandle), P(-half, capBase));
            Curve(P(-half, capBase + quarterEllipseHandle * capDepth), P(-quarterEllipseHandle * half, outer), P(0, outer));
            Curve(P(quarterEllipseHandle * half, outer), P(half, capBase + quarterEllipseHandle * capDepth), P(half, capBase));
            Curve(P(half, capBase - sideHandle), P(shoulder - handle, radial + tangentRise), P(shoulder, radial));
            CircleTo(P(diagonal, diagonal));
        }
    }
}
