using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetCollectionItemsTests
{
    private sealed record Row(string Key, string Label, bool Playing = false);
    private sealed record WireElement(ViewNode Node) : WidgetElement(Node.Id)
    {
        internal override ViewNode ToProtocolNode() => Node;
    }

    internal static Task Run()
    {
        var rendered = 0;
        var fail = false;
        var cache = new WidgetCollectionItems<Row>(row => new(row.Key), row =>
        {
            rendered++;
            if (fail && row.Key == "three") throw new InvalidOperationException("factory failure");
            return UI.Button(row.Label, "play." + row.Key, row.Key).Selected(row.Playing).Classes("song");
        });
        Row[] inputs = [new("one", "One"), new("two", "Two"), new("three", "Three")];
        var first = cache.Capture(inputs);
        Check(rendered == 3, "first capture renders each item");
        var same = cache.Capture(inputs.Select(row => row with { }).ToArray());
        Check(rendered == 3 && ReferenceEquals(first[1], same[1]), "equal immutable inputs reuse declarations");
        var reordered = cache.Capture([inputs[2], inputs[0], inputs[1]]);
        Check(rendered == 3 && ReferenceEquals(reordered[0], first[2]), "reorder follows stable key");
        var changed = cache.Capture([inputs[0], inputs[1] with { Playing = true }, inputs[2]]);
        Check(rendered == 4 && ReferenceEquals(changed[0], first[0]) && !ReferenceEquals(changed[1], first[1]),
            "only changed render inputs rebuild their item");
        var prior = Snapshot(first);
        var current = Snapshot(changed);
        Check(prior.Root.Children[1].IsSelected != true && current.Root.Children[1].IsSelected == true,
            "old captured declarations remain immutable");
        Check(current.Root.Children[1].CollectionItemKey == "two", "capture applies stable collection key");
        Check(current.Root.Children[0].StyleClasses.Contains("song"), "capture preserves authored styles");
        var styled = changed[0].Classes("extra");
        Check(styled.ToProtocolNode().StyleClasses.Contains("extra"), "post-capture root style modifiers still apply");
        var beforeDuplicate = rendered;
        Throws<ArgumentException>(() => cache.Capture([inputs[0], inputs[0]]));
        Check(rendered == beforeDuplicate, "duplicate keys rejected before invoking factories");
        fail = true;
        Throws<InvalidOperationException>(() => cache.Capture([
            inputs[0] with { Label = "Changed" }, inputs[1] with { Playing = true }, inputs[2] with { Label = "Fails" }]));
        fail = false;
        var afterFailure = cache.Capture([inputs[0], inputs[1] with { Playing = true }, inputs[2]]);
        Check(ReferenceEquals(afterFailure[0], changed[0]) && ReferenceEquals(afterFailure[1], changed[1]),
            "failed capture does not replace last good cache");
        cache.Capture([inputs[0]]);
        var beforeEvicted = rendered;
        cache.Capture(inputs);
        Check(rendered == beforeEvicted + 2, "evicted descriptors are not retained indefinitely");
        cache.Clear();
        var beforeClear = rendered;
        cache.Capture(inputs);
        Check(rendered == beforeClear + 3, "clear invalidates reuse without invalidating old snapshots");
        Throws<ArgumentOutOfRangeException>(() => cache.Capture(Enumerable.Range(0, 257).Select(i => new Row("key" + i, "Item")).ToArray()));
        var wrongKey = new WidgetCollectionItems<Row>(row => new(row.Key), row =>
            UI.Button(row.Label, "play", row.Key).CollectionItem(new("other")));
        Throws<ArgumentException>(() => wrongKey.Capture(inputs));
        WidgetCollectionItems<Row>? recursive = null;
        recursive = new(row => new(row.Key), row => { recursive!.Clear(); return UI.Button(row.Label, "play", row.Key); });
        Throws<InvalidOperationException>(() => recursive.Capture(inputs));
        var backing = new List<ViewNode> { new() { Id = "copy", Kind = ViewNodeKind.Text, Text = "Before" } };
        var frozen = new WidgetCollectionItems<Row>(row => new(row.Key), row => new WireElement(new ViewNode
        {
            Id = row.Key, Kind = ViewNodeKind.ActionSurface, ActionId = "play", AccessibilityLabel = row.Label,
            ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical, Children = backing,
        }));
        var frozenCapture = frozen.Capture([inputs[0]]);
        backing[0] = backing[0] with { Text = "After" };
        backing.Add(new() { Id = "extra", Kind = ViewNodeKind.Text, Text = "Extra" });
        var frozenNode = Snapshot(frozenCapture).Root.Children[0];
        Check(frozenNode.Children.Count == 1 && frozenNode.Children[0].Text == "Before",
            "capture freezes mutable backing collections instead of retaining live builder lists");
        Throws<NotSupportedException>(() => ((IList<ViewNode>)frozenNode.Children).Clear());
        return Task.CompletedTask;
    }
    private static ViewSnapshot Snapshot(IReadOnlyList<WidgetElement> items) =>
        new WidgetView(UI.CollectionList("songs", 64, items: items.ToArray())).CreateSnapshot("instance", 1);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
