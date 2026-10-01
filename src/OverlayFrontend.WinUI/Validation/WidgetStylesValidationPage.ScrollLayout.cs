using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ScrollRowSizingAsync()
    {
        presenter.Width = 640; presenter.Height = 360;
        // Display Profiles uses intrinsic-width action rows; Playnite Categories
        // mixes full-width rows with an intentionally narrow Create button.
        var card = new ViewNode { Id = "scroll.card", Kind = ViewNodeKind.ActionSurface,
            ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal, AccessibilityLabel = "Display profile",
            ActionId = "open", Children = (ViewNode[])[new() {
                Id = "scroll.copy", Kind = ViewNodeKind.Text, Text = "Display profile" }] };
        var category = new ViewNode { Id = "scroll.category", Kind = ViewNodeKind.Button,
            Text = "Category", ActionId = "category" };
        var create = category with { Id = "scroll.create", Text = "Create category" };
        var capped = category with { Id = "scroll.capped", Text = "Bounded row" };
        var animated = category with { Id = "scroll.animated", Text = "Animated row",
            Transition = new("scroll.layout", "stable", 0, WidgetTransitionKind.Layout) };
        var root = new ViewNode { Id = "scroll.rows", Kind = ViewNodeKind.Scroll,
            ScrollAxis = ScrollAxis.Vertical, Children = (ViewNode[])[card, category, create, capped, animated] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            [category.Id] = Compute("button { width: 100%; min-height: 64px; }", category.Id, "button"),
            [create.Id] = Compute("button { width: 340px; max-width: 340px; }", create.Id, "button"),
            [capped.Id] = Compute("button { min-width: 220px; max-width: 280px; }", capped.Id, "button"),
        };
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<ScrollViewer>("Widget.scroll.rows")?.ActualWidth > 600);
        presenter.UpdateLayout();
        var scroll = Find<ScrollViewer>("Widget.scroll.rows")!;
        var panel = (StackPanel)scroll.Content;
        var cardControl = Find<Button>("Widget.scroll.card")!;
        var categoryControl = Find<Button>("Widget.scroll.category")!;
        var createControl = Find<Button>("Widget.scroll.create")!;
        var cappedControl = Find<Button>("Widget.scroll.capped")!;
        var animatedControl = Find<Button>("Widget.scroll.animated")!;
        Check(FillsWidth(cardControl) && FillsWidth(categoryControl),
            "ordinary scroll rows stretch for implicit Display Profiles and explicit 100% Categories sizing");
        Check(Math.Abs(createControl.ActualWidth - 340) < 1 && Math.Abs(cappedControl.ActualWidth - 280) < 1,
            "scroll stretching preserves fixed-width Create Category and bounded-width controls");
        Check(FillsWidth(animatedControl), "scroll row motion wrapper fills the same viewport as ordinary controls");

        presenter.Width = 480;
        await Wait(() => scroll.ActualWidth < 500);
        presenter.Apply(CreateFrame(root, styles));
        presenter.UpdateLayout();
        Check(FillsWidth(cardControl) && FillsWidth(categoryControl) && FillsWidth(animatedControl) &&
            ReferenceEquals(cardControl, Find<Button>("Widget.scroll.card")) && Math.Abs(createControl.ActualWidth - 340) < 1,
            "viewport resize and publication retain row identity and recompute stretch without widening fixed controls");

        foreach (var align in new[] { "start", "center", "end" })
        {
            styles[root.Id] = Compute($"scroll {{ align: {align}; }}", root.Id, "scroll");
            presenter.Apply(CreateFrame(root, styles));
            presenter.UpdateLayout();
            var left = createControl.TransformToVisual(panel).TransformPoint(new(0, 0)).X;
            var remaining = panel.ActualWidth - createControl.ActualWidth;
            var expected = align == "start" ? 0 : align == "center" ? remaining / 2 : remaining;
            Check(Math.Abs(left - expected) < 1 && cardControl.ActualWidth < panel.ActualWidth && FillsWidth(categoryControl),
                $"scroll align {align} positions narrow rows while explicit full-width rows still fill");
        }

        // Reuse the same controls while swapping axes and removing old sizing.
        root = root with { ScrollAxis = ScrollAxis.Horizontal };
        styles = root.Children.ToDictionary(node => node.Id, node => Compute(
            "* { width: 160px; }", node.Id, node.Kind == ViewNodeKind.ActionSurface ? "actionSurface" : "button"));
        styles[category.Id] = Compute("button { width: 160px; height: 100%; }", category.Id, "button");
        styles[create.Id] = Compute("button { width: 160px; height: 80px; max-height: 80px; }", create.Id, "button");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => scroll.ScrollableWidth > 200);
        presenter.UpdateLayout();
        Check(panel.Orientation == Orientation.Horizontal && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled &&
            Math.Abs(panel.ActualHeight - scroll.ViewportHeight) < 1 &&
            Math.Abs(cardControl.ActualHeight - panel.ActualHeight) < 1 &&
            Math.Abs(categoryControl.ActualHeight - panel.ActualHeight) < 1 &&
            Math.Abs(animatedControl.ActualHeight - panel.ActualHeight) < 1 && Math.Abs(createControl.ActualHeight - 80) < 1,
            "horizontal scrolling stretches only the bounded height and preserves explicit heights after axis changes");
        Check(ReferenceEquals(categoryControl, Find<Button>("Widget.scroll.category")) &&
            categoryControl.HorizontalAlignment == HorizontalAlignment.Left,
            "axis changes retain controls and clear previous cross-axis alignment");
        var diagnostics = presenter.CaptureLayoutDiagnostics(2);
        Check(diagnostics["nodes"]!.AsArray().Count == 2 && diagnostics["omittedNodes"]!.GetValue<int>() > 0 &&
            diagnostics["nodes"]![0]!["layoutOwner"] is not null && !diagnostics.ToJsonString().Contains("Display profile"),
            "failure layout snapshot is bounded, includes layout owners, and excludes display text");
        presenter.Width = double.NaN; presenter.Height = double.NaN;

        bool FillsWidth(FrameworkElement element) => Math.Abs(element.ActualWidth - panel.ActualWidth) < 1;
    }
}
