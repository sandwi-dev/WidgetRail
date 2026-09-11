using System.Collections;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PackageRuntimeTests
{
    [TestMethod]
    public async Task HomeOrdersWholeCatalogBeforePagingAndReportsMatchingTotal()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(10);
        client.Games[0] = client.Games[0] with { Favorite = true, Name = "Zulu", LastActivityUnixMilliseconds = 1 };
        client.Games[1] = client.Games[1] with { Favorite = true, Name = "Bravo", LastActivityUnixMilliseconds = 2 };
        client.Games[2] = client.Games[2] with { Favorite = true, Name = "Alpha", LastActivityUnixMilliseconds = 2 };
        await using var service = Service(directory.Path, client);
        var all = new List<string>();
        WidgetCollectionCursor? cursor = null;
        do
        {
            var result = await service.QueryWithAuthorityAsync(AllGames, new(PlayniteLibraryQueryScope.Home),
                cursor, cursor is null ? null : WidgetCursorDirection.After, 2, cursor is null, CancellationToken.None);
            Assert.AreEqual(10, result.MatchingGameCount);
            all.AddRange(result.Page.Items.Select(item => item.SavedId));
            cursor = result.Page.After is { } after ? new(after) : null;
        } while (cursor is not null);
        CollectionAssert.AreEqual(new[] { 2, 1, 0, 9, 8, 7, 6, 5, 4, 3 }.Select(index => client.Games[index].Id).ToArray(), all.ToArray());
        var filtered = await service.QueryWithAuthorityAsync(AllGames with { SearchText = "Alpha" },
            new(PlayniteLibraryQueryScope.Library), null, null, 2, false, CancellationToken.None);
        Assert.AreEqual(1, filtered.MatchingGameCount);
        var serialized = System.Text.Json.JsonSerializer.Serialize(filtered);
        Assert.AreEqual(1, System.Text.Json.JsonSerializer.Deserialize<PlayniteLibraryQueryResult>(serialized)!.MatchingGameCount);
        var empty = await service.QueryWithAuthorityAsync(AllGames with { SearchText = "Nothing matches" },
            new(PlayniteLibraryQueryScope.Library), null, null, 2, false, CancellationToken.None);
        Assert.AreEqual(0, empty.MatchingGameCount);
    }

    private static readonly WidgetAppLibraryQuery AllGames = new(
        InstalledOnly: false,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

    [TestMethod]
    public void ArtworkMemoryCountersRollUpResetSaturateAndStayIdentityFree()
    {
        var counters = new PlayniteArtworkMemoryCounters();
        Parallel.For(0, 32, _ => counters.Record(new(
            PlayniteArtworkMemoryEventKind.Store,
            PlayniteArtworkRole.Cover,
            Bytes: 1024)));
        Parallel.For(0, 32, _ => counters.Record(new(
            PlayniteArtworkMemoryEventKind.Hit,
            PlayniteArtworkRole.Cover,
            Bytes: 1024)));
        counters.Record(new(
            PlayniteArtworkMemoryEventKind.BackgroundToCoverFallback,
            PlayniteArtworkRole.Background));
        counters.Record(new(
            PlayniteArtworkMemoryEventKind.Miss,
            PlayniteArtworkRole.Neutral));
        counters.Record(new(
            PlayniteArtworkMemoryEventKind.Eviction,
            PlayniteArtworkRole.Cover,
            Bytes: 1024));
        var snapshot = counters.Capture();
        Assert.AreEqual(31L, snapshot.Entries);
        Assert.AreEqual(31L * 1024L, snapshot.CurrentBytes);
        Assert.AreEqual(32L * 1024L, snapshot.HighWaterBytes);
        Assert.AreEqual(32L, snapshot.Hits);
        Assert.AreEqual(1L, snapshot.Misses);
        Assert.AreEqual(1L, snapshot.BackgroundToCoverFallbacks);
        Assert.AreEqual(1L, snapshot.Evictions);
        Assert.AreEqual(65L, snapshot.CoverEvents);
        Assert.AreEqual(1L, snapshot.BackgroundEvents);
        Assert.AreEqual(1L, snapshot.NeutralEvents);
        Assert.AreEqual(long.MaxValue,
            PlayniteArtworkMemoryCounters.AddSaturated(long.MaxValue - 1, 2));
        var properties = typeof(PlayniteArtworkMemorySnapshot).GetProperties()
            .Select(property => property.Name).ToArray();
        Assert.IsFalse(properties.Any(name =>
            name.Contains("game", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("handle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("path", StringComparison.OrdinalIgnoreCase)));

        counters.Reset();
        Assert.AreEqual(default, counters.Capture());
    }

    [TestMethod]
    public void ArtworkContentCacheIsByteBoundedLeastRecentlyUsedAndCopyFree()
    {
        var productionBudget = (long)typeof(PlayniteArtworkContentCache).GetField(
            nameof(PlayniteArtworkContentCache.MaximumRetainedBytes),
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Assert.AreEqual(134_217_728L, productionBudget);
        var cache = new PlayniteArtworkContentCache(maximumRetainedBytes: 9);
        var first = new WidgetEncodedArtwork(WidgetArtworkContentType.Png,
            new byte[] { 1, 2, 3, 4 });
        var second = new WidgetEncodedArtwork(WidgetArtworkContentType.Jpeg,
            new byte[] { 5, 6, 7, 8 });
        var third = new WidgetEncodedArtwork(WidgetArtworkContentType.WebP,
            new byte[] { 9, 10, 11, 12 });

        Assert.IsTrue(cache.Store("first", first).Stored);
        Assert.IsTrue(cache.Store("second", second).Stored);
        Assert.AreEqual(8L, cache.RetainedBytes);
        Assert.IsTrue(cache.TryGet("first", out var hit));
        Assert.AreSame(first, hit,
            "A cache hit must return the admitted value without another byte copy.");

        var admitted = cache.Store("third", third);
        Assert.IsTrue(admitted.Stored);
        CollectionAssert.AreEqual(new[] { "second" },
            admitted.Evictions.Select(value => value.Handle).ToArray());
        Assert.IsTrue(cache.Contains("first"));
        Assert.IsFalse(cache.Contains("second"));
        Assert.IsTrue(cache.Contains("third"));
        Assert.AreEqual(8L, cache.RetainedBytes);

        var oversized = cache.Store("oversized",
            new WidgetEncodedArtwork(WidgetArtworkContentType.Png, new byte[10]));
        Assert.IsFalse(oversized.Stored);
        Assert.IsFalse(cache.Contains("oversized"));
        Assert.AreEqual(8L, cache.RetainedBytes);

        var replacement = cache.Store("first",
            new WidgetEncodedArtwork(WidgetArtworkContentType.Png, new byte[6]));
        Assert.IsTrue(replacement.Stored);
        Assert.AreEqual(4, replacement.PreviousBytes);
        CollectionAssert.AreEqual(new[] { "third" },
            replacement.Evictions.Select(value => value.Handle).ToArray());
        Assert.AreEqual(6L, cache.RetainedBytes);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageServiceTraversesTenThousandAndPagesThirtyTwoOrSixtyFour()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(10_000);
        await using var service = Service(directory.Path, client);

        var first = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        Assert.AreEqual(32, first.Page.Items.Count);
        Assert.AreEqual("32", first.Page.After);
        Assert.AreEqual(157, client.QueryCalls,
            "Ten thousand records must be traversed in bounded 64-item Bridge pages.");
        Assert.IsTrue(client.Queries.All(query => query.Limit == 64));
        Assert.IsTrue(client.Queries.All(query => query.IncludeHidden));
        Assert.IsTrue(client.Queries.All(query => !query.InstalledOnly));

        var second = await service.QueryAsync(AllGames,
            new WidgetCollectionCursor(first.Page.After!), WidgetCursorDirection.After,
            64, refresh: false, CancellationToken.None);
        Assert.AreEqual(64, second.Items.Count);
        Assert.AreEqual("96", second.After);
        Assert.AreEqual(157, client.QueryCalls,
            "Presentation paging must reuse the bounded current catalog.");

        var search = await service.QueryAsync(
            AllGames with { SearchText = "Game 09999" }, null, null, 32,
            refresh: false, CancellationToken.None);
        Assert.AreEqual("Game 09999", search.Items.Single().Presentation.DisplayName);
        Assert.AreEqual(client.Games[9999].Id, search.Items.Single().AppId);
        Assert.AreEqual(client.Games[9999].Id, search.Items.Single().SavedId);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageServiceProjectsCurrentPlayniteAuthorityAndExactGuidActions()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(4);
        client.Games[0] = client.Games[0] with
        {
            Favorite = true,
            Categories = ["Controllers"],
            CompletionStatus = "Completed",
        };
        client.Categories.Add(new(FakeLibraryClient.GuidFrom(30_001), "Controllers"));
        client.Games[1] = client.Games[1] with
        {
            IsInstalled = false,
            Source = "Manual",
            Platforms = ["Emulated"],
        };
        await using var service = Service(directory.Path, client);

        var result = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        var installed = result.Page.Items.Single(item => item.AppId == client.Games[0].Id);
        var owned = result.Page.Items.Single(item => item.AppId == client.Games[1].Id);
        Assert.AreEqual(WidgetAppLibraryAvailabilityState.Installed,
            installed.Presentation.Availability.State);
        Assert.Contains(WidgetAppLibraryAction.Launch,
            installed.Presentation.Capabilities.Actions);
        Assert.AreEqual(WidgetAppLibraryAvailabilityState.Unavailable,
            owned.Presentation.Availability.State);
        Assert.IsEmpty(owned.Presentation.Capabilities.Actions);
        Assert.Contains(client.Games[0].Id, result.Authority.FavoriteGameIds);
        Assert.Contains(client.Games[0].Id,
            result.Authority.Categories.Single().SavedIds);
        Assert.AreEqual("Completed",
            result.Authority.CompletionStatuses[client.Games[0].Id]);

        var exactSubset = await service.QueryWithAuthorityAsync(
            AllGames with { FavoriteSavedIds = [client.Games[1].Id] },
            new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.AreEqual(client.Games[1].Id, exactSubset.Page.Items.Single().SavedId,
            "The exact requested saved-ID subset must include a non-favorite game.");
        Assert.IsFalse(exactSubset.Page.Items.Any(item =>
                item.SavedId == client.Games[0].Id),
            "An unrelated favorite game must not satisfy an exact saved-ID subset.");
        CollectionAssert.AreEqual(result.Authority.FavoriteGameIds.ToArray(),
            exactSubset.Authority.FavoriteGameIds.ToArray(),
            "Exact subset filtering must not change authoritative favorite metadata.");

        var favorite = await service.SetFavoriteAsync(
            client.Games[0].Id, false, CancellationToken.None);
        Assert.IsNotNull(favorite);
        Assert.AreEqual(client.Games[0].Id, client.LastResolvedId);
        Assert.AreEqual(client.Games[0].Id, client.LastMutatedId);
        var launch = await service.LaunchObservedAsync(
            client.Games[0].Id, WidgetAppLaunchOverlayBehavior.KeepOpen,
            CancellationToken.None);
        Assert.AreEqual(WidgetAppLaunchObservationState.RequestAccepted, launch.State);
        Assert.IsFalse(launch.SupportsRunning);
        Assert.IsFalse(launch.SupportsEnded);
        Assert.AreEqual(client.Games[0].Id, client.LastLaunchedId);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageServiceUsesAuthoritativeCategoriesAndStableProviderIdentity()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(2);
        var existingProviderId = FakeLibraryClient.GuidFrom(30_001);
        client.Categories.Add(new(existingProviderId, "Controllers"));
        client.Games[0] = client.Games[0] with { Categories = ["controllers"] };
        await using var service = Service(directory.Path, client);

        var initial = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        var existing = initial.Authority.Categories.Single();
        Assert.AreEqual("category." + Guid.Parse(existingProviderId).ToString("N"),
            existing.Id);
        Assert.AreEqual("Controllers", existing.Name);
        CollectionAssert.AreEqual(new[] { client.Games[0].Id },
            existing.SavedIds.ToArray(),
            "Membership names must join case-insensitively to the authoritative category.");

        var created = await service.CreateCategoryAsync("Strategy", CancellationToken.None);
        Assert.IsNotNull(created);
        var listed = client.Categories.Single(category => category.Name == "Strategy");
        Assert.AreEqual("category." + Guid.Parse(listed.Id).ToString("N"), created.Id);

        var refreshed = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        var empty = refreshed.Authority.Categories.Single(category =>
            category.Name == "Strategy");
        Assert.AreEqual(created.Id, empty.Id,
            "Create and authoritative-list projection must use the same provider GUID.");
        Assert.IsEmpty(empty.SavedIds,
            "An authoritative empty category must publish before any game membership exists.");

        client.Categories.Add(new(FakeLibraryClient.GuidFrom(30_002), "Keep"));
        client.Games[1] = client.Games[1] with { Categories = ["Keep"] };
        var changed = await service.SetCategoryMembershipAsync(
            client.Games[1].Id, "Strategy", included: true, CancellationToken.None);
        Assert.IsNotNull(changed);
        Assert.AreEqual(1, client.ResolveCalls,
            "A category membership mutation must use one provider game snapshot.");
        Assert.AreEqual(1, client.SetCategoryCalls);
        CollectionAssert.AreEquivalent(new[] { "Keep", "Strategy" },
            client.Games[1].Categories.ToArray(),
            "The semantic mutation must preserve every unrelated provider category.");

        var assigned = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { client.Games[1].Id },
            assigned.Authority.Categories.Single(category => category.Id == created.Id)
                .SavedIds.ToArray());

        var filtered = await service.QueryWithAuthorityAsync(
            AllGames, new PlayniteLibraryQueryContext(
                PlayniteLibraryQueryScope.Category, "Strategy"), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.AreEqual(client.Games[1].Id, filtered.Page.Items.Single().SavedId,
            "Browse category filtering must use the same authoritative category name.");

        client.CategoryMutationResponseCategories = [];
        var queryCallsBeforeUnconfirmedMutation = client.QueryCalls;
        var unconfirmed = await service.SetCategoryMembershipAsync(
            client.Games[1].Id, "Strategy", included: true, CancellationToken.None);
        Assert.IsNull(unconfirmed,
            "A same-game response that omits the requested membership is not success.");
        var currentAfterUnconfirmedMutation = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.IsGreaterThan(queryCallsBeforeUnconfirmedMutation, client.QueryCalls,
            "An unconfirmed category mutation must dirty the catalog before its remote write.");
        Assert.IsEmpty(currentAfterUnconfirmedMutation.Authority.Categories.Single(category =>
            category.Id == created.Id).SavedIds,
            "A refresh:false query must observe current provider state after an unconfirmed write.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task CategoryResponsesUseProviderBoundsAndExactRequestedIdentity()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(1);
        var longName = new string('C', 64);
        client.Categories.Add(new(FakeLibraryClient.GuidFrom(30_500), longName));
        await using var service = Service(directory.Path, client);

        var listed = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        Assert.AreEqual(longName, listed.Authority.Categories.Single().Name,
            "Provider category names through the Bridge bound must not use the 32-character UI bound.");
        var uiCategoryNameLimit = (int?)typeof(PlayniteLibraryPrivateState)
            .GetField(nameof(PlayniteLibraryPrivateState.MaximumCategoryNameLength),
                BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue();
        Assert.AreEqual(32, uiCategoryNameLimit,
            "The widget's create-entry bound remains intentionally narrower.");

        client.CreatedCategoryResponseName = "Different";
        var queryCallsBeforeMismatchedCreate = client.QueryCalls;
        var mismatched = await service.CreateCategoryAsync(
            "Requested", CancellationToken.None);
        Assert.IsNull(mismatched,
            "A create response must identify the exact normalized requested category.");
        var currentAfterMismatchedCreate = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.IsGreaterThan(queryCallsBeforeMismatchedCreate, client.QueryCalls,
            "A mismatched create response must leave the catalog dirty.");
        Assert.AreEqual("Requested", currentAfterMismatchedCreate.Authority.Categories
            .Single(category => category.Name == "Requested").Name,
            "A refresh:false query must fetch provider state instead of retaining the cache.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedCategoryRefreshRetainsSequenceAndRemainsRetryable()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(1);
        await using var service = Service(directory.Path, client);

        var initial = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: true, CancellationToken.None);
        var initialSequence = initial.Page.Sources.Single().Revision;
        var created = await service.CreateCategoryAsync("Strategy", CancellationToken.None);
        Assert.IsNotNull(created);

        client.FailCategoryLists = true;
        var retained = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.AreEqual(initialSequence, retained.Page.Sources.Single().Revision,
            "A failed post-create refresh must retain the monotonic last-good sequence.");
        Assert.AreEqual(WidgetAppLibrarySourceHealth.Degraded,
            retained.Page.Sources.Single().Health);

        client.FailCategoryLists = false;
        var retried = await service.QueryWithAuthorityAsync(
            AllGames, new(PlayniteLibraryQueryScope.Library), null, null,
            32, refresh: false, CancellationToken.None);
        Assert.AreEqual(initialSequence + 1, retried.Page.Sources.Single().Revision,
            "The dirty catalog must retry without an explicit refresh and advance from last-good.");
        Assert.AreEqual(created.Id,
            retried.Authority.Categories.Single(category => category.Name == "Strategy").Id);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageServiceOwnsBrowseFilteringSortingAndCompleteSourceObservations()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(40);
        client.Games[0] = client.Games[0] with
            { Name = "Zulu", Source = "Steam", Favorite = false };
        client.Games[1] = client.Games[1] with
            { Name = "Alpha", Source = "GOG", Favorite = true };
        client.Games[39] = client.Games[39] with
            { Name = "Omega Xbox", Source = "Xbox", Favorite = false };
        for (var index = 2; index < client.Games.Count - 1; index++)
            client.Games[index] = client.Games[index] with { Source = "Steam" };
        await using var service = Service(directory.Path, client);

        var observed = await service.QueryWithAuthorityAsync(AllGames,
            new(PlayniteLibraryQueryScope.Library), null, null, 32,
            refresh: true, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "GOG", "Steam", "Xbox" },
            observed.Page.Sources.Select(source => source.DisplayName).ToArray(),
            "Source observations must cover the complete bounded catalog, not only the visible page.");
        Assert.IsFalse(observed.Page.Items.Any(item =>
                item.Presentation.Source.DisplayName == "Xbox"),
            "The Xbox source fixture must remain beyond the visible first page.");

        var ascending = await Query(WidgetAppLibrarySortOrder.DisplayName);
        Assert.AreEqual("Alpha", ascending.Items[0].Presentation.DisplayName);
        Assert.AreEqual("Zulu", ascending.Items[^1].Presentation.DisplayName);

        var descending = await Query(WidgetAppLibrarySortOrder.DisplayNameDescending);
        Assert.AreEqual("Zulu", descending.Items[0].Presentation.DisplayName);
        Assert.AreEqual("Alpha", descending.Items[^1].Presentation.DisplayName);

        var bySource = await Query(WidgetAppLibrarySortOrder.SourceThenDisplayName);
        Assert.AreEqual("GOG", bySource.Items[0].Presentation.Source.DisplayName);
        Assert.AreEqual("Alpha", bySource.Items[0].Presentation.DisplayName);
        Assert.IsTrue(bySource.Items.Zip(bySource.Items.Skip(1), (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    left.Presentation.Source.DisplayName,
                    right.Presentation.Source.DisplayName) <= 0)
            .All(value => value));
        var steamNames = bySource.Items.Where(item =>
                item.Presentation.Source.DisplayName == "Steam")
            .Select(item => item.Presentation.DisplayName).ToArray();
        CollectionAssert.AreEqual(steamNames.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase).ToArray(), steamNames,
            "Source sorting must use display name as its deterministic secondary key.");

        var source = await service.QueryAsync(AllGames with
            {
                SourceAttribution = "Xbox",
                Sort = WidgetAppLibrarySortOrder.DisplayName,
            }, null, null, 64, refresh: false, CancellationToken.None);
        Assert.AreEqual(client.Games[39].Id, source.Items.Single().SavedId);

        var favorite = await service.QueryAsync(AllGames with
            {
                FavoriteSavedIds = [client.Games[1].Id],
            }, null, null, 64, refresh: false, CancellationToken.None);
        Assert.AreEqual(client.Games[1].Id, favorite.Items.Single().SavedId);
        Assert.IsTrue(favorite.Items.Single().Presentation.DisplayName == "Alpha");

        ValueTask<WidgetAppLibraryPage> Query(WidgetAppLibrarySortOrder sort) =>
            service.QueryAsync(AllGames with { Sort = sort }, null, null, 64,
                refresh: false,
                CancellationToken.None);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentlyPlayedIsPlayniteAuthoritativeFilteredDeterministicAndBounded()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(40);
        for (var index = 0; index < client.Games.Count; index++)
            client.Games[index] = client.Games[index] with
            {
                Source = "Steam",
                LastActivityUnixMilliseconds = index == 0 ? null : 1_000L + index,
            };
        client.Games[1] = client.Games[1] with
        {
            Name = "bravo",
            LastActivityUnixMilliseconds = 2_000,
        };
        client.Games[2] = client.Games[2] with
        {
            Name = "Alpha",
            LastActivityUnixMilliseconds = 2_000,
        };
        client.Games[3] = client.Games[3] with
        {
            Name = "alpha",
            LastActivityUnixMilliseconds = 2_000,
        };
        client.Games[39] = client.Games[39] with
        {
            Source = "GOG",
            LastActivityUnixMilliseconds = 9_000,
        };
        client.Games[38] = client.Games[38] with
        {
            Hidden = true,
            LastActivityUnixMilliseconds = 8_000,
        };
        client.Games[37] = client.Games[37] with
        {
            IsInstalled = false,
            LastActivityUnixMilliseconds = 10_000,
        };
        await using var service = Service(directory.Path, client);

        var result = await service.QueryWithAuthorityAsync(
            AllGames with { InstalledOnly = true, SourceAttribution = "Steam" },
            new(PlayniteLibraryQueryScope.RecentlyPlayed), null, null,
            64, refresh: true, CancellationToken.None);
        var expected = client.Games
            .Where(game => game.IsInstalled && !game.Hidden && game.Source == "Steam" &&
                game.LastActivityUnixMilliseconds is not null)
            .OrderByDescending(game => game.LastActivityUnixMilliseconds)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.Id, StringComparer.Ordinal)
            .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
            .Select(game => game.Id)
            .ToArray();

        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumRecentItems,
            result.Page.Items.Count);
        CollectionAssert.AreEqual(expected,
            result.Page.Items.Select(item => item.SavedId).ToArray());
        Assert.IsFalse(result.Page.Items.Any(item => item.SavedId == client.Games[0].Id),
            "A game without Playnite LastActivity must not enter Recently played.");
        Assert.IsFalse(result.Page.Items.Any(item => item.SavedId == client.Games[38].Id ||
            item.SavedId == client.Games[39].Id || item.SavedId == client.Games[37].Id),
            "Installed/source/hidden filters must apply before the Recently played cap.");
        CollectionAssert.AreEqual(new[]
        {
            client.Games[2].Id,
            client.Games[3].Id,
            client.Games[1].Id,
        }, result.Page.Items.Where(item => item.Presentation.Metadata!
                .LastPlayedAtUnixMilliseconds == 2_000)
            .Select(item => item.SavedId).ToArray(),
            "Equal timestamps must use name then exact ID tie ordering.");

        var intersection = await service.QueryWithAuthorityAsync(
            AllGames with
            {
                InstalledOnly = true,
                SearchText = "Alpha",
                SourceAttribution = "Steam",
                FavoriteSavedIds = [client.Games[2].Id],
            }, new(PlayniteLibraryQueryScope.RecentlyPlayed), null, null,
            64, refresh: false, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { client.Games[2].Id },
            intersection.Page.Items.Select(item => item.SavedId).ToArray(),
            "Search, source, hidden and exact saved-ID filters must intersect before capping.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task HomeUsesGlobalPlayniteActivityOrderBeforePaging()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(7);
        client.Games[0] = client.Games[0] with
            { Name = "Middle", LastActivityUnixMilliseconds = 100 };
        client.Games[1] = client.Games[1] with
            { Name = "Zulu", LastActivityUnixMilliseconds = 300 };
        client.Games[2] = client.Games[2] with
            { Name = "alpha", LastActivityUnixMilliseconds = 300 };
        client.Games[3] = client.Games[3] with
            { Name = "Beta", LastActivityUnixMilliseconds = null };
        client.Games[4] = client.Games[4] with
            { Name = "alpha", LastActivityUnixMilliseconds = null };
        client.Games[5] = client.Games[5] with
            { Hidden = true, LastActivityUnixMilliseconds = 900 };
        client.Games[6] = client.Games[6] with
            { IsInstalled = false, LastActivityUnixMilliseconds = 1_000 };
        await using var service = Service(directory.Path, client);

        var page = await service.QueryWithAuthorityAsync(
            AllGames with { InstalledOnly = true },
            new(PlayniteLibraryQueryScope.Home), null, null, 64,
            refresh: true, CancellationToken.None);
        var expected = new[]
        {
            client.Games[2].Id,
            client.Games[1].Id,
            client.Games[0].Id,
            client.Games[4].Id,
            client.Games[3].Id,
        };
        CollectionAssert.AreEqual(expected,
            page.Page.Items.Select(item => item.SavedId).ToArray());

        await service.LaunchObservedAsync(client.Games[3].Id,
            WidgetAppLaunchOverlayBehavior.KeepOpen, CancellationToken.None);
        var afterLaunch = await service.QueryWithAuthorityAsync(
            AllGames with { InstalledOnly = true },
            new(PlayniteLibraryQueryScope.Home), null, null, 64,
            refresh: false, CancellationToken.None);
        CollectionAssert.AreEqual(expected,
            afterLaunch.Page.Items.Select(item => item.SavedId).ToArray(),
            "A package-local launch must not manufacture Playnite activity order.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkMissingMalformedAndStaleLastGoodFailClosed()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(2);
        await using var service = Service(directory.Path, client);
        var current = await service.QueryAsync(AllGames, null, null, 32,
            refresh: true, CancellationToken.None);
        var artwork = current.Items[0].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Tile)!;
        var resolvedArtwork = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(artwork.Handle), CancellationToken.None);
        Assert.IsNotNull(resolvedArtwork);
        Assert.AreEqual(WidgetArtworkContentType.Png, resolvedArtwork.ContentType);
        CollectionAssert.AreEqual(
            Convert.FromBase64String(FakeLibraryClient.TinyPng),
            resolvedArtwork.Bytes.ToArray());
        var hero = current.Items[0].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!;
        Assert.AreNotEqual(artwork.Handle, hero.Handle,
            "Cover and background must retain distinct same-game resource handles.");
        var resolvedBackground = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(hero.Handle), CancellationToken.None);
        Assert.IsNotNull(resolvedBackground);
        CollectionAssert.AreEqual(Convert.FromBase64String(FakeLibraryClient.TinyPng),
            resolvedBackground.Bytes.ToArray());
        var gameABytes = resolvedBackground.Bytes.ToArray();
        CollectionAssert.AreEqual(new[]
        {
            PlayniteBridgeArtworkKind.Cover,
            PlayniteBridgeArtworkKind.Background,
        }, client.ArtworkKinds.Take(2).ToArray());

        const string distinctCover =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=";
        client.ArtworkResponses[(client.Games[1].Id,
            PlayniteBridgeArtworkKind.Background)] = null;
        client.ArtworkResponses[(client.Games[1].Id,
            PlayniteBridgeArtworkKind.Cover)] = distinctCover;
        var fallbackHero = current.Items[1].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!;
        var fallbackBackground = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(fallbackHero.Handle), CancellationToken.None);
        Assert.IsNotNull(fallbackBackground,
            "Only a same-game not-found background may fall back to its cover.");
        CollectionAssert.AreEqual(Convert.FromBase64String(distinctCover),
            fallbackBackground.Bytes.ToArray());
        CollectionAssert.AreEqual(new[]
        {
            PlayniteBridgeArtworkKind.Background,
            PlayniteBridgeArtworkKind.Cover,
        }, client.ArtworkKinds.Skip(2).Take(2).ToArray());
        client.Artwork = null;
        client.ArtworkResponses[(client.Games[1].Id,
            PlayniteBridgeArtworkKind.Cover)] = null;
        var otherArtwork = current.Items[1].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Tile)!;
        Assert.IsNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(otherArtwork.Handle), CancellationToken.None));

        using var neutralDirectory = new TestDirectory();
        var missingClient = new FakeLibraryClient(1)
        {
            Artwork = null,
            BackgroundArtwork = null,
        };
        await using var missingService = Service(neutralDirectory.Path, missingClient);
        var missingPage = await missingService.QueryAsync(AllGames, null, null, 32,
            refresh: true, CancellationToken.None);
        var missingHero = missingPage.Items[0].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!;
        var neutral = await missingService.ResolveArtworkAsync(
            new WidgetArtworkHandle(missingHero.Handle), CancellationToken.None);
        Assert.IsNotNull(neutral,
            "A double not-found Hero must actively replace retained artwork.");
        Assert.AreEqual(WidgetArtworkContentType.Png, neutral.ContentType);
        var neutralPixel = DecodeSingleRgbaPng(neutral.Bytes.Span);
        Assert.AreEqual((byte)0, neutralPixel.Red,
            "The neutral Hero pixel must not contribute a red color channel.");
        Assert.AreEqual((byte)0, neutralPixel.Green,
            "The neutral Hero pixel must not contribute a green color channel.");
        Assert.AreEqual((byte)0, neutralPixel.Blue,
            "The neutral Hero pixel must not contribute a blue color channel.");
        Assert.AreEqual((byte)0, neutralPixel.Alpha,
            "The neutral Hero pixel must be fully transparent.");
        CollectionAssert.AreEqual(new[]
        {
            PlayniteBridgeArtworkKind.Background,
            PlayniteBridgeArtworkKind.Cover,
        }, missingClient.ArtworkKinds.ToArray());
        CollectionAssert.AreEqual(new[]
        {
            (missingClient.Games[0].Id, PlayniteBridgeArtworkKind.Background),
            (missingClient.Games[0].Id, PlayniteBridgeArtworkKind.Cover),
        }, missingClient.ArtworkRequests.ToArray());
        CollectionAssert.AreNotEqual(gameABytes, neutral.Bytes.ToArray(),
            "The neutral Hero must never inherit the previously resolved game's bytes.");

        client.FailQueries = true;
        var stale = await service.QueryAsync(AllGames, null, null, 32,
            refresh: true, CancellationToken.None);
        Assert.AreEqual(WidgetAppLibrarySourceHealth.Degraded,
            stale.Sources.Single().Health);
        Assert.IsTrue(stale.Items.All(item =>
            item.Presentation.Availability.State == WidgetAppLibraryAvailabilityState.StaleSource));
        Assert.IsTrue(stale.Items.All(item =>
            item.Presentation.Capabilities.Actions.Count == 0));

        client.FailResolve = true;
        var failure = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(async () =>
            await service.LaunchObservedAsync(client.Games[0].Id,
                WidgetAppLaunchOverlayBehavior.KeepOpen, CancellationToken.None));
        Assert.AreEqual("platform_unavailable", failure.ErrorCode);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkRegistryRetainsEveryOwnerThroughIncomingPageAndFixedRows()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(608);
        var incomingGames = client.Games.Skip(448).Take(64).ToArray();
        var incomingFixedGames = client.Games.Skip(512).Take(64).ToArray();
        foreach (var index in Enumerable.Range(576, 32))
            client.Games[index] = client.Games[index] with { Hidden = true };
        var hiddenGames = client.Games.Skip(576).Take(32).ToArray();
        client.Games.RemoveRange(448, 160);
        var diagnostics = new RecordingArtworkDiagnostics();
        await using var service = Service(directory.Path, client, diagnostics);

        var activePages = await QueryEveryPage(service,
            refresh: true, CancellationToken.None);
        Assert.AreEqual(448, activePages.Count);
        var retainedHome = ArtworkHandles(activePages.Take(192));
        var fixedRows = ArtworkHandles(activePages.Skip(192).Take(64));
        var liveBrowse = ArtworkHandles(activePages.Skip(256).Take(192));
        var activeHandles = retainedHome.Concat(fixedRows).Concat(liveBrowse).ToArray();
        Assert.AreEqual(896, activeHandles.Length,
            "Three simultaneous owners must retain two independently revisioned roles per game.");
        service.PinArtworkHandles(activeHandles);
        Assert.AreEqual(896, PrivateDictionary(service, "_artwork").Count);

        client.Games.AddRange(incomingGames);
        client.Games.AddRange(incomingFixedGames);
        client.Games.AddRange(hiddenGames);
        var allPages = await QueryEveryPage(service,
            refresh: true, CancellationToken.None);
        var incoming = ArtworkHandles(allPages.Skip(448).Take(64));
        Assert.AreEqual(128, incoming.Length,
            "One complete incoming page must fit beside every active owner before the pin swap.");
        var resolvedFixedRows = await service.ResolveSavedAsync(
            incomingFixedGames.Select(game => game.Id).ToArray(),
            CancellationToken.None);
        var incomingFixed = ArtworkHandles(resolvedFixedRows);
        Assert.AreEqual(128, incomingFixed.Length,
            "One complete incoming fixed/title set must fit beside the cursor page.");
        var resolvedHidden = await service.ResolveSavedAsync(
            hiddenGames.Select(game => game.Id).ToArray(), CancellationToken.None);
        var hidden = ArtworkHandles(resolvedHidden);
        Assert.AreEqual(64, hidden.Length,
            "Every simultaneously retained Hidden owner must be budgeted.");
        Assert.AreEqual(1_216, PrivateDictionary(service, "_artwork").Count,
            "Registration must reserve the complete steady owner set plus one incoming transition.");
        Assert.IsTrue(incoming.Concat(incomingFixed).Concat(hidden).All(handle =>
                PrivateDictionary(service, "_artwork").Contains(handle)),
            "No newly admitted page, fixed-row, or Hidden handle may be evicted before the pin swap.");

        var precommitOwners = retainedHome.Concat(incomingFixed).Concat(liveBrowse)
            .Concat(hidden).ToArray();
        Assert.AreEqual(960, precommitOwners.Length,
            "The maximum simultaneous Home, Browse, fixed-row, and Hidden owners changed.");
        service.PinArtworkHandles(precommitOwners);
        var registrations = PrivateDictionary(service, "_artwork");
        Assert.AreEqual(1_088, registrations.Count,
            "The pin swap must retire replaced published rows while preserving the pending cursor transition.");
        Assert.IsTrue(precommitOwners.Concat(incoming).All(registrations.Contains),
            "Every current published owner and never-published incoming cursor handle must remain registered.");
        Assert.IsTrue(fixedRows.All(handle => !registrations.Contains(handle)),
            "The exact previously published fixed rows replaced by the pin swap were not retired.");
        CollectionAssert.AreEqual(
            PrivateArtworkOrder(service).Distinct(StringComparer.Ordinal).ToArray(),
            PrivateArtworkOrder(service),
            "Retired or reused registrations left duplicate entries in the bounded FIFO order.");
        Assert.AreEqual(registrations.Count, PrivateArtworkOrder(service).Length,
            "The bounded FIFO order diverged from live artwork registrations.");
        foreach (var handle in incoming)
            Assert.IsNotNull(await service.ResolveArtworkAsync(
                    new WidgetArtworkHandle(handle), CancellationToken.None),
                $"Precommit pin publication evicted pending cursor handle {handle}.");

        var retainedBrowseAfterMove = liveBrowse.Skip(128).Concat(incoming).ToArray();
        Assert.AreEqual(384, retainedBrowseAfterMove.Length);
        var requiredAfterSwap = retainedHome.Concat(retainedBrowseAfterMove)
            .Concat(incomingFixed).Concat(hidden).ToArray();
        Assert.AreEqual(960, requiredAfterSwap.Length);
        service.PinArtworkHandles(requiredAfterSwap);
        foreach (var handle in requiredAfterSwap)
            Assert.IsNotNull(await service.ResolveArtworkAsync(
                    new WidgetArtworkHandle(handle), CancellationToken.None),
                $"The retained rendered or newly admitted handle {handle} was stranded during swap.");
        Assert.IsFalse(diagnostics.Records.Any(record => record.Code == "unknown-handle"));
        Assert.AreEqual(960, PrivateDictionary(service, "_artwork").Count,
            "A maximum disjoint retained-owner set was silently truncated.");
        Assert.IsTrue(PrivateDictionary(service, "_artwork").Count <= 1_216,
            "Registration retention exceeded the bounded steady-plus-transition ceiling.");
        Assert.AreEqual(PrivateDictionary(service, "_artwork").Count,
            PrivateArtworkOrder(service).Length,
            "The bounded FIFO order diverged after the atomic owner swap.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkByteEvictionPreservesPublishedHandleAuthority()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(3);
        var artworkBytes = Convert.FromBase64String(FakeLibraryClient.TinyPng).Length;
        await using var service = Service(
            directory.Path, client, artworkCacheBytes: artworkBytes * 2L);
        var page = await service.QueryAsync(AllGames, null, null, 32,
            refresh: true, CancellationToken.None);
        var handles = page.Items.Select(item => item.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Tile)!.Handle)
            .ToArray();
        service.PinArtworkHandles(handles);

        Assert.IsNotNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(handles[0]), CancellationToken.None));
        Assert.IsNotNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(handles[1]), CancellationToken.None));
        Assert.IsNotNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(handles[0]), CancellationToken.None));
        Assert.IsNotNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(handles[2]), CancellationToken.None));

        Assert.AreEqual(artworkBytes * 2L, service.RetainedArtworkBytes);
        Assert.AreEqual(2, service.RetainedArtworkEntryCount);
        Assert.IsTrue(service.IsArtworkContentRetained(handles[0]));
        Assert.IsFalse(service.IsArtworkContentRetained(handles[1]),
            "The least recently used payload was not evicted.");
        Assert.IsTrue(service.IsArtworkContentRetained(handles[2]));
        Assert.IsTrue(PrivateDictionary(service, "_artwork").Contains(handles[1]),
            "Content eviction revoked a still-published handle registration.");

        var requestsBeforeReload = client.ArtworkRequests.Count;
        Assert.IsNotNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(handles[1]), CancellationToken.None));
        Assert.AreEqual(requestsBeforeReload + 1, client.ArtworkRequests.Count,
            "An evicted live handle did not reload through its retained authority.");
        Assert.IsTrue(service.RetainedArtworkBytes <= artworkBytes * 2L);
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetiredArtworkResolutionCannotResurrectOrphanCache(bool neutralFallback)
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(512);
        var incomingGames = client.Games.Skip(448).ToArray();
        client.Games.RemoveRange(448, incomingGames.Length);
        var diagnostics = new RecordingArtworkDiagnostics();
        await using var service = Service(directory.Path, client, diagnostics);
        var activePages = await QueryEveryPage(service,
            refresh: true, CancellationToken.None);
        var activeHandles = ArtworkHandles(activePages);
        service.PinArtworkHandles(activeHandles);
        client.Games.AddRange(incomingGames);
        var allPages = await QueryEveryPage(service,
            refresh: true, CancellationToken.None);
        var allHandles = ArtworkHandles(allPages);
        var target = activePages[0].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!.Handle;
        var targetGameId = activePages[0].AppId;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.ArtworkResolveHandler = async (gameId, kind, cancellationToken) =>
        {
            if (neutralFallback && kind == PlayniteBridgeArtworkKind.Background)
                return new(null, "not-found", "none");
            if (string.Equals(gameId, targetGameId, StringComparison.Ordinal))
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            if (neutralFallback)
                return new(null, "not-found", "none");
            var bytes = Convert.FromBase64String(FakeLibraryClient.TinyPng);
            return new(new WidgetEncodedArtwork(WidgetArtworkContentType.Png, bytes),
                "resolved", PlayniteBridgeClient.ArtworkSizeClass(bytes.Length));
        };

        var resolving = service.ResolveArtworkAsync(
            new WidgetArtworkHandle(target), CancellationToken.None).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.PinArtworkHandles(allHandles.Where(handle =>
                !string.Equals(handle, target, StringComparison.Ordinal))
            .Take(896).ToArray());
        Assert.IsFalse(PrivateDictionary(service, "_artwork").Contains(target),
            "The in-flight handle must be retired by the exact pin transition.");
        release.TrySetResult();
        Assert.IsNotNull(await resolving,
            "The current caller may consume bytes that completed after retirement.");
        Assert.IsFalse(PrivateDictionary(service, "_artwork").Contains(target));
        Assert.IsFalse(service.IsArtworkContentRetained(target),
            "A late ordinary or neutral result must not create orphan cache content.");
        Assert.IsNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(target), CancellationToken.None));
        Assert.AreEqual(1, diagnostics.Records.Count(record =>
            record.Code == "unknown-handle"));
    }

    [TestMethod, Timeout(30_000)]
    public async Task CancellationAndMalformedTraversalNeverPublishPartialAuthority()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(65) { MalformedSecondPage = true };
        await using var service = Service(directory.Path, client);
        var malformed = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(async () =>
            await service.QueryAsync(AllGames, null, null, 32,
                refresh: true, CancellationToken.None));
        Assert.AreEqual("invalid_payload", malformed.ErrorCode);

        client.MalformedSecondPage = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.QueryAsync(AllGames, null, null, 32,
                refresh: true, cancellation.Token));
    }

    private static PlayniteLibraryApplicationService Service(
        string root, FakeLibraryClient client,
        IPlayniteLibraryArtworkDiagnostics? diagnostics = null,
        long artworkCacheBytes = PlayniteArtworkContentCache.MaximumRetainedBytes)
    {
        Directory.CreateDirectory(root);
        return new(client, new PlayniteLibraryStateFileStore(
            Path.Combine(root, "organization.json")), diagnostics, artworkCacheBytes);
    }

    private static (byte Red, byte Green, byte Blue, byte Alpha) DecodeSingleRgbaPng(
        ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < signature.Length || !png[..signature.Length].SequenceEqual(signature))
            throw new InvalidDataException("Expected a PNG signature.");

        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        using var idat = new MemoryStream();
        for (var offset = signature.Length; offset < png.Length;)
        {
            if (png.Length - offset < 12)
                throw new InvalidDataException("PNG chunk header is truncated.");
            var length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
            if (length < 0 || png.Length - offset - 12 < length)
                throw new InvalidDataException("PNG chunk payload is truncated.");

            var type = Encoding.ASCII.GetString(png.Slice(offset + 4, 4));
            var data = png.Slice(offset + 8, length);
            if (type == "IHDR")
            {
                if (data.Length != 13)
                    throw new InvalidDataException("PNG IHDR length is invalid.");
                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                bitDepth = data[8];
                colorType = data[9];
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                png.Slice(offset + 8 + length, 4));
            var actualCrc = PngCrc32(png.Slice(offset + 4, length + 4));
            if (actualCrc != expectedCrc)
                throw new InvalidDataException($"PNG {type} CRC is invalid.");

            offset += length + 12;
        }

        if (width != 1 || height != 1 || bitDepth != 8 || colorType != 6)
            throw new InvalidDataException("Expected one 8-bit RGBA PNG pixel.");

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        zlib.CopyTo(decoded);
        var scanline = decoded.ToArray();
        if (scanline.Length != 5 || scanline[0] != 0)
            throw new InvalidDataException("Expected one unfiltered RGBA scanline.");
        return (scanline[1], scanline[2], scanline[3], scanline[4]);
    }

    private static uint PngCrc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        return crc ^ uint.MaxValue;
    }

    private static async Task<IReadOnlyList<WidgetAppLibraryItem>> QueryEveryPage(
        PlayniteLibraryApplicationService service,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var values = new List<WidgetAppLibraryItem>();
        WidgetCollectionCursor? cursor = null;
        var first = true;
        do
        {
            var page = await service.QueryAsync(AllGames, cursor,
                first ? null : WidgetCursorDirection.After, 64,
                refresh && first, cancellationToken);
            values.AddRange(page.Items);
            cursor = page.After is null ? null : new WidgetCollectionCursor(page.After);
            first = false;
        } while (cursor is not null);
        return values;
    }

    private static string[] ArtworkHandles(IEnumerable<WidgetAppLibraryItem> items) =>
        items.SelectMany(item => item.Presentation.Artwork.Items)
            .Select(artwork => artwork.Handle)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IDictionary PrivateDictionary(
        PlayniteLibraryApplicationService service,
        string name) => (IDictionary)(typeof(PlayniteLibraryApplicationService)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(service) ?? throw new AssertFailedException(name + " was null."));

    private static string[] PrivateArtworkOrder(
        PlayniteLibraryApplicationService service) =>
        ((IEnumerable)(typeof(PlayniteLibraryApplicationService)
            .GetField("_artworkOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service) ?? throw new AssertFailedException("_artworkOrder was null.")))
        .Cast<string>()
        .ToArray();

    private sealed class RecordingArtworkDiagnostics : IPlayniteLibraryArtworkDiagnostics
    {
        internal List<(string Stage, string Code, int Count, string SizeClass)> Records
            { get; } = [];
        public void Record(string stage, string code, int count, string sizeClass) =>
            Records.Add((stage, code, count, sizeClass));
    }

    private sealed class FakeLibraryClient : IPlayniteLibraryBridgeClient
    {
        internal const string TinyPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XcF7WQAAAABJRU5ErkJggg==";
        internal FakeLibraryClient(int count)
        {
            Games = Enumerable.Range(0, count).Select(index => new PlayniteBridgeGame(
                GuidFrom(index), $"Game {index:D5}", "Playnite", IsInstalled: true,
                Favorite: false, Hidden: false, CompletionStatus: null,
                Categories: [], Genres: ["Game"], Platforms: ["Windows"],
                PlaytimeSeconds: index * 60L, LastActivityUnixMilliseconds: index)).ToList();
        }

        internal List<PlayniteBridgeGame> Games { get; }
        internal List<PlayniteBridgeNamedItem> Categories { get; } = [];
        internal List<PlayniteBridgeGameQuery> Queries { get; } = [];
        internal int QueryCalls { get; private set; }
        internal bool FailQueries { get; set; }
        internal bool FailResolve { get; set; }
        internal bool FailCategoryLists { get; set; }
        internal string? CreatedCategoryResponseName { get; set; }
        internal IReadOnlyList<string>? CategoryMutationResponseCategories { get; set; }
        internal bool MalformedSecondPage { get; set; }
        internal string? Artwork { get; set; } = TinyPng;
        internal string? BackgroundArtwork { get; set; } = TinyPng;
        internal List<PlayniteBridgeArtworkKind> ArtworkKinds { get; } = [];
        internal List<(string GameId, PlayniteBridgeArtworkKind Kind)> ArtworkRequests
            { get; } = [];
        internal Dictionary<(string GameId, PlayniteBridgeArtworkKind Kind), string?>
            ArtworkResponses { get; } = [];
        internal Func<string, PlayniteBridgeArtworkKind, CancellationToken,
            ValueTask<PlayniteBridgeArtworkResult>>? ArtworkResolveHandler { get; set; }
        internal string? LastResolvedId { get; private set; }
        internal string? LastMutatedId { get; private set; }
        internal string? LastLaunchedId { get; private set; }
        internal int ResolveCalls { get; private set; }
        internal int SetCategoryCalls { get; private set; }

        public ValueTask<PlayniteBridgeGamePage> QueryGamesAsync(
            PlayniteBridgeGameQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCalls++;
            Queries.Add(query);
            if (FailQueries) throw new PlayniteBridgeTransportException(false);
            var offset = MalformedSecondPage && query.Offset == 64 ? 63 : query.Offset;
            return ValueTask.FromResult(new PlayniteBridgeGamePage(
                Games.Count, offset, query.Limit,
                Games.Skip(query.Offset).Take(query.Limit).ToArray()));
        }

        public ValueTask<PlayniteBridgeGame?> ResolveGameAsync(
            string gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            LastResolvedId = gameId;
            if (FailResolve) throw new PlayniteBridgeTransportException(false);
            return ValueTask.FromResult(Games.SingleOrDefault(game => game.Id == gameId));
        }

        public ValueTask<PlayniteBridgeArtworkResult> ResolveArtworkAsync(
            string gameId, PlayniteBridgeArtworkKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsTrue(Games.Any(game => game.Id == gameId));
            ArtworkKinds.Add(kind);
            ArtworkRequests.Add((gameId, kind));
            if (ArtworkResolveHandler is not null)
                return ArtworkResolveHandler(gameId, kind, cancellationToken);
            var encoded = ArtworkResponses.TryGetValue((gameId, kind), out var exact)
                ? exact
                : kind == PlayniteBridgeArtworkKind.Background
                    ? BackgroundArtwork
                    : Artwork;
            if (encoded is null)
                return ValueTask.FromResult(new PlayniteBridgeArtworkResult(
                    null, "not-found", "none"));
            var bytes = Convert.FromBase64String(encoded);
            return ValueTask.FromResult(new PlayniteBridgeArtworkResult(
                new WidgetEncodedArtwork(WidgetArtworkContentType.Png, bytes),
                "resolved",
                PlayniteBridgeClient.ArtworkSizeClass(bytes.Length)));
        }

        public ValueTask<bool> LaunchAsync(string gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLaunchedId = gameId;
            return ValueTask.FromResult(Games.Any(game => game.Id == gameId));
        }

        public ValueTask<PlayniteBridgeGame?> SetFavoriteAsync(
            string gameId, bool favorite, CancellationToken cancellationToken) =>
            Mutate(gameId, game => game with { Favorite = favorite }, cancellationToken);

        public ValueTask<PlayniteBridgeGame?> SetHiddenAsync(
            string gameId, bool hidden, CancellationToken cancellationToken) =>
            Mutate(gameId, game => game with { Hidden = hidden }, cancellationToken);

        public ValueTask<PlayniteBridgeGame?> SetCategoriesAsync(
            string gameId, IReadOnlyList<string> categories,
            CancellationToken cancellationToken)
        {
            SetCategoryCalls++;
            return Mutate(gameId, game => game with
            {
                Categories = CategoryMutationResponseCategories ?? categories,
            }, cancellationToken);
        }

        public ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(
            string gameId, string completionStatus, CancellationToken cancellationToken) =>
            Mutate(gameId, game => game with { CompletionStatus = completionStatus },
                cancellationToken);

        public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailCategoryLists) throw new PlayniteBridgeTransportException(false);
            return ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>(
                Categories.ToArray());
        }

        public ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(
            string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Categories.Any(category => string.Equals(
                    category.Name, name, StringComparison.OrdinalIgnoreCase)))
                return ValueTask.FromResult<PlayniteBridgeNamedItem?>(null);
            var created = new PlayniteBridgeNamedItem(
                GuidFrom(30_100 + Categories.Count), name);
            Categories.Add(created);
            return ValueTask.FromResult<PlayniteBridgeNamedItem?>(created with
            {
                Name = CreatedCategoryResponseName ?? created.Name,
            });
        }

        public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCompletionStatusesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>(
                [new(GuidFrom(20_002), "Completed")]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private ValueTask<PlayniteBridgeGame?> Mutate(
            string gameId, Func<PlayniteBridgeGame, PlayniteBridgeGame> mutation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastMutatedId = gameId;
            var index = Games.FindIndex(game => game.Id == gameId);
            if (index < 0) return ValueTask.FromResult<PlayniteBridgeGame?>(null);
            Games[index] = mutation(Games[index]);
            return ValueTask.FromResult<PlayniteBridgeGame?>(Games[index]);
        }

        internal static string GuidFrom(int value) =>
            $"00000000-0000-0000-0000-{value:D12}";
    }

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wrail-playnite-library-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
