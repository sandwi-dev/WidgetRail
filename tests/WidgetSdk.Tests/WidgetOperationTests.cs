using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetOperationTests
{
    public static async Task Run()
    {
        await RejectsInactiveWorkAsync();
        await JoinsSingleFlightAsync();
        await ReplacesLatestWithoutOverlapAsync();
        await RunsSerialInOrderAsync();
        await ReportsFailuresWithoutFaultingCompletionAsync();
        await DrainsLifecycleWorkBeforeCallbacksAsync();
        await StateTransitionsPreserveActiveWorkAsync();
        await ActiveLifetimeCancellationIsNormalButRealFaultsPropagateAsync();
        await DelayedMutationsAreLatestLifecycleOwnedAndDeterministicAsync();
    }

    private static async Task DelayedMutationsAreLatestLifecycleOwnedAndDeterministicAsync()
    {
        var clock = new ManualTimeProvider();
        var widget = new OperationWidget(clock);
        Equal(WidgetOperationAdmission.RejectedInactive,
            widget.Delayed(TimeSpan.FromSeconds(1), () => { }).Admission);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

        var calls = 0;
        var first = widget.Delayed(TimeSpan.FromSeconds(5), () => calls++);
        clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Yield();
        Equal(0, calls);
        var replacement = widget.Delayed(TimeSpan.FromSeconds(5), () => calls += 10);
        Equal(WidgetOperationAdmission.Replaced, replacement.Admission);
        clock.Advance(TimeSpan.FromSeconds(4));
        await Task.Yield();
        Equal(0, calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        Equal(WidgetOperationStatus.Succeeded, (await replacement.Completion).Status);
        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(10, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        Equal(10, calls);

        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var due = widget.Delayed(TimeSpan.FromSeconds(1), () =>
        {
            callbackEntered.Set();
            releaseCallback.Wait();
        });
        Exception? advanceFailure = null;
        using var advanceCompleted = new ManualResetEventSlim();
        var advanceThread = new Thread(() =>
        {
            try { clock.Advance(TimeSpan.FromSeconds(1)); }
            catch (Exception exception) { advanceFailure = exception; }
            finally { advanceCompleted.Set(); }
        });
        WidgetOperationHandle? concurrentReplacement = null;
        Exception? concurrentFailure = null;
        using var replacementCompleted = new ManualResetEventSlim();
        var replacementThread = new Thread(() =>
        {
            try
            {
                concurrentReplacement = widget.Delayed(
                    TimeSpan.FromSeconds(1), () => calls++);
            }
            catch (Exception exception)
            {
                concurrentFailure = exception;
            }
            finally
            {
                replacementCompleted.Set();
            }
        });
        var replacementStarted = false;
        var replacementJoined = false;
        var advanceJoined = false;
        try
        {
            advanceThread.Start();
            True(callbackEntered.Wait(TimeSpan.FromSeconds(1)),
                "The due delayed callback did not enter its barrier.");
            replacementThread.Start();
            replacementStarted = true;
            True(replacementCompleted.Wait(TimeSpan.FromSeconds(1)),
                "Concurrent scheduling blocked on the SDK mutation gate.");
        }
        finally
        {
            releaseCallback.Set();
            if (replacementStarted)
                replacementJoined = replacementThread.Join(TimeSpan.FromSeconds(1));
            advanceJoined = advanceThread.Join(TimeSpan.FromSeconds(1));
        }
        True(replacementJoined && replacementCompleted.IsSet,
            "The concurrent scheduling thread did not terminate.");
        True(advanceJoined && advanceCompleted.IsSet,
            "The fake-time advance thread did not terminate.");
        if (advanceFailure is not null) throw advanceFailure;
        if (concurrentFailure is not null) throw concurrentFailure;
        Equal(WidgetOperationStatus.Succeeded, (await due.Completion).Status);
        True(widget.CancelDelayed(), "The concurrent replacement was not admitted.");
        Equal(WidgetOperationStatus.Canceled,
            (await concurrentReplacement!.Value.Completion).Status);

        var canceled = widget.Delayed(TimeSpan.FromSeconds(1), () => calls++);
        True(widget.CancelDelayed(), "The delayed mutation slot was not cancelable.");
        Equal(WidgetOperationStatus.Canceled, (await canceled.Completion).Status);

        using var owner = new CancellationTokenSource();
        var routeOwned = widget.Delayed(TimeSpan.FromSeconds(1),
            () => calls++, owner.Token);
        owner.Cancel();
        Equal(WidgetOperationStatus.Canceled, (await routeOwned.Completion).Status);

        var active = widget.Delayed(TimeSpan.FromSeconds(1), () => calls++);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        Equal(WidgetOperationStatus.Canceled, (await active.Completion).Status);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        var state = widget.DelayedState(TimeSpan.FromSeconds(1), () => calls++);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        Equal(WidgetOperationStatus.Canceled, (await state.Completion).Status);

        var failures = 0;
        widget.SubscribeDelayedFailure((_, args) =>
        {
            if (args.Exception is InvalidOperationException)
                failures++;
        });
        var failed = widget.Delayed(TimeSpan.FromSeconds(1),
            () => throw new InvalidOperationException("expected"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var failure = await failed.Completion;
        Equal(WidgetOperationStatus.Failed, failure.Status);
        True(failure.Exception is InvalidOperationException,
            "Delayed callback failure did not retain its exception.");
        Equal(1, failures);

        var destroying = widget.DelayedWidget(TimeSpan.FromSeconds(1), () => calls++);
        await WidgetTestHost.DestroyAsync(widget);
        Equal(WidgetOperationStatus.Canceled, (await destroying.Completion).Status);
        Equal(10, calls);

        Throws<ArgumentOutOfRangeException>(() => widget.Delayed(
            TimeSpan.Zero, () => { }));
        Throws<ArgumentOutOfRangeException>(() => widget.Delayed(
            WidgetTimedMutation.MaximumDelay + TimeSpan.FromTicks(1), () => { }));
        Throws<ArgumentNullException>(() => widget.Delayed(
            TimeSpan.FromSeconds(1), null!));
        Throws<ArgumentOutOfRangeException>(() =>
            new InvalidTimedMutationWidget());

        var throwingClockWidget = new OperationWidget(new ThrowingTimeProvider());
        await WidgetTestHost.InitializeAsync(throwingClockWidget);
        await WidgetTestHost.SetLifecycleStateAsync(
            throwingClockWidget, WidgetLifecycleState.Visible);
        var clockFailures = 0;
        throwingClockWidget.SubscribeDelayedFailure((_, args) =>
        {
            if (args.Exception is InvalidOperationException) clockFailures++;
        });
        var throwingDelay = throwingClockWidget.Delayed(
            TimeSpan.FromSeconds(1), () => calls++);
        var throwingResult = await throwingDelay.Completion;
        Equal(WidgetOperationStatus.Failed, throwingResult.Status);
        True(throwingResult.Exception is InvalidOperationException,
            "The TimeProvider failure was not retained.");
        await throwingClockWidget.WhenDelayedIdle();
        Equal(1, clockFailures);
        await WidgetTestHost.DestroyAsync(throwingClockWidget);

        using var retiredLifetime = new CancellationTokenSource();
        retiredLifetime.Cancel();
        using var retiredMutation = new WidgetTimedMutation(
            WidgetOperationLifetime.Active,
            TimeProvider.System,
            _ => new(true, retiredLifetime.Token));
        var retiredAdmission = retiredMutation.ScheduleLatest(
            TimeSpan.FromSeconds(1), () => calls++);
        Equal(WidgetOperationAdmission.RejectedInactive, retiredAdmission.Admission);
        Equal(WidgetOperationStatus.Rejected,
            (await retiredAdmission.Completion).Status);
    }

    private static async Task RejectsInactiveWorkAsync()
    {
        var widget = new OperationWidget();
        var invoked = false;
        var beforeCreation = widget.Latest("before", WidgetOperationLifetime.Widget, _ =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });
        Equal(WidgetOperationAdmission.RejectedInactive, beforeCreation.Admission);
        Equal(WidgetOperationStatus.Rejected, (await beforeCreation.Completion).Status);

        await WidgetTestHost.InitializeAsync(widget);
        var active = widget.Latest("inactive", WidgetOperationLifetime.Active, _ =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });
        Equal(WidgetOperationAdmission.RejectedInactive, active.Admission);
        True(!invoked, "Rejected operations must never invoke widget code.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task JoinsSingleFlightAsync()
    {
        var widget = await CreateVisibleAsync();
        var release = NewSignal();
        var invocations = 0;
        var first = widget.Single("refresh", WidgetOperationLifetime.Active, async context =>
        {
            Interlocked.Increment(ref invocations);
            await release.Task.WaitAsync(context.CancellationToken);
        });
        var second = widget.Single("refresh", WidgetOperationLifetime.Active,
            _ => throw new InvalidOperationException("A joined delegate must not run."));

        Equal(WidgetOperationAdmission.Started, first.Admission);
        Equal(WidgetOperationAdmission.Joined, second.Admission);
        True(ReferenceEquals(first.Completion, second.Completion),
            "Single-flight callers must observe the exact incumbent completion.");
        Equal(1, Volatile.Read(ref invocations));
        release.TrySetResult();
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ReplacesLatestWithoutOverlapAsync()
    {
        var widget = await CreateVisibleAsync();
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var thirdRan = NewSignal();
        var concurrent = 0;
        var maximumConcurrent = 0;
        var firstWasCurrentAfterReplacement = true;

        var first = widget.Latest("page", WidgetOperationLifetime.Active, async context =>
        {
            var now = Interlocked.Increment(ref concurrent);
            UpdateMaximum(ref maximumConcurrent, now);
            firstStarted.TrySetResult();
            await releaseFirst.Task;
            firstWasCurrentAfterReplacement = context.IsCurrent;
            Interlocked.Decrement(ref concurrent);
        });
        await firstStarted.Task;

        var second = widget.Latest("page", WidgetOperationLifetime.Active,
            _ => throw new InvalidOperationException("A superseded pending delegate must not run."));
        var third = widget.Latest("page", WidgetOperationLifetime.Active, context =>
        {
            var now = Interlocked.Increment(ref concurrent);
            UpdateMaximum(ref maximumConcurrent, now);
            True(context.IsCurrent, "Newest latest operation was not current when started.");
            thirdRan.TrySetResult();
            Interlocked.Decrement(ref concurrent);
            return ValueTask.CompletedTask;
        });

        Equal(WidgetOperationAdmission.Replaced, second.Admission);
        Equal(WidgetOperationAdmission.Replaced, third.Admission);
        Equal(WidgetOperationStatus.Superseded, (await second.Completion).Status);
        releaseFirst.TrySetResult();
        await thirdRan.Task;
        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await third.Completion).Status);
        True(!firstWasCurrentAfterReplacement,
            "Replaced latest work must become stale before it can publish results.");
        Equal(1, maximumConcurrent);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task RunsSerialInOrderAsync()
    {
        var widget = await CreateVisibleAsync();
        var firstRelease = NewSignal();
        var order = new List<int>();
        var first = widget.Serial("save", WidgetOperationLifetime.Widget, async context =>
        {
            order.Add(1);
            await firstRelease.Task.WaitAsync(context.CancellationToken);
        });
        var second = widget.Serial("save", WidgetOperationLifetime.Widget, _ =>
        {
            order.Add(2);
            return ValueTask.CompletedTask;
        });
        var third = widget.Serial("save", WidgetOperationLifetime.Widget, _ =>
        {
            order.Add(3);
            return ValueTask.CompletedTask;
        });

        Equal(WidgetOperationAdmission.Enqueued, second.Admission);
        Equal(WidgetOperationAdmission.Enqueued, third.Admission);
        firstRelease.TrySetResult();
        await Task.WhenAll(first.Completion, second.Completion, third.Completion);
        True(order.SequenceEqual([1, 2, 3]), "Serial operations did not preserve FIFO order.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ReportsFailuresWithoutFaultingCompletionAsync()
    {
        var widget = await CreateVisibleAsync();
        var failures = 0;
        widget.SubscribeFailure((_, _) => throw new InvalidOperationException("observer"));
        widget.SubscribeFailure((_, args) =>
        {
            Equal("failure", args.Key);
            Interlocked.Increment(ref failures);
        });

        var operation = widget.Single("failure", WidgetOperationLifetime.Active,
            _ => throw new InvalidOperationException("expected"));
        var result = await operation.Completion;
        Equal(WidgetOperationStatus.Failed, result.Status);
        True(result.Exception is InvalidOperationException,
            "Operation failure was not returned to the caller.");
        Equal(1, failures);
        True(!widget.Busy("failure"), "Failed operation left its lane busy.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task DrainsLifecycleWorkBeforeCallbacksAsync()
    {
        var widget = await CreateVisibleAsync();
        var started = NewSignal();
        widget.Single("observer", WidgetOperationLifetime.Active, async context =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            finally
            {
                widget.OperationCleanupFinished = true;
            }
        });
        await started.Task;
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        True(widget.OperationCleanupFinished, "Active work was not drained on deactivation.");
        True(widget.DeactivationSawCleanup,
            "OnDeactivated ran before active operation cleanup completed.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task StateTransitionsPreserveActiveWorkAsync()
    {
        var widget = await CreateVisibleAsync();
        var stateStarted = NewSignal();
        var activeStarted = NewSignal();
        var state = widget.Single("state", WidgetOperationLifetime.State, async context =>
        {
            stateStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        });
        var active = widget.Single("active", WidgetOperationLifetime.Active, async context =>
        {
            activeStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        });
        await Task.WhenAll(stateStarted.Task, activeStarted.Task);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        Equal(WidgetOperationStatus.Canceled, (await state.Completion).Status);
        True(!active.Completion.IsCompleted,
            "Visible to Interactive must preserve active-lifetime operations.");
        True(widget.Cancel("active"), "Active operation was not registered.");
        Equal(WidgetOperationStatus.Canceled, (await active.Completion).Status);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ActiveLifetimeCancellationIsNormalButRealFaultsPropagateAsync()
    {
        var normal = new ActiveLifetimeCancellationWidget();
        await WidgetTestHost.InitializeAsync(normal);
        await WidgetTestHost.SetLifecycleStateAsync(normal, WidgetLifecycleState.Visible);
        await WidgetTestHost.SetLifecycleStateAsync(normal, WidgetLifecycleState.Background);
        Equal(WidgetLifecycleState.Background, normal.CurrentState);
        await WidgetTestHost.SetLifecycleStateAsync(normal, WidgetLifecycleState.Visible);
        Equal(2, normal.Activations);
        await WidgetTestHost.SetLifecycleStateAsync(normal, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(normal);

        var faulting = new FaultingDeactivationWidget();
        await WidgetTestHost.InitializeAsync(faulting);
        await WidgetTestHost.SetLifecycleStateAsync(faulting, WidgetLifecycleState.Visible);
        var propagated = false;
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(
                faulting, WidgetLifecycleState.Background);
        }
        catch (InvalidOperationException exception) when (exception.Message == "expected fault")
        {
            propagated = true;
        }
        True(propagated, "A real deactivation fault was incorrectly treated as normal cancellation.");
        await WidgetTestHost.DestroyAsync(faulting);
    }

    private static async Task<OperationWidget> CreateVisibleAsync()
    {
        var widget = new OperationWidget();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var observed = Volatile.Read(ref maximum);
        while (value > observed)
        {
            var previous = Interlocked.CompareExchange(ref maximum, value, observed);
            if (previous == observed) return;
            observed = previous;
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class OperationWidget : Widget
    {
        private readonly WidgetTimedMutation _delayed;
        private readonly WidgetTimedMutation _delayedState;
        private readonly WidgetTimedMutation _delayedWidget;

        public OperationWidget(TimeProvider? timeProvider = null)
        {
            _delayed = CreateTimedMutation(
                WidgetOperationLifetime.Active, timeProvider ?? TimeProvider.System);
            _delayedState = CreateTimedMutation(
                WidgetOperationLifetime.State, timeProvider ?? TimeProvider.System);
            _delayedWidget = CreateTimedMutation(
                WidgetOperationLifetime.Widget, timeProvider ?? TimeProvider.System);
        }
        public bool OperationCleanupFinished { get; set; }
        public bool DeactivationSawCleanup { get; private set; }

        public WidgetOperationHandle Single(
            string key,
            WidgetOperationLifetime lifetime,
            Func<WidgetOperationContext, ValueTask> operation) =>
            Operations.RunSingleFlight(key, operation, lifetime);

        public WidgetOperationHandle Latest(
            string key,
            WidgetOperationLifetime lifetime,
            Func<WidgetOperationContext, ValueTask> operation) =>
            Operations.RunLatest(key, operation, lifetime);

        public WidgetOperationHandle Serial(
            string key,
            WidgetOperationLifetime lifetime,
            Func<WidgetOperationContext, ValueTask> operation) =>
            Operations.RunSerial(key, operation, lifetime);

        public WidgetOperationHandle Delayed(TimeSpan delay, Action callback,
            CancellationToken ownerCancellationToken = default) =>
            _delayed.ScheduleLatest(delay, callback, ownerCancellationToken);
        public WidgetOperationHandle DelayedWidget(TimeSpan delay, Action callback) =>
            _delayedWidget.ScheduleLatest(delay, callback);
        public WidgetOperationHandle DelayedState(TimeSpan delay, Action callback) =>
            _delayedState.ScheduleLatest(delay, callback);
        public bool CancelDelayed() => _delayed.Cancel();
        public Task WhenDelayedIdle() => _delayed.WhenIdleAsync();

        public bool Busy(string key) => Operations.IsBusy(key);
        public bool Cancel(string key) => Operations.Cancel(key);
        public void SubscribeFailure(EventHandler<WidgetOperationFailedEventArgs> handler) =>
            Operations.OperationFailed += handler;
        public void SubscribeDelayedFailure(
            EventHandler<WidgetTimedMutationFailedEventArgs> handler) =>
            _delayed.Failed += handler;

        public override WidgetView Render() => new(UI.Text("Operations", "root"));

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
        {
            DeactivationSawCleanup = OperationCleanupFinished;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InvalidTimedMutationWidget : Widget
    {
        public InvalidTimedMutationWidget() =>
            CreateTimedMutation((WidgetOperationLifetime)99);
        public override WidgetView Render() => new(UI.Text("Invalid", "root"));
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) =>
            throw new InvalidOperationException("expected timer failure");
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
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
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
            private TimeSpan _period;
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
                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? null
                    : now + _period;
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

    private sealed class ActiveLifetimeCancellationWidget : Widget
    {
        private CancellationToken _activeLifetime;

        public int Activations { get; private set; }
        public WidgetLifecycleState CurrentState => LifecycleState;

        public override WidgetView Render() => new(UI.Text("Active lifetime", "root"));

        protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
        {
            _activeLifetime = activeLifetime;
            Activations++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken) =>
            ValueTask.FromException(new OperationCanceledException(_activeLifetime));
    }

    private sealed class FaultingDeactivationWidget : Widget
    {
        public override WidgetView Render() => new(UI.Text("Faulting lifetime", "root"));

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken) =>
            ValueTask.FromException(new InvalidOperationException("expected fault"));
    }
}
