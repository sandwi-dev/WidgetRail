using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PlayniteRailLoadingAsync(JsonElement fixture, ViewSnapshot snapshot,
        ViewNode stage, ViewNode collection, ViewNode poster,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles> rowStyles)
    {
        var ordinary = new WidgetSpacer { Width = 60 };
        ordinary.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Check(ordinary.DesiredSize.Width == 60 && ordinary.DesiredSize.Height == 0,
            "ordinary Spacer keeps its prior empty-height measurement");
        ordinary.SetAspectRatio(2d / 3);
        ordinary.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Check(ordinary.DesiredSize.Width == 60 && ordinary.DesiredSize.Height == 90,
            "aspect-ratio Spacer derives height from the authored width");
        ordinary.Height = 30;
        ordinary.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Check(ordinary.DesiredSize.Height == 30, "an explicit spacer height still bounds ratio content");
        ordinary.Height = double.NaN;
        ordinary.SetAspectRatio(null);
        ordinary.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Check(ordinary.DesiredSize.Height == 0, "removing the ratio releases reserved poster geometry");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var styles = fixture.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
        foreach (var pair in rowStyles) styles.TryAdd(pair.Key, pair.Value);
        var descriptor = new BridgeWidgetDescriptor { Id = "playnite.rail-loading", Name = "Playnite loading geometry",
            InstanceId = snapshot.WidgetInstanceId, RuntimeGeneration = "fixture", PresentationGeneration = "fixture",
            Icon = WidgetGlyph.Play, PackageContentDigest = string.Empty };
        var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
            1, descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles);
        var fragment = new WidgetViewPresenter(presentationOnly: true) { VerticalAlignment = VerticalAlignment.Bottom };
        var viewport = new WidgetViewPresenter { Width = 800, Content = fragment };
        var measurements = new List<object>();
        host.Children.Add(viewport);
        try
        {
            foreach (var height in new[] { 360d, 470d, 680d })
            {
                viewport.Height = height;
                await Wait(() => Math.Abs(viewport.ActualHeight - height) < 1);
                // The exact SDK-exported reservation is tested against the exact
                // acquired poster in its real native item-container style path.
                // Only range availability is simulated; no live Bridge is used.
                Publish([]);
                await Wait(() => fragment.ActualHeight > 0 && FindSpacer() is { ActualWidth: > 0, ActualHeight: > 0 });
                fragment.UpdateLayout();
                var reservedHeight = fragment.ActualHeight;
                var reservedTop = fragment.TransformToVisual(viewport).TransformPoint(new()).Y;
                Capture("cold", height);
                var spacer = FindSpacer()!;
                var expectedWidth = Math.Clamp(height * .28, 112, 150);
                Check(Math.Abs(spacer.ActualWidth - expectedWidth) < 1 &&
                    !spacer.IsHitTestVisible && AutomationProperties.GetAccessibilityView(spacer) == AccessibilityView.Raw,
                    $"{height}-DIP Home viewport reserves its responsive poster size without an input or accessibility target");
                var nativeBody = ChildrenOf(fragment).OfType<Grid>().Single(element => AutomationProperties.GetAutomationId(element) == "Widget." + collection.Id);
                var rowPresenter = new WidgetViewPresenter(presentationOnly: true);
                rowPresenter.UseIndexedContainerStyles();
                rowPresenter.ApplyFragment(frame, poster, snapshot.ActiveInputScopeId);
                var container = new ListViewItem { Content = rowPresenter };
                WidgetIndexedCollectionView.ConfigureContainerLayout(container, ScrollAxis.Horizontal, 0);
                nativeBody.Children.Add(container);
                using var containerStyle = new NativeComputedStyleAdapter(container) { StretchComposedContent = true };
                containerStyle.Update(rowStyles.GetValueOrDefault(poster.Id));
                try
                {
                    await Wait(() => container.ActualWidth > 0 && rowPresenter.ActualHeight > 0);
                    fragment.UpdateLayout();
                    Capture("populated", height);
                    Check(Math.Abs(fragment.ActualHeight - reservedHeight) < 1 &&
                        Math.Abs(fragment.TransformToVisual(viewport).TransformPoint(new()).Y - reservedTop) < 1,
                        $"{height}-DIP Home rail keeps its height and bottom-aligned position when a poster arrives " +
                        $"(height {reservedHeight:F3} → {fragment.ActualHeight:F3}; top {reservedTop:F3} → {fragment.TransformToVisual(viewport).TransformPoint(new()).Y:F3})");
                }
                finally { nativeBody.Children.Remove(container); await rowPresenter.DisposeAsync(); }
                await Task.Delay(40);
                fragment.UpdateLayout();
                Check(Math.Abs(fragment.ActualHeight - reservedHeight) < 1,
                    $"{height}-DIP Home rail does not collapse during a new range request");
            }
        }
        finally { host.Children.Remove(viewport); await fragment.DisposeAsync(); await viewport.DisposeAsync(); }

        WidgetSpacer? FindSpacer() => ChildrenOf(fragment).OfType<WidgetSpacer>().FirstOrDefault();
        void Publish(IReadOnlyList<ViewNode> children)
        {
            var body = collection with { Kind = ViewNodeKind.Row, IndexedCollection = null, IndexedGroups = null,
                IndexedOccurrenceKeys = null, CollectionLayout = null, ScrollAxis = null, Children = children };
            fragment.ApplyFragment(frame, stage with { Children = stage.Children.Select(child => child.Id == collection.Id ? body : child).ToArray() },
                snapshot.ActiveInputScopeId);
        }

        void Capture(string phase, double height)
        {
            measurements.Add(new { phase, height, scale = XamlRoot.RasterizationScale,
                actual = fragment.ActualHeight,
                nodes = ChildrenOf(fragment).OfType<FrameworkElement>().Select(element => new {
                    type = element.GetType().Name, id = AutomationProperties.GetAutomationId(element),
                    width = element.ActualWidth, height = element.ActualHeight,
                    desiredWidth = element.DesiredSize.Width, desiredHeight = element.DesiredSize.Height,
                    margin = element.Margin.ToString() }).ToArray() });
            if (App.ValidationFixturePath is { } fixturePath)
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "Native-Home-Rail-Loading.measurements.json"),
                    JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        }

        static IEnumerable<DependencyObject> ChildrenOf(DependencyObject node)
        {
            yield return node;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                foreach (var child in ChildrenOf(VisualTreeHelper.GetChild(node, index))) yield return child;
        }
    }
}
