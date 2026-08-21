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

    private sealed class OperationWidget : Widget
    {
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

        public bool Busy(string key) => Operations.IsBusy(key);
        public bool Cancel(string key) => Operations.Cancel(key);
        public void SubscribeFailure(EventHandler<WidgetOperationFailedEventArgs> handler) =>
            Operations.OperationFailed += handler;

        public override WidgetView Render() => new(UI.Text("Operations", "root"));

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
        {
            DeactivationSawCleanup = OperationCleanupFinished;
            return ValueTask.CompletedTask;
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
