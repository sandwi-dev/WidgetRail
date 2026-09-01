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
    private static readonly WidgetAppLibraryQuery AllGames = new(
        InstalledOnly: false,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

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
        var client = new FakeLibraryClient(576);
        var incomingGames = client.Games.Skip(448).ToArray();
        foreach (var index in Enumerable.Range(512, 64))
            client.Games[index] = client.Games[index] with { Hidden = true };
        client.Games.RemoveRange(448, incomingGames.Length);
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
        var allPages = await QueryEveryPage(service,
            refresh: true, CancellationToken.None);
        var incoming = ArtworkHandles(allPages.Skip(448).Take(64));
        Assert.AreEqual(128, incoming.Length,
            "One complete incoming page must fit beside every active owner before the pin swap.");
        var resolvedFixedRows = await service.ResolveSavedAsync(
            incomingGames.Skip(64).Select(game => game.Id).ToArray(),
            CancellationToken.None);
        var incomingFixed = ArtworkHandles(resolvedFixedRows);
        Assert.AreEqual(128, incomingFixed.Length,
            "One complete incoming fixed/title set must fit beside the cursor page.");
        Assert.AreEqual(1_152, PrivateDictionary(service, "_artwork").Count,
            "Registration must reserve a full two-role cursor page and fixed-row set for atomic handoff.");
        Assert.IsTrue(incoming.Concat(incomingFixed).All(handle =>
                PrivateDictionary(service, "_artwork").Contains(handle)),
            "No newly admitted page or fixed-row handle may be evicted before the pin swap.");

        var precommitOwners = retainedHome.Concat(incomingFixed).Concat(liveBrowse).ToArray();
        Assert.AreEqual(896, precommitOwners.Length);
        service.PinArtworkHandles(precommitOwners);
        Assert.AreEqual(1_152, PrivateDictionary(service, "_artwork").Count,
            "The synchronous fixed-row invalidation must preserve the pending cursor transition window.");
        foreach (var handle in incoming)
            Assert.IsNotNull(await service.ResolveArtworkAsync(
                    new WidgetArtworkHandle(handle), CancellationToken.None),
                $"Precommit pin publication evicted pending cursor handle {handle}.");

        var requiredAfterSwap = retainedHome.Concat(incoming).Concat(incomingFixed).ToArray();
        service.PinArtworkHandles(requiredAfterSwap);
        foreach (var handle in requiredAfterSwap)
            Assert.IsNotNull(await service.ResolveArtworkAsync(
                    new WidgetArtworkHandle(handle), CancellationToken.None),
                $"The retained rendered or newly admitted handle {handle} was stranded during swap.");
        Assert.IsFalse(diagnostics.Records.Any(record => record.Code == "unknown-handle"));
        Assert.AreEqual(1_152, PrivateDictionary(service, "_artwork").Count,
            "Registration retention must remain bounded after the atomic owner swap.");
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
        Assert.IsFalse(PrivateDictionary(service, "_artworkContent").Contains(target),
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
        IPlayniteLibraryArtworkDiagnostics? diagnostics = null)
    {
        Directory.CreateDirectory(root);
        return new(client, new PlayniteLibraryStateFileStore(
            Path.Combine(root, "organization.json")), diagnostics);
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

            offset += length + 12;
        }

        if (width != 1 || height != 1 || bitDepth != 8 || colorType != 6)
            throw new InvalidDataException("Expected one 8-bit RGBA PNG pixel.");

        var compressed = idat.ToArray();
        if (compressed.Length <= 6)
            throw new InvalidDataException("PNG zlib payload is truncated.");
        using var deflatePayload = new MemoryStream(
            compressed, 2, compressed.Length - 6, writable: false);
        using var deflate = new DeflateStream(deflatePayload, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        deflate.CopyTo(decoded);
        var scanline = decoded.ToArray();
        if (scanline.Length != 5 || scanline[0] is not (0 or 1))
            throw new InvalidDataException("Expected one unfiltered or Sub-filtered RGBA scanline.");
        return (scanline[1], scanline[2], scanline[3], scanline[4]);
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
        internal List<PlayniteBridgeGameQuery> Queries { get; } = [];
        internal int QueryCalls { get; private set; }
        internal bool FailQueries { get; set; }
        internal bool FailResolve { get; set; }
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
            CancellationToken cancellationToken) => Mutate(
            gameId, game => game with { Categories = categories }, cancellationToken);

        public ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(
            string gameId, string completionStatus, CancellationToken cancellationToken) =>
            Mutate(gameId, game => game with { CompletionStatus = completionStatus },
                cancellationToken);

        public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>([]);

        public ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(
            string name, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PlayniteBridgeNamedItem?>(new(GuidFrom(20_001), name));

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

        private static string GuidFrom(int value) =>
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
