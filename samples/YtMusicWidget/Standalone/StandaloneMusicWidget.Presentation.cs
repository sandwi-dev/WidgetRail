using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class StandaloneMusicWidget
{
    public override WidgetView Render()
    {
        lock (_gate)
        {
            var state = _service.State;
            var panel = _panel;
            var compact = panel is not null;
            var content = panel == "setup" ? Setup(state) : Browse(state);
            var entry = EntryFocus(state);
            var group = UI.Stack(compact ? ContentGroupId : _browse.GroupId, content).Classes(compact ? "music-panel-body" : "music-body");
            if (group.Id == ContentGroupId && !entry.StartsWith("music.nav.", StringComparison.Ordinal))
                group = group.RememberChildFocus(entry);
            var status = state.Player.Error ?? state.PlaybackNotice ?? _status;
            if (string.IsNullOrWhiteSpace(status) || status == "Loading…" || _signingIn) status = string.Empty;
            var children = new List<WidgetElement> { Header(panel, status) };
            if (compact) children.Add(group);
            else
            {
                var parts = UI.NavigationShellParts("music.nav", _tab, entry, group,
                    Tabs.Select(tab => new NavigationShellDestination(tab, Title(tab), "tab." + tab, WidgetGlyph.Music)).ToArray(),
                    compactLeadingAdornment: CompactControllerKey(ControllerButton.LeftTrigger, "Left trigger, previous section", "tabs.previous"),
                    compactTrailingAdornment: CompactControllerKey(ControllerButton.RightTrigger, "Right trigger, next section", "tabs.next")).WithTransitions();
                children.Add(UI.Row("music.navigation.header",
                    parts.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always).AddClasses("music-navigation-tabs"),
                    UI.ControllerHint(ControllerButton.Y, "Settings", "hint.settings").Classes("music-settings-hint"))
                    .Classes("music-navigation-header"));
                children.Add(UI.Row("music.panes", Player(state, "main", entry),
                    group.TransitionContent("music.nav", _tab, Array.IndexOf(Tabs, _tab))).Classes("music-panes"));
            }
            var root = UI.Stack("music.root", children.ToArray()).InputScope("music.root").Classes("music-root");
            if (panel is not null) root = root.Shortcut(ControllerButton.B, "panel.back", label: "Back to music");
            else
            {
                root = root.Shortcut(ControllerButton.LeftTrigger, "tab.previous", label: "Previous tab")
                    .Shortcut(ControllerButton.RightTrigger, "tab.next", label: "Next tab")
                    .Shortcut(ControllerButton.Y, "tab.setup", label: "Settings");
                if (_history.Count != 0) root = root.Shortcut(ControllerButton.B, "back", label: "Back");
            }
            if (state.Current is not null)
                root = PlaybackShortcuts(root);

            var pinned = PlaybackShortcuts(UI.Stack("music.pinned", Player(state, "pinned"))
                .InputScope("music.pinned").Classes("music-root", "music-pinned-root"));
            return new(root, InitialFocusId: entry, Surface: new()
            {
                Mode = WidgetSurfaceMode.Adaptive, PreferredWidth = compact ? 760 : 840,
                PreferredHeight = compact ? 440 : 580, MinimumWidth = 620, MinimumHeight = compact ? 360 : 400,
            })
            {
                FocusGroupEntryRequest = _focusRequest,
                QuickActions = state.Current is null ? [] :
                    [new(ControllerButton.X, "player.toggle", "Play or pause"), new(ControllerButton.LeftBumper, "player.previous", "Previous song"), new(ControllerButton.RightBumper, "player.next", "Next song")],
                PinnedLayouts = state.Current is null ? [] : [WidgetView.PinnedLayout("music.compact", "Compact now playing",
                    new() { Mode = WidgetSurfaceMode.Compact, PreferredWidth = 360, PreferredHeight = 440, MinimumWidth = 320, MinimumHeight = 300 },
                    pinned, initialFocusId: "player.pinned.toggle")],
            };
        }
    }

    private static StackElement PlaybackShortcuts(StackElement root) =>
        root.Shortcut(ControllerButton.X, "player.toggle", label: "Play or pause")
            .Shortcut(ControllerButton.LeftBumper, "player.previous", label: "Previous song")
            .Shortcut(ControllerButton.RightBumper, "player.next", label: "Next song");

    private static WidgetElement Header(string? panel, string status)
    {
        var children = new List<WidgetElement>
        {
            UI.Icon(WidgetIcon.PackageSvg("ytmusic.mark", WidgetPackageIconColorMode.OriginalColor, WidgetGlyph.Music),
                "music.brand", "YouTube Music").Classes("music-brand"),
            UI.Text(panel == "setup" ? "YouTube Music · Settings" : "YouTube Music", "music.title")
                .Classes("music-title"),
            UI.Text(status, "music.status").Classes("music-status"),
        };
        if (panel is not null)
            children.Add(UI.Button("Back", "panel.back", "panel.back")
                .Shortcut(ControllerButton.B, "Back to music").Classes("music-secondary"));
        return UI.Row("music.header", children.ToArray()).Classes("music-header");
    }

    private static ControllerGlyphElement CompactControllerKey(
        ControllerButton button, string accessibilityLabel, string id) =>
        UI.ControllerGlyph(button, id, accessibilityLabel).Classes("music-section-trigger-key");

    private WidgetElement Setup(MusicState state)
    {
        var children = new List<WidgetElement>();
        if (_signingIn)
        {
            children.Add(UI.Row("setup.progress", UI.LoadingIndicator("setup.spinner"),
                UI.Text("Finish signing in", "setup.title").Classes("music-hero-title")).Classes("music-controls"));
            children.Add(UI.Text("Complete sign-in in the browser, then return to WidgetRail.", "setup.description").Classes("music-copy"));
            children.Add(UI.Button("Cancel sign-in", "signin.cancel", "setup.primary").Classes("music-primary"));
        }
        else
        {
            children.Add(UI.Icon(state.Connected ? WidgetGlyph.Check : WidgetGlyph.Music, "setup.icon",
                state.Connected ? "Connected" : "YouTube Music").Classes("music-hero-icon"));
            children.Add(UI.Text(state.Connected ? "Your library is connected" : "Your music, ready to play", "setup.title").Classes("music-hero-title"));
            children.Add(UI.Text(state.Connected ? "Browse your library or discover something new."
                : "Connect your account for your playlists, albums and saved songs.", "setup.description").Classes("music-copy"));
            children.Add(UI.Button(state.Connected ? "Browse library" : "Sign in with Google", state.Connected ? "tab.library" : "signin", "setup.primary")
                .Classes("music-primary"));
            var secondary = new List<WidgetElement>
            {
                UI.Button(state.Connected ? "Reconnect" : "Browse without signing in", state.Connected ? "signin" : "tab.home", "setup.secondary.action")
                    .Classes("music-secondary"),
            };
            if (state.Connected) secondary.Add(UI.Button("Disconnect", "disconnect", "disconnect").Classes("music-secondary"));
            children.Add(UI.Row("setup.secondary", secondary.ToArray()).Classes("music-controls"));
        }
        children.Add(UI.ControllerHint(ControllerButton.B, "Back to music", "setup.back.hint"));
        children.Add(UI.Text("Unofficial YouTube Music client", "setup.notice").Classes("music-muted"));
        return UI.VerticalScroll("setup.scroll", UI.Stack("setup.card", children.ToArray()).Classes("music-setup-card"))
            .Classes("music-panel-scroll");
    }

    private WidgetElement Browse(MusicState state)
    {
        IReadOnlyList<BrowseEntry> entries = _browse.Items;
        var content = new List<WidgetElement>();
        var rows = new List<WidgetElement>();
        if (_tab == "search")
            content.Add(UI.TextEntry(_query, "Search music — press A to type", "search", "search", 96).Classes("music-search"));
        if (_tab == "library" && _history.Count == 0)
        {
            var filters = new[] { "playlists", "songs", "albums", "artists" }.Select(filter =>
                (WidgetElement)UI.Button(Title(filter), "library." + filter, "library." + filter)
                    .Selected(_value == filter).Classes("music-filter")).ToArray();
            content.Add(UI.Row("library.toolbar",
                UI.Row("library.filters", filters).Classes("music-filters"),
                RefreshControl()).Classes("music-library-toolbar"));
        }
        if (!state.Connected && _tab == "library")
        {
            content.Add(UI.Text("Your library", "page.title").Classes("music-section-title"));
            content.Add(UI.Text("Sign in to see your playlists and saved music.", "library.connect.copy").Classes("music-copy"));
            content.Add(UI.Button("Connect account", "tab.setup", "connect.prompt").Classes("music-primary"));
        }
        else
        {
            var heading = new List<WidgetElement>();
            heading.Add(UI.Text(_tab == "queue" ? "Up next" : Page.Title, "page.title").Classes("music-section-title", "music-grow"));
            if (_tab != "library" || _history.Count != 0)
            {
                if (_loading || _tab != "queue") heading.Add(RefreshControl());
            }
            content.Add(UI.Row("page.heading", heading.ToArray()).Classes("music-controls"));
            if (!_loading && entries.Count == 0)
            {
                rows.Add(UI.Icon(WidgetGlyph.Music, "empty.icon", "Music").Classes("music-empty-icon"));
                rows.Add(UI.Text(_tab == "queue" ? "Your next songs appear here" : _tab == "search" && _query.Length == 0 ? "Find your next song" : "No music to show yet", "page.empty.title")
                    .Classes("music-section-title"));
                rows.Add(UI.Text(_tab == "queue" ? "Play a song, or open its options and choose Start radio." : "Search for a song, artist, album or playlist.", "page.empty")
                    .Classes("music-muted"));
                if (_tab != "search") rows.Add(UI.Button("Search music", "tab.search", "empty.search").Classes("music-primary"));
            }
        }
        if (entries.Count > 0 && (state.Connected || _tab != "library"))
        {
            var scroll = _kind == "home"
                ? UI.CollectionGrid(_browse.ScrollId, _browse.Collection, 130, 230, "Music recommendations", 4)
                    .Grouped(HomeGroups(entries))
                : UI.CollectionList(_browse.ScrollId, _browse.Collection, 90, "Music");
            content.Add(scroll.Classes("music-scroll"));
        }
        else
        {
            var empty = UI.VerticalScroll(_browse.ScrollId, rows.ToArray()).Classes("music-scroll");
            if (_kind == "library" && state.Connected && !_loading) empty = empty.RememberChildFocus("empty.search");
            content.Add(empty);
        }
        return UI.Stack("music.browse", content.ToArray()).Classes("music-browse");
    }

    private static IndexedCollectionGroup[] HomeGroups(IReadOnlyList<BrowseEntry> entries)
    {
        var groups = new List<IndexedCollectionGroup>();
        foreach (var entry in entries)
        {
            var title = string.IsNullOrWhiteSpace(entry.Item.Section) ? "Recommendations" : entry.Item.Section;
            if (groups.Count == 0 || groups[^1].Header != title)
                groups.Add(new("section." + entry.Index, title, 1));
            else groups[^1] = groups[^1] with { Count = groups[^1].Count + 1 };
        }
        return groups.ToArray();
    }

    // Every factory input is immutable and explicit, including focus neighbours
    // and playback state. Timer/status updates can reuse unchanged declarations.
    private sealed record BrowseRow(BrowseEntry Entry, bool Home, bool Playing, string? FocusUp, bool HasPlayer);

    private static WidgetElement RenderBrowseRow(BrowseRow input)
    {
        var item = input.Entry.Item;
        var index = input.Entry.Index;
        var subtitle = string.IsNullOrWhiteSpace(item.Subtitle) ? null : item.Subtitle;
        var label = (item.Kind == "song" ? "Play " : "Open ") + item.Title + ". " + item.Subtitle;
        var row = input.Home
            ? UI.PosterTile(item.Title, Title(item.Kind), "item." + index, "item." + index,
                subtitle: subtitle, artwork: item.Artwork.StartsWith("https://", StringComparison.Ordinal) ? Artwork(item) : null,
                accessibilityLabel: label).Classes("music-home-poster")
            : UI.Tile(item.Title, input.Playing ? "Now playing" : Title(item.Kind), "item." + index, "item." + index,
                subtitle: subtitle, artwork: Artwork(item), accessibilityLabel: label).Classes("music-track");
        row = row.Selected(input.Playing);
        if (input.FocusUp is not null) row = row.FocusUp(input.FocusUp);
        if (!input.Home && input.HasPlayer) row = row.FocusLeft("player.main.toggle");
        if (item.Kind is "song" or "playlist" or "album") row = row.ContextMenuShortcut(ControllerButton.Menu)
            .ContextAction("next." + index, "Play next");
        if (item.Kind == "song") row = row.ContextAction("radio." + index, "Start radio");
        return row;
    }

    private WidgetElement RefreshControl() => _loading
        ? UI.LoadingIndicator("page.loading")
        : UI.Button("", "refresh", "refresh").Icon(WidgetGlyph.Refresh, "Refresh this page").Classes("music-refresh");

    private string TopEntry(MusicState state) => _tab == "search" ? "search" : _tab == "library" && _history.Count == 0 ? "library." + _libraryFilter : NavigationFocusId;

    private string EntryFocus(MusicState state)
    {
        if (_panel == "setup") return "setup.primary";
        var entries = _browse.Items;
        if (_tab == "search") return "search";
        if (_tab == "library" && !state.Connected) return "connect.prompt";
        if (entries.Length > 0) return _browse.ScrollId;
        if (_tab == "library" && _history.Count == 0 && _loading) return "library." + _libraryFilter;
        if (!_loading) return "empty.search";
        return NavigationFocusId;
    }

    private string NavigationFocusId => WidgetIds.Scope("music.nav").KeyedId("compact", _tab);

    private static WidgetElement Player(MusicState state, string mode, string? adjacentFocus = null)
    {
        var prefix = "player." + mode;
        var pane = mode == "main";
        var current = state.Current;
        if (current is null)
            return UI.EmptyState("Nothing playing", "Choose a song or start a radio.", prefix + ".empty", glyph: WidgetGlyph.Music)
                .Classes("music-player", pane ? "music-player-pane" : "music-player-pinned");
        var p = state.Player;
        var toggle = UI.IconButton(p.Playing ? WidgetGlyph.Pause : WidgetGlyph.Play, "player.toggle", prefix + ".toggle",
                p.Playing ? "Pause" : "Play", IconButtonVariant.Primary, IconButtonSize.Large)
            .FocusLeft(prefix + ".previous").FocusRight(prefix + ".next").FocusUp(prefix + ".seek.slider").Classes("music-play");
        var repeat = UI.IconButton(WidgetGlyph.Repeat, "player.repeat", prefix + ".repeat", "Repeat: " + state.Repeat,
                size: IconButtonSize.Small).Selected(state.Repeat != "off")
            .FocusLeft(prefix + ".next").FocusUp(prefix + ".seek.slider").Classes("music-secondary-action");
        if (adjacentFocus is not null) repeat = repeat.FocusRight(adjacentFocus);
        var controls = UI.Row(prefix + ".controls",
            UI.IconButton(WidgetGlyph.Shuffle, "player.shuffle", prefix + ".shuffle", "Shuffle", size: IconButtonSize.Small)
                .Selected(state.Shuffle).FocusRight(prefix + ".previous").FocusUp(prefix + ".seek.slider").Classes("music-secondary-action"),
            UI.IconButton(WidgetGlyph.Previous, "player.previous", prefix + ".previous", "Previous song")
                .FocusLeft(prefix + ".shuffle").FocusRight(prefix + ".toggle").FocusUp(prefix + ".seek.slider").Classes("music-transport"),
            toggle,
            UI.IconButton(WidgetGlyph.Next, "player.next", prefix + ".next", "Next song")
                .FocusLeft(prefix + ".toggle").FocusRight(prefix + ".repeat").FocusUp(prefix + ".seek.slider").Classes("music-transport"),
            repeat).Classes("music-transport-row");
        WidgetElement artwork = current.Artwork.StartsWith("https://", StringComparison.Ordinal)
            ? UI.Image(current.Artwork, prefix + ".artwork", current.Title, ImageFit.Cover).Classes("music-artwork")
            : UI.Icon(WidgetGlyph.Music, prefix + ".artwork", "No artwork").Classes("music-artwork", "music-artwork-placeholder");
        if (!pane) artwork = artwork.AddClasses("music-pinned-artwork");
        var content = new List<WidgetElement>
        { UI.Row(prefix + ".artwork.frame", artwork).Classes("music-artwork-frame") };
        content.Add(UI.Text(current.Title, prefix + ".title").Classes("music-track-title"));
        content.Add(UI.Text(p.Buffering ? "Buffering…" : current.Subtitle, prefix + ".artist").Classes("music-player-subtitle"));
        content.Add(UI.Scrubber(TimeSpan.FromSeconds(Math.Clamp(p.Position, 0, Math.Max(1, p.Duration))),
                TimeSpan.FromSeconds(Math.Max(1, p.Duration)), TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, p.Duration))), "player.seek", prefix + ".seek", "Playback position")
            .RequireControllerActivation().Disabled(p.Duration <= 0).FocusDown(prefix + ".toggle").AddClasses("music-scrubber"));
        content.Add(controls);
        content.Add(UI.Row(prefix + ".volume.row",
            UI.Icon(WidgetGlyph.Volume, prefix + ".volume.icon", "Volume").Classes("music-volume-icon"),
            UI.Slider(p.Volume, 0, 1, .05, "player.volume", prefix + ".volume", "Volume", Math.Round(p.Volume * 100) + "%")
                .RequireControllerActivation().Classes("music-grow")).Classes("music-volume-row"));
        return UI.VerticalScroll(prefix + ".scroll", content.ToArray())
            .Classes("music-player", pane ? "music-player-pane" : "music-player-pinned") with { ShowScrollbar = false };
    }

    private static TileArtwork Artwork(MusicItem item) => item.Artwork.StartsWith("https://", StringComparison.Ordinal)
        ? TileArtwork.FromHttps(item.Artwork, item.Title) : TileArtwork.FromGlyph(WidgetGlyph.Music, item.Title);
    private static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
