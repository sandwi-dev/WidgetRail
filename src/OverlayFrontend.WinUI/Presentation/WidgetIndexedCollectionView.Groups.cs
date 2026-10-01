using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private CollectionViewSource? groupedSource;
    private NativeGroupedRangeView? groupedView;
    private ObservableCollection<WidgetIndexedGroup>? groups;
    private WidgetRail.WidgetBridge.BridgeNodeRenderStyles? groupHeaderStyles;

    private void DetachItems()
    {
        measurementRetention?.Dispose(); measurementRetention = null;
        if (view is not null) view.ItemsSource = null;
        groupedView?.Dispose();
        groupedView = null;
        if (groupedSource is not null) groupedSource.Source = null;
        groupedSource = null;
        groups = null;
        groupHeaderStyles = null;
    }

    private void ApplyGroups(IReadOnlyList<IndexedCollectionGroup>? declarations)
    {
        if (source is null || view is null) return;
        if (declarations is null)
        {
            if (groupedView is not null) DetachItems();
            if (view.GroupStyle.Count != 0) view.GroupStyle.Clear();
            if (!ReferenceEquals(view.ItemsSource, source.Items)) view.ItemsSource = source.Items;
            return;
        }
        if (groups is null || groups.Count != declarations.Count ||
            groups.Where((group, index) => group.Key != declarations[index].Key || group.Count != declarations[index].Count).Any())
        {
            CancelNavigation();
            DetachItems();
            groups = [];
            var offset = 0;
            foreach (var declaration in declarations)
            {
                groups.Add(new(declaration.Key, declaration.Header, offset, declaration.Count, source.Items));
                offset = checked(offset + declaration.Count);
            }
            if (offset != source.Items.Count) throw new InvalidDataException("Groups do not partition the indexed query.");
            groupedSource = new() { IsSourceGrouped = true, Source = groups };
            groupedView = new(groupedSource.View, source.Items);
        }
        else
            for (var index = 0; index < groups.Count; ++index) groups[index].Header = declarations[index].Header;
        RefreshGroupHeaderStyles();
        if (view.GroupStyle.Count == 0)
            view.GroupStyle.Add(new GroupStyle
            {
                HidesIfEmpty = true,
                HeaderTemplate = (DataTemplate)Application.Current.Resources["WidgetIndexedGroupHeaderTemplate"],
                HeaderContainerStyle = (Style)Application.Current.Resources[view is GridView
                    ? "WidgetIndexedGridHeaderContainer" : "WidgetIndexedListHeaderContainer"],
            });
        if (!ReferenceEquals(view.ItemsSource, groupedView)) view.ItemsSource = groupedView;
    }

    private void RefreshGroupHeaderStyles()
    {
        if (groups is null || source is null) return;
        var header = source.Presentation.RenderStyles.GetValueOrDefault(source.Declaration.Id)?.GroupHeader;
        if (!ReferenceEquals(header, groupHeaderStyles?.Base))
            groupHeaderStyles = header is null ? null : new() { Base = header, Focused = header, Pressed = header };
        foreach (var group in groups) group.HeaderStyle = groupHeaderStyles;
    }

    private int GroupIndex(int item)
    {
        if (groups is null) return -1;
        var low = 0;
        var high = groups.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (groups[middle].StartIndex <= item) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private int? GroupedTarget(int current, Microsoft.UI.Xaml.Input.FocusNavigationDirection direction, int columns)
    {
        var groupIndex = GroupIndex(current);
        if (groups is null || groupIndex < 0) return null;
        var group = groups[groupIndex];
        var local = current - group.StartIndex;
        var column = local % columns;
        switch (direction)
        {
            case Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left:
                return column > 0 ? current - 1 : null;
            case Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right:
                return column < columns - 1 && local + 1 < group.Count ? current + 1 : null;
            case Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down:
                if (local + columns < group.Count) return current + columns;
                if (local / columns < (group.Count - 1) / columns) return group.StartIndex + group.Count - 1;
                for (var next = groupIndex + 1; next < groups.Count; ++next)
                    if (groups[next].Count > 0) return groups[next].StartIndex + Math.Min(column, groups[next].Count - 1);
                return null;
            case Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up:
                if (local >= columns) return current - columns;
                for (var previous = groupIndex - 1; previous >= 0; --previous)
                    if (groups[previous].Count > 0)
                    {
                        var last = groups[previous].Count - 1;
                        return groups[previous].StartIndex + last / columns * columns + Math.Min(column, last % columns);
                    }
                return null;
            default: return null;
        }
    }
}
