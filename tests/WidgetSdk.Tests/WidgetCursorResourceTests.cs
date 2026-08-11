using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

internal static class WidgetCursorResourceTests
{
    public static async Task Run()
    {
        await TraversesTenThousandItemsWithinBound();
        await HandlesEmptySparseFinalAndLastGoodError();
        await PreservesAnchorAcrossAppendPrependAndRefresh();
        await DirectionChangeAllowsEvictedRefetch();
        await TraversalHistoryFailsClosedAndRefreshResetsIt();
        await RejectsLateDuplicateAndLoopResults();
        await IdenticalIntentJoinsOneLoad();
        await DifferentIntentReplacesCurrentLoad();
        await ResetCancelsJoinedLoadAndAllowsFreshWork();
        await ActiveLifecycleDrainsJoinedLoad();
        ContractIsVersionedOpaqueAndBounded();
    }

    private static async Task IdenticalIntentJoinsOneLoad()
    {
        var started = Signal();
        var release = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.Started, first.Admission);
        Equal(WidgetOperationAdmission.Joined, joined.Admission);
        True(ReferenceEquals(first.Completion, joined.Completion),
            "An identical cursor intent did not share the exact completion.");
        Equal(1, Volatile.Read(ref calls));
        release.SetResult();
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await joined.Completion).Status);
        Equal(1, Volatile.Read(ref calls));
        await StopAsync(widget);
    }

    private static async Task DifferentIntentReplacesCurrentLoad()
    {
        var firstStarted = Signal();
        var firstRelease = Signal();
        var secondStarted = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstStarted.TrySetResult();
                await firstRelease.Task;
                return Page(0, limit, 200);
            }
            secondStarted.TrySetResult();
            return Page(100, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await firstStarted.Task;
        var replacement = widget.Resource.Refresh();
        Equal(WidgetOperationAdmission.Replaced, replacement.Admission);
        firstRelease.SetResult();
        await secondStarted.Task;
        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await replacement.Completion).Status);
        Equal(2, Volatile.Read(ref calls));
        Equal("item.100", widget.Resource.Snapshot.Items[0].Id);
        await StopAsync(widget);
    }

    private static async Task ResetCancelsJoinedLoadAndAllowsFreshWork()
    {
        var started = Signal();
        var canceled = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                using var registration = token.Register(() => canceled.TrySetResult());
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        widget.Resource.Reset(invalidate: false);
        await canceled.Task;
        Equal(WidgetOperationStatus.Canceled, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Canceled, (await joined.Completion).Status);
        Equal(WidgetPagedResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        Equal(2, Volatile.Read(ref calls));
        await StopAsync(widget);
    }

    private static async Task ActiveLifecycleDrainsJoinedLoad()
    {
        var started = Signal();
        var canceled = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            Interlocked.Increment(ref calls);
            using var registration = token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        var background = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        await canceled.Task;
        await background;
        Equal(WidgetOperationStatus.Canceled, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Canceled, (await joined.Completion).Status);
        Equal(1, Volatile.Read(ref calls));
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task TraversesTenThousandItemsWithinBound()
    {
        await TraverseWithinBound(2_000);
        await TraverseWithinBound(10_000);
    }

    private static async Task TraverseWithinBound(int total)
    {
        var widget = await StartAsync(Options(total));
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        for (var page = 1; page < total / 100; page++)
        {
            Equal(WidgetOperationStatus.Succeeded,
                (await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
            True(widget.Resource.RetainedItemCount <= 200,
                "A 10,000-item provider escaped the 200-item retained window.");
            True(widget.Render().CreateSnapshot("cursor.fixture", page).Root.Children
                    .SelectMany(Flatten).Count() <= 203,
                "A 10,000-item provider serialized an unbounded snapshot.");
        }
        Equal($"item.{total - 200}", widget.Resource.Snapshot.Items[0].Id);
        Equal($"focus.item.{total - 100}", widget.Resource.Snapshot.RequestedFocusId);
        True(widget.Resource.RetainedCursorCount <= WidgetCursorResource<Item>.MaximumCursorHistory,
            "Cursor history exceeded its explicit bound.");
        await StopAsync(widget);
    }

    private static async Task PreservesAnchorAcrossAppendPrependAndRefresh()
    {
        var inserted = false;
        var removed = false;
        var widget = await StartAsync(Options(2_000, transform: items =>
        {
            if (removed) return items.Where(item => item.Id != "item.150").ToArray();
            if (inserted) return [new("item.inserted"), .. items.Take(99)];
            return items;
        }));
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        widget.Resource.SelectAnchor(new("item.150"), invalidate: false);
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal(new WidgetCollectionItemKey("item.150"), widget.Resource.Snapshot.Anchor);
        await widget.Resource.Move(WidgetCursorDirection.Before, "items.list").Completion;
        Equal("focus.item.99", widget.Resource.Snapshot.RequestedFocusId);

        inserted = true;
        await widget.Resource.Refresh().Completion;
        // A refresh whose current window no longer contains the old key uses
        // a deterministic nearest visible fallback, never an ordinal identity.
        Equal(new WidgetCollectionItemKey("item.150"), widget.Resource.Snapshot.Anchor);
        removed = true;
        inserted = false;
        await widget.Resource.Refresh().Completion;
        True(widget.Resource.Snapshot.Anchor is not null,
            "Deletion produced an unanchored non-empty collection.");
        await StopAsync(widget);
    }

    private static async Task HandlesEmptySparseFinalAndLastGoodError()
    {
        var empty = await StartAsync(new()
        {
            PageSize = 4,
            MaximumRetainedItems = 8,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(
                new WidgetCursorPage<Item>([], null, null)),
        });
        Equal(WidgetOperationStatus.Succeeded,
            (await empty.Resource.EnsureLoaded().Completion).Status);
        Equal(WidgetPagedResourceStatus.Ready, empty.Resource.Snapshot.Status);
        Equal(0, empty.Resource.Snapshot.Items.Count);
        True(!empty.Resource.Snapshot.HasBefore && !empty.Resource.Snapshot.HasAfter,
            "An empty final page exposed a transport boundary.");
        True(empty.Resource.Snapshot.Anchor is null,
            "An empty final page retained a phantom anchor.");
        await StopAsync(empty);

        var calls = 0;
        var sparse = await StartAsync(new()
        {
            PageSize = 4,
            MaximumRetainedItems = 8,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ++calls switch
            {
                1 => ValueTask.FromResult(new WidgetCursorPage<Item>(
                    [new("sparse.0")], null, new("sparse.next"))),
                2 => ValueTask.FromResult(new WidgetCursorPage<Item>(
                    [new("sparse.1"), new("sparse.2")], new("sparse.previous"), null)),
                _ => ValueTask.FromException<WidgetCursorPage<Item>>(
                    new InvalidOperationException("fixture unavailable")),
            },
        });
        await sparse.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Succeeded,
            (await sparse.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(3, sparse.Resource.Snapshot.Items.Count);
        Equal("sparse.0", sparse.Resource.Snapshot.Items[0].Id);
        Equal("sparse.2", sparse.Resource.Snapshot.Items[^1].Id);
        True(!sparse.Resource.Snapshot.HasAfter,
            "A partial final page exposed another forward cursor.");
        var lastGood = sparse.Resource.Snapshot;

        Equal(WidgetOperationStatus.Failed,
            (await sparse.Resource.Refresh().Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, sparse.Resource.Snapshot.Status);
        Equal(lastGood.Items.Count, sparse.Resource.Snapshot.Items.Count);
        for (var index = 0; index < lastGood.Items.Count; index++)
            Equal(lastGood.Items[index].Id, sparse.Resource.Snapshot.Items[index].Id);
        Equal(lastGood.Before, sparse.Resource.Snapshot.Before);
        Equal(lastGood.After, sparse.Resource.Snapshot.After);
        Equal(lastGood.Anchor, sparse.Resource.Snapshot.Anchor);
        Equal(WidgetResourceError.InvalidPage, sparse.Resource.Snapshot.Error);
        await StopAsync(sparse);
    }

    private static async Task RejectsLateDuplicateAndLoopResults()
    {
        var first = Signal();
        var release = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(2_000, async: async (cursor, direction, limit, token) =>
        {
            calls++;
            if (calls == 1)
            {
                first.SetResult();
                await release.Task;
                return Page(0, limit, 2_000);
            }
            return Page(100, limit, 2_000);
        }));
        var stale = widget.Resource.EnsureLoaded();
        await first.Task;
        var current = widget.Resource.Refresh();
        release.SetResult();
        await Task.WhenAll(stale.Completion, current.Completion);
        Equal("item.100", widget.Resource.Snapshot.Items[0].Id);

        var duplicate = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(new WidgetCursorPage<Item>(
                [new("same"), new("same")], null, null)),
        });
        Equal(WidgetOperationStatus.Failed,
            (await duplicate.Resource.EnsureLoaded().Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, duplicate.Resource.Snapshot.Status);
        await StopAsync(duplicate);

        var loop = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, _, _) => cursor is null
                ? ValueTask.FromResult(new WidgetCursorPage<Item>([new("a")], null, new("loop")))
                : ValueTask.FromResult(new WidgetCursorPage<Item>([new("b")], null, cursor)),
        });
        await loop.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Failed,
            (await loop.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal("a", loop.Resource.Snapshot.Items[0].Id);
        await StopAsync(loop);

        var cycle = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, _, _) => cursor?.Value switch
            {
                null => ValueTask.FromResult(CyclePage(0, "A")),
                "A" => ValueTask.FromResult(CyclePage(2, "B")),
                "B" => ValueTask.FromResult(CyclePage(4, "C")),
                "C" => ValueTask.FromResult(CyclePage(6, "A")),
                _ => throw new InvalidOperationException("Unexpected cursor."),
            },
        });
        await cycle.Resource.EnsureLoaded().Completion;
        await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal("cycle.2", cycle.Resource.Snapshot.Items[0].Id);
        Equal("cycle.5", cycle.Resource.Snapshot.Items[^1].Id);
        Equal(WidgetOperationStatus.Failed,
            (await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, cycle.Resource.Snapshot.Status);
        Equal("cycle.2", cycle.Resource.Snapshot.Items[0].Id);
        Equal("cycle.5", cycle.Resource.Snapshot.Items[^1].Id);
        True(cycle.Resource.Snapshot.Items.All(item => item.Id != "cycle.6"),
            "A multi-hop cycle partially changed the retained window.");
        True(cycle.Resource.RetainedCursorCount <= WidgetCursorResource<Item>.MaximumCursorHistory,
            "Cycle detection escaped the cursor-history bound.");
        await StopAsync(cycle);
    }

    private static async Task DirectionChangeAllowsEvictedRefetch()
    {
        var widget = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                return ValueTask.FromResult(Page(start, limit, 8));
            },
        });
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal("item.2", widget.Resource.Snapshot.Items[0].Id);

        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(WidgetCursorDirection.Before, "items.list").Completion).Status);
        Equal("item.0", widget.Resource.Snapshot.Items[0].Id);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal("item.2", widget.Resource.Snapshot.Items[0].Id);
        Equal("item.5", widget.Resource.Snapshot.Items[^1].Id);
        await StopAsync(widget);
    }

    private static async Task TraversalHistoryFailsClosedAndRefreshResetsIt()
    {
        var widget = await StartAsync(new()
        {
            PageSize = 1,
            MaximumRetainedItems = 2,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                return ValueTask.FromResult(Page(start, limit, 1_000));
            },
        });
        await widget.Resource.EnsureLoaded().Completion;
        for (var index = 1; index <= 127; index++)
            Equal(WidgetOperationStatus.Succeeded,
                (await widget.Resource.Move(
                    WidgetCursorDirection.After, "items.list").Completion).Status);

        Equal(WidgetCursorResource<Item>.MaximumCursorHistory,
            widget.Resource.RetainedCursorCount);
        var lastGood = widget.Resource.Snapshot.Items.Select(item => item.Id).ToArray();
        Equal(WidgetOperationStatus.Failed,
            (await widget.Resource.Move(
                WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(lastGood.Length, widget.Resource.Snapshot.Items.Count);
        for (var index = 0; index < lastGood.Length; index++)
            Equal(lastGood[index], widget.Resource.Snapshot.Items[index].Id);

        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Refresh().Completion).Status);
        Equal(0, widget.Resource.RetainedCursorCount);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(
                WidgetCursorDirection.After, "items.list").Completion).Status);
        True(widget.Resource.RetainedCursorCount <=
                WidgetCursorResource<Item>.MaximumCursorHistory,
            "A refreshed traversal escaped the cursor-history bound.");
        await StopAsync(widget);
    }

    private static WidgetCursorPage<Item> CyclePage(int start, string after) => new(
        [new($"cycle.{start}"), new($"cycle.{start + 1}")], null, new(after));

    private static void ContractIsVersionedOpaqueAndBounded()
    {
        var artwork = new WidgetArtworkHandle("library.art.42");
        var view = new WidgetView(UI.Stack("root",
            UI.Button("Play", "play", "header.play"),
            UI.VerticalScroll("items.list",
                UI.ResponsiveGrid("items.grid", 160, 4,
                    UI.Artwork(artwork, "art", "Artwork")
                        .CollectionItem(new("item.42")))) with
            {
                CollectionAnchorKey = "item.42",
            }));
        var snapshot = view.CreateSnapshot("cursor.contract", 1);
        Equal(ProtocolConstants.CursorCollectionVersion, snapshot.ProtocolVersion);
        var image = snapshot.Root.Children[1].Children[0].Children[0];
        Equal("library.art.42", image.ArtworkHandle);
        True(image.ImageSource is null, "An opaque artwork handle became fetch authority.");
        Equal("header.play", snapshot.Root.Children[0].Id);
        var legacy = snapshot with { ProtocolVersion = ProtocolConstants.FocusPersistenceVersion };
        True(ViewSnapshotValidator.Validate(legacy).Any(error => error.Code == "feature_requires_version"),
            "Cursor collection fields were accepted by protocol v13.");
        Throws<ArgumentException>(() => new WidgetArtworkHandle("https://bad/path"));
        Throws<ArgumentOutOfRangeException>(() => new FixtureWidget(new()
        {
            PageSize = 100,
            MaximumRetainedItems = 101,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.Unexpected,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(Page(0, 100, 2_000)),
        }));
    }

    private static WidgetCursorResourceOptions<Item> Options(int total,
        Func<IReadOnlyList<Item>, IReadOnlyList<Item>>? transform = null,
        Func<WidgetCollectionCursor?, WidgetCursorDirection?, int, CancellationToken,
            ValueTask<WidgetCursorPage<Item>>>? @async = null) => new()
    {
        PageSize = 100,
        MaximumRetainedItems = 200,
        Viewports = [Viewport()],
        MapError = _ => WidgetResourceError.InvalidPage,
        LoadPage = @async ?? DefaultLoader(total, transform),
    };

    private static Func<WidgetCollectionCursor?, WidgetCursorDirection?, int,
        CancellationToken, ValueTask<WidgetCursorPage<Item>>> DefaultLoader(
        int total, Func<IReadOnlyList<Item>, IReadOnlyList<Item>>? transform) =>
        (cursor, _, limit, _) =>
        {
            var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
            var page = Page(start, limit, total);
            return ValueTask.FromResult(page with
            {
                Items = transform?.Invoke(page.Items) ?? page.Items,
            });
        };

    private static WidgetCursorPage<Item> Page(int start, int limit, int total)
    {
        var count = Math.Min(limit, Math.Max(0, total - start));
        var items = Enumerable.Range(start, count).Select(i => new Item($"item.{i}")).ToArray();
        return new(items,
            start > 0 ? new WidgetCollectionCursor($"c{Math.Max(0, start - limit)}") : null,
            start + count < total ? new WidgetCollectionCursor($"c{start + count}") : null);
    }

    private static WidgetCursorViewport<Item> Viewport() => new(
        "items.list", item => new(item.Id), item => "focus." + item.Id, "empty");

    private static async Task<FixtureWidget> StartAsync(WidgetCursorResourceOptions<Item> options)
    {
        var widget = new FixtureWidget(options);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }
    private static async Task StopAsync(FixtureWidget widget)
    {
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child)) yield return descendant;
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
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record Item(string Id);
    private sealed class FixtureWidget : Widget
    {
        public FixtureWidget(WidgetCursorResourceOptions<Item> options) =>
            Resource = CreateCursorResource("test.cursor", options);
        public WidgetCursorResource<Item> Resource { get; }
        public override WidgetView Render()
        {
            var value = Resource.Snapshot;
            var items = value.Items.Select(item => Resource.PresentItem(item,
                UI.Button(item.Id, "select", "focus." + item.Id))).ToArray();
            var scroll = Resource.Present(UI.VerticalScroll("items.list", items));
            return new(UI.Stack("root", scroll), value.RequestedFocusId);
        }
    }
}
