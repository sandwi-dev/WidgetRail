using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Playnite;

namespace WidgetRail.Tests.Playnite;

[TestClass]
public sealed class GameLaunchTrackerTests
{
    [TestMethod]
    public void ConflictApprovalExpiresWhenTheRunningSetChanges()
    {
        var tracker = new GameLaunchTracker();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var target = Guid.NewGuid();
        tracker.Starting(first);
        var prompt = tracker.Conflict(target, null)!;
        Assert.AreEqual(first, prompt.Game);
        Assert.IsNull(tracker.Conflict(first, null));
        Assert.IsNull(tracker.Conflict(target, prompt.Revision));
        tracker.Started(first, 101, 20);
        CollectionAssert.AreEquivalent(new[] { first }, tracker.RunningGames());
        tracker.Starting(second);
        Assert.AreEqual(2, tracker.Conflict(target, prompt.Revision)!.Count);
        tracker.Stopped(first);
        Assert.AreEqual(0, tracker.RunningGames().Length);
        tracker.Stopped(second);
        Assert.IsNull(tracker.Conflict(target, null));
    }

    [TestMethod]
    public void OnlyTheRequestedGameAndLaunchCanCompleteTheReceipt()
    {
        var tracker = new GameLaunchTracker();
        var game = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ticket = tracker.Begin(game, false, false);
        tracker.Starting(other);
        tracker.Started(other, 101, 20);
        Assert.AreEqual("pending", tracker.Read(game, ticket)!.State);
        tracker.Starting(game);
        tracker.Started(game, 102, 30);
        Assert.AreEqual(30L, tracker.Read(game, ticket)!.CreatedFileTime);
        var instance = tracker.RunningSessions()[game].ProcessKey;
        Assert.AreEqual(64, instance!.Length);
        Assert.AreNotEqual(instance, tracker.RunningSessions()[other].ProcessKey);
        Assert.IsNull(tracker.Read(other, ticket));
        var again = tracker.Begin(game, true, false);
        Assert.IsNull(tracker.Read(game, ticket));
        Assert.AreEqual(102, tracker.Read(game, again)!.ProcessId);
        tracker.Stopped(game);
        Assert.AreEqual("ended", tracker.Read(game, again)!.State);
        tracker.Started(game, 101, 20);
        Assert.AreEqual("ended", tracker.Read(game, again)!.State);
        tracker.Starting(game);
        Assert.IsNull(tracker.Read(game, again));
    }
}
