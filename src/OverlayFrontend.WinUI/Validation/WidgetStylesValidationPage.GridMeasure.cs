using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeGridMeasureAsync()
    {
        presenter.Width = 1050.4;
        var child = new ViewNode { Id = "fill", Kind = ViewNodeKind.Button, Text = "Fill", ActionId = "fill" };
        presenter.Apply(CreateFrame(new() { Id = "aligned", Kind = ViewNodeKind.Stack, Children = [child] },
            new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["aligned"] = Compute("#aligned { align: center; }", "aligned", "stack"),
                ["fill"] = Compute("#fill { width: 100%; }", "fill", "button"),
            }));
        await Wait(() => Find<Button>("Widget.fill")?.ActualWidth > 1000);
        Check(Find<Button>("Widget.fill")!.HorizontalAlignment == HorizontalAlignment.Stretch,
            "explicit full-width composition fills native cross-axis space despite a centered parent");
        // At 125% DPI this leaves 1305 physical pixels for six cells. Rounding
        // each 174-DIP cell up independently wraps into five columns.
        var grid = new GridView { Width = 1052, Height = 300, Padding = new Thickness(4),
            ItemsSource = Enumerable.Range(0, 178).Select(index => "Item " + index).ToArray(),
            ItemsPanel = (ItemsPanelTemplate)Application.Current.Resources["WidgetIndexedGridPanel"] };
        ScrollViewer.SetHorizontalScrollMode(grid, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        grid.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer is SelectorItem container)
                WidgetIndexedCollectionView.ConfigureContainerLayout(container, ScrollAxis.Vertical, 225);
        };
        grid.SizeChanged += (_, _) => WidgetIndexedCollectionView.UpdateGridWidth(grid, 160, 6);
        grid.Loaded += (_, _) => WidgetIndexedCollectionView.UpdateGridWidth(grid, 160, 6);
        host.Children.Add(grid);
        try
        {
            await Wait(() => grid.ItemsPanelRoot is ItemsWrapGrid { ItemWidth: > 0 });
            var panel = (ItemsWrapGrid)grid.ItemsPanelRoot;
            var scroll = FindGridScroll(grid)!;
            await Task.Delay(120);
            Check(Math.Abs(grid.ActualWidth - 1052) < 1 && panel.MaximumRowsOrColumns == 6 &&
                panel.ItemWidth * 6 <= scroll.ViewportWidth - grid.Padding.Left - grid.Padding.Right + 1,
                "native grid sizing uses viewport minus authored padding and respects its six-column bound");
            await Wait(() => grid.ContainerFromIndex(6) is FrameworkElement);
            var zero = (GridViewItem)grid.ContainerFromIndex(0);
            var six = (GridViewItem)grid.ContainerFromIndex(6);
            var y = zero.TransformToVisual(grid).TransformPoint(new(0, 0)).Y;
            Check(Enumerable.Range(0, 6).All(index => grid.ContainerFromIndex(index) is FrameworkElement item &&
                    Math.Abs(item.TransformToVisual(grid).TransformPoint(new(0, 0)).Y - y) < 1) &&
                six.TransformToVisual(grid).TransformPoint(new(0, 0)).Y > y + 100,
                "six actual native item containers occupy row one and item six starts row two");
            zero.Focus(FocusState.Keyboard);
            FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions { SearchRoot = grid });
            await Wait(() => six.FocusState != FocusState.Unfocused);
            Check(six.FocusState == FocusState.Keyboard && panel.MaximumRowsOrColumns == grid.IndexFromContainer(six),
                "computed controller Down target matches the next native visual row");
            var original = (grid.ActualWidth, panel.ItemWidth, scroll.ExtentWidth);
            for (var iteration = 0; iteration < 15; ++iteration)
            { WidgetIndexedCollectionView.UpdateGridWidth(grid, 160, 6); await Task.Delay(10); }
            Check(original == (grid.ActualWidth, panel.ItemWidth, scroll.ExtentWidth),
                "repeated sizing does not ratchet GridView width through a layout feedback loop");
            grid.Width = 600;
            await Wait(() => panel.MaximumRowsOrColumns == 3);
            Check(panel.ItemWidth * 3 <= scroll.ViewportWidth - grid.Padding.Left - grid.Padding.Right + 1,
                "native grid resize recomputes layout and navigation column count from the same viewport");
        }
        finally { host.Children.Remove(grid); }
        presenter.Width = double.NaN;

        static ScrollViewer? FindGridScroll(DependencyObject element)
        {
            if (element is ScrollViewer scroll) return scroll;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); ++i)
                if (FindGridScroll(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
            return null;
        }
    }
}
