using WidgetRail.WidgetSdk;

internal static class WidgetOutOfBandCommandTests
{
    public static async Task Run()
    {
        await CorrelatedEventsEnforceEveryAuthorityGuardAsync();
        await IndependentObservationsRetainThenSettleProjectedStateAsync();
        await ExpiryRetiresOnlyCorrelationAuthorityAsync();
    }

    private static async Task CorrelatedEventsEnforceEveryAuthorityGuardAsync()
    {
        var widget = new CommandWidget();
        await WidgetTestHost.InitializeAsync(widget);

        var first = widget.Run(new("media-a", 10));
        Equal(WidgetOutOfBandCommandAdmission.Started, first.Admission);
        Equal(1L, first.Sequence);
        Equal(10, widget.State.Value);
        True(widget.IsPending, "The admitted projection was not retained.");

        var duplicate = widget.Run(new("media-a", 20));
        Equal(WidgetOutOfBandCommandAdmission.RejectedPending, duplicate.Admission);
        Equal(10, widget.State.Value);

        Equal(WidgetOutOfBandObservationAdmission.RejectedAuthority,
            widget.Observe(new(1, 0, "media-b", 1, 1)));
        Equal(WidgetOutOfBandObservationAdmission.RejectedCorrelation,
            widget.Observe(new(1, 99, "media-a", 1, 1)));
        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(new(1, 0, "media-a", 1, 2)));
        Equal(10, widget.State.Value);
        Equal(2, widget.State.ProviderRevision);
        True(widget.IsPending, "An unrelated observation acknowledged the projection.");
        Equal(WidgetOutOfBandObservationAdmission.RejectedStale,
            widget.Observe(new(1, 0, "media-a", 10, 3)));

        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(new(2, first.Sequence, "media-a", 10, 4)));
        False(widget.IsPending, "The correlated terminal did not retire its projection.");
        Equal(null, widget.State.ProjectedSequence);
        Equal(WidgetOutOfBandObservationAdmission.RejectedCorrelation,
            widget.Observe(new(3, first.Sequence, "media-a", 10, 5)));

        var replacement = widget.RunLatest(new("media-a", 20));
        var newest = widget.RunLatest(new("media-a", 30));
        Equal(WidgetOutOfBandCommandAdmission.Started, replacement.Admission);
        Equal(WidgetOutOfBandCommandAdmission.Replaced, newest.Admission);
        Equal(replacement.Sequence + 1, newest.Sequence);
        Equal(WidgetOutOfBandObservationAdmission.RejectedCorrelation,
            widget.Observe(new(3, replacement.Sequence, "media-a", 20, 6)));
        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(new(3, newest.Sequence, "media-a", 30, 7)));

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task IndependentObservationsRetainThenSettleProjectedStateAsync()
    {
        var widget = new CommandWidget();
        await WidgetTestHost.InitializeAsync(widget);

        var stalePoll = widget.BeginObservation();
        var run = widget.Run(new("media-a", 40));
        Equal(WidgetOutOfBandCommandAdmission.Started, run.Admission);

        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(stalePoll, new(0, 0, "media-a", 5, 8)));
        Equal(40, widget.State.Value);
        Equal(8, widget.State.ProviderRevision);
        True(widget.IsPending,
            "A poll begun before projection incorrectly settled the command.");

        var successor = widget.BeginObservation();
        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(successor, new(0, 0, "media-a", 41, 9)));
        Equal(41, widget.State.Value);
        False(widget.IsPending,
            "The first post-projection authoritative observation did not settle.");

        var matching = widget.Run(new("media-a", 50));
        True(matching.IsAccepted, "The matching-observation projection was rejected.");
        var matchingPoll = widget.BeginObservation();
        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(matchingPoll, new(0, 0, "media-a", 50, 10)));
        False(widget.IsPending, "A matching poll did not confirm the projection.");

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ExpiryRetiresOnlyCorrelationAuthorityAsync()
    {
        var clock = new ManualTimeProvider();
        var widget = new CommandWidget(clock);
        await WidgetTestHost.InitializeAsync(widget);

        var run = widget.Run(new("media-a", 60, TimeSpan.FromSeconds(5)));
        Equal(60, widget.State.Value);
        clock.Advance(TimeSpan.FromSeconds(5));
        await WaitUntil(() => !widget.IsPending);

        Equal(WidgetOutOfBandObservationAdmission.RejectedCorrelation,
            widget.Observe(new(1, run.Sequence, "media-a", 60, 11)));
        Equal(60, widget.State.Value);
        var observation = widget.BeginObservation();
        Equal(WidgetOutOfBandObservationAdmission.Accepted,
            widget.Observe(observation, new(0, 0, "media-a", 55, 12)));
        Equal(55, widget.State.Value);

        await WidgetTestHost.DestroyAsync(widget);
    }

    private sealed record State(
        string Entity,
        int Value,
        long? ProjectedSequence,
        int ProviderRevision)
    {
        internal static State Initial { get; } = new("media-a", 0, null, 0);
    }

    private sealed record Request(string Entity, int Value, TimeSpan? ExpiresAfter = null);
    private sealed record Observation(
        long Sequence,
        long Correlation,
        string Entity,
        int Value,
        int ProviderRevision);
    private sealed record Authority(string Entity, int Value);

    private sealed class CommandWidget : Widget
    {
        private readonly WidgetModel<State> _model;
        private readonly WidgetOutOfBandCommand<State, Request, Observation, Authority> _command;

        internal CommandWidget(TimeProvider? timeProvider = null)
        {
            _model = CreateModel(State.Initial);
            _command = CreateOutOfBandCommand(_model,
                new WidgetOutOfBandCommandOptions<State, Request, Observation, Authority>
                {
                    TimeProvider = timeProvider,
                    Apply = (state, request, sequence) => new(
                        state with
                        {
                            Entity = request.Entity,
                            Value = request.Value,
                            ProjectedSequence = sequence,
                        },
                        new(request.Entity, request.Value),
                        ExpiresAfter: request.ExpiresAfter),
                    ObservationSequence = observation => observation.Sequence,
                    CorrelationSequence = observation => observation.Correlation,
                    MatchesAuthority = (state, observation) =>
                        string.Equals(state.Entity, observation.Entity,
                            StringComparison.Ordinal),
                    ConfirmsProjection = (_, authority, observation, beganAfterProjection) =>
                        beganAfterProjection || observation.Value == authority.Value,
                    Reconcile = (state, observation, confirmsProjection) => state with
                    {
                        Value = confirmsProjection ? observation.Value : state.Value,
                        ProjectedSequence = confirmsProjection
                            ? null
                            : state.ProjectedSequence,
                        ProviderRevision = observation.ProviderRevision,
                    },
                });
        }

        internal State State => _model.Value;
        internal bool IsPending => _command.IsPending;
        internal WidgetOutOfBandCommandRun Run(Request request) => _command.Run(request);
        internal WidgetOutOfBandCommandRun RunLatest(Request request) =>
            _command.RunLatest(request);
        internal WidgetOutOfBandObservationTicket BeginObservation() =>
            _command.BeginObservation();
        internal WidgetOutOfBandObservationAdmission Observe(Observation observation) =>
            _command.Observe(observation);
        internal WidgetOutOfBandObservationAdmission Observe(
            WidgetOutOfBandObservationTicket ticket,
            Observation observation) => _command.Observe(ticket, observation);

        public override WidgetView Render() => new(
            UI.Text(State.Value.ToString(), "out-of-band.value"));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _now.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_gate) _timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_gate)
            {
                _now += duration;
                foreach (var timer in _timers.ToArray())
                    if (timer.TryFire(_now, out var callback)) callbacks.Add(callback);
            }
            foreach (var callback in callbacks) callback.Callback(callback.State);
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private DateTimeOffset? _dueAt;
            private bool _disposed;

            internal ManualTimer(ManualTimeProvider owner, TimerCallback callback,
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
                _dueAt = null;
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

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not reached.");
            await Task.Yield();
        }
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
