using WidgetRail.WidgetSdk;

internal static class WidgetResourceTests
{
    internal static async Task Run()
    {
        ValidatesOptions();
        await RejectsInactiveWithoutStickyLoadingAsync();
        await LoadsCoalescesAndCachesAsync();
        await ExpiredCacheLoadsAgainAsync();
        await RefreshRetainsLastGoodAndRetriesAsync();
        await FailureCanDiscardLastGoodValueAsync();
        await PublishRejectsAStaleReadAsync();
        await ResetRejectsAStaleReadAsync();
        await MapperFailureUsesSafeCopyAsync();
        await LifecycleCancellationDrainsBeforeDeactivationAsync();
    }

    private static void ValidatesOptions()
    {
        Throws<ArgumentOutOfRangeException>(() => new ResourceWidget(Options(_ =>
            ValueTask.FromResult(new TestValue("value"))) with
            { CacheDuration = TimeSpan.FromTicks(-1) }));
        Throws<ArgumentOutOfRangeException>(() => new ResourceWidget(Options(_ =>
            ValueTask.FromResult(new TestValue("value"))) with
            { Lifetime = (WidgetOperationLifetime)99 }));
    }

    private static async Task RejectsInactiveWithoutStickyLoadingAsync()
    {
        var calls = 0;
        var widget = new ResourceWidget(Options(_ =>
        {
            calls++;
            return ValueTask.FromResult(new TestValue("value"));
        }));
        await WidgetTestHost.InitializeAsync(widget);

        var load = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.RejectedInactive, load.Admission);
        Equal(WidgetOperationStatus.Rejected, (await load.Completion).Status);
        Equal(0, calls);
        Equal(WidgetResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task LoadsCoalescesAndCachesAsync()
    {
        var started = Signal();
        var release = new TaskCompletionSource<TestValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = await StartAsync(Options(async token =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return await release.Task.WaitAsync(token);
        }));

        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        Equal(WidgetResourceStatus.Loading, widget.Resource.Snapshot.Status);
        var joined = widget.Resource.Refresh();
        Equal(WidgetOperationAdmission.Joined, joined.Admission);
        True(ReferenceEquals(first.Completion, joined.Completion),
            "Duplicate reads did not share one exact completion task.");

        release.TrySetResult(new("ready"));
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        Equal("ready", widget.Resource.Snapshot.Value!.Name);
        var cached = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.Completed, cached.Admission);
        Equal(1, calls);
        await StopAsync(widget);
    }

    private static async Task RefreshRetainsLastGoodAndRetriesAsync()
    {
        var fail = false;
        var calls = 0;
        var widget = await StartAsync(Options(_ =>
        {
            calls++;
            if (fail) throw new InvalidOperationException("private detail");
            return ValueTask.FromResult(new TestValue($"value-{calls}"));
        }));
        await widget.Resource.EnsureLoaded().Completion;

        fail = true;
        var failed = await widget.Resource.Refresh().Completion;
        Equal(WidgetOperationStatus.Failed, failed.Status);
        Equal(WidgetResourceStatus.Error, widget.Resource.Snapshot.Status);
        Equal("value-1", widget.Resource.Snapshot.Value!.Name);
        Equal("safe_error", widget.Resource.Snapshot.Error!.Code);
        False(widget.Resource.Snapshot.Error.Message.Contains("private", StringComparison.Ordinal),
            "A raw provider exception reached resource UI state.");

        fail = false;
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Retry().Completion).Status);
        Equal("value-3", widget.Resource.Snapshot.Value!.Name);
        await StopAsync(widget);
    }

    private static async Task ExpiredCacheLoadsAgainAsync()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        var widget = await StartAsync(Options(_ =>
            ValueTask.FromResult(new TestValue($"value-{++calls}"))) with
            {
                CacheDuration = TimeSpan.FromSeconds(30),
                TimeProvider = time,
            });

        await widget.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationAdmission.Completed,
            widget.Resource.EnsureLoaded().Admission);
        time.Advance(TimeSpan.FromSeconds(31));
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        Equal(2, calls);
        Equal("value-2", widget.Resource.Snapshot.Value!.Name);
        await StopAsync(widget);
    }

    private static async Task FailureCanDiscardLastGoodValueAsync()
    {
        var fail = false;
        var widget = await StartAsync(Options(_ => fail
            ? ValueTask.FromException<TestValue>(new InvalidOperationException("provider"))
            : ValueTask.FromResult(new TestValue("ready"))) with
            { RetainLastGoodValue = false });
        await widget.Resource.EnsureLoaded().Completion;

        fail = true;
        Equal(WidgetOperationStatus.Failed,
            (await widget.Resource.Refresh().Completion).Status);
        True(widget.Resource.Snapshot.Value is null,
            "A non-retaining resource kept its last good value after failure.");
        await StopAsync(widget);
    }

    private static async Task PublishRejectsAStaleReadAsync()
    {
        var started = Signal();
        var release = new TaskCompletionSource<TestValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = await StartAsync(Options(async _ =>
        {
            started.TrySetResult();
            return await release.Task;
        }));

        var load = widget.Resource.EnsureLoaded();
        await started.Task;
        widget.Resource.Publish(new("subscription"));
        release.TrySetResult(new("stale-read"));

        Equal(WidgetOperationStatus.Canceled, (await load.Completion).Status);
        Equal("subscription", widget.Resource.Snapshot.Value!.Name);
        await StopAsync(widget);
    }

    private static async Task ResetRejectsAStaleReadAsync()
    {
        var started = Signal();
        var release = new TaskCompletionSource<TestValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = await StartAsync(Options(async _ =>
        {
            started.TrySetResult();
            return await release.Task;
        }));

        var load = widget.Resource.EnsureLoaded();
        await started.Task;
        widget.Resource.Reset();
        release.TrySetResult(new("late"));

        Equal(WidgetOperationStatus.Canceled, (await load.Completion).Status);
        Equal(WidgetResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        True(widget.Resource.Snapshot.Value is null,
            "Reset allowed a stale read to republish data.");
        await StopAsync(widget);
    }

    private static async Task MapperFailureUsesSafeCopyAsync()
    {
        var options = Options(_ =>
            ValueTask.FromException<TestValue>(new InvalidOperationException("provider"))) with
            { MapError = _ => throw new InvalidOperationException("mapper") };
        var widget = await StartAsync(options);

        Equal(WidgetOperationStatus.Failed,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        Equal(WidgetResourceError.Unexpected, widget.Resource.Snapshot.Error);
        await StopAsync(widget);
    }

    private static async Task LifecycleCancellationDrainsBeforeDeactivationAsync()
    {
        var started = Signal();
        ResourceWidget? widget = null;
        widget = await StartAsync(Options(async token =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Unreachable.");
            }
            finally
            {
                widget!.CleanupFinished = true;
            }
        }));
        var load = widget.Resource.EnsureLoaded();
        await started.Task;

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        Equal(WidgetOperationStatus.Canceled, (await load.Completion).Status);
        Equal(WidgetResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        True(widget.DeactivationSawCleanup,
            "Resource cleanup did not finish before OnDeactivated.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static WidgetResourceOptions<TestValue> Options(
        Func<CancellationToken, ValueTask<TestValue>> load) => new()
        {
            Load = load,
            MapError = _ => new("safe_error", "This resource could not be loaded."),
            CacheDuration = TimeSpan.MaxValue,
        };

    private static async Task<ResourceWidget> StartAsync(
        WidgetResourceOptions<TestValue> options)
    {
        var widget = new ResourceWidget(options);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }

    private static async Task StopAsync(ResourceWidget widget)
    {
        if (widget.State != WidgetLifecycleState.Background)
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) => True(!condition, message);

    private sealed record TestValue(string Name);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class ResourceWidget : Widget
    {
        internal ResourceWidget(WidgetResourceOptions<TestValue> options) =>
            Resource = CreateResource("test.resource", options);

        internal WidgetResource<TestValue> Resource { get; }
        internal WidgetLifecycleState State => LifecycleState;
        internal bool CleanupFinished { get; set; }
        internal bool DeactivationSawCleanup { get; private set; }

        public override WidgetView Render() => new(UI.Text(
            Resource.Snapshot.Value?.Name ?? "Empty", "resource", "Resource"));

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
        {
            DeactivationSawCleanup = CleanupFinished;
            return ValueTask.CompletedTask;
        }
    }
}
