using WidgetRail.WidgetSdk;

internal static class WidgetQueryResourceTests
{
    internal static async Task Run()
    {
        await LatestQueryWaitsForRetiredProvider();
        await ScopeCancellationRetainsLastGoodAndRejectsLateRead();
        await DifferentScopeDoesNotJoinRetiredRead();
        await FailuresRetryAndResetReleasesValue();
        await InactiveAndCanceledOwnersCannotStart();
        await CapacityRejectionSettlesAsError();
        await SynchronousScopeCancellationIsNotFailure();
        await ConcurrentCancellationAndSnapshotReads();
        await CompletedScopeRegistrationDoesNotRetainQuery();
        await ValueTypesHaveExplicitPresence();
    }

    private static async Task LatestQueryWaitsForRetiredProvider()
    {
        var entered = Signal();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var active = 0;
        var peak = 0;
        var widget = await Start(async (query, _) =>
        {
            peak = Math.Max(peak, Interlocked.Increment(ref active));
            try
            {
                calls.Add(query);
                if (query == "A") { entered.SetResult(); return await release.Task; }
                return query;
            }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            var first = widget.Resource.EnsureLoaded("A");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var joined = widget.Resource.EnsureLoaded("A");
            Equal(WidgetOperationAdmission.Joined, joined.Admission);
            Equal(first.Completion, joined.Completion);
            var second = widget.Resource.EnsureLoaded("B");
            var third = widget.Resource.EnsureLoaded("C");
            Equal(WidgetResourceStatus.NotLoaded, widget.Resource.GetSnapshot("A").Status);
            Equal(WidgetResourceStatus.Loading, widget.Resource.GetSnapshot("C").Status);
            release.SetResult("late A");
            await Task.WhenAll(first.Completion, second.Completion, third.Completion).WaitAsync(TimeSpan.FromSeconds(3));
            Equal("A,C", string.Join(',', calls));
            Equal(1, peak);
            Equal("C", widget.Resource.GetSnapshot("C").Value);
            Equal(WidgetOperationStatus.Superseded, (await second.Completion).Status);
        }
        finally { release.TrySetResult("cleanup"); await Stop(widget); }
    }

    private static async Task ScopeCancellationRetainsLastGoodAndRejectsLateRead()
    {
        var entered = Signal();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = await Start(async (_, _) =>
        {
            if (++calls == 1) return "good";
            entered.TrySetResult();
            return await release.Task;
        });
        using var scope = new CancellationTokenSource();
        try
        {
            await widget.Resource.EnsureLoaded("game", scope.Token).Completion;
            var refresh = widget.Resource.EnsureLoaded("game", scope.Token, forceRefresh: true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Equal(WidgetResourceStatus.Refreshing, widget.Resource.GetSnapshot("game").Status);
            scope.Cancel();
            Equal(WidgetResourceStatus.Ready, widget.Resource.GetSnapshot("game").Status);
            Equal("good", widget.Resource.GetSnapshot("game").Value);
            release.SetResult("late");
            await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Equal("good", widget.Resource.GetSnapshot("game").Value);
            Equal(WidgetOperationAdmission.Completed, widget.Resource.EnsureLoaded("game").Admission);
        }
        finally { release.TrySetResult("cleanup"); await Stop(widget); }
    }

    private static async Task DifferentScopeDoesNotJoinRetiredRead()
    {
        var entered = Signal();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = await Start(async (_, _) =>
        {
            if (++calls == 1) { entered.SetResult(); return await release.Task; }
            return "new owner";
        });
        using var oldScope = new CancellationTokenSource();
        using var newScope = new CancellationTokenSource();
        try
        {
            var old = widget.Resource.EnsureLoaded("same", oldScope.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var next = widget.Resource.EnsureLoaded("same", newScope.Token);
            oldScope.Cancel(); // Must not cancel the new owner.
            release.SetResult("old owner");
            await Task.WhenAll(old.Completion, next.Completion).WaitAsync(TimeSpan.FromSeconds(3));
            Equal("new owner", widget.Resource.GetSnapshot("same").Value);
            Equal(2, calls);
        }
        finally { release.TrySetResult("cleanup"); await Stop(widget); }
    }

    private static async Task FailuresRetryAndResetReleasesValue()
    {
        var fail = false;
        var widget = await Start((query, _) => fail ? ValueTask.FromException<string>(new Exception("private")) : ValueTask.FromResult(query));
        try
        {
            await widget.Resource.EnsureLoaded("A").Completion;
            fail = true;
            await widget.Resource.EnsureLoaded("A", forceRefresh: true).Completion;
            Equal("A", widget.Resource.GetSnapshot("A").Value);
            Equal("safe", widget.Resource.GetSnapshot("A").Error!.Code);
            fail = false;
            await widget.Resource.EnsureLoaded("A").Completion;
            Equal(WidgetResourceStatus.Ready, widget.Resource.GetSnapshot("A").Status);
            widget.Resource.Reset();
            Equal(WidgetResourceStatus.NotLoaded, widget.Resource.GetSnapshot("A").Status);
        }
        finally { await Stop(widget); }
    }

    private static async Task InactiveAndCanceledOwnersCannotStart()
    {
        var calls = 0;
        var widget = new QueryWidget((query, _) => { calls++; return ValueTask.FromResult(query); });
        await WidgetTestHost.InitializeAsync(widget);
        try
        {
            var inactive = widget.Resource.EnsureLoaded("A");
            await inactive.Completion;
            Equal(WidgetOperationAdmission.RejectedInactive, inactive.Admission);
            Equal(WidgetResourceStatus.NotLoaded, widget.Resource.GetSnapshot("A").Status);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
            await widget.Resource.EnsureLoaded("A").Completion;
            using var scope = new CancellationTokenSource();
            scope.Cancel();
            Equal(WidgetOperationAdmission.RejectedInactive, widget.Resource.EnsureLoaded("B", scope.Token).Admission);
            Equal("A", widget.Resource.GetSnapshot("A").Value);
            Equal(1, calls);
        }
        finally { await Stop(widget); }
    }

    private static async Task CapacityRejectionSettlesAsError()
    {
        var widget = await Start((query, _) => ValueTask.FromResult(query));
        try
        {
            widget.FillCapacity();
            var rejected = widget.Resource.EnsureLoaded("A");
            await rejected.Completion;
            Equal(WidgetOperationAdmission.RejectedCapacity, rejected.Admission);
            Equal(WidgetResourceStatus.Error, widget.Resource.GetSnapshot("A").Status);
            Equal("resource_busy", widget.Resource.GetSnapshot("A").Error!.Code);
        }
        finally { await Stop(widget); }
    }

    private static async Task SynchronousScopeCancellationIsNotFailure()
    {
        using var scope = new CancellationTokenSource();
        var widget = await Start((_, token) =>
        {
            scope.Cancel();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult("unreachable");
        });
        try
        {
            var result = await widget.Resource.EnsureLoaded("A", scope.Token).Completion;
            Equal(WidgetOperationStatus.Canceled, result.Status);
            Equal(0, widget.Failures);
            Equal(WidgetResourceStatus.NotLoaded, widget.Resource.GetSnapshot("A").Status);
        }
        finally { await Stop(widget); }
    }

    private static async Task ConcurrentCancellationAndSnapshotReads()
    {
        var widget = await Start(async (query, token) =>
        {
            await Task.Delay(1, token);
            return query;
        });
        widget.Invalidated += (_, _) =>
        {
            foreach (var key in new[] { "A", "B" })
            {
                var snapshot = widget.Resource.GetSnapshot(key);
                if (snapshot.HasValue) Equal(key, snapshot.Value);
            }
        };
        try
        {
            for (var i = 0; i < 200; ++i)
            {
                using var scope = new CancellationTokenSource();
                var first = widget.Resource.EnsureLoaded("A", scope.Token, forceRefresh: true);
                await Task.WhenAll(Task.Run(scope.Cancel), Task.Run(async () =>
                    await widget.Resource.EnsureLoaded("B", forceRefresh: true).Completion))
                    .WaitAsync(TimeSpan.FromSeconds(3));
                await first.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
        finally { await Stop(widget); }
    }

    private static async Task CompletedScopeRegistrationDoesNotRetainQuery()
    {
        using var scope = new CancellationTokenSource();
        var widget = await Start((query, _) => ValueTask.FromResult(query));
        try
        {
            var weak = PublishAndReset(widget, scope.Token);
            await widget.Resource.WhenIdleAsync();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Equal(false, weak.IsAlive);
            // Keep the scope alive: an undisposed callback would retain its request/query.
            scope.Cancel();
        }
        finally { await Stop(widget); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference PublishAndReset(QueryWidget widget, CancellationToken scope)
    {
        var query = new string('q', 1024);
        var weak = new WeakReference(query);
        widget.Resource.EnsureLoaded(query, scope).Completion.GetAwaiter().GetResult();
        widget.Resource.Reset();
        return weak;
    }

    private static async Task ValueTypesHaveExplicitPresence()
    {
        var widget = new ValueWidget();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        try
        {
            Equal(false, widget.Resource.GetSnapshot("A").HasValue);
            var load = widget.Resource.EnsureLoaded("A");
            Equal(WidgetResourceStatus.Loading, widget.Resource.GetSnapshot("A").Status);
            Equal(false, widget.Resource.GetSnapshot("A").HasValue);
            widget.Release.SetResult(0);
            await load.Completion;
            Equal(true, widget.Resource.GetSnapshot("A").HasValue);
            Equal(0, widget.Resource.GetSnapshot("A").Value);
            Equal(false, widget.Resource.GetSnapshot("B").HasValue);
            widget.Fail = true;
            await widget.Resource.EnsureLoaded("A", forceRefresh: true).Completion;
            Equal(false, widget.Resource.GetSnapshot("A").HasValue);
            Equal(WidgetResourceStatus.Error, widget.Resource.GetSnapshot("A").Status);
        }
        finally { widget.Release.TrySetResult(0); await WidgetTestHost.DestroyAsync(widget); }
    }

    private sealed class ValueWidget : Widget
    {
        internal readonly TaskCompletionSource<int> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Fail;
        internal WidgetQueryResource<string, int> Resource { get; }
        internal ValueWidget() => Resource = CreateQueryResource<string, int>("value-query", new()
        {
            Load = (_, _) => Fail ? ValueTask.FromException<int>(new Exception("failure")) : new(Release.Task),
            MapError = _ => new("safe", "Could not load."), RetainLastGoodValue = false,
        });
        public override WidgetView Render() => new(UI.Text("Value", "value-view"));
    }

    private static async Task<QueryWidget> Start(Func<string, CancellationToken, ValueTask<string>> load)
    {
        var widget = new QueryWidget(load);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }
    private static async Task Stop(QueryWidget widget)
    {
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    private sealed class QueryWidget : Widget
    {
        internal WidgetQueryResource<string, string> Resource { get; }
        internal int Failures;
        internal QueryWidget(Func<string, CancellationToken, ValueTask<string>> load)
        {
            Resource = CreateQueryResource<string, string>("query", new()
            { Load = load, MapError = _ => new("safe", "Could not load."), CacheDuration = TimeSpan.MaxValue });
            Operations.OperationFailed += (_, _) => Failures++;
        }
        internal void FillCapacity()
        {
            for (var i = 0; i < WidgetOperations.MaximumTrackedOperations; i++)
                Operations.RunLatest($"busy-{i}", async context => await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken));
        }
        public override WidgetView Render() => new(UI.Text("Query", "query-view"));
    }
}
