using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

/// <summary>Native declarative presentation; credentials and stream URLs remain in the application.</summary>
public sealed partial class StandaloneMusicWidget : Widget
{
    private const int PageSize = 24;
    private readonly object _gate = new();
    private readonly IMusicService _service;
    private BrowseState _browse;
    private readonly Dictionary<string, BrowseState> _sections = new(StringComparer.Ordinal);
    private string _libraryFilter = "playlists";
    private MusicPage Page { get => _browse.Page; set => _browse.Page = value; }
    private string _tab = "home", _kind = "home", _value = "", _query = "";
    private string _status = "Loading YouTube Music…";
    private WidgetCursorResource<BrowseEntry> Collection => _browse.Collection;
    private long _collectionGeneration;
    private sealed record BrowseEntry(int Index, MusicItem Item, WidgetCollectionItemKey Key);
    // Fixed section slots plus one reusable detail slot bound retained cursor state.
    private sealed class BrowseState(string id)
    {
        public string ScrollId { get; } = "music.scroll." + id;
        public string GroupId { get; } = "music.content." + id;
        public WidgetCursorResource<BrowseEntry> Collection { get; set; } = null!;
        public MusicPage Page { get; set; } = new("Home", []);
        public IReadOnlyList<MusicItem> Source { get; set; } = [];
        public BrowseEntry[] Items { get; set; } = [];
        public int Offset { get; set; }
        public string Kind { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Loaded { get; set; }
    }
    private bool _loading, _signingIn, _initialized;
    private long _pageGeneration;
    private long _focusSequence;
    private FocusGroupEntryRequest? _focusRequest;
    private string? _panel;
    private string? _panelReturnFocus;
    private readonly Stack<(string Kind, string Value, int Offset, string Focus)> _history = new();
    private static readonly string[] Tabs = ["home", "search", "library", "queue"];

    public StandaloneMusicWidget(IMusicService service)
    {
        _service = service;
        _browse = GetBrowseState("home", "");
        _service.Changed += ServiceChanged;
    }

    private BrowseState GetBrowseState(string kind, string value)
    {
        var id = kind == "library" ? "library." + value : kind is "home" or "search" or "queue" ? kind : "detail";
        if (_sections.TryGetValue(id, out var existing)) return existing;
        var state = new BrowseState(id);
        state.Collection = CreateCursorResource<BrowseEntry>("music.collection." + id, new()
        {
            PageSize = PageSize, MaximumRetainedItems = 96, RetainedItemTarget = 72,
            PaginationThreshold = 4, LoadPage = (cursor, direction, limit, token) => LoadCollectionPage(state, cursor, limit, token),
            MapError = _ => new("music.collection.failed", "Could not load more music."),
            Viewports = [new(state.ScrollId, item => item.Key, item => "item." + item.Index)
                { EstimatedItemExtent = 90 }],
        });
        _sections.Add(id, state);
        return state;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        if (!_initialized)
            Operations.RunSingleFlight("music.initialize", async context =>
            {
                try
                {
                    await _service.InitializeAsync(context.CancellationToken);
                    lock (_gate)
                    {
                        _initialized = true; _status = "";
                        if (!_service.State.Connected) _panel = "setup";
                    }
                    LoadPage();
                }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                { if (context.IsCurrent) SetStatus("YouTube Music could not start. Open Setup or retry."); }
            }, WidgetOperationLifetime.Active);
        else if (_loading) LoadPage();
        else
        {
            lock (_gate)
                if (_tab == "queue" && !ReferenceEquals(_browse.Source, _service.State.Queue))
                    ReplaceCollection(_service.State.Queue);
                else Collection.EnsureLoaded();
        }
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
            if (IsActive && _tab == "queue" && !ReferenceEquals(_browse.Source, _service.State.Queue))
                ReplaceCollection(_service.State.Queue);
        if (IsActive) Invalidate();
    }

    private WidgetOperationHandle ReplaceCollection(IReadOnlyList<MusicItem> items)
    {
        _browse.Source = items;
        var generation = ++_collectionGeneration;
        _browse.Items = items.Select((item, index) =>
            new BrowseEntry(index, item, new($"music.item.{generation}.{index}"))).ToArray();
        _browse.Offset = Math.Min(_browse.Offset, Math.Max(0, ((items.Count - 1) / PageSize) * PageSize));
        Collection.Reset(invalidate: false);
        return Collection.EnsureLoaded();
    }

