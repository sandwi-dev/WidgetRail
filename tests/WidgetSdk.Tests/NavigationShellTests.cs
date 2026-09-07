using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class NavigationShellTests
{
    internal static Task LegacyOverloadsMatchFrozenPreExtractionTrees()
    {
        var sevenShell = CreateShell();
        var seven = sevenShell.ToProtocolNode();
        AssertFrozenLegacyTree(seven, adorned: false);
        AssertFullSnapshotMatchesFrozenPreExtraction(sevenShell, adorned: false);

        var nineShell = UI.NavigationShell(
            "shell",
            "library",
            "page.open",
            Content(),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play",
            UI.ControllerHint(ControllerButton.LeftBumper, "Previous", "hint.previous"),
            UI.ControllerHint(ControllerButton.RightBumper, "Next", "hint.next"));
        var nine = nineShell.ToProtocolNode();
        AssertFrozenLegacyTree(nine, adorned: true);
        AssertFullSnapshotMatchesFrozenPreExtraction(nineShell, adorned: true);
        return Task.CompletedTask;
    }

    internal static Task NamedPartsComposeOneScopedResponsiveContentTree()
    {
        var parts = UI.NavigationShellParts(
            "shell",
            "library",
            "page.open",
            Content(),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play",
            UI.ControllerHint(ControllerButton.LeftBumper, "Previous", "hint.previous"),
            UI.ControllerHint(ControllerButton.RightBumper, "Next", "hint.next"));

        var compactPart = parts.CompactNavigation.ToProtocolNode();
        var bodyPart = parts.Body.ToProtocolNode();
        Equal("shell.compact", compactPart.Id);
        Equal(ResponsiveVisibility.CompactOnly, compactPart.VisibleWhen);
        Equal("shell.body", bodyPart.Id);
        Equal(null, compactPart.InputScopeId);
        Equal(null, bodyPart.InputScopeId);

        var header = UI.Row(
            "custom.header",
            parts.CompactNavigation,
            UI.ControllerHint(ControllerButton.Y, "Settings", "custom.settings"));
        var root = UI.Stack("custom.root", header, parts.Body)
            .InputScope("custom.root");
        var snapshot = new WidgetView(
                root,
                "page.open",
                ActiveInputScopeId: "custom.root")
            .CreateSnapshot("navigation.parts", 1);

        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Equal(1, Descendants(snapshot.Root).Count(node => node.Id == "page.content"));
        Equal("custom.header", snapshot.Root.Children[0].Id);
        Equal("shell.compact", snapshot.Root.Children[0].Children[0].Id);
        Equal("custom.settings", snapshot.Root.Children[0].Children[1].Id);
        Equal("shell.body", snapshot.Root.Children[1].Id);
        Equal(ResponsiveVisibility.ExpandedOnly,
            Find(snapshot.Root, "shell.rail").VisibleWhen);
        Equal("page.open", Find(snapshot.Root, ExpectedKeyedId("compact", "library")).Focus!.Down);
        Equal("pane.play", Find(snapshot.Root, ExpectedKeyedId("rail", "library")).Focus!.Right);

        Throws<ArgumentException>(() => UI.NavigationShellParts(
            "interactive-parts",
            "library",
            "page.open",
            Content(),
            Destinations(),
            compactLeadingAdornment: UI.Button("Open", "open", "interactive.open")));
        Throws<ArgumentException>(() => UI.NavigationShellParts(
            "missing-pane",
            "library",
            "page.open",
            Content(),
            Destinations(),
            expandedPaneEntryFocusId: "pane.play"));
        return Task.CompletedTask;
    }

    internal static Task TypedAvailableEntryMatchesLegacyOutput()
    {
        var legacy = UI.NavigationShell(
            "shell",
            "library",
            "page.open",
            Content(),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play",
            UI.ControllerHint(ControllerButton.LeftBumper, "Previous", "hint.previous"),
            UI.ControllerHint(ControllerButton.RightBumper, "Next", "hint.next"));
        var typed = UI.NavigationShell(
            "shell",
            "library",
            NavigationShellContentEntry.Available("page.open"),
            Content(),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play",
            UI.ControllerHint(ControllerButton.LeftBumper, "Previous", "hint.previous"),
            UI.ControllerHint(ControllerButton.RightBumper, "Next", "hint.next"));

        var legacySnapshot = new WidgetView(legacy, "page.open")
            .CreateSnapshot("navigation.available", 1);
        var typedSnapshot = new WidgetView(typed, "page.open")
            .CreateSnapshot("navigation.available", 1);
        True(SnapshotJson.Serialize(legacySnapshot).AsSpan()
                .SequenceEqual(SnapshotJson.Serialize(typedSnapshot)),
            "An available typed content entry must preserve legacy serialized output.");
        return Task.CompletedTask;
    }

    internal static Task UnavailableEntryOmitsOnlyMissingContentEdges()
    {
        var unavailableShell = UI.NavigationShell(
            "shell",
            "library",
            NavigationShellContentEntry.Unavailable,
            UI.Stack("page.content", UI.LoadingIndicator("page.loading", "Loading")),
            Destinations());
        var unavailableSnapshot = new WidgetView(unavailableShell)
            .CreateSnapshot("navigation.unavailable", 1);
        Equal(0, ViewSnapshotValidator.Validate(unavailableSnapshot).Count);
        var unavailable = unavailableSnapshot.Root;
        var compact = Find(unavailable, "shell.compact").Children;
        var rail = Find(unavailable, "shell.rail").Children;

        for (var index = 0; index < compact.Count; index++)
        {
            Equal(null, compact[index].Focus!.Down);
            Equal(compact[(index - 1 + compact.Count) % compact.Count].Id,
                compact[index].Focus!.Left);
            Equal(compact[(index + 1) % compact.Count].Id,
                compact[index].Focus!.Right);
            Equal(null, rail[index].Focus!.Right);
            Equal(rail[(index - 1 + rail.Count) % rail.Count].Id,
                rail[index].Focus!.Up);
            Equal(rail[(index + 1) % rail.Count].Id,
                rail[index].Focus!.Down);
            True(!string.IsNullOrWhiteSpace(compact[index].FocusPersistenceId),
                "Unavailable content must preserve logical destination focus persistence.");
        }

        var withPane = UI.NavigationShellParts(
            "shell",
            "library",
            NavigationShellContentEntry.Unavailable,
            UI.Stack("page.content", UI.LoadingIndicator("page.loading", "Loading")),
            Destinations(),
            UI.Stack("pane", UI.Button("Play", "play", "pane.play")),
            "pane.play");
        var paneCompact = withPane.CompactNavigation.ToProtocolNode().Children;
        var paneRail = Find(withPane.Body.ToProtocolNode(), "shell.rail").Children;
        True(paneCompact.All(node => node.Focus!.Down is null),
            "An unavailable content entry must not author compact Down edges.");
        True(paneRail.All(node => node.Focus!.Right == "pane.play"),
            "An independently available expanded pane must remain reachable.");
        return Task.CompletedTask;
    }

    internal static Task ContentEntryValueValidatesAvailableIdentifiers()
    {
        Throws<ArgumentException>(() => NavigationShellContentEntry.Available(""));
        Throws<ArgumentException>(() => NavigationShellContentEntry.Available("bad id"));
        Throws<ArgumentException>(() => UI.NavigationShell(
            "shell", "library", (string)null!, Content(), Destinations()));

        var defaultEntry = default(NavigationShellContentEntry);
        var shell = UI.NavigationShell(
            "shell", "library", defaultEntry,
            UI.Stack("page.content", UI.LoadingIndicator("page.loading", "Loading")),
            Destinations()).ToProtocolNode();
        True(Find(shell, "shell.compact").Children.All(node => node.Focus!.Down is null),
            "The default content-entry value must be unambiguously unavailable.");
        return Task.CompletedTask;
    }

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

    private static void AssertFrozenLegacyTree(ViewNode root, bool adorned)
    {
        Equal("shell", root.Id);
        Equal(ViewNodeKind.Stack, root.Kind);
        SequenceEqual(["wrail-navigation-shell"], root.StyleClasses);
        SequenceEqual(["shell.compact", "shell.body"],
            root.Children.Select(child => child.Id));

        var compact = root.Children[0];
        Equal(ViewNodeKind.Row, compact.Kind);
        Equal(ResponsiveVisibility.CompactOnly, compact.VisibleWhen);
        SequenceEqual(["wrail-navigation-shell__compact"], compact.StyleClasses);
        var expectedCompactIds = new List<string>();
        if (adorned) expectedCompactIds.Add("hint.previous");
        expectedCompactIds.AddRange(Destinations().Select(destination =>
            ExpectedKeyedId("compact", destination.Id)));
        if (adorned) expectedCompactIds.Add("hint.next");
        SequenceEqual(expectedCompactIds, compact.Children.Select(child => child.Id));

        var body = root.Children[1];
        Equal(ViewNodeKind.Row, body.Kind);
        SequenceEqual(["wrail-navigation-shell__body"], body.StyleClasses);
        SequenceEqual(["shell.rail", "shell.persistent", "shell.content"],
            body.Children.Select(child => child.Id));

        var rail = body.Children[0];
        Equal(ResponsiveVisibility.ExpandedOnly, rail.VisibleWhen);
        SequenceEqual(["wrail-navigation-shell__rail"], rail.StyleClasses);
        SequenceEqual(Destinations().Select(destination => ExpectedKeyedId("rail", destination.Id)),
            rail.Children.Select(child => child.Id));

        var destinations = Destinations();
        for (var index = 0; index < destinations.Length; index++)
        {
            var destination = destinations[index];
            var compactButton = Find(root, ExpectedKeyedId("compact", destination.Id));
            var railButton = Find(root, ExpectedKeyedId("rail", destination.Id));
            var selected = destination.Id == "library";
            Equal(destination.ActionId, compactButton.ActionId);
            Equal(destination.ActionId, railButton.ActionId);
            Equal(destination.Glyph, compactButton.Glyph);
            Equal(destination.Glyph, railButton.Glyph);
            Equal(selected ? true : null, compactButton.IsSelected);
            Equal(selected ? true : null, railButton.IsSelected);
            Equal(destination.IsDisabled ? true : null, compactButton.IsDisabled);
            Equal(destination.IsDisabled ? true : null, railButton.IsDisabled);
            Equal(ExpectedKeyedId("focus", destination.Id), compactButton.FocusPersistenceId);
            Equal(compactButton.FocusPersistenceId, railButton.FocusPersistenceId);
            Equal("page.open", compactButton.Focus!.Down);
            Equal("pane.play", railButton.Focus!.Right);
            Equal($"{destination.Label}, {(selected ? "Selected" : "Not selected")}",
                compactButton.AccessibilityLabel);
            SequenceEqual(
                [
                    "wrail-navigation-shell__compact-item",
                    selected
                        ? "wrail-navigation-shell__item--selected"
                        : "wrail-navigation-shell__item--idle",
                ],
                compactButton.StyleClasses);
        }

        var persistent = body.Children[1];
        Equal(ResponsiveVisibility.ExpandedOnly, persistent.VisibleWhen);
        SequenceEqual(["wrail-navigation-shell__persistent"], persistent.StyleClasses);
        SequenceEqual(["pane"], persistent.Children.Select(child => child.Id));
        var content = body.Children[2];
        SequenceEqual(["wrail-navigation-shell__content"], content.StyleClasses);
        SequenceEqual(["page.content"], content.Children.Select(child => child.Id));
    }

    private static void AssertFullSnapshotMatchesFrozenPreExtraction(
        StackElement actual,
        bool adorned)
    {
        var expected = FrozenPreExtractionShell(adorned);
        var expectedSnapshot = new WidgetView(expected, "page.open")
            .CreateSnapshot("navigation.legacy-frozen", 1);
        var actualSnapshot = new WidgetView(actual, "page.open")
            .CreateSnapshot("navigation.legacy-frozen", 1);
        var expectedJson = SnapshotJson.Serialize(expectedSnapshot);
        var actualJson = SnapshotJson.Serialize(actualSnapshot);
        if (!expectedJson.AsSpan().SequenceEqual(actualJson))
            throw new InvalidOperationException(
                $"Legacy {(adorned ? "nine" : "seven")}-argument NavigationShell output changed.");
    }

    private static StackElement FrozenPreExtractionShell(bool adorned)
    {
        var destinations = Destinations();
        var compactIds = destinations.Select(destination =>
            ExpectedKeyedId("compact", destination.Id)).ToArray();
        var railIds = destinations.Select(destination =>
            ExpectedKeyedId("rail", destination.Id)).ToArray();
        var focusIds = destinations.Select(destination =>
            ExpectedKeyedId("focus", destination.Id)).ToArray();
        var compactButtons = new ButtonElement[destinations.Length];
        var railButtons = new ButtonElement[destinations.Length];
        for (var index = 0; index < destinations.Length; index++)
        {
            var destination = destinations[index];
            var selected = destination.Id == "library";
            var state = selected ? "Selected" : "Not selected";
            compactButtons[index] = new ButtonElement(
                compactIds[index], destination.Label, destination.ActionId)
            {
                AccessibilityLabel = $"{destination.Label}, {state}",
                Glyph = destination.Glyph,
                IsSelected = selected ? true : null,
                IsDisabled = destination.IsDisabled ? true : null,
                FocusPersistenceId = focusIds[index],
                FocusNeighbors = new FocusNeighbors(
                    Down: "page.open",
                    Left: compactIds[(index - 1 + destinations.Length) % destinations.Length],
                    Right: compactIds[(index + 1) % destinations.Length]),
                RequiredStyleClasses =
                [
                    "wrail-navigation-shell__compact-item",
                    selected
                        ? "wrail-navigation-shell__item--selected"
                        : "wrail-navigation-shell__item--idle",
                ],
            };
            railButtons[index] = new ButtonElement(
                railIds[index], destination.Label, destination.ActionId)
            {
                AccessibilityLabel = $"{destination.Label}, {state}",
                Glyph = destination.Glyph,
                IsSelected = selected ? true : null,
                IsDisabled = destination.IsDisabled ? true : null,
                FocusPersistenceId = focusIds[index],
                FocusNeighbors = new FocusNeighbors(
                    Up: railIds[(index - 1 + destinations.Length) % destinations.Length],
                    Down: railIds[(index + 1) % destinations.Length],
                    Right: "pane.play"),
                RequiredStyleClasses =
                [
                    "wrail-navigation-shell__rail-item",
                    selected
                        ? "wrail-navigation-shell__item--selected"
                        : "wrail-navigation-shell__item--idle",
                ],
            };
        }

        var compactChildren = new List<WidgetElement>();
        if (adorned)
            compactChildren.Add(UI.ControllerHint(
                ControllerButton.LeftBumper, "Previous", "hint.previous"));
        compactChildren.AddRange(compactButtons);
        if (adorned)
            compactChildren.Add(UI.ControllerHint(
                ControllerButton.RightBumper, "Next", "hint.next"));
        var compact = new RowElement("shell.compact", compactChildren)
        {
            RequiredStyleClasses = ["wrail-navigation-shell__compact"],
        }.VisibleWhen(ResponsiveVisibility.CompactOnly);
        var rail = new StackElement("shell.rail", railButtons)
        {
            RequiredStyleClasses = ["wrail-navigation-shell__rail"],
        }.VisibleWhen(ResponsiveVisibility.ExpandedOnly);
        var persistent = new StackElement(
            "shell.persistent",
            [UI.Stack("pane", UI.Button("Play", "play", "pane.play"))])
        {
            RequiredStyleClasses = ["wrail-navigation-shell__persistent"],
        }.VisibleWhen(ResponsiveVisibility.ExpandedOnly);
        var content = new StackElement("shell.content", [Content()])
        {
            RequiredStyleClasses = ["wrail-navigation-shell__content"],
        };
        var body = new RowElement("shell.body", [rail, persistent, content])
        {
            RequiredStyleClasses = ["wrail-navigation-shell__body"],
        };
        return new StackElement("shell", [compact, body])
        {
            RequiredStyleClasses = ["wrail-navigation-shell"],
        };
    }

    private static string ExpectedKeyedId(string name, string durableKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(durableKey));
        return $"shell.{name}-{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected '[{string.Join(", ", expected)}]', got '[{string.Join(", ", actual)}]'.");
    }

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
