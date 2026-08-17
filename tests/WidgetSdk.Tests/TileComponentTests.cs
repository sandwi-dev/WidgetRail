using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TileComponentTests
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";

    internal static Task MediaTilesAreSemantic()
    {
        var tile = UI.MediaTile(
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
        var app = UI.AppTile(
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
        True(new[] { "wrail-action-surface", "wrail-tile", "wrail-media-tile" }
                .SequenceEqual(node.StyleClasses),
            "Media tile root classes changed.");
        True(new[] { "media.current.artwork", "media.current.content" }
                .SequenceEqual(node.Children.Select(child => child.Id)),
            "Media tile child order or stable IDs changed.");
        Equal("Like Whatever", Find(node, "media.current.title").Text);
        Equal("Craig Connelly", Find(node, "media.current.subtitle").Text);
        Equal("The Sound of Garuda · 4:14", Find(node, "media.current.metadata").Text);
        Equal("Playing", Find(node, "media.current.state").Text);
        Equal("State: Playing", Find(node, "media.current.state").AccessibilityLabel);
        Equal(WidgetGlyph.Music, Find(node, "media.current.artwork").Glyph);
        Equal(1, AllNodes(node).Count(candidate => candidate.IsFocusable));
        Equal(1, AllNodes(node).Count(candidate => candidate.ActionId is not null));
        return Task.CompletedTask;
    }

    internal static Task AppTilesAreSemantic()
    {
        var remote = UI.AppTile(
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
        var inline = UI.AppTile(
            "Terminal", "Running", "focus.terminal", "app.terminal",
            artwork: TileArtwork.FromInlinePng(Png, "Terminal icon", ImageFit.Contain));
        var snapshot = new WidgetView(
            UI.Stack("apps.root", remote, inline), InitialFocusId: remote.Id)
            .CreateSnapshot("apps.instance", 2);

        var remoteNode = Find(snapshot.Root, remote.Id);
        True(remoteNode.IsFocusable, "Disabled app tiles must remain focusable.");
        Equal(true, remoteNode.IsDisabled);
        Equal("Update available", Find(remoteNode, "app.game.state").Text);
        var remoteArt = Find(remoteNode, "app.game.artwork");
        Equal(ViewNodeKind.Image, remoteArt.Kind);
        Equal("https://cdn.example.test/icons/disaster-crew.png", remoteArt.ImageSource);
        Equal(ImageFit.Cover, remoteArt.ImageFit);
        True(remoteArt.StyleClasses.Contains("wrail-app-tile__artwork"),
            "App artwork lacks its semantic class.");

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
        Throws<ArgumentException>(() => UI.AppTile(
            "Application", " ", "launch", "bad.state"));
        Throws<ArgumentException>(() => UI.AppTile(
            new string('a', UI.MaximumTileTitleCharacters + 1),
            "Ready", "launch", "bad.title"));
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
            UI.AppTile("Game", "Ready", "routing.open", "routing.tile"),
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
