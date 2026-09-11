using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SettingsToastScenarios
{
    public static async Task ExpiryAndReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-settings-toast-" + Guid.NewGuid().ToString("N"));
        var paths = new PlatformSettingsPaths(root);
        var clock = new ManualTimerTimeProvider();
        var widget = new SettingsWidget(new PlatformSettingsStore(paths), new ThemeCatalog(paths), timeProvider: clock);
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        ViewSnapshot Snapshot() => widget.Render().CreateSnapshot("settings-toast-test", 1);
        bool HasToast() => Nodes(Snapshot().Root).Any(node => node.Id == "settings.toast");
        async Task Refresh() => await widget.OnActionAsync(new WidgetActionEvent("refresh", "test"));
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            await widget.OnActionAsync(new WidgetActionEvent("open.controllers", "test"));
            foreach (var shortcut in new[] { "Guide", "View + Menu" })
            {
                await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.toggle", "controllers.open-shortcut"));
                var changed = Snapshot();
                if (Nodes(changed.Root).SingleOrDefault(node => node.Id == "settings.toast.message")?.Text !=
                    $"Controller shortcut set to {shortcut}")
                    throw new Exception("Saving the shortcut must replace busy feedback with a confirmation.");
                clock.Advance(TimeSpan.FromSeconds(4));
                if (!HasToast()) throw new Exception("Shortcut confirmation disappeared before its duration elapsed.");
                clock.Advance(TimeSpan.FromSeconds(1));
                await widget.ToastExpiryTask.WaitAsync(TimeSpan.FromSeconds(5));
                if (HasToast() || Snapshot().InitialFocusId != changed.InitialFocusId)
                    throw new Exception("Shortcut confirmation must expire after five seconds without changing focus.");
            }
            await Refresh();
            var snapshot = Snapshot();
            if (!HasToast() || snapshot.Root.Children.Last().Id != "settings.toast")
                throw new Exception("Feedback must use a toast after the page content.");
            if (Nodes(snapshot.Root).Any(node => node.Id == "settings.status") ||
                Nodes(snapshot.Root).Single(node => node.Id == "settings.header").Children.Any(node => node.Id == "settings.toast"))
                throw new Exception("The Settings header must not contain status feedback.");
            var toast = Nodes(snapshot.Root).Single(node => node.Id == "settings.toast");
            if (Nodes(toast).Any(node => node.Kind == ViewNodeKind.Button || node.InputScopeId is not null))
                throw new Exception("Toast must not introduce controller focus or an input scope.");
            clock.Advance(TimeSpan.FromSeconds(4));
            await Refresh();
            clock.Advance(TimeSpan.FromSeconds(1));
            if (!HasToast()) throw new Exception("An earlier deadline removed replacement feedback.");
            clock.Advance(TimeSpan.FromSeconds(4));
            await widget.ToastExpiryTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (HasToast() || Snapshot().InitialFocusId != snapshot.InitialFocusId)
                throw new Exception("Toast expiry must remove feedback without changing focus.");
            await Refresh();
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            clock.Advance(TimeSpan.FromSeconds(10));
            await widget.ToastExpiryTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (HasToast()) throw new Exception("Feedback must retire with the active widget lifetime.");
        }
        finally
        {
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

file sealed class ManualTimerTimeProvider : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _now.Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state, dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan duration)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_timers)
        {
            _now += duration;
            foreach (var timer in _timers.ToArray())
                if (timer.TryFire(_now, out var callback)) callbacks.Add(callback);
        }
        foreach (var callback in callbacks) callback.Callback(callback.State);
    }

    private sealed class Timer : ITimer
    {
        private readonly ManualTimerTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;
        private TimeSpan _period;
        private bool _disposed;

        internal Timer(ManualTimerTimeProvider owner, TimerCallback callback,
            object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;
            _period = period;
            _dueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : _owner.GetUtcNow() + dueTime;
            return true;
        }

        internal bool TryFire(DateTimeOffset now,
            out (TimerCallback Callback, object? State) callback)
        {
            callback = default;
            if (_disposed || _dueAt is null || _dueAt > now) return false;
            callback = (_callback, _state);
            _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
            return true;
        }

        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
