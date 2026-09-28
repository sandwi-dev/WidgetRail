using System.Diagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetIndexedCollectionTests
{
    public static async Task Diagnose(Func<Task> run)
    {
        try { await run(); }
        catch (ProtocolValidationException exception)
        {
            throw new Exception(string.Join("; ", exception.Errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")), exception);
        }
    }
    public static async Task Declarations()
    {
        var reads = 0;
        var renders = 0;
        var widget = new Fixture(Options(
            read: (query, start, count, token) => { ++reads; return ValueTask.FromResult<IReadOnlyList<Item>>(Items(start, count)); },
            render: (query, item, context) => { ++renders; return Button(query, item, context); }), 100_000);
        var snapshot = widget.RenderSnapshot("indexed-widget", 1);
        Equal(ProtocolConstants.IndexedCollectionVersion, snapshot.ProtocolVersion, "protocol requirement");
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count, "valid declaration");
        Equal(0, reads, "declaring collection must not fetch all rows");
        Equal(0, renders, "declaring collection must not render all rows");
        var collection = snapshot.Root.Children[1];
        Equal(ViewNodeKind.IndexedCollection, collection.Kind, "collection kind");
        Equal(0, collection.Children.Count, "no inline item trees");
        Equal(100_000, collection.IndexedCollection!.Count, "exact total");
        Equal(widget.Source.Descriptor, SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.Children[1].IndexedCollection, "roundtrip descriptor");
        True(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = ProtocolConstants.IndexedCollectionVersion - 1 }).Count != 0, "older protocol must reject indexed source");
        var range = await widget.ReadIndexedRangeAsync(widget.Request(12, 3), CancellationToken.None);
        Equal(1, reads, "one range fetch"); Equal(3, renders, "only demanded rows rendered");
        Equal("key-12", range.Items[0].Key, "exact first item");
        Equal("scope", range.ScopeId, "inherited scope");
        Equal(3, range.Items.Count, "exact range count");
        await WidgetTestHost.DestroyAsync(widget);
    }

    public static async Task Authority()
    {
        var widget = new Fixture(Options());
        var parent = widget.RenderSnapshot("indexed-widget", 1);
        var request = widget.Request(0, 2);
        var range = await widget.ReadIndexedRangeAsync(request, CancellationToken.None);
        IndexedCollectionContract.ValidateRange(parent, request, range);
        var wrongRanges = new[]
        {
            range with { WidgetInstanceId = "other-widget" }, range with { CollectionId = "other-collection" },
            range with { ScopeId = "other-scope" }, range with { StartIndex = 1 }, range with { DemandId = "other-demand" },
            range with { PinnedLayoutId = "undeclared" }, range with { Source = range.Source with { QueryGeneration = 20 } },
            range with { Items = range.Items.Take(1).ToArray() },
        };
        foreach (var wrong in wrongRanges) Throws<ArgumentException>(() => IndexedCollectionContract.ValidateRange(parent, request, wrong));
        Throws<ArgumentException>(() => IndexedCollectionContract.ResolveScope(parent, request with { CollectionId = "home" }));
        Throws<ArgumentException>(() => IndexedCollectionContract.ResolveScope(parent, request with { PinnedLayoutId = "undeclared" }));
        Throws<ArgumentException>(() => IndexedCollectionContract.ResolveScope(parent, request with { Source = request.Source with { Count = 41 } }));
        foreach (var invalid in new[] { request with { StartIndex = -1 }, request with { Count = 0 }, request with { Count = 65 }, request with { StartIndex = 39 }, request with { StartIndex = int.MaxValue } })
            Throws<ArgumentOutOfRangeException>(() => IndexedCollectionContract.ValidateRequest(invalid));

        var item = range.Items[0];
        var duplicate = range with { Items = [item, item] };
        Throws<ArgumentException>(() => IndexedCollectionContract.ValidateRange(parent, request, duplicate));
        var forgedScope = item with { Root = item.Root with { InputScopeId = "escaped" } };
        Throws<ProtocolValidationException>(() => IndexedCollectionContract.ValidateRange(parent, request, range with { Items = [forgedScope, range.Items[1]] }));
        var inlineParent = parent with { Root = parent.Root with { Children = [parent.Root.Children[0], parent.Root.Children[1] with { Children = [item.Root] }] } };
        True(ViewSnapshotValidator.Validate(inlineParent).Count != 0, "ordinary parent cannot smuggle materialized items");

        var withPresentation = range with { Items = [item with { Root = item.Root with
        {
            Focus = new FocusNeighbors(Up: "home"),
            FocusPresentation = new ViewNode { Id = "item-details", Kind = ViewNodeKind.Text, Text = "Details" },
        } }, range.Items[1]] };
        var consumerParent = parent with { Root = new ViewNode
        {
            Id = "consumer", Kind = ViewNodeKind.FocusPresentationSurface, Children = [parent.Root],
            DefaultFocusPresentation = new ViewNode { Id = "default-details", Kind = ViewNodeKind.Text, Text = "Choose a game" },
        } };
        IndexedCollectionContract.ValidateRange(consumerParent, request, withPresentation);
        Throws<ProtocolValidationException>(() => IndexedCollectionContract.ValidateRange(parent, request, withPresentation));
        var unknownNeighbor = withPresentation with { Items = [withPresentation.Items[0] with { Root = withPresentation.Items[0].Root with
        { Focus = new FocusNeighbors(Up: "missing-header") } }, range.Items[1]] };
        Throws<ProtocolValidationException>(() => IndexedCollectionContract.ValidateRange(consumerParent, request, unknownNeighbor));

        widget.Pinned = true;
        parent = widget.RenderSnapshot("indexed-widget", 2);
        var pinnedRequest = request with { CollectionId = "pinned-collection", PinnedLayoutId = "pinned" };
        var pinned = await widget.ReadIndexedRangeAsync(pinnedRequest, CancellationToken.None);
        Equal("pinned-scope", pinned.ScopeId, "pinned scope");
        Equal("pinned", pinned.PinnedLayoutId, "pinned authority");
        IndexedCollectionContract.ValidateRange(parent, pinnedRequest, pinned);
        Throws<ArgumentException>(() => IndexedCollectionContract.ValidateRange(parent, request, pinned));
        widget.Modal = true;
        parent = widget.RenderSnapshot("indexed-widget", 3);
        var fullRequest = request with { PinnedLayoutId = "host.full-widget" };
        Equal("scope", IndexedCollectionContract.ResolveScope(parent, fullRequest), "full widget pinned excludes modal but retains parent scope");
        var fullRange = await widget.ReadIndexedRangeAsync(fullRequest, CancellationToken.None);
        IndexedCollectionContract.ValidateRange(parent, fullRequest, fullRange);
        await WidgetTestHost.DestroyAsync(widget);
    }

    public static async Task QueryLifetime()
    {
        var entered = Signal(); var release = Signal();
        var widget = new Fixture(Options(read: async (query, start, count, token) =>
        {
            entered.TrySetResult(); await release.Task; return Items(start, count);
        }));
        widget.RenderSnapshot("indexed-widget", 1);
        var oldRequest = widget.Request(0, 2);
        var pending = widget.ReadIndexedRangeAsync(oldRequest, CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        widget.Source.PublishQuery("new-query", 30);
        await ThrowsAsync<InvalidOperationException>(async () => await pending);
        release.TrySetResult();
        Equal(2L, widget.Source.Descriptor.QueryGeneration, "query generation changes");
        Equal(0L, widget.Source.Descriptor.ContentRevision, "new query starts content revision");
        widget.RenderSnapshot("indexed-widget", 2);
        await ThrowsAsync<ArgumentException>(async () => await widget.ReadIndexedRangeAsync(oldRequest, CancellationToken.None));
        var fresh = await widget.ReadIndexedRangeAsync(widget.Request(0, 2), CancellationToken.None);
        Equal("new-query: item-0", fresh.Items[0].Root.Text, "captured query renders current rows");
        var old = widget.Request(0, 1);
        widget.Source.UpdateContent("updated-content");
        Equal(2L, widget.Source.Descriptor.QueryGeneration, "content preserves membership generation");
        Equal(1L, widget.Source.Descriptor.ContentRevision, "content revision advances");
        widget.RenderSnapshot("indexed-widget", 3);
        await ThrowsAsync<ArgumentException>(async () => await widget.ReadIndexedRangeAsync(old, CancellationToken.None));
        var updated = await widget.ReadIndexedRangeAsync(widget.Request(0, 2), CancellationToken.None);
        Equal(fresh.Items[0].Root.Id, updated.Items[0].Root.Id, "content update preserves item ID");
        Equal("updated-content: item-0", updated.Items[0].Root.Text, "new query values reflected");
        await WidgetTestHost.DestroyAsync(widget);
        Throws<OperationCanceledException>(() => widget.Source.PublishQuery("retired", 1));
    }

    public static async Task ProviderValidation()
    {
        foreach (var reader in new Func<string, int, int, CancellationToken, ValueTask<IReadOnlyList<Item>>>[]
        {
            (q, start, count, token) => ValueTask.FromResult<IReadOnlyList<Item>>([]),
            (q, start, count, token) => ValueTask.FromResult<IReadOnlyList<Item>>([new("same", "a"), new("same", "b")]),
        })
        {
            var widget = new Fixture(Options(read: reader)); widget.RenderSnapshot("indexed-widget", 1);
            await ThrowsAsync<InvalidOperationException>(async () => await widget.ReadIndexedRangeAsync(widget.Request(0, 2), CancellationToken.None));
            await WidgetTestHost.DestroyAsync(widget);
        }
        foreach (var renderer in new Func<string, Item, WidgetIndexedItemContext, WidgetElement>[]
        {
            (q, item, context) => UI.Text(item.Title, context.Id("text")),
            (q, item, context) => Button(q, item, context).CollectionItem(new WidgetCollectionItemKey("wrong-key")),
        })
        {
            var widget = new Fixture(Options(render: renderer)); widget.RenderSnapshot("indexed-widget", 1);
            await ThrowsAsync<InvalidOperationException>(async () => await widget.ReadIndexedRangeAsync(widget.Request(0, 2), CancellationToken.None));
            await WidgetTestHost.DestroyAsync(widget);
        }
        Throws<ArgumentOutOfRangeException>(() => new Fixture(Options() with { ReadTimeout = TimeSpan.FromMilliseconds(1) }));
        var stable = new Fixture(Options(read: (q, start, count, token) => ValueTask.FromResult<IReadOnlyList<Item>>([new("same-game", "Game")])));
        stable.RenderSnapshot("indexed-widget", 1);
        var first = await stable.ReadIndexedRangeAsync(stable.Request(0, 1), CancellationToken.None);
        var moved = await stable.ReadIndexedRangeAsync(stable.Request(3, 1), CancellationToken.None);
        Equal(first.Items[0].Root.Id, moved.Items[0].Root.Id, "key identity is independent of requested index");
        await WidgetTestHost.DestroyAsync(stable);
    }

    public static async Task ImmutableAndHostileRanges()
    {
        var children = new List<ViewNode>();
        var classes = new List<string> { "initial-style" };
        var widget = new Fixture(Options(render: (q, item, context) =>
        {
            children.Clear();
            children.Add(new ViewNode { Id = context.Id("text"), Kind = ViewNodeKind.Text, Text = "original" });
            return new RawElement(new ViewNode
            {
                Id = context.Id("surface"), Kind = ViewNodeKind.ActionSurface, ActionId = "play",
                AccessibilityLabel = "Game", ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
                Children = children, StyleClasses = classes,
            });
        })) { Pinned = true };
        var parent = widget.RenderSnapshot("indexed-widget", 1);
        var request = widget.Request(0, 1);
        var first = await widget.ReadIndexedRangeAsync(request, CancellationToken.None);
        children.Clear(); classes.Clear();
        Equal(1, first.Items[0].Root.Children.Count, "published child list is frozen");
        Equal("initial-style", first.Items[0].Root.StyleClasses.Single(), "published styles are frozen");
        Throws<NotSupportedException>(() => ((IList<IndexedCollectionItem>)first.Items).Clear());
        Throws<NotSupportedException>(() => ((IList<ViewNode>)first.Items[0].Root.Children).Clear());
        var secondRequest = request with { CollectionId = "pinned-collection", PinnedLayoutId = "pinned" };
        var second = await widget.ReadIndexedRangeAsync(secondRequest, CancellationToken.None);
        True(first.Items[0].Root.Id != second.Items[0].Root.Id, "same source in different containers needs distinct element IDs");
        Equal("original", first.Items[0].Root.Children[0].Text, "later reads cannot mutate previously returned content");

        var firstItem = first.Items[0];
        foreach (var malformed in new ViewNode[]
        {
            firstItem.Root with { Children = null! },
            firstItem.Root with { Children = new ViewNode[] { null! } },
            firstItem.Root with { Children = Enumerable.Repeat(firstItem.Root.Children[0], IndexedCollectionLimits.MaximumRangeNodes + 1).ToArray() },
        })
            Throws<ArgumentException>(() => IndexedCollectionContract.ValidateRange(parent, request,
                first with { Items = [firstItem with { Root = malformed }] }));
        await WidgetTestHost.DestroyAsync(widget);

        var responsive = new Fixture(Options(render: (q, item, context) => Button(q, item, context).VisibleWhen(ResponsiveVisibility.CompactOnly)));
        responsive.RenderSnapshot("indexed-widget", 1);
        await ThrowsAsync<InvalidOperationException>(async () => await responsive.ReadIndexedRangeAsync(responsive.Request(0, 1), CancellationToken.None));
        await WidgetTestHost.DestroyAsync(responsive);
    }

    private sealed record RawElement(ViewNode Node) : WidgetElement(Node.Id)
    {
        internal override ViewNode ToProtocolNode() => Node;
    }
    public static async Task TimeoutAndConcurrency()
    {
        var allEntered = Signal(); var release = Signal(); var completed = Signal();
        var entered = 0; var finished = 0;
        var widget = new Fixture(Options(read: async (q, start, count, token) =>
        {
            if (Interlocked.Increment(ref entered) == 4) allEntered.TrySetResult();
            await release.Task; // Deliberately ignores cancellation.
            if (Interlocked.Increment(ref finished) == 4) completed.TrySetResult();
            return Items(start, count);
        }) with { ReadTimeout = TimeSpan.FromMilliseconds(300) });
        widget.RenderSnapshot("indexed-widget", 1);
        var reads = Enumerable.Range(0, 4).Select(i => widget.ReadIndexedRangeAsync(widget.Request(i, 1), CancellationToken.None).AsTask()).ToArray();
        try
        {
            await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ThrowsAsync<InvalidOperationException>(async () => await widget.ReadIndexedRangeAsync(widget.Request(5, 1), CancellationToken.None));
            foreach (var read in reads) await ThrowsAsync<TimeoutException>(async () => await read);
            await ThrowsAsync<InvalidOperationException>(async () => await widget.ReadIndexedRangeAsync(widget.Request(6, 1), CancellationToken.None));
            Equal(4, entered, "timed-out providers retain bounded concurrency slots");
        }
        finally { release.TrySetResult(); }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { await widget.ReadIndexedRangeAsync(widget.Request(7, 1), CancellationToken.None); break; }
            catch (InvalidOperationException) when (deadline.Elapsed < TimeSpan.FromSeconds(3)) { await Task.Delay(10); }
        }
        await WidgetTestHost.DestroyAsync(widget);

        var started = Signal();
        var cancellable = new Fixture(Options(read: async (q, start, count, token) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Items(start, count);
        }));
        cancellable.RenderSnapshot("indexed-widget", 1);
        using var cancel = new CancellationTokenSource();
        var pending = cancellable.ReadIndexedRangeAsync(cancellable.Request(0, 1), cancel.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await ThrowsAsync<OperationCanceledException>(async () => await pending);
        await WidgetTestHost.DestroyAsync(cancellable);

        using var renderCancellation = new CancellationTokenSource();
        var cancelDuringRender = new Fixture(Options(render: (q, item, context) =>
        {
            renderCancellation.Cancel();
            return Button(q, item, context);
        }));
        cancelDuringRender.RenderSnapshot("indexed-widget", 1);
        await ThrowsAsync<OperationCanceledException>(async () => await cancelDuringRender.ReadIndexedRangeAsync(
            cancelDuringRender.Request(0, 1), renderCancellation.Token));
        await WidgetTestHost.DestroyAsync(cancelDuringRender);

    }

    private sealed record Item(string Key, string Title);
    private sealed class Fixture : Widget
    {
        public WidgetIndexedCollection<string, Item> Source { get; }
        public bool Pinned { get; set; }
        public bool Modal { get; set; }
        public Fixture(WidgetIndexedCollectionOptions<string, Item> options, int count = 40) => Source = CreateIndexedCollection("games", "initial", count, options);
        public IndexedCollectionRangeRequest Request(int start, int count) => new("collection", Source.Descriptor, start, count, $"demand-{start}");
        public override WidgetView Render()
        {
            var page = new WidgetView(UI.Stack("root", UI.Button("Home", "home", "home"),
                UI.CollectionGrid("collection", Source, 180, 270, "Games")).InputScope("scope"), "home", ActiveInputScopeId: "scope");
            if (Pinned) page = page with { PinnedLayouts = [WidgetView.PinnedLayout("pinned", "Pinned games", new(),
                UI.Stack("pinned-root", UI.CollectionList("pinned-collection", Source, 72, "Pinned games")).InputScope("pinned-scope"),
                initialFocusId: "pinned-collection", activeInputScopeId: "pinned-scope")] };
            return Modal ? page.WithModal(new("dialog", "Details", UI.Button("Play", "play", "play"), "play", "close")) : page;
        }
    }
    private static WidgetIndexedCollectionOptions<string, Item> Options(
        Func<string, int, int, CancellationToken, ValueTask<IReadOnlyList<Item>>>? read = null,
        Func<string, Item, WidgetIndexedItemContext, WidgetElement>? render = null) => new()
    {
        OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        ReadRange = read ?? ((q, start, count, token) => ValueTask.FromResult<IReadOnlyList<Item>>(Items(start, count))),
        ItemKey = item => new(item.Key), RenderItem = render ?? Button,
    };
    private static WidgetElement Button(string query, Item item, WidgetIndexedItemContext context) =>
        UI.Button(query + ": " + item.Title, "play", context.Id("button"));
    private static Item[] Items(int start, int count) => Enumerable.Range(start, count).Select(i => new Item($"key-{i}", $"item-{i}")).ToArray();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, actual {actual}"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        var task = action();
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) != task)
            throw new Exception("The expected failure did not complete within the test watchdog.");
        try { await task; } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
