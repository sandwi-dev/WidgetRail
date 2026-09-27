using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CollectionLayoutTests
{
    internal static Task Run()
    {
        WidgetElement[] items = Enumerable.Range(0, 4)
            .Select(i => UI.Button($"Song {i}", $"play-{i}", $"song-{i}")
                .CollectionItem(new WidgetCollectionItemKey($"key-{i}"))).ToArray();
        var list = new WidgetView(UI.CollectionList("songs", 72, items: items), "song-0")
            .CreateSnapshot("instance", 1);
        Check(list.ProtocolVersion == ProtocolConstants.CollectionLayoutVersion, "list negotiates realization contract");
        Check(list.Root.CollectionAnchorKey == "key-0", "static collections have an initial logical anchor");
        var roundtrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(list));
        Check(roundtrip.Root.CollectionLayout is { Kind: CollectionLayoutKind.List, EstimatedItemExtent: 72 }, "layout roundtrip");
        Check(roundtrip.Root.Children.Select(n => n.Id).SequenceEqual(items.Select(n => n.Id)), "stable order roundtrip");
        var grid = new WidgetView(UI.CollectionGrid("games", 180, 270, 6, items))
            .CreateSnapshot("instance", 2);
        Check(grid.Root.CollectionLayout is { Kind: CollectionLayoutKind.AdaptiveGrid, MinimumColumnWidth: 180, MaximumColumns: 6 }, "grid shape declared");
        Check(new WidgetView(UI.CollectionList("empty", 72)).CreateSnapshot("instance", 1).Root.Children.Count == 0,
            "empty collection is valid");
        Check(new WidgetView(UI.CollectionList("horizontal", 72, ScrollAxis.Horizontal, items))
            .CreateSnapshot("instance", 1).Root.ScrollAxis == ScrollAxis.Horizontal, "horizontal list contract");
        Invalid(list with { ProtocolVersion = 58 }, "feature_requires_version");
        Invalid(list with { Root = list.Root with { Kind = ViewNodeKind.Stack } }, "invalid_collection_layout");
        Invalid(list with { Root = list.Root with { CollectionLayout = list.Root.CollectionLayout! with
            { EstimatedItemExtent = double.NaN } } }, "invalid_collection_estimate");
        Invalid(list with { Root = list.Root with { CollectionLayout = list.Root.CollectionLayout! with
            { MaximumColumns = 3 } } }, "invalid_collection_grid");
        Invalid(grid with { Root = grid.Root with { ScrollAxis = ScrollAxis.Horizontal } }, "invalid_collection_grid");
        Invalid(grid with { Root = grid.Root with { CollectionLayout = grid.Root.CollectionLayout! with
            { MinimumColumnWidth = null } } }, "invalid_collection_grid");
        Invalid(list with { Root = list.Root with { Children = [list.Root.Children[0] with
            { CollectionItemKey = null }] } }, "invalid_collection_item");
        Invalid(list with { Root = list.Root with { Children = [list.Root.Children[0] with
            { VisibleWhen = ResponsiveVisibility.CompactOnly }] } }, "invalid_collection_item");
        Invalid(list with { Root = list.Root with { Children = [list.Root.Children[0], list.Root.Children[1] with
            { CollectionItemKey = "key-0" }] } }, "duplicate_collection_item_key");
        Throws<ArgumentException>(() => UI.CollectionList("bad", 72, items: [UI.Text("text", "text")]));
        Throws<ArgumentException>(() => UI.CollectionList("bad", 72, items: [items[0], items[0]]));
        Throws<ArgumentOutOfRangeException>(() => UI.CollectionList("bad", double.PositiveInfinity));
        Throws<ArgumentOutOfRangeException>(() => UI.CollectionGrid("bad", 0, 72));
        Throws<ArgumentOutOfRangeException>(() => UI.CollectionGrid("bad", 100, 72, 0));

        var changed = list with { Sequence = 2, Root = list.Root with
            { CollectionLayout = list.Root.CollectionLayout! with { EstimatedItemExtent = 88 } } };
        var publication = WidgetPresentationDiff.Create(list, changed, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1,
            PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(publication.Update?.Operations.Single().Properties?.Single().Property == PresentationProperty.CollectionLayout,
            $"layout changes use the atomic property path ({publication.FallbackReason})");
        return Task.CompletedTask;
    }
    private static void Invalid(ViewSnapshot snapshot, string code) =>
        Check(ViewSnapshotValidator.Validate(snapshot).Any(e => e.Code == code), $"expected {code}");
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
