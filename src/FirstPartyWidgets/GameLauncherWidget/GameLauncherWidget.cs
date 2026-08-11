using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using System.Text;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

/// <summary>
/// Controller-first complete installed game library. The trusted provider owns
/// discovery and launch authority; this widget retains only a bounded cursor
/// window plus a non-authorizing display projection.
/// </summary>
public sealed class GameLauncherWidget : Widget
{
    public const int PageSize = WidgetAppLibraryService.MaximumPageSize;
    public const int MaximumRetainedItems = 192;
    internal const int MaximumRetainedLaunchStates = 32;
    private const int MaximumKnownSources = 32;
    private static readonly WidgetAppLibraryQuery InstalledGames = new(
        InstalledOnly: true,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);
    private static readonly WidgetAppLibraryQuery InstalledRegistrations = new(
        InstalledOnly: true,
        Kind: null,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly WidgetCursorResource<GameLauncherItem> _library;
    private readonly WidgetNavigator<GameLauncherRoute> _navigation;
    private GameLauncherPrivateState _organization = GameLauncherPrivateState.Empty;
    private long _stateRevision;
    private string? _variantSeedSavedId;
    private bool _organizationBusy;
    private string _status = "Game Launcher loads when visible";
    private string? _launchingSavedId;
    private readonly Dictionary<string, GameLauncherLaunchState> _launchStates =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _launchStateRecency = [];
    private long _launchGeneration;
    private WidgetAppLibraryQuery _query = InstalledGames;
    private bool _favoriteFilter;
    private GameLauncherRecentMode _recentMode;
    private GameLauncherFixedRows _fixedRows = GameLauncherFixedRows.Empty;
    private IReadOnlyList<WidgetAppLibrarySource> _sourceObservations = [];
    private long _fixedRowsRevision;
    private readonly SortedSet<string> _knownSources = new(StringComparer.OrdinalIgnoreCase);

    public GameLauncherWidget()
    {
        _navigation = CreateNavigator("game-launcher.navigation", GameLauncherRoute.Library,
            maximumDepth: 1, maximumRoutes: 2);
        _library = CreateCursorResource<GameLauncherItem>("game-launcher.library", new()
        {
            PageSize = PageSize,
            MaximumRetainedItems = MaximumRetainedItems,
            PaginationThreshold = 2,
            LoadPage = LoadPageAsync,
            MapError = MapError,
            Viewports =
            [
                new(GameLauncherPresentation.ScrollId, item => item.Key,
                    item => GameLauncherIdentity.FocusId("grid", item.Key),
                    "game-launcher.empty.action"),
            ],
        });
    }

    internal WidgetCursorResourceSnapshot<GameLauncherItem> Collection => _library.Snapshot;
    internal int RetainedCursorCount => _library.RetainedCursorCount;
    internal IReadOnlyList<GameLauncherDisplayItem> WarmItems
    {
        get { lock (_gate) return _organization.Items.ToArray(); }
    }
    internal GameLauncherPrivateState Organization
    {
        get { lock (_gate) return _organization; }
    }
    internal int RetainedLaunchStateCount
    {
        get { lock (_gate) return _launchStates.Count; }
    }
    internal Task WhenLibraryIdleAsync(CancellationToken cancellationToken = default) =>
        _library.WhenIdleAsync(cancellationToken);
    internal Task WhenWarmStateIdleAsync(CancellationToken cancellationToken = default) =>
        Operations.WhenIdleAsync("game-launcher.warm-state", cancellationToken);

    public override WidgetView Render()
    {
        GameLauncherPresentationState state;
        var navigation = _navigation.Value;
        lock (_gate)
            state = new(
                _library.Snapshot,
                _organization,
                StatusLocked(_library.Snapshot),
                _launchingSavedId,
                new Dictionary<string, GameLauncherLaunchState>(
                    _launchStates, StringComparer.Ordinal),
                _organizationBusy,
                LifecycleState == WidgetLifecycleState.Interactive,
                _query,
                _recentMode,
                _favoriteFilter,
                navigation.Route,
                _fixedRows,
                _sourceObservations);
        var view = GameLauncherPresentation.Render(state);
        var root = _navigation.Scope(navigation, (StackElement)view.Root);
        return view with
        {
            Root = root,
            InitialFocusId = navigation.InitialFocusId ?? view.InitialFocusId,
            ActiveInputScopeId = navigation.InputScopeId,
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _ = Operations.RunLatest("game-launcher.warm-state",
            async context =>
            {
                await LoadWarmStateAsync(context.CancellationToken).ConfigureAwait(false);
                _ = _library.EnsureLoaded();
            },
            WidgetOperationLifetime.Active);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        _library.Reset(invalidate: false);
        lock (_gate)
        {
            _launchingSavedId = null;
            _launchGeneration++;
            _variantSeedSavedId = null;
            _organizationBusy = false;
            _fixedRows = GameLauncherFixedRows.Empty;
            _sourceObservations = [];
            _fixedRowsRevision++;
            _status = "Game Launcher is paused";
        }
        Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        _library.Reset(invalidate: false);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_navigation.TryHandleBack(action, action.SourceElementId))
        {
            ReturnToLibrary();
            return;
        }
        if (_library.TryHandlePagination(action, out _))
        {
            Operations.Cancel("game-launcher.launch-lifecycle");
            return;
        }
        switch (action.ActionId)
        {
            case "game-launcher.previous":
                Operations.Cancel("game-launcher.launch-lifecycle");
                _ = _library.Move(WidgetCursorDirection.Before, GameLauncherPresentation.ScrollId);
                return;
            case "game-launcher.next":
                Operations.Cancel("game-launcher.launch-lifecycle");
                _ = _library.Move(WidgetCursorDirection.After, GameLauncherPresentation.ScrollId);
                return;
            case "game-launcher.refresh":
                Operations.Cancel("game-launcher.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case "game-launcher.retry":
                _ = _library.Retry();
                return;
            case "game-launcher.launch":
                await LaunchAsync(action.SourceElementId, cancellationToken).ConfigureAwait(false);
                return;
            case "game-launcher.favorite":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await ToggleFavoriteAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.variant":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await ToggleVariantAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.prefer":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await PreferVariantAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.organization.reset":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(GameLauncherOrganizationPolicy.Clear,
                    "Organization cleared", cancellationToken).ConfigureAwait(false);
                return;
            case "game-launcher.add.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(GameLauncherRoute.AddGames, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _query = InstalledRegistrations;
                        _favoriteFilter = false;
                        _recentMode = GameLauncherRecentMode.Off;
                        _fixedRows = GameLauncherFixedRows.Empty;
                        _fixedRowsRevision++;
                    }
                    ReloadQuery();
                }
                return;
            case "game-launcher.add.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    ReturnToLibrary();
                return;
            case "game-launcher.manual.toggle":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != GameLauncherRoute.AddGames) return;
                await ToggleManualAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.recent.clear":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(GameLauncherOrganizationPolicy.ClearRecent,
                    "Recent launches cleared", cancellationToken).ConfigureAwait(false);
                lock (_gate) _recentMode = GameLauncherRecentMode.Off;
                ReloadQuery();
                return;
            case "game-launcher.search.commit":
                if (action.CommittedText is null) return;
                ReplaceQuery(_query with { SearchText = NormalizeSearch(action.CommittedText) });
                return;
            case "game-launcher.query.clear":
                lock (_gate)
                {
                    _favoriteFilter = false;
                    _recentMode = GameLauncherRecentMode.Off;
                }
                ReplaceQuery(_navigation.Value.Route == GameLauncherRoute.AddGames
                    ? InstalledRegistrations : InstalledGames, force: true);
                return;
            case "game-launcher.filter.favorites":
                if (_navigation.Value.Route != GameLauncherRoute.Library) return;
                lock (_gate) _favoriteFilter = !_favoriteFilter;
                ReloadQuery();
                return;
            case "game-launcher.filter.recent":
                if (_navigation.Value.Route != GameLauncherRoute.Library) return;
                lock (_gate)
                {
                    _recentMode = _recentMode switch
                    {
                        GameLauncherRecentMode.Off => GameLauncherRecentMode.RecentFirst,
                        GameLauncherRecentMode.RecentFirst => GameLauncherRecentMode.RecentOnly,
                        _ => GameLauncherRecentMode.Off,
                    };
                }
                ReloadQuery();
                return;
            case "game-launcher.filter.source":
                ReplaceQuery(_query with { SourceAttribution = NextSource() });
                return;
            case "game-launcher.filter.sort":
                ReplaceQuery(_query with { Sort = _query.Sort switch
                {
                    WidgetAppLibrarySortOrder.DisplayName =>
                        WidgetAppLibrarySortOrder.DisplayNameDescending,
                    WidgetAppLibrarySortOrder.DisplayNameDescending =>
                        WidgetAppLibrarySortOrder.SourceThenDisplayName,
                    _ => WidgetAppLibrarySortOrder.DisplayName,
                }});
                return;
        }
    }

    private static string? NormalizeSearch(string value)
    {
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private string? NextSource()
    {
        lock (_gate)
        {
            var choices = _knownSources.ToArray();
            if (choices.Length == 0) return null;
            if (_query.SourceAttribution is null) return choices[0];
            var index = Array.FindIndex(choices, value => string.Equals(
                value, _query.SourceAttribution, StringComparison.OrdinalIgnoreCase));
            return index < 0 || index + 1 == choices.Length ? null : choices[index + 1];
        }
    }

    private void ReplaceQuery(WidgetAppLibraryQuery query, bool force = false)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive || !force && query == _query) return;
        lock (_gate)
        {
            _query = query;
        }
        ReloadQuery();
    }

    private void ReloadQuery()
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        Operations.Cancel("game-launcher.launch-lifecycle");
        lock (_gate)
        {
            _status = "Applying library filters…";
            _launchingSavedId = null;
            _launchGeneration++;
            _fixedRows = GameLauncherFixedRows.Empty;
            _fixedRowsRevision++;
        }
        _library.Reset(invalidate: false);
        _ = _library.EnsureLoaded();
        Invalidate();
    }

    private void ReturnToLibrary()
    {
        lock (_gate)
        {
            _query = InstalledGames;
            _favoriteFilter = false;
            _recentMode = GameLauncherRecentMode.Off;
            _fixedRows = GameLauncherFixedRows.Empty;
            _fixedRowsRevision++;
        }
        ReloadQuery();
    }

    private WidgetAppLibraryQuery EffectiveQueryLocked()
    {
        IEnumerable<string>? savedIds = null;
        if (_favoriteFilter) savedIds = _organization.FavoriteSavedIds;
        if (_recentMode == GameLauncherRecentMode.RecentOnly)
            savedIds = savedIds is null
                ? _organization.RecentSavedIds
                : savedIds.Intersect(_organization.RecentSavedIds, StringComparer.Ordinal);
        return _query with
        {
            FavoriteSavedIds = savedIds?.Take(
                WidgetAppLibraryQuery.MaximumFavoriteSavedIds).ToArray() ?? [],
        };
    }

    private async ValueTask LoadWarmStateAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await HostServices.PrivateState.ReadAsync<GameLauncherPrivateState>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var normalized = GameLauncherOrganizationPolicy.Normalize(
                stored.Exists ? stored.Value : null);
            var revision = stored.Revision;
            if (stored.Exists && ReferenceEquals(
                    normalized, GameLauncherPrivateState.Empty))
            {
                var reset = await HostServices.PrivateState.WriteAsync(
                        GameLauncherPrivateState.Empty,
                        stored.Revision,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                revision = reset.Revision;
            }
            lock (_gate)
            {
                _organization = normalized;
                _stateRevision = revision;
                _status = normalized.Items.Count == 0
                    ? "Loading installed games…"
                    : $"Checking {normalized.Items.Count} saved display rows…";
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lock (_gate) _organization = GameLauncherPrivateState.Empty;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async ValueTask<WidgetCursorPage<GameLauncherItem>> LoadPageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        CancellationToken cancellationToken)
    {
        var requestCursor = direction is null ? null : cursor;
        WidgetAppLibraryQuery query;
        WidgetAppLibraryQuery baseQuery;
        GameLauncherPrivateState organization;
        GameLauncherRecentMode recentMode;
        bool favoriteFilter;
        var route = _navigation.Value.Route;
        lock (_gate)
        {
            baseQuery = _query;
            query = EffectiveQueryLocked();
            organization = _organization;
            recentMode = _recentMode;
            favoriteFilter = _favoriteFilter;
        }
        var page = await HostServices.AppLibrary.QueryAsync(
                query,
                requestCursor,
                direction,
                limit,
                refresh: direction is null,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var items = page.Items.Select(GameLauncherItem.From).ToArray();
        var fixedRows = GameLauncherFixedRows.Empty;
        if (route == GameLauncherRoute.Library && direction is null)
        {
            var fixedSavedIds = organization.RecentSavedIds
                .Concat(organization.ManualSavedIds)
                .Distinct(StringComparer.Ordinal)
                .Take(WidgetAppLibraryService.MaximumSavedItems)
                .ToArray();
            IReadOnlyList<WidgetAppLibraryItem> resolved = fixedSavedIds.Length == 0
                ? Array.Empty<WidgetAppLibraryItem>()
                : await HostServices.AppLibrary.ResolveSavedAsync(
                    fixedSavedIds, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var resolvedBySavedId = resolved.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var automaticManualGames = organization.ManualSavedIds
                .Where(savedId => resolvedBySavedId.TryGetValue(savedId, out var item) &&
                    item.Kind == WidgetAppLibraryKind.Game)
                .ToHashSet(StringComparer.Ordinal);
            if (automaticManualGames.Count != 0)
            {
                await SaveStateAsync(
                    state => GameLauncherOrganizationPolicy.RemoveAutomaticManualGames(
                        state, automaticManualGames), cancellationToken).ConfigureAwait(false);
                lock (_gate) organization = _organization;
            }
            GameLauncherItem[] recent = recentMode == GameLauncherRecentMode.Off
                ? []
                : organization.RecentSavedIds
                    .Select(savedId => resolvedBySavedId.GetValueOrDefault(savedId))
                    .OfType<WidgetAppLibraryItem>()
                    .Where(item => MatchesFixedQuery(item, query))
                    .Select(GameLauncherItem.From)
                    .Take(GameLauncherPrivateState.MaximumRecentItems)
                    .ToArray();
            GameLauncherItem[] manual = recentMode == GameLauncherRecentMode.RecentOnly
                ? []
                : organization.ManualSavedIds
                    .Select(savedId => resolvedBySavedId.GetValueOrDefault(savedId))
                    .OfType<WidgetAppLibraryItem>()
                    .Where(item =>
                        item.Kind != WidgetAppLibraryKind.Game &&
                        MatchesFixedQuery(item, query))
                    .Select(GameLauncherItem.From)
                    .Take(GameLauncherPrivateState.MaximumManualItems)
                    .ToArray();
            fixedRows = new(recent, manual);
        }
        lock (_gate)
            foreach (var source in page.Sources.Select(source => source.DisplayName)
                         .Concat(page.Items.Select(item => item.SourceAttribution)))
                if (!string.IsNullOrWhiteSpace(source) &&
                    (_knownSources.Contains(source) ||
                     _knownSources.Count < MaximumKnownSources))
                    _knownSources.Add(source);
        GameLauncherFixedRows retainedForProjection;
        lock (_gate) retainedForProjection = direction is null ? fixedRows : _fixedRows;
        var projectionItems = items.Concat(retainedForProjection.All)
            .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
            .ToArray();
        var projectionSaved = await PersistProjectionAsync(projectionItems, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            if (direction is null &&
                route == _navigation.Value.Route && baseQuery == _query &&
                recentMode == _recentMode && favoriteFilter == _favoriteFilter)
            {
                _fixedRows = fixedRows;
                _fixedRowsRevision++;
            }
            if (route == _navigation.Value.Route && baseQuery == _query &&
                recentMode == _recentMode && favoriteFilter == _favoriteFilter)
                _sourceObservations = page.Sources.ToArray();
            _status = !projectionSaved
                ? "Games loaded · organization was not saved"
                : items.Length == 0 && fixedRows.All.Any() ? "Saved games resolved" :
                    items.Length == 0 ? "No installed games" :
                    $"{items.Length}{(page.After is null ? string.Empty : "+")} games in the current catalog window";
        }
        return new(items,
            page.Before is null ? null : new WidgetCollectionCursor(page.Before),
            page.After is null ? null : new WidgetCollectionCursor(page.After));
    }

    private async Task LaunchAsync(string sourceElementId, CancellationToken cancellationToken)
    {
        var handle = Operations.RunSingleFlight(
            "game-launcher.launch-lifecycle",
            context => new ValueTask(LaunchCoreAsync(
                sourceElementId, context.CancellationToken, cancellationToken)),
            WidgetOperationLifetime.Active);
        if (!handle.IsAccepted) return;
        await handle.Completion.ConfigureAwait(false);
    }

    private async Task LaunchCoreAsync(
        string sourceElementId,
        CancellationToken activeLifetime,
        CancellationToken requestCancellation)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation, activeLifetime);
        GameLauncherItem? selected = null;
        long generation = 0;
        long collectionRevision = 0;
        long fixedRowsRevision = 0;
        try
        {
            GameLauncherFixedRows fixedRows;
            lock (_gate)
            {
                fixedRows = _fixedRows;
                fixedRowsRevision = _fixedRowsRevision;
            }
            selected = _library.Snapshot.Items.Concat(fixedRows.All).FirstOrDefault(item =>
                string.Equals(
                GameLauncherIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
            if (selected is null) return;
            if (_library.Snapshot.Items.Any(item => item.Key == selected.Key))
                _library.SelectAnchor(selected.Key, invalidate: false);
            lock (_gate)
            {
                _launchingSavedId = selected.Value.SavedId;
                generation = ++_launchGeneration;
                RemoveLaunchStateLocked(selected.Value.SavedId);
                _status = $"Pending · {selected.Value.DisplayName}";
            }
            collectionRevision = _library.Snapshot.Revision;
            Invalidate();
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    [selected.Value.SavedId], lifetime.Token).ConfigureAwait(false);
            var current = resolved.SingleOrDefault(item => string.Equals(
                item.SavedId, selected.Value.SavedId, StringComparison.Ordinal));
            var stillCurrent = IsCurrentResolved(selected.Key);
            if (current is null || !stillCurrent)
                throw new WidgetCapabilityException(
                    "app_not_found", "The selected game is no longer available.");
            var observation = await HostServices.AppLibrary.LaunchObservedAsync(
                    current.AppId,
                    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
                    lifetime.Token).ConfigureAwait(false);
            var accepted = false;
            lock (_gate)
            {
                if (generation == _launchGeneration &&
                    collectionRevision == _library.Snapshot.Revision &&
                    fixedRowsRevision == _fixedRowsRevision &&
                    IsCurrentResolved(selected.Key))
                {
                    accepted = true;
                    SetLaunchStateLocked(selected.Value.SavedId,
                        ToLaunchState(observation.State));
                    _status = LaunchStatus(selected.Value.DisplayName, observation.State);
                }
            }
            if (accepted && observation.State !=
                WidgetAppLaunchObservationState.RequestAccepted)
                await SaveStateAsync(state => GameLauncherOrganizationPolicy.RecordRecent(
                        state, new GameLauncherDisplayItem(selected.Value.SavedId,
                            selected.Value.DisplayName, selected.Value.SourceAttribution)),
                    lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (requestCancellation.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (selected is not null && generation == _launchGeneration &&
                    collectionRevision == _library.Snapshot.Revision &&
                    fixedRowsRevision == _fixedRowsRevision &&
                    IsCurrentResolved(selected.Key))
                {
                    SetLaunchStateLocked(
                        selected.Value.SavedId, GameLauncherLaunchState.Failed);
                    _status = $"Failed · {LaunchError(exception)}";
                }
            }
        }
        finally
        {
            lock (_gate)
                if (generation == _launchGeneration) _launchingSavedId = null;
            Invalidate();
        }
    }

    private void SetLaunchStateLocked(string savedId, GameLauncherLaunchState state)
    {
        RemoveLaunchStateLocked(savedId);
        _launchStates[savedId] = state;
        _launchStateRecency.AddLast(savedId);
        while (_launchStates.Count > MaximumRetainedLaunchStates)
            RemoveLaunchStateLocked(_launchStateRecency.First!.Value);
    }

    private void RemoveLaunchStateLocked(string savedId)
    {
        _launchStates.Remove(savedId);
        var node = _launchStateRecency.Find(savedId);
        if (node is not null) _launchStateRecency.Remove(node);
    }

    private static GameLauncherLaunchState ToLaunchState(
        WidgetAppLaunchObservationState state) => state switch
        {
            WidgetAppLaunchObservationState.RequestAccepted =>
                GameLauncherLaunchState.RequestAccepted,
            WidgetAppLaunchObservationState.LauncherStarted =>
                GameLauncherLaunchState.LauncherStarted,
            WidgetAppLaunchObservationState.Running => GameLauncherLaunchState.Running,
            WidgetAppLaunchObservationState.Ended => GameLauncherLaunchState.Ended,
            _ => GameLauncherLaunchState.RequestAccepted,
        };

    private static string LaunchStatus(
        string displayName,
        WidgetAppLaunchObservationState state) => state switch
        {
            WidgetAppLaunchObservationState.RequestAccepted =>
                $"Request accepted · {displayName}",
            WidgetAppLaunchObservationState.LauncherStarted =>
                $"Launcher started · {displayName}",
            WidgetAppLaunchObservationState.Running => $"Running · {displayName}",
            WidgetAppLaunchObservationState.Ended => $"Ended · {displayName}",
            _ => $"Request accepted · {displayName}",
        };

    private async Task ToggleFavoriteAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var display = DisplayForSource(sourceElementId);
        if (display is null) return;
        bool favorite;
        bool filteringFavorites;
        lock (_gate)
        {
            favorite = !_organization.FavoriteSavedIds.Contains(
                display.SavedId, StringComparer.Ordinal);
            filteringFavorites = _favoriteFilter;
        }
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.SetFavorite(state, display, favorite),
            favorite ? $"Favorited {display.DisplayName}" : $"Removed {display.DisplayName} from favorites",
            cancellationToken).ConfigureAwait(false);
        if (filteringFavorites)
        {
            ReloadQuery();
        }
    }

    private async Task ToggleManualAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var item = _library.Snapshot.Items.FirstOrDefault(candidate => string.Equals(
            GameLauncherIdentity.FocusId("add", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        if (item is null) return;
        if (item.Value.Kind == WidgetAppLibraryKind.Game)
        {
            lock (_gate) _status = "Games are included automatically";
            Invalidate();
            return;
        }
        var display = new GameLauncherDisplayItem(item.Value.SavedId,
            item.Value.DisplayName, item.Value.SourceAttribution);
        bool included;
        lock (_gate) included = _organization.ManualSavedIds.Contains(
            display.SavedId, StringComparer.Ordinal);
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.SetManual(state, display, !included),
            included ? $"Removed {display.DisplayName}" : $"Added {display.DisplayName}",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ToggleVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var current = DisplayForSource(sourceElementId);
        if (current is null) return;
        GameLauncherDisplayItem? first;
        GameLauncherVariantGroup? existing;
        var started = false;
        lock (_gate)
        {
            if (_variantSeedSavedId is null)
            {
                _variantSeedSavedId = current.SavedId;
                _status = $"Variant selection started with {current.DisplayName}";
                started = true;
            }
            if (started)
            {
                first = null;
                existing = null;
            }
            else
            {
                first = DisplayForSavedLocked(_variantSeedSavedId!);
                existing = first is null ? null : _organization.VariantGroups.FirstOrDefault(group =>
                    group.SavedIds.Contains(first.SavedId, StringComparer.Ordinal) &&
                    group.SavedIds.Contains(current.SavedId, StringComparer.Ordinal));
                _variantSeedSavedId = null;
            }
        }
        if (started) { Invalidate(); return; }
        if (first is null || first.SavedId == current.SavedId)
        {
            lock (_gate) _status = "Choose two distinct variants";
            Invalidate();
            return;
        }
        await MutateOrganizationAsync(
            existing is null
                ? state => GameLauncherOrganizationPolicy.Pair(state, first, current)
                : state => GameLauncherOrganizationPolicy.Unmerge(
                    state, existing.Id, current.SavedId),
            existing is null
                ? $"Grouped {first.DisplayName} with {current.DisplayName}"
                : $"Removed {current.DisplayName} from its variant group",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PreferVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var display = DisplayForSource(sourceElementId);
        if (display is null) return;
        GameLauncherVariantGroup? group;
        lock (_gate) group = GameLauncherOrganizationPolicy.GroupFor(
            _organization, display.SavedId);
        if (group is null)
        {
            lock (_gate) _status = "Group variants before choosing a preferred launch";
            Invalidate();
            return;
        }
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.Prefer(state, group.Id, display.SavedId),
            $"Preferred variant: {display.DisplayName}", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> PersistProjectionAsync(
        IReadOnlyList<GameLauncherItem> items,
        CancellationToken cancellationToken) => await SaveStateAsync(
            state => GameLauncherStateMutation.Apply(
                GameLauncherOrganizationPolicy.ProjectPage(state, items)),
            cancellationToken).ConfigureAwait(false);

    private async Task MutateOrganizationAsync(
        Func<GameLauncherPrivateState, GameLauncherStateMutation> apply,
        string success,
        CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        lock (_gate) _organizationBusy = true;
        Invalidate();
        var saved = false;
        try
        {
            saved = await SaveStateAsync(apply, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            lock (_gate)
            {
                _organizationBusy = false;
                if (LifecycleState is WidgetLifecycleState.Visible or
                    WidgetLifecycleState.Interactive)
                    _status = saved ? success : "Organization change was not saved";
            }
            Invalidate();
        }
    }

    private async Task<bool> SaveStateAsync(
        Func<GameLauncherPrivateState, GameLauncherStateMutation> apply,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GameLauncherPrivateState baseline;
            long revision;
            lock (_gate)
            {
                baseline = _organization;
                revision = _stateRevision;
            }
            var saved = await GameLauncherStateStore.SaveAsync(
                    apply,
                    (state, expected, token) => HostServices.PrivateState.WriteAsync(
                        state, expected, cancellationToken: token),
                    token => HostServices.PrivateState.ReadAsync<GameLauncherPrivateState>(
                        cancellationToken: token),
                    baseline,
                    revision,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _organization = saved.State;
                _stateRevision = saved.Revision;
            }
            return saved.Saved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private GameLauncherDisplayItem? DisplayForSource(string sourceElementId)
    {
        GameLauncherFixedRows fixedRows;
        lock (_gate) fixedRows = _fixedRows;
        var item = _library.Snapshot.Items.Concat(fixedRows.All)
            .FirstOrDefault(candidate => string.Equals(
            GameLauncherIdentity.FocusId("grid", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        return item is null ? null : new(
            item.Value.SavedId, item.Value.DisplayName, item.Value.SourceAttribution);
    }

    private GameLauncherDisplayItem? DisplayForSavedLocked(string savedId) =>
        _organization.Items.FirstOrDefault(item => item.SavedId == savedId) ??
        _library.Snapshot.Items.Where(item => item.Value.SavedId == savedId)
            .Select(item => new GameLauncherDisplayItem(
                item.Value.SavedId, item.Value.DisplayName, item.Value.SourceAttribution))
            .FirstOrDefault();

    private bool IsCurrentResolved(WidgetCollectionItemKey key)
    {
        if (_library.Snapshot.Items.Any(item => item.Key == key)) return true;
        lock (_gate) return _fixedRows.All.Any(item => item.Key == key);
    }

    private static bool MatchesFixedQuery(
        WidgetAppLibraryItem item,
        WidgetAppLibraryQuery query) =>
        (query.SearchText is null || item.DisplayName.Contains(
            query.SearchText, StringComparison.OrdinalIgnoreCase)) &&
        (query.SourceAttribution is null || string.Equals(item.SourceAttribution,
            query.SourceAttribution, StringComparison.OrdinalIgnoreCase)) &&
        (query.FavoriteSavedIds.Count == 0 || query.FavoriteSavedIds.Contains(
            item.SavedId, StringComparer.Ordinal));

    private string StatusLocked(WidgetCursorResourceSnapshot<GameLauncherItem> snapshot) =>
        snapshot.Status switch
        {
            WidgetPagedResourceStatus.Loading when _organization.Items.Count == 0 =>
                "Loading installed games…",
            WidgetPagedResourceStatus.Refreshing => "Refreshing installed games…",
            WidgetPagedResourceStatus.LoadingAdjacent => "Loading more games…",
            WidgetPagedResourceStatus.Error when snapshot.Items.Count != 0 =>
                $"{snapshot.Items.Count} games · some sources unavailable",
            WidgetPagedResourceStatus.Error => "Installed game library unavailable",
            _ => _status,
        };

    private static WidgetResourceError MapError(Exception exception) => exception switch
    {
        WidgetCapabilityException capability when capability.ErrorCode is
            "permission_denied" or "capability_not_declared" or "capability_revoked" =>
            new("permission_denied", "Allow Game Launcher access in Settings."),
        WidgetCapabilityException capability when capability.ErrorCode == "lifecycle_denied" =>
            new("lifecycle_denied", "Return to Game Launcher to load installed games."),
        WidgetCapabilityException =>
            new("library_unavailable", "Installed games are temporarily unavailable."),
        InvalidOperationException => WidgetResourceError.InvalidPage,
        _ => WidgetResourceError.Unexpected,
    };

    private static string LaunchError(Exception exception) => exception switch
    {
        WidgetCapabilityException capability when capability.ErrorCode == "app_not_found" =>
            "The selected game is no longer installed",
        WidgetCapabilityException capability when capability.ErrorCode is
            "permission_denied" or "capability_not_declared" or "capability_revoked" =>
            "Allow Game Launcher launch access in Settings",
        WidgetCapabilityException => "The selected game could not be opened",
        _ => "The selected game could not be opened",
    };
}
