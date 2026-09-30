using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ResponsiveAndPostersAsync()
    {
        await FlowSpacingAndJustificationAsync();
        await GrowingControlsAndGroupHeadersAsync();
        await ScrollRowSizingAsync();
        presenter.Width = 1000; presenter.Height = 600;
        var compact = new ViewNode { Id = "compact", Kind = ViewNodeKind.Button, Text = "Compact",
            ActionId = "compact", FocusPersistenceId = "destination", VisibleWhen = ResponsiveVisibility.CompactOnly };
        var expanded = compact with { Id = "expanded", Text = "Expanded", ActionId = "expanded", VisibleWhen = ResponsiveVisibility.ExpandedOnly };
        var root = new ViewNode { Id = "responsive", Kind = ViewNodeKind.Row, Children = (ViewNode[])[compact, expanded] };
        presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        await Wait(() => presenter.ActualWidth == 1000 && Find<Button>("Widget.expanded")?.Visibility == Visibility.Visible);
        var expandedControl = Find<Button>("Widget.expanded")!;
        expandedControl.Focus(FocusState.Keyboard);
        Check(Find<Button>("Widget.compact")!.Visibility == Visibility.Collapsed && Find<Grid>("Widget.responsive")!.ColumnDefinitions.Count == 1,
            "inactive responsive branch contributes no control or layout gap");
        presenter.Width = 800;
        await Wait(() => Find<Button>("Widget.compact")?.FocusState != FocusState.Unfocused && expandedControl.Visibility == Visibility.Collapsed);
        Check(Find<Button>("Widget.compact")!.FocusState == FocusState.Keyboard, "responsive resize restores the logical focus destination without a new snapshot");
        presenter.Width = 1000;
        await Wait(() => expandedControl.FocusState == FocusState.Keyboard);
        Check(ReferenceEquals(expandedControl, Find<Button>("Widget.expanded")), "responsive toggles retain native control identity");
        presenter.Height = 500;
        await Wait(() => expandedControl.Visibility == Visibility.Collapsed);
        Check(Find<Button>("Widget.compact")!.Visibility == Visibility.Visible, "short widgets use compact composition independently of width");

        var art = new ViewNode { Id = "poster.artwork", Kind = ViewNodeKind.Image, ImageFit = ImageFit.Cover, AccessibilityLabel = "Cover",
            ImageSource = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==" };
        var copy = new ViewNode { Id = "poster.scrim", Kind = ViewNodeKind.Stack,
            Children = (ViewNode[])[new() { Id = "poster.title", Kind = ViewNodeKind.Text, Text = "Portrait title" }] };
        var poster = new ViewNode { Id = "poster", Kind = ViewNodeKind.ActionSurface, ActionId = "open",
            AccessibilityLabel = "Portrait title", ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
            ActionSurfacePresentation = ActionSurfacePresentation.Poster, Children = (ViewNode[])[art, copy] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles> { ["poster"] = Compute("#poster { width: 120px; height: 180px; padding: 0px; }", "poster", "actionSurface") };
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[poster] }, styles));
        await Wait(() => Find<WidgetArtworkView>("Widget.poster.artwork")?.ActualHeight > 100);
        var panel = (WidgetPosterPanel)Find<Button>("Widget.poster")!.Content;
        var image = Find<WidgetArtworkView>("Widget.poster.artwork")!;
        var scrim = Find<Grid>("Widget.poster.scrim")!;
        var imageSlot = LayoutInformation.GetLayoutSlot(image);
        Check(panel.ColumnDefinitions.Count == 0 && panel.RowDefinitions.Count == 0 && Math.Abs(imageSlot.Width - panel.ActualWidth) < .01 &&
            Math.Abs(imageSlot.Height - panel.ActualHeight) < .01 &&
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(panel).Clip is Microsoft.UI.Composition.CompositionGeometricClip,
            "poster artwork occupies one clipped native overlay cell instead of consuming a stack row");
        var copyBounds = scrim.TransformToVisual(panel).TransformBounds(new(0, 0, scrim.ActualWidth, scrim.ActualHeight));
        Check(Math.Abs(copyBounds.Bottom - panel.ActualHeight) < .01 && image.Stretch == Stretch.UniformToFill,
            "poster copy is bottom aligned over native cover artwork");
        styles["poster.scrim"] = Compute("#badge { height: 34px; min-height: 34px; padding: 7px 8px; }", "badge", "stack");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[poster] }, styles));
        presenter.UpdateLayout();
        copyBounds = scrim.TransformToVisual(panel).TransformBounds(new(0, 0, scrim.ActualWidth, scrim.ActualHeight));
        Check(Math.Abs(copyBounds.Bottom - panel.ActualHeight) < 1 && copyBounds.Height < 40,
            "fixed-height poster badge and its layout wrapper remain at the bottom edge");
        styles["poster"] = Compute("#poster { width: 120px; aspect-ratio: 1; padding: 0px; }", "poster", "actionSurface");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[poster] }, styles));
        await Wait(() => Near(panel.ActualWidth, panel.ActualHeight));
        Check(Near(panel.AspectRatio, 1), "square authored posters use their resolved aspect ratio");
        styles["poster"] = Compute("#poster { width: 120px; height: 180px; padding: 0px; corner-radius: 12px; } #poster:focused { corner-radius: 18px; }", "poster", "actionSurface");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[poster] }, styles));
        var posterControl = Find<Button>("Widget.poster")!;
        outside.Focus(FocusState.Keyboard);
        var clip = (Microsoft.UI.Composition.CompositionGeometricClip)Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(panel).Clip;
        var geometry = (Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)clip.Geometry;
        await Wait(() => geometry.CornerRadius.X == 12);
        posterControl.Focus(FocusState.Keyboard);
        await Wait(() => geometry.CornerRadius.X == 18);
        Check(posterControl.CornerRadius.TopLeft == geometry.CornerRadius.X,
            "poster artwork and focused box share the authored corner radius");
        styles["poster"] = Compute("#poster { width: 120px; height: 180px; padding: 0px; corner-radius: 0px; }", "poster", "actionSurface");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[poster] }, styles));
        Check(geometry.CornerRadius.X == 0 && ReferenceEquals(posterControl, Find<Button>("Widget.poster")),
            "square theme restores square artwork without replacing the poster control");
        var before = Find<Button>("Widget.poster");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack,
            Children = (ViewNode[])[poster with { ActionSurfacePresentation = ActionSurfacePresentation.Standard }] }, styles));
        Check(!ReferenceEquals(before, Find<Button>("Widget.poster")) && Find<Button>("Widget.poster")!.Content is Panel and not WidgetPosterPanel,
            "changing presentation retires the incompatible poster layout without changing action identity");
        presenter.Width = 620;
        var cells = Enumerable.Range(0, 5).Select(index => new ViewNode { Id = "cell" + index,
            Kind = ViewNodeKind.Button, Text = "Cell " + index, ActionId = "cell" + index }).ToArray();
        presenter.Apply(CreateFrame(new() { Id = "grid", Kind = ViewNodeKind.Grid, GridMinimumColumnWidth = 180,
            GridMaximumColumns = 4, Children = cells }, new Dictionary<string, BridgeNodeRenderStyles>()));
        await Wait(() => Find<WidgetResponsiveGrid>("Widget.grid")?.ColumnDefinitions.Count == 3);
        var cell = Find<Button>("Widget.cell4")!;
        cell.Focus(FocusState.Keyboard);
        Check(Grid.GetRow(cell) == 1 && Grid.GetColumn(cell) == 1, "responsive static grid uses actual width and preserves document order");
        presenter.Width = 400;
        await Wait(() => Find<WidgetResponsiveGrid>("Widget.grid")?.ColumnDefinitions.Count == 2);
        Check(Grid.GetRow(cell) == 2 && Grid.GetColumn(cell) == 0 && cell.FocusState == FocusState.Keyboard,
            "native grid reflow retains the focused control without a snapshot or a custom focus layout");
        var scrollRows = Enumerable.Range(0, 30).Select(index => new ViewNode { Id = "scroll.row" + index,
            Kind = ViewNodeKind.Button, Text = "Row " + index, ActionId = "scroll.row" + index }).ToArray();
        presenter.Apply(CreateFrame(new() { Id = "scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
            Children = scrollRows }, new Dictionary<string, BridgeNodeRenderStyles> { ["scroll"] = Compute("#scroll { height: 180px; }", "scroll", "scroll") }));
        await Wait(() => Find<ScrollViewer>("Widget.scroll")?.ScrollableHeight > 300);
        var scroller = Find<ScrollViewer>("Widget.scroll")!;
        var first = Find<Button>("Widget.scroll.row0")!;
        first.Focus(FocusState.Keyboard);
        // Native focus reveal is asynchronous; finish it before exercising the
        // independent analog operation, as production input does across frames.
        await Task.Delay(150);
        Check(presenter.ScrollBy(0, 120), "normalized analog movement finds the focused native scroll owner");
        await Wait(() => scroller.VerticalOffset > 100);
        Check(first.FocusState == FocusState.Keyboard, "analog scrolling preserves focus without navigation or animation queues");
        presenter.ScrollBy(0, -100000);
        await Wait(() => scroller.VerticalOffset == 0);
        Check(!presenter.ScrollBy(double.NaN, 1), "native analog scroll clamps boundaries and rejects nonfinite movement");
        presenter.Width = double.NaN; presenter.Height = double.NaN;
    }

    private async Task GrowingControlsAndGroupHeadersAsync()
    {
        presenter.Width = 640; presenter.Height = 100;
        var buttons = Enumerable.Range(0, 4).Select(index => new ViewNode
            { Id = "filter." + index, Kind = ViewNodeKind.Button, Text = "Filter " + index, ActionId = "filter." + index }).ToArray();
        var root = new ViewNode { Id = "filters", Kind = ViewNodeKind.Row, Children = buttons };
        var styles = buttons.ToDictionary(node => node.Id, node => Compute(
            "button { flex-grow: 1; min-height: 44px; text-align: center; }", node.Id, "button"));
        styles[root.Id] = Compute("#filters { gap: 4px; align: center; }", root.Id, "row");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<Button>("Widget.filter.0")?.ActualWidth > 150);
        var first = Find<Button>("Widget.filter.0")!;
        Check(buttons.All(node => Math.Abs(Find<Button>("Widget." + node.Id)!.ActualWidth - 157) < 1),
            "growing buttons fill equal native grid tracks instead of painting a small button inside each slot");
        styles[buttons[0].Id] = Compute("button { min-height: 44px; }", buttons[0].Id, "button");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => first.ActualWidth < 150);
        Check(ReferenceEquals(first, Find<Button>("Widget.filter.0")),
            "removing growth returns to intrinsic size without replacing the button");
        styles[buttons[0].Id] = Compute("button { flex-grow: 1; }", buttons[0].Id, "button");
        styles[root.Id] = Compute("#filters { direction: column; gap: 4px; align: center; }", root.Id, "row");
        presenter.Height = 320;
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => first.ActualHeight > 70 && first.ActualWidth < 150);
        Check(buttons.All(node => Math.Abs(Find<Button>("Widget." + node.Id)!.ActualHeight - 77) < 1),
            "changing the row axis transfers growth to height and restores cross-axis centering");

        foreach (var gridHeader in new[] { true, false })
        {
            var label = new WidgetGroupHeader { Text = "Recommendations", HeaderStyle =
                Compute("#header { color: #44bbcc; font-size: 20px; }", "header", "text") };
            ContentControl container = gridHeader ? new GridViewHeaderItem() : new ListViewHeaderItem();
            container.Style = (Style)Application.Current.Resources[gridHeader
                ? "WidgetIndexedGridHeaderContainer" : "WidgetIndexedListHeaderContainer"];
            container.Content = label; container.Width = 300;
            host.Children.Add(container);
            await Wait(() => label.ActualWidth > 0);
            var bounds = label.TransformToVisual(container).TransformBounds(new(0, 0, label.ActualWidth, label.ActualHeight));
            Check(Math.Abs(bounds.Left - 6) < 1 && container.Padding == new Thickness(0) && !container.IsTabStop,
                $"{(gridHeader ? "grid" : "list")} group header has one shared inset and no independent focus target");
            Check(!Descendants(container).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().Any(),
                "native group chrome adds no unthemed separator around the themed section title");
            host.Children.Remove(container);
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); ++index)
            {
                var child = VisualTreeHelper.GetChild(owner, index); yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
