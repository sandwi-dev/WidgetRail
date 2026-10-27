using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class BrowserSessionStartupsTests
{
    [TestMethod]
    public async Task ClosingDuringIgnoredRegistrationPreventsLateBrowserCreation()
    {
        var starts = new BrowserSessionStartups();
        var key = (Widget: "widget", Document: "old-session");
        var registration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var opening = Open();
        await reached.Task;
        Assert.AreEqual(1, starts.CountResidents([]));
        starts.Retire(key);
        using var replacement = starts.TryReserve(key, [], default)!;
        registration.SetResult();
        Assert.IsFalse(await opening);
        Assert.AreEqual(0, created);
        Assert.IsTrue(replacement.IsCurrent, "Late cleanup must not remove the new reservation.");
        Assert.AreEqual(1, starts.CountResidents([]));

        async Task<bool> Open()
        {
            using var lease = starts.TryReserve(key, [], default)!;
            reached.SetResult();
            await registration.Task; // Deliberately ignores cancellation, as a late I/O completion can.
            return lease.TryCommit(() => created++);
        }
    }

    [TestMethod]
    public async Task ConcurrentDelayedStartsCannotOverbookTheFourBrowserLimit()
    {
        var starts = new BrowserSessionStartups();
        var residents = new HashSet<(string Widget, string Document)> { ("a", "one"), ("b", "two"), ("c", "three") };
        var registration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 5).Select(index => Open(("new", index.ToString()))).ToArray();
        Assert.AreEqual(4, starts.CountResidents(residents));
        registration.SetResult();
        var admitted = await Task.WhenAll(tasks);
        Assert.AreEqual(1, admitted.Count(result => result));
        Assert.AreEqual(4, residents.Count);
        Assert.AreEqual(4, starts.CountResidents(residents));

        async Task<bool> Open((string Widget, string Document) key)
        {
            using var lease = starts.TryReserve(key, residents, default);
            if (lease is null) return false;
            await registration.Task;
            return lease.TryCommit(() => residents.Add(key));
        }
    }

    [TestMethod]
    public void LiveSessionLossCancelsOnlyTheMissingStartup()
    {
        var starts = new BrowserSessionStartups();
        using var first = starts.TryReserve(("widget", "first"), [], default)!;
        using var second = starts.TryReserve(("widget", "second"), [], default)!;
        starts.RetireMissing(new HashSet<string> { "second" });
        Assert.IsTrue(first.Token.IsCancellationRequested);
        Assert.IsFalse(first.TryCommit(() => Assert.Fail("Retired session committed.")));
        Assert.IsTrue(second.IsCurrent);
        Assert.AreEqual(1, starts.CountResidents([]));
    }

    [TestMethod]
    public void CommitAndRetirementOverlapCountEachBrowserOnlyOnce()
    {
        var starts = new BrowserSessionStartups();
        var key = (Widget: "widget", Document: "session");
        using var lease = starts.TryReserve(key, [], default)!;
        Assert.AreEqual(1, starts.CountResidents(new[] { key, key }));
        Assert.IsNull(starts.TryReserve(key, [], default));
        lease.Dispose();
        Assert.AreEqual(1, starts.CountResidents(new[] { key, key }));
        Assert.AreEqual(0, starts.CountResidents([]));
    }

    [TestMethod]
    public void WidgetRetirementAndShutdownPreventNewCommits()
    {
        var starts = new BrowserSessionStartups();
        using var lifetime = new CancellationTokenSource();
        using var first = starts.TryReserve(("first", "one"), [], lifetime.Token)!;
        using var second = starts.TryReserve(("second", "two"), [], lifetime.Token)!;
        starts.RetireWidget("first");
        Assert.IsFalse(first.IsCurrent);
        Assert.IsTrue(second.IsCurrent);
        lifetime.Cancel();
        starts.RetireAll();
        Assert.IsFalse(second.TryCommit(() => Assert.Fail("Shutdown startup committed.")));
        Assert.IsNull(starts.TryReserve(("third", "three"), [], lifetime.Token));
        Assert.AreEqual(0, starts.CountResidents([]));
    }
}
