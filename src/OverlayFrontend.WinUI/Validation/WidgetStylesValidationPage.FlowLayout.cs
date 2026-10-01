using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task FlowSpacingAndJustificationAsync()
    {
        presenter.Width = 400; presenter.Height = 240;
        var first = new ViewNode { Id = "flow.first", Kind = ViewNodeKind.Button, Text = "A", ActionId = "a" };
        var last = first with { Id = "flow.last", Text = "B", ActionId = "b" };
        var hidden = first with { Id = "flow.hidden", VisibleWhen = ResponsiveVisibility.ExpandedOnly };
        foreach (var horizontal in new[] { true, false })
        {
            var root = new ViewNode { Id = "flow", Kind = ViewNodeKind.Row, Children = new[] { first, hidden, last } };
            var styles = root.Children.ToDictionary(node => node.Id, node => Compute(
                "button { width: 40px; height: 40px; min-width: 0px; min-height: 0px; padding: 0px; }", node.Id, "button"));
            styles[root.Id] = FlowStyle(20, "space-around");
            var frame = CreateFrame(root, styles);
            presenter.Apply(frame);
            await Wait(() => Find<Button>("Widget.flow.last")?.ActualWidth == 40);
            presenter.UpdateLayout();
            var grid = Find<Grid>("Widget.flow")!;
            var a = Find<Button>("Widget.flow.first")!;
            var b = Find<Button>("Widget.flow.last")!;
            Check(Find<Button>("Widget.flow.hidden")!.Visibility == Visibility.Collapsed,
                $"flow {horizontal}: responsive child is excluded from distribution");
            AssertAround(20);
            a.Focus(FocusState.Keyboard);
            var changedStyles = new Dictionary<string, BridgeNodeRenderStyles>(styles) { [root.Id] = FlowStyle(40, "space-around") };
            presenter.Apply(frame with { RenderStyles = changedStyles, AppearanceRevision = frame.AppearanceRevision + 1 });
            presenter.UpdateLayout();
            AssertAround(40);
            Check(ReferenceEquals(a, Find<Button>("Widget.flow.first")) && a.FocusState == FocusState.Keyboard,
                $"flow {horizontal}: appearance-only gap update preserves control and focus");

            styles[root.Id] = FlowStyle(20, "space-between");
            presenter.Apply(CreateFrame(root, styles)); presenter.UpdateLayout();
            Check(Math.Abs(Start(a)) < 1 && Math.Abs(Start(b) + Extent(b) - Extent(grid)) < 1,
                $"flow {horizontal}: space-between fills free space after reserving gap");
            styles[root.Id] = FlowStyle(40, "space-around");
            presenter.Apply(CreateFrame(root with { Children = new[] { first } }, styles)); presenter.UpdateLayout();
            Check(Math.Abs(Start(a) - (Extent(grid) - Extent(a)) / 2) < 1,
                $"flow {horizontal}: one child is centered without an artificial gap");
            presenter.Apply(CreateFrame(root with { Children = [] }, styles)); presenter.UpdateLayout();
            Check(grid.RowDefinitions.Count == 0 && grid.ColumnDefinitions.Count == 0,
                $"flow {horizontal}: empty flow has no retained distribution tracks");

            BridgeNodeRenderStyles FlowStyle(int gap, string justify) => Compute(
                $"row {{ direction: {(horizontal ? "row" : "column")}; gap: {gap}px; justify: {justify}; align: start; }}", root.Id, "row");
            double Extent(FrameworkElement element) => horizontal ? element.ActualWidth : element.ActualHeight;
            double Start(FrameworkElement element)
            {
                var point = element.TransformToVisual(grid).TransformPoint(new(0, 0));
                return horizontal ? point.X : point.Y;
            }
            void AssertAround(double gap)
            {
                var outer = (Extent(grid) - Extent(a) - Extent(b) - gap) / 4;
                Check(Math.Abs(Start(a) - outer) < 1 && Math.Abs(Start(b) - Start(a) - Extent(a) - gap - 2 * outer) < 1 &&
                    Math.Abs(Extent(grid) - Start(b) - Extent(b) - outer) < 1,
                    $"flow {horizontal}: space-around reserves {gap} DIPs plus two-to-one free-space distribution");
            }
        }

        var scrollRoot = new ViewNode { Id = "flow.scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
            Children = new[] { first, hidden, last } };
        var scrollStyles = scrollRoot.Children.ToDictionary(node => node.Id, node => Compute(
            "button { width: 40px; height: 40px; min-width: 0px; min-height: 0px; padding: 0px; }", node.Id, "button"));
        scrollStyles[scrollRoot.Id] = Compute("scroll { gap: 7px 19px; }", scrollRoot.Id, "scroll");
        var scrollFrame = CreateFrame(scrollRoot, scrollStyles);
        presenter.Apply(scrollFrame);
        await Wait(() => Find<ScrollViewer>("Widget.flow.scroll")?.IsLoaded == true);
        presenter.UpdateLayout();
        var scroller = Find<ScrollViewer>("Widget.flow.scroll")!;
        var panel = (StackPanel)scroller.Content;
        var scrollA = Find<Button>("Widget.flow.first")!;
        var scrollB = Find<Button>("Widget.flow.last")!;
        Check(panel.Spacing == 7 && Math.Abs(scrollB.TransformToVisual(panel).TransformPoint(new(0, 0)).Y - scrollA.ActualHeight - 7) < 1,
            "vertical Scroll uses authored row gap and collapsed child adds no gap");
        scrollA.Focus(FocusState.Keyboard);
        var updated = new Dictionary<string, BridgeNodeRenderStyles>(scrollStyles)
            { [scrollRoot.Id] = Compute("scroll { gap: 13px 23px; }", scrollRoot.Id, "scroll") };
        presenter.Apply(scrollFrame with { RenderStyles = updated, AppearanceRevision = scrollFrame.AppearanceRevision + 1 });
        presenter.UpdateLayout();
        Check(panel.Spacing == 13 && scrollA.FocusState == FocusState.Keyboard,
            "Scroll gap updates on appearance publication without replacing focused content");
        presenter.Apply(CreateFrame(scrollRoot with { ScrollAxis = ScrollAxis.Horizontal }, updated)); presenter.UpdateLayout();
        Check(ReferenceEquals(panel, scroller.Content) && panel.Orientation == Orientation.Horizontal && panel.Spacing == 23 &&
            Math.Abs(scrollB.TransformToVisual(panel).TransformPoint(new(0, 0)).X - scrollA.ActualWidth - 23) < 1,
            "retained horizontal Scroll switches to authored column gap");
        updated.Remove(scrollRoot.Id);
        presenter.Apply(CreateFrame(scrollRoot with { ScrollAxis = ScrollAxis.Horizontal }, updated)); presenter.UpdateLayout();
        Check(panel.Spacing == 12, "removing Scroll gap restores the default spacing");
        presenter.Width = double.NaN; presenter.Height = double.NaN;
    }
}
