using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private static bool SameGridLayoutMode(ViewNode previous, ViewNode next) =>
        next.Kind != ViewNodeKind.Grid || (previous.GridLayout is null) == (next.GridLayout is null);

    private void UpdateExplicitGrid(Grid grid, ViewNode node, GridLayoutDefinition layout)
    {
        // Declaration tracks map directly to WinUI. Do not manufacture gap tracks
        // or derive tracks from child visibility: authored cells keep their slots.
        // An empty definition collection is WinUI's implicit single star track.
        while (grid.RowDefinitions.Count > layout.Rows.Count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        while (grid.RowDefinitions.Count < layout.Rows.Count) grid.RowDefinitions.Add(new());
        for (var index = 0; index < layout.Rows.Count; ++index)
        {
            var source = layout.Rows[index];
            var target = grid.RowDefinitions[index];
            target.Height = NativeTrack(source);
            target.MinHeight = source.Minimum;
            target.MaxHeight = source.Maximum ?? double.PositiveInfinity;
        }
        while (grid.ColumnDefinitions.Count > layout.Columns.Count) grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
        while (grid.ColumnDefinitions.Count < layout.Columns.Count) grid.ColumnDefinitions.Add(new());
        for (var index = 0; index < layout.Columns.Count; ++index)
        {
            var source = layout.Columns[index];
            var target = grid.ColumnDefinitions[index];
            target.Width = NativeTrack(source);
            target.MinWidth = source.Minimum;
            target.MaxWidth = source.Maximum ?? double.PositiveInfinity;
        }
        grid.RowSpacing = layout.RowSpacing;
        grid.ColumnSpacing = layout.ColumnSpacing;
        foreach (var child in node.Children)
        {
            var target = bindings[child.Id].LayoutElement;
            var cell = child.GridCell;
            Grid.SetRow(target, cell?.Row ?? 0);
            Grid.SetColumn(target, cell?.Column ?? 0);
            Grid.SetRowSpan(target, cell?.RowSpan ?? 1);
            Grid.SetColumnSpan(target, cell?.ColumnSpan ?? 1);
            target.HorizontalAlignment = HorizontalAlignment.Stretch;
            target.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private static GridLength NativeTrack(GridTrackDefinition track) => track.Sizing switch
    {
        GridTrackSizing.Auto => GridLength.Auto,
        GridTrackSizing.Pixel => new(track.Value, GridUnitType.Pixel),
        GridTrackSizing.Star => new(track.Value, GridUnitType.Star),
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };
}
