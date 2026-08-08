using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

internal static class GridComponentTests
{
    internal static Task RoundTripsTypedSemantics()
    {
        var grid = UI.ResponsiveGrid(
                "library.grid",
                minimumColumnWidth: 180,
                maximumColumns: 5,
                UI.AppTile("Discord", "Ready", "launch.discord", "app.discord"),
                UI.MediaTile("Current song", "Playing", "open.media", "media.current"))
            .InputScope("library.scope")
            .Shortcut(ControllerButton.B, "library.back")
            .AddClasses("library-grid");
        var snapshot = new WidgetView(
            grid,
            InitialFocusId: "app.discord",
            ActiveInputScopeId: "library.scope")
            .CreateSnapshot("library.instance", 9);

        Equal(ProtocolConstants.ResponsiveGridVersion, snapshot.ProtocolVersion);
        Equal(ViewNodeKind.Grid, snapshot.Root.Kind);
        Equal(180D, snapshot.Root.GridMinimumColumnWidth);
        Equal(5, snapshot.Root.GridMaximumColumns);
        Equal("library.scope", snapshot.Root.InputScopeId);
        Equal("library.back", snapshot.Root.Shortcuts.Single().ActionId);
        True(snapshot.Root.StyleClasses.SequenceEqual(
                new[] { "gbar-responsive-grid", "library-grid" }),
            "Responsive grid semantic classes changed.");

        var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Equal(ViewNodeKind.Grid, restored.Root.Kind);
        Equal(180D, restored.Root.GridMinimumColumnWidth);
        Equal(5, restored.Root.GridMaximumColumns);
        Equal("app.discord", restored.InitialFocusId);
        Equal("library.scope", restored.ActiveInputScopeId);
        Equal(2, restored.Root.Children.Count);
        return Task.CompletedTask;
    }

    internal static Task ValidatesBoundsAndCompatibility()
    {
        var empty = UI.ResponsiveGrid(
            "empty.grid", ProtocolConstants.MinimumGridColumnWidth);
        var emptySnapshot = new WidgetView(empty)
            .CreateSnapshot("empty.instance", 1);
        Equal(ProtocolConstants.ResponsiveGridVersion, emptySnapshot.ProtocolVersion);
        Equal(0, emptySnapshot.Root.Children.Count);
        Equal(0, ViewSnapshotValidator.Validate(emptySnapshot).Count);

        Throws<ArgumentOutOfRangeException>(() => UI.ResponsiveGrid(
            "small.grid", ProtocolConstants.MinimumGridColumnWidth - 0.01));
        Throws<ArgumentOutOfRangeException>(() => UI.ResponsiveGrid(
            "large.grid", ProtocolConstants.MaximumGridColumnWidth + 0.01));
        Throws<ArgumentOutOfRangeException>(() => UI.ResponsiveGrid(
            "nan.grid", double.NaN));
        Throws<ArgumentOutOfRangeException>(() => UI.ResponsiveGrid(
            "zero.columns", 100, 0));
        Throws<ArgumentOutOfRangeException>(() => UI.ResponsiveGrid(
            "many.columns", 100, ProtocolConstants.MaximumGridColumns + 1));
        Throws<ArgumentNullException>(() => UI.ResponsiveGrid(
            "null.grid", 100, children: null!));
        Throws<ArgumentException>(() => UI.ResponsiveGrid(
            "null.child.grid", 100, children: [null!]));

        var legacyGrid = emptySnapshot with
        {
            ProtocolVersion = ProtocolConstants.ActionSurfaceVersion,
        };
        True(ViewSnapshotValidator.Validate(legacyGrid).Any(error =>
                error.Code == "feature_requires_version"),
            "Protocol v7 must reject the v8 Grid feature.");
        var invalidMinimum = emptySnapshot with
        {
            Root = emptySnapshot.Root with { GridMinimumColumnWidth = double.PositiveInfinity },
        };
        True(ViewSnapshotValidator.Validate(invalidMinimum).Any(error =>
                error.Code == "invalid_grid_minimum_column_width"),
            "Wire validation accepted a non-finite grid minimum.");
        var invalidMaximum = emptySnapshot with
        {
            Root = emptySnapshot.Root with
            {
                GridMaximumColumns = ProtocolConstants.MaximumGridColumns + 1,
            },
        };
        True(ViewSnapshotValidator.Validate(invalidMaximum).Any(error =>
                error.Code == "invalid_grid_maximum_columns"),
            "Wire validation accepted an excessive grid column cap.");
        var gridPropertyOnStack = emptySnapshot with
        {
            Root = emptySnapshot.Root with { Kind = ViewNodeKind.Stack },
        };
        True(ViewSnapshotValidator.Validate(gridPropertyOnStack).Any(error =>
                error.Code == "grid_property_not_allowed"),
            "Grid properties leaked onto another container kind.");

        var actionV7 = new WidgetView(UI.ActionSurface(
            "open", "legacy.action", "Open",
            ActionSurfaceOrientation.Horizontal,
            UI.Text("Open", "legacy.action.label")))
            .CreateSnapshot("legacy.action.instance", 1);
        Equal(ProtocolConstants.ActionSurfaceVersion, actionV7.ProtocolVersion);
        var baseline = new WidgetView(UI.Stack("legacy.root", UI.Text("Ready", "legacy.text")))
            .CreateSnapshot("legacy.instance", 1);
        Equal(ProtocolConstants.BaselineVersion, baseline.ProtocolVersion);
        for (var version = ProtocolConstants.MinimumSupportedVersion;
             version < ProtocolConstants.ActionSurfaceVersion;
             version++)
            Equal(0, ViewSnapshotValidator.Validate(baseline with
            {
                ProtocolVersion = version,
            }).Count);

        var nullChildren = emptySnapshot with
        {
            Root = emptySnapshot.Root with { Children = null! },
        };
        True(ViewSnapshotValidator.Validate(nullChildren).Any(error =>
                error.Code == "required" && error.Path.EndsWith(".children", StringComparison.Ordinal)),
            "Null child collections must fail normal wire validation.");
        var nullActionChildren = actionV7 with
        {
            Root = actionV7.Root with { Children = null! },
        };
        True(ViewSnapshotValidator.Validate(nullActionChildren).Any(error =>
                error.Code == "required" && error.Path.EndsWith(".children", StringComparison.Ordinal)),
            "Null ActionSurface child collections must fail normal wire validation.");
        return Task.CompletedTask;
    }

    internal static Task PreservesChildrenAndFocus()
    {
        const int childCount = 256;
        var children = Enumerable.Range(0, childCount)
            .Select(index => (WidgetElement)UI.Button(
                $"Application {index}", $"launch.{index}", $"app.{index}"))
            .ToArray();
        children[0] = ((ButtonElement)children[0]).FocusRight("app.1");
        children[1] = ((ButtonElement)children[1]).FocusLeft("app.0");
        var snapshot = new WidgetView(
            UI.ResponsiveGrid("large.grid", 120, 8, children),
            InitialFocusId: "app.0")
            .CreateSnapshot("large.instance", 1);

        Equal(childCount, snapshot.Root.Children.Count);
        Equal("app.0", snapshot.Root.Children[0].Id);
        Equal("app.255", snapshot.Root.Children[^1].Id);
        Equal("app.1", snapshot.Root.Children[0].Focus!.Right);
        Equal("app.0", snapshot.Root.Children[1].Focus!.Left);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        return Task.CompletedTask;
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
