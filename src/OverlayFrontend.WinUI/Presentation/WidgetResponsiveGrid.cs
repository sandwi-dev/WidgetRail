using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounded static SDK grids use native star columns; indexed collections remain virtualized controls.</summary>
internal sealed class WidgetResponsiveGrid : Grid
{
    private FrameworkElement[] items = [];
    private double minimum = 160;
    private int maximum = 12;
    internal WidgetResponsiveGrid() => SizeChanged += (_, _) => Reflow();
    internal void Configure(FrameworkElement[] children, double minimumColumnWidth, int maximumColumns)
    { items = children; minimum = minimumColumnWidth; maximum = maximumColumns; Reflow(); }
    private void Reflow()
    {
        var width = Math.Max(0, ActualWidth - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right);
        var columns = Math.Clamp((int)Math.Floor((width + ColumnSpacing) / (minimum + ColumnSpacing)), 1, maximum);
        columns = Math.Min(columns, Math.Max(1, items.Length));
        var rows = (items.Length + columns - 1) / columns;
        while (ColumnDefinitions.Count > columns) ColumnDefinitions.RemoveAt(ColumnDefinitions.Count - 1);
        while (ColumnDefinitions.Count < columns) ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        while (RowDefinitions.Count > rows) RowDefinitions.RemoveAt(RowDefinitions.Count - 1);
        while (RowDefinitions.Count < rows) RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < items.Length; ++index)
        { SetRow(items[index], index / columns); SetColumn(items[index], index % columns); }
    }
}
