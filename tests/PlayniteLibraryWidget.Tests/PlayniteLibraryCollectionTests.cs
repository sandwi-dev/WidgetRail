using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void BrowseItemCacheRetainsIdentityWithoutRetainingStalePresentation()
    {
        var items = Enumerable.Range(0, 3).Select(index => PlayniteLibraryItem.From(
            Item("app-" + index, "saved-" + index, "Game " + index, "Steam"))).ToArray();
        var inputs = items.Select(item => new PlayniteLibraryPresentation.TileInput(
            item.Presentation.DisplayName, "Steam", item.Value.SavedId, null, false, null,
            item, false, true, item.Key, true, null, null, false, true)).ToArray();
        var cache = PlayniteLibraryPresentation.CreateBrowseItemCache();
        var initial = cache.Capture(inputs);
        var reordered = cache.Capture([inputs[2], inputs[0], inputs[1]]);
        Assert.AreSame(initial[2], reordered[0]);
        Assert.AreSame(initial[0], reordered[1]);
        var launching = cache.Capture([inputs[0] with { Launching = true }, inputs[1], inputs[2]]);
        Assert.AreNotSame(initial[0], launching[0]);
        Assert.AreSame(initial[1], launching[1]);
        var artwork = cache.Capture([inputs[0] with { ArtworkHandle = "art.new" }, inputs[1], inputs[2]]);
        Assert.AreNotSame(launching[0], artwork[0]);
        Assert.AreSame(launching[1], artwork[1]);
        var busy = cache.Capture(inputs.Select(item => item with { Interactive = false }).ToArray());
        Assert.AreNotSame(initial[1], busy[1]);
        cache.Capture([inputs[0]]);
        var restored = cache.Capture(inputs);
        Assert.AreNotSame(initial[1], restored[1], "Evicted declarations are not retained indefinitely.");
    }

    [TestMethod]
    public void CachedBrowseMatchesUncachedPresentationThroughDataAndActionChanges()
    {
        var items = Enumerable.Range(0, 4).Select(index => PlayniteLibraryItem.From(
            Item("app-" + index, "saved-" + index, "Game " + index, "Steam"))).ToArray();
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, items),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []);
        var cache = PlayniteLibraryPresentation.CreateBrowseItemCache();
        var firstView = new PresentationWidget(PlayniteLibraryPresentation.Render(state, cache))
            .RenderSnapshot("reuse.fixture", 1);
        var statusView = new PresentationWidget(PlayniteLibraryPresentation.Render(state with { Status = "Updated" }, cache))
            .RenderSnapshot("reuse.fixture", 2);
        var firstItems = Nodes(firstView.Root).Single(node => node.Id == PlayniteLibraryPresentation.ScrollId).Children;
        var statusItems = Nodes(statusView.Root).Single(node => node.Id == PlayniteLibraryPresentation.ScrollId).Children;
        for (var index = 0; index < firstItems.Count; ++index)
            Assert.AreSame(firstItems[index].Children, statusItems[index].Children,
                "A status-only render must reuse the actual production poster's immutable content.");
        foreach (var scenario in new[]
        {
            state, state with { Status = "Unrelated status update" },
            state with { LaunchingSavedId = items[1].Value.SavedId },
            state with { OrganizationBusy = true },
            state with { BrowseRetained = true },
            state with { Collection = Snapshot(WidgetPagedResourceStatus.Ready, items.Reverse().ToArray()) },
            state with { Organization = state.Organization with
            {
                FavoriteSavedIds = [items[0].Value.SavedId],
                Categories = [new("category-a", "Favorites", [items[0].Value.SavedId])],
            } },
            state,
        })
        {
            var cached = new PresentationWidget(PlayniteLibraryPresentation.Render(scenario, cache))
                .RenderSnapshot("cache.fixture", 1);
            var eager = new PresentationWidget(PlayniteLibraryPresentation.Render(scenario))
                .RenderSnapshot("cache.fixture", 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(cached).Count);
            CollectionAssert.AreEqual(SnapshotJson.Serialize(eager), SnapshotJson.Serialize(cached));
        }
    }
}
