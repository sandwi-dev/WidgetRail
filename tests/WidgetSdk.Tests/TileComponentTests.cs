using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TileComponentTests
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";

    internal static Task TilesAreSemantic()
    {
        var tile = UI.Tile(
                "Like Whatever",
                "Playing",
                "open.now-playing",
                "media.current",
                subtitle: "Craig Connelly",
                metadata: "The Sound of Garuda · 4:14",
                artwork: TileArtwork.FromGlyph(WidgetGlyph.Music, "Album artwork"))
            .FocusDown("app.discord")
            .Shortcut(ControllerButton.X, actionId: "toggle-playback")
            .Busy();
        var app = UI.Tile(
            "Discord", "Ready to launch", "launch.discord", "app.discord",
            artwork: TileArtwork.FromGlyph(WidgetGlyph.Play, "Discord icon"));
        var snapshot = new WidgetView(
            UI.Stack("tiles.root", tile, app),
            InitialFocusId: tile.Id).CreateSnapshot("tiles.instance", 1);

        Equal(ProtocolConstants.ActionSurfaceVersion, snapshot.ProtocolVersion);
        var node = Find(snapshot.Root, tile.Id);
        Equal(ViewNodeKind.ActionSurface, node.Kind);
        Equal(ActionSurfaceOrientation.Horizontal, node.ActionSurfaceOrientation);
        Equal("open.now-playing", node.ActionId);
        Equal(true, node.IsBusy);
        True(node.IsFocusable, "Busy tiles must remain controller-focusable.");
        Equal("app.discord", node.Focus!.Down);
        Equal("toggle-playback", node.Shortcuts.Single().ActionId);
        True(new[] { "wrail-action-surface", "wrail-tile" }
                .SequenceEqual(node.StyleClasses),
            "Tile root classes changed.");
        True(new[] { "media.current.artwork", "media.current.content" }
                .SequenceEqual(node.Children.Select(child => child.Id)),
            "Tile child order or stable IDs changed.");
        Equal("Like Whatever", Find(node, "media.current.title").Text);
        True(Find(node, "media.current.title").StyleClasses
                .SequenceEqual(["wrail-tile__title"]),
            "Tile title classes changed.");
        Equal("Craig Connelly", Find(node, "media.current.subtitle").Text);
        True(Find(node, "media.current.subtitle").StyleClasses
                .SequenceEqual(["wrail-tile__subtitle"]),
            "Tile subtitle classes changed.");
        Equal("The Sound of Garuda · 4:14", Find(node, "media.current.metadata").Text);
        True(Find(node, "media.current.metadata").StyleClasses
                .SequenceEqual(["wrail-tile__metadata"]),
            "Tile metadata classes changed.");
        Equal("Playing", Find(node, "media.current.state").Text);
        Equal("State: Playing", Find(node, "media.current.state").AccessibilityLabel);
        True(Find(node, "media.current.state").StyleClasses
                .SequenceEqual(["wrail-tile__state"]),
            "Tile state classes changed.");
        True(Find(node, "media.current.content").StyleClasses
                .SequenceEqual(["wrail-tile__content"]),
            "Tile content classes changed.");
        Equal(WidgetGlyph.Music, Find(node, "media.current.artwork").Glyph);
        Equal(1, AllNodes(node).Count(candidate => candidate.IsFocusable));
        Equal(1, AllNodes(node).Count(candidate => candidate.ActionId is not null));
        return Task.CompletedTask;
    }

    internal static Task TilesConstrainArtworkAndState()
    {
        var remote = UI.Tile(
                "Disaster Crew",
                "Update available",
                "launch.game",
                "app.game",
                subtitle: "Game",
                metadata: "Last played yesterday",
                artwork: TileArtwork.FromHttps(
                    "https://cdn.example.test/icons/disaster-crew.png",
                    "Disaster Crew icon"))
            .Disabled();
        var inline = UI.Tile(
            "Terminal", "Running", "focus.terminal", "app.terminal",
            artwork: TileArtwork.FromInlinePng(Png, "Terminal icon", ImageFit.Contain));
        var snapshot = new WidgetView(
            UI.Stack("apps.root", remote, inline), InitialFocusId: remote.Id)
            .CreateSnapshot("apps.instance", 2);

        var remoteNode = Find(snapshot.Root, remote.Id);
        True(remoteNode.IsFocusable, "Disabled tiles must remain focusable.");
        Equal(true, remoteNode.IsDisabled);
        Equal("Update available", Find(remoteNode, "app.game.state").Text);
        var remoteArt = Find(remoteNode, "app.game.artwork");
        Equal(ViewNodeKind.Image, remoteArt.Kind);
        Equal("https://cdn.example.test/icons/disaster-crew.png", remoteArt.ImageSource);
        Equal(ImageFit.Cover, remoteArt.ImageFit);
        True(remoteArt.StyleClasses.SequenceEqual(["wrail-tile__artwork"]),
            "Tile artwork classes changed.");

        var inlineArt = Find(snapshot.Root, "app.terminal.artwork");
        True(inlineArt.ImageSource!.StartsWith("data:image/png;base64,", StringComparison.Ordinal),
            "Inline artwork did not remain local.");
        Equal(ProtocolConstants.ActionSurfaceVersion, snapshot.ProtocolVersion);
        Throws<ArgumentException>(() => TileArtwork.FromHttps(
            "http://example.test/icon.png", "Unsafe icon"));
        Throws<ArgumentException>(() => TileArtwork.FromHttps(
            "https://user:secret@example.test/icon.png", "Credentialed icon"));
        Throws<ArgumentOutOfRangeException>(() => TileArtwork.FromGlyph(
            (WidgetGlyph)999, "Unknown icon"));
        Throws<ArgumentException>(() => UI.Tile(
            "Application", " ", "launch", "bad.state"));
        Throws<ArgumentException>(() => UI.Tile(
            new string('a', UI.MaximumTileTitleCharacters + 1),
            "Ready", "launch", "bad.title"));
        return Task.CompletedTask;
    }

    internal static Task PosterTilesAreBoundedAndVersioned()
    {
        const string fullTitle =
            "A complete accessible poster title that is intentionally longer than two visible lines";
        var poster = UI.PosterTile(
                fullTitle,
                "Available",
                "poster.open",
                "poster.card",
                subtitle: "Provider label",
                metadata: "Platform · 2026",
                artwork: TileArtwork.FromHttps(
                    "https://cdn.example.test/posters/sample.jpg",
                    "Sample poster artwork"))
            .Shortcut(ControllerButton.X, actionId: "poster.secondary")
            .Selected();
        var snapshot = new WidgetView(poster, InitialFocusId: poster.Id)
            .CreateSnapshot("poster.instance", 1);

        Equal(ProtocolConstants.PosterTileVersion, snapshot.ProtocolVersion);
        Equal(ViewNodeKind.ActionSurface, snapshot.Root.Kind);
        Equal(ActionSurfaceOrientation.Vertical, snapshot.Root.ActionSurfaceOrientation);
        Equal(ActionSurfacePresentation.Poster, snapshot.Root.ActionSurfacePresentation);
        Equal(fullTitle, snapshot.Root.AccessibilityLabel!.Split(", ")[1]);
        True(snapshot.Root.AccessibilityLabel.Contains(fullTitle, StringComparison.Ordinal),
            "Poster accessibility must retain complete unclamped title copy.");
        True(snapshot.Root.StyleClasses.SequenceEqual(
                ["wrail-action-surface", "wrail-poster-tile"]),
            "Poster root classes changed.");
        True(snapshot.Root.Children.Select(child => child.Id).SequenceEqual(
                ["poster.card.artwork", "poster.card.scrim"]),
            "Poster layering order or stable IDs changed.");
        Equal(ImageFit.Cover, Find(snapshot.Root, "poster.card.artwork").ImageFit);
        True(Find(snapshot.Root, "poster.card.artwork").StyleClasses
                .SequenceEqual(["wrail-poster-tile__artwork"]),
            "Poster artwork class changed.");
        True(Find(snapshot.Root, "poster.card.scrim").StyleClasses
                .SequenceEqual(["wrail-poster-tile__scrim"]),
            "Poster scrim class changed.");
        Equal(fullTitle, Find(snapshot.Root, "poster.card.title").Text);
        Equal("Platform · 2026", Find(snapshot.Root, "poster.card.metadata").Text);
        Equal("Available", Find(snapshot.Root, "poster.card.state").Text);
        Equal(1, AllNodes(snapshot.Root).Count(candidate => candidate.IsFocusable));
        Equal(1, AllNodes(snapshot.Root).Count(candidate => candidate.ActionId is not null));
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

        var withoutArtwork = UI.PosterTile(
            "No artwork", "Unavailable", "poster.open", "poster.fallback");
        var fallbackSnapshot = new WidgetView(withoutArtwork, InitialFocusId: withoutArtwork.Id)
            .CreateSnapshot("poster.fallback.instance", 1);
        Equal(1, fallbackSnapshot.Root.Children.Count);
        Equal("poster.fallback.scrim", fallbackSnapshot.Root.Children[0].Id);
        Equal(ProtocolConstants.PosterTileVersion, fallbackSnapshot.ProtocolVersion);

        var legacy = snapshot with { ProtocolVersion = ProtocolConstants.PosterTileVersion - 1 };
        True(ViewSnapshotValidator.Validate(legacy).Any(error =>
                error.Code == "feature_requires_version"),
            "Poster presentation must fail closed before protocol v37.");
        Throws<ArgumentException>(() => UI.PosterTile(
            "Glyph", "Ready", "poster.open", "poster.glyph",
            artwork: TileArtwork.FromGlyph(WidgetGlyph.Play, "Glyph")));
        Throws<ArgumentException>(() => UI.PosterTile(
            "Contain", "Ready", "poster.open", "poster.contain",
            artwork: TileArtwork.FromHttps(
                "https://cdn.example.test/poster.jpg", "Poster", ImageFit.Contain)));
        return Task.CompletedTask;
    }

    internal static Task ContextActionsAreBoundedAndVersioned()
    {
        var tile = UI.Tile(
                "Album", "Ready", "album.open", "album.tile")
            .ContextAction("album.queue", "Add to queue")
            .ContextAction(
                "album.remove", "Remove from library",
                WidgetContextActionStyle.Danger, disabled: true);
        var snapshot = new WidgetView(tile, InitialFocusId: tile.Id)
            .CreateSnapshot("context.instance", 1);

        Equal(ProtocolConstants.ContextActionsVersion, snapshot.ProtocolVersion);
        Equal(2, snapshot.Root.ContextActions.Count);
        Equal("album.queue", snapshot.Root.ContextActions[0].ActionId);
        Equal(WidgetContextActionStyle.Danger, snapshot.Root.ContextActions[1].Style);
        Equal(true, snapshot.Root.ContextActions[1].IsDisabled);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

        var boundarySnapshot = new WidgetView(
            UI.Stack(
                "context.root",
                UI.Text("Legacy content", "context.legacy"),
                tile),
            InitialFocusId: tile.Id).CreateSnapshot("context.boundary", 2);
        using (var document = JsonDocument.Parse(SnapshotJson.Serialize(boundarySnapshot)))
        {
            var root = document.RootElement.GetProperty("root");
            Equal(0, root.GetProperty("contextActions").GetArrayLength());
            Equal(0, root.GetProperty("children")[0]
                .GetProperty("contextActions").GetArrayLength());
            Equal(2, root.GetProperty("children")[1]
                .GetProperty("contextActions").GetArrayLength());
        }

        var legacy = snapshot with
        {
            ProtocolVersion = ProtocolConstants.ContextActionsVersion - 1,
        };
        True(ViewSnapshotValidator.Validate(legacy).Any(error =>
                error.Code == "feature_requires_version"),
            "Context actions must fail closed before protocol v34.");
        var duplicate = snapshot with
        {
            Root = snapshot.Root with
            {
                ContextActions =
                [
                    new("same", "First"),
                    new("same", "Second"),
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(duplicate).Any(error =>
                error.Code == "duplicate_context_action"),
            "Duplicate context action IDs must fail closed.");
        Throws<InvalidOperationException>(() => Enumerable.Range(
                0, ProtocolConstants.MaximumContextActionCount + 1)
            .Aggregate(tile with { ContextActions = [] },
                (current, index) => current.ContextAction(
                    $"action.{index}", $"Action {index}")));
        var hint = UI.ControllerHint(ControllerButton.Menu, "Menu", "menu.hint")
            .ContextMenu(ControllerButton.Menu, new WidgetContextAction("library.open", "Library"));
        var menu = new WidgetView(UI.Stack("menu.root", hint, tile.ContextMenuShortcut(ControllerButton.X)), tile.Id)
            .CreateSnapshot("menu.instance", 1);
        Equal(ProtocolConstants.ContextMenuTriggerVersion, menu.ProtocolVersion);
        True(PresentationPropertyMetadata.Impact(PresentationProperty.ContextMenuButton).HasFlag(PresentationPropertyImpact.Paint),
            "Changing a menu trigger must rebuild its visible popup anchor geometry.");
        Equal(0, ViewSnapshotValidator.Validate(menu).Count);
        True(!menu.Root.Children[0].IsFocusable, "A menu hint must stay outside controller focus traversal.");
        Equal(ControllerButton.Menu, menu.Root.Children[0].ContextMenuButton);
        Equal(ControllerButton.X, menu.Root.Children[1].ContextMenuButton);
        True(ViewSnapshotValidator.Validate(menu with { ProtocolVersion = 50 }).Count > 0, "Old hosts reject explicit menu triggers.");
        Throws<ArgumentOutOfRangeException>(() => tile.ContextMenuShortcut(ControllerButton.B));
        return Task.CompletedTask;
    }

    internal static async Task ActionSurfacesValidateAndRoute()
    {
        var surface = UI.ActionSurface(
                "open.details",
                "custom.surface",
                "Open network details, connected",
                ActionSurfaceOrientation.Vertical,
                UI.Text("Network", "custom.surface.title"),
                UI.Text("Connected", "custom.surface.state"))
            .Selected();
        var snapshot = new WidgetView(surface, InitialFocusId: surface.Id)
            .CreateSnapshot("surface.instance", 4);
        Equal(ProtocolConstants.ActionSurfaceVersion, snapshot.ProtocolVersion);
        Equal(ActionSurfaceOrientation.Vertical, snapshot.Root.ActionSurfaceOrientation);
        Equal(true, snapshot.Root.IsSelected);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

        var legacy = snapshot with
        {
            ProtocolVersion = ProtocolConstants.ActionSurfaceVersion - 1,
        };
        True(ViewSnapshotValidator.Validate(legacy).Any(error =>
                error.Code == "feature_requires_version"),
            "ActionSurface must fail closed on protocol v6.");
        var nestedAction = snapshot with
        {
            Root = snapshot.Root with
            {
                Children =
                [
                    snapshot.Root.Children[0] with
                    {
                        Kind = ViewNodeKind.Button,
                        ActionId = "nested.action",
                    },
                    snapshot.Root.Children[1],
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(nestedAction).Any(error =>
                error.Code == "interactive_action_surface_descendant"),
            "Nested actionable content must fail closed.");
        Throws<ArgumentException>(() => UI.ActionSurface(
            "bad", "bad.surface", "Bad surface", ActionSurfaceOrientation.Horizontal,
            UI.Button("Nested", "nested", "nested.button")));
        Throws<ArgumentException>(() => UI.ActionSurface(
            "empty", "empty.surface", "Empty surface", ActionSurfaceOrientation.Horizontal));
        Throws<ArgumentException>(() => UI.ActionSurface(
            "many", "many.surface", "Many children", ActionSurfaceOrientation.Horizontal,
            Enumerable.Range(0, ProtocolConstants.MaximumActionSurfaceDirectChildren + 1)
                .Select(index => UI.Text(index.ToString(), $"many.child.{index}"))
                .ToArray()));
        Throws<ArgumentException>(() => UI.ActionSurface(
            "large", "large.surface", "Large surface", ActionSurfaceOrientation.Horizontal,
            UI.Stack("large.content",
                Enumerable.Range(0, ProtocolConstants.MaximumActionSurfaceDescendants)
                    .Select(index => UI.Text(index.ToString(), $"large.child.{index}"))
                    .ToArray())));
        WidgetElement tooDeep = UI.Text("Deep", "deep.leaf");
        for (var depth = 0; depth < ProtocolConstants.MaximumActionSurfaceRelativeDepth; depth++)
            tooDeep = UI.Stack($"deep.level.{depth}", tooDeep);
        Throws<ArgumentException>(() => UI.ActionSurface(
            "deep", "deep.surface", "Deep surface", ActionSurfaceOrientation.Horizontal,
            tooDeep));

        var tooLargeWire = snapshot with
        {
            Root = snapshot.Root with
            {
                Children =
                [
                    new ViewNode
                    {
                        Id = "wire.large",
                        Kind = ViewNodeKind.Stack,
                        Children = Enumerable.Range(0, ProtocolConstants.MaximumActionSurfaceDescendants)
                            .Select(index => new ViewNode
                            {
                                Id = $"wire.large.{index}",
                                Kind = ViewNodeKind.Text,
                                Text = index.ToString(),
                            })
                            .ToArray(),
                    },
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(tooLargeWire).Any(error =>
                error.Code == "action_surface_too_large"),
            "Wire validation did not enforce the ActionSurface descendant bound.");
        var deepWireChild = new ViewNode
        {
            Id = "wire.deep.leaf",
            Kind = ViewNodeKind.Text,
            Text = "Deep",
        };
        for (var depth = 0; depth < ProtocolConstants.MaximumActionSurfaceRelativeDepth; depth++)
            deepWireChild = new ViewNode
            {
                Id = $"wire.deep.{depth}",
                Kind = ViewNodeKind.Stack,
                Children = [deepWireChild],
            };
        var tooDeepWire = snapshot with
        {
            Root = snapshot.Root with { Children = [deepWireChild] },
        };
        True(ViewSnapshotValidator.Validate(tooDeepWire).Any(error =>
                error.Code == "action_surface_too_deep"),
            "Wire validation did not enforce the ActionSurface relative-depth bound.");

        var widget = new TileRoutingWidget();
        var routed = widget.RenderSnapshot("routing.instance", 7);
        await widget.SetActiveAsync(true, CancellationToken.None);
        var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "routing.tile",
            SnapshotSequence: routed.Sequence,
            ActiveInputScopeId: routed.ActiveInputScopeId));
        True(handled, "A did not activate the focused ActionSurface.");
        var action = await widget.NextActionAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Equal("routing.open", action.ActionId);
        Equal("routing.tile", action.SourceElementId);
    }

    private static IEnumerable<ViewNode> AllNodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in AllNodes(child))
            yield return descendant;
    }

    private static ViewNode Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Node '{id}' was not found.");
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

    private sealed class TileRoutingWidget : Widget
    {
        private readonly Channel<WidgetActionEvent> _actions = Channel.CreateUnbounded<WidgetActionEvent>();

        public override WidgetView Render() => new(
            UI.Tile("Game", "Ready", "routing.open", "routing.tile"),
            InitialFocusId: "routing.tile");

        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken = default)
        {
            _actions.Writer.TryWrite(action);
            return ValueTask.CompletedTask;
        }

        public ValueTask<WidgetActionEvent> NextActionAsync() => _actions.Reader.ReadAsync();
    }
}
