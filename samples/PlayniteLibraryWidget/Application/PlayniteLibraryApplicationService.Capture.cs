using System.Collections.ObjectModel;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    public async ValueTask<PlayniteLibraryCapturedQuery> CaptureQueryAsync(WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context, bool refresh, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(context.Scope)) throw new ArgumentOutOfRangeException(nameof(context));
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = lifetime.Token;
        // Copy mutable list members before awaiting provider work.
        query = query with { FavoriteSavedIds = Array.AsReadOnly(query.FavoriteSavedIds.ToArray()) };
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var (catalog, stale) = await LoadCatalogLockedAsync(refresh, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var games = Array.AsReadOnly(ApplyQuery(catalog.Games, query, context).Select(FreezeGame).ToArray());
            var authority = ProjectAuthority(catalog.Games, catalog.Categories);
            authority = authority with
            {
                FavoriteGameIds = Array.AsReadOnly(authority.FavoriteGameIds.ToArray()),
                HiddenGameIds = Array.AsReadOnly(authority.HiddenGameIds.ToArray()),
                Categories = Array.AsReadOnly(authority.Categories.Select(category => category with
                    { SavedIds = Array.AsReadOnly(category.SavedIds.ToArray()) }).ToArray()),
                CompletionStatuses = new ReadOnlyDictionary<string, string?>(
                    authority.CompletionStatuses.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            };
            var retrievedAt = catalog.RetrievedAt;
            return new(query, context, games, authority, Array.AsReadOnly(ProjectSources(catalog, stale)),
                catalog.Revision, retrievedAt, stale,
                game =>
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return PlayniteLibraryItem.From(Project(game, stale, registerArtwork: false, retrievedAt));
                }, ResolveCapturedArtworkAsync);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (ObjectDisposedException) { throw; }
        catch (Exception error) { throw Safe(error); }
        finally { _gate.Release(); }
    }

    // Caller owns _gate. Publication is atomic; a failed refresh cannot expose
    // a partially fetched catalog to either cursor or captured query consumers.
    private async ValueTask<(Catalog Catalog, bool Stale)> LoadCatalogLockedAsync(bool refresh, CancellationToken token)
    {
        try
        {
            var mutationRevision = Interlocked.Read(ref _catalogMutationRevision);
            if (refresh || _lastGood is null || Interlocked.Read(ref _lastGoodMutationRevision) != mutationRevision)
            {
                var catalog = await FetchCatalogAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _lastGood = catalog;
                Interlocked.Exchange(ref _lastGoodMutationRevision, mutationRevision);
            }
            return (_lastGood, false);
        }
        catch (Exception error) when (CanRetain(error, token) && _lastGood is not null)
        { return (_lastGood, true); }
    }

    private static WidgetAppLibrarySource[] ProjectSources(Catalog catalog, bool stale) => catalog.Games
        .Select(game => game.Source).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .Take(PlayniteLibraryPrivateState.MaximumProvenSources)
        .Select(value => new WidgetAppLibrarySource(SourceId(value), value,
            stale ? WidgetAppLibrarySourceHealth.Degraded : WidgetAppLibrarySourceHealth.Healthy,
            catalog.Sequence, stale ? "stale_last_good" : "connected")
        {
            AccountState = WidgetAppLibrarySourceAccountState.NotApplicable,
            LastSuccessfulRefreshAtUnixMilliseconds = catalog.RetrievedAt,
        }).ToArray();

    private static PlayniteBridgeGame FreezeGame(PlayniteBridgeGame game) => game with
    {
        Categories = Array.AsReadOnly(game.Categories.ToArray()),
        Genres = Array.AsReadOnly(game.Genres.ToArray()), Platforms = Array.AsReadOnly(game.Platforms.ToArray()),
        Developers = Array.AsReadOnly(game.Developers.ToArray()), Publishers = Array.AsReadOnly(game.Publishers.ToArray()),
        Features = Array.AsReadOnly(game.Features.ToArray()), Tags = Array.AsReadOnly(game.Tags.ToArray()),
        Series = Array.AsReadOnly(game.Series.ToArray()), AgeRatings = Array.AsReadOnly(game.AgeRatings.ToArray()),
        Links = Array.AsReadOnly(game.Links.ToArray()),
    };

    private static string ArtworkHandle(string gameId, string revision, PlayniteBridgeArtworkKind kind) =>
        ContentId("artwork-handle", gameId, revision, kind.ToString());

    private ValueTask<WidgetEncodedArtwork?> ResolveCapturedArtworkAsync(PlayniteBridgeGame game,
        WidgetArtworkHandle handle, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        var revision = ContentId("artwork-revision", game.Id, GameRevision(game));
        foreach (var kind in new[] { PlayniteBridgeArtworkKind.Cover, PlayniteBridgeArtworkKind.Background })
            if (string.Equals(handle.Value, ArtworkHandle(game.Id, revision, kind), StringComparison.Ordinal))
                return ResolveArtworkCoreAsync(handle, new(game.Id, revision, kind), captured: true, token);
        return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
    }
}
