using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryApplicationService(
    IPlayniteLibraryBridgeClient client,
    PlayniteLibraryStateFileStore state,
    IPlayniteLibraryArtworkDiagnostics? artworkDiagnostics = null,
    long artworkCacheBytes = PlayniteArtworkContentCache.MaximumRetainedBytes)
    : IPlayniteLibraryApplicationService
{
    private const int RegisteredArtworkRolesPerGame = 2;
    private const int MaximumArtworkEntries =
        (PlayniteLibraryWidget.MaximumRetainedItems * 2 +
         WidgetAppLibraryService.MaximumSavedItems +
         PlayniteLibraryPrivateState.MaximumExcludedItems) *
        RegisteredArtworkRolesPerGame;
    private const int MaximumArtworkTransitionEntries =
        (WidgetAppLibraryService.MaximumPageSize + WidgetAppLibraryService.MaximumSavedItems) *
        RegisteredArtworkRolesPerGame;
    private const int MaximumCategoryMemberships = PlayniteBridgeClient.MaximumGames;
    private readonly IPlayniteLibraryBridgeClient _client = client ??
        throw new ArgumentNullException(nameof(client));
    private readonly PlayniteLibraryStateFileStore _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ArtworkRegistration> _artwork =
        new(StringComparer.Ordinal);
    private readonly PlayniteArtworkContentCache _artworkContent =
        new(artworkCacheBytes);
    private readonly Queue<string> _artworkOrder = [];
    private readonly object _artworkGate = new();
    private HashSet<string> _pinnedArtwork = new(StringComparer.Ordinal);
    private Catalog? _lastGood;
    // One active traversal per bounded query scope keeps Home independent of
    // Browse and retires replaced queries without an unbounded snapshot cache.
    private sealed record Traversal(string Id, WidgetAppLibraryQuery Query,
        PlayniteLibraryQueryContext Context, PlayniteBridgeGame[] Items);
    private readonly Dictionary<PlayniteLibraryQueryScope, Traversal> _traversals = [];
    private long _catalogMutationRevision;
    private long _lastGoodMutationRevision;
    private readonly IPlayniteLibraryArtworkDiagnostics _artworkDiagnostics =
        artworkDiagnostics ?? PlayniteLibraryArtworkDiagnostics.None;
    private static readonly WidgetEncodedArtwork NeutralArtwork = new(
        WidgetArtworkContentType.Png,
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNgYGBgAAAABQAB" +
            "eqhXUAAAAABJRU5ErkJggg=="));

    public bool OwnsArtworkContent => true;

    internal long RetainedArtworkBytes
    {
        get { lock (_artworkGate) return _artworkContent.RetainedBytes; }
    }

    internal int RetainedArtworkEntryCount
    {
        get { lock (_artworkGate) return _artworkContent.Count; }
    }

    internal bool IsArtworkContentRetained(string handle)
    {
        lock (_artworkGate) return _artworkContent.Contains(handle);
    }

    public void PinArtworkHandles(IReadOnlyList<string> handles)
    {
        ArgumentNullException.ThrowIfNull(handles);
        var memoryEvents = new List<PlayniteArtworkMemoryEvent>();
        lock (_artworkGate)
        {
            var nextPinnedArtwork = handles.Where(_artwork.ContainsKey)
                .Take(MaximumArtworkEntries)
                .ToHashSet(StringComparer.Ordinal);
            var orderChanged = false;
            foreach (var retiredHandle in _pinnedArtwork.Except(nextPinnedArtwork))
                orderChanged |= RemoveArtworkLocked(retiredHandle, memoryEvents);
            _pinnedArtwork = nextPinnedArtwork;
            if (orderChanged) CompactArtworkOrderLocked();
            TrimArtworkLocked(
                MaximumArtworkEntries + MaximumArtworkTransitionEntries,
                memoryEvents);
        }
        RecordMemory(memoryEvents);
    }

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
        if (!Enum.IsDefined(context.Scope)) throw new ArgumentOutOfRangeException(nameof(context));
        if (limit is < 1 or > WidgetAppLibraryService.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stale = false;
            Catalog catalog;
            try
            {
                var mutationRevision = Interlocked.Read(ref _catalogMutationRevision);
                var lastGood = _lastGood;
                if (refresh || lastGood is null ||
                    Interlocked.Read(ref _lastGoodMutationRevision) != mutationRevision)
                {
                    catalog = await FetchCatalogAsync(cancellationToken).ConfigureAwait(false);
                    _lastGood = catalog;
                    Interlocked.Exchange(ref _lastGoodMutationRevision, mutationRevision);
                }
                else
                {
                    catalog = lastGood;
                }
            }
            catch (Exception exception) when (CanRetain(exception, cancellationToken) &&
                                               _lastGood is not null)
            {
                catalog = _lastGood;
                stale = true;
            }

            Traversal traversal;
            int offset;
            if (direction is null)
            {
                traversal = new(Guid.NewGuid().ToString("N"),
                    query with { FavoriteSavedIds = query.FavoriteSavedIds.ToArray() }, context,
                    ApplyQuery(catalog.Games, query, context).ToArray());
                _traversals[context.Scope] = traversal;
                offset = 0;
            }
            else
            {
                if (!_traversals.TryGetValue(context.Scope, out var existing) ||
                    existing.Context != context || !SameQuery(existing.Query, query))
                    throw new WidgetCapabilityException("invalid_cursor", "Refresh the library to continue browsing.");
                traversal = existing;
                offset = CursorOffset(cursor, traversal);
            }
            var current = catalog.Games.ToDictionary(game => game.Id, StringComparer.Ordinal);
            var pageItems = traversal.Items.Skip(offset).Take(limit)
                .Select(game => current.TryGetValue(game.Id, out var updated)
                    ? Project(updated, stale) : Project(game, stale: true)).ToArray();
            var before = offset == 0 ? null : Cursor(traversal, Math.Max(0, offset - limit));
            var after = offset + pageItems.Length >= traversal.Items.Length
                ? null
                : Cursor(traversal, offset + pageItems.Length);
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
            return new(page, ProjectAuthority(catalog.Games, catalog.Categories), stale)
            {
                MatchingGameCount = traversal.Items.Length,
            };
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
            ArtworkRegistration? registration;
            lock (_artworkGate)
                _artwork.TryGetValue(handle.Value, out registration);
            if (registration is null)
            {
                _artworkDiagnostics.Record("authority", "unknown-handle", 1, "unknown");
                return null;
            }
            WidgetEncodedArtwork? cached;
            lock (_artworkGate)
                _artworkContent.TryGet(handle.Value, out cached);
            if (cached is not null)
            {
                _artworkDiagnostics.RecordMemory(new(
                    PlayniteArtworkMemoryEventKind.Hit,
                    Role(registration.Kind),
                    cached.Bytes.Length));
                return cached;
            }
            _artworkDiagnostics.RecordMemory(new(
                PlayniteArtworkMemoryEventKind.Miss, Role(registration.Kind)));
            var result = await _client.ResolveArtworkAsync(
                    registration.GameId, registration.Kind, cancellationToken)
                .ConfigureAwait(false);
            var backgroundNotFound = false;
            if (result.Artwork is null && registration.Kind == PlayniteBridgeArtworkKind.Background &&
                result.Code == "not-found")
            {
                // Playnite may not have a separate backdrop. Only that exact,
                // same-game absence may fall back to its cover; malformed and
                // unsupported payloads remain isolated failures.
                backgroundNotFound = true;
                _artworkDiagnostics.RecordMemory(new(
                    PlayniteArtworkMemoryEventKind.BackgroundToCoverFallback,
                    PlayniteArtworkRole.Background));
                result = await _client.ResolveArtworkAsync(
                        registration.GameId, PlayniteBridgeArtworkKind.Cover,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            if (backgroundNotFound && result.Artwork is null && result.Code == "not-found")
            {
                CacheArtworkIfCurrent(handle.Value, registration, NeutralArtwork);
                _artworkDiagnostics.Record("resolve", "not-found-neutral", 1, "tiny");
                return NeutralArtwork;
            }
            if (result.Artwork is null)
            {
                _artworkDiagnostics.Record("resolve", result.Code, 1, result.SizeClass);
                return null;
            }
            CacheArtworkIfCurrent(handle.Value, registration, result.Artwork);
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

    public async ValueTask<WidgetAppLibraryItem?> SetCategoryMembershipAsync(
        string gameId,
        string categoryName,
        bool included,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedName = NormalizeProviderCategoryName(categoryName) ??
                throw new ArgumentException("Category is invalid.", nameof(categoryName));
            var current = await _client.ResolveGameAsync(gameId, cancellationToken)
                .ConfigureAwait(false);
            if (current is null) return null;
            var categories = current.Categories
                .Where(name => !string.Equals(
                    name, normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (included)
            {
                if (categories.Count >= PlayniteBridgeClient.MaximumCategoriesPerGame)
                    return null;
                categories.Add(normalizedName);
            }
            InvalidateCatalog();
            var changed = await _client.SetCategoriesAsync(
                    current.Id, categories, cancellationToken).ConfigureAwait(false);
            if (changed is null || !string.Equals(
                    changed.Id, current.Id, StringComparison.Ordinal) ||
                changed.Categories.Contains(
                    normalizedName, StringComparer.OrdinalIgnoreCase) != included)
                return null;
            return Project(changed, stale: false);
        }
        catch (Exception exception) { throw Safe(exception); }
    }

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
            var requestedName = NormalizeProviderCategoryName(name) ??
                throw new ArgumentException("Category is invalid.", nameof(name));
            InvalidateCatalog();
            var value = await _client.CreateCategoryAsync(requestedName, cancellationToken)
                .ConfigureAwait(false);
            if (value is null || !Guid.TryParse(value.Id, out var id) ||
                NormalizeProviderCategoryName(value.Name) is not { } normalizedName ||
                !string.Equals(normalizedName, value.Name, StringComparison.Ordinal) ||
                !string.Equals(normalizedName, requestedName, StringComparison.Ordinal))
                return null;
            return new(CategoryId(id), normalizedName, []);
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
        lock (_artworkGate)
        {
            _artwork.Clear();
            _artworkContent.Clear();
            _artworkOrder.Clear();
            _pinnedArtwork.Clear();
        }
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
            InvalidateCatalog();
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
        var categories = ProjectCategories(games,
            await _client.ListCategoriesAsync(cancellationToken).ConfigureAwait(false));
        var revision = Revision(games, categories);
        var sequence = _lastGood is null ? 1 : _lastGood.Sequence +
            (string.Equals(_lastGood.Revision, revision, StringComparison.Ordinal) ? 0 : 1);
        return new(games, categories, revision, sequence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static IEnumerable<PlayniteBridgeGame> ApplyQuery(
        IReadOnlyList<PlayniteBridgeGame> games,
        WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context)
    {
        IEnumerable<PlayniteBridgeGame> values = games;
        if (query.InstalledOnly || context.Scope == PlayniteLibraryQueryScope.Home)
            values = values.Where(game => game.IsInstalled);
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
        if (context.Scope == PlayniteLibraryQueryScope.Home)
            return values
                .OrderByDescending(game => game.Favorite)
                .ThenBy(game => game.LastActivityUnixMilliseconds is null)
                .ThenByDescending(game => game.LastActivityUnixMilliseconds)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Id, StringComparer.Ordinal);
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
        var memoryEvents = new List<PlayniteArtworkMemoryEvent>();
        lock (_artworkGate)
        {
            if (!_artwork.ContainsKey(handle))
            {
                _artwork.Add(handle, new(gameId, revision, kind));
                _artworkOrder.Enqueue(handle);
            }
            TrimArtworkLocked(
                MaximumArtworkEntries + MaximumArtworkTransitionEntries,
                memoryEvents);
        }
        RecordMemory(memoryEvents);
        return handle;
    }

    private void TrimArtworkLocked(
        int maximumEntries,
        ICollection<PlayniteArtworkMemoryEvent> memoryEvents)
    {
        var attempts = _artworkOrder.Count;
        while (_artwork.Count > maximumEntries && attempts-- > 0)
        {
            var candidate = _artworkOrder.Dequeue();
            if (_pinnedArtwork.Contains(candidate))
            {
                _artworkOrder.Enqueue(candidate);
                continue;
            }
            RemoveArtworkLocked(candidate, memoryEvents);
        }
    }

    private bool RemoveArtworkLocked(
        string handle,
        ICollection<PlayniteArtworkMemoryEvent> memoryEvents)
    {
        var registrationRemoved = _artwork.Remove(handle, out var registration);
        if (_artworkContent.Remove(handle) is { } removed)
            memoryEvents.Add(new(
                PlayniteArtworkMemoryEventKind.Eviction,
                !registrationRemoved
                    ? PlayniteArtworkRole.Neutral
                    : Role(registration!.Kind),
                removed.Bytes));
        return registrationRemoved;
    }

    private void CompactArtworkOrderLocked()
    {
        var count = _artworkOrder.Count;
        var retained = new HashSet<string>(StringComparer.Ordinal);
        while (count-- > 0)
        {
            var handle = _artworkOrder.Dequeue();
            if (_artwork.ContainsKey(handle) && retained.Add(handle))
                _artworkOrder.Enqueue(handle);
        }
    }

    private void CacheArtworkIfCurrent(
        string handle,
        ArtworkRegistration registration,
        WidgetEncodedArtwork artwork)
    {
        var memoryEvents = new List<PlayniteArtworkMemoryEvent>();
        lock (_artworkGate)
        {
            if (_artwork.TryGetValue(handle, out var current) &&
                ReferenceEquals(current, registration))
            {
                var stored = _artworkContent.Store(handle, artwork);
                if (stored.Stored)
                    memoryEvents.Add(new(
                        PlayniteArtworkMemoryEventKind.Store,
                        Role(registration.Kind),
                        artwork.Bytes.Length,
                        stored.PreviousBytes));
                foreach (var eviction in stored.Evictions)
                {
                    var role = _artwork.TryGetValue(eviction.Handle, out var owner)
                        ? Role(owner.Kind)
                        : PlayniteArtworkRole.Neutral;
                    memoryEvents.Add(new(
                        PlayniteArtworkMemoryEventKind.Eviction,
                        role,
                        eviction.Bytes));
                }
            }
        }
        RecordMemory(memoryEvents);
    }

    private void RecordMemory(IEnumerable<PlayniteArtworkMemoryEvent> values)
    {
        foreach (var value in values) _artworkDiagnostics.RecordMemory(value);
    }

    private static PlayniteArtworkRole Role(PlayniteBridgeArtworkKind kind) => kind switch
    {
        PlayniteBridgeArtworkKind.Cover => PlayniteArtworkRole.Cover,
        PlayniteBridgeArtworkKind.Background => PlayniteArtworkRole.Background,
        _ => PlayniteArtworkRole.Neutral,
    };

    private static PlayniteLibraryAuthorityProjection ProjectAuthority(
        IReadOnlyList<PlayniteBridgeGame> games,
        IReadOnlyList<PlayniteLibraryCategory> categories)
    {
        var favorite = games.Where(game => game.Favorite).Select(game => game.Id).ToArray();
        var hidden = games.Where(game => game.Hidden).Select(game => game.Id).ToArray();
        var completion = games.ToDictionary(
            game => game.Id, game => game.CompletionStatus, StringComparer.Ordinal);
        return new(favorite, hidden, categories, completion);
    }

    private static IReadOnlyList<PlayniteLibraryCategory> ProjectCategories(
        IReadOnlyList<PlayniteBridgeGame> games,
        IReadOnlyList<PlayniteBridgeNamedItem> catalog)
    {
        if (catalog.Count > PlayniteLibraryPrivateState.MaximumCategories)
            throw new PlayniteBridgeDataException("invalid_playnite_data");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projected = new List<PlayniteLibraryCategory>(catalog.Count);
        foreach (var value in catalog)
        {
            if (!Guid.TryParse(value.Id, out var providerId) ||
                NormalizeProviderCategoryName(value.Name) is not { } name ||
                !string.Equals(name, value.Name, StringComparison.Ordinal) ||
                !ids.Add(CategoryId(providerId)) || !names.Add(name))
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            projected.Add(new(CategoryId(providerId), name, []));
        }

        var byName = projected.ToDictionary(
            category => category.Name,
            category => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var memberships = 0;
        foreach (var game in games)
        foreach (var name in game.Categories)
        {
            if (memberships >= MaximumCategoryMemberships) break;
            if (!byName.TryGetValue(name, out var members)) continue;
            members.Add(game.Id);
            memberships++;
        }

        return projected
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Id, StringComparer.Ordinal)
            .Select(category => category with
            {
                SavedIds = byName[category.Name]
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();
    }

    private void InvalidateCatalog() =>
        Interlocked.Increment(ref _catalogMutationRevision);

    private static string? NormalizeProviderCategoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is > 0 and <= PlayniteBridgeClient.MaximumFilterCharacters &&
            !normalized.Any(char.IsControl) ? normalized : null;
    }

    private static bool SameQuery(WidgetAppLibraryQuery left, WidgetAppLibraryQuery right) =>
        left.InstalledOnly == right.InstalledOnly && left.Kind == right.Kind &&
        left.SourceAttribution == right.SourceAttribution && left.Sort == right.Sort &&
        left.SearchText == right.SearchText &&
        left.FavoriteSavedIds.SequenceEqual(right.FavoriteSavedIds, StringComparer.Ordinal);

    private static string Cursor(Traversal traversal, int offset) =>
        traversal.Id + "." + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int CursorOffset(WidgetCollectionCursor? cursor, Traversal traversal)
    {
        var prefix = traversal.Id + ".";
        if (cursor is null || !cursor.Value.Value.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(cursor.Value.Value.AsSpan(prefix.Length),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) ||
            offset < 0 || offset >= traversal.Items.Length)
            throw new WidgetCapabilityException("invalid_cursor", "Refresh the library to continue browsing.");
        return offset;
    }

    private static bool CanRetain(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is
            PlayniteBridgeDataException or PlayniteBridgeTransportException or
            PlayniteCredentialException;

    private static string Revision(
        IReadOnlyList<PlayniteBridgeGame> games,
        IReadOnlyList<PlayniteLibraryCategory> categories) => ContentId(
        "catalog", games.OrderBy(game => game.Id, StringComparer.Ordinal)
            .Select(GameRevision)
            .Concat(categories.OrderBy(category => category.Id, StringComparer.Ordinal)
                .Select(category => ContentId(
                    "category", category.Id, category.Name,
                    string.Join('\u001f', category.SavedIds))))
            .ToArray());

    private static string GameRevision(PlayniteBridgeGame game) => ContentId(
        "game", game.Id, game.Name, game.Source, game.IsInstalled.ToString(),
        game.Favorite.ToString(), game.Hidden.ToString(), game.CompletionStatus ?? string.Empty,
        string.Join('\u001f', game.Categories), game.PlaytimeSeconds.ToString(),
        game.LastActivityUnixMilliseconds?.ToString() ?? string.Empty,
        game.Version ?? string.Empty, game.Description ?? string.Empty);

    private static string SourceId(string value) => "source-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 12)).ToLowerInvariant();

    private static string CategoryId(Guid value) => "category." + value.ToString("N");

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
        IReadOnlyList<PlayniteLibraryCategory> Categories,
        string Revision,
        long Sequence,
        long RetrievedAt);

    private sealed record ArtworkRegistration(
        string GameId,
        string Revision,
        PlayniteBridgeArtworkKind Kind);
}
