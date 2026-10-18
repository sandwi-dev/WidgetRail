using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AdjacentScrollPanelNavigationAsync()
    {
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.Width = 800; presenter.Height = 480;
        ViewNode Button(string id) => new() { Id = id, Kind = ViewNodeKind.Button, Text = id, ActionId = id };
        ViewNode Space(string id) => new() { Id = id, Kind = ViewNodeKind.Spacer };
        var source = Button("panels.source");
        var details = Button("panels.details");
        var left = new ViewNode { Id = "panels.left", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
            Children = (ViewNode[])[Space("panels.leading"), source] };
        var right = left with { Id = "panels.right", GridCell = new() { Column = 1 },
            Children = (ViewNode[])[details, Space("panels.explanation")] };
        var root = new ViewNode { Id = "panels.root", Kind = ViewNodeKind.Grid,
            GridLayout = new() { Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Star }],
                Columns = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Star }, new() { Sizing = GridTrackSizing.Star }] },
            Children = (ViewNode[])[left, right] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            [root.Id] = Compute("grid { padding: 0px; gap: 16px; }", root.Id, "grid"),
            [left.Id] = Compute("scroll { padding: 12px; gap: 0px; }", left.Id, "scroll"),
            [right.Id] = Compute("scroll { padding: 12px; gap: 0px; }", right.Id, "scroll"),
            ["panels.leading"] = Compute("spacer { height: 320px; }", "panels.leading", "spacer"),
            ["panels.explanation"] = Compute("spacer { height: 180px; }", "panels.explanation", "spacer"),
            [source.Id] = Compute("button { height: 44px; min-height: 0px; margin: 0px; }", source.Id, "button"),
            [details.Id] = Compute("button { height: 44px; min-height: 0px; margin: 0px; }", details.Id, "button"),
        };
        void Apply(ViewNode panel) => presenter.Apply(CreateFrame(root with { Children = (ViewNode[])[left, panel] }, styles));
        Button Source() => Find<Button>("Widget.panels.source")!;
        Button Details() => Find<Button>("Widget.panels.details")!;
        bool Focused(DependencyObject element) => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), element);
        Apply(right);
        await Wait(() => Source() is { IsLoaded: true } && Details() is { IsLoaded: true });
        Source().Focus(FocusState.Keyboard);
        var rightScroll = Find<ScrollViewer>("Widget.panels.right")!;
        Check(!rightScroll.IsTabStop && rightScroll.ScrollableHeight < .5,
            "sparse detail panel has a visible button and no redundant container stop");
        Check(presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(Details()),
            "Right from a low row enters the adjacent panel's high visible button");
        if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--directional-navigation-result") is { } result)
            await CaptureStatusAsync(presenter, System.IO.Path.ChangeExtension(result, ".png"));
        var beforeAction = actions;
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        Check(actions == beforeAction + 1, "A activates the entered detail button exactly once");
        Check(presenter.MoveFocus(FocusNavigationDirection.Left) && Focused(Source()),
            "Left from the high detail button returns to the neighboring low row");
        Check(presenter.ScrollFocusOutlineForValidation is null,
            "cross-panel button navigation does not flash a reading-container outline");

        var nearer = Button("panels.nearer");
        styles[nearer.Id] = styles[details.Id];
        Apply(right with { Children = (ViewNode[])[details, Space("panels.explanation"), nearer] });
        await Wait(() => Find<Button>("Widget.panels.nearer") is { IsLoaded: true });
        Source().Focus(FocusState.Keyboard);
        presenter.MoveFocus(FocusNavigationDirection.Right);
        Check(Focused(Find<Button>("Widget.panels.nearer")!), "panel entry chooses its nearest visible control");

        // An aligned control remains authoritative even if the sparse panel is closer.
        var aligned = Button("panels.aligned");
        var alignedPanel = left with { Id = "panels.aligned-panel", GridCell = new() { Column = 2 },
            Children = (ViewNode[])[Space("panels.aligned-leading"), aligned] };
        styles[aligned.Id] = styles[source.Id];
        styles[alignedPanel.Id] = styles[left.Id];
        styles["panels.aligned-leading"] = styles["panels.leading"];
        var threeColumns = root with { GridLayout = root.GridLayout! with { Columns = (GridTrackDefinition[])[
            new() { Sizing = GridTrackSizing.Star }, new() { Sizing = GridTrackSizing.Star },
            new() { Sizing = GridTrackSizing.Pixel, Value = 100 }] }, Children = (ViewNode[])[left, right, alignedPanel] };
        presenter.Apply(CreateFrame(threeColumns, styles));
        await Wait(() => Find<Button>("Widget.panels.aligned") is { IsLoaded: true });
        Source().Focus(FocusState.Keyboard);
        presenter.MoveFocus(FocusNavigationDirection.Right);
        Check(Focused(Find<Button>("Widget.panels.aligned")!), "ordinary aligned navigation wins before sparse-panel fallback");

        Apply(right with { InputScopeId = "panels.inactive" });
        await Task.Delay(60);
        Source().Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(Source()),
            "panel fallback cannot cross an inactive input scope");

        Apply(right with { Children = (ViewNode[])[details with { IsDisabled = true }, Space("panels.explanation")] });
        await Task.Delay(60);
        Source().Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(Source()),
            "a fitted panel with only disabled controls cannot become an entry target");
        Apply(right with { Children = (ViewNode[])[Space("panels.explanation")] });
        await Task.Delay(60);
        Source().Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right), "short text-only panels do not add focus stops");

        var longSpace = Space("panels.long");
        styles[longSpace.Id] = Compute("spacer { height: 1000px; }", longSpace.Id, "spacer");
        Apply(right with { Children = (ViewNode[])[longSpace, details] });
        rightScroll = Find<ScrollViewer>("Widget.panels.right")!;
        await Wait(() => rightScroll.ScrollableHeight > 400 && rightScroll.IsTabStop);
        Source().Focus(FocusState.Keyboard);
        Check(presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(rightScroll),
            "offscreen-only actions enter their reading viewport without jumping to hidden controls");
        Check(rightScroll.VerticalOffset < .5, "entry preserves the neighboring panel's scroll position");
        presenter.ScrollBy(0, 2000);
        await Wait(() => rightScroll.VerticalOffset > 500);
        Check(Focused(rightScroll), "revealing a detail action waits for scroll release before settling focus");
        Check(presenter.SettleScrollFocus() && Focused(Details()), "scroll release settles on the revealed action");

        // A tall nested panel may exist in layout but only its high strip is visible.
        var clipped = right with { Id = "panels.clipped", Children = (ViewNode[])[right with { GridCell = null }] };
        styles[clipped.Id] = Compute("scroll { height: 100px; padding: 0px; }", clipped.Id, "scroll");
        styles[right.Id] = Compute("scroll { height: 480px; padding: 12px; }", right.Id, "scroll");
        Apply(clipped);
        await Wait(() => Find<ScrollViewer>("Widget.panels.clipped") is { IsLoaded: true });
        Source().Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(Source()),
            "clipped nested panel bounds cannot create a false horizontal neighbor");
        styles[right.Id] = styles[left.Id];

        // A panel below the source is not a horizontal neighbor after reflow.
        root = root with { GridLayout = new() {
            Columns = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Star }],
            Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Star }, new() { Sizing = GridTrackSizing.Star }] } };
        Apply(right with { GridCell = new() { Row = 1 } });
        await Task.Delay(100);
        Source().Focus(FocusState.Keyboard);
        Check(!presenter.MoveFocus(FocusNavigationDirection.Right) && Focused(Source()),
            "stacked panels cannot be entered with Right after responsive reflow");
    }
}
