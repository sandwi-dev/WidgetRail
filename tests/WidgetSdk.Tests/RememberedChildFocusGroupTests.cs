using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class RememberedChildFocusGroupTests
{
    internal static Task Run()
    {
        SharedContainerApiOwnsTheContract();
        ProtocolV33RoundTripsAndValidates();
        InvalidGroupsFailClosed();
        return Task.CompletedTask;
    }

    private static void SharedContainerApiOwnsTheContract()
    {
        var child = UI.Button("Play", "play", "controls.play");
        ContainerElement[] containers =
        [
            UI.Stack("stack", child),
            UI.Row("row", child),
            UI.Scroll("scroll", ScrollAxis.Vertical, child),
            UI.ResponsiveGrid("grid", 160, null, child),
            UI.Card("card", child),
        ];

        foreach (var container in containers)
        {
            var authored = container
                .InputScope("player")
                .Shortcut(ControllerButton.X, "player.toggle")
                .RememberChildFocus("controls.play");
            Equal("player", authored.InputScopeId);
            Equal("controls.play", authored.InitialChildFocusId);
            Equal(1, authored.Shortcuts.Count);
            Equal(1, authored.Children.Count);
        }

        True(typeof(StackElement).BaseType == typeof(ContainerElement));
        True(typeof(RowElement).BaseType == typeof(ContainerElement));
        True(typeof(ScrollElement).BaseType == typeof(ContainerElement));
        True(typeof(GridElement).BaseType == typeof(ContainerElement));
        True(UI.Card("card-check", child) is ContainerElement);
        WidgetElement actionSurface = UI.ActionSurface(
            "open", "action", "Open", ActionSurfaceOrientation.Vertical,
            UI.Text("Presentational", "action.copy"));
        True(actionSurface is not ContainerElement);
        True(child.VisibleWhen(ResponsiveVisibility.CompactOnly) is not ContainerElement);
        True(child.CollectionItem(new WidgetCollectionItemKey("item")) is not ContainerElement);
    }

    private static void ProtocolV33RoundTripsAndValidates()
    {
        var controls = UI.Row("controls",
                UI.Button("Play", "play", "controls.play")
                    .FocusRight("controls.seek"),
                UI.Slider(20, 0, 100, 5, "controls.seek.changed",
                    "controls.seek", "Seek"))
            .RememberChildFocus("controls.play");
        var snapshot = new WidgetView(
                UI.Stack("root",
                    UI.Button("Entry", "entry", "entry").FocusDown("controls"),
                    controls),
                "entry")
            .CreateSnapshot("focus-groups.instance", 1);

        Equal(ProtocolConstants.RememberedChildFocusGroupVersion,
            snapshot.ProtocolVersion);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var group = snapshot.Root.Children.Single(node => node.Id == "controls");
        Equal("controls.play", group.InitialChildFocusId);
        Equal("controls", snapshot.Root.Children[0].Focus!.Down);

        var legacy = snapshot with
        {
            ProtocolVersion = ProtocolConstants.RememberedChildFocusGroupVersion - 1,
        };
        True(ViewSnapshotValidator.Validate(legacy).Any(error =>
            error.Code == "feature_requires_version" &&
            error.Path.EndsWith("initialChildFocusId", StringComparison.Ordinal)));

        var json = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(snapshot));
        True(json.Contains("\"initialChildFocusId\":\"controls.play\"",
            StringComparison.Ordinal));
    }

    private static void InvalidGroupsFailClosed()
    {
        var buttonGroup = RawNode("button-group", ViewNodeKind.Button) with
        {
            ActionId = "activate",
            Text = "Button",
            InitialChildFocusId = "button-group.child",
            Children = [RawButton("button-group.child")],
        };
        Error(Snapshot(buttonGroup), "focus_group_on_non_container");

        var hiddenGroup = RawNode("hidden-group", ViewNodeKind.Row) with
        {
            InitialChildFocusId = "hidden-child",
            Children =
            [
                RawButton("hidden-child") with
                {
                    VisibleWhen = ResponsiveVisibility.CompactOnly,
                },
            ],
        };
        Error(Snapshot(hiddenGroup), "invalid_initial_child_focus");

        var crossScope = RawNode("cross-group", ViewNodeKind.Stack) with
        {
            InitialChildFocusId = "dialog.child",
            Children =
            [
                RawNode("dialog", ViewNodeKind.Stack) with
                {
                    InputScopeId = "dialog",
                    Children = [RawButton("dialog.child")],
                },
            ],
        };
        Error(Snapshot(crossScope), "initial_child_focus_outside_scope");

        var missingGroupNeighbor = Snapshot(RawButton("entry") with
        {
            Focus = new FocusNeighbors(Down: "missing-group"),
        });
        Error(missingGroupNeighbor, "invalid_focus_target");
    }

    private static ViewSnapshot Snapshot(ViewNode child) => new()
    {
        ProtocolVersion = ProtocolConstants.RememberedChildFocusGroupVersion,
        Sequence = 1,
        WidgetInstanceId = "focus-groups.instance",
        ActiveInputScopeId = "root",
        InitialFocusId = child.IsFocusable ? child.Id : null,
        Root = RawNode("root", ViewNodeKind.Stack) with { Children = [child] },
    };

    private static ViewNode RawNode(string id, ViewNodeKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Children = [],
    };

    private static ViewNode RawButton(string id) => RawNode(id, ViewNodeKind.Button) with
    {
        Text = id,
        ActionId = $"{id}.activate",
    };

    private static void Error(ViewSnapshot snapshot, string code) =>
        True(ViewSnapshotValidator.Validate(snapshot).Any(error => error.Code == code));

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected condition to be true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
