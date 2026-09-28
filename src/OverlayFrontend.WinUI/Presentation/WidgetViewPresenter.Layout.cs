using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private BridgeComputedStyleValue? ComputedStyle(ViewNode node, string property) =>
        frame?.RenderStyles.GetValueOrDefault(node.Id)?.Base.GetValueOrDefault(property);

    private double? Length(ViewNode node, string property) => ComputedStyle(node, property) is { Number: { } number, Unit: "px" or null }
        && double.IsFinite(number) && number >= 0 ? number : null;

    private void ApplySizeAndTypography(FrameworkElement element, ViewNode node)
    {
        element.Width = Length(node, "width") ?? double.NaN;
        element.Height = Length(node, "height") ?? double.NaN;
        element.MinWidth = Length(node, "min-width") ?? 0;
        element.MinHeight = Length(node, "min-height") ?? 0;
        element.MaxWidth = Length(node, "max-width") ?? double.PositiveInfinity;
        element.MaxHeight = Length(node, "max-height") ?? double.PositiveInfinity;
        if (element is TextBlock text)
        {
            text.MaxLines = (int)(ComputedStyle(node, "max-lines")?.Number ?? 0);
            text.TextTrimming = ComputedStyle(node, "text-overflow")?.Text == "ellipsis" ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        }
    }

    private void UpdateLayout(Grid grid, ViewNode node)
    {
        var horizontal = node.Kind == ViewNodeKind.Row;
        var count = node.Children.Count;
        if (horizontal)
        {
            grid.RowDefinitions.Clear();
            while (grid.ColumnDefinitions.Count > count) grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
            while (grid.ColumnDefinitions.Count < count) grid.ColumnDefinitions.Add(new());
        }
        else
        {
            grid.ColumnDefinitions.Clear();
            while (grid.RowDefinitions.Count > count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            while (grid.RowDefinitions.Count < count) grid.RowDefinitions.Add(new());
        }
        grid.RowSpacing = horizontal ? 0 : Length(node, "gap") ?? 12;
        grid.ColumnSpacing = horizontal ? Length(node, "gap") ?? 12 : 0;
        for (var index = 0; index < count; ++index)
        {
            var child = node.Children[index];
            var grow = ComputedStyle(child, "flex-grow")?.Number ?? (NeedsConstrainedViewport(child) && Length(child, horizontal ? "width" : "height") is null ? 1 : 0);
            var length = grow > 0 ? new GridLength(grow, GridUnitType.Star) : GridLength.Auto;
            if (horizontal) grid.ColumnDefinitions[index].Width = length;
            else grid.RowDefinitions[index].Height = length;
            Grid.SetRow(bindings[child.Id].Element, horizontal ? 0 : index);
            Grid.SetColumn(bindings[child.Id].Element, horizontal ? index : 0);
        }
    }
    private static bool NeedsConstrainedViewport(ViewNode node) => node.Kind is ViewNodeKind.IndexedCollection or ViewNodeKind.Scroll or ViewNodeKind.ModalLayer || node.Children.Any(NeedsConstrainedViewport);
}
