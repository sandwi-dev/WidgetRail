using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class IndexedSourceWindowScenarios
{
    private static async Task<int> PaintSeedAsync(DispatcherQueue dispatcher)
    {
        var checks = 0;
        var query = new IndexedQueryIdentity(new("seed", "instance", "rows"), 1);
        var priorLease = new Lease();
        await using var prior = new IndexedItemsSource<string>(query, 4, dispatcher,
            (request, _) => Task.FromResult(new IndexedRangeResult<string>(request.Query, request.RequestId,
                request.StartIndex, (KeyedCollectionItem<string>[])[new("a", "old:a"), new("b", "old:b")], request.ContentRevision, priorLease)), pageSize: 2);
        using var keep = prior.Retain(0);
        await Until(() => keep.Slot.Value is not null);
        var reply = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IndexedRangeRequest? fresh = null;
        var nextLease = new Lease();
        await using var next = new IndexedItemsSource<string>(query, 4, dispatcher,
            (request, _) => { fresh = request; return reply.Task; }, pageSize: 2, contentRevision: 1);
        next.SeedSameQueryPresentation(prior);
        Check(next.ResidentSlots == 2 && ((IndexedItem<string>)next[0]).Value == "old:a" &&
            next.CompletedLoads == 0 && next.RetainedIndices == 0,
            "Recreated query seeds only resident paint, never pages or retention ownership");
        using var nextKeep = next.Retain(0);
        await Until(() => fresh is not null);
        await prior.DisposeAsync();
        Check(priorLease.Releases == 1 && nextKeep.Slot.Value == "old:a",
            "Previous source teardown releases its lease without erasing copied presentation");
        reply.SetResult(new(fresh!.Value.Query, fresh.Value.RequestId, 0,
            (KeyedCollectionItem<string>[])[new("a", "new:a"), new("b", "new:b")], fresh.Value.ContentRevision, nextLease));
        await Until(() => nextKeep.Slot.Value == "new:a");
        Check(next.CompletedLoads == 1 && nextLease.Releases == 0,
            "The new view independently acquires and owns its authoritative payload");
        foreach (var (identity, count, revision) in new[] {
            (query with { Generation = 2 }, 4, 1L), (query with { Authority = new("seed", "other", "rows") }, 4, 1L),
            (query, 3, 1L), (query, 4, 0L) })
        {
            await using var rejected = new IndexedItemsSource<string>(identity, count, dispatcher,
                (_, _) => throw new InvalidOperationException("Rejected paint cannot cause a request."), contentRevision: revision);
            try { rejected.SeedSameQueryPresentation(next); throw new InvalidDataException("Unrelated paint was seeded."); }
            catch (InvalidOperationException) { ++checks; }
            Check(rejected.ResidentSlots == 0, "Rejected identity/count/revision publishes no stale rows");
        }
        await next.DisposeAsync();
        Check(nextLease.Releases == 1, "Seeded view teardown releases its own lease once");
        return checks;
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); ++checks; }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Seeded source did not settle."); await Task.Delay(10); }
        }
    }
}
