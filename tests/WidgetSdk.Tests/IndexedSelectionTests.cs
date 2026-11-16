using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class IndexedSelectionTests
{
    internal static async Task Run()
    {
        var widget = new Fixture(100_000);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "selection-test");
        try
        {
            Check(widget.Source.Selection is { IsActive: false, Count: 0 } && widget.Reads == 0, "Opt-in does not activate or realize rows.");
            Check(ViewSnapshotValidator.Validate(host.CurrentSnapshot with { ProtocolVersion = 111 }).Any(e => e.Code == "feature_requires_version"), "Old hosts reject selection declarations.");
            widget.Source.BeginSelection(); host.PublishSnapshot();
            await Gesture(10, IndexedSelectionGesture.Toggle, () => widget.Source.Selection!.Indices.SequenceEqual(new[] { 10 }));
            await Gesture(12, IndexedSelectionGesture.Extend, () => widget.Source.Selection!.Indices.SequenceEqual(new[] { 10, 11, 12 }));
            await Gesture(20, IndexedSelectionGesture.Toggle, () => widget.Source.Selection!.Count == 4);
            await Gesture(18, IndexedSelectionGesture.ExtendAdd, () => widget.Source.Selection!.Indices.SequenceEqual(new[] { 10,11,12,18,19,20 }));
            var before = widget.Source.Selection!.Indices.ToArray();
            await Gesture(1000, IndexedSelectionGesture.Extend, () => widget.Source.Selection!.LimitReached);
            Check(widget.Source.Selection!.Indices.SequenceEqual(before), "An oversized range rejects the whole gesture.");
            Check(!widget.Source.SelectAll() && widget.Source.Selection!.Indices.SequenceEqual(before), "Select all never means only loaded rows or silently truncates a large query.");
            var renders = widget.Renders;
            var rowsRead = widget.RowsRead;
            var selected = await widget.Source.ReadSelectedAsync();
            Check(selected.Select(i => i.Index).SequenceEqual(before) && widget.Renders == renders && widget.MaxRead <= 64, "Selection reads only bounded data, without realizing templates.");
            Check(widget.RowsRead - rowsRead == before.Length, "Sparse selection reads only selected positions, not surrounding windows.");
            widget.Source.UpdateContent("updated"); host.PublishSnapshot();
            Check(widget.Source.Selection!.Indices.SequenceEqual(before), "Content updates preserve selection.");
            Check((await widget.Source.ReadSelectedAsync()).All(i => i.Content == "updated"), "Mutation inputs resolve current revisioned data.");
            using var stale = await host.AcquireAsync("items", 10, 1);
            var oldEpoch = widget.Source.Selection!.Epoch;
            widget.Source.EndSelection(); widget.Source.BeginSelection(); host.PublishSnapshot();
            Check(stale.RouteSelection("item-10", new(oldEpoch, IndexedSelectionGesture.Toggle)) is null, "Prior selection mode cannot reselect after reset.");
            widget.Source.PublishQuery("new-query", 4); host.PublishSnapshot();
            Check(widget.Source.Selection is { IsActive: true, Count: 0 } && widget.Source.SelectAll(), "New query clears prior positions and selects all only when bounded.");
            Check(widget.Source.Selection!.Indices.SequenceEqual(new[] { 0,1,2,3 }), "Small-query all selects every matching position.");
            var pendingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            widget.ReaderGate = pendingGate.Task; widget.ReaderEntered = entered;
            var pending = widget.Source.ReadSelectedAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            widget.Source.PublishQuery("replacement", 2); host.PublishSnapshot();
            try { await pending; throw new InvalidOperationException("Retired query returned selected items."); }
            catch (Exception error) when (error is WidgetIndexedSelectionException { Reason: WidgetIndexedSelectionFailure.Changed }) { }
            pendingGate.TrySetResult(); widget.ReaderGate = null;
            Check(widget.Actions == 0, "Selection gestures never invoke author item actions.");

            async Task Gesture(int index, IndexedSelectionGesture gesture, Func<bool> ready)
            {
                using var lease = await host.AcquireAsync("items", index, 1);
                Check(lease.RouteSelection("item-" + index, new(widget.Source.Selection!.Epoch, gesture)) == WidgetOperationAdmission.Enqueued, "Current selection input must be admitted.");
                var deadline = Environment.TickCount64 + 3000;
                while (!ready()) { if (Environment.TickCount64 > deadline) throw new TimeoutException(gesture.ToString()); await Task.Delay(5); }
                host.PublishSnapshot();
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }

        var legacy = new Fixture(2, false);
        await WidgetTestHost.InitializeAsync(legacy);
        await WidgetTestHost.SetLifecycleStateAsync(legacy, WidgetLifecycleState.Interactive);
        using var oldHost = WidgetTestHost.CreateIndexedCollectionHost(legacy, "legacy-selection");
        try
        {
            Check(legacy.Source.Selection is null && oldHost.CurrentSnapshot.Root.Children[0].IndexedSelection is null, "Legacy consumers publish no selection metadata.");
            using var lease = await oldHost.AcquireAsync("items", 0, 1);
            Check(lease.RouteSelection("item-0", new(0, IndexedSelectionGesture.Toggle)) is null, "Opt-out rejects selection input.");
            Check(lease.RouteAction("item-0", ControllerButton.A) == WidgetOperationAdmission.Enqueued, "Legacy item activation stays admitted.");
            var until = Environment.TickCount64 + 3000;
            while (legacy.Actions == 0) { if (Environment.TickCount64 > until) throw new TimeoutException("legacy activation"); await Task.Delay(5); }
        }
        finally { await WidgetTestHost.DestroyAsync(legacy); }
    }
    internal static async Task Lookup()
    {
        var widget = new Fixture(100_000, selection: false, lookup: true);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "lookup-test");
        try
        {
            Check(await host.LocateAsync("items", new("item-75000")) == 75000 && widget.Reads == 0 && widget.Renders == 0, "Large lookup resolves metadata without reading or rendering rows.");
            Check(await host.LocateAsync("items", new("absent")) is null, "Removed occurrence returns no position.");
            Check(ViewSnapshotValidator.Validate(host.CurrentSnapshot with { ProtocolVersion = 112 }).Any(e => e.Code == "feature_requires_version"), "Lookup requires its explicit protocol version.");
            widget.Source.PublishQuery("reverse", 100_000); host.PublishSnapshot();
            Check(await host.LocateAsync("items", new("item-75000")) == 24999, "The new immutable query controls lookup order.");
            widget.BadLookup = true;
            try { await host.LocateAsync("items", new("item-1")); throw new Exception("Invalid provider index was accepted."); }
            catch (InvalidOperationException) { }
            widget.BadLookup = false;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            widget.LookupGate = release.Task; widget.LookupEntered = entered;
            var pending = host.LocateAsync("items", new("item-75000")).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            widget.Source.PublishQuery("replacement", 100_000); host.PublishSnapshot();
            try { await pending; throw new Exception("Retired lookup returned a position."); }
            catch (InvalidOperationException) { }
            release.TrySetResult();
            var source = widget.Source.Descriptor;
            foreach (var request in new[] {
                new IndexedCollectionRangeRequest("items",source,0,1,"lookup") { Kind=IndexedCollectionRequestKind.Locate,LocateKey="item-1" },
                new IndexedCollectionRangeRequest("items",source with { CanLocateItems=false },0,0,"lookup") { Kind=IndexedCollectionRequestKind.Locate,LocateKey="item-1" },
                new IndexedCollectionRangeRequest("items",source,0,1,"lookup") { LocateKey="item-1" } })
            {
                try { IndexedCollectionContract.ValidateRequest(request); throw new Exception("Invalid lookup shape accepted."); }
                catch (ArgumentException) { }
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record Item(int Index, string Content);
    private sealed class Fixture : Widget
    {
        internal WidgetIndexedCollection<string, Item> Source { get; }
        internal int Reads, Renders, Actions, MaxRead, RowsRead;
        internal Task? ReaderGate;
        internal TaskCompletionSource? ReaderEntered;
        internal Task? LookupGate; internal TaskCompletionSource? LookupEntered; internal bool BadLookup;
        internal Fixture(int count, bool selection = true, bool lookup = false)
        {
            Source = CreateIndexedCollection<string, Item>("source", "initial", count, new()
            {
                EnableSelection = selection,
                FindIndex = lookup ? async (query, key, token) =>
                {
                    if (LookupGate is { } gate) { LookupEntered?.TrySetResult(); await gate; }
                    if (BadLookup) return count;
                    if (!key.Value.StartsWith("item-", StringComparison.Ordinal) || !int.TryParse(key.Value.AsSpan(5), out var index) || index < 0 || index >= count) return (int?)null;
                    return query == "reverse" ? count - 1 - index : index;
                } : null,
                ReadRange = async (query, start, length, token) =>
                {
                    ++Reads; RowsRead += length; MaxRead = Math.Max(MaxRead, length);
                    if (ReaderGate is { } gate) { ReaderEntered?.TrySetResult(); await gate.WaitAsync(token); }
                    return Enumerable.Range(start,length).Select(i => new Item(i,query)).ToArray();
                },
                ItemKey = item => new("item-" + item.Index),
                RenderItem = (_, item, context) => { ++Renders; return UI.Button("Item " + item.Index, "open", context.Id("button")); },
                OnAction = (_, _, _, _) => { ++Actions; return ValueTask.CompletedTask; },
            });
        }
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("items", Source, 48, "Selectable items")).InputScope("root"), ActiveInputScopeId: "root");
    }
}
