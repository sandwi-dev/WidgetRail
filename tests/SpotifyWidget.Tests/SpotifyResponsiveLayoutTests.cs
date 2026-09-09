using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class SpotifyResponsiveLayoutTests
{
    internal static Task NamedHostEnvelopesFitAndPreserveFocus()
    {
        var theme = CompileTheme();
        var handles = new SpotifyPresentationHandleFixture();
        var snapshot = SpotifyPresentation.Render(
                ReadyState(), handles.Compact, handles.UpNext)
            .CreateSnapshot("spotify.responsive-fit", 1);

        Equal(ViewNodeKind.Row, Find(snapshot.Root, "spotify.connected.panes").Kind);
        var player = Find(snapshot.Root, "spotify.card");
        HasClass(player, "spotify-player-pane");
        HasClass(player, "spotify-player-card");
        Equal(1, Count(snapshot.Root, "spotify.card"));
        Equal(1, Count(snapshot.Root, "spotify.track-title"));
        Equal("Small Hours", Find(snapshot.Root, "spotify.track-title").Text);
        Equal("Northern Lines", Find(snapshot.Root, "spotify.track-subtitle").Text);
        Equal("Night Drive", Find(snapshot.Root, "spotify.context").Text);
        Equal("Spotify", Find(snapshot.Root, "spotify.attribution").Text);

        var compactNavigation = FindClass(snapshot.Root,
            "wrail-navigation-shell__compact");
        Equal(ResponsiveVisibility.Always, compactNavigation.VisibleWhen);
        Equal(5, compactNavigation.Children.Count);
        Equal(3, compactNavigation.Children.Count(child => child.ActionId is not null));
        Equal(2, compactNavigation.Children.Count(child => child.ActionId is null));
        Equal<string?>(null, Find(snapshot.Root,
            "spotify.section.previous.hint").ActionId);
        Equal<string?>(null, Find(snapshot.Root,
            "spotify.section.next.hint").ActionId);
        MissingAction(snapshot.Root, "spotify.nav.player");
        foreach (var action in new[]
                 {
                     "spotify.nav.queue", "spotify.nav.playlists", "spotify.nav.devices",
                 })
        {
            _ = FindAction(compactNavigation, action);
        }

        var paneStyle = Resolve(theme, "stack", "spotify.card",
            "spotify-player-card", "spotify-player-card-wide", "spotify-player-pane");
        Equal("320px", paneStyle.Get("width")?.Text);
        Equal("0", paneStyle.Get("flex-grow")?.Text);
        Equal("1", paneStyle.Get("flex-shrink")?.Text);
        var bodyStyle = Resolve(theme, "row", "spotify.connected.panes",
            "spotify-connected-panes");
        Equal("0px", bodyStyle.Get("flex-basis")?.Text);
        Equal("1", bodyStyle.Get("flex-grow")?.Text);
        Equal("1", bodyStyle.Get("flex-shrink")?.Text);
        return Task.CompletedTask;
    }

    private static SpotifyPresentationState ReadyState()
    {
        var playlist = new SpotifyPlaylistSummary(
            "playlist-one", "Night Drive", "Late-night focus",
            "https://i.scdn.co/image/playlist",
            "https://open.spotify.com/playlist/playlist-one",
            "spotify:playlist:playlist-one", "Listener", false, true, 1);
        var playlistItem = new SpotifyPlaylistCollectionItem(
            playlist, new WidgetCollectionItemKey("playlist.fixture"));
        var playlistSnapshot = new WidgetCursorResourceSnapshot<SpotifyPlaylistCollectionItem>(
            WidgetPagedResourceStatus.Ready, [playlistItem], null, null,
            playlistItem.Key, null, null, 1);
        var navigation = new WidgetNavigationSnapshot<SpotifyRoute>(
            SpotifyRoute.Playlists, SpotifyRoute.Playlists, 0,
            "spotify.window", null, null, 1, CancellationToken.None);
        return new(
            new(1, 1, 0, null), SpotifyWidgetViewState.Ready,
            new SpotifyPlaybackSummary(true, true, 60_000, 240_000, 1,
                SpotifyRepeatState.Off, false,
                new SpotifyPlaybackItemSummary(SpotifyPlaybackItemType.Track,
                    "Small Hours", "Northern Lines", "Night Drive",
                    "https://i.scdn.co/image/current", "spotify:track:current"),
                new(false, false, false, false, false, false, false, false), "Spotify"),
            null, "Playing Small Hours", null, 0, false, navigation,
            EmptyCursor<SpotifyMediaCollectionItem>(),
            new SpotifyCursorPresentation<SpotifyPlaylistCollectionItem>(
            playlistSnapshot, UI.VerticalScroll("spotify.playlists.scroll", []) with
            {
                CollectionAnchorKey = playlistItem.Key.Value,
            }),
            null, null, null, false, null, false, null);
    }

    private static WidgetCursorResourceSnapshot<T> EmptyCursor<T>() where T : notnull =>
        new(WidgetPagedResourceStatus.Ready, [], null, null, null, null, null, 0);

    private static WrssTheme CompileTheme()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "styles", "default.wrss"));
        var parsed = WrssParser.Parse(source, "styles/default.wrss");
        True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
        var compiled = WrssThemeCompiler.Compile([parsed.Document]);
        True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
        return compiled.Theme!;
    }

    private static WrssResolvedStyle Resolve(
        WrssTheme theme, string role, string id, params string[] classes) =>
        theme.Resolve(new WrssElement(role, id,
            new HashSet<string>(classes, StringComparer.Ordinal)))!;

    private static ViewNode Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        throw new InvalidOperationException($"Missing node '{id}'.");
    }

    private static ViewNode FindClass(ViewNode node, string value)
    {
        if (node.StyleClasses.Contains(value)) return node;
        foreach (var child in node.Children)
            try { return FindClass(child, value); }
            catch (InvalidOperationException) { }
        throw new InvalidOperationException($"Missing class '{value}'.");
    }

    private static ViewNode FindAction(ViewNode node, string action)
    {
        if (node.ActionId == action) return node;
        foreach (var child in node.Children)
            try { return FindAction(child, action); }
            catch (InvalidOperationException) { }
        throw new InvalidOperationException($"Missing action '{action}'.");
    }

    private static int Count(ViewNode node, string id) =>
        (node.Id == id ? 1 : 0) + node.Children.Sum(child => Count(child, id));

    private static void MissingAction(ViewNode node, string action)
    {
        True(node.ActionId != action, $"Unexpected action '{action}'.");
        foreach (var child in node.Children) MissingAction(child, action);
    }

    private static void HasClass(ViewNode node, string value) =>
        True(node.StyleClasses.Contains(value), $"Missing class '{value}'.");

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }
}
