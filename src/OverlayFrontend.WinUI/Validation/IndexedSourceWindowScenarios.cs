using System.Collections.Concurrent;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class IndexedSourceWindowScenarios
{
    private sealed record Pending(IndexedRangeRequest Request, TaskCompletionSource<IndexedRangeResult<string>> Reply);
    private sealed class Lease : IAsyncDisposable
    {
        internal int Releases;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Releases); return ValueTask.CompletedTask; }
    }

    internal static async Task<int> RunAsync(DispatcherQueue dispatcher)
    {
        var checks = 0;
        var requests = new ConcurrentQueue<Pending>();
        var leases = new List<Lease>();
        var keys = new[] { "a", "b", "c", "d" };
        await using var source = new IndexedItemsSource<string>(new(new("window", "instance", "rows"), 1), 4, dispatcher,
            (request, _) =>
            {
                var completion = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                requests.Enqueue(new(request, completion));
                return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }, pageSize: 2, concurrency: 1);
        using var anchor = source.Retain(1);
        using var duplicate = source.Retain(1);
        using var removed = source.Retain(3);
        var first = await Next(); Complete(first, keys);
        await Until(() => source.CompletedLoads == 1);
        var delayed = await Next();
        var slot = anchor.Slot;
        var changes = new List<NotifyCollectionChangedAction>();
        source.CollectionChanged += (_, args) =>
        {
            changes.Add(args.Action);
            Check(Enumerable.Range(0, source.Count).All(index => source.IndexOf(source[index]) == index),
                "Every collection event exposes consistent native indices");
        };
        var next = new[] { "x", "a", "b", "c" };
        source.RebaseWindow(source.Query with { Generation = 2 }, 0, keys, next);
        Check(ReferenceEquals(source[2], slot) && slot.Index == 2 && slot.Value == "1:b",
            "Prepend preserves exact slot and previous pixels at the new index");
        Check(source.IndexOf(removed.Slot) == -1 && removed.Slot.Index == -1 && source.RetainedIndices == 1,
            "Removed occurrence cannot alias the new occupant of its index");
        removed.Dispose();
        Check(source.RetainedIndices == 1, "Retired token disposal cannot release a different occurrence");
        Check(changes.SequenceEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }),
            "Window update emits no Reset or Replace");
        var stale = Complete(delayed, keys);
        var current = await Next();
        Check(current.Request.Query.Generation == 2 && current.Request.StartIndex == 2,
            "Retained demand follows its occurrence into the current query");
        Complete(current, next);
        await Until(() => slot.Value == "2:b" && stale.Releases == 1);
        Check(slot.Key == "b" && ReferenceEquals(source[2], slot), "Late old response cannot overwrite the retained occurrence");
        anchor.Dispose();
        Check(source.RetainedIndices == 1, "Duplicate retention survives query rebasing");
        duplicate.Dispose();
        await Until(() => source.ResidentSlots == 0 && leases.All(lease => lease.Releases == 1));
        Check(source.Count == 4 && source.RetainedIndices == 0, "Eviction bounds slots and releases all old/new leases exactly once");
        Check(!IndexedItemsSource<string>.CanRebaseWindow(new[]{"a", "b"}, new[]{"b", "a"}), "Reordering uses existing replacement behavior");
        Check(!IndexedItemsSource<string>.CanRebaseWindow(new[]{"a", "b"}, new[]{"c", "d"}), "Unrelated membership cannot reuse old rows");
        Check(!IndexedItemsSource<string>.CanRebaseWindow(new[]{"a", "b"}, new[]{"a", "a"}), "Duplicate occurrence identity is rejected");
        Check(!IndexedItemsSource<string>.CanRebaseWindow(Enumerable.Range(0,513).Select(index=>index.ToString()).ToArray(), new[]{"0"}),
            "Large lazy sources do not enter bounded reconciliation");
        return checks + await PreparedAsync(dispatcher) + await PaintSeedAsync(dispatcher);

        Lease Complete(Pending pending, string[] values)
        {
            var lease = new Lease(); leases.Add(lease);
            pending.Reply.SetResult(new(pending.Request.Query, pending.Request.RequestId, pending.Request.StartIndex,
                Enumerable.Range(pending.Request.StartIndex, pending.Request.Count)
                    .Select(index => new KeyedCollectionItem<string>(values[index], pending.Request.Query.Generation + ":" + values[index])).ToArray(),
                pending.Request.ContentRevision, lease));
            return lease;
        }
        async Task<Pending> Next() { await Until(() => !requests.IsEmpty); requests.TryDequeue(out var pending); return pending!; }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 6000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Window source did not settle."); await Task.Delay(10); }
        }
        void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); ++checks; }
    }
}
