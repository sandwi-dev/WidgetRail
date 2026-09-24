using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

/// <summary>Native declarative presentation; credentials and stream URLs remain in the application.</summary>
public sealed class StandaloneMusicWidget : Widget
{
    private const int PageSize = 12;
    private readonly object _gate = new();
    private readonly IMusicService _service;
    private MusicPage _page = new("Home", []);
    private string _tab = "home", _kind = "home", _value = "", _query = "";
    private string _status = "Loading YouTube Music…";
    private int _offset;
    private bool _loading, _signingIn, _initialized;
    private long _pageGeneration;
    private readonly Stack<(string Kind, string Value)> _history = new();
    private static readonly string[] Tabs = ["home", "search", "library", "queue", "setup"];

    public StandaloneMusicWidget(IMusicService service)
    {
        _service = service;
        _service.Changed += ServiceChanged;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        if (!_initialized)
            Operations.RunSingleFlight("music.initialize", async context =>
            {
                try
                {
                    await _service.InitializeAsync(context.CancellationToken);
                    lock (_gate) { _initialized = true; _status = ""; }
                    LoadPage();
                }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                { if (context.IsCurrent) SetStatus("YouTube Music could not start. Open Setup or retry."); }
            }, WidgetOperationLifetime.Active);
        else if (_loading) LoadPage();
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        _service.Changed -= ServiceChanged;
        await _service.DisposeAsync().AsTask().WaitAsync(shutdownToken);
    }

    private void ServiceChanged()
    {
        lock (_gate)
            if (_tab == "queue")
                _offset = Math.Min(_offset, Math.Max(0, ((_service.State.Queue.Count - 1) / PageSize) * PageSize));
        if (IsActive) Invalidate();
    }
    private void SetStatus(string value) { lock (_gate) _status = value; Invalidate(); }

