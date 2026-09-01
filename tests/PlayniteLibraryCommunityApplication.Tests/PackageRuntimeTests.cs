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
        CollectionAssert.AreEqual(new[]
        {
            PlayniteBridgeArtworkKind.Cover,
            PlayniteBridgeArtworkKind.Background,
        }, client.ArtworkKinds.Take(2).ToArray());

        client.BackgroundArtwork = null;
        var fallbackHero = current.Items[1].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!;
        var fallbackBackground = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(fallbackHero.Handle), CancellationToken.None);
        Assert.IsNotNull(fallbackBackground,
            "Only a same-game not-found background may fall back to its cover.");
        CollectionAssert.AreEqual(Convert.FromBase64String(FakeLibraryClient.TinyPng),
            fallbackBackground.Bytes.ToArray());
        CollectionAssert.AreEqual(new[]
        {
            PlayniteBridgeArtworkKind.Background,
            PlayniteBridgeArtworkKind.Cover,
        }, client.ArtworkKinds.Skip(2).Take(2).ToArray());
        client.Artwork = null;
        var otherArtwork = current.Items[1].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Tile)!;
        Assert.IsNull(await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(otherArtwork.Handle), CancellationToken.None));

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
    public async Task ArtworkRegistryRetainsBothRolesAcrossThreeMaximumPages()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(192);
        await using var service = Service(directory.Path, client);

        var first = await service.QueryAsync(AllGames, null, null, 64,
            refresh: true, CancellationToken.None);
        var earliest = first.Items[0];
        var cover = earliest.Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Tile)!;
        var background = earliest.Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Hero)!;
        var second = await service.QueryAsync(AllGames,
            new WidgetCollectionCursor(first.After!), WidgetCursorDirection.After,
            64, refresh: false, CancellationToken.None);
        var third = await service.QueryAsync(AllGames,
            new WidgetCollectionCursor(second.After!), WidgetCursorDirection.After,
            64, refresh: false, CancellationToken.None);

        Assert.AreEqual(64, first.Items.Count);
        Assert.AreEqual(64, second.Items.Count);
        Assert.AreEqual(64, third.Items.Count);
        Assert.IsNull(third.After);
        var coverBytes = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(cover.Handle), CancellationToken.None);
        var backgroundBytes = await service.ResolveArtworkAsync(
            new WidgetArtworkHandle(background.Handle), CancellationToken.None);
        Assert.IsNotNull(coverBytes,
            "The earliest retained page's cover handle must survive two later pages.");
        Assert.IsNotNull(backgroundBytes,
            "The earliest retained page's distinct background handle must survive two later pages.");
        Assert.AreNotEqual(cover.Handle, background.Handle);
        CollectionAssert.AreEqual(Convert.FromBase64String(FakeLibraryClient.TinyPng),
            coverBytes.Bytes.ToArray());
        CollectionAssert.AreEqual(Convert.FromBase64String(FakeLibraryClient.TinyPng),
            backgroundBytes.Bytes.ToArray());
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
        string root, FakeLibraryClient client)
    {
        Directory.CreateDirectory(root);
        return new(client, new PlayniteLibraryStateFileStore(
            Path.Combine(root, "organization.json")));
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
            var encoded = kind == PlayniteBridgeArtworkKind.Background
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
