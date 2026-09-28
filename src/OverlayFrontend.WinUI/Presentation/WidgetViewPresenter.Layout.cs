using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private BridgeComputedStyleValue? ComputedStyle(ViewNode node, string property) =>
        frame?.RenderStyles.GetValueOrDefault(node.Id)?.Base.GetValueOrDefault(property);

    private double? Length(ViewNode node, string property)
    {
        if (ComputedStyle(node, property) is not { Number: { } number } value || !double.IsFinite(number) || number < 0) return null;
        var viewport = Viewport();
        return value.Unit switch { "px" or null => number, "vw" => viewport.Width * number / 100,
            "vh" => viewport.Height * number / 100, _ => null };
    }

    private void ApplySizeAndTypography(FrameworkElement element, ViewNode node)
    {
        element.Width = Length(node, "width") ?? double.NaN;
        element.Height = Length(node, "height") ?? double.NaN;
        element.MinWidth = Length(node, "min-width") ?? 0;
        element.MinHeight = Length(node, "min-height") ?? 0;
        element.MaxWidth = Length(node, "max-width") ?? double.PositiveInfinity;
        element.MaxHeight = Length(node, "max-height") ?? double.PositiveInfinity;
        element.Margin = NativeComputedStyleAdapter.Spacing(frame?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "margin") ?? new Thickness(0);
        if (element is TextBlock text)
        {
            text.MaxLines = (int)(ComputedStyle(node, "max-lines")?.Number ?? 0);
            text.TextTrimming = ComputedStyle(node, "text-overflow")?.Text == "ellipsis" ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        }
    }

    private void UpdateLayout(Grid grid, ViewNode node)
    {
        var horizontal = node.Kind == ViewNodeKind.Row;
        var children = node.Children.Where(child => bindings[child.Id].Element.Visibility == Visibility.Visible).ToArray();
        if (grid is WidgetResponsiveGrid responsive)
        {
            var spacing = NativeComputedStyleAdapter.Spacing(frame?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
            responsive.RowSpacing = spacing?.Top ?? 12;
            responsive.ColumnSpacing = spacing?.Right ?? 12;
            responsive.Configure(children.Select(child => bindings[child.Id].Element).ToArray(),
                node.GridMinimumColumnWidth ?? 160, node.GridMaximumColumns ?? 12);
            return;
        }
        var gap = NativeComputedStyleAdapter.Spacing(frame?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
        var spacingSize = (horizontal ? gap?.Right : gap?.Top) ?? 12;
        var grows = children.Select(child => ComputedStyle(child, "flex-grow")?.Number ??
            (NeedsConstrainedViewport(child) && Length(child, horizontal ? "width" : "height") is null ? 1 : 0)).ToArray();
        var justify = grows.Any(grow => grow > 0) ? "start" : ComputedStyle(node, "justify")?.Text;
        var tracks = new List<(GridLength Length, double Minimum)>();
        var slots = new int[children.Length];
        if (children.Length > 0 && justify is "center" or "end") tracks.Add((new(1, GridUnitType.Star), 0));
        for (var index = 0; index < children.Length; ++index)
        {
            if (index > 0) tracks.Add((justify == "space-between" ? new(1, GridUnitType.Star) : new(spacingSize), spacingSize));
            slots[index] = tracks.Count;
            tracks.Add((grows[index] > 0 ? new(grows[index], GridUnitType.Star) : GridLength.Auto, 0));
        }
        if (children.Length > 0 && justify == "center") tracks.Add((new(1, GridUnitType.Star), 0));
        // Explicit gap tracks avoid spacing around invisible children and at the
        // ends of centered groups. Native Grid still owns every measurement.
        grid.RowSpacing = grid.ColumnSpacing = 0;
        if (horizontal)
        {
            grid.RowDefinitions.Clear();
            while (grid.ColumnDefinitions.Count > tracks.Count) grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
            while (grid.ColumnDefinitions.Count < tracks.Count) grid.ColumnDefinitions.Add(new());
            for (var index = 0; index < tracks.Count; ++index)
            { grid.ColumnDefinitions[index].Width = tracks[index].Length; grid.ColumnDefinitions[index].MinWidth = tracks[index].Minimum; }
        }
        else
        {
            grid.ColumnDefinitions.Clear();
            while (grid.RowDefinitions.Count > tracks.Count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            while (grid.RowDefinitions.Count < tracks.Count) grid.RowDefinitions.Add(new());
            for (var index = 0; index < tracks.Count; ++index)
            { grid.RowDefinitions[index].Height = tracks[index].Length; grid.RowDefinitions[index].MinHeight = tracks[index].Minimum; }
        }
        for (var index = 0; index < children.Length; ++index)
        {
            var child = children[index];
            Grid.SetRow(bindings[child.Id].Element, horizontal ? 0 : slots[index]);
            Grid.SetColumn(bindings[child.Id].Element, horizontal ? slots[index] : 0);
            var element = bindings[child.Id].Element;
            var alignment = ComputedStyle(node, "align")?.Text;
            if (horizontal)
                element.VerticalAlignment = alignment switch { "start" => VerticalAlignment.Top, "center" => VerticalAlignment.Center,
                    "end" => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch };
            else
                element.HorizontalAlignment = alignment switch { "start" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center,
                    "end" => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
        }
    }
    private static bool NeedsConstrainedViewport(ViewNode node) => node.Kind is ViewNodeKind.IndexedCollection or ViewNodeKind.Scroll or ViewNodeKind.ModalLayer || node.Children.Any(NeedsConstrainedViewport);
}
