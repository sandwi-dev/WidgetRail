using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

internal static class WidgetCursorResourceTests
{
    public static async Task Run()
    {
        await TraversesTenThousandItemsWithinBound();
        await PreservesAnchorAcrossAppendPrependAndRefresh();
        await RejectsLateDuplicateAndLoopResults();
        ContractIsVersionedOpaqueAndBounded();
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
    }

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
