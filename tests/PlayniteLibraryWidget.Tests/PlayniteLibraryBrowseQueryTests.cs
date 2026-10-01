using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void FinalBrowseMembershipMatchesExistingProjectedPoliciesAndKeepsProviderOrder()
    {
        var games = BrowseGames(8);
        var ids = new[] { games[0].Id, games[3].Id, games[5].Id, games[6].Id };
        var settings = new WidgetAppLibraryQuery(false) { SearchText = "game", FavoriteSavedIds = ids };
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            FavoriteSavedIds = [games[0].Id, games[3].Id, games[5].Id],
            ExcludedSavedIds = [games[6].Id],
            TitleOverrides = [new(games[0].Id, "Zulu game"), new(games[1].Id, "No match")],
            VariantGroups = [new("variant.games", [games[0].Id, games[3].Id], games[3].Id)],
        };
        var projected = new List<string>();
        var source = BrowseCapture(games, settings, game => projected.Add(game.Id));
        var query = new PlayniteLibraryBrowseQuery(source, organization, favoriteOnly: true);
        Assert.AreEqual(3, query.Count);
        Assert.AreEqual(0, projected.Count, "Computing membership must not project item metadata/artwork or create trees.");

        var actual = query.ReadRange(0, query.Count);
        CollectionAssert.AreEqual(new[] { "Zulu game", "Game 3", "Game 5" }, actual.Select(item => item.Row.Display.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, true }, actual.Select(item => item.Row.Favorite).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false }, actual.Select(item => item.Row.Preferred).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 2, 0 }, actual.Select(item => item.Row.GroupSize).ToArray());
        CollectionAssert.AreEqual(new[] { games[0].Id, games[3].Id, games[5].Id }, projected);
        Assert.AreEqual("Zulu game", actual[0].Item.Presentation.DisplayName, "Title overrides do not re-sort the provider order.");
        Assert.IsTrue(query.TryGetIndex(actual[2].Key, out var index));
        Assert.AreEqual(2, index);
        Assert.AreEqual(5, actual[2].ProviderItem.Index);
        Assert.AreEqual(2, actual[2].Index);
        Assert.AreEqual(actual[2].Key, query.KeyAt(2));
    }

    [TestMethod]
    public void FinalBrowseRandomRangesPrepareOnlyDemandedRowsAndAreBounded()
    {
        var games = BrowseGames(10_000);
        var projected = new List<string>();
        var source = BrowseCapture(games, new(false), game => projected.Add(game.Id));
        var organization = PlayniteLibraryPrivateState.Empty with { ExcludedSavedIds = [games[0].Id, games[5_001].Id] };
        var query = new PlayniteLibraryBrowseQuery(source, organization, false);
        Assert.AreEqual(9_998, query.Count);
        Assert.AreEqual(0, projected.Count);
        var deep = query.ReadRange(8_998, 64);
        Assert.AreEqual(games[9_000].Id, deep[0].Item.Value.SavedId);
        Assert.AreEqual(64, projected.Count);
        var sparse = query.ReadRange(4_999, 3);
        CollectionAssert.AreEqual(new[] { games[5_000].Id, games[5_002].Id, games[5_003].Id },
            sparse.Select(row => row.Item.Value.SavedId).ToArray());
        Assert.AreEqual(67, projected.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(0, 65));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(query.Count, 1));
        Assert.ThrowsExactly<OperationCanceledException>(() => query.ReadRange(0, 1, new(true)));
        Assert.AreEqual(67, projected.Count);
    }

    [TestMethod]
    public void FinalBrowseFreezesOrganizationAndDoesNotInjectHomeFixedRows()
    {
        var games = BrowseGames(3);
        var titles = new List<PlayniteLibraryTitleOverride> { new(games[0].Id, "Frozen title") };
        var favorites = new List<string> { games[0].Id };
        var members = new List<string> { games[0].Id };
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = [new("outside", "Frozen title", "Steam")],
            ManualSavedIds = ["outside"], TitleOverrides = titles, FavoriteSavedIds = favorites,
            Categories = [new("category.test", "Original", members)],
            VariantGroups = [new("variant.test", members, games[0].Id)],
        };
        var source = BrowseCapture(games, new(false));
        var query = new PlayniteLibraryBrowseQuery(source, organization, true);
        titles[0] = new(games[0].Id, "Changed title"); favorites.Clear(); members.Clear();
        var row = query.ReadRange(0, 1)[0];
        Assert.AreEqual(1, query.Count);
        Assert.AreEqual("Frozen title", row.Item.Presentation.DisplayName);
        Assert.IsTrue(row.Row.Favorite);
        Assert.IsTrue(row.Row.Preferred);
        Assert.AreEqual(1, row.Row.GroupSize);
        Assert.AreEqual(games[0].Id, query.Categories[0].SavedIds.Single());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList)query.Categories[0].SavedIds)[0] = "changed");
        Assert.IsFalse(query.TryGetIndex(PlayniteLibraryIdentity.Key("outside"), out _));
        var empty = new PlayniteLibraryBrowseQuery(source, organization, true);
        Assert.AreEqual(0, empty.Count, "An empty favorite filter must not become all games.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => empty.ReadRange(0, 1));
        Assert.ThrowsExactly<ArgumentException>(() => new PlayniteLibraryBrowseQuery(
            BrowseCapture(games, new(false), scope: PlayniteLibraryQueryScope.Home), organization, false));
    }

    [TestMethod]
    public async Task FinalBrowseArtworkUsesCapturedProviderOccurrenceAndRejectsAnotherProjection()
    {
        var games = BrowseGames(4);
        var calls = new List<(string, string)>();
        var source = BrowseCapture(games, new(false), artwork: (game, handle, token) =>
        {
            token.ThrowIfCancellationRequested();
            calls.Add((game.Id, handle.Value));
            return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
        });
        var query = new PlayniteLibraryBrowseQuery(source, PlayniteLibraryPrivateState.Empty with
            { ExcludedSavedIds = [games[0].Id, games[2].Id], TitleOverrides = [new(games[3].Id, "Alias")] }, false);
        var row = query.ReadRange(1, 1)[0];
        Assert.AreEqual(games[3].Id, row.Item.Value.SavedId);
        Assert.AreEqual(games[3].Id, row.Item.Value.AppId, "Action identity remains the provider identity, never display text or a retained index.");
        var handle = new WidgetArtworkHandle(row.Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile)!.Handle);
        await query.ResolveArtworkAsync(row, handle);
        Assert.AreEqual((games[3].Id, handle.Value), calls.Single());
        var replacement = new PlayniteLibraryBrowseQuery(source, PlayniteLibraryPrivateState.Empty, false);
        Assert.IsNull(await replacement.ResolveArtworkAsync(row, handle));
        Assert.ThrowsExactly<OperationCanceledException>(() => query.ResolveArtworkAsync(row, handle, new(true)));
        Assert.AreEqual(1, calls.Count);
    }

    private static PlayniteBridgeGame[] BrowseGames(int count) => Enumerable.Range(0, count).Select(index =>
        new PlayniteBridgeGame("game-" + index, "Game " + index, "Steam", true, false, false, null,
            [], [], [], 0, null)).ToArray();

    private static PlayniteLibraryItem BrowseItem(PlayniteBridgeGame game) =>
        PlayniteLibraryItem.From(Item(game.Id, game.Id, game.Name, game.Source, "art-" + game.Id));

    private static PlayniteLibraryCapturedQuery BrowseCapture(IReadOnlyList<PlayniteBridgeGame> games,
        WidgetAppLibraryQuery query, Action<PlayniteBridgeGame>? projected = null,
        PlayniteLibraryQueryScope scope = PlayniteLibraryQueryScope.Library,
        Func<PlayniteBridgeGame, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? artwork = null) =>
        new(query, new(scope), games, PlayniteLibraryAuthorityProjection.Empty, [], "captured", 1, false,
            game => { projected?.Invoke(game); return BrowseItem(game); },
            artwork ?? ((_, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null)));
}
