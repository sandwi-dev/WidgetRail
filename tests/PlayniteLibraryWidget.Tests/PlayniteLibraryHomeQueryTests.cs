using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void HomeIndexedMembershipKeepsManualTitleMatchProviderAndUnavailableOrder()
    {
        var games = BrowseGames(6);
        var app = Item("app", "manual-app", "Utility", "Windows");
        app = app with { Presentation = app.Presentation with { Kind = WidgetAppLibraryKind.Application } };
        var manual = PlayniteLibraryItem.From(app);
        var titleMatch = BrowseItem(games[4]);
        var fixedRows = new PlayniteLibraryFixedRows([], [manual], [titleMatch]);
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = [new("manual-app", "Utility", "Windows"), new("missing", "Unavailable", "Steam")],
            ManualSavedIds = ["manual-app"], FavoriteSavedIds = [games[2].Id, "missing"],
            ExcludedSavedIds = [games[1].Id],
            TitleOverrides = [new(games[0].Id, "ZZZ override"), new("missing", "Missing alias")],
            VariantGroups = [new("variants", [games[0].Id, games[2].Id], games[2].Id)],
        };
        var projected = new List<string>();
        var source = BrowseCapture(games, new(true), game => projected.Add(game.Id), PlayniteLibraryQueryScope.Home);
        var query = new PlayniteLibraryHomeQuery(source, organization, fixedRows, false);
        Assert.AreEqual(0, projected.Count);
        var expectedState = State(Snapshot(WidgetPagedResourceStatus.Ready, games.Select(BrowseItem).Select(item =>
                item.WithProjectedValue(PlayniteLibraryTitlePolicy.Project(organization, item.Value))).ToArray()),
            PlayniteLibraryTitlePolicy.Project(organization), PlayniteLibraryRoute.Library, []) with { FixedRows = fixedRows };
        var expected = PlayniteLibraryHeroRailPolicy.Project(expectedState, null, 0).Items;
        var actual = query.ReadRange(0, query.Count);
        CollectionAssert.AreEqual(expected.Select(row => row.Display).ToArray(), actual.Select(item => item.Row.Display).ToArray());
        CollectionAssert.AreEqual(expected.Select(row => (row.Favorite, row.Preferred, row.GroupSize)).ToArray(),
            actual.Select(item => (item.Row.Favorite, item.Row.Preferred, item.Row.GroupSize)).ToArray());
        CollectionAssert.AreEqual(new[] { "manual-app", games[4].Id, games[0].Id, games[2].Id, games[3].Id, games[5].Id, "missing" },
            actual.Select(item => item.Row.Display.SavedId).ToArray());
        Assert.IsNull(actual[^1].Row.Current, "Unavailable display entries never gain provider authority.");
        Assert.AreEqual(4, projected.Count, "Manual/title-match rows reuse bounded resolutions; only demanded provider rows project.");
    }

    [TestMethod]
    public void HomeIndexedDeepRangesAreBoundedAndDoNotBuildUndemandedRows()
    {
        var games = BrowseGames(10_000);
        var count = 0;
        var source = BrowseCapture(games, new(true), _ => count++, PlayniteLibraryQueryScope.Home);
        var query = new PlayniteLibraryHomeQuery(source, PlayniteLibraryPrivateState.Empty, PlayniteLibraryFixedRows.Empty, false);
        Assert.AreEqual(10_000, query.Count);
        Assert.AreEqual(0, count);
        var range = query.ReadRange(9_000, 4);
        Assert.AreEqual(4, count);
        Assert.AreEqual(games[9_000].Id, range[0].Row.Current!.Value.SavedId);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(0, 65));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(10_000, 1));
        Assert.ThrowsExactly<OperationCanceledException>(() => query.ReadRange(0, 1, new(true)));
        Assert.AreEqual(4, count);
    }

    [TestMethod]
    public async Task HomeArtworkStaysWithExactCapturedProviderOrFixedRow()
    {
        var calls = new List<string>();
        var games = BrowseGames(3);
        var source = BrowseCapture(games, new(true), scope: PlayniteLibraryQueryScope.Home,
            artwork: (game, _, _) => { calls.Add(game.Id); return ValueTask.FromResult<WidgetEncodedArtwork?>(null); });
        var app = Item("app", "manual-app", "Utility", "Windows", "fixed-art");
        app = app with { Presentation = app.Presentation with { Kind = WidgetAppLibraryKind.Application } };
        var fixedRows = new PlayniteLibraryFixedRows([], [PlayniteLibraryItem.From(app)], []);
        var settings = PlayniteLibraryPrivateState.Empty with { ManualSavedIds = ["manual-app"] };
        var query = new PlayniteLibraryHomeQuery(source, settings, fixedRows, false);
        var rows = query.ReadRange(0, 4);
        ValueTask<WidgetEncodedArtwork?> Fixed(WidgetArtworkHandle handle, CancellationToken token)
        { calls.Add(handle.Value); return ValueTask.FromResult<WidgetEncodedArtwork?>(null); }
        await query.ResolveArtworkAsync(rows[3], new("art-game-2"), Fixed, default);
        await query.ResolveArtworkAsync(rows[0], new("fixed-art"), Fixed, default);
        await query.ResolveArtworkAsync(rows[0], new("wrong"), Fixed, default);
        var other = new PlayniteLibraryHomeQuery(source, settings, fixedRows, false);
        await other.ResolveArtworkAsync(rows[3], new("art-game-2"), Fixed, default);
        CollectionAssert.AreEqual(new[] { games[2].Id, "fixed-art" }, calls);
    }
}
