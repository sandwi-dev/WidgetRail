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
            var group = UI.Stack(ContentGroupId, content).Classes(compact ? "music-panel-body" : "music-body");
            if (!entry.StartsWith("music.nav.", StringComparison.Ordinal)) group = group.RememberChildFocus(entry);
            var children = new List<WidgetElement> { Header(panel) };
            if (compact) children.Add(group);
            else
            {
                var parts = UI.NavigationShellParts("music.nav", _tab, entry, group,
                    Tabs.Select(tab => new NavigationShellDestination(tab, Title(tab), "tab." + tab, WidgetGlyph.Music)).ToArray(),
                    compactLeadingAdornment: UI.Text("LT", "tabs.previous", "Left trigger, previous section").Classes("music-section-trigger"),
                    compactTrailingAdornment: UI.Text("RT", "tabs.next", "Right trigger, next section").Classes("music-section-trigger"));
                children.Add(UI.Row("music.navigation.header",
                    parts.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always).AddClasses("music-navigation-tabs"),
                    UI.ControllerHint(ControllerButton.Y, "Settings", "hint.settings").Classes("music-settings-hint"))
                    .Classes("music-navigation-header"));
                children.Add(UI.Row("music.panes", Player(state, "main", entry), group).Classes("music-panes"));
            }
            var status = state.Player.Error ?? _status;
            if (!string.IsNullOrWhiteSpace(status) && status != "Loading…" && !_signingIn)
                children.Add(UI.Text(status, "music.status").Classes("music-status"));
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
                root = root.Shortcut(ControllerButton.X, "player.toggle", label: "Play or pause")
                    .Shortcut(ControllerButton.LeftBumper, "player.previous", label: "Previous song")
                    .Shortcut(ControllerButton.RightBumper, "player.next", label: "Next song");

            var pinned = UI.Stack("music.pinned", Player(state, "pinned"))
                .InputScope("music.pinned").Classes("music-root");
            return new(root, InitialFocusId: entry, Surface: new()
            {
                Mode = WidgetSurfaceMode.Adaptive, PreferredWidth = compact ? 760 : 980,
                PreferredHeight = compact ? 440 : 700, MinimumWidth = 620, MinimumHeight = compact ? 360 : 400,
            })
            {
                FocusGroupEntryRequest = _focusRequest,
                QuickActions = state.Current is null ? [] :
                    [new(ControllerButton.X, "player.toggle", "Play or pause"), new(ControllerButton.LeftBumper, "player.previous", "Previous song"), new(ControllerButton.RightBumper, "player.next", "Next song")],
                PinnedLayouts = state.Current is null ? [] : [WidgetView.PinnedLayout("music.compact", "Compact now playing",
                    new() { Mode = WidgetSurfaceMode.Compact, PreferredWidth = 420, PreferredHeight = 330, MinimumWidth = 360, MinimumHeight = 300 }, pinned)],
            };
        }
    }

    private WidgetElement Header(string? panel)
    {
        var action = panel is null
            ? UI.Button("", "tab.setup", "settings.open").Icon(WidgetGlyph.Settings, "Settings — Y").Classes("music-icon-button")
            : UI.Button("Back", "panel.back", "panel.back").Shortcut(ControllerButton.B, "Back to music").Classes("music-secondary");
        return UI.Row("music.header",
            UI.Icon(WidgetIcon.PackageSvg("ytmusic.mark", WidgetPackageIconColorMode.OriginalColor, WidgetGlyph.Music),
                "music.brand", "YouTube Music").Classes("music-brand"),
            UI.Text(panel == "setup" ? "YouTube Music · Settings" : "YouTube Music", "music.title")
                .Classes("music-title", "music-grow"), action).Classes("music-header");
    }

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
        var items = _tab == "queue" ? state.Queue : _page.Items;
        var content = new List<WidgetElement>();
        if (_tab == "search")
            content.Add(UI.TextEntry(_query, "Search music — press A to type", "search", "search", 96).Classes("music-search"));
        if (_tab == "library")
            content.Add(UI.Row("library.filters", new[] { "playlists", "songs", "albums", "artists" }.Select(filter =>
                (WidgetElement)UI.Button(Title(filter), "library." + filter, "library." + filter)
                    .Selected(_value == filter).Classes("music-filter")).ToArray()).Classes("music-controls"));
        if (!state.Connected && _tab == "library")
        {
            content.Add(UI.Text("Your library", "page.title").Classes("music-section-title"));
            content.Add(UI.Text("Sign in to see your playlists and saved music.", "library.connect.copy").Classes("music-copy"));
            content.Add(UI.Button("Connect account", "tab.setup", "connect.prompt").Classes("music-primary"));
        }
        else
        {
            var heading = new List<WidgetElement>();
            if (_history.Count != 0) heading.Add(UI.Button("Back", "back", "back").Classes("music-secondary"));
            heading.Add(UI.Text(_tab == "queue" ? "Up next" : _page.Title, "page.title").Classes("music-section-title", "music-grow"));
            if (_loading) heading.Add(UI.LoadingIndicator("page.loading"));
            else if (_tab != "queue") heading.Add(UI.Button("", "refresh", "refresh").Icon(WidgetGlyph.Refresh, "Refresh this page").Classes("music-icon-button"));
            content.Add(UI.Row("page.heading", heading.ToArray()).Classes("music-controls"));
            var end = Math.Min(_offset + PageSize, items.Count);
            for (var index = _offset; index < end; index++)
            {
                var item = items[index];
                var playing = _tab == "queue" && index == state.Index;
                var row = UI.Tile(item.Title, playing ? "Now playing" : Title(item.Kind), "item." + index, "item." + index,
                        subtitle: string.IsNullOrWhiteSpace(item.Subtitle) ? null : item.Subtitle, artwork: Artwork(item),
                        accessibilityLabel: (item.Kind == "song" ? "Play " : "Open ") + item.Title + ". " + item.Subtitle)
                    .Selected(playing).Classes("music-track")
                    .FocusUp(index == _offset ? TopEntry(state) : "item." + (index - 1));
                if (index + 1 < end) row = row.FocusDown("item." + (index + 1));
                else if (items.Count > PageSize) row = row.FocusDown(_offset + PageSize < items.Count ? "page.next" : "page.previous");
                row = row.FocusLeft(state.Current is null ? "player.main.empty" : "player.main.toggle");
                if (item.Kind == "song") row = row.ContextMenuShortcut(ControllerButton.Menu)
                    .ContextAction("radio." + index, "Start radio");
                content.Add(row);
            }
            if (!_loading && items.Count == 0)
            {
                content.Add(UI.Icon(WidgetGlyph.Music, "empty.icon", "Music").Classes("music-empty-icon"));
                content.Add(UI.Text(_tab == "queue" ? "Your next songs appear here" : _tab == "search" && _query.Length == 0 ? "Find your next song" : "No music to show yet", "page.empty.title")
                    .Classes("music-section-title"));
                content.Add(UI.Text(_tab == "queue" ? "Play a song, or open its options and choose Start radio." : "Search for a song, artist, album or playlist.", "page.empty")
                    .Classes("music-muted"));
                if (_tab != "search") content.Add(UI.Button("Search music", "tab.search", "empty.search").Classes("music-primary"));
            }
            if (items.Count > PageSize)
            {
                var pages = new List<WidgetElement>();
                if (_offset > 0) pages.Add(UI.Button("Previous page", "page.previous", "page.previous").FocusUp("item." + _offset));
                pages.Add(UI.Text($"{_offset + 1}–{end} of {items.Count}", "page.count").Classes("music-muted", "music-grow"));
                if (end < items.Count) pages.Add(UI.Button("Next page", "page.next", "page.next").FocusUp("item." + (end - 1)));
                content.Add(UI.Row("page.controls", pages.ToArray()).Classes("music-controls"));
            }
        }
        return UI.VerticalScroll("music.scroll", content.ToArray()).Classes("music-scroll");
    }

    private string TopEntry(MusicState state) => _tab == "search" ? "search" : _tab == "library" ? "library.playlists" : NavigationFocusId;

    private string EntryFocus(MusicState state)
    {
        if (_panel == "setup") return "setup.primary";
        var items = _tab == "queue" ? state.Queue : _page.Items;
        if (_panelReturnFocus is { } remembered && remembered.StartsWith("item.", StringComparison.Ordinal) &&
            int.TryParse(remembered[5..], out var index) && index >= _offset && index < Math.Min(_offset + PageSize, items.Count)) return remembered;
        if (_tab == "search") return "search";
        if (_tab == "library" && !state.Connected) return "connect.prompt";
        if (_history.Count != 0 && items.Count == 0) return "back";
        if (items.Count > _offset) return "item." + _offset;
        if (_tab == "library") return "library.playlists";
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
            return UI.Stack(prefix,
                UI.Icon(WidgetGlyph.Music, prefix + ".icon", "Nothing playing").Classes("music-empty-icon"),
                UI.Text("Nothing playing", prefix + ".title").Classes("music-section-title"),
                UI.Text("Choose a song or start a radio.", prefix + ".copy").Classes("music-muted"),
                UI.Button("Search music", "tab.search", prefix + ".empty").Classes("music-secondary"))
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
        var content = new List<WidgetElement>();
        if (pane) content.Add(UI.Row(prefix + ".artwork.frame", artwork).Classes("music-artwork-frame"));
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
            .Classes("music-player", pane ? "music-player-pane" : "music-player-pinned");
    }

    private static TileArtwork Artwork(MusicItem item) => item.Artwork.StartsWith("https://", StringComparison.Ordinal)
        ? TileArtwork.FromHttps(item.Artwork, item.Title) : TileArtwork.FromGlyph(WidgetGlyph.Music, item.Title);
    private static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
