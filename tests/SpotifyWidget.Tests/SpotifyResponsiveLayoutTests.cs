using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class SpotifyResponsiveLayoutTests
{
    private const double HeaderHeight = 64;
    private const double RootVerticalPadding = 28;
    private const double RootGap = 8;
    private const double ShellGap = 8;
    private const double NavigationWidth = 128;
    private const double PlayerHorizontalPadding = 20;

    internal static Task NamedHostEnvelopesFitAndPreserveFocus()
    {
        var theme = CompileTheme();
        var view = SpotifyPresentation.Render(ReadyState());
        var snapshot = view.CreateSnapshot("spotify.responsive-fit", 1);
        var wide = Find(snapshot.Root, "spotify.shell.wide");
        var compact = Find(snapshot.Root, "spotify.shell.compact");

        Equal(ResponsiveVisibility.ExpandedOnly, wide.VisibleWhen);
        Equal(ResponsiveVisibility.CompactOnly, compact.VisibleWhen);
        Equal(ViewNodeKind.Row, compact.Kind);
        Equal(ViewNodeKind.Stack, Find(compact, "spotify.navigation.compact").Kind);
        Equal(ViewNodeKind.Scroll, Find(compact, "spotify.player.compact.scroll").Kind);
        MissingClass(compact, "spotify-navigation-tabs");

        var scaleCases = new[] { 1D, 1.25D, 1.5D };
        var envelopes = new[]
        {
            new Envelope(620, 400, true),
            new Envelope(760, 440, true),
            new Envelope(978, 466, true),
            new Envelope(980, 560, false),
            new Envelope(1280, 720, false),
        };
        foreach (var scale in scaleCases)
        foreach (var envelope in envelopes)
            AssertEnvelope(theme, snapshot, envelope, scale);

        foreach (var destination in new[] { "player", "queue", "playlists", "devices" })
            AssertSharedPersistence(snapshot, $"spotify.nav.wide.{destination}",
                $"spotify.nav.compact.{destination}",
                $"spotify.destination.{destination}");
        AssertSharedPersistence(snapshot, "spotify.seek.slider",
            "spotify.player.compact.seek.slider", "spotify.transport.seek");
        AssertSharedPersistence(snapshot, "spotify.play-toggle",
            "spotify.player.compact.play-toggle", "spotify.transport.play-toggle");
        AssertLeftEdge(snapshot, "spotify.seek.slider", "spotify.nav.wide.player");
        AssertLeftEdge(snapshot, "spotify.player.compact.seek.slider",
            "spotify.nav.compact.player");
        return Task.CompletedTask;
    }

    private static void AssertEnvelope(
        WrssTheme theme, ViewSnapshot snapshot, Envelope envelope, double scale)
    {
        var compact = IsCompact(envelope.Width, envelope.Height);
        Equal(envelope.Compact, compact);
        var selected = compact
            ? Find(snapshot.Root, "spotify.shell.compact")
            : Find(snapshot.Root, "spotify.shell.wide");
        var rejected = compact
            ? Find(snapshot.Root, "spotify.shell.wide")
            : Find(snapshot.Root, "spotify.shell.compact");
        True(IsVisible(selected, compact), "Selected responsive branch is not visible.");
        True(!IsVisible(rejected, compact), "Inactive responsive branch remained visible.");

        var logicalBodyHeight = envelope.Height - RootVerticalPadding - HeaderHeight - RootGap;
        True(logicalBodyHeight > 0, "Host envelope leaves no Spotify body.");
        if (compact)
        {
            var paneWidth = envelope.Width - 32 - NavigationWidth - ShellGap;
            True(paneWidth - PlayerHorizontalPadding >= 420,
                $"Compact player controls have only {paneWidth - PlayerHorizontalPadding}px at {envelope.Width}x{envelope.Height}.");
            var scrollStyle = Resolve(theme, "scroll", "spotify.player.compact.scroll",
                "spotify-compact-player-scroll");
            Equal("1", scrollStyle.Get("flex-grow")?.Text);
            Equal("1", scrollStyle.Get("flex-shrink")?.Text);
            var card = Resolve(theme, "stack", "spotify.player.compact.card",
                "spotify-player-card", "spotify-player-card-compact");
            Equal("0", card.Get("flex-grow")?.Text);
            Equal("0", card.Get("flex-shrink")?.Text);
            var artwork = Resolve(theme, "stack", "spotify.player.compact.artwork-frame",
                "spotify-artwork-frame", "spotify-compact-artwork-frame");
            var details = Resolve(theme, "stack", "spotify.player.compact.details",
                "spotify-details", "spotify-compact-details");
            var scrubber = Resolve(theme, "scrubber", "spotify.player.compact.seek",
                "wrail-scrubber", "spotify-scrubber", "spotify-compact-scrubber");
            var controls = Resolve(theme, "row", "spotify.player.compact.controls",
                "spotify-primary-controls", "spotify-compact-primary-controls");
            var attribution = Resolve(theme, "text", "spotify.player.compact.attribution",
                "spotify-attribution", "spotify-compact-attribution");
            Equal(96D, Pixels(artwork.Get("height")));
            Equal(18D, Number(Resolve(theme, "text", "spotify.player.compact.title",
                "spotify-track-title", "spotify-compact-track-title").Get("font-size")));
            var intrinsicHeight = Pixels(artwork.Get("height")) +
                Pixels(details.Get("min-height")) +
                Pixels(scrubber.Get("min-height")) +
                Pixels(controls.Get("min-height")) +
                Pixels(attribution.Get("min-height")) + 20 + 4 * 6;
            True(intrinsicHeight <= logicalBodyHeight ||
                    Find(snapshot.Root, "spotify.player.compact.scroll").Kind ==
                        ViewNodeKind.Scroll,
                "Compact controls neither fit nor have one focus-revealing scroll owner.");
        }
        else
        {
            True(envelope.Width >= 960 && envelope.Height >= 540,
                "Expanded branch selected outside the host contract.");
            True(Find(snapshot.Root, "spotify.player.wide").Kind == ViewNodeKind.Stack,
                "Expanded player lost its persistent pane.");
        }

        var physicalWidth = envelope.Width * scale;
        var physicalHeight = envelope.Height * scale;
        True(Math.Abs(physicalWidth / scale - envelope.Width) < 0.001 &&
                Math.Abs(physicalHeight / scale - envelope.Height) < 0.001,
            "DPI conversion changed the logical responsive envelope.");
    }

    private static SpotifyPresentationState ReadyState() => new(
        new(1, 1, 1, null), SpotifyWidgetViewState.Ready,
        new SpotifyPlaybackSummary(true, true, 60_000, 240_000, 1,
            SpotifyRepeatState.Off, false,
            new SpotifyPlaybackItemSummary(SpotifyPlaybackItemType.Track,
                "Small Hours", "Northern Lines", "Night Drive",
                "https://i.scdn.co/image/current", "spotify:track:current"),
            new(false, false, false, false, false, false, false, false), "Spotify"),
        null, "Playing Small Hours", null, false, 0, SpotifyDestination.Player,
        EmptyCursor<SpotifyMediaCollectionItem>(),
        EmptyCursor<SpotifyPlaylistCollectionItem>(), null, null, null,
        false, null, "spotify.play-toggle");

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
            new HashSet<string>(classes, StringComparer.Ordinal)));

    private static double Pixels(WrssComputedValue? value)
    {
        if (value?.Unit == "px" && value.Number is { } number) return number;
        throw new InvalidOperationException($"Expected a pixel length; got '{value?.Text}'.");
    }

    private static double Number(WrssComputedValue? value) =>
        value?.Number ?? throw new InvalidOperationException(
            $"Expected a numeric value; got '{value?.Text}'.");

    private static bool IsCompact(double width, double height) => width < 960 || height < 540;

    private static bool IsVisible(ViewNode node, bool compact) =>
        node.VisibleWhen is null or ResponsiveVisibility.Always ||
        compact && node.VisibleWhen == ResponsiveVisibility.CompactOnly ||
        !compact && node.VisibleWhen == ResponsiveVisibility.ExpandedOnly;

    private static void AssertSharedPersistence(
        ViewSnapshot snapshot, string wideId, string compactId, string expected)
    {
        Equal(expected, Find(snapshot.Root, wideId).FocusPersistenceId);
        Equal(expected, Find(snapshot.Root, compactId).FocusPersistenceId);
    }

    private static void AssertLeftEdge(ViewSnapshot snapshot, string id, string expected) =>
        Equal(expected, Find(snapshot.Root, id).Focus?.Left);

    private static ViewNode Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        throw new InvalidOperationException($"Missing node '{id}'.");
    }

    private static void MissingClass(ViewNode node, string value)
    {
        True(!node.StyleClasses.Contains(value), $"Unexpected class '{value}'.");
        foreach (var child in node.Children) MissingClass(child, value);
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private readonly record struct Envelope(double Width, double Height, bool Compact);
}
