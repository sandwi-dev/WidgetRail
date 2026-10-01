using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private static bool GridNear(double actual, double expected) => Math.Abs(actual - expected) <= 1;

    private async Task ExplicitGridAsync()
    {
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.Width = 600; presenter.Height = 360;
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["explicit"] = Compute("#explicit { gap: 99px; direction: column; padding: 0px; }", "explicit", "grid"),
            ["auto.content"] = Compute("#auto { height: 40px; padding: 0px; }", "auto", "spacer"),
            ["focus"] = Compute("#focus { padding: 0px; min-width: 0px; min-height: 0px; }", "focus", "button"),
        };
        var layout = new GridLayoutDefinition
        {
            Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 50 }, new() { Sizing = GridTrackSizing.Auto },
                new() { Sizing = GridTrackSizing.Star }],
            Columns = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 120 },
                new() { Sizing = GridTrackSizing.Star, Minimum = 100, Maximum = 200 },
                new() { Sizing = GridTrackSizing.Star, Value = 2 }],
            RowSpacing = 8, ColumnSpacing = 10,
        };
        var wrapped = new ViewNode { Id = "auto.content", Kind = ViewNodeKind.Spacer,
            GridCell = new() { Row = 1, Column = 1, ColumnSpan = 2 } };
        var focused = new ViewNode { Id = "focus", Kind = ViewNodeKind.Button, ActionId = "focus", Text = "Focus retained",
            GridCell = new() { Row = 2, Column = 2 } };
        var root = new ViewNode { Id = "explicit", Kind = ViewNodeKind.Grid, InputScopeId = "explicit.scope",
            GridLayout = layout, Children = (ViewNode[])[wrapped, focused] };
        void Apply(ViewNode value) => presenter.Apply(CreateFrame(value, styles, "explicit.scope"));
        try
        {
            Apply(root);
            await Wait(() => Find<Grid>("Widget.explicit")?.ActualWidth == 600 && Find<Button>("Widget.focus")?.IsLoaded == true);
            presenter.UpdateLayout();
            var grid = Find<Grid>("Widget.explicit")!;
            var button = Find<Button>("Widget.focus")!;
            var spacer = Find<Border>("Widget.auto.content")!;
            var owner = VisualTreeHelper.GetParent(spacer) as FrameworkElement;
            Check(grid.GetType() == typeof(Grid), "explicit SDK grid uses the platform Grid directly");
            Check(grid.RowSpacing == 8 && grid.ColumnSpacing == 10 && grid.RowDefinitions.Count == 3 && grid.ColumnDefinitions.Count == 3,
                "explicit tracks and spacing are not rewritten by flex direction or theme gap");
            Check(GridNear(grid.RowDefinitions[0].ActualHeight, 50) && GridNear(grid.RowDefinitions[1].ActualHeight, 40) &&
                GridNear(grid.RowDefinitions[2].ActualHeight, 254), "native fixed Auto and star rows divide finite height");
            Check(GridNear(grid.ColumnDefinitions[0].ActualWidth, 120) &&
                Math.Abs(grid.ColumnDefinitions[2].ActualWidth - 2 * grid.ColumnDefinitions[1].ActualWidth) <= 2,
                "native weighted star columns distribute remaining width after fixed tracks and spacing");
            Check(owner is WidgetMotionHost && Grid.GetRow(owner) == 1 && Grid.GetColumn(owner) == 1 && Grid.GetColumnSpan(owner) == 2 &&
                GridNear(owner.ActualWidth, grid.ColumnDefinitions[1].ActualWidth + 10 + grid.ColumnDefinitions[2].ActualWidth),
                "cell placement and span apply to the actual motion-wrapper layout owner");
            button.Focus(FocusState.Keyboard);
            var retainedColumn = grid.ColumnDefinitions[1];
            presenter.Width = 900;
            await Wait(() => grid.ActualWidth == 900);
            Check(GridNear(retainedColumn.ActualWidth, 200) && ReferenceEquals(retainedColumn, grid.ColumnDefinitions[1]) &&
                button.FocusState != FocusState.Unfocused, "native resize respects track maximum and retains definitions and focused control");
            presenter.Width = 340;
            await Wait(() => grid.ActualWidth == 340);
            Check(GridNear(retainedColumn.ActualWidth, 100) && GridNear(grid.ColumnDefinitions[2].ActualWidth, 100),
                "native star allocation honors track minimum while assigning the remaining width");
            presenter.Width = 900;
            await Wait(() => grid.ActualWidth == 900);

            wrapped = wrapped with { GridCell = new() { Row = 0, Column = 0, RowSpan = 2 } };
            focused = focused with { GridCell = new() { Row = 2, Column = 1 } };
            root = root with { Children = (ViewNode[])[wrapped, focused] };
            Apply(root); presenter.UpdateLayout();
            Check(ReferenceEquals(button, Find<Button>("Widget.focus")) && ReferenceEquals(grid, Find<Grid>("Widget.explicit")) &&
                Grid.GetRow(owner!) == 0 && Grid.GetRowSpan(owner!) == 2 && Grid.GetColumnSpan(owner!) == 1 && Grid.GetColumn(button) == 1,
                "same identities can move cells and change spans without recreating controls");
            Check(button.FocusState != FocusState.Unfocused, "cell updates preserve native focus");

            root = root with { Children = (ViewNode[])[wrapped with { GridCell = null }, focused with { GridCell = null }],
                GridLayout = new() };
            Apply(root); presenter.UpdateLayout();
            Check(grid.RowDefinitions.Count == 0 && grid.ColumnDefinitions.Count == 0 && grid.RowSpacing == 0 && grid.ColumnSpacing == 0 &&
                Grid.GetRow(owner!) == 0 && Grid.GetColumn(owner!) == 0 && Grid.GetRowSpan(owner!) == 1 && Grid.GetColumnSpan(owner!) == 1,
                "removing tracks and cells restores WinUI implicit star sizing and default attached placement");
            Check(GridNear(grid.ActualWidth, owner!.ActualWidth), "implicit single star fills available width");

            root = root with { GridLayout = null, GridMinimumColumnWidth = 180, GridMaximumColumns = 3 };
            Apply(root);
            await Wait(() => Find<WidgetResponsiveGrid>("Widget.explicit")?.ColumnDefinitions.Count == 2);
            Check(ReferenceEquals(button, Find<Button>("Widget.focus")) && Grid.GetColumnSpan(owner!) == 1 && Grid.GetRowSpan(owner!) == 1,
                "explicit-to-responsive replacement preserves child identity and clears explicit spans");
            root = root with { Kind = ViewNodeKind.Row, GridMinimumColumnWidth = null, GridMaximumColumns = null };
            Apply(root); presenter.UpdateLayout();
            Check(Find<Grid>("Widget.explicit")!.GetType() == typeof(Grid) && ReferenceEquals(button, Find<Button>("Widget.focus")) &&
                Grid.GetRow(owner!) == 0 && Grid.GetColumn(owner!) == 0 && Grid.GetRowSpan(owner!) == 1,
                "moving the same controls to ordinary row layout leaves no stale explicit placement");

            var rows = Enumerable.Range(0, 40).Select(index => new ViewNode { Id = "bounded." + index,
                Kind = ViewNodeKind.Button, ActionId = "row." + index, Text = "Row " + index }).ToArray();
            root = new() { Id = "explicit", Kind = ViewNodeKind.Grid, InputScopeId = "explicit.scope",
                GridLayout = new() { Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 80 }, new() { Sizing = GridTrackSizing.Star }] },
                Children = (ViewNode[])[new() { Id = "grid.scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                    GridCell = new() { Row = 1 }, Children = rows }] };
            Apply(root);
            await Wait(() => Find<ScrollViewer>("Widget.grid.scroll")?.ScrollableHeight > 0);
            var scroll = Find<ScrollViewer>("Widget.grid.scroll")!;
            Check(GridNear(scroll.ActualHeight, 280) && scroll.ViewportHeight <= 281 && scroll.ScrollableHeight > 500,
                "native star row gives scrolling content a finite viewport without custom flex constraints");
            var nativeRow = Find<Button>("Widget.bounded.0")!;
            nativeRow.Focus(FocusState.Keyboard);
            presenter.Height = 440;
            await Wait(() => GridNear(scroll.ActualHeight, 360));
            Check(nativeRow.FocusState != FocusState.Unfocused && ReferenceEquals(scroll, Find<ScrollViewer>("Widget.grid.scroll")),
                "resizing an explicit grid preserves its scroll owner and focused control");
        }
        finally
        {
            presenter.Width = presenter.Height = double.NaN;
            presenter.ApplyAppearance(AppearanceSettings.Default, true);
        }
    }
}
