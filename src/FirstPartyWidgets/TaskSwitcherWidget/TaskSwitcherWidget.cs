using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.TaskSwitcher;

public sealed class TaskSwitcherWidget : Widget
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly WidgetTimedMutation _toastExpiry;
    private IReadOnlyList<WidgetTaskWindow> _windows = [];
    private Task _refreshLoop = Task.CompletedTask;
    private long _generation;
    private long _toastGeneration;
    private bool _loaded;
    private bool _busy;
    private string? _focus;
    private string? _toast;
    private ToastTone _toastTone;
    private string? _loadError;

    public TaskSwitcherWidget() { _toastExpiry = CreateTimedMutation(); }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        long generation;
        lock (_gate)
        {
            generation = ++_generation;
            _windows = []; _focus = null; _loaded = false; _loadError = null; _busy = false;
            SetToastLocked(null);
        }
        await RefreshAsync(generation, activeLifetime).ConfigureAwait(false);
        _refreshLoop = RefreshLoopAsync(generation, activeLifetime);
    }

    private async Task RefreshLoopAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                await RefreshAsync(generation, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_gate) { ++_generation; _busy = false; SetToastLocked(null); }
        await _refreshLoop.ConfigureAwait(false);
    }

    private async Task RefreshAsync(long generation, CancellationToken cancellationToken, bool reorder = false)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        try
        {
            var windows = await HostServices.TaskSwitcher.GetWindowsAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return;
                if (!reorder && _windows.Count > 0)
                {
                    var fresh = windows.ToDictionary(window => window.WindowId, StringComparer.Ordinal);
                    var oldIds = _windows.Select(window => window.WindowId).ToHashSet(StringComparer.Ordinal);
                    windows = _windows.Where(window => fresh.ContainsKey(window.WindowId))
                        .Select(window => fresh[window.WindowId])
                        .Concat(windows.Where(window => !oldIds.Contains(window.WindowId))).ToArray();
                }
                changed = !_loaded || _loadError is not null || !_windows.SequenceEqual(windows);
                var previousIndex = _windows.ToList().FindIndex(window => _focus is not null &&
                    (_focus == window.WindowId || _focus == CloseId(window.WindowId)));
                _windows = windows;
                if (_focus != "tasks.refresh" && !_windows.Any(window =>
                        _focus == window.WindowId || _focus == CloseId(window.WindowId)))
                    _focus = windows.Count == 0 ? "tasks.refresh" :
                        windows[Math.Clamp(previousIndex, 0, windows.Count - 1)].WindowId;
                _loaded = true; _loadError = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate)
            {
                if (generation != _generation) return;
                var message = FriendlyError(error);
                changed = _loadError != message;
                _loadError = message; _loaded = true;
            }
        }
        finally
        {
            _refreshGate.Release();
            if (changed) Invalidate();
        }
    }

    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        long generation;
        WidgetTaskWindow? window;
        var close = action.ActionId.StartsWith("close.", StringComparison.Ordinal);
        lock (_gate)
        {
            generation = _generation;
            if (_busy) return;
            window = _windows.FirstOrDefault(item => action.ActionId == "switch." + item.WindowId ||
                action.ActionId == "close." + item.WindowId);
            if (action.ActionId != "refresh" && window is null) return;
            _busy = true;
        }
        Invalidate();
        try
        {
            if (window is not null)
            {
                if (close) await HostServices.TaskSwitcher.CloseAsync(window.WindowId, cancellationToken).ConfigureAwait(false);
                else await HostServices.TaskSwitcher.SwitchAsync(window.WindowId, cancellationToken).ConfigureAwait(false);
                lock (_gate) if (generation == _generation && close)
                    SetToastLocked("Close requested. The app may ask you to save your work.", ToastTone.Info);
            }
            if (window is null || close)
                await RefreshAsync(generation, cancellationToken, reorder: window is null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate) if (generation == _generation) SetToastLocked(FriendlyError(error), ToastTone.Danger);
        }
        finally
        {
            lock (_gate) if (generation == _generation) _busy = false;
            Invalidate();
        }
    }

    public override ValueTask<bool> OnControllerInputAsync(ControllerInputEvent input, CancellationToken cancellationToken = default)
    {
        lock (_gate) if (input.FocusedElementId is { } id) _focus = id;
        return base.OnControllerInputAsync(input, cancellationToken);
    }

    private void SetToastLocked(string? text, ToastTone tone = ToastTone.Neutral)
    {
        _toast = text; _toastTone = tone;
        var generation = ++_toastGeneration;
        _toastExpiry.Cancel();
        if (text is null) return;
        _toastExpiry.ScheduleLatest(UI.DefaultToastDuration, () =>
        {
            lock (_gate)
            {
                if (_toastGeneration != generation) return;
                _toast = null;
            }
            Invalidate();
        });
    }

    private static string FriendlyError(Exception error) =>
        (error as WidgetCapabilityException)?.ErrorCode switch
        {
            "permission_denied" or "capability_revoked" => "Allow window access in this widget's permissions, then try again.",
            "window_unavailable" => "That window is no longer available. Refresh the list and choose another.",
            "window_switch_denied" => "Windows couldn't switch to this window. Try opening it from the taskbar.",
            "window_close_denied" => "Windows couldn't close this window. Close it from the app itself.",
            _ => "Windows couldn't complete this request. Try again.",
        };

    private static string CloseId(string id) => "close-" + id;

    public override WidgetView Render()
    {
        lock (_gate)
        {
            var refresh = UI.Button("Refresh", "refresh", "tasks.refresh").Icon(WidgetGlyph.Refresh)
                .Busy(_busy).Classes("tasks-refresh");
            if (_windows.Count > 0) refresh = refresh.FocusDown(_windows[0].WindowId);
            var header = UI.Row("tasks.header",
                UI.Stack("tasks.heading",
                    UI.Text("Task Switcher", "tasks.title").Classes("tasks-title"),
                    UI.Text(_loadError is not null ? "Couldn't refresh windows" : _loaded ? $"{_windows.Count} open windows" : "Finding open windows...", "tasks.count").Classes("tasks-count"))
                    .Classes("tasks-heading"), refresh).Classes("tasks-header");
            var rows = new List<WidgetElement>();
            for (var index = 0; index < _windows.Count; index++)
            {
                var window = _windows[index];
                var tile = UI.PosterTile(window.ApplicationName,
                    window.IsMinimized ? "Minimized" : "Open", "switch." + window.WindowId, window.WindowId,
                    UI.WindowPreview(window.WindowId, window.WindowId + ".preview",
                        "Preview of " + window.ApplicationName).Classes("tasks-preview"),
                    subtitle: window.Title,
                    accessibilityLabel: "Switch to " + window.ApplicationName + ": " + window.Title)
                    .Busy(_busy).Classes("tasks-window") with
                    { Shortcuts = [new(ControllerButton.X, "close." + window.WindowId, Label: "Close window")] };
                var close = UI.Button("Close", "close." + window.WindowId, CloseId(window.WindowId))
                    .Busy(_busy).Classes("tasks-close");
                rows.Add(UI.Stack(window.WindowId + ".row", tile, close).Classes("tasks-row"));
            }
            if (rows.Count == 0)
                rows.Add(UI.Text(_loadError ?? (_loaded ? "No other application windows are open." : "Loading windows..."),
                    "tasks.empty").Classes("tasks-help"));
            var children = new List<WidgetElement>
            {
                header,
                UI.VerticalScroll("tasks.list", UI.ResponsiveGrid("tasks.grid", 240, 3, rows.ToArray()).Classes("tasks-grid")).Classes("tasks-list"),
            };
            if (_toast is not null)
                children.Add(UI.Toast("Task Switcher", _toast, _toastTone, "tasks.toast"));
            return new(UI.Stack("tasks.root", children.ToArray()).InputScope("tasks")
                .Shortcut(ControllerButton.Y, "refresh").Classes("tasks-widget"),
                _focus ?? (_windows.Count == 0 ? "tasks.refresh" : _windows[0].WindowId),
                Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard,
                    PreferredWidth = 900, PreferredHeight = 620, MinimumWidth = 400, MinimumHeight = 360 });
        }
    }
}