    private ValueTask<WidgetCursorPage<BrowseEntry>> LoadCollectionPage(
        BrowseState state, WidgetCollectionCursor? cursor, int limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var start = cursor is null ? state.Offset : int.Parse(cursor.Value.Value.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
            var entries = state.Items.Skip(start).Take(limit).ToArray();
            return ValueTask.FromResult(new WidgetCursorPage<BrowseEntry>(entries,
                start == 0 ? null : new WidgetCollectionCursor("p" + Math.Max(0, start - limit)),
                start + entries.Length >= state.Items.Length ? null : new WidgetCollectionCursor("p" + (start + entries.Length)))
                { FirstItemIndex = start, TotalItemCount = state.Items.Length });
        }
    }
    private void SetStatus(string value) { lock (_gate) _status = value; Invalidate(); }
    private string ContentGroupId => _panel == "setup" ? "music.content.setup" : _browse.GroupId;
    private void EnterContent()
    {
        _focusSequence++;
        _focusRequest = new() { RequestId = _focusSequence, GroupId = ContentGroupId };
    }

    private void LoadPage(int offset = 0, string? returnFocus = null, bool refresh = true)
    {
        string kind, value;
        long generation;
        lock (_gate)
        {
            kind = _kind; value = _value; generation = ++_pageGeneration;
            _browse = GetBrowseState(kind, value);
            _focusRequest = null;
            _panelReturnFocus = returnFocus;
            if (!refresh && _browse.Loaded && _browse.Kind == kind && _browse.Value == value)
            {
                _loading = false; _status = "";
                if (kind == "queue" && !ReferenceEquals(_browse.Source, _service.State.Queue))
                    ReplaceCollection(_service.State.Queue);
                else Collection.EnsureLoaded();
                EnterContent(); Invalidate(); return;
            }
            _browse.Kind = kind; _browse.Value = value; _browse.Loaded = false;
            _browse.Offset = offset;
            Collection.Reset(invalidate: false);
            _browse.Items = []; _browse.Source = [];
            Page = new(kind == "search" ? "Search" : kind == "library" ? "Library" : "Home", []);
            _loading = kind is not ("queue" or "setup") && !(kind == "search" && value.Length == 0);
            if (!_loading) { _status = ""; ReplaceCollection(kind == "queue" ? _service.State.Queue : []); _browse.Loaded = true; EnterContent(); Invalidate(); return; }
            _status = "Loading…";
        }
        Invalidate();
        Operations.RunLatest("music.page", async context =>
        {
            try
            {
                var page = await _service.BrowseAsync(kind, value, context.CancellationToken);
                WidgetOperationHandle collectionLoad;
                lock (_gate)
                {
                    if (!context.IsCurrent || generation != _pageGeneration) return;
                    Page = page; _status = "";
                    collectionLoad = ReplaceCollection(page.Items);
                }
                await collectionLoad.Completion;
                lock (_gate)
                {
                    if (!context.IsCurrent || generation != _pageGeneration) return;
                    _loading = false;
                    _browse.Loaded = true;
                    EnterContent();
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
            _panel = null;
            _panelReturnFocus = null;
            _tab = _kind = tab; _value = tab == "library" ? _libraryFilter : tab == "search" ? _query : "";
            _history.Clear();
        }
        LoadPage(refresh: false);
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (!IsActive || action.Phase is not (ControllerEventPhase.Pressed or ControllerEventPhase.Repeated)) return ValueTask.CompletedTask;
        if (Collection.TryHandlePagination(action, out _)) return ValueTask.CompletedTask;
        var id = action.ActionId;
        if (action.Phase == ControllerEventPhase.Repeated && id is not ("player.seek" or "player.volume")) return ValueTask.CompletedTask;
        if (id == "tab.setup")
        {
            lock (_gate) { _panelReturnFocus = action.FocusedElementId; _panel = "setup"; EnterContent(); }
            Invalidate();
        }
        else if (id == "panel.back")
        {
            lock (_gate) { _panel = null; EnterContent(); }
            Invalidate();
        }
        else if (id.StartsWith("tab.", StringComparison.Ordinal) && Tabs.Contains(id[4..])) SelectTab(id[4..]);
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
            lock (_gate) { _kind = "library"; _value = _libraryFilter = id[8..]; _history.Clear(); }
            LoadPage(refresh: false);
        }
        else if (id == "back")
        {
            (string Kind, string Value, int Offset, string Focus) previous;
            lock (_gate)
            {
                if (!_history.TryPop(out previous)) return ValueTask.CompletedTask;
                _kind = previous.Kind; _value = previous.Value;
            }
            LoadPage(previous.Offset, previous.Focus, refresh: false);
        }
        else if (id == "refresh") LoadPage();
        else if (id == "signin")
        {
            Operations.RunSingleFlight("music.auth", async context =>
            {
                lock (_gate) { _signingIn = true; _status = "Complete sign-in in the browser, then return here."; }
                Invalidate();
                try
                {
                    var status = await _service.SignInAsync(context.CancellationToken);
                    if (context.IsCurrent) { ResetAccountPages(); SetStatus(status); }
                }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                { if (context.IsCurrent) SetStatus("Sign-in did not complete. Select Sign in to try again."); }
                finally { lock (_gate) _signingIn = false; Invalidate(); }
            }, WidgetOperationLifetime.Widget);
        }
        else if (id == "signin.cancel")
        {
            Operations.RunSingleFlight("music.cancel-auth", async context =>
            {
                try
                {
                    await _service.CancelSignInAsync(context.CancellationToken);
                    Operations.Cancel("music.auth");
                    SetStatus("Sign-in canceled. You can browse public music or try again.");
                }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                { if (context.IsCurrent) SetStatus("Could not cancel sign-in. Close the browser window to finish."); }
            }, WidgetOperationLifetime.Widget);
        }
        else if (id == "disconnect") RunPlayback(async token =>
        {
            await _service.DisconnectAsync(token);
            ResetAccountPages();
        });
        else if (id.StartsWith("item.", StringComparison.Ordinal) || id.StartsWith("radio.", StringComparison.Ordinal) || id.StartsWith("next.", StringComparison.Ordinal))
        {
            var separator = id.IndexOf('.');
            if (!int.TryParse(id[(separator + 1)..], out var index)) return ValueTask.CompletedTask;
            MusicItem[] items;
            lock (_gate) items = (_tab == "queue" ? _service.State.Queue : Page.Items).ToArray();
            if (index < 0 || index >= items.Length) return ValueTask.CompletedTask;
            var item = items[index];
            if (id.StartsWith("next.", StringComparison.Ordinal) && item.Kind == "song")
            {
                Operations.RunSerial("music.queue", async context =>
                {
                    try
                    {
                        await _service.PlayNextAsync(item, context.CancellationToken);
                        SetStatus("Added to play next");
                    }
                    catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException)
                    { SetStatus("Could not add the song to the queue. Try again."); }
                }, WidgetOperationLifetime.Widget);
            }
            else if (id.StartsWith("radio.", StringComparison.Ordinal) && item.Kind == "song")
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
                lock (_gate) { _history.Push((_kind, _value, (index / PageSize) * PageSize, "item." + index)); _kind = item.Kind; _value = item.Id; }
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
                    try { await _service.CommandAsync(command, command == "seek" ? action.RequestedValue / 1000 : action.RequestedValue, context.CancellationToken); }
                    catch (Exception error) when (error is IOException or OperationCanceledException) { SetStatus("Playback command failed. Try again."); }
                }, WidgetOperationLifetime.Widget);
            }
        }
        return ValueTask.CompletedTask;
    }

    private void ResetAccountPages()
    {
        lock (_gate)
        {
            ++_pageGeneration;
            foreach (var state in _sections.Values)
            {
                state.Loaded = false;
                state.Source = []; state.Items = []; state.Page = new("", []);
                state.Collection.Reset(invalidate: false);
            }
            _history.Clear();
            _kind = _tab; _value = _tab == "library" ? _libraryFilter : _tab == "search" ? _query : "";
        }
        LoadPage();
    }

    private void RunPlayback(Func<CancellationToken, Task> run) => Operations.RunLatest("music.selection", async context =>
    {
        SetStatus("Preparing playback…");
        try { await run(context.CancellationToken); if (context.IsCurrent) SetStatus(""); }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        { if (context.IsCurrent) SetStatus("Playback could not start. Try again, or reconnect in Setup."); }
    }, WidgetOperationLifetime.Widget);

}
