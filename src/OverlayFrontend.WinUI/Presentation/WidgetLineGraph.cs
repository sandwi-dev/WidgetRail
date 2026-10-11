using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;
using PlotPath = Microsoft.UI.Xaml.Shapes.Path;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounded native geometry. Updating samples never replaces the focus tree.</summary>
internal sealed partial class WidgetLineGraph : UserControl
{
    private readonly Grid root = new() { RowSpacing = 4 };
    private readonly TextBlock upper = new() { FontSize = 12 };
    private readonly TextBlock lower = new() { FontSize = 12 };
    private readonly TextBlock empty = new() { Text = "Collecting…", HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid plot = new();
    private readonly PlotPath line = new() { StrokeThickness = 2 };
    private readonly PlotPath grid = new() { StrokeThickness = 1, Opacity = .25 };
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse latest = new() { Width = 5, Height = 5,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private LineGraphDefinition? graph;

    internal WidgetLineGraph()
    {
        IsTabStop = false; IsHitTestVisible = false; Height = 150; MinHeight = 80;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(plot, 1); Grid.SetRow(lower, 2);
        plot.Children.Add(grid); plot.Children.Add(line); plot.Children.Add(latest); plot.Children.Add(empty);
        root.Children.Add(upper); root.Children.Add(plot); root.Children.Add(lower);
        Content = root;
        plot.SizeChanged += (_, _) => Draw();
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => ApplyBrush());
        Loaded += (_, _) => { ApplyBrush(); Draw(); };
    }

    internal void Apply(LineGraphDefinition definition)
    {
        graph = definition;
        var maximum = definition.EffectiveMaximum;
        upper.Text = Label(maximum, definition.Unit);
        lower.Text = Label(definition.Minimum, definition.Unit);
        var values = definition.Points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToArray();
        empty.Visibility = values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetFullDescription(this, values.Length == 0 ? "No samples available" :
            $"Latest {Label(values[^1], definition.Unit)}; minimum {Label(values.Min(), definition.Unit)}; maximum {Label(values.Max(), definition.Unit)}.");
        Draw();
    }

    private void ApplyBrush()
    {
        line.Stroke = Foreground; grid.Stroke = Foreground; latest.Fill = Foreground;
        upper.Foreground = Foreground; lower.Foreground = Foreground; empty.Foreground = Foreground;
    }

    private void Draw()
    {
        if (graph is null || plot.ActualWidth <= 0 || plot.ActualHeight <= 0) return;
        var width = Math.Max(1, plot.ActualWidth - 6); var height = Math.Max(1, plot.ActualHeight - 6);
        var geometry = new PathGeometry();
        PathFigure? segment = null;
        Point? last = null;
        var maximum = graph.EffectiveMaximum;
        foreach (var sample in graph.Points)
        {
            if (sample.Value is not { } value) { segment = null; last = null; continue; }
            var point = new Point(3 + width * (sample.X - graph.XMinimum) / (graph.XMaximum - graph.XMinimum),
                3 + height * (1 - (value - graph.Minimum) / (maximum - graph.Minimum)));
            if (segment is null) { segment = new() { StartPoint = point, IsFilled = false }; geometry.Figures.Add(segment); }
            else segment.Segments.Add(new LineSegment { Point = point });
            last = point;
        }
        line.Data = geometry;
        latest.Visibility = last.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (last is { } end) latest.Margin = new(end.X - 2.5, end.Y - 2.5, 0, 0);
        var guides = new PathGeometry();
        foreach (var fraction in new[] { 0d, .5, 1 })
        {
            var figure = new PathFigure { StartPoint = new(3, 3 + height * fraction), IsFilled = false };
            figure.Segments.Add(new LineSegment { Point = new(3 + width, 3 + height * fraction) });
            guides.Figures.Add(figure);
        }
        grid.Data = guides;
    }

    private static string Label(double value, string unit) => value.ToString("0.#", CultureInfo.CurrentCulture) +
        (string.IsNullOrEmpty(unit) ? "" : " " + unit);
}
