namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    public ValueTask<PlayniteProviderRefreshResult> RefreshAchievementsAsync(string gameId, CancellationToken token) =>
        _client.RefreshAchievementsAsync(gameId, token);
    public async ValueTask<PlayniteEventBatch?> ReadEventsAsync(long after, CancellationToken token)
    {
        var batch = await _client.ReadEventsAsync(after, token).ConfigureAwait(false);
        if (batch is not null && (batch.ResetRequired || batch.Changes.Any(change => change.Kind is "library-changed" or "game-changed")))
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
