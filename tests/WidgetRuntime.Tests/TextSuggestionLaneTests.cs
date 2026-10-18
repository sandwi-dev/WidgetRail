using System.Collections.Concurrent;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class TextSuggestionLaneTests
{
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(5000);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    internal static async Task Run()
    {
        var replies = new ConcurrentQueue<RuntimeEnvelope>(); var faults = new ConcurrentQueue<Exception>();
        var probe = new Probe();
        var lane = new WidgetTextSuggestionLane(probe, (reply, _) => { replies.Enqueue(reply); return Task.CompletedTask; }, faults.Enqueue, default);
        var request = new TextSuggestionQuery("search", "catalog", Guid.NewGuid().ToString("N"), 1, "fire");
        Check(lane.TryRead(1, request), "Query refused");
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!lane.Cancel(request with { Text = "forged" }), "Cancellation accepted a different demand");
        Check(lane.Cancel(request), "Exact cancellation failed");
        await Until(() => replies.Count == 1);
        Check(RuntimeJson.FromElement<TextSuggestionResult>(replies.First().Payload).Unavailable, "Cancelled query published author data");
        Check(!lane.TryRead(2, request with { Revision = 2 }), "Ignoring cancellation freed the author capacity early");
        probe.Release.SetResult(new([new("old", "Old", "Old")]));
        await lane.CloseAsync();
        Check(replies.Count == 1 && faults.IsEmpty, "Late provider published twice or poisoned the worker");

        probe = new Probe(); replies.Clear();
        lane = new(probe, (reply, _) => { replies.Enqueue(reply); return Task.CompletedTask; }, faults.Enqueue, default);
        Check(lane.TryRead(3, request), "Timeout query refused");
        await Until(() => replies.Count == 1);
        Check(RuntimeJson.FromElement<TextSuggestionResult>(replies.First().Payload).Unavailable, "Timeout did not return an optional unavailable result");
        Check(!lane.TryRead(4, request with { Revision = 2 }), "Timeout leaked a second callback");
        probe.Release.SetResult(TextSuggestionResult.Empty);
        await lane.CloseAsync();
        Check(faults.IsEmpty, "Optional provider timeout became a fatal worker failure");

        var blocked = new BlockingCancellationProbe(); replies.Clear();
        lane = new(blocked, (reply, _) => { replies.Enqueue(reply); return Task.CompletedTask; }, faults.Enqueue, default);
        try
        {
            Check(lane.TryRead(5, request), "Blocking provider query refused");
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(lane.Cancel(request), "Blocking provider cancellation refused");
            await Until(() => replies.Count == 1);
            Check(RuntimeJson.FromElement<TextSuggestionResult>(replies.First().Payload).Unavailable,
                "A blocking author cancellation callback prevented the terminal reply");
            Check(!lane.TryRead(6, request with { Revision = 2 }), "Blocked cancellation leaked provider capacity");
        }
        finally { blocked.Release.Set(); }
        await lane.CloseAsync();
        Check(faults.IsEmpty, "Throwing cancellation callback poisoned the worker");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Probe : Widget
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<TextSuggestionResult> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WidgetView Render() => new(UI.Text("Suggestions", "root"));
        public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken token)
        { Entered.TrySetResult(); return new(Release.Task); }
    }

    private sealed class BlockingCancellationProbe : Widget
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        public override WidgetView Render() => new(UI.Text("Suggestions", "root"));
        public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken token)
        {
            token.Register(() => { Release.Wait(); throw new InvalidOperationException("Broken cancellation"); });
            Entered.TrySetResult();
            Release.Wait(); // Also blocks before returning a ValueTask.
            return ValueTask.FromResult(TextSuggestionResult.Empty);
        }
    }
}
