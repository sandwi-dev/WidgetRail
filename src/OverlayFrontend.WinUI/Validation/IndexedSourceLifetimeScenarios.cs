using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Exercises asynchronous ownership using the real WinUI dispatcher.</summary>
internal static class IndexedSourceLifetimeScenarios
{
    private sealed record Pending(IndexedRangeRequest Request, TaskCompletionSource<IndexedRangeResult<string>> Reply);
    private sealed class Lifetime : IAsyncDisposable
    {
        internal int Releases;
        public async ValueTask DisposeAsync() { await Task.Delay(15); Interlocked.Increment(ref Releases); }
    }

    internal static async Task<int> RunAsync(DispatcherQueue dispatcher)
    {
        var requests = new ConcurrentQueue<Pending>();
        var owners = new List<Lifetime>();
        var checks = 0;
        await using var source = new IndexedItemsSource<string>(new(new("validation", "lifetime", "rows"), 1),
            10, dispatcher, (request, _) =>
            {
                // Deliberately ignore cancellation to test late successful replies.
                var reply = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                requests.Enqueue(new(request, reply));
                return reply.Task.WaitAsync(TimeSpan.FromSeconds(8));
            }, pageSize: 2, concurrency: 1);
        source.RangesChanged(new(0, 2), []);
        var first = await Next();
        var firstOwner = Complete(first);
        await Until(() => source.CompletedLoads == 1);
        var slot = (IndexedItem<string>)source[0];
        Check(slot.Value == "0:0", "initial payload");

        source.RefreshContent(1);
        var refresh = await Next();
        Check(ReferenceEquals(slot, source[0]) && slot.Value == "0:0", "refresh retains logical slot and previous pixels while pending");
        var refreshOwner = Complete(refresh);
        await Until(() => source.CompletedLoads == 2 && firstOwner.Releases == 1);
        Check(ReferenceEquals(slot, source[0]) && slot.Value == "1:0", "refresh publishes into the existing slot");

        source.RefreshContent(2);
        var stale = await Next();
        source.RangesChanged(new(4, 2), []);
        await Until(() => source.CancelledLoads > 0 && refreshOwner.Releases == 1);
        var staleOwner = Complete(stale);
        var next = await Next();
        var nextOwner = Complete(next);
        await Until(() => source.CompletedLoads == 3 && staleOwner.Releases == 1);
        Check(((IndexedItem<string>)source[4]).Value == "2:4" && source.ResidentSlots == 2,
            "cancelled late delivery releases ownership without admitting distant rows");

        source.RefreshContent(3);
        var invalid = await Next();
        var invalidOwner = Complete(invalid, wrongIdentity: true);
        await Until(() => source.FailedLoads == 1 && invalidOwner.Releases == 1);
        Check(((IndexedItem<string>)source[4]).Value == "2:4" && nextOwner.Releases == 0,
            "invalid result releases only its own data and preserves the previous page");

        source.RefreshContent(4);
        var duringClose = await Next();
        var disposal = source.DisposeAsync().AsTask();
        Check(!disposal.IsCompleted, "source disposal waits for actual outstanding provider completion");
        Complete(duringClose);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Check(owners.All(owner => owner.Releases == 1), "all admitted rejected and late pages release exactly once");
        return checks;

        async Task<Pending> Next()
        {
            await Until(() => !requests.IsEmpty);
            return requests.TryDequeue(out var request) ? request : throw new InvalidOperationException("Missing range request.");
        }
        Lifetime Complete(Pending pending, bool wrongIdentity = false)
        {
            var owner = new Lifetime(); owners.Add(owner);
            var request = pending.Request;
            pending.Reply.SetResult(new(request.Query, request.RequestId + (wrongIdentity ? 1 : 0), request.StartIndex,
                Enumerable.Range(request.StartIndex, request.Count).Select(index =>
                    new KeyedCollectionItem<string>($"item.{index}", $"{request.ContentRevision}:{index}")).ToArray(),
                request.ContentRevision, owner));
            return owner;
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); ++checks; }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Indexed ownership scenario did not progress.");
            await Task.Delay(10);
        }
    }
}
