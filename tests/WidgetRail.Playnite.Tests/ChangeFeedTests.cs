using WidgetRail.Playnite;

namespace WidgetRail.Playnite.Tests;

[TestClass]
public sealed class ChangeFeedTests
{
    [TestMethod]
    public async Task EventsHaveBoundedPagesAndOverflowRequiresResync()
    {
        using var feed = new ChangeFeed();
        for (var i = 0; i < 260; i++) feed.Publish(new IntegrationEvent("library-changed"));
        var expired = await feed.PollAsync(0, 0, default);
        Assert.IsTrue(expired.ResetRequired); Assert.AreEqual(260, expired.NextSequence); Assert.AreEqual(0, expired.Events.Count);
        var page = await feed.PollAsync(4, 0, default);
        Assert.IsFalse(page.ResetRequired); Assert.AreEqual(128, page.Events.Count); Assert.AreEqual(132, page.NextSequence);
        var next = await feed.PollAsync(page.NextSequence, 0, default);
        Assert.AreEqual(128, next.Events.Count); Assert.AreEqual(260, next.NextSequence);
        Assert.IsTrue((await feed.PollAsync(261, 0, default)).ResetRequired);
    }
    [TestMethod]
    public async Task ChangeWakesWaiterAndRetirementCancelsRemainingWaits()
    {
        using var feed = new ChangeFeed();
        var wait = feed.PollAsync(0, 25000, default);
        feed.Publish(new IntegrationEvent("game-changed", Guid.NewGuid().ToString("D")));
        Assert.AreEqual(1, (await wait.WaitAsync(TimeSpan.FromSeconds(2))).Events.Count);
        var first = feed.PollAsync(1, 25000, default);
        var second = feed.PollAsync(1, 25000, default);
        await Assert.ThrowsAsync<EventWaitCapacityException>(() => feed.PollAsync(1, 25000, default));
        Assert.IsTrue((await feed.PollAsync(-1, 0, default)).ResetRequired);
        feed.Dispose();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await second);
    }
}
