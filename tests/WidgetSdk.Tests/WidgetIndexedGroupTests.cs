using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetIndexedGroupTests
{
    public static async Task Run()
    {
        var widget = new Fixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        try
        {
            var groups = new[] { new IndexedCollectionGroup("first", "Recommendations", 10),
                new("empty", "Empty section", 0), new("second", "Recommendations", 30) };
            var declaration = UI.CollectionGrid("collection", widget.Source, 130, 170, "Home", 4).Grouped(groups);
            groups[0] = new("mutated", "Mutation", 10);
            Check(declaration.Groups![0].Key == "first", "SDK freezes the caller's array");
            Throws<NotSupportedException>(() => ((IList<IndexedCollectionGroup>)declaration.Groups!)[0] = groups[0]);
            var snapshot = new WidgetView(declaration, "collection").CreateSnapshot("groups", 1);
            Check(snapshot.ProtocolVersion == 62 && snapshot.Root.Children.Count == 0, "grouping version with no inline item trees");
            Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.IndexedGroups!.SequenceEqual(declaration.Groups!), "group roundtrip");
            Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 61 }).Count > 0, "old peers reject groups");
            Check(ViewSnapshotValidator.Validate(snapshot).Count == 0, "empty groups and repeated headings allowed");
            var list = new WidgetView(UI.CollectionList("list", widget.Source, 60, "Songs").Grouped(declaration.Groups!.ToArray()), "list").CreateSnapshot("groups", 2);
            Check(ViewSnapshotValidator.Validate(list).Count == 0, "vertical lists support the same partition");
            Throws<ArgumentException>(() => UI.CollectionList("horizontal", widget.Source, 60, "Songs", ScrollAxis.Horizontal).Grouped(declaration.Groups!.ToArray()));
            var max = Enumerable.Range(0, 256).Select(index => new IndexedCollectionGroup($"group-{index}", "Section", index == 0 ? 40 : 0)).ToArray();
            Check(UI.CollectionList("max", widget.Source, 60, "Songs").Grouped(max).Groups!.Count == 256, "maximum group bound accepted");

            var invalid = new IndexedCollectionGroup[][]
            {
                [], [new("first", "Section", 39)], [new("first", "Section", 41)],
                [new("same", "A", 20), new("same", "B", 20)],
                [new("bad key", "Section", 40)], [new("first", " ", 40)],
                [new("first", "Line\nbreak", 40)], [new("first", new string('h', 4097), 40)],
                [new("first", "Section", -1), new("second", "Section", 41)],
                [new("first", "Section", int.MaxValue), new("second", "Section", int.MaxValue)],
                [null!], [.. max, new("overflow", "Section", 0)],
            };
            foreach (var value in invalid)
            {
                Throws<ArgumentException>(() => UI.CollectionGrid("collection", widget.Source, 130, 170, "Home").Grouped(value));
                Check(ViewSnapshotValidator.Validate(snapshot with { Root = snapshot.Root with { IndexedGroups = value } }).Count > 0,
                    "raw protocol rejects malformed partition");
            }
            Check(ViewSnapshotValidator.Validate(snapshot with { Root = snapshot.Root with { ScrollAxis = ScrollAxis.Horizontal } }).Count > 0,
                "raw horizontal grouping rejected");
            var ordinary = new WidgetView(UI.Text("Plain", "plain")).CreateSnapshot("groups", 3);
            Check(ViewSnapshotValidator.Validate(ordinary with { ProtocolVersion = 62, Root = ordinary.Root with { IndexedGroups = [] } }).Count > 0,
                "non-indexed node cannot carry grouping");
            var nodes = 0;
            var mutable = declaration.Groups!.ToList();
            var frozen = WidgetDeclarationSnapshot.Freeze(snapshot.Root with { IndexedGroups = mutable }, 1, ref nodes);
            mutable.Clear();
            Check(frozen.IndexedGroups!.Count == 3, "retained declarations clone grouping metadata");

            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "groups-widget");
            using var lease = await host.AcquireAsync("collection", 8, 5);
            Check(lease.Range.Items.Select(item => item.Key).SequenceEqual(Enumerable.Range(8, 5).Select(index => $"item-{index}")),
                "materialized range crosses group boundary and empty group without changing flat order");
            var unchanged = host.CurrentSnapshot;
            var republished = host.PublishSnapshot();
            var noOp = WidgetPresentationDiff.Create(unchanged, republished, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", unchanged.Sequence,
                PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate);
            Check(noOp.Update?.Operations.Count == 0, "equal frozen group values do not replace the collection on ordinary updates");
            var before = host.CurrentSnapshot;
            widget.Groups = [new("all", "All songs", 40)];
            var after = host.PublishSnapshot();
            Check(lease.Range.Source == after.Root.Children[0].IndexedCollection, "regrouping preserves source identity");
            Check(lease.RouteAction("item-12", ControllerButton.A) == WidgetOperationAdmission.Enqueued, "existing captured item remains actionable after regrouping");
            var action = await widget.Action.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(action.FocusedCollectionItem?.Index == 12, "group headers never alter logical focus indices");
            AssertTransport(before, after);
            var renamedBefore = after;
            widget.Groups = [new("all", "Renamed heading", 40)];
            AssertTransport(renamedBefore, host.PublishSnapshot());
            var sourceBefore = host.CurrentSnapshot;
            widget.Source.PublishQuery("replacement", 40);
            AssertTransport(sourceBefore, host.PublishSnapshot());

            widget.Source.PublishQuery("empty", 0); widget.Groups = [];
            Check(host.PublishSnapshot().Root.Children[0].IndexedGroups is { Count: 0 }, "empty query accepts zero groups");
            widget.Groups = [new("empty", "Nothing yet", 0)];
            Check(ViewSnapshotValidator.Validate(host.PublishSnapshot()).Count == 0, "empty query accepts hidden empty group");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static void AssertTransport(ViewSnapshot before, ViewSnapshot after)
    {
        const string generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var publication = WidgetPresentationDiff.Create(before, after, generation, before.Sequence,
            PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(publication.Update is not null, "small grouped change stays incremental");
        Check(publication.Update!.Operations.Any(operation => operation.Kind == PresentationUpdateOperationKind.ReplaceSubtree && operation.TargetId == "collection"),
            "group or source metadata uses atomic collection replacement, never a dropped unsupported property");
        var received = PresentationUpdateMaterializer.Apply(before,
            PresentationUpdateJson.Deserialize(PresentationUpdateJson.Serialize(publication.Update)), generation);
        Check(received.Root.Children[0].IndexedCollection == after.Root.Children[0].IndexedCollection &&
            received.Root.Children[0].IndexedGroups!.SequenceEqual(after.Root.Children[0].IndexedGroups!), "materialized delta preserves group/source metadata");
    }

    private sealed class Fixture : Widget
    {
        public readonly WidgetIndexedCollection<string, int> Source;
        public IndexedCollectionGroup[] Groups = [new("first", "Recommendations", 10), new("empty", "Empty", 0), new("second", "More", 30)];
        public readonly TaskCompletionSource<WidgetActionEvent> Action = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Fixture() => Source = CreateIndexedCollection<string, int>("items", "initial", 40, new()
        {
            ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start, count).ToArray()),
            ItemKey = item => new($"item-{item}"),
            RenderItem = (_, item, context) => UI.Button($"Song {item}", "play", context.Id("row")),
            OnAction = (_, _, action, _) => { Action.TrySetResult(action); return ValueTask.CompletedTask; },
        });
        public override WidgetView Render() => new(UI.Stack("root",
            UI.CollectionGrid("collection", Source, 130, 170, "Home", 4).Grouped(Groups),
            UI.Text(new string('x', 2000), "unchanged-content")).InputScope("scope"), "collection", ActiveInputScopeId: "scope");
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
