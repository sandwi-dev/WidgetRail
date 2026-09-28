using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real-dispatcher retention checks sharing the ordinary indexed range reader.</summary>
internal static class IndexedSourceRetentionScenarios
{
    private sealed record Pending(IndexedRangeRequest Request, TaskCompletionSource<IndexedRangeResult<string>> Reply);
    private sealed class Lifetime : IAsyncDisposable
    {
        public int Releases;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Releases); return ValueTask.CompletedTask; }
    }

    internal static async Task<int> RunAsync(DispatcherQueue dispatcher)
    {
        var requests = new ConcurrentQueue<Pending>();
        var owners = new List<Lifetime>();
        var checks = 0;
        await using var source = new IndexedItemsSource<string>(new(new("validation", "retention", "rows"), 1),
            40, dispatcher, (request, _) =>
            {
                var reply = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                requests.Enqueue(new(request, reply));
                return reply.Task.WaitAsync(TimeSpan.FromSeconds(8));
            }, pageSize: 2, concurrency: 1);

        using var retained = source.Retain(8);
        using var duplicate = source.Retain(8);
        using var samePage = source.Retain(9);
        Check(source.RetainedIndices == 2 && ReferenceEquals(retained.Slot, duplicate.Slot) &&
            ReferenceEquals(retained.Slot, source[8]), "retained indices share stable slots and duplicate refcounts");
        var initial = await Next();
        Check(initial.Request.StartIndex == 8, "explicit retention can demand a page without a native container");
        var initialOwner = Complete(initial);
        await Until(() => source.CompletedLoads == 1);
        Check(requests.IsEmpty && retained.Slot.Value == "0:8", "same-page retention uses one provider result");

        source.RangesChanged(new(0, 2), []);
        var visible = await Next();
        var visibleOwner = Complete(visible);
        await Until(() => source.CompletedLoads == 2);
        source.RangesChanged(new(0, 0), []);
        await Until(() => visibleOwner.Releases == 1 && source.ResidentSlots == 2);
        Check(initialOwner.Releases == 0 && retained.Slot.Value == "0:8", "native eviction leaves explicit presentation demand intact");
        retained.Dispose(); retained.Dispose(); samePage.Dispose();
        Check(source.RetainedIndices == 1 && initialOwner.Releases == 0, "duplicate owner retains page after other tokens close");

        source.RefreshContent(1);
        var refresh = await Next();
        Check(ReferenceEquals(duplicate.Slot, source[8]) && duplicate.Slot.Value == "0:8",
            "offscreen content refresh keeps slot and previous pixels during loading");
        var refreshedOwner = Complete(refresh);
        await Until(() => source.CompletedLoads == 3 && initialOwner.Releases == 1);
        Check(duplicate.Slot.Value == "1:8" && ReferenceEquals(duplicate.Slot, source[8]), "refresh updates retained slot in place");
        duplicate.Dispose();
        await Until(() => refreshedOwner.Releases == 1 && source.ResidentSlots == 0);
        Check(source.RetainedIndices == 0, "last presentation owner permits ordinary page eviction");

        // Native visible demand takes priority over explicit demand and native buffer
        // pages, but all three use the same one-provider concurrency limit.
        source.RangesChanged(new(0, 2), (Microsoft.UI.Xaml.Data.ItemIndexRange[])[new(4, 2)]);
        using var offscreen = source.Retain(12);
        var nativeFirst = await Next();
        Check(nativeFirst.Request.StartIndex == 0, "native visible page has first scheduling priority");
        source.RangesChanged(new(0, 0), []);
        await Until(() => source.CancelledLoads > 0);
        Check(requests.IsEmpty, "cancelled uncooperative provider still occupies shared concurrency capacity");
        var lateOwner = Complete(nativeFirst);
        var retainedNext = await Next();
        Check(retainedNext.Request.StartIndex == 12, "retained demand continues through native demand withdrawal");
        var retainedOwner = Complete(retainedNext);
        await Until(() => source.CompletedLoads == 4 && lateOwner.Releases == 1);

        source.RefreshContent(2);
        var pendingClose = await Next();
        var disposal = source.DisposeAsync().AsTask();
        Check(!disposal.IsCompleted, "source disposal drains outstanding retained provider work");
        offscreen.Dispose(); offscreen.Dispose();
        Complete(pendingClose);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Check(owners.All(owner => owner.Releases == 1) && retainedOwner.Releases == 1 && source.RetainedIndices == 0,
            "source shutdown releases each data owner once and invalidates all retention counts");
        Throws<ObjectDisposedException>(() => source.Retain(0));
        ++checks;

        // Exercise bounds before queued preparation runs, so this check cannot hide
        // an extra provider behind a second retention-specific implementation.
        var reads = 0;
        await using var bounded = new IndexedItemsSource<string>(new(new("validation", "retention", "bounded"), 1),
            40, dispatcher, (_, _) => { ++reads; throw new InvalidOperationException("No provider should run for disposed demand."); });
        var tokens = Enumerable.Range(0, IndexedItemsSource<string>.MaximumRetainedIndices).Select(bounded.Retain).ToArray();
        using var overlap = bounded.Retain(0);
        Throws<InvalidOperationException>(() => bounded.Retain(8));
        Throws<ArgumentOutOfRangeException>(() => bounded.Retain(-1));
        Throws<ArgumentOutOfRangeException>(() => bounded.Retain(40));
        tokens[0].Dispose();
        Throws<InvalidOperationException>(() => bounded.Retain(8));
        overlap.Dispose();
        using var replacement = bounded.Retain(8);
        Check(bounded.RetainedIndices == 8, "limit counts unique indices; duplicate release cannot prematurely free a slot");
        bounded.Dispose();
        foreach (var token in tokens) { token.Dispose(); token.Dispose(); }
        replacement.Dispose();
        await bounded.DisposeAsync();
        Check(reads == 0, "disposal before dispatcher admission cancels all retained demand without provider work");
        return checks;

        async Task<Pending> Next()
        {
            await Until(() => !requests.IsEmpty);
            return requests.TryDequeue(out var request) ? request : throw new InvalidOperationException("Missing retained request.");
        }
        Lifetime Complete(Pending pending)
        {
            var owner = new Lifetime(); owners.Add(owner);
            var request = pending.Request;
            pending.Reply.SetResult(new(request.Query, request.RequestId, request.StartIndex,
                Enumerable.Range(request.StartIndex, request.Count).Select(index =>
                    new KeyedCollectionItem<string>($"item.{index}", $"{request.ContentRevision}:{index}")).ToArray(),
                request.ContentRevision, owner));
            return owner;
        }
        void Check(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("Indexed retention: " + reason); ++checks; }
    }

    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Indexed retention scenario did not progress.");
            await Task.Delay(10);
        }
    }
}
