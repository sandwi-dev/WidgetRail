using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

internal static class WidgetPagedResourceTests
{
    public static async Task Run()
    {
        ValidatesOptions();
        await RejectsInactiveLoadsWithoutStickyStateAsync();
        await LoadsAndCoalescesAsync();
        await NavigatesCachesAndRestoresFocusAsync();
        await EvictsLeastRecentlyUsedPagesAsync();
        await LatestRefreshRejectsStaleResultsAsync();
        await SupportsSparsePagesAndRejectsMalformedMetadataAsync();
        await MapsErrorsAndRetriesExactIntentAsync();
        await RetainsPageAndStopsAutomaticRetryAfterAdjacentFailureAsync();
        await ResetRejectsLatePublicationAsync();
        await LifecycleDrainsResourceWorkAsync();
        await PaginationActionsRemainExplicitAsync();
    }

    private static async Task RetainsPageAndStopsAutomaticRetryAfterAdjacentFailureAsync()
    {
        var failAdjacent = true;
        var widget = await StartAsync(Options((offset, limit, _) =>
        {
            if (offset > 0 && failAdjacent)
                throw new InvalidOperationException("private adjacent failure");
            return ValueTask.FromResult(Page(offset, limit, 4));
        }));
        await widget.Resource.EnsureLoaded().Completion;

        var failed = await widget.Resource.Move(
            WidgetPageDirection.Next, "page.wide").Completion;
        Equal(WidgetOperationStatus.Failed, failed.Status);
        Equal(WidgetPagedResourceStatus.Error, widget.Resource.Snapshot.Status);
        Equal(0, widget.Resource.Snapshot.Page!.Offset);
        var blocked = widget.Resource.Paginate(
            UI.VerticalScroll("page.wide", UI.Button("Item", "select", "item")));
        True(blocked.NearEndActionId is null,
            "A failed adjacent page remained in an automatic retry loop.");

        failAdjacent = false;
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Retry().Completion).Status);
        Equal(2, widget.Resource.Snapshot.Page!.Offset);
        Equal("wide.2", widget.Resource.Snapshot.RequestedFocusId);
        widget.Resource.ClearRequestedFocus(invalidate: false);
        True(widget.Resource.Snapshot.RequestedFocusId is null,
            "An acknowledged focus request remained stale.");
        await StopAsync(widget);
    }

    private static async Task SupportsSparsePagesAndRejectsMalformedMetadataAsync()
    {
        var sparse = await StartAsync(Options((offset, limit, _) =>
            ValueTask.FromResult(new WidgetPage<TestItem>(
                [new("partial")], offset, limit, Total: 5))));

        var loaded = await sparse.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Succeeded, loaded.Status);
        True(sparse.Resource.Snapshot.HasNext,
            "A sparse provider page lost its server-defined next offset.");
        await StopAsync(sparse);

        var malformed = await StartAsync(Options((offset, limit, _) =>
            ValueTask.FromResult(new WidgetPage<TestItem>(
                [new("bad-limit")], offset, limit - 1, Total: 1))));

        var result = await malformed.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Failed, result.Status);
        Equal(WidgetPagedResourceStatus.Error, malformed.Resource.Snapshot.Status);
        Equal(WidgetResourceError.InvalidPage, malformed.Resource.Snapshot.Error);
        True(malformed.Resource.Snapshot.Page is null,
            "A malformed provider page was partially published.");
        await StopAsync(malformed);
    }

    private static async Task RejectsInactiveLoadsWithoutStickyStateAsync()
    {
        var calls = 0;
        var widget = new ResourceWidget(Options((offset, limit, _) =>
        {
            calls++;
            return ValueTask.FromResult(Page(offset, limit, 2));
        }));
        await WidgetTestHost.InitializeAsync(widget);

        var load = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.RejectedInactive, load.Admission);
        Equal(WidgetOperationStatus.Rejected, (await load.Completion).Status);
        Equal(0, calls);
        Equal(WidgetPagedResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static void ValidatesOptions()
    {
        Throws<ArgumentOutOfRangeException>(() => new ResourceWidget(
            Options((_, _, _) => ValueTask.FromResult(Page(0, 2, 2))) with
            { PageSize = 0 }));
        Throws<ArgumentOutOfRangeException>(() => new ResourceWidget(
            Options((_, _, _) => ValueTask.FromResult(Page(0, 2, 2))) with
            { MaximumCachedPages = WidgetPagedResource<TestItem>.MaximumCachePages + 1 }));
        Throws<ArgumentException>(() => new ResourceWidget(
            Options((_, _, _) => ValueTask.FromResult(Page(0, 2, 2))) with
            {
                Viewports =
                [
                    Viewport("page.wide", "wide"),
                    Viewport("page.wide", "duplicate"),
                ],
            }));
    }

    private static async Task LoadsAndCoalescesAsync()
    {
        var started = Signal();
        var release = new TaskCompletionSource<WidgetPage<TestItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = await StartAsync(Options(async (offset, _, token) =>
        {
            Equal(0, offset);
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return await release.Task.WaitAsync(token);
        }));

        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        Equal(WidgetPagedResourceStatus.Loading, widget.Resource.Snapshot.Status);
        var duplicate = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.Joined, duplicate.Admission);
        True(ReferenceEquals(first.Completion, duplicate.Completion),
            "Duplicate page requests must share one exact completion task.");
        Equal(1, calls);

        release.TrySetResult(Page(0, 2, 5));
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        var snapshot = widget.Resource.Snapshot;
        Equal(WidgetPagedResourceStatus.Ready, snapshot.Status);
        Equal(2, snapshot.Page!.Items.Count);
        True(snapshot.Page.Items is ICollection<TestItem> collection && collection.IsReadOnly,
            "Published page items must be immutable to callers.");
        await StopAsync(widget);
    }

    private static async Task NavigatesCachesAndRestoresFocusAsync()
    {
        var calls = new List<int>();
        var widget = await StartAsync(Options((offset, limit, _) =>
        {
            calls.Add(offset);
            return ValueTask.FromResult(Page(offset, limit, 6));
        }));
        await widget.Resource.EnsureLoaded().Completion;

        var initialScroll = widget.Resource.Paginate(
            UI.VerticalScroll("page.wide", UI.Text("One", "item")));
        True(initialScroll.NearStartActionId is null,
            "Initial page unexpectedly exposed previous pagination.");
        True(initialScroll.NearEndActionId is not null,
            "Initial page did not expose automatic next pagination.");

        await widget.Resource.Move(WidgetPageDirection.Next, "page.wide").Completion;
        Equal("wide.2", widget.Resource.Snapshot.RequestedFocusId);
        Equal(2, widget.Resource.Snapshot.Page!.Offset);
        var reverse = widget.Resource.Move(WidgetPageDirection.Previous, "page.compact");
        Equal(WidgetOperationAdmission.Completed, reverse.Admission);
        Equal(WidgetOperationStatus.Succeeded, (await reverse.Completion).Status);
        Equal("compact.1", widget.Resource.Snapshot.RequestedFocusId);
        True(calls.SequenceEqual([0, 2]), "Reverse cache navigation called the provider.");
        await StopAsync(widget);
    }

    private static async Task EvictsLeastRecentlyUsedPagesAsync()
    {
        var calls = new List<int>();
        var options = Options((offset, limit, _) =>
        {
            calls.Add(offset);
            return ValueTask.FromResult(Page(offset, limit, 6));
        }) with { MaximumCachedPages = 2 };
        var widget = await StartAsync(options);
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Move(WidgetPageDirection.Next, "page.wide").Completion;
        await widget.Resource.Move(WidgetPageDirection.Next, "page.wide").Completion;
        Equal(2, widget.Resource.CachedPageCount);

        await widget.Resource.Move(WidgetPageDirection.Previous, "page.wide").Completion;
        Equal(3, calls.Count);
        await widget.Resource.Move(WidgetPageDirection.Previous, "page.wide").Completion;
        True(calls.SequenceEqual([0, 2, 4, 0]),
            "The deterministic LRU did not evict and reload the oldest page.");
        await StopAsync(widget);
    }

    private static async Task LatestRefreshRejectsStaleResultsAsync()
    {
        var firstStarted = Signal();
        var firstRelease = new TaskCompletionSource<WidgetPage<TestItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = await StartAsync(Options(async (offset, limit, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.TrySetResult();
                return await firstRelease.Task;
            }
            return Page(offset, limit, 2, "new");
        }));

        var stale = widget.Resource.EnsureLoaded();
        await firstStarted.Task;
        var latest = widget.Resource.Refresh();
        firstRelease.TrySetResult(Page(0, 2, 2, "stale"));
        Equal(WidgetOperationStatus.Superseded, (await stale.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await latest.Completion).Status);
        Equal("new-0", widget.Resource.Snapshot.Page!.Items[0].Id);
        await StopAsync(widget);
    }

    private static async Task MapsErrorsAndRetriesExactIntentAsync()
    {
        var offsets = new List<int>();
        var fail = true;
        var widget = await StartAsync(Options((offset, limit, _) =>
        {
            offsets.Add(offset);
            if (fail) throw new InvalidOperationException("private provider detail");
            return ValueTask.FromResult(Page(offset, limit, 4));
        }));

        var failed = await widget.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Failed, failed.Status);
        Equal(WidgetPagedResourceStatus.Error, widget.Resource.Snapshot.Status);
        Equal("safe_error", widget.Resource.Snapshot.Error!.Code);
        True(!widget.Resource.Snapshot.Error.Message.Contains("private", StringComparison.Ordinal),
            "The resource exposed an unmapped provider exception.");

        fail = false;
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Retry().Completion).Status);
        True(offsets.SequenceEqual([0, 0]), "Retry did not replay the failed page intent.");
        await StopAsync(widget);
    }

    private static async Task ResetRejectsLatePublicationAsync()
    {
        var started = Signal();
        var release = new TaskCompletionSource<WidgetPage<TestItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = await StartAsync(Options(async (_, _, _) =>
        {
            started.TrySetResult();
            return await release.Task;
        }));
        var load = widget.Resource.EnsureLoaded();
        await started.Task;
        widget.Resource.Reset();
        release.TrySetResult(Page(0, 2, 2, "late"));
        Equal(WidgetOperationStatus.Canceled, (await load.Completion).Status);
        Equal(WidgetPagedResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        True(widget.Resource.Snapshot.Page is null,
            "Reset allowed a cancellation-ignoring provider to restore stale data.");
        await StopAsync(widget);
    }

    private static async Task LifecycleDrainsResourceWorkAsync()
    {
        var started = Signal();
        ResourceWidget? widget = null;
        widget = await StartAsync(Options(async (_, _, token) =>
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
        Equal(WidgetPagedResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        True(widget.DeactivationSawCleanup,
            "Paged resource cleanup did not finish before OnDeactivated.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task PaginationActionsRemainExplicitAsync()
    {
        var calls = 0;
        var widget = await StartAsync(Options((offset, limit, _) =>
        {
            calls++;
            return ValueTask.FromResult(Page(offset, limit, 4));
        }));
        _ = widget.RenderSnapshot("resource-before-load", 1);
        Equal(0, calls);
        await widget.Resource.EnsureLoaded().Completion;

        var rendered = widget.RenderSnapshot("resource-ready", 2);
        var scroll = Find(rendered.Root, "page.wide");
        var actionId = scroll.ScrollNearEndActionId;
        True(actionId is not null, "Ready resource did not bind its next action.");
        var handled = widget.Resource.TryHandlePagination(
            new(actionId!, "page.wide"), out var operation);
        True(handled, "The exact configured pagination action was not handled.");
        await operation.Completion;
        Equal(2, calls);
        True(!widget.Resource.TryHandlePagination(
                new(actionId!, "unrelated.scroll"), out _),
            "A pagination action escaped its configured scroll viewport.");
        await StopAsync(widget);
    }

    private static WidgetPagedResourceOptions<TestItem> Options(
        Func<int, int, CancellationToken, ValueTask<WidgetPage<TestItem>>> loader) => new()
        {
            PageSize = 2,
            MaximumCachedPages = 3,
            MaximumCachedItems = 8,
            LoadPage = loader,
            MapError = _ => new("safe_error", "This collection could not be loaded."),
            Viewports =
            [
                Viewport("page.wide", "wide"),
                Viewport("page.compact", "compact"),
            ],
            CacheDuration = TimeSpan.MaxValue,
        };

    private static WidgetPagedViewport<TestItem> Viewport(string scrollId, string prefix) =>
        new(scrollId, (_, index) => $"{prefix}.{index}", $"{prefix}.empty");

    private static WidgetPage<TestItem> Page(
        int offset,
        int limit,
        int total,
        string prefix = "item")
    {
        var count = Math.Min(limit, Math.Max(0, total - offset));
        return new(Enumerable.Range(offset, count)
            .Select(index => new TestItem($"{prefix}-{index}"))
            .ToArray(), offset, limit, total);
    }

    private static async Task<ResourceWidget> StartAsync(
        WidgetPagedResourceOptions<TestItem> options)
    {
        var widget = new ResourceWidget(options);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }

    private static async Task StopAsync(ResourceWidget widget)
    {
        if (widget.TestLifecycleState != WidgetLifecycleState.Background)
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static ViewNode Find(ViewNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
        {
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Node '{id}' was not found.");
    }

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private sealed record TestItem(string Id);

    private sealed class ResourceWidget : Widget
    {
        public ResourceWidget(WidgetPagedResourceOptions<TestItem> options)
        {
            Resource = CreatePagedResource("test.collection", options);
        }

        public WidgetPagedResource<TestItem> Resource { get; }
        public WidgetLifecycleState TestLifecycleState => LifecycleState;
        public bool CleanupFinished { get; set; }
        public bool DeactivationSawCleanup { get; private set; }

        public override WidgetView Render()
        {
            var snapshot = Resource.Snapshot;
            var rows = snapshot.Page?.Items.Select((item, local) =>
                UI.Button(item.Id, "select", $"wide.{snapshot.Page.Offset + local}"))
                .ToArray<WidgetElement>() ?? [];
            var scroll = Resource.Paginate(UI.VerticalScroll("page.wide", rows));
            return new(UI.Stack("root", scroll), snapshot.RequestedFocusId);
        }

        protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
        {
            DeactivationSawCleanup = CleanupFinished;
            return ValueTask.CompletedTask;
        }
    }
}
