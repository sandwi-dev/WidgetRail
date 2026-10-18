using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TextSuggestionControllerTests
{
    private static TextSuggestionResult Result(string value) => new([new("match", value, value)]);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(3000);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    [TestMethod]
    public async Task RapidEditsCoalesceAndLateRepliesCannotReplaceNewText()
    {
        var calls = new ConcurrentQueue<(TextSuggestionQuery Query, TaskCompletionSource<TextSuggestionResult> Reply)>();
        var views = new ConcurrentQueue<TextSuggestionView>();
        using var owner = new TextSuggestionController("search", new("catalog"), 100, (query, _) =>
        {
            var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Enqueue((query, reply)); return reply.Task;
        }, views.Enqueue, debounce: 10, interval: 10);
        owner.Update("f"); owner.Update("fi"); owner.Update("fire");
        await Until(() => calls.Count == 1);
        Assert.IsTrue(calls.TryPeek(out var first)); Assert.AreEqual("fire", first.Query.Text);
        owner.Update("ice"); owner.Update("ice spear");
        await Task.Delay(30);
        Assert.AreEqual(1, calls.Count, "A cancellation-ignoring query must not spawn concurrent provider calls.");
        first.Reply.SetResult(Result("Fireball"));
        await Until(() => calls.Count == 2);
        var last = calls.Last(); Assert.AreEqual("ice spear", last.Query.Text);
        last.Reply.SetResult(Result("Ice Spear"));
        await Until(() => views.Any(view => view.Items.Count == 1));
        Assert.IsFalse(views.Any(view => view.Items.Any(item => item.Value == "Fireball")));
        Assert.AreEqual("Ice Spear", views.Last().Items.Single().Value);
    }

    [TestMethod]
    public async Task SelectionReplacesWholeEntryWithoutSubmittingOrQueryingAgain()
    {
        var views = new ConcurrentQueue<TextSuggestionView>(); var calls = 0;
        using var owner = new TextSuggestionController("search", new("catalog"), 100,
            (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult(Result("Ice Tipped Spear")); }, views.Enqueue, 1, 1);
        owner.Update("ice tipped");
        await Until(() => views.Any(view => view.Items.Count > 0));
        var shown = views.Last();
        Assert.AreEqual("Ice Tipped Spear", owner.Accept(shown.Revision, "match"));
        Assert.IsNull(owner.Accept(shown.Revision, "match"), "An old UI button must lose selection authority.");
        owner.Update("Ice Tipped Spear");
        await Task.Delay(25); Assert.AreEqual(1, calls);
        Assert.AreEqual("Ice Tipped Spear", views.Last().Text);
    }

    [TestMethod]
    public async Task TimeoutDoesNotStartMoreWorkUntilIgnoringProviderActuallyDrains()
    {
        var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new ConcurrentQueue<TextSuggestionView>(); var calls = 0;
        using var owner = new TextSuggestionController("search", new("catalog"), 100,
            (_, _) => { Interlocked.Increment(ref calls); return calls == 1 ? reply.Task : Task.FromResult(Result("New")); }, views.Enqueue, 1, 1, 30);
        owner.Update("old"); await Until(() => views.Any(view => view.Unavailable));
        owner.Update("new"); await Task.Delay(50); Assert.AreEqual(1, calls);
        reply.SetResult(Result("Old"));
        await Until(() => calls == 2 && views.Any(view => view.Items.Count > 0));
        Assert.AreEqual("New", views.Last().Items.Single().Value);
    }

    [TestMethod]
    public async Task CloseAndShortQueryRetireOutstandingResults()
    {
        var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new ConcurrentQueue<TextSuggestionView>(); var entered = new TaskCompletionSource();
        var owner = new TextSuggestionController("search", new("catalog", 2), 100,
            (_, _) => { entered.TrySetResult(); return reply.Task; }, views.Enqueue, 1, 1);
        owner.Update("fire"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        owner.Update(""); Assert.IsFalse(views.Last().Loading);
        owner.Dispose(); var count = views.Count; reply.SetResult(Result("Fireball"));
        await Task.Delay(30); Assert.AreEqual(count, views.Count);
        Assert.IsFalse(views.Any(view => view.Items.Count > 0));
    }

    [TestMethod]
    public async Task InvalidDuplicateOrOversizeResultsStayUnavailable()
    {
        foreach (var invalid in new[]
        {
            new TextSuggestionResult([new("same", "One", "One"), new("same", "Two", "Two")]),
            new TextSuggestionResult([new("one", "One", new string('x', 101))]),
            new TextSuggestionResult([new("one", "bad\nlabel", "One")]),
            new TextSuggestionResult(Enumerable.Range(0, 9).Select(i => new TextSuggestion("id" + i, "One", "One")).ToArray()),
        })
        {
            var views = new ConcurrentQueue<TextSuggestionView>();
            using var owner = new TextSuggestionController("search", new("catalog"), 100, (_, _) => Task.FromResult(invalid), views.Enqueue, 1, 1);
            owner.Update("one"); await Until(() => views.Any(view => view.Unavailable));
            Assert.IsFalse(views.Any(view => view.Items.Count > 0));
        }
    }

    [TestMethod]
    public async Task DefaultPacingCapsEvenFastProviders()
    {
        var times = new ConcurrentQueue<long>();
        using var owner = new TextSuggestionController("search", new("catalog"), 100,
            (_, _) => { times.Enqueue(Environment.TickCount64); return Task.FromResult(Result("Gem")); }, _ => { });
        for (var i = 0; i < 5; i++)
        {
            owner.Update("gem" + i);
            var expected = i + 1;
            await Until(() => times.Count == expected);
        }
        var starts = times.ToArray();
        for (var i = 1; i < starts.Length; i++) Assert.IsTrue(starts[i] - starts[i - 1] >= 240, "Host request rate exceeded its default cap.");
    }
}
