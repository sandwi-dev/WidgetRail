using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Binding notifications use the real dispatcher and can change source lifetime inline.</summary>
internal static class IndexedSourceReentrancyScenarios
{
    private sealed class Lifetime : IAsyncDisposable
    {
        internal int Releases;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Releases); return ValueTask.CompletedTask; }
    }

    internal static async Task<int> RunAsync(DispatcherQueue dispatcher)
    {
        var checks = 0;
        foreach (var action in new[] { "suspend", "dispose", "refresh" })
        {
            var owner = new Lifetime();
            var nextOwner = new Lifetime();
            var reply = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextRequested = new TaskCompletionSource<IndexedRangeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextReply = new TaskCompletionSource<IndexedRangeResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var source = new IndexedItemsSource<string>(new(new("validation", action, "rows"), 1), 2,
                dispatcher, (request, token) =>
                {
                    if (request.ContentRevision == 0)
                    {
                        reply.TrySetResult(Result(request, owner));
                        return reply.Task;
                    }
                    nextRequested.TrySetResult(request);
                    return nextReply.Task.WaitAsync(token);
                }, pageSize: 2, concurrency: 1);
            var first = (IndexedItem<string>)source[0];
            var second = (IndexedItem<string>)source[1];
            Task? retirement = null;
            var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            first.StateChanged += (_, _) =>
            {
                if (notified.Task.IsCompleted) return;
                switch (action)
                {
                    case "suspend": retirement = source.SetPresentationActiveAsync(false); break;
                    case "dispose": retirement = source.DisposeAsync().AsTask(); break;
                    case "refresh": source.RefreshContent(1); break;
                }
                notified.TrySetResult();
            };
            source.RangesChanged(new(0, 2), []);
            await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (retirement is not null) await retirement.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!second.HasValue, $"{action}: retired publication does not notify subsequent rows");
            if (action == "refresh")
            {
                var request = await nextRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(owner.Releases == 0, "refresh: old page stays owned until valid replacement");
                nextReply.SetResult(Result(request, nextOwner));
                var deadline = Environment.TickCount64 + 5000;
                while (second.Value != "1:1")
                {
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException("Refreshed row was not published.");
                    await Task.Delay(10);
                }
                Check(first.Value == "1:0", "refresh: current revision replaces both stable slots");
            }
            else Check(owner.Releases == 1, $"{action}: incoming page ownership drains before retirement completes");
            await source.DisposeAsync();
            Check(owner.Releases == 1 && (action != "refresh" || nextOwner.Releases == 1),
                $"{action}: all page owners release exactly once");
        }
        var boundOwner = new Lifetime();
        var loads = 0;
        await using (var boundSource = new IndexedItemsSource<string>(new(new("validation", "bound-slot", "rows"), 1), 10,
            dispatcher, (request, _) => Task.FromResult(new IndexedRangeResult<string>(request.Query, request.RequestId, request.StartIndex,
                Enumerable.Range(request.StartIndex, request.Count).Select(index => new KeyedCollectionItem<string>($"item.{index}", $"{Interlocked.Increment(ref loads)}:{index}")).ToArray(),
                Lifetime: request.RequestId == 1 ? boundOwner : new Lifetime())), pageSize: 2))
        {
            boundSource.RangesChanged(new(0, 2), []);
            await Until(() => boundSource.CompletedLoads == 1);
            var bound = (IndexedItem<string>)boundSource[0];
            var oldValue = bound.Value;
            var notifications = 0;
            EventHandler observer = (_, _) => ++notifications;
            bound.StateChanged += observer;
            boundSource.RangesChanged(new(4, 2), []);
            await Until(() => boundSource.CompletedLoads == 2 && boundOwner.Releases == 1);
            Check(ReferenceEquals(bound, boundSource[0]) && boundSource.ResidentSlots == 3,
                "native binding keeps only its stable slot without retaining page authority or creating provider demand");
            boundSource.RangesChanged(new(0, 2), []);
            await Until(() => boundSource.CompletedLoads == 3);
            Check(ReferenceEquals(bound, boundSource[0]) && bound.Value != oldValue && notifications == 1,
                "range reentry updates the still-bound native slot");
            bound.StateChanged -= observer;
            boundSource.RangesChanged(new(8, 2), []);
            await Until(() => boundSource.CompletedLoads == 4 && boundSource.ResidentSlots == 2);
            Check(!ReferenceEquals(bound, boundSource[0]), "recycled native binding no longer pins slot identity");
        }
        return checks;

        static async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Bound slot lifetime did not settle.");
                await Task.Delay(10);
            }
        }

        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); ++checks; }
        static IndexedRangeResult<string> Result(IndexedRangeRequest request, Lifetime owner) =>
            new(request.Query, request.RequestId, request.StartIndex,
                Enumerable.Range(request.StartIndex, request.Count).Select(index =>
                    new KeyedCollectionItem<string>($"item.{index}", $"{request.ContentRevision}:{index}")).ToArray(),
                request.ContentRevision, owner);
    }
}
