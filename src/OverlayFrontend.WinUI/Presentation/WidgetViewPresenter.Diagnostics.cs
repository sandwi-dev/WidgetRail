using System.Text.Json;
using System.Text.Json.Serialization;
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
            return new
            {
                id = binding.Identity.Id, parent = node.ParentId, kind = node.Node.Kind.ToString(), nativeType = element.GetType().Name,
                visible = element.Visibility.ToString(), element.IsLoaded,
                x = origin.X, y = origin.Y, element.Width, element.Height, element.ActualWidth, element.ActualHeight,
                element.MinWidth, element.MinHeight, element.MaxWidth, element.MaxHeight,
                desired = new { element.DesiredSize.Width, element.DesiredSize.Height },
                layoutSlot = LayoutInformation.GetLayoutSlot(element), margin = element.Margin,
                horizontalAlignment = element.HorizontalAlignment.ToString(), verticalAlignment = element.VerticalAlignment.ToString(),
                row = Grid.GetRow(element), column = Grid.GetColumn(element),
                rows = (element as Grid)?.RowDefinitions.Select(value => value.Height.ToString()).ToArray(),
                columns = (element as Grid)?.ColumnDefinitions.Select(value => value.Width.ToString()).ToArray(),
                image = image is null ? null : new { hasSource = image.Source is not null, decodedWidth = (image.Source as BitmapSource)?.PixelWidth,
                    decodedHeight = (image.Source as BitmapSource)?.PixelHeight, fit = image.Stretch.ToString() },
                scroll = scroll is null ? null : new { scroll.ViewportWidth, scroll.ViewportHeight, scroll.ExtentWidth, scroll.ExtentHeight,
                    scroll.HorizontalOffset, scroll.VerticalOffset },
                indexed = indexed is null ? null : new { indexed.ActualWidth, indexed.Padding,
                    viewport = itemsScroll?.ViewportWidth, panelWidth = itemsPanel?.ActualWidth,
                    itemWidth = itemsPanel?.ItemWidth, columns = itemsPanel?.MaximumRowsOrColumns,
                    realized = Enumerable.Range(0, Math.Min(indexed.Items.Count, 10)).Select(index =>
                        indexed.ContainerFromIndex(index) is FrameworkElement item ? new { index,
                            item.ActualWidth, item.Margin, slot = LayoutInformation.GetLayoutSlot(item) } : null).ToArray() },
                style = presentation?.RenderStyles.GetValueOrDefault(binding.Identity.Id)?.Base.Where(pair => properties.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Text),
            };
        }).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new { sequence = frame?.Authority.SnapshotSequence, ActualWidth, ActualHeight,
            motion = new { starts = transitionStarts, targets = LastTransitionTargetCount, outgoing = OutgoingTransitionCount,
                outcome = lastTransitionOutcome?.ToString() }, nodes },
            new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
}
