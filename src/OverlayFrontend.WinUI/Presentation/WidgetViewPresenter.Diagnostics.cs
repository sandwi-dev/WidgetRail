using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    /// <summary>Explicit developer probe only. Excludes user text, artwork identifiers/bytes, and actions.</summary>
    internal void WriteLayoutDiagnostics(string path)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Capture native layout on its dispatcher.");
        var properties = new HashSet<string>(StringComparer.Ordinal) { "width", "height", "min-width", "min-height", "max-width", "max-height",
            "flex-grow", "flex-shrink", "flex-basis", "align", "justify", "direction", "gap", "padding", "margin", "aspect-ratio", "overflow" };
        var nodes = bindings.Values.Select(binding =>
        {
            var element = binding.Element;
            var node = declarations[binding.Identity.Id];
            var origin = element.TransformToVisual(this).TransformPoint(new(0, 0));
            var image = element as Image;
            var scroll = element as ScrollViewer;
            var indexed = (element as WidgetIndexedCollectionView)?.NativeView;
            var itemsPanel = indexed?.ItemsPanelRoot as ItemsWrapGrid;
            var itemsScroll = indexed is null ? null : FindScroll(indexed);
            return new JsonObject
            {
                ["id"] = binding.Identity.Id, ["parent"] = node.ParentId, ["kind"] = node.Node.Kind.ToString(), ["nativeType"] = element.GetType().Name,
                ["visible"] = element.Visibility.ToString(), ["IsLoaded"] = element.IsLoaded,
                ["x"] = Number(origin.X), ["y"] = Number(origin.Y), ["Width"] = Number(element.Width), ["Height"] = Number(element.Height),
                ["ActualWidth"] = Number(element.ActualWidth), ["ActualHeight"] = Number(element.ActualHeight),
                ["MinWidth"] = Number(element.MinWidth), ["MinHeight"] = Number(element.MinHeight),
                ["MaxWidth"] = Number(element.MaxWidth), ["MaxHeight"] = Number(element.MaxHeight),
                ["desired"] = new JsonObject { ["Width"] = Number(element.DesiredSize.Width), ["Height"] = Number(element.DesiredSize.Height) },
                ["layoutSlot"] = Rectangle(LayoutInformation.GetLayoutSlot(element)), ["margin"] = ThicknessNode(element.Margin),
                ["horizontalAlignment"] = element.HorizontalAlignment.ToString(), ["verticalAlignment"] = element.VerticalAlignment.ToString(),
                ["row"] = Grid.GetRow(element), ["column"] = Grid.GetColumn(element),
                ["rows"] = Strings((element as Grid)?.RowDefinitions.Select(value => value.Height.ToString())),
                ["columns"] = Strings((element as Grid)?.ColumnDefinitions.Select(value => value.Width.ToString())),
                ["image"] = image is null ? null : new JsonObject { ["hasSource"] = image.Source is not null,
                    ["decodedWidth"] = (image.Source as BitmapSource)?.PixelWidth, ["decodedHeight"] = (image.Source as BitmapSource)?.PixelHeight,
                    ["fit"] = image.Stretch.ToString() },
                ["scroll"] = scroll is null ? null : new JsonObject { ["ViewportWidth"] = Number(scroll.ViewportWidth),
                    ["ViewportHeight"] = Number(scroll.ViewportHeight), ["ExtentWidth"] = Number(scroll.ExtentWidth), ["ExtentHeight"] = Number(scroll.ExtentHeight),
                    ["HorizontalOffset"] = Number(scroll.HorizontalOffset), ["VerticalOffset"] = Number(scroll.VerticalOffset) },
                ["indexed"] = indexed is null ? null : new JsonObject { ["ActualWidth"] = Number(indexed.ActualWidth), ["Padding"] = ThicknessNode(indexed.Padding),
                    ["viewport"] = Number(itemsScroll?.ViewportWidth), ["panelWidth"] = Number(itemsPanel?.ActualWidth),
                    ["itemWidth"] = Number(itemsPanel?.ItemWidth), ["columns"] = itemsPanel?.MaximumRowsOrColumns,
                    ["realized"] = new JsonArray(Enumerable.Range(0, Math.Min(indexed.Items.Count, 10)).Select(index =>
                        indexed.ContainerFromIndex(index) is FrameworkElement item ? new JsonObject { ["index"] = index,
                            ["ActualWidth"] = Number(item.ActualWidth), ["Margin"] = ThicknessNode(item.Margin), ["slot"] = Rectangle(LayoutInformation.GetLayoutSlot(item)) } : null).ToArray()) },
                ["style"] = presentation?.RenderStyles.GetValueOrDefault(binding.Identity.Id)?.Base is { } style
                    ? new JsonObject(style.Where(pair => properties.Contains(pair.Key)).Select(pair =>
                        new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value.Text)))) : null,
            };
        }).ToArray();
        var snapshot = new JsonObject
        {
            ["sequence"] = frame?.Authority.SnapshotSequence, ["ActualWidth"] = Number(ActualWidth), ["ActualHeight"] = Number(ActualHeight),
            ["motion"] = new JsonObject { ["starts"] = transitionStarts, ["targets"] = LastTransitionTargetCount,
                ["outgoing"] = OutgoingTransitionCount, ["outcome"] = lastTransitionOutcome?.ToString() },
            ["nodes"] = new JsonArray(nodes),
        };
        File.WriteAllText(path, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        static JsonNode? Number(double? value) => value is null ? null : double.IsFinite(value.Value)
            ? JsonValue.Create(value.Value) : JsonValue.Create(value.Value.ToString(CultureInfo.InvariantCulture));
        static JsonArray? Strings(IEnumerable<string>? values) => values is null ? null : new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        static JsonObject ThicknessNode(Thickness value) => new()
        { ["Left"] = Number(value.Left), ["Top"] = Number(value.Top), ["Right"] = Number(value.Right), ["Bottom"] = Number(value.Bottom) };
        static JsonObject Rectangle(Windows.Foundation.Rect value) => new()
        {
            ["X"] = Number(value.X), ["Y"] = Number(value.Y), ["Width"] = Number(value.Width), ["Height"] = Number(value.Height),
            ["Left"] = Number(value.Left), ["Top"] = Number(value.Top), ["Right"] = Number(value.Right), ["Bottom"] = Number(value.Bottom), ["IsEmpty"] = value.IsEmpty,
        };
    }
}
