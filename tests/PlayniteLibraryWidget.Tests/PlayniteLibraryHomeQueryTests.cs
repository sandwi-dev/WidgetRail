using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void HomeFilteredFavoriteDoesNotBecomeUnavailableWhenBrowseCachesItsDisplay()
    {
        var installed = BrowseGames(1)[0] with { Id = "saved-installed" };
        var filtered = installed with { Id = "saved-uninstalled", Name = "Other game", IsInstalled = false, Favorite = true };
        var authority = new PlayniteLibraryAuthorityProjection([filtered.Id], [], [],
            new Dictionary<string, string?> { [installed.Id] = null, [filtered.Id] = null });
        PlayniteLibraryCapturedQuery Capture(params PlayniteBridgeGame[] games) => new(new(true),
            new(PlayniteLibraryQueryScope.Home), games, authority, [], "stable", 1, false,
            BrowseItem, (_, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null));
        var source = Capture(installed);
        var first = new PlayniteLibraryDisplayItem(installed.Id, installed.Name, installed.Source);
        var other = new PlayniteLibraryDisplayItem(filtered.Id, filtered.Name, filtered.Source);
        var privateState = PlayniteLibraryPrivateState.Empty with { Items = [first] };
        PlayniteLibraryHomeQuery Project(PlayniteLibraryPrivateState state) => new(source,
            state with { FavoriteSavedIds = authority.FavoriteGameIds }, PlayniteLibraryFixedRows.Empty, false);
        var before = Project(privateState);
        var browseState = PlayniteLibraryOrganizationPolicy.ProjectDisplay(privateState, [other, first]);
        var afterBrowse = Project(browseState);
        Assert.IsTrue(before.SameMembership(afterBrowse),
            "Caching a known game excluded by Home must not add an unavailable Home row.");
        Assert.AreEqual(1, afterBrowse.Count);
        Assert.AreEqual(0, afterBrowse.ReadRange(0, afterBrowse.Count).Count(item => item.Row.Favorite), "Home's favorites remain scoped to its query.");
        CollectionAssert.AreEqual(new[] { filtered.Id }, authority.FavoriteGameIds.ToArray(),
            "Filtering Home display never mutates the provider's favorite authority.");
        var afterHomeRefresh = Project(PlayniteLibraryOrganizationPolicy.ProjectDisplay(browseState, [first]));
        Assert.IsTrue(afterBrowse.SameMembership(afterHomeRefresh));

        var newlyInstalled = new PlayniteLibraryHomeQuery(Capture(installed, filtered with { IsInstalled = true }),
            browseState with { FavoriteSavedIds = authority.FavoriteGameIds }, PlayniteLibraryFixedRows.Empty, false);
        Assert.IsFalse(afterBrowse.SameMembership(newlyInstalled));
        Assert.AreEqual(2, newlyInstalled.Count);
        Assert.AreEqual(1, newlyInstalled.ReadRange(0, newlyInstalled.Count).Count(item => item.Row.Favorite), "A genuine provider membership change adds the favorite normally.");
    }

    [TestMethod]
    public void HomeReferencedFallbackOrderSurvivesBrowseDisplayCacheRefresh()
    {
        var games = BrowseGames(1).Select(game => game with { Id = "saved-live" }).ToArray();
        var live = new PlayniteLibraryDisplayItem(games[0].Id, games[0].Name, games[0].Source);
        var first = new PlayniteLibraryDisplayItem("saved-missing-z", "Zebra", "Steam");
        var second = new PlayniteLibraryDisplayItem("saved-missing-a", "Alpha", "Steam");
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = [live, first, second],
            FavoriteSavedIds = [first.SavedId, second.SavedId],
        };
        var source = BrowseCapture(games, new(true), scope: PlayniteLibraryQueryScope.Home);
        var before = new PlayniteLibraryHomeQuery(source, organization, PlayniteLibraryFixedRows.Empty, false);
        // Browsing the full library writes display metadata in its own sort order.
        // Neither the Home provider membership nor the user's references changed.
        var refreshed = PlayniteLibraryOrganizationPolicy.ProjectDisplay(organization, [second, live, first]);
        CollectionAssert.AreEqual(organization.FavoriteSavedIds.ToArray(), refreshed.FavoriteSavedIds.ToArray());
        var after = new PlayniteLibraryHomeQuery(source, refreshed, PlayniteLibraryFixedRows.Empty, false);
        Assert.IsTrue(before.SameMembership(after), "Display-cache order must not renew Home's logical query.");
        CollectionAssert.AreEqual(new[] { live.SavedId, first.SavedId, second.SavedId },
            after.ReadRange(0, after.Count).Select(item => item.Row.Display.SavedId).ToArray());
        CollectionAssert.AreEqual(new[] { first.SavedId, second.SavedId },
            after.ReadRange(0, after.Count).Where(item => item.Row.Favorite).Select(item => item.Row.Display.SavedId).ToArray());
        Assert.IsTrue(after.ReadRange(1, 2).All(item => item.Row.Current is null),
            "Stable display ordering must not grant unavailable games provider authority.");

        var reorderedReferences = new PlayniteLibraryHomeQuery(source, refreshed with
        { FavoriteSavedIds = [second.SavedId, first.SavedId] }, PlayniteLibraryFixedRows.Empty, false);
        Assert.IsFalse(after.SameMembership(reorderedReferences),
            "An actual change to domain reference order still replaces logical membership.");
        var removedReference = new PlayniteLibraryHomeQuery(source, refreshed with
        { FavoriteSavedIds = [first.SavedId] }, PlayniteLibraryFixedRows.Empty, false);
        Assert.AreEqual(2, removedReference.Count);
        Assert.IsFalse(after.SameMembership(removedReference));
    }

    [TestMethod]
    public void HomeWarmFallbackPreservesSavedDisplayOrderIncludingUnreferencedEntries()
    {
        var source = BrowseCapture([], new(true), scope: PlayniteLibraryQueryScope.Home);
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = [new("saved-z", "Zebra", "Steam"), new("saved-a", "Alpha", "Steam"), new("saved-m", "Middle", "Steam")],
            FavoriteSavedIds = ["saved-a", "saved-z"],
        };
        var warm = new PlayniteLibraryHomeQuery(source, organization, PlayniteLibraryFixedRows.Empty, false, warmDisplayOnly: true);
        CollectionAssert.AreEqual(new[] { "saved-z", "saved-a", "saved-m" },
            warm.ReadRange(0, warm.Count).Select(item => item.Row.Display.SavedId).ToArray());
        var live = new PlayniteLibraryHomeQuery(source, organization, PlayniteLibraryFixedRows.Empty, false);
        CollectionAssert.AreEqual(new[] { "saved-a", "saved-z" },
            live.ReadRange(0, live.Count).Select(item => item.Row.Display.SavedId).ToArray());
    }

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
        var actual = query.ReadRange(0, query.Count);
        CollectionAssert.AreEqual(new[] { "Utility", "Game 4", "ZZZ override", "Game 2", "Game 3", "Game 5", "Missing alias" },
            actual.Select(item => item.Row.Display.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, false, true, false, false, true }, actual.Select(item => item.Row.Favorite).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, false, true, false, false, false }, actual.Select(item => item.Row.Preferred).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 2, 2, 0, 0, 0 }, actual.Select(item => item.Row.GroupSize).ToArray());
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
