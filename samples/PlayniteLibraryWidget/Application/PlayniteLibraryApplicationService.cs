using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryApplicationService(
    IPlayniteLibraryBridgeClient client,
    PlayniteLibraryStateFileStore state,
    IPlayniteLibraryArtworkDiagnostics? artworkDiagnostics = null)
    : IPlayniteLibraryApplicationService
{
    private const int RegisteredArtworkRolesPerGame = 2;
    private const int MaximumArtworkEntries =
        (PlayniteLibraryWidget.MaximumRetainedItems + WidgetAppLibraryService.MaximumSavedItems) *
        RegisteredArtworkRolesPerGame;
    private const int MaximumCategoryMemberships = PlayniteBridgeClient.MaximumGames;
    private readonly IPlayniteLibraryBridgeClient _client = client ??
        throw new ArgumentNullException(nameof(client));
    private readonly PlayniteLibraryStateFileStore _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ArtworkRegistration> _artwork =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WidgetEncodedArtwork> _artworkContent =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _artworkOrder = [];
    private Catalog? _lastGood;
    private readonly IPlayniteLibraryArtworkDiagnostics _artworkDiagnostics =
        artworkDiagnostics ?? PlayniteLibraryArtworkDiagnostics.None;

    public bool OwnsArtworkContent => true;

    public async ValueTask<WidgetAppLibraryPage> QueryAsync(
        WidgetAppLibraryQuery query,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken) => (await QueryWithAuthorityAsync(
            query, new(PlayniteLibraryQueryScope.Library), cursor, direction,
            limit, refresh, cancellationToken).ConfigureAwait(false)).Page;

    public async ValueTask<PlayniteLibraryQueryResult> QueryWithAuthorityAsync(
        WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        if (limit is < 1 or > WidgetAppLibraryService.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stale = false;
            Catalog catalog;
            try
            {
                catalog = refresh || _lastGood is null
                    ? await FetchCatalogAsync(cancellationToken).ConfigureAwait(false)
                    : _lastGood;
                _lastGood = catalog;
            }
            catch (Exception exception) when (CanRetain(exception, cancellationToken) &&
                                               _lastGood is not null)
            {
                catalog = _lastGood;
                stale = true;
            }

            var filtered = ApplyQuery(catalog.Games, query, context).ToArray();
            var offset = CursorOffset(cursor, direction, limit, filtered.Length);
            var pageItems = filtered.Skip(offset).Take(limit)
                .Select(game => Project(game, stale)).ToArray();
            var before = offset == 0 ? null : Math.Max(0, offset - limit).ToString();
            var after = offset + pageItems.Length >= filtered.Length
                ? null
                : (offset + pageItems.Length).ToString();
            var sourceHealth = stale
                ? WidgetAppLibrarySourceHealth.Degraded
                : WidgetAppLibrarySourceHealth.Healthy;
            var sources = catalog.Games.Select(game => game.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Take(PlayniteLibraryPrivateState.MaximumProvenSources)
                .Select(value => new WidgetAppLibrarySource(
                    SourceId(value), value, sourceHealth, catalog.Sequence,
                    stale ? "stale_last_good" : "connected")
                {
                    AccountState = WidgetAppLibrarySourceAccountState.NotApplicable,
                    LastSuccessfulRefreshAtUnixMilliseconds = catalog.RetrievedAt,
                }).ToArray();
            var page = new WidgetAppLibraryPage(
                pageItems, before, after, catalog.Revision) { Sources = sources };
            return new(page, ProjectAuthority(catalog.Games));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Safe(exception);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(savedIds);
        if (savedIds.Count > WidgetAppLibraryService.MaximumSavedItems)
            throw new ArgumentOutOfRangeException(nameof(savedIds));
        var result = new List<WidgetAppLibraryItem>();
        foreach (var value in savedIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayniteBridgeGame? game;
            try { game = await _client.ResolveGameAsync(value, cancellationToken)
                    .ConfigureAwait(false); }
            catch (PlayniteBridgeDataException exception) when (
                exception.Code == "game_not_found") { continue; }
            if (game is not null) result.Add(Project(game, stale: false));
        }
        return result;
    }

    public ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new WidgetRunningAppObservation([], "playnite-unavailable"));
    }

    public ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
        string savedId,
        string revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = savedId;
        _ = revision;
        return ValueTask.FromResult<WidgetAppLibraryItem?>(null);
    }

    public async ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken)
    {
        _ = overlayBehavior;
        try
        {
            var game = await _client.ResolveGameAsync(appId, cancellationToken)
                .ConfigureAwait(false);
            if (game is null || !game.IsInstalled)
                throw new PlayniteBridgeDataException("game_not_found");
            if (!await _client.LaunchAsync(game.Id, cancellationToken).ConfigureAwait(false))
                throw new PlayniteBridgeDataException("game_not_found");
            // Bridge acknowledges request admission only. WIDGE-121 owns observed-start and
            // overlay-close semantics; HTTP success is deliberately not projected as started.
            return new(WidgetAppLaunchObservationState.RequestAccepted,
                SupportsRunning: false, SupportsEnded: false);
        }
        catch (Exception exception) { throw Safe(exception); }
    }

    public async ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_artwork.TryGetValue(handle.Value, out var registration))
            {
                _artworkDiagnostics.Record("authority", "unknown-handle", 1, "unknown");
                return null;
            }
            if (_artworkContent.TryGetValue(handle.Value, out var cached)) return cached;
            var result = await _client.ResolveArtworkAsync(
                    registration.GameId, registration.Kind, cancellationToken)
                .ConfigureAwait(false);
            if (result.Artwork is null && registration.Kind == PlayniteBridgeArtworkKind.Background &&
                result.Code == "not-found")
            {
                // Playnite may not have a separate backdrop. Only that exact,
                // same-game absence may fall back to its cover; malformed and
                // unsupported payloads remain isolated failures.
                result = await _client.ResolveArtworkAsync(
                        registration.GameId, PlayniteBridgeArtworkKind.Cover,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            if (result.Artwork is null)
            {
                _artworkDiagnostics.Record("resolve", result.Code, 1, result.SizeClass);
                return null;
            }
            _artworkContent[handle.Value] = result.Artwork;
            return result.Artwork;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsArtworkFailure(exception))
        {
            _artworkDiagnostics.Record("resolve", ArtworkFailureCode(exception), 1,
                "unknown");
            return null;
        }
        finally { _gate.Release(); }
    }

    private static bool IsArtworkFailure(Exception exception) => exception is
        PlayniteBridgeDataException or PlayniteBridgeTransportException or
        HttpRequestException or IOException or InvalidOperationException;

    internal static string ArtworkFailureCode(Exception exception) => exception switch
    {
        PlayniteBridgeDataException data when data.Code == "game_not_found" => "not-found",
        PlayniteBridgeDataException data when data.Code == "playnite_unavailable" =>
            "provider-unavailable",
        PlayniteBridgeTransportException transport when transport.IsOverBudget =>
            "response-over-budget",
        PlayniteBridgeTransportException transport when transport.IsMalformed =>
            "response-over-budget-or-malformed",
        PlayniteBridgeTransportException transport when
            transport.InnerException is OperationCanceledException => "timeout",
        PlayniteBridgeTransportException => "transport-failure",
        HttpRequestException => "transport-failure",
        IOException => "io-failure",
        _ => "unexpected-failure",
    };

    public ValueTask<WidgetAppLibraryItem?> SetFavoriteAsync(
        string gameId, bool favorite, CancellationToken cancellationToken) =>
        MutateAsync(gameId,
            (id, token) => _client.SetFavoriteAsync(id, favorite, token),
            cancellationToken);

    public ValueTask<WidgetAppLibraryItem?> SetHiddenAsync(
        string gameId, bool hidden, CancellationToken cancellationToken) =>
        MutateAsync(gameId,
            (id, token) => _client.SetHiddenAsync(id, hidden, token),
            cancellationToken);

    public ValueTask<WidgetAppLibraryItem?> SetCategoriesAsync(
        string gameId,
        IReadOnlyList<string> categories,
        CancellationToken cancellationToken) => MutateAsync(gameId,
        (id, token) => _client.SetCategoriesAsync(id, categories, token),
        cancellationToken);

    public ValueTask<WidgetAppLibraryItem?> SetCompletionStatusAsync(
        string gameId,
        string completionStatus,
        CancellationToken cancellationToken) => MutateAsync(gameId,
        (id, token) => _client.SetCompletionStatusAsync(id, completionStatus, token),
        cancellationToken);

    public async ValueTask<PlayniteLibraryCategory?> CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await _client.CreateCategoryAsync(name, cancellationToken)
                .ConfigureAwait(false);
            return value is null || !Guid.TryParse(value.Id, out var id)
                ? null
                : new("category." + id.ToString("N"), value.Name, []);
        }
        catch (Exception exception) { throw Safe(exception); }
    }

    public async ValueTask<IReadOnlyList<string>> GetCompletionStatusesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _client.ListCompletionStatusesAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(value => value.Name).ToArray();
        }
        catch (Exception exception) { throw Safe(exception); }
    }

    public ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken) => _state.ReadAsync(cancellationToken);

    public ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        PlayniteLibraryPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken) =>
        _state.WriteAsync(state, expectedRevision, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        _artwork.Clear();
        _artworkContent.Clear();
        _artworkOrder.Clear();
        _gate.Dispose();
    }

    private async ValueTask<WidgetAppLibraryItem?> MutateAsync(
        string gameId,
        Func<string, CancellationToken, ValueTask<PlayniteBridgeGame?>> mutation,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await _client.ResolveGameAsync(gameId, cancellationToken)
                .ConfigureAwait(false);
            if (current is null) return null;
            var changed = await mutation(current.Id, cancellationToken).ConfigureAwait(false);
            if (changed is null || !string.Equals(changed.Id, current.Id, StringComparison.Ordinal))
                return null;
            _lastGood = null;
            return Project(changed, stale: false);
        }
        catch (Exception exception) { throw Safe(exception); }
    }

    private async Task<Catalog> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        var games = new List<PlayniteBridgeGame>();
        int? expectedTotal = null;
        for (var offset = 0; ; offset += PlayniteBridgeClient.MaximumPageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _client.QueryGamesAsync(new(
                    offset,
                    PlayniteBridgeClient.MaximumPageSize,
                    InstalledOnly: false,
                    IncludeHidden: true), cancellationToken)
                .ConfigureAwait(false);
            expectedTotal ??= page.Total;
            if (page.Total != expectedTotal || page.Offset != offset ||
                games.Count + page.Games.Count > PlayniteBridgeClient.MaximumGames)
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            games.AddRange(page.Games);
            if (games.Count == page.Total) break;
            if (page.Games.Count == 0)
                throw new PlayniteBridgeDataException("invalid_playnite_data");
        }
        if (games.Select(game => game.Id).Distinct(StringComparer.Ordinal).Count() != games.Count)
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        var revision = Revision(games);
        var sequence = _lastGood is null ? 1 : _lastGood.Sequence +
            (string.Equals(_lastGood.Revision, revision, StringComparison.Ordinal) ? 0 : 1);
        return new(games, revision, sequence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static IEnumerable<PlayniteBridgeGame> ApplyQuery(
        IReadOnlyList<PlayniteBridgeGame> games,
        WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context)
    {
        IEnumerable<PlayniteBridgeGame> values = games;
        if (query.InstalledOnly) values = values.Where(game => game.IsInstalled);
        if (context.Scope == PlayniteLibraryQueryScope.Hidden)
            values = values.Where(game => game.Hidden);
        else
            values = values.Where(game => !game.Hidden);
        if (context.Scope == PlayniteLibraryQueryScope.Category)
            values = values.Where(game => context.CategoryName is not null &&
                game.Categories.Contains(context.CategoryName,
                    StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.SearchText))
            values = values.Where(game => game.Name.Contains(
                query.SearchText, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.SourceAttribution))
            values = values.Where(game => string.Equals(
                game.Source, query.SourceAttribution, StringComparison.OrdinalIgnoreCase));
        if (query.FavoriteSavedIds.Count != 0)
            values = values.Where(game => query.FavoriteSavedIds.Contains(
                game.Id, StringComparer.Ordinal));
        if (context.Scope == PlayniteLibraryQueryScope.RecentlyPlayed)
            return values
                .Where(game => game.LastActivityUnixMilliseconds is not null)
                .OrderByDescending(game => game.LastActivityUnixMilliseconds)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Id, StringComparer.Ordinal)
                .Take(PlayniteLibraryPrivateState.MaximumRecentItems);
        return query.Sort switch
        {
            WidgetAppLibrarySortOrder.DisplayNameDescending => values
                .OrderByDescending(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Id, StringComparer.Ordinal),
            WidgetAppLibrarySortOrder.SourceThenDisplayName => values
                .OrderBy(game => game.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Id, StringComparer.Ordinal),
            _ => values.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Id, StringComparer.Ordinal),
        };
    }

    private WidgetAppLibraryItem Project(PlayniteBridgeGame game, bool stale)
    {
        var revision = GameRevision(game);
        var artwork = RegisterArtwork(game, revision);
        var installed = game.IsInstalled && !stale;
        var availability = stale
            ? new WidgetAppLibraryAvailability(
                WidgetAppLibraryAvailabilityState.StaleSource, false, "stale_last_good")
            : game.IsInstalled
                ? new WidgetAppLibraryAvailability(
                    WidgetAppLibraryAvailabilityState.Installed, true, "installed")
                : new WidgetAppLibraryAvailability(
                    WidgetAppLibraryAvailabilityState.Unavailable, false, "owned_not_installed");
        var categories = game.Categories.ToList();
        if (!string.IsNullOrWhiteSpace(game.CompletionStatus))
            categories.Add("Status: " + game.CompletionStatus);
        return new(game.Id, game.Id, new WidgetAppLibraryPresentation(
            game.Name,
            WidgetAppLibraryKind.Game,
            new WidgetAppLibrarySourceReference(SourceId(game.Source), game.Source),
            availability,
            artwork,
            new WidgetAppLibraryMetadata(
                revision,
                new WidgetAppLibraryMetadataAttribution(
                    "Playnite Bridge", revision, game.Source,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                Version = game.Version,
                LastPlayedAtUnixMilliseconds = game.LastActivityUnixMilliseconds,
                PlaytimeMinutes = game.PlaytimeSeconds / 60,
                Categories = categories,
                Description = game.Description,
            },
            new WidgetAppLibraryCapabilitySet(installed
                ? [WidgetAppLibraryAction.Launch]
                : []),
            ActiveOperation: null));
    }

    private WidgetAppLibraryArtworkSet RegisterArtwork(
        PlayniteBridgeGame game,
        string gameRevision)
    {
        var revision = ContentId("artwork-revision", game.Id, gameRevision);
        var cover = RegisterArtwork(game.Id, revision, PlayniteBridgeArtworkKind.Cover);
        var background = RegisterArtwork(game.Id, revision, PlayniteBridgeArtworkKind.Background);
        return new([
            new(WidgetAppLibraryArtworkRole.Tile, cover, revision,
                WidgetAppLibraryArtworkFallback.Game),
            new(WidgetAppLibraryArtworkRole.Cover, cover, revision,
                WidgetAppLibraryArtworkFallback.Game),
            new(WidgetAppLibraryArtworkRole.Hero, background, revision,
                WidgetAppLibraryArtworkFallback.Game),
        ]);
    }

    private string RegisterArtwork(
        string gameId,
        string revision,
        PlayniteBridgeArtworkKind kind)
    {
        var handle = ContentId("artwork-handle", gameId, revision, kind.ToString());
        if (!_artwork.ContainsKey(handle))
        {
            _artwork.Add(handle, new(gameId, revision, kind));
            _artworkOrder.Enqueue(handle);
            while (_artworkOrder.Count > MaximumArtworkEntries)
            {
                var retired = _artworkOrder.Dequeue();
                _artwork.Remove(retired);
                _artworkContent.Remove(retired);
            }
        }
        return handle;
    }

    private static PlayniteLibraryAuthorityProjection ProjectAuthority(
        IReadOnlyList<PlayniteBridgeGame> games)
    {
        var favorite = games.Where(game => game.Favorite).Select(game => game.Id).ToArray();
        var hidden = games.Where(game => game.Hidden).Select(game => game.Id).ToArray();
        var categoryMembers = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        var memberships = 0;
        foreach (var game in games)
        foreach (var name in game.Categories)
        {
            if (memberships >= MaximumCategoryMemberships) break;
            if (!categoryMembers.TryGetValue(name, out var members))
            {
                if (categoryMembers.Count >= PlayniteLibraryPrivateState.MaximumCategories)
                    continue;
                categoryMembers.Add(name, members = []);
            }
            members.Add(game.Id);
            memberships++;
        }
        var categories = categoryMembers.OrderBy(pair => pair.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PlayniteLibraryCategory(
                CategoryId(pair.Key), pair.Key, pair.Value)).ToArray();
        var completion = games.ToDictionary(
            game => game.Id, game => game.CompletionStatus, StringComparer.Ordinal);
        return new(favorite, hidden, categories, completion);
    }

    private static int CursorOffset(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        int count)
    {
        if (direction is null) return 0;
        if (cursor is null || !int.TryParse(cursor.Value.Value, out var offset) ||
            offset < 0 || offset > count)
            throw new WidgetCapabilityException("invalid_cursor", "The cursor is invalid.");
        return direction == WidgetCursorDirection.Before
            ? Math.Max(0, offset)
            : offset;
    }

    private static bool CanRetain(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is
            PlayniteBridgeDataException or PlayniteBridgeTransportException or
            PlayniteCredentialException;

    private static string Revision(IReadOnlyList<PlayniteBridgeGame> games) => ContentId(
        "catalog", games.OrderBy(game => game.Id, StringComparer.Ordinal)
            .Select(GameRevision).ToArray());

    private static string GameRevision(PlayniteBridgeGame game) => ContentId(
        "game", game.Id, game.Name, game.Source, game.IsInstalled.ToString(),
        game.Favorite.ToString(), game.Hidden.ToString(), game.CompletionStatus ?? string.Empty,
        string.Join('\u001f', game.Categories), game.PlaytimeSeconds.ToString(),
        game.LastActivityUnixMilliseconds?.ToString() ?? string.Empty,
        game.Version ?? string.Empty, game.Description ?? string.Empty);

    private static string SourceId(string value) => "source-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 12)).ToLowerInvariant();

    private static string CategoryId(string value) => "category." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16)).ToLowerInvariant();

    private static string ContentId(string kind, params string[] values) =>
        "pl-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            kind + "\0" + string.Join("\0", values))).AsSpan(0, 16)).ToLowerInvariant();

    private static WidgetCapabilityException Safe(Exception exception)
    {
        if (exception is WidgetCapabilityException capability) return capability;
        var code = exception switch
        {
            PlayniteBridgeDataException data when data.Code is
                "credential_missing" or "authentication_required" => data.Code,
            PlayniteBridgeDataException data when data.Code is
                "game_not_found" => "app_not_found",
            PlayniteBridgeDataException data when data.Code is
                "invalid_playnite_data" => "invalid_payload",
            _ => "platform_unavailable",
        };
        return new(code, code switch
        {
            "credential_missing" => "Configure Playnite Bridge before loading the library.",
            "authentication_required" => "The saved Playnite Bridge token was rejected.",
            "app_not_found" => "The selected Playnite game is no longer current.",
            "invalid_payload" => "Playnite Bridge returned malformed library data.",
            _ => "Playnite Bridge is unavailable.",
        });
    }

    private sealed record Catalog(
        IReadOnlyList<PlayniteBridgeGame> Games,
        string Revision,
        long Sequence,
        long RetrievedAt);

    private sealed record ArtworkRegistration(
        string GameId,
        string Revision,
        PlayniteBridgeArtworkKind Kind);
}
