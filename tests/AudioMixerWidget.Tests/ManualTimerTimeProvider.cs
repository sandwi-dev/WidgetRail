internal sealed class ManualTimerTimeProvider : TimeProvider
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
