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
            var content = panel == "setup" ? Setup(state) : panel == "player" ? Player(state, "main") : Browse(state);
            var entry = EntryFocus(state);
            var group = UI.Stack(ContentGroupId, content).Classes(compact ? "music-panel-body" : "music-body");
            if (!entry.StartsWith("tab.", StringComparison.Ordinal)) group = group.RememberChildFocus(entry);
            var children = new List<WidgetElement> { Header(panel) };
            if (!compact) children.Add(TabsBar(entry));
            children.Add(group);
            var status = state.Player.Error ?? _status;
            if (!string.IsNullOrWhiteSpace(status) && status != "Loading…" && !_signingIn)
                children.Add(UI.Text(status, "music.status").Classes("music-status"));
            if (!compact)
            {
                if (state.Current is not null) children.Add(MiniPlayer(state));
                children.Add(UI.Row("music.help",
                    UI.ControllerHint(ControllerButton.A, "Play / open", "hint.activate"),
                    UI.ControllerHint(ControllerButton.Menu, "Song options", "hint.options"),
                    UI.ControllerHint(ControllerButton.Y, "Settings", "hint.settings")).Classes("music-hints"));
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
                root = root.Shortcut(ControllerButton.X, "player.toggle", label: "Play or pause")
                    .Shortcut(ControllerButton.LeftBumper, "player.previous", label: "Previous song")
                    .Shortcut(ControllerButton.RightBumper, "player.next", label: "Next song");

            var pinned = UI.Stack("music.pinned", Player(state, "pinned"))
                .InputScope("music.pinned").Classes("music-root");
            return new(root, InitialFocusId: entry, Surface: new()
            {
                Mode = WidgetSurfaceMode.Adaptive, PreferredWidth = compact ? 760 : 960,
                PreferredHeight = compact ? 440 : 680, MinimumWidth = 600, MinimumHeight = compact ? 360 : 440,
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
            UI.Text(panel == "setup" ? "YouTube Music · Settings" : panel == "player" ? "Now playing" : "YouTube Music", "music.title")
                .Classes("music-title", "music-grow"), action).Classes("music-header");
    }

    private WidgetElement TabsBar(string entry)
    {
        var buttons = new List<WidgetElement> { UI.ControllerHint(ControllerButton.LeftTrigger, "Previous", "tabs.previous") };
        for (var index = 0; index < Tabs.Length; index++)
        {
            var tab = Tabs[index];
            buttons.Add(UI.Button(Title(tab), "tab." + tab, "tab." + tab).Selected(tab == _tab)
                .FocusLeft("tab." + Tabs[Math.Max(0, index - 1)])
                .FocusRight("tab." + Tabs[Math.Min(Tabs.Length - 1, index + 1)])
                .FocusDown(entry).FocusUp("settings.open").Classes("music-tab"));
        }
        buttons.Add(UI.ControllerHint(ControllerButton.RightTrigger, "Next", "tabs.next"));
        return UI.Row("music.tabs", buttons.ToArray()).Classes("music-tabs");
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
                else if (state.Current is not null) row = row.FocusDown("player.open");
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

    private string TopEntry(MusicState state) => _tab == "search" ? "search" : _tab == "library" ? "library.playlists" : "tab." + _tab;

    private string EntryFocus(MusicState state)
    {
        if (_panel == "setup") return "setup.primary";
        if (_panel == "player") return state.Current is null ? "player.main.empty" : "player.main.toggle";
        var items = _tab == "queue" ? state.Queue : _page.Items;
        if (_panelReturnFocus is { } remembered && remembered.StartsWith("item.", StringComparison.Ordinal) &&
            int.TryParse(remembered[5..], out var index) && index >= _offset && index < Math.Min(_offset + PageSize, items.Count)) return remembered;
        if (_tab == "search") return "search";
        if (_tab == "library" && !state.Connected) return "connect.prompt";
        if (_history.Count != 0 && items.Count == 0) return "back";
        if (items.Count > _offset) return "item." + _offset;
        if (_tab == "library") return "library.playlists";
        if (!_loading) return "empty.search";
        return "tab." + _tab;
    }

    private static WidgetElement MiniPlayer(MusicState state)
    {
        var current = state.Current!;
        return UI.ActionSurface("player.open", "player.open", "Playback controls. " + current.Title, ActionSurfaceOrientation.Horizontal,
            UI.Icon(state.Player.Playing ? WidgetGlyph.Pause : WidgetGlyph.Play, "mini.state", state.Player.Playing ? "Playing" : "Paused").Classes("music-mini-icon"),
            UI.Stack("mini.copy", UI.Text(current.Title, "mini.title").Classes("music-track-title"),
                UI.Text(state.Player.Buffering ? "Buffering…" : current.Subtitle, "mini.artist").Classes("music-muted")).Classes("music-grow"),
            UI.ControllerHint(ControllerButton.X, "Play / pause", "mini.toggle.hint"),
            UI.Icon(WidgetGlyph.Settings, "mini.controls", "Open playback controls").Classes("music-mini-icon"))
            .Classes("music-mini-player");
    }

    private static WidgetElement Player(MusicState state, string mode)
    {
        var prefix = "player." + mode;
        var current = state.Current;
        if (current is null)
            return UI.Button("Browse music", "tab.home", prefix + ".empty").Classes("music-primary");
        var p = state.Player;
        var controls = UI.Row(prefix + ".controls",
            UI.Button("", "player.previous", prefix + ".previous").Icon(WidgetGlyph.Previous, "Previous song"),
            UI.Button("", "player.toggle", prefix + ".toggle").Icon(p.Playing ? WidgetGlyph.Pause : WidgetGlyph.Play, p.Playing ? "Pause" : "Play").Classes("music-primary"),
            UI.Button("", "player.next", prefix + ".next").Icon(WidgetGlyph.Next, "Next song"),
            UI.Button("Shuffle", "player.shuffle", prefix + ".shuffle").Selected(state.Shuffle),
            UI.Button("Repeat: " + state.Repeat, "player.repeat", prefix + ".repeat")).Classes("music-controls");
        var content = new List<WidgetElement>
        {
            UI.Text(current.Title, prefix + ".title").Classes("music-track-title"),
            UI.Text(p.Buffering ? "Buffering…" : current.Subtitle, prefix + ".artist").Classes("music-muted"), controls,
            UI.Text(Time(p.Position) + " / " + Time(p.Duration), prefix + ".time").Classes("music-muted"),
            UI.Slider(Math.Clamp(p.Position, 0, Math.Max(1, p.Duration)), 0, Math.Max(1, p.Duration), Math.Min(10, Math.Max(1, p.Duration)),
                "player.seek", prefix + ".seek", "Playback position", Time(p.Position) + " / " + Time(p.Duration))
                .RequireControllerActivation().Disabled(p.Duration <= 0),
            UI.Row(prefix + ".volume.row", UI.Icon(WidgetGlyph.Volume, prefix + ".volume.icon", "Volume").Classes("music-mini-icon"),
                UI.Slider(p.Volume, 0, 1, .05, "player.volume", prefix + ".volume", "Volume", Math.Round(p.Volume * 100) + "%")
                    .RequireControllerActivation().Classes("music-grow")).Classes("music-controls"),
        };
        return UI.VerticalScroll(prefix + ".scroll", UI.Stack(prefix, content.ToArray()).Classes("music-player")).Classes("music-panel-scroll");
    }

    private static TileArtwork Artwork(MusicItem item) => item.Artwork.StartsWith("https://", StringComparison.Ordinal)
        ? TileArtwork.FromHttps(item.Artwork, item.Title) : TileArtwork.FromGlyph(WidgetGlyph.Music, item.Title);
    private static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    private static string Time(double seconds) => $"{(int)Math.Max(0, seconds) / 60}:{(int)Math.Max(0, seconds) % 60:00}";
}
