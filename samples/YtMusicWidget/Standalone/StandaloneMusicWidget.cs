using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

/// <summary>Native declarative presentation; credentials and stream URLs remain in the application.</summary>
public sealed partial class StandaloneMusicWidget : Widget
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
    private long _focusSequence;
    private FocusGroupEntryRequest? _focusRequest;
    private string? _panel;
    private string? _panelReturnFocus;
    private readonly Stack<(string Kind, string Value, int Offset, string Focus)> _history = new();
    private static readonly string[] Tabs = ["home", "search", "library", "queue"];

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
    private string ContentGroupId => "music.content." + _focusSequence;
    private void EnterContent()
    {
        _focusSequence++;
        _focusRequest = new() { RequestId = _focusSequence, GroupId = ContentGroupId };
    }

    private void LoadPage(int offset = 0, string? returnFocus = null)
    {
        string kind, value;
        long generation;
        lock (_gate)
        {
            kind = _kind; value = _value; generation = ++_pageGeneration;
            _focusRequest = null;
            _offset = offset;
            _panelReturnFocus = returnFocus;
            _page = new(kind == "search" ? "Search" : kind == "library" ? "Library" : "Home", []);
            _loading = kind is not ("queue" or "setup") && !(kind == "search" && value.Length == 0);
            if (!_loading) { _status = ""; EnterContent(); Invalidate(); return; }
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
                    _offset = Math.Min(_offset, Math.Max(0, ((page.Items.Count - 1) / PageSize) * PageSize));
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
            _tab = _kind = tab; _value = tab == "library" ? "playlists" : tab == "search" ? _query : "";
            _history.Clear();
        }
        LoadPage();
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (!IsActive || action.Phase is not (ControllerEventPhase.Pressed or ControllerEventPhase.Repeated)) return ValueTask.CompletedTask;
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
            lock (_gate) { _kind = "library"; _value = id[8..]; _history.Clear(); }
            LoadPage();
        }
        else if (id == "back")
        {
            (string Kind, string Value, int Offset, string Focus) previous;
            lock (_gate)
            {
                if (!_history.TryPop(out previous)) return ValueTask.CompletedTask;
                _kind = previous.Kind; _value = previous.Value;
            }
            LoadPage(previous.Offset, previous.Focus);
        }
        else if (id == "refresh") LoadPage();
        else if (id is "page.next" or "page.previous")
        {
            lock (_gate)
            {
                var count = _tab == "queue" ? _service.State.Queue.Count : _page.Items.Count;
                _offset = Math.Clamp(_offset + (id == "page.next" ? PageSize : -PageSize), 0, Math.Max(0, ((count - 1) / PageSize) * PageSize));
                EnterContent();
            }
            Invalidate();
        }
        else if (id == "signin")
        {
            Operations.RunSingleFlight("music.auth", async context =>
            {
                lock (_gate) { _signingIn = true; _status = "Complete sign-in in the browser, then return here."; }
                Invalidate();
                try
                {
                    var status = await _service.SignInAsync(context.CancellationToken);
                    if (context.IsCurrent) SetStatus(status);
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
                lock (_gate) { _history.Push((_kind, _value, _offset, "item." + index)); _kind = item.Kind; _value = item.Id; }
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

    private void RunPlayback(Func<CancellationToken, Task> run) => Operations.RunLatest("music.selection", async context =>
    {
        SetStatus("Preparing playback…");
        try { await run(context.CancellationToken); if (context.IsCurrent) SetStatus(""); }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        { if (context.IsCurrent) SetStatus("Playback could not start. Try again, or reconnect in Setup."); }
    }, WidgetOperationLifetime.Widget);

}
