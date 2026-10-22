using System.Diagnostics;
using System.Text.Json;
using WidgetRail.WidgetSdk;

// Diagnostic microbenchmark, not a wall-clock test or native scrolling claim.
internal static class WidgetQueryResourceProbe
{
    internal static async Task WriteAsync(string path)
    {
        var widget = new ProbeWidget();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        using var scope = new CancellationTokenSource();
        var results = new List<object>();
        try
        {
            widget.Plain.EnsureLoaded().Completion.GetAwaiter().GetResult();
            widget.Query.EnsureLoaded("A", scope.Token).Completion.GetAwaiter().GetResult();
            foreach (var scenario in new[] { "snapshot", "cached", "refresh", "replace" })
            {
                void Plain(int index)
                {
                    if (scenario == "snapshot") { _ = widget.Plain.Snapshot; return; }
                    if (scenario == "replace") widget.Plain.Reset(false);
                    widget.Plain.EnsureLoaded(scenario == "refresh").Completion.GetAwaiter().GetResult();
                }
                void Query(int index)
                {
                    var key = scenario == "replace" && index % 2 != 0 ? "B" : "A";
                    if (scenario == "snapshot") { _ = widget.Query.GetSnapshot(key); return; }
                    widget.Query.EnsureLoaded(key, scope.Token, scenario == "refresh").Completion.GetAwaiter().GetResult();
                }
                object Measure(Action<int> run)
                {
                    const int iterations = 10000;
                    for (var i = 0; i < 1000; ++i) run(i);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var started = Stopwatch.GetTimestamp();
                    for (var i = 0; i < iterations; ++i) run(i);
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    return new { microsecondsPerCall = elapsed.TotalMicroseconds / iterations,
                        allocatedBytesPerCall = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations };
                }
                results.Add(new { scenario, plain = Measure(Plain), keyedWithCallerScope = Measure(Query) });
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            scope = "In-process synchronous read resource machinery only; plain baseline has no caller scope. Excludes provider IO, UI, process working set and native scrolling.", results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Wrote query resource probe: " + path);
    }
    private sealed class ProbeWidget : Widget
    {
        internal WidgetResource<string> Plain { get; }
        internal WidgetQueryResource<string, string> Query { get; }
        internal ProbeWidget()
        {
            Plain = CreateResource<string>("plain", new()
            { Load = _ => ValueTask.FromResult("value"), MapError = _ => WidgetResourceError.Unexpected, CacheDuration = TimeSpan.MaxValue });
            Query = CreateQueryResource<string, string>("keyed", new()
            { Load = (_, _) => ValueTask.FromResult("value"), MapError = _ => WidgetResourceError.Unexpected, CacheDuration = TimeSpan.MaxValue });
        }
        public override WidgetView Render() => new(UI.Text("Probe", "probe"));
    }
}
