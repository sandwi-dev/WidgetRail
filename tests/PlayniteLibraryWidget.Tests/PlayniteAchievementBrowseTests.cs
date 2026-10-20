using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public void AchievementSearchDoesNotExposeSecretsAndKeysSurviveProgressChanges()
    {
        var secret = new PlayniteAchievement("Spoiler", "Defeat the final boss", false, null, 2, null, true) { Id = "secret", Progress = new(3, 10) };
        var visible = new PlayniteAchievement("Explorer", "Visit ten places", false, null, 10, null, false) { Id = "explorer", Progress = new(8, 10) };
        var state = new PlayniteDetailsExtras { Achievements = new(true, 2, 0, [secret, visible]), AchievementSearch = "boss" };
        Assert.AreEqual(0, new PlayniteAchievementBrowse(1, state).Rows.Count);
        state = state with { AchievementSearch = "", AchievementSort = AchievementSort.Progress };
        Assert.AreEqual("Explorer", new PlayniteAchievementBrowse(1, state).Rows[0].Name);
        var key = PlayniteAchievementEntry.KeyFor(secret);
        Assert.AreEqual(key, PlayniteAchievementEntry.KeyFor(secret with { Unlocked = true, UnlockedAt = DateTimeOffset.UtcNow }));
        Assert.AreEqual(PlayniteAchievementEntry.KeyFor(secret with { Id = null }), PlayniteAchievementEntry.KeyFor(secret with { Id = "" }));
        state = state with { AchievementSearch = "boss", RevealedAchievements = new HashSet<string> { key } };
        Assert.AreEqual(key, new PlayniteAchievementBrowse(1, state).Rows.Single().Key);
    }

    [TestMethod]
    public async Task AchievementBrowseVirtualizesAndReturnsToSameOccurrenceWithoutMoreReads()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        var reads = 0;
        var items = Enumerable.Range(0, 4000).Select(index => new PlayniteAchievement("Achievement " + index,
            "Description " + index, index % 2 == 0, null, index % 100, null, false) { Id = "id." + index }).ToArray();
        application.AchievementsHandler = (_, _) => { reads++; return ValueTask.FromResult(new PlayniteAchievements(true, 4000, 2000, items)); };
        await Interactive(widget);
        await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 160_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievement data");
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-browse");
        var snapshot = host.CurrentSnapshot;
        Assert.IsTrue(Nodes(snapshot.Root).Count() < 200);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var declaration = Nodes(snapshot.Root).Single(node => node.Id == PlayniteAchievementBrowse.ListId);
        Assert.AreEqual(4000, declaration.IndexedCollection!.Count);
        using var lease = await host.AcquireAsync(declaration.Id, 100, 4);
        var row = lease.Range.Items[2];
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(row.Key, ControllerButton.A));
        await WaitIndexed(() => widget.RenderState.Value.DetailsExtras.InspectedAchievement == row.Key);
        var detail = host.PublishSnapshot();
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(detail).Count);
        Assert.IsTrue(Nodes(detail.Root).Any(node => node.Text == "Back to achievements"));
        await widget.OnActionAsync(new(DetailAction + "achievement.back", "back"));
        var returned = host.PublishSnapshot();
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(returned).Count);
        Assert.AreEqual(row.Key, returned.FocusGroupEntryRequest!.IndexedItem!.ItemKey);
        await widget.OnActionAsync(new(DetailAction + "achievement.filter.Locked", "filter"));
        host.PublishSnapshot();
        Assert.AreEqual(2000, Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == declaration.Id).IndexedCollection!.Count);
        Assert.AreNotEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(row.Key, ControllerButton.A));
        await widget.OnActionAsync(new(DetailAction + "achievement.search", "search") { CommittedText = "Description 3999" });
        host.PublishSnapshot();
        Assert.AreEqual(1, Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == declaration.Id).IndexedCollection!.Count);
        await widget.OnActionAsync(new(DetailAction + "achievement.sort.Name", "sort"));
        host.PublishSnapshot();
        Assert.AreEqual(1, reads);
        await widget.OnActionAsync(new(DetailAction + "refresh", "refresh"));
        await Bounded(widget.WhenDetailsIdleAsync(), "refresh");
        host.PublishSnapshot();
        Assert.AreEqual(2, reads);
        Assert.AreEqual("Description 3999", widget.RenderState.Value.DetailsExtras.AchievementSearch);
        Assert.AreEqual(AchievementFilter.Locked, widget.RenderState.Value.DetailsExtras.AchievementFilter);
        await Background(widget);
    }
}
