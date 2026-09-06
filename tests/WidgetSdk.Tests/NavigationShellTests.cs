using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class NavigationShellTests
{
    internal static Task ComposesOneResponsiveContentTree()
    {
        var shell = CreateShell();
        var snapshot = new WidgetView(shell, "page.open")
            .CreateSnapshot("navigation.instance", 1);

        Equal(ProtocolConstants.FocusPersistenceVersion, snapshot.ProtocolVersion);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Equal(1, Descendants(snapshot.Root).Count(node => node.Id == "page.content"));

        var compact = Find(snapshot.Root, "shell.compact");
        var rail = Find(snapshot.Root, "shell.rail");
        Equal(ResponsiveVisibility.CompactOnly, compact.VisibleWhen);
        Equal(ResponsiveVisibility.ExpandedOnly, rail.VisibleWhen);
        Equal(3, compact.Children.Count);
        Equal(3, rail.Children.Count);

        var compactLibrary = compact.Children.Single(node => node.IsSelected == true);
        var railLibrary = rail.Children.Single(node => node.IsSelected == true);
        Equal("nav.library", compactLibrary.ActionId);
        Equal("nav.library", railLibrary.ActionId);
        Equal(compactLibrary.FocusPersistenceId, railLibrary.FocusPersistenceId);
        True(!string.IsNullOrWhiteSpace(compactLibrary.FocusPersistenceId),
            "Responsive presentations must publish an explicit focus-persistence identity.");
        True(compactLibrary.Id != railLibrary.Id,
            "Responsive presentations must use distinct stable control IDs.");
        Equal("page.open", compactLibrary.Focus!.Down);
        Equal("pane.play", railLibrary.Focus!.Right);
        True(compactLibrary.AccessibilityLabel!.EndsWith(", Selected",
                StringComparison.Ordinal),
            "Selected state must not rely on color alone.");
        return Task.CompletedTask;
    }

    internal static Task PreservesControllerTraversal()
    {
        var snapshot = new WidgetView(CreateShell())
            .CreateSnapshot("navigation.traversal", 1);
        var compact = Find(snapshot.Root, "shell.compact").Children;
        var rail = Find(snapshot.Root, "shell.rail").Children;

        for (var index = 0; index < compact.Count; index++)
        {
            Equal(compact[(index + 1) % compact.Count].Id, compact[index].Focus!.Right);
            Equal(compact[(index - 1 + compact.Count) % compact.Count].Id,
                compact[index].Focus!.Left);
            Equal("page.open", compact[index].Focus!.Down);

            Equal(rail[(index + 1) % rail.Count].Id, rail[index].Focus!.Down);
            Equal(rail[(index - 1 + rail.Count) % rail.Count].Id,
                rail[index].Focus!.Up);
            Equal("pane.play", rail[index].Focus!.Right);
        }
        return Task.CompletedTask;
    }

    internal static Task CompactAdornmentsAreInputInertAndAdditive()
    {
        var legacy = new WidgetView(CreateShell())
            .CreateSnapshot("navigation.legacy", 1);
        var adornedShell = UI.NavigationShell(
            "shell",
            "library",
            "page.open",
            Content(),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play",
            UI.ControllerHint(ControllerButton.LeftBumper, "Previous", "hint.previous"),
            UI.ControllerHint(ControllerButton.RightBumper, "Next", "hint.next"));
        var adorned = new WidgetView(adornedShell, "page.open")
            .CreateSnapshot("navigation.adorned", 1);

        Equal(0, ViewSnapshotValidator.Validate(adorned).Count);
        var legacyCompact = Find(legacy.Root, "shell.compact").Children;
        var adornedCompact = Find(adorned.Root, "shell.compact").Children;
        Equal(legacyCompact.Count + 2, adornedCompact.Count);
        Equal("hint.previous", adornedCompact[0].Id);
        Equal("hint.next", adornedCompact[^1].Id);
        for (var index = 0; index < legacyCompact.Count; index++)
        {
            Equal(legacyCompact[index].Id, adornedCompact[index + 1].Id);
            Equal(legacyCompact[index].ActionId, adornedCompact[index + 1].ActionId);
            Equal(legacyCompact[index].Focus, adornedCompact[index + 1].Focus);
        }

        var legacyRail = Find(legacy.Root, "shell.rail").Children;
        var adornedRail = Find(adorned.Root, "shell.rail").Children;
        Equal(legacyRail.Count, adornedRail.Count);
        for (var index = 0; index < legacyRail.Count; index++)
        {
            Equal(legacyRail[index].Id, adornedRail[index].Id);
            Equal(legacyRail[index].ActionId, adornedRail[index].ActionId);
            Equal(legacyRail[index].Focus, adornedRail[index].Focus);
        }

        Throws<ArgumentException>(() => UI.NavigationShell(
            "interactive-adornment",
            "library",
            "page.open",
            Content(),
            Destinations(),
            expandedPane: null,
            expandedPaneEntryFocusId: null,
            compactLeadingAdornment: UI.Stack(
                "interactive-hint",
                UI.Button("Open", "open", "interactive-hint.open")),
            compactTrailingAdornment: null));
        return Task.CompletedTask;
    }

    internal static Task ValidatesAuthoringBounds()
    {
        var destinations = Destinations();
        Throws<ArgumentOutOfRangeException>(() => UI.NavigationShell(
            "shell", "library", "page.open", Content(), [destinations[0]]));
        Throws<ArgumentException>(() => UI.NavigationShell(
            "shell", "missing", "page.open", Content(), destinations));
        Throws<ArgumentException>(() => UI.NavigationShell(
            "shell", "library", "page.open", Content(),
            [destinations[0], destinations[0]]));
        var sharedAction = UI.NavigationShell(
            "shared-action-shell", "home", "page.open", Content(),
            [
                destinations[0],
                destinations[1] with { ActionId = destinations[0].ActionId },
            ]).ToProtocolNode();
        var compactShared = Find(sharedAction, "shared-action-shell.compact").Children;
        Equal(compactShared[0].ActionId, compactShared[1].ActionId);
        True(compactShared[0].FocusPersistenceId != compactShared[1].FocusPersistenceId,
            "Shared actions must not collapse distinct logical focus destinations.");
        Throws<ArgumentException>(() => UI.NavigationShell(
            "shell", "library", "page.open", Content(), destinations,
            expandedPaneEntryFocusId: "pane.play"));
        Throws<ArgumentOutOfRangeException>(() => UI.NavigationShell(
            "shell", "library", "page.open", Content(),
            Enumerable.Range(0, UI.MaximumNavigationShellDestinations + 1)
                .Select(index => new NavigationShellDestination(
                    $"destination-{index}", $"Destination {index}",
                    $"nav.destination-{index}", WidgetGlyph.Play))
                .ToArray()));
        return Task.CompletedTask;
    }

    internal static Task FocusPersistenceIsOptionalAndVersioned()
    {
        var legacy = new WidgetView(UI.Button("Play", "play", "player.play"))
            .CreateSnapshot("focus.legacy", 1);
        Equal(ProtocolConstants.BaselineVersion, legacy.ProtocolVersion);
        Equal(null, legacy.Root.FocusPersistenceId);
        var legacyJson = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(legacy));
        True(!legacyJson.Contains("focusPersistenceId", StringComparison.Ordinal),
            "Legacy snapshots must omit the optional focus-persistence field.");

        var persisted = new WidgetView(
                UI.Button("Play", "play", "player.play")
                    .PersistFocusAs("player.transport.play"))
            .CreateSnapshot("focus.persisted", 1);
        Equal(ProtocolConstants.FocusPersistenceVersion, persisted.ProtocolVersion);
        Equal("player.transport.play", persisted.Root.FocusPersistenceId);

        var downgraded = persisted with
        {
            ProtocolVersion = ProtocolConstants.FocusPersistenceVersion - 1,
        };
        True(ViewSnapshotValidator.Validate(downgraded).Any(error =>
                error.Path == "$.root.focusPersistenceId" &&
                error.Code == "feature_requires_version"),
            "Authored focus persistence must require protocol v13.");
        return Task.CompletedTask;
    }

    private static StackElement CreateShell() => UI.NavigationShell(
        "shell",
        "library",
        "page.open",
        Content(),
        Destinations(),
        UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
        "pane.play");

    private static StackElement Content() =>
        UI.Stack("page.content", UI.Button("Open", "open", "page.open"));

    private static NavigationShellDestination[] Destinations() =>
    [
        new("home", "Home", "nav.home", WidgetGlyph.Play),
        new("library", "Library", "nav.library", WidgetGlyph.Music),
        new("settings", "Settings", "nav.settings", WidgetGlyph.Settings,
            IsDisabled: true),
    ];

    private static ViewNode Find(ViewNode node, string id) =>
        Descendants(node).Single(candidate => candidate.Id == id);

    private static IEnumerable<ViewNode> Descendants(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
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
