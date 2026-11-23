using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    private readonly PublicMediaClient storeClient = new(new()
    {
        IdentifyProduct = true, MaximumConcurrentRequests = 1, MaximumQueuedRequests = 1, OverallTimeout = TimeSpan.FromSeconds(8),
    });

    public async ValueTask<SteamStoreMedia> GetStoreMediaAsync(PlayniteStoreLookup lookup, CancellationToken token)
    {
        using var scope = ConnectionScope(token);
        var media = SteamStoreMedia.Empty;
        if (lookup.AppId is uint appId)
        {
            var url = new Uri(FormattableString.Invariant(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic,movies,screenshots"));
            try
            {
                var bytes = await storeClient.DownloadAsync(url, PublicMediaPolicy.SameOrigin(url),
                    512 * 1024, scope.Token).ConfigureAwait(false);
                media = SteamStoreMedia.Parse(appId, bytes.Bytes);
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
            { scope.Token.ThrowIfCancellationRequested(); }
        }
        scope.Token.ThrowIfCancellationRequested();
        if (lookup.LinkedTrailer is { } linked) return media with { PublisherTrailerId = linked };
        if (media.HasNativeTrailer ||
            PlaynitePublisherTrailers.Website(media.PublisherWebsite ?? lookup.Website) is not { } publisher ||
            !PublicMediaPolicy.PublicHttps.Allows(publisher)) return media;
        try
        {
            var site = await storeClient.DownloadAsync(publisher, PublicMediaPolicy.SameOrigin(publisher), 512 * 1024, scope.Token)
                .ConfigureAwait(false);
            if (site.MediaType is not ("text/html" or "application/xhtml+xml")) return media;
            scope.Token.ThrowIfCancellationRequested();
            return media with { PublisherTrailerId = PlaynitePublisherTrailers.Find(media.StoreName ?? lookup.Name, site.Bytes) };
        }
        // A third-party page can exceed the parser's nesting limit or fail acquisition.
        // Its optional video must never suppress this game's verified screenshots.
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        { scope.Token.ThrowIfCancellationRequested(); return media; }
    }
    public ValueTask<PlayniteProviderRefreshResult> RefreshAchievementsAsync(string gameId, CancellationToken token) =>
        _client.RefreshAchievementsAsync(gameId, token);
    private sealed record EventPosition(string Instance, string Connection, long Sequence);
    private EventPosition? lastEventPosition;

    public async ValueTask<PlayniteEventBatch?> ReadEventsAsync(long after, CancellationToken token)
    {
        var batch = await _client.ReadEventsAsync(after, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (batch is null) return null;
        var position = new EventPosition(batch.InstanceId, batch.ConnectionId, batch.NextSequence);
        var previous = Interlocked.Exchange(ref lastEventPosition, position);
        if ((batch.ResetRequired && previous != position) ||
            batch.Changes.Any(change => change.Kind is "library-changed" or "game-changed" or "game-state"))
        {
            InvalidateCatalog();
            if (_client is PlayniteBridgeClient bridge) bridge.ResetArtworkPathCache();
        }
        return batch;
    }
    public async ValueTask<PlayniteGameDetails?> GetGameDetailsAsync(string gameId, CancellationToken token)
    {
        var game = await _client.ResolveGameAsync(gameId, token).ConfigureAwait(false);
        return game is null ? null : new(Project(game, stale: false), game);
    }
    public ValueTask<PlayniteAchievementOverview> GetAchievementOverviewAsync(CancellationToken token) => _client.GetAchievementOverviewAsync(token);
    public ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token) =>
        _client.GetAchievementsAsync(gameId, token);
    public ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token) =>
        _client.GetActivityAsync(gameId, token);
    public async ValueTask<PlayniteRecentActivity> GetRecentActivityAsync(CancellationToken token)
    {
        using var connection = ConnectionScope(token);
        token = connection.Token;
        Dictionary<string, string> heroes;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var (catalog, stale) = await LoadCatalogLockedAsync(false, token).ConfigureAwait(false);
            if (stale) return PlayniteRecentActivity.Empty;
            // Register only references while the catalog is held. The host requests artwork for realized posts.
            heroes = ApplyQuery(catalog.Games, new(false), new(PlayniteLibraryQueryScope.RecentlyPlayed)).Take(12)
                .ToDictionary(game => game.Id, game => RegisterArtwork(game.Id, ArtworkRevision(game), PlayniteBridgeArtworkKind.Background));
        }
        finally { _gate.Release(); }
        var activity = await _client.GetRecentActivityAsync(heroes.Keys.ToArray(), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return activity with { HeroHandles = heroes };
    }
    public async ValueTask<bool> ChangeInstallationAsync(string gameId, bool install, CancellationToken token)
    {
        using var connection = ConnectionScope(token);
        token = connection.Token;
        var game = await _client.ResolveGameAsync(gameId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (game is null) return false;
        if (game.IsInstalled == install) return true;
        return await _client.ChangeInstallationAsync(game.Id, install, token).ConfigureAwait(false);
    }
}
