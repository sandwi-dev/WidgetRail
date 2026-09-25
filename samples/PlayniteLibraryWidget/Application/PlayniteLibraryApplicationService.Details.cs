using System.Diagnostics;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    public async ValueTask<PlayniteGameDetails?> GetGameDetailsAsync(string gameId, CancellationToken token)
    {
        var game = await _client.ResolveGameAsync(gameId, token).ConfigureAwait(false);
        return game is null ? null : new(Project(game, stale: false), game);
    }
    public ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token) =>
        _client.GetAchievementsAsync(gameId, token);
    public ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token) =>
        _client.GetActivityAsync(gameId, token);
    public async ValueTask<bool> ChangeInstallationAsync(string gameId, bool install, CancellationToken token)
    {
        var game = await _client.ResolveGameAsync(gameId, token).ConfigureAwait(false);
        if (game is null) return false;
        if (game.IsInstalled == install) return true;
        return await _client.ChangeInstallationAsync(game.Id, install, token).ConfigureAwait(false);
    }
    public async ValueTask<bool> OpenGameLinkAsync(string gameId, string url, CancellationToken token)
    {
        var game = await _client.ResolveGameAsync(gameId, token).ConfigureAwait(false);
        if (game is null || !game.Links.Any(link => link.Url == url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        token.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }
}
