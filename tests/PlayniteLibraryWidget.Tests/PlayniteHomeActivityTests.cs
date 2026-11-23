using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using LibraryWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task EmptyInstalledShelfRetainsLibraryEntryWhenHistoryArrives()
    {
        var provider = new FakeHost(0);
        var widget = Create(provider, out TestApplicationService app);
        var incoming = new TaskCompletionSource<PlayniteRecentActivity>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.RecentActivityHandler = token => new(incoming.Task.WaitAsync(token));
        const string browseId = "playnite-library.home.view-library";
        try
        {
            await Interactive(widget);
            await Ready(widget, provider);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "empty-shelf");
            var loading = host.CurrentSnapshot;
            Assert.IsTrue(Nodes(loading.Root).Any(node => node.Id == browseId));
            Assert.IsTrue(Nodes(loading.Root).Any(node => node.Id == LibraryWidget.HomeActivityList + ".loading"));
            var item = provider.ItemFactory(0);
            incoming.SetResult(new([new(item.SavedId, item.Presentation.DisplayName, DateTimeOffset.UtcNow, 120)], []));
            await PrivateField<WidgetResource<PlayniteRecentActivity>>(widget, "homeRecentActivity").WhenIdleAsync();
            var ready = host.PublishSnapshot();
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(ready).Count);
            Assert.AreEqual(loading.InitialFocusId, ready.InitialFocusId);
            var nodes = Nodes(ready.Root).ToArray();
            var list = nodes.Single(node => node.Id == LibraryWidget.HomeActivityList);
            Assert.AreEqual(1, list.IndexedCollection!.Count);
            Assert.AreEqual(browseId, list.Focus?.Up);
            Assert.AreEqual(list.Id, nodes.Single(node => node.Id == browseId).Focus?.Down);
            Assert.IsFalse(nodes.Any(node => node.Id == PlayniteLibraryPresentation.HomeRailId));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task DailyUnlocksKeepIndividualGridCellsAndReturnToTheirOwnAchievement()
    {
        var provider = new FakeHost(4);
        var widget = Create(provider, out TestApplicationService app);
        var first = provider.ItemFactory(0);
        var second = provider.ItemFactory(1);
        var at = new DateTimeOffset(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local));
        app.RecentActivityHandler = _ => ValueTask.FromResult(new PlayniteRecentActivity([], [
            new(first.SavedId, first.Presentation.DisplayName, "rare", "Last gate", at, 4.2) { Description = "Open the final gate." },
            new(second.SavedId, second.Presentation.DisplayName, "start", "First light", at.AddMinutes(-1), null),
            new(second.SavedId, second.Presentation.DisplayName, "third", "Together", at.AddHours(-1), 15.2),
            new(second.SavedId, second.Presentation.DisplayName, "older", "Homecoming", at.AddDays(-1), null),
        ]));
        try
        {
            await Interactive(widget);
            await Ready(widget, provider);
            await PrivateField<WidgetResource<PlayniteRecentActivity>>(widget, "homeRecentActivity").WhenIdleAsync();
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "daily-unlocks");
            var list = Nodes(host.CurrentSnapshot.Root).Single(n => n.Id == LibraryWidget.HomeActivityList);
            Assert.AreEqual(2, list.IndexedGroups!.Count);
            Assert.AreEqual(CollectionLayoutKind.AdaptiveGrid, list.CollectionLayout!.Kind);
            Assert.AreEqual(4, list.IndexedCollection!.Count);
            CollectionAssert.AreEqual(new[] { 3, 1 }, list.IndexedGroups.Select(group => group.Count).ToArray());
            Assert.IsTrue(list.IndexedGroups.All(group => group.ShowHeaderRule));
            string[] expectedNames = ["Last gate", "First light", "Together"];
            for (var i = 0; i < expectedNames.Length; i++)
            {
                using var range = await host.AcquireAsync(list.Id, 0, 3);
                var chosen = range.Range.Items[i];
                Assert.IsFalse(chosen.Root.IsReadingGroup);
                Assert.IsTrue(Nodes(range.Range.Items[0].Root).Any(n => n.Text == "Open the final gate."));
                Assert.AreEqual(WidgetOperationAdmission.Enqueued, range.RouteAction(chosen.Key, ControllerButton.A));
                await WaitIndexed(() => widget.RenderState.Value.DetailsItem?.Value.SavedId == (i == 0 ? first : second).SavedId);
                Assert.AreEqual(expectedNames[i], widget.RenderState.Value.DetailsExtras.AchievementSearch);
                host.PublishSnapshot();
                await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
                Assert.AreEqual(chosen.Key, host.PublishSnapshot().FocusGroupEntryRequest?.IndexedItem?.ItemKey);
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task HomeShowsGamesBeforeLateActivityAndReturnsToTheSelectedPost()
    {
        var provider = new FakeHost(4);
        var widget = Create(provider, out TestApplicationService app);
        var incoming = new TaskCompletionSource<PlayniteRecentActivity>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.RecentActivityHandler = token => new(incoming.Task.WaitAsync(token));
        var item = provider.ItemFactory(2);
        var date = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        try
        {
            await Interactive(widget);
            await Ready(widget, provider);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "home-activity");
            var before = host.CurrentSnapshot;
            Assert.IsTrue(Nodes(before.Root).Any(node => node.Id == PlayniteLibraryPresentation.HomeRailId));
            Assert.IsFalse(Nodes(before.Root).Any(node => node.Id == LibraryWidget.HomeActivityList));
            incoming.SetResult(new([new(item.SavedId, item.Presentation.DisplayName, date, 600),
                new(item.SavedId, item.Presentation.DisplayName, date.AddHours(1), 180)],
                [new(item.SavedId, item.Presentation.DisplayName, "unlock", "Last gate", date, 20)]));
            await PrivateField<WidgetResource<PlayniteRecentActivity>>(widget, "homeRecentActivity").WhenIdleAsync();
            var ready = host.PublishSnapshot();
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(ready).Count);
            var list = Nodes(ready.Root).Single(node => node.Id == LibraryWidget.HomeActivityList);
            Assert.AreEqual(2, list.IndexedCollection!.Count);
            CollectionAssert.AreEqual(new[] { 1, 1 }, list.IndexedGroups!.Select(group => group.Count).ToArray());
            StringAssert.Contains(list.IndexedGroups![0].Header, "Play sessions");
            Assert.IsTrue(list.IndexedGroups[0].ShowHeaderRule);
            Assert.IsFalse(list.IndexedGroups[1].ShowHeaderRule);
            Assert.AreEqual("Achievements", list.IndexedGroups[1].Header);
            using var range = await host.AcquireAsync(LibraryWidget.HomeActivityList, 0, 2);
            StringAssert.StartsWith(range.Range.Items[0].Root.AccessibilityLabel!, "13m across 2 sessions");
            var post = range.Range.Items.Single(row => row.Root.AccessibilityLabel!.StartsWith("Unlocked Last gate", StringComparison.Ordinal));
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, range.RouteAction(post.Key, ControllerButton.A));
            await WaitIndexed(() => widget.RenderState.Value.DetailsItem?.Value.SavedId == item.SavedId);
            Assert.AreEqual(PlayniteDetailsTab.Achievements, widget.RenderState.Value.DetailsExtras.Tab);
            Assert.AreEqual("Last gate", widget.RenderState.Value.DetailsExtras.AchievementSearch);
            host.PublishSnapshot();
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
            var returned = host.PublishSnapshot();
            Assert.AreEqual(LibraryWidget.HomeActivityList, returned.FocusGroupEntryRequest?.GroupId);
            Assert.AreEqual(post.Key, returned.FocusGroupEntryRequest?.IndexedItem?.ItemKey);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
}
