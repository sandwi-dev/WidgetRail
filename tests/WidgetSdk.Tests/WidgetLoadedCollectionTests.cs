using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetLoadedCollectionTests
{
    internal static async Task Run()
    {
        var input = Enumerable.Range(0, 10000).Select(index => new Item("item-" + index, "Value " + index)).ToArray();
        var widget = new Fixture(input);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        try
        {
            input[0] = new("mutated", "Caller changed its array");
            Check(widget.Source.Items[0].Key == "item-0", "Published list borrowed the caller's mutable array.");
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "loaded");
            Check(widget.Renders == 0 && widget.Source.Descriptor.Count == 10000, "Loaded list eagerly rendered rows or capped count.");
            using var deep = await host.AcquireAsync("items", 9999, 1);
            Check(widget.Renders == 1 && deep.Range.Items[0].Key == "item-9999", "Deep range was not lazy.");
            var before = widget.Source.Descriptor;
            Check(widget.Source.Publish("initial", "context", widget.Source.Items) == WidgetLoadedCollectionChange.Unchanged &&
                widget.Source.Descriptor == before, "Unchanged publication changed authority.");
            var copy = widget.Source.Items.ToArray();
            copy[9999] = copy[9999] with { Label = "Updated" };
            var keyReads = widget.KeyReads;
            Check(widget.Source.Publish("initial", "updated-context", copy) == WidgetLoadedCollectionChange.Content,
                "Content change replaced membership.");
            Check(widget.KeyReads - keyReads == 1, "Updating one immutable item recomputed every occurrence key.");
            Check(widget.Source.Descriptor.QueryGeneration == before.QueryGeneration &&
                widget.Source.Descriptor.ContentRevision > before.ContentRevision, "Content update lost query identity.");
            Check(deep.RouteAction("item-9999", ControllerButton.A) is null, "Retired content accepted an old row action.");
            host.PublishSnapshot();
            using var updated = await host.AcquireAsync("items", 9999, 1);
            Check(updated.Range.Items[0].Root.Text == "updated-context:Updated", "Range did not capture new context/item content.");
            Check(updated.RouteAction("item-9999", ControllerButton.A) == WidgetOperationAdmission.Enqueued,
                "Current loaded row did not admit its action.");
            Check(await widget.Action.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "updated-context:Updated",
                "Action did not retain its captured context/item.");

            var oldQuery = widget.Source.Descriptor.QueryGeneration;
            Check(widget.Source.Publish("different-filter", "updated-context", copy) == WidgetLoadedCollectionChange.Query &&
                widget.Source.Descriptor.QueryGeneration > oldQuery, "Same keys concealed a semantic query change.");
            oldQuery = widget.Source.Descriptor.QueryGeneration;
            Check(widget.Source.Publish("different-filter", "updated-context", copy, forceReset: true) == WidgetLoadedCollectionChange.Query &&
                widget.Source.Descriptor.QueryGeneration > oldQuery, "Explicit reset did not retire authority.");
            widget.Source.Publish("small", "small-context", [new("duplicate.1", "Same"), new("duplicate.2", "Same")]);
            host.PublishSnapshot();
            Check(Find(host.CurrentSnapshot.Root, "items").IndexedOccurrenceKeys is null, "Semantic replacement bridged occurrence memory from a different context.");
            widget.Source.Publish("small", "small-context", [new("duplicate.1", "Same"), new("duplicate.2", "Same"), new("third", "Third")]);
            host.PublishSnapshot();
            Check(Find(host.CurrentSnapshot.Root, "items").IndexedOccurrenceKeys!.Count == 3,
                "Small complete query did not publish bounded continuity keys.");
            widget.Source.Publish("small", "small-context", [new("duplicate.2", "Same"), new("duplicate.1", "Same")]);
            Check(widget.Source.Descriptor.ContentRevision == 0, "Reordering was misclassified as content.");
            try
            {
                widget.Source.Publish("bad", "bad", [new("same", "First"), new("same", "Second")]);
                throw new InvalidOperationException("Duplicate occurrence keys were accepted.");
            }
            catch (ArgumentException) { }
            Check(widget.Source.Items.Count == 2 && widget.Source.Context == "small-context", "Invalid publication changed committed state.");
            widget.Source.Publish("empty", "empty", []);
            Check(widget.Source.Descriptor.Count == 0, "Empty publication retained rows.");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        var retained = new RetentionFixture();
        await WidgetTestHost.InitializeAsync(retained);
        var contexts = PublishRetiredContexts(retained);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(contexts.Take(contexts.Count - 1).All(reference => !reference.IsAlive) && contexts[^1].IsAlive,
            "Loaded source retained retired publication contexts.");
        GC.KeepAlive(retained);
        await WidgetTestHost.DestroyAsync(retained);
    }

    private static List<WeakReference> PublishRetiredContexts(RetentionFixture fixture)
    {
        var references = new List<WeakReference>();
        for (var i = 0; i < 64; ++i)
        {
            var context = new object();
            fixture.Source.Publish("identity", context, fixture.Source.Items);
            references.Add(new(context));
        }
        return references;
    }

    private sealed class RetentionFixture : Widget
    {
        internal readonly WidgetLoadedCollection<object, int> Source;
        internal RetentionFixture() => Source = CreateLoadedCollection<object, int>("retention", "identity", new object(), [1], new()
        {
            ItemKey = value => new("item-" + value),
            RenderItem = (_, _, ids) => UI.Button("Item", "open", ids.Id("row")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        });
        public override WidgetView Render() => new(UI.Stack("root"));
    }

    private sealed record Item(string Key, string Label);
    private sealed class Fixture : Widget
    {
        internal readonly WidgetLoadedCollection<string, Item> Source;
        internal int Renders;
        internal int KeyReads;
        internal readonly TaskCompletionSource<string> Action = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Fixture(IReadOnlyList<Item> items) => Source = CreateLoadedCollection<string, Item>("loaded-source", "initial", "context", items, new()
        {
            PreserveOccurrences = true,
            ItemKey = item => { KeyReads++; return new(item.Key); },
            RenderItem = (context, item, ids) => { Renders++; return UI.Button(context + ":" + item.Label, "open", ids.Id("row")); },
            OnAction = (context, item, _, _) => { Action.TrySetResult(context + ":" + item.Label); return ValueTask.CompletedTask; },
        });
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("items", Source, 48, "Items")), "items", ActiveInputScopeId: "root");
    }

    private static ViewNode Find(ViewNode node, string id) => node.Id == id ? node : node.Children.Select(child => FindOrNull(child, id)).First(result => result is not null)!;
    private static ViewNode? FindOrNull(ViewNode node, string id) => node.Id == id ? node : node.Children.Select(child => FindOrNull(child, id)).FirstOrDefault(result => result is not null);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
