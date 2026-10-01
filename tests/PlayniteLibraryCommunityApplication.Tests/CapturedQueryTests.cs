using System.Collections;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace PlayniteLibraryCommunityApplication.Tests;

public sealed partial class PackageRuntimeTests
{
    [TestMethod, Timeout(30_000)]
    public async Task CapturedQuerySupportsRandomBoundedRangesWithoutRegisteringCatalogArtwork()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(10_000);
        await using var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        Assert.AreEqual(10_000, query.Count);
        Assert.AreEqual(0, PrivateDictionary(service, "_artwork").Count);
        foreach (var (start, count) in new[] { (9_990, 10), (17, 64), (4_000, 3), (0, 1), (47, 8) })
        {
            var rows = query.ReadRange(start, count);
            CollectionAssert.AreEqual(client.Games.Skip(start).Take(count).Select(game => game.Id).ToArray(),
                rows.Select(row => row.Item.Value.SavedId).ToArray());
            Assert.AreEqual(start, rows[0].Index);
        }
        Assert.AreEqual(0, PrivateDictionary(service, "_artwork").Count, "Demand projection must not enter the old cursor pin registry.");
        Assert.AreEqual(0, client.ArtworkRequests.Count);
        var filtered = await service.CaptureQueryAsync(AllGames with
            { SearchText = "Game 000", Sort = WidgetAppLibrarySortOrder.DisplayNameDescending },
            new(PlayniteLibraryQueryScope.Library), false, CancellationToken.None);
        Assert.AreEqual(100, filtered.Count);
        Assert.AreEqual(client.Games[99].Id, filtered.ReadRange(0, 1)[0].Item.Value.SavedId);
        Assert.AreEqual(10_000, query.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(0, 65));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(9_999, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => query.ReadRange(int.MaxValue, 1));
        Assert.ThrowsExactly<OperationCanceledException>(() => query.ReadRange(0, 1, new(true)));
    }

    [TestMethod, Timeout(30_000)]
    public async Task CapturedContentAndAuthorityStayFrozenAcrossProviderMutationAndRefresh()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(3);
        var genres = new List<string> { "Original genre" };
        client.Games[0] = client.Games[0] with { Genres = genres };
        await using var service = Service(directory.Path, client);
        var favorites = new List<string>();
        var old = await service.CaptureQueryAsync(AllGames with { FavoriteSavedIds = favorites },
            new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var before = old.ReadRange(0, 1)[0].Item.Value;
        favorites.Add(client.Games[2].Id);
        genres[0] = "Changed genre";
        client.Games[0] = client.Games[0] with { Name = "Renamed", Description = "new content" };
        await service.SetFavoriteAsync(client.Games[0].Id, true, CancellationToken.None);
        var next = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var again = old.ReadRange(0, 1)[0].Item.Value;
        Assert.AreEqual(before.Presentation.DisplayName, again.Presentation.DisplayName);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(again));
        Assert.AreEqual("Original genre", old.Games[0].Genres[0]);
        Assert.AreEqual(0, old.Query.FavoriteSavedIds.Count);
        Assert.AreEqual(0, old.Authority.FavoriteGameIds.Count);
        Assert.AreEqual(1, next.Authority.FavoriteGameIds.Count);
        Assert.AreNotEqual(old.CatalogRevision, next.CatalogRevision);
        Assert.AreEqual("Renamed", next.Games.Single(game => game.Id == old.Games[0].Id).Name);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList)old.Games)[0] = client.Games[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList)old.Games[0].Genres)[0] = "mutated");
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary)old.Authority.CompletionStatuses)[old.Games[0].Id] = "changed");
    }

    [TestMethod, Timeout(30_000)]
    public async Task CapturedArtworkSurvivesCursorRegistryEvictionAndRejectsOtherItemAuthority()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(2_000);
        await using var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var rows = query.ReadRange(0, 2);
        var handle = new WidgetArtworkHandle(rows[0].Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle);
        await QueryEveryPage(service, true, CancellationToken.None);
        Assert.IsFalse(PrivateDictionary(service, "_artwork").Contains(handle.Value));
        Assert.IsNull(await service.ResolveArtworkAsync(handle, CancellationToken.None));
        Assert.IsNull(await query.ResolveArtworkAsync(rows[1], handle));
        Assert.AreEqual(0, client.ArtworkRequests.Count);
        Assert.IsNotNull(await query.ResolveArtworkAsync(rows[0], handle));
        Assert.AreEqual((client.Games[0].Id, PlayniteBridgeArtworkKind.Cover), client.ArtworkRequests.Single());
        Assert.IsTrue(service.IsArtworkContentRetained(handle.Value));
        var other = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), false, CancellationToken.None);
        Assert.IsNull(await other.ResolveArtworkAsync(rows[0], handle));
        Assert.IsNull(await query.ResolveArtworkAsync(rows[0], new WidgetArtworkHandle("unknown")));
        Assert.AreEqual(1, client.ArtworkRequests.Count);
        client.Games[0] = client.Games[0] with { Name = "Changed revision" };
        var revised = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var changed = revised.ReadRange(0, 1)[0];
        var changedHandle = new WidgetArtworkHandle(changed.Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle);
        Assert.AreEqual(rows[0].Item.Value.SavedId, changed.Item.Value.SavedId);
        Assert.AreNotEqual(handle, changedHandle);
        Assert.IsNull(await query.ResolveArtworkAsync(rows[0], changedHandle), "Same game with another revision is not this row's artwork authority.");
        Assert.IsNotNull(await query.ResolveArtworkAsync(rows[0], handle));
        Assert.AreEqual(1, client.ArtworkRequests.Count, "The original captured artwork revision still reuses its bytes.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task CapturedArtworkDoesNotBlockCatalogAndProviderConcurrencyIsBounded()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(8);
        await using var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var rows = query.ReadRange(0, 8);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = 0;
        client.ArtworkResolveHandler = async (_, _, token) =>
        {
            if (Interlocked.Increment(ref admitted) == 4) fourEntered.SetResult();
            await release.Task.WaitAsync(token);
            return ArtworkResult();
        };
        Task<WidgetEncodedArtwork?> Read(int i, CancellationToken token = default) => query.ResolveArtworkAsync(rows[i],
            new(rows[i].Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle), token).AsTask();
        var active = Enumerable.Range(0, 4).Select(i => Read(i)).ToArray();
        try
        {
            await fourEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancellation = new CancellationTokenSource();
            var waiting = Read(4, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
            Assert.AreEqual(4, Volatile.Read(ref admitted));
            client.Games[0] = client.Games[0] with { Name = "refreshed during artwork" };
            var next = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreNotEqual(query.CatalogRevision, next.CatalogRevision);
            Assert.IsFalse(active.Any(task => task.IsCompleted));
        }
        finally { release.TrySetResult(); await Task.WhenAll(active); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task CapturedArtworkKeepsFallbackAndDoesNotCacheCanceledProviderResults()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(2) { BackgroundArtwork = null };
        await using var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var rows = query.ReadRange(0, 2);
        var background = new WidgetArtworkHandle(rows[0].Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero)!.Handle);
        Assert.IsNotNull(await query.ResolveArtworkAsync(rows[0], background));
        CollectionAssert.AreEqual(new[] { PlayniteBridgeArtworkKind.Background, PlayniteBridgeArtworkKind.Cover }, client.ArtworkKinds);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ArtworkResolveHandler = async (_, _, _) => { entered.SetResult(); await release.Task; return ArtworkResult(); };
        using var cancellation = new CancellationTokenSource();
        var cover = new WidgetArtworkHandle(rows[1].Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle);
        var read = query.ResolveArtworkAsync(rows[1], cover, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel(); release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await read);
        Assert.IsFalse(service.IsArtworkContentRetained(cover.Value));
    }

    [TestMethod, Timeout(30_000)]
    public async Task CapturedRefreshRetainsLastGoodAndDoesNotReplaceCursorTraversal()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(7);
        await using var service = Service(directory.Path, client);
        var first = await service.QueryAsync(AllGames, null, null, 2, true, CancellationToken.None);
        var captured = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), false, CancellationToken.None);
        var cursorNext = await service.QueryAsync(AllGames, new(first.After!), WidgetCursorDirection.After, 2, false, CancellationToken.None);
        Assert.AreEqual(captured.Games[2].Id, cursorNext.Items[0].SavedId);
        client.FailQueries = true;
        var stale = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        Assert.IsTrue(stale.RetainedLastGood);
        Assert.AreEqual(captured.CatalogRevision, stale.CatalogRevision);
        Assert.AreEqual(WidgetAppLibrarySourceHealth.Degraded, stale.Sources.Single().Health);
        Assert.IsFalse(stale.ReadRange(0, 1)[0].Item.Presentation.Availability.IsLaunchable);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, new(true)));
    }

    private static PlayniteBridgeArtworkResult ArtworkResult() => new(
        new WidgetEncodedArtwork(WidgetArtworkContentType.Png, Convert.FromBase64String(FakeLibraryClient.TinyPng)), "resolved", "tiny");

    [TestMethod, Timeout(30_000)]
    public async Task ServiceRetirementCancelsCapturedArtworkAndRetiresItsProjection()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(1);
        await using var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var row = query.ReadRange(0, 1)[0];
        var handle = new WidgetArtworkHandle(row.Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ArtworkResolveHandler = async (_, _, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return ArtworkResult();
        };
        var read = query.ResolveArtworkAsync(row, handle).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await read);
        Assert.AreEqual(0, service.RetainedArtworkBytes);
        Assert.ThrowsExactly<ObjectDisposedException>(() => query.ReadRange(0, 1));
        Assert.ThrowsExactly<ObjectDisposedException>(() => query.ResolveArtworkAsync(row, handle));
    }

    [TestMethod, Timeout(30_000)]
    public async Task ClientDisposalFailureStillDrainsArtworkAndClearsCapturedCache()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(2);
        var service = Service(directory.Path, client);
        var query = await service.CaptureQueryAsync(AllGames, new(PlayniteLibraryQueryScope.Library), true, CancellationToken.None);
        var rows = query.ReadRange(0, 2);
        WidgetArtworkHandle Handle(int i) => new(rows[i].Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Cover)!.Handle);
        Assert.IsNotNull(await query.ResolveArtworkAsync(rows[0], Handle(0)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ArtworkResolveHandler = async (_, _, _) => { entered.SetResult(); await release.Task; return ArtworkResult(); };
        client.DisposeHandler = () => ValueTask.FromException(new IOException("dispose failure"));
        var read = query.ResolveArtworkAsync(rows[1], Handle(1)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var disposing = service.DisposeAsync().AsTask();
        try { Assert.IsFalse(disposing.IsCompleted, "Even failed client disposal must wait for the actual provider call."); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await read);
        await Assert.ThrowsExactlyAsync<IOException>(async () => await disposing.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(0, service.RetainedArtworkBytes);
        Assert.AreEqual(0, PrivateDictionary(service, "_artwork").Count);
        Assert.AreSame(disposing, service.DisposeAsync().AsTask());
        Assert.ThrowsExactly<ObjectDisposedException>(() => query.ReadRange(0, 1));
    }

    [TestMethod, Timeout(30_000)]
    public async Task LateLegacyResolutionCannotRegisterArtworkAfterServiceDisposal()
    {
        using var directory = new TestDirectory();
        var client = new FakeLibraryClient(1);
        await using var service = Service(directory.Path, client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GameResolveHandler = async (_, _) => { entered.SetResult(); await release.Task; return client.Games[0]; };
        var resolve = service.ResolveSavedAsync([client.Games[0].Id], CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.DisposeAsync();
        release.SetResult();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await resolve);
        Assert.AreEqual(0, PrivateDictionary(service, "_artwork").Count);
        Assert.AreEqual(0, service.RetainedArtworkBytes);
    }
}
