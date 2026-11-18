using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetLoadedViewportTests
{
    internal static async Task Run()
    {
        await TailProvenance();
        await CallbackBound();
        var fixture = new Fixture();
        await WidgetTestHost.InitializeAsync(fixture);
        await WidgetTestHost.SetLifecycleStateAsync(fixture, WidgetLifecycleState.Interactive);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "viewport-fixture");
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");
        await Observe(first, 1, true, true);
        Check(fixture.Rows.ViewportState is { ActiveViews: 1, FollowsEnd: true }, "Initial viewport was not admitted");
        await Observe(second, 1, true, false);
        Check(!fixture.Rows.ViewportState.FollowsEnd, "A second reader was ignored");
        await Observe(second, 2, false, false);
        Check(fixture.Rows.ViewportState.FollowsEnd, "Retired reader continued blocking following");
        await Observe(first, 1, true, false);
        Check(fixture.Rows.ViewportState.FollowsEnd, "Stale sequence changed viewport state");
        try { fixture.Rows.Publish("bad", "bad", [1, 1]); }
        catch (ArgumentException) { }
        Check(fixture.Rows.ViewportState.FollowsEnd, "Invalid publication reset live viewport state");

        var pending = Recover(first, 2);
        await fixture.Started.Task;
        Check(fixture.Rows.ViewportState is { FollowsEnd: false, Recovering: true }, "Reader mode did not precede async recovery");
        fixture.Release.SetResult();
        Check((await pending).ViewportRecovered == true && fixture.Commits == 1, "Current recovery failed to commit once");
        host.PublishSnapshot();
        Check(fixture.Rows.ViewportState is { FollowsEnd: false, Recovering: false }, "Recovery silently resumed tail advancement");

        fixture.Prepare();
        using var cancelled = new CancellationTokenSource();
        pending = Recover(first, 3, cancelled.Token);
        await fixture.Started.Task;
        cancelled.Cancel();
        fixture.Release.SetResult();
        try { await pending; throw new InvalidOperationException("Cancelled recovery did not complete cancellation"); }
        catch (OperationCanceledException) { }
        await fixture.Finished.Task;
        Check(fixture.Commits == 1, "Cancelled recovery changed domain state");

        fixture.Prepare();
        pending = Recover(first, 4);
        await fixture.Started.Task;
        fixture.Rows.Publish("replacement", "replacement", [10, 11]); host.PublishSnapshot();
        fixture.Release.SetResult();
        try { await pending; throw new InvalidOperationException("Semantic replacement retained recovery response authority"); }
        catch (Exception error) when (error is ArgumentException or OperationCanceledException) { }
        await fixture.Finished.Task;
        Check(fixture.Commits == 1 && fixture.Rows.ViewportState.ActiveViews == 0, "Old recovery crossed semantic replacement");
        await Observe(first, 5, true, true);
        fixture.Hidden = true; host.PublishSnapshot();
        Check(fixture.Rows.ViewportState.ActiveViews == 0, "Hidden collection retained viewport authority");
        fixture.Hidden = false; host.PublishSnapshot();
        await Observe(first, 6, true, true);
        await WidgetTestHost.SetLifecycleStateAsync(fixture, WidgetLifecycleState.Background);
        Check(fixture.Rows.ViewportState.ActiveViews == 0, "Inactive widget retained viewport authority");
        await WidgetTestHost.DestroyAsync(fixture);

        ValueTask<IndexedCollectionRange> Observe(string viewport, long sequence, bool active, bool end) => fixture.ReadIndexedRangeAsync(
            Request() with { Kind = IndexedCollectionRequestKind.ObserveViewport, Viewport = new(viewport, sequence, active, end) }, CancellationToken.None);
        ValueTask<IndexedCollectionRange> Recover(string viewport, long sequence, CancellationToken token = default) => fixture.ReadIndexedRangeAsync(
            Request() with { Kind = IndexedCollectionRequestKind.RecoverViewport, Recovery = new(viewport, sequence, "item.1") }, token);
        IndexedCollectionRangeRequest Request() => new("items", fixture.Rows.Descriptor, 0, 0, Guid.NewGuid().ToString("N"));
    }

    private static async Task TailProvenance()
    {
        var fixture = new Fixture();
        await WidgetTestHost.InitializeAsync(fixture);
        fixture.Rows.Publish("live", "live", Enumerable.Range(0, 40).ToArray());
        var old = fixture.Rows.Descriptor.QueryGeneration;
        fixture.Rows.Publish("live", "live", Enumerable.Range(40, 40).ToArray());
        Check(fixture.Rows.Descriptor.EndFollowingPreviousQuery == old, "Disjoint coalesced forward window lost chronology proof");
        fixture.Rows.Publish("live", "live", Enumerable.Range(40, 39).ToArray());
        Check(fixture.Rows.Descriptor.EndFollowingPreviousQuery is null, "Tail deletion claimed forward advancement");
        fixture.Rows.Publish("live", "live", new[] { 39 }.Concat(Enumerable.Range(40, 39)).ToArray());
        Check(fixture.Rows.Descriptor.EndFollowingPreviousQuery is null, "Earlier insertion claimed forward advancement");
        fixture.Rows.Publish("live", "live", Enumerable.Range(20, 40).ToArray());
        Check(fixture.Rows.Descriptor.EndFollowingPreviousQuery is null, "Around recovery claimed forward advancement");
        fixture.Rows.Publish("live", "live", Enumerable.Range(20, 40).Reverse().ToArray());
        Check(fixture.Rows.Descriptor.EndFollowingPreviousQuery is null, "Unordered replacement claimed chronology proof");
        await WidgetTestHost.DestroyAsync(fixture);
    }

    private static async Task CallbackBound()
    {
        var fixture = new BoundFixture();
        await WidgetTestHost.InitializeAsync(fixture);
        await WidgetTestHost.SetLifecycleStateAsync(fixture, WidgetLifecycleState.Interactive);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "bounded-viewport");
        for (var index = 0; index < 4; index++)
        {
            using var cancellation = new CancellationTokenSource();
            var pending = fixture.ReadIndexedRangeAsync(Request(Guid.NewGuid().ToString("N"), 1), cancellation.Token).AsTask();
            Check(await fixture.Started.WaitAsync(TimeSpan.FromSeconds(3)), "Bounded callback did not start");
            cancellation.Cancel();
            try { await pending; throw new InvalidOperationException("Ignored cancellation held the transport demand"); }
            catch (OperationCanceledException) { }
        }
        var fifth = Guid.NewGuid().ToString("N");
        try { await fixture.ReadIndexedRangeAsync(Request(fifth, 1), CancellationToken.None); throw new InvalidOperationException("Fifth underlying callback bypassed the bound"); }
        catch (InvalidOperationException error) when (error.Message.Contains("still draining", StringComparison.Ordinal)) { }
        fixture.Release.SetResult();
        var deadline = Environment.TickCount64 + 3000;
        for (var attempt = 2; ; attempt++)
        {
            try { await fixture.ReadIndexedRangeAsync(Request(fifth, attempt), CancellationToken.None); break; }
            catch (InvalidOperationException error) when (error.Message.Contains("still draining", StringComparison.Ordinal) && Environment.TickCount64 < deadline)
            { await Task.Delay(10); }
        }
        await WidgetTestHost.DestroyAsync(fixture);
        IndexedCollectionRangeRequest Request(string id, long sequence) => new("items", fixture.Rows.Descriptor, 0, 0, Guid.NewGuid().ToString("N"))
        { Kind = IndexedCollectionRequestKind.ObserveViewport, Viewport = new(id, sequence, true, false) };
    }

    private sealed class BoundFixture : Widget
    {
        internal readonly SemaphoreSlim Started = new(0);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly WidgetLoadedCollection<string, int> Rows;
        internal BoundFixture() => Rows = CreateLoadedCollection<string, int>("bounded-viewport", "identity", "context", [1], new()
        {
            PreserveOccurrences = true, FollowEnd = true,
            ItemKey = item => new("item." + item), RenderItem = (_, _, ids) => UI.Button("Item", "open", ids.Id("row")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
            OnViewportChanged = async (_, _, _) => { Started.Release(); await Release.Task; },
        });
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("items", Rows, 48, "Items")), ActiveInputScopeId: "root");
    }

    private sealed class Fixture : Widget
    {
        internal readonly WidgetLoadedCollection<string, int> Rows;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Commits;
        internal bool Hidden;
        internal void Prepare() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); Finished = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal Fixture()
        {
            Rows = CreateLoadedCollection<string, int>("viewport-source", "initial", "initial", [1, 2], new()
            {
                PreserveOccurrences = true, FollowEnd = true,
                LiveOrder = (left, right) => left.CompareTo(right),
                ItemKey = item => new("item." + item), RenderItem = (_, item, ids) => UI.Button("Item " + item, "open", ids.Id("row")),
                OnAction = (_, _, _, _) => ValueTask.CompletedTask,
                OnRecoverViewport = async (_, recovery, _) =>
                {
                    Started.TrySetResult(); await Release.Task;
                    try { return recovery.TryCommit(() => { Commits++; return true; }) ? WidgetViewportRecoveryResult.Recovered : WidgetViewportRecoveryResult.NotFound; }
                    finally { Finished.TrySetResult(); }
                },
            });
        }
        public override WidgetView Render() => new(Hidden ? UI.Stack("root") : UI.Stack("root", UI.CollectionList("items", Rows, 48, "Items")), ActiveInputScopeId: "root");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