    private void LoadPage()
    {
        string kind, value;
        long generation;
        lock (_gate)
        {
            kind = _kind; value = _value; generation = ++_pageGeneration;
            _offset = 0;
            _page = new(kind == "search" ? "Search" : kind == "library" ? "Library" : "Home", []);
            _loading = kind is not ("queue" or "setup") && !(kind == "search" && value.Length == 0);
            if (!_loading) { Invalidate(); return; }
            _status = "Loading…";
        }
        Invalidate();
        Operations.RunLatest("music.page", async context =>
        {
            try
            {
                var page = await _service.BrowseAsync(kind, value, context.CancellationToken);
                lock (_gate)
                {
                    if (!context.IsCurrent || generation != _pageGeneration) return;
                    _page = page; _loading = false; _status = "";
                }
                Invalidate();
            }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                lock (_gate)
                {
                    if (!context.IsCurrent || generation != _pageGeneration) return;
                    _loading = false;
                    _status = "This page could not load. Retry, or reconnect in Setup.";
                }
                Invalidate();
            }
        }, WidgetOperationLifetime.Active);
    }

    private void SelectTab(string tab)
    {
        lock (_gate)
        {
            _tab = _kind = tab; _value = tab == "library" ? "playlists" : tab == "search" ? _query : "";
            _history.Clear();
        }
        LoadPage();
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (!IsActive || action.Phase != ControllerEventPhase.Pressed) return ValueTask.CompletedTask;
        var id = action.ActionId;
        if (id.StartsWith("tab.", StringComparison.Ordinal) && Tabs.Contains(id[4..])) SelectTab(id[4..]);
        else if (id is "tab.previous" or "tab.next")
        {
            var index = Array.IndexOf(Tabs, _tab);
            SelectTab(Tabs[(index + (id == "tab.next" ? 1 : Tabs.Length - 1)) % Tabs.Length]);
        }
        else if (id == "search")
        {
            lock (_gate) { _query = (action.CommittedText ?? "").Trim(); _kind = "search"; _value = _query; }
            LoadPage();
        }
        else if (id.StartsWith("library.", StringComparison.Ordinal) && id[8..] is "playlists" or "songs" or "albums" or "artists")
        {
            lock (_gate) { _kind = "library"; _value = id[8..]; _history.Clear(); }
            LoadPage();
        }
        else if (id == "back")
        {
            lock (_gate)
                if (_history.TryPop(out var previous)) { _kind = previous.Kind; _value = previous.Value; }
            LoadPage();
        }
        else if (id == "refresh") LoadPage();
        else if (id is "page.next" or "page.previous")
        {
            lock (_gate)
            {
                var count = _tab == "queue" ? _service.State.Queue.Count : _page.Items.Count;
                _offset = Math.Clamp(_offset + (id == "page.next" ? PageSize : -PageSize), 0, Math.Max(0, ((count - 1) / PageSize) * PageSize));
            }
            Invalidate();
        }
        else if (id == "signin")
        {
            Operations.RunSingleFlight("music.auth", async context =>
            {
                lock (_gate) { _signingIn = true; _status = "Complete sign-in in the browser, then return here."; }
                Invalidate();
                try { SetStatus(await _service.SignInAsync(context.CancellationToken)); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { SetStatus("Sign-in did not complete. Try again using Edge or Chrome."); }
                finally { lock (_gate) _signingIn = false; Invalidate(); }
            }, WidgetOperationLifetime.Widget);
        }
        else if (id == "disconnect") RunPlayback(token => _service.DisconnectAsync(token));
        else if (id.StartsWith("item.", StringComparison.Ordinal) || id.StartsWith("radio.", StringComparison.Ordinal))
        {
            var separator = id.IndexOf('.');
            if (!int.TryParse(id[(separator + 1)..], out var index)) return ValueTask.CompletedTask;
            MusicItem[] items;
            lock (_gate) items = (_tab == "queue" ? _service.State.Queue : _page.Items).ToArray();
            if (index < 0 || index >= items.Length) return ValueTask.CompletedTask;
            var item = items[index];
            if (id.StartsWith("radio.", StringComparison.Ordinal) && item.Kind == "song")
                RunPlayback(token => _service.RadioAsync(item, token));
            else if (_tab == "queue") RunPlayback(token => _service.CommandAsync("queue", index, token));
            else if (item.Kind == "song")
            {
                var tracks = items.Where(i => i.Kind == "song").ToArray();
                var selected = items.Take(index).Count(i => i.Kind == "song");
                RunPlayback(token => _service.PlayAsync(tracks, selected, token));
            }
            else
            {
                lock (_gate) { _history.Push((_kind, _value)); _kind = item.Kind; _value = item.Id; }
                LoadPage();
            }
        }
        else if (id.StartsWith("player.", StringComparison.Ordinal))
        {
            var command = id[7..];
            if (command is "previous" or "next") RunPlayback(token => _service.CommandAsync(command, null, token));
            else if (command is "toggle" or "shuffle" or "repeat" or "seek" or "volume")
            {
                // Transport remains responsive while catalogue requests and stream resolution are pending.
                Operations.RunSerial("music.transport", async context =>
                {
                    try { await _service.CommandAsync(command, action.RequestedValue, context.CancellationToken); }
                    catch (Exception error) when (error is IOException or OperationCanceledException) { SetStatus("Playback command failed. Try again."); }
                }, WidgetOperationLifetime.Widget);
            }
        }
        return ValueTask.CompletedTask;
    }

    private void RunPlayback(Func<CancellationToken, Task> run) => Operations.RunLatest("music.selection", async context =>
    {
        SetStatus("Preparing playback…");
        try { await run(context.CancellationToken); if (context.IsCurrent) SetStatus(""); }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        { if (context.IsCurrent) SetStatus("Playback could not start. Try again, or reconnect in Setup."); }
    }, WidgetOperationLifetime.Widget);

    public override WidgetView Render()
    {
        lock (_gate)
        {
            var state = _service.State;
            var items = _tab == "queue" ? state.Queue : _page.Items;
            var content = new List<WidgetElement>();
            if (_tab == "setup")
            {
                content.Add(UI.Text(state.Connected ? "Your account is connected" : "Connect YouTube Music", "setup.title").Classes("music-title"));
                content.Add(UI.Text("Sign in through a separate Edge or Chrome window to browse your library. Playback runs inside WidgetRail; YTMDesktop is not required.", "setup.description").Classes("music-description"));
                content.Add(UI.Button(_signingIn ? "Waiting for sign-in…" : state.Connected ? "Reconnect" : "Sign in", "signin", "signin").Disabled(_signingIn));
                content.Add(UI.Button("Disconnect and remove saved session", "disconnect", "disconnect").Disabled(!state.Connected || _signingIn));
                content.Add(UI.Text("Uses an unofficial YouTube Music integration. Availability can change when YouTube updates its service.", "setup.notice").Classes("music-muted"));
            }
            else
            {
                if (_tab == "search") content.Add(UI.TextEntry(_query, "Search songs, albums, artists and playlists", "search", "search", 96));
                if (_tab == "library")
                    content.Add(UI.Row("library.filters", new[] { "playlists", "songs", "albums", "artists" }.Select(filter =>
                        (WidgetElement)UI.Button(char.ToUpperInvariant(filter[0]) + filter[1..], "library." + filter, "library." + filter).Selected(_value == filter)).ToArray()).Classes("music-controls"));
                if (!state.Connected)
                    content.Add(UI.Button("Connect your account in Setup", "tab.setup", "connect.prompt"));
                var toolbar = new List<WidgetElement>();
                if (_history.Count != 0) toolbar.Add(UI.Button("Back", "back", "back"));
                toolbar.Add(UI.Text(_tab == "queue" ? "Queue" : _page.Title, "page.title").Classes("music-title", "music-grow"));
                toolbar.Add(UI.Button("Refresh", "refresh", "refresh").Disabled(_loading || _tab == "queue"));
                content.Add(UI.Row("page.header", toolbar.ToArray()).Classes("music-controls"));
                foreach (var (item, index) in items.Skip(_offset).Take(PageSize).Select((item, local) => (item, local + _offset)))
                {
                    var row = new List<WidgetElement>();
                    if (item.Artwork.StartsWith("https://", StringComparison.Ordinal))
                        row.Add(UI.Image(item.Artwork, "cover." + index, item.Title).Classes("music-cover"));
                    row.Add(UI.Button(item.Title + (item.Subtitle.Length == 0 ? "" : " · " + item.Subtitle), "item." + index, "item." + index)
                        .Selected(_tab == "queue" && index == state.Index).Classes("music-track"));
                    if (item.Kind == "song") row.Add(UI.Button("Start radio", "radio." + index, "radio." + index).Classes("music-radio"));
                    content.Add(UI.Row("row." + index, row.ToArray()).Classes("music-row"));
                }
                if (items.Count == 0 && !_loading) content.Add(UI.Text(_tab == "queue" ? "Play a song or start radio to build your queue." : "No items to show. Search for music or connect your account.", "page.empty").Classes("music-muted"));
                if (items.Count > PageSize)
                    content.Add(UI.Row("page.controls",
                        UI.Button("Previous", "page.previous", "page.previous").Disabled(_offset == 0),
                        UI.Text($"{_offset + 1}–{Math.Min(_offset + PageSize, items.Count)} of {items.Count}", "page.count"),
                        UI.Button("Next", "page.next", "page.next").Disabled(_offset + PageSize >= items.Count)).Classes("music-controls"));
            }
            var scroll = UI.VerticalScroll("music.scroll", content.ToArray()).Classes("music-scroll");
            NavigationShellDestination[] destinations = Tabs.Select(tab => new NavigationShellDestination(
                "destination." + tab, char.ToUpperInvariant(tab[0]) + tab[1..], "tab." + tab,
                tab == "setup" ? WidgetGlyph.Settings : WidgetGlyph.Music)).ToArray();
            var entry = _tab == "setup" ? (_signingIn ? null : "signin") :
                _tab == "search" ? "search" : _tab == "library" ? "library.playlists" :
                !state.Connected ? "connect.prompt" : _history.Count != 0 ? "back" :
                items.Count > _offset ? "item." + _offset : _tab != "queue" && !_loading ? "refresh" : null;
            var shell = UI.NavigationShell("music.navigation", "destination." + _tab,
                entry is null ? NavigationShellContentEntry.Unavailable : NavigationShellContentEntry.Available(entry),
                scroll, destinations).Classes("music-navigation");
            var root = UI.Stack("music.root",
                UI.Text("YouTube Music", "music.title").Classes("music-title"),
                UI.Text(state.Player.Error ?? _status, "music.status").Classes("music-muted"),
                shell, Player(state, "main"))
                .InputScope("music.root")
                .Shortcut(ControllerButton.LeftTrigger, "tab.previous", label: "Previous tab")
                .Shortcut(ControllerButton.RightTrigger, "tab.next", label: "Next tab")
                .Classes("music-root");
            if (_history.Count != 0) root = root.Shortcut(ControllerButton.B, "back", label: "Back");
            if (state.Current is not null)
                root = root.Shortcut(ControllerButton.X, "player.toggle", label: "Play or pause")
                    .Shortcut(ControllerButton.LeftBumper, "player.previous", label: "Previous song")
                    .Shortcut(ControllerButton.RightBumper, "player.next", label: "Next song");
            var pinned = UI.Stack("music.pinned", Player(state, "pinned")).InputScope("music.pinned").Classes("music-root");
            return new(root, Surface: new() { Mode = WidgetSurfaceMode.Adaptive, PreferredWidth = 960,
                PreferredHeight = 720, MinimumWidth = 600, MinimumHeight = 440 })
            {
                QuickActions = state.Current is null ? [] :
                    [new(ControllerButton.X, "player.toggle", "Play or pause"), new(ControllerButton.LeftBumper, "player.previous", "Previous song"), new(ControllerButton.RightBumper, "player.next", "Next song")],
                PinnedLayouts = [WidgetView.PinnedLayout("music.compact", "Compact now playing",
                    new() { Mode = WidgetSurfaceMode.Compact, PreferredWidth = 420, PreferredHeight = 300, MinimumWidth = 360, MinimumHeight = 260 }, pinned)],
            };
        }
    }

    private static WidgetElement Player(MusicState state, string mode)
    {
        var p = state.Player;
        var prefix = "player." + mode;
        var current = state.Current;
        var controls = UI.Row(prefix + ".controls",
            UI.Button("", "player.previous", prefix + ".previous").Icon(WidgetGlyph.Previous, "Previous song").Disabled(current is null),
            UI.Button("", "player.toggle", prefix + ".toggle").Icon(p.Playing ? WidgetGlyph.Pause : WidgetGlyph.Play, p.Playing ? "Pause" : "Play").Disabled(current is null),
            UI.Button("", "player.next", prefix + ".next").Icon(WidgetGlyph.Next, "Next song").Disabled(current is null),
            UI.Button("Shuffle", "player.shuffle", prefix + ".shuffle").Selected(state.Shuffle),
            UI.Button("Repeat: " + state.Repeat, "player.repeat", prefix + ".repeat")).Classes("music-controls");
        return UI.Stack(prefix,
            UI.Text(current?.Title ?? "Choose music to play", prefix + ".title").Classes("music-track-title"),
            UI.Text(p.Buffering ? "Buffering…" : current?.Subtitle ?? "", prefix + ".artist").Classes("music-muted"),
            controls,
            UI.Row(prefix + ".sliders",
                UI.Slider(Math.Clamp(p.Position, 0, Math.Max(1, p.Duration)), 0, Math.Max(1, p.Duration), Math.Min(10, Math.Max(1, p.Duration)), "player.seek", prefix + ".seek", "Playback position", Time(p.Position) + " / " + Time(p.Duration))
                    .RequireControllerActivation().Disabled(p.Duration <= 0).Classes("music-grow"),
                UI.Slider(p.Volume, 0, 1, .05, "player.volume", prefix + ".volume", "Volume", Math.Round(p.Volume * 100) + "%")
                    .RequireControllerActivation().Classes("music-volume")).Classes("music-controls")).Classes("music-player");
    }

    private static string Time(double seconds) => $"{(int)Math.Max(0, seconds) / 60}:{(int)Math.Max(0, seconds) % 60:00}";
}
