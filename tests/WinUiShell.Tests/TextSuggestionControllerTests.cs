using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TextSuggestionControllerTests
{
    private static TextSuggestionResult Result(string value) => new([new("match", value, value)]);
    [TestMethod]
    public async Task RefreshRetainsVisibleResultsButRevokesAllSelectionUntilCurrentReply()
    {
        var views = new ConcurrentQueue<TextSuggestionView>();
        var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var owner = new TextSuggestionController("search", new("catalog"), 100,
            (_, _) => Interlocked.Increment(ref calls) == 1 ? Task.FromResult(Result("Fireball")) : reply.Task,
            views.Enqueue, 1, 1);
        owner.Update("fi"); await Until(() => views.LastOrDefault()?.Selectable == true);
        var original = views.Last();
        owner.Update("fi"); await Task.Delay(20); Assert.AreEqual(1, calls);
        owner.Update("ice");
        var retained = views.Last();
        Assert.AreEqual("Fireball", retained.Items.Single().Value);
        Assert.IsTrue(retained.Stale); Assert.IsFalse(retained.Selectable);
        Assert.IsNull(owner.Accept(original.Revision, "match"));
        Assert.IsNull(owner.Accept(retained.Revision, "match"));
        await Until(() => calls == 2);
        Assert.IsTrue(views.Last().Loading); Assert.AreEqual("Fireball", views.Last().Items.Single().Value);
        reply.SetResult(Result("Ice Spear"));
        await Until(() => views.LastOrDefault()?.Selectable == true);
        Assert.AreEqual("Ice Spear", owner.Accept(views.Last().Revision, "match"));
    }

    [TestMethod]
    public async Task ShortInputAndEmptyReplyClearRetainedResults()
    {
        var views = new ConcurrentQueue<TextSuggestionView>();
        using var owner = new TextSuggestionController("search", new("catalog", MinimumLength: 2), 100,
            (query, _) => Task.FromResult(query.Text == "none" ? TextSuggestionResult.Empty : Result("Gem")), views.Enqueue, 1, 1);
        owner.Update("gem"); await Until(() => views.LastOrDefault()?.Selectable == true);
        owner.Update("g"); Assert.AreEqual(0, views.Last().Items.Count); Assert.IsFalse(views.Last().Stale);
        owner.Update("gem"); await Until(() => views.LastOrDefault()?.Selectable == true);
        owner.Update("none"); await Until(() => views.LastOrDefault() is { Text: "none", Queried: true });
        Assert.AreEqual(0, views.Last().Items.Count);
    }

    [TestMethod]
    public async Task LatestPendingDisplayExpiresEvenWhenOldProviderIgnoresCancellation()
    {
        var views = new ConcurrentQueue<TextSuggestionView>();
        var blocked = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var owner = new TextSuggestionController("search", new("catalog"), 100,
            (query, _) => { Interlocked.Increment(ref calls); return query.Text == "old" ? blocked.Task : Task.FromResult(Result(query.Text)); },
            views.Enqueue, 1, 1, timeout: 100);
        owner.Update("gem"); await Until(() => views.LastOrDefault()?.Selectable == true);
        owner.Update("old"); await Until(() => calls == 2);
        owner.Update("latest");
        await Until(() => views.LastOrDefault() is { Text: "latest", Unavailable: true });
        Assert.AreEqual(0, views.Last().Items.Count); Assert.AreEqual(2, calls);
        blocked.SetResult(Result("Old reply")); await owner.Completion;
        Assert.AreEqual("latest", views.Last().Items.Single().Value); Assert.AreEqual(3, calls);
    }
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

    [TestMethod]
    public async Task ConfiguredTimingIsUsedWithoutPermittingOldUiStates()
    {
        var source = new TextSuggestionSource("remote")
            { DebounceMilliseconds = 300, MinimumRequestIntervalMilliseconds = 500, TimeoutMilliseconds = 5000 };
        var views = new ConcurrentQueue<TextSuggestionView>();
        var calls = new ConcurrentQueue<(long Time, TextSuggestionQuery Query)>();
        using var owner = new TextSuggestionController("search", source, 100, (query, _) =>
        { calls.Enqueue((Environment.TickCount64, query)); return Task.FromResult(Result(query.Text)); }, views.Enqueue);
        var start = Environment.TickCount64;
        owner.Update("first");
        await Until(() => views.Any(view => view.Items.Count > 0));
        Assert.IsTrue(calls.First().Time - start >= 290);
        Assert.AreEqual(5000, calls.First().Query.TimeoutMilliseconds);
        var ready = views.Last();
        var loading = views.Single(view => view.Loading);
        Assert.AreEqual(ready.Revision, loading.Revision);
        Assert.IsFalse(owner.IsCurrent(loading), "A delayed loading notification must not replace ready results for the same revision.");
        Assert.IsTrue(owner.IsCurrent(ready));
        owner.Update("second");
        await Until(() => calls.Count == 2);
        Assert.IsTrue(calls.Last().Time - calls.First().Time >= 490);
    }

    [TestMethod]
    public async Task ClosingDuringConcurrentEditsAndRepliesDrainsWithoutPublishingAfterRetirement()
    {
        // Repeatedly race edits, cancellation and completion rather than relying on one lucky ordering.
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var views = new ConcurrentQueue<TextSuggestionView>();
            var owner = new TextSuggestionController("search", new("catalog"), 100, (_, token) =>
            {
                entered.TrySetResult();
                token.Register(() => { if (iteration % 2 == 0) reply.TrySetCanceled(token); });
                return reply.Task;
            }, views.Enqueue, 0, 0);
            owner.Update("initial"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.WhenAll(Task.Run(() => { for (var i = 0; i < 8; i++) owner.Update("edit" + i); }),
                Task.Run(() => reply.TrySetResult(Result("result"))), Task.Run(owner.Dispose));
            owner.Dispose();
            var afterClose = views.Count;
            await owner.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(afterClose, views.Count);
            Assert.IsFalse(views.Any(owner.IsCurrent));
            Assert.IsNull(owner.Accept(1, "match"));
        }
    }

    [TestMethod]
    public async Task OldEditorCannotPublishIntoReopenedEditorWithTheSameField()
    {
        var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TextSuggestionQuery? oldQuery = null, newQuery = null;
        var oldViews = new ConcurrentQueue<TextSuggestionView>();
        var old = new TextSuggestionController("search", new("catalog"), 100, (query, _) =>
        { oldQuery = query; entered.TrySetResult(); return reply.Task; }, oldViews.Enqueue, 0, 0);
        old.Update("old"); await entered.Task; old.Dispose();
        var newViews = new ConcurrentQueue<TextSuggestionView>();
        using var reopened = new TextSuggestionController("search", new("catalog"), 100, (query, _) =>
        { newQuery = query; return Task.FromResult(Result("new")); }, newViews.Enqueue, 0, 0);
        reopened.Update("new"); await Until(() => newViews.Any(view => view.Items.Count > 0));
        reply.TrySetResult(Result("old")); await old.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreNotEqual(oldQuery!.SessionId, newQuery!.SessionId);
        Assert.IsFalse(oldViews.Any(view => view.Items.Count > 0));
        Assert.AreEqual("new", newViews.Last().Items.Single().Value);
    }

    [TestMethod]
    public async Task EmptyEntryCanRequestRecentSuggestionsOnlyWhenExplicitlyEnabled()
    {
        var defaultsCalled = false;
        var views = new ConcurrentQueue<TextSuggestionView>();
        using var defaults = new TextSuggestionController("search", new("default"), 100,
            (_, _) => { defaultsCalled = true; return Task.FromResult(Result("unexpected")); }, _ => { }, 0, 0);
        using var recent = new TextSuggestionController("search", new("recent", MinimumLength: 0), 100,
            (query, _) => { Assert.AreEqual("", query.Text); return Task.FromResult(Result("Recent search")); }, views.Enqueue, 0, 0);
        defaults.Update(""); recent.Update("");
        await Until(() => views.Any(view => view.Items.Count > 0));
        Assert.IsFalse(defaultsCalled);
        Assert.AreEqual("Recent search", views.Last().Items.Single().Value);
    }
}
