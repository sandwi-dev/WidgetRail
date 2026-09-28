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
        presenter.Width = 1000; presenter.Height = 600;
        var compact = new ViewNode { Id = "compact", Kind = ViewNodeKind.Button, Text = "Compact",
            ActionId = "compact", FocusPersistenceId = "destination", VisibleWhen = ResponsiveVisibility.CompactOnly };
        var expanded = compact with { Id = "expanded", Text = "Expanded", ActionId = "expanded", VisibleWhen = ResponsiveVisibility.ExpandedOnly };
        var root = new ViewNode { Id = "responsive", Kind = ViewNodeKind.Row, Children = [compact, expanded] };
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
            Children = [new() { Id = "poster.title", Kind = ViewNodeKind.Text, Text = "Portrait title" }] };
        var poster = new ViewNode { Id = "poster", Kind = ViewNodeKind.ActionSurface, ActionId = "open",
            AccessibilityLabel = "Portrait title", ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
            ActionSurfacePresentation = ActionSurfacePresentation.Poster, Children = [art, copy] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles> { ["poster"] = Compute("#poster { width: 120px; height: 180px; padding: 0px; }", "poster", "actionSurface") };
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack, Children = [poster] }, styles));
        await Wait(() => Find<Image>("Widget.poster.artwork")?.ActualHeight > 100);
        var panel = (WidgetPosterPanel)Find<Button>("Widget.poster")!.Content;
        var image = Find<Image>("Widget.poster.artwork")!;
        var scrim = Find<Grid>("Widget.poster.scrim")!;
        var imageSlot = LayoutInformation.GetLayoutSlot(image);
        Check(panel.ColumnDefinitions.Count == 0 && panel.RowDefinitions.Count == 0 && Math.Abs(imageSlot.Width - panel.ActualWidth) < .01 &&
            Math.Abs(imageSlot.Height - panel.ActualHeight) < .01 && panel.Clip is RectangleGeometry,
            "poster artwork occupies one clipped native overlay cell instead of consuming a stack row");
        var copyBounds = scrim.TransformToVisual(panel).TransformBounds(new(0, 0, scrim.ActualWidth, scrim.ActualHeight));
        Check(Math.Abs(copyBounds.Bottom - panel.ActualHeight) < .01 && image.Stretch == Stretch.UniformToFill,
            "poster copy is bottom aligned over native cover artwork");
        var before = Find<Button>("Widget.poster");
        presenter.Apply(CreateFrame(new() { Id = "poster.root", Kind = ViewNodeKind.Stack,
            Children = [poster with { ActionSurfacePresentation = ActionSurfacePresentation.Standard }] }, styles));
        Check(!ReferenceEquals(before, Find<Button>("Widget.poster")) && Find<Button>("Widget.poster")!.Content is StackPanel,
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
        presenter.Width = double.NaN; presenter.Height = double.NaN;
    }
}
