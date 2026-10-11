using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class LineGraphValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly Button outside = new() { Content = "Focus stays here during graph updates" };
    private readonly TextBlock status = new() { Text = "Graph checks pending", TextWrapping = TextWrapping.Wrap };
    private long sequence;

    internal LineGraphValidationPage(string result)
    {
        Content = new StackPanel { Margin = new(24), Spacing = 12, Children = { status, outside, presenter } };
        Apply(20);
        Loaded += async (_, _) =>
        {
            List<string> checks = []; string? error = null;
            try
            {
                await Task.Delay(250);
                var graph = Find<WidgetLineGraph>(presenter) ?? throw new Exception("No native graph rendered");
                Check(graph.ActualWidth > 100 && graph.ActualHeight >= 80, $"Graph receives responsive native bounds ({graph.ActualWidth:0}×{graph.ActualHeight:0})");
                Check(!graph.IsTabStop && !graph.IsHitTestVisible, "Graph does not capture controller or pointer input");
                Check(graph.ValidationFigureCount == 2, "Missing sample separates the two line segments");
                Check(AutomationProperties.GetName(graph) == "CPU history", "Graph has its author-provided accessible name");
                outside.Focus(FocusState.Keyboard);
                Apply(30);
                await Task.Delay(50);
                Check(ReferenceEquals(graph, Find<WidgetLineGraph>(presenter)), "Live update retains the native graph instance");
                Check(outside.FocusState != FocusState.Unfocused, "Live update preserves external focus");
                Check(AutomationProperties.GetFullDescription(graph).Contains("30"), "Accessible value updates with samples");
                var oldWidth = graph.ActualWidth;
                presenter.Width = oldWidth / 2;
                await Task.Delay(80);
                Check(graph.ActualWidth < oldWidth && graph.ValidationGeometryFits, "Resizing recomputes bounded geometry");
                graph.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
                Check(graph.ValidationBrushMatches, "Plot and labels follow the control's theme/style brush");
                Apply(null);
                await Task.Delay(50);
                Check(AutomationProperties.GetFullDescription(graph) == "No samples available" && graph.ValidationFigureCount == 0,
                    "Unavailable history clears old lines and exposes its empty state");
                status.Text = $"PASS: {checks.Count} native graph checks";
            }
            catch (Exception failure) { error = failure.ToString(); status.Text = "FAIL: " + error; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result))!);
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));

            void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks.Add(name); }
        };
    }

    private void Apply(double? value)
    {
        var snapshot = new ViewSnapshot { WidgetInstanceId = "graph.instance", Sequence = ++sequence, ActiveInputScopeId = "graphs",
            Root = new() { Id = "graphs", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[new() { Id = "history", Kind = ViewNodeKind.LineGraph,
                AccessibilityLabel = "CPU history", Graph = new((GraphPoint[])[new(-60, value), new(-50, value), new(-30, null), new(-10, value), new(0, value)], -60, 0, 0, 100, "%") }] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "graph", Name = "Graph", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
