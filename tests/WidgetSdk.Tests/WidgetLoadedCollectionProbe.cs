using System.Diagnostics;
using System.Text.Json;
using WidgetRail.WidgetSdk;

// Explicit diagnostic probe, not a wall-clock test gate. Run in a disposable
// test process; compare the old authored publication algorithm with the adapter.
internal static class WidgetLoadedCollectionProbe
{
    internal static async Task WriteAsync(string path)
    {
        var results = new List<object>();
        foreach (var count in new[] { 512, 10000 })
        foreach (var scenario in new[] { "unchanged", "content", "identity", "reorder" })
        {
            var first = Enumerable.Range(0, count).Select(i => new Item("key-" + i, "value")).ToArray();
            var second = scenario == "reorder" ? first.Reverse().ToArray() : first.ToArray();
            if (scenario == "content") second[^1] = second[^1] with { Value = "changed" };
            var legacy = new Legacy(first);
            var loaded = new Loaded(first);
            await WidgetTestHost.InitializeAsync(legacy);
            await WidgetTestHost.InitializeAsync(loaded);
            try
            {
                var iterations = count == 512 ? 1000 : 200;
                void Run(Action<string, IReadOnlyList<Item>> apply, int i) => apply(
                    scenario == "identity" && i % 2 != 0 ? "other" : "initial", i % 2 == 0 ? first : second);
                for (var warmup = 0; warmup < 100; ++warmup) { Run(legacy.Publish, warmup); Run(loaded.Publish, warmup); }
                object Measure(Action<string, IReadOnlyList<Item>> apply)
                {
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    for (var i = 0; i < iterations; ++i) Run(apply, i);
                    var elapsed = timer.Elapsed.TotalMilliseconds;
                    return new { microsecondsPerPublication = elapsed * 1000 / iterations,
                        allocatedBytesPerPublication = (GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations };
                }
                results.Add(new { count, scenario, iterations, legacy = Measure(legacy.Publish), loaded = Measure(loaded.Publish),
                    renderedRows = loaded.RenderedRows });
            }
            finally { await WidgetTestHost.DestroyAsync(legacy); await WidgetTestHost.DestroyAsync(loaded); }
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "In-process loaded-list publication only; excludes provider/model projection, renderer, scrolling and native memory.", results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Wrote loaded collection comparison: " + path);
    }

    private sealed record Item(string Key, string Value);
    private sealed record Query(string Identity, IReadOnlyList<Item> Items);
    private sealed class Legacy : Widget
    {
        private Query current;
        private readonly WidgetIndexedCollection<Query, Item> source;
        internal Legacy(IReadOnlyList<Item> items)
        {
            current = new("initial", items.ToArray());
            source = CreateIndexedCollection<Query, Item>("legacy", current, items.Count, new()
            {
                ReadRange = (query, start, count, token) => ValueTask.FromResult<IReadOnlyList<Item>>(query.Items.Skip(start).Take(count).ToArray()),
                ItemKey = item => new(item.Key),
                OccurrenceKeys = query => query.Items.Count <= 512 ? query.Items.Select(item => new WidgetCollectionItemKey(item.Key)).ToArray() : null,
                RenderItem = (_, item, ids) => UI.Button(item.Value, "open", ids.Id("row")),
                OnAction = (_, _, _, _) => ValueTask.CompletedTask,
            });
        }
        internal void Publish(string identity, IReadOnlyList<Item> items)
        {
            if (current.Identity == identity && current.Items.SequenceEqual(items)) return;
            var same = current.Identity == identity && current.Items.Select(item => item.Key).SequenceEqual(items.Select(item => item.Key));
            current = new(identity, items.ToArray());
            if (same) source.UpdateContent(current); else source.PublishQuery(current, items.Count);
        }
        public override WidgetView Render() => new(UI.Stack("root"));
    }
    private sealed class Loaded : Widget
    {
        private readonly WidgetLoadedCollection<string, Item> source;
        internal int RenderedRows;
        internal Loaded(IReadOnlyList<Item> items) => source = CreateLoadedCollection<string, Item>("loaded", "initial", "context", items, new()
        {
            PreserveOccurrences = true,
            ItemKey = item => new(item.Key),
            RenderItem = (_, item, ids) => { RenderedRows++; return UI.Button(item.Value, "open", ids.Id("row")); },
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        });
        internal void Publish(string identity, IReadOnlyList<Item> items) => source.Publish(identity, "context", items);
        public override WidgetView Render() => new(UI.Stack("root"));
    }
}
