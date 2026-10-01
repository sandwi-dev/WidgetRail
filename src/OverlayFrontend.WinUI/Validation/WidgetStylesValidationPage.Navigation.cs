using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task DirectionalNavigationAsync()
    {
        await DirectionalScrollingAsync();
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.Width = 600; presenter.Height = 320;
        ViewNode Button(string id) => new() { Id = id, Kind = ViewNodeKind.Button, Text = id, ActionId = id };
        var shortcut = Button("shortcut") with { Focus = new(Up: "quit") };
        var exclusive = Button("exclusive");
        var scroll = new ViewNode { Id = "options", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
            GridCell = new() { Row = 1 }, Children = (ViewNode[])[new() { Id = "intro", Kind = ViewNodeKind.Spacer }, shortcut,
                new() { Id = "help", Kind = ViewNodeKind.Spacer }, exclusive, new() { Id = "tail", Kind = ViewNodeKind.Spacer }] };
        var root = new ViewNode { Id = "navigation", Kind = ViewNodeKind.Grid,
            GridLayout = new() { Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 50 }, new() { Sizing = GridTrackSizing.Star }] },
            Children = (ViewNode[])[Button("quit"), scroll] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["navigation"] = Compute("#x { padding: 0px; gap: 0px; }", "x", "grid"),
            ["options"] = Compute("#x { padding: 0px; gap: 0px; }", "x", "scroll"),
            ["intro"] = Compute("#x { height: 100px; }", "x", "spacer"),
            ["help"] = Compute("#x { height: 100px; }", "x", "spacer"),
            ["tail"] = Compute("#x { height: 500px; }", "x", "spacer"),
        };
        foreach (var id in new[] { "shortcut", "exclusive", "quit" })
            styles[id] = Compute("#x { height: 44px; min-height: 0px; padding: 0px; margin: 0px; }", "x", "button");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<Button>("Widget.exclusive") is { IsLoaded: true });
        var optionScroll = Find<ScrollViewer>("Widget.options")!;
        var exclusiveControl = Find<Button>("Widget.exclusive")!;
        var shortcutControl = Find<Button>("Widget.shortcut")!;
        exclusiveControl.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        optionScroll.ChangeView(null, 170, null, true);
        await Wait(() => optionScroll.VerticalOffset >= 169);
        Check(shortcutControl.TransformToVisual(optionScroll).TransformPoint(new(0, 44)).Y < 0,
            "fixture shortcut is entirely above the viewport while Exclusive control is focused");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), exclusiveControl), "fixture retains Exclusive control focus");
        Check(presenter.MoveFocus(FocusNavigationDirection.Up), "Up inside scroll is consumed");
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), shortcutControl) && optionScroll.VerticalOffset < 169);
        Check(true, "offscreen shortcut wins over nearer fixed Quit header and is brought into view");
        optionScroll.ChangeView(null, 0, null, true);
        await Wait(() => optionScroll.VerticalOffset < .5);
        Check(presenter.MoveFocus(FocusNavigationDirection.Up), "explicit header exit at the content edge is consumed");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.quit")), "explicit Up wins at the first page control");

        exclusiveControl.Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right), "Right cannot jump diagonally to fixed header");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), exclusiveControl), "horizontal boundary retains focus");
        scroll = scroll with { Children = (ViewNode[])[scroll.Children[0], shortcut with { IsDisabled = true }, scroll.Children[2], exclusive, scroll.Children[4]] };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll] }, styles));
        exclusiveControl = Find<Button>("Widget.exclusive")!;
        exclusiveControl.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        optionScroll.ChangeView(null, 0, null, true);
        await Wait(() => optionScroll.VerticalOffset < .5);
        presenter.MoveFocus(FocusNavigationDirection.Up);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.quit")), "disabled offscreen control is skipped and the actual scroll boundary can exit");
        presenter.SetPresentationInputEnabled(false);
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.quit")), "withdrawn input cannot move focus");
        presenter.SetPresentationInputEnabled(true);

        // The same owner policy must hold after native adaptive-column layout,
        // including a terminal partial row and a narrow single-column viewport.
        var tiles = Enumerable.Range(0, 7).Select(index => Button("tile." + index)).ToArray();
        foreach (var tile in tiles) styles[tile.Id] = styles["exclusive"];
        styles["tiles"] = Compute("#x { gap: 12px; min-width: 0px; }", "x", "grid");
        var grid = new ViewNode { Id = "tiles", Kind = ViewNodeKind.Grid, GridMinimumColumnWidth = 150,
            GridMaximumColumns = 3, Children = tiles };
        scroll = scroll with { Children = (ViewNode[])[grid] };
        foreach (var width in new[] { 600d, 400d, 260d })
        {
            presenter.Width = width;
            presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll] }, styles));
            var columns = width == 600 ? 3 : width == 400 ? 2 : 1;
            await Wait(() => Find<WidgetResponsiveGrid>("Widget.tiles")?.ColumnDefinitions.Count == columns);
            Find<Button>("Widget.tile.0")!.Focus(FocusState.Keyboard);
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.tile." + columns)),
                $"Down stays in the grid's native column at width {width}");
            Find<Button>("Widget.tile." + (columns - 1))!.Focus(FocusState.Keyboard);
            Check(!presenter.MoveFocus(FocusNavigationDirection.Right), $"Right does not wrap rows or jump to header at width {width}");
            Find<Button>("Widget.tile.5")!.Focus(FocusState.Keyboard);
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.tile.6")),
                $"terminal partial row remains reachable at width {width}");
        }

        // Authored edges still outrank the owning grid. Busy is not disabled.
        presenter.Width = 600;
        tiles[0] = tiles[0] with { Focus = new(Down: "quit") };
        tiles[3] = tiles[3] with { IsBusy = true };
        grid = grid with { Children = tiles };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll with { Children = (ViewNode[])[grid] }] }, styles));
        await Wait(() => Find<WidgetResponsiveGrid>("Widget.tiles")?.ColumnDefinitions.Count == 3);
        Find<Button>("Widget.tile.0")!.Focus(FocusState.Keyboard);
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.quit")), "authored Down overrides grid ownership");
        tiles[0] = tiles[0] with { Focus = null };
        grid = grid with { Children = tiles };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll with { Children = (ViewNode[])[grid] }] }, styles));
        Find<Button>("Widget.tile.0")!.Focus(FocusState.Keyboard);
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.tile.3")), "busy controls retain navigability while action admission remains blocked");

        var isolated = new ViewNode { Id = "isolated", Kind = ViewNodeKind.Stack, InputScopeId = "inactive.scope",
            Children = (ViewNode[])[tiles[3] with { IsBusy = false }] };
        grid = grid with { Children = tiles.Select((tile, index) => index == 3 ? isolated : tile).ToArray() };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll with { Children = (ViewNode[])[grid] }] }, styles));
        Find<Button>("Widget.tile.0")!.Focus(FocusState.Keyboard);
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.tile.6")), "geometry cannot cross an inactive nested input scope");

        styles["before"] = styles["exclusive"];
        styles["middle"] = Compute("#x { height: 200px; }", "x", "spacer");
        styles["inner"] = Compute("#x { height: 60px; gap: 12px; }", "x", "scroll");
        foreach (var id in new[] { "left", "right" }) styles[id] = Compute("#x { width: 200px; height: 44px; }", "x", "button");
        var inner = new ViewNode { Id = "inner", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Horizontal,
            Children = (ViewNode[])[Button("left"), Button("right")] };
        var nested = scroll with { Children = (ViewNode[])[Button("before"), new() { Id = "middle", Kind = ViewNodeKind.Spacer }, inner,
            new() { Id = "tail", Kind = ViewNodeKind.Spacer }] };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), nested] }, styles));
        await Wait(() => Find<Button>("Widget.right") is { IsLoaded: true });
        optionScroll = Find<ScrollViewer>("Widget.options")!;
        Find<Button>("Widget.right")!.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        optionScroll.ChangeView(null, 200, null, true);
        await Wait(() => optionScroll.VerticalOffset >= 199);
        presenter.MoveFocus(FocusNavigationDirection.Left);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.left")), "Left stays in the nearest horizontal scroll owner");
        presenter.MoveFocus(FocusNavigationDirection.Up);
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.before")) && optionScroll.VerticalOffset < 199);
        Check(true, "Up from a horizontal row reveals the outer vertical scroll target before its fixed header");

        Find<Button>("Widget.right")!.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        var verticalBefore = optionScroll.VerticalOffset;
        var innerScroll = Find<ScrollViewer>("Widget.inner")!;
        var horizontalBefore = innerScroll.HorizontalOffset;
        presenter.MoveFocus(FocusNavigationDirection.Down);
        await Wait(() => optionScroll.VerticalOffset > verticalBefore + 1);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.right")) &&
            Math.Abs(innerScroll.HorizontalOffset - horizontalBefore) < .5,
            "Down through noninteractive tail chooses the matching outer axis without moving focus");

        // Leave the grid mounted for the script's real keyboard-routing checks.
        grid = grid with { Children = tiles };
        presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[Button("quit"), scroll with { Children = (ViewNode[])[grid] }] }, styles));
        await Wait(() => Find<Button>("Widget.tile.3") is { IsLoaded: true });
    }
}
