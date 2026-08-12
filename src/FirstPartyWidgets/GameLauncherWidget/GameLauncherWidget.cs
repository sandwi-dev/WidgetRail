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
    private GameLauncherDetailsSelection? _detailsSelection;
    private string? _runningRevision;
    private long _fixedRowsRevision;
    private string? _pendingRestoredSavedId;
    private bool _preferLibraryContentFocus;
    private string? _heroSavedId;
    private int _heroIndex;
    private readonly SortedSet<string> _knownSources = new(StringComparer.OrdinalIgnoreCase);

    public GameLauncherWidget()
    {
        _navigation = CreateNavigator("game-launcher.navigation", GameLauncherRoute.Library,
            maximumDepth: 1, maximumRoutes: 5);
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
        GameLauncherDetailsState? details = null;
        bool preferLibraryContentFocus;
        var navigation = _navigation.Value;
        lock (_gate)
        {
            state = CapturePresentationStateLocked(navigation.Route);
            if (navigation.Route == GameLauncherRoute.Details && _detailsSelection is { } selected)
                details = GameLauncherDetailsPolicy.Project(selected, _library.Snapshot,
                    _fixedRows, _organization, _launchingSavedId, _launchStates,
                    _status, _variantSeedSavedId, _organizationBusy,
                    LifecycleState == WidgetLifecycleState.Interactive);
            preferLibraryContentFocus = _preferLibraryContentFocus;
        }
        var view = details is null
            ? GameLauncherPresentation.Render(state)
            : GameLauncherDetailsPresentation.Render(details);
        var root = _navigation.Scope(navigation, (StackElement)view.Root);
        return view with
        {
            Root = root,
            InitialFocusId = preferLibraryContentFocus
                ? view.InitialFocusId
                : navigation.InitialFocusId ?? view.InitialFocusId,
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
        if (_navigation.Value.Route == GameLauncherRoute.Details)
            _navigation.Back();
        lock (_gate)
        {
            _launchingSavedId = null;
            _launchGeneration++;
            _variantSeedSavedId = null;
            _organizationBusy = false;
            _fixedRows = GameLauncherFixedRows.Empty;
            _sourceObservations = [];
            _detailsSelection = null;
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
        SelectHeroForSource(action.SourceElementId);
        var routeBeforeBack = _navigation.Value.Route;
        if (_navigation.TryHandleBack(action, action.SourceElementId))
        {
            if (routeBeforeBack == GameLauncherRoute.Details)
            {
                lock (_gate) _detailsSelection = null;
            }
            else
            {
                await ReturnToLibraryAsync().ConfigureAwait(false);
            }
            return;
        }
        if (_library.TryHandlePagination(action, out _))
        {
            Operations.Cancel("game-launcher.launch-lifecycle");
            return;
        }
        switch (action.ActionId)
        {
            case "game-launcher.details.open":
                OpenDetails(action.SourceElementId);
                return;
            case "game-launcher.previous":
                TryMovePage(action, WidgetCursorDirection.Before);
                return;
            case "game-launcher.next":
                TryMovePage(action, WidgetCursorDirection.After);
                return;
            case "game-launcher.refresh":
                Operations.Cancel("game-launcher.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case "game-launcher.retry":
                _ = _library.Retry();
                return;
            case "game-launcher.launch":
                if (ResolveActionSource(action.SourceElementId) is { } launchSource)
                    await LaunchAsync(launchSource, cancellationToken).ConfigureAwait(false);
                return;
            case "game-launcher.favorite":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } favoriteSource)
                    await ToggleFavoriteAsync(favoriteSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "game-launcher.hide":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (GameLauncherRoute.Library or
                        GameLauncherRoute.Details) ||
                    ResolveActionSource(action.SourceElementId) is not { } hideSource) return;
                if (await SetHiddenAsync(hideSource, hidden: true, cancellationToken)
                        .ConfigureAwait(false) &&
                    _navigation.Value.Route == GameLauncherRoute.Details)
                    CloseDetails(preferLibraryContentFocus: true);
                return;
            case "game-launcher.restore":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != GameLauncherRoute.Hidden) return;
                await SetHiddenAsync(action.SourceElementId, hidden: false, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.variant":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } variantSource)
                {
                    var detailsRoute = _navigation.Value.Route == GameLauncherRoute.Details;
                    var result = await ToggleVariantAsync(variantSource, cancellationToken)
                        .ConfigureAwait(false);
                    if (detailsRoute && result == GameLauncherVariantActionResult.Started &&
                        _navigation.Value.Route == GameLauncherRoute.Details)
                        CloseDetails();
                }
                return;
            case "game-launcher.prefer":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } preferSource)
                    await PreferVariantAsync(preferSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "game-launcher.organization.reset":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(GameLauncherOrganizationPolicy.Clear,
                    "Organization cleared", cancellationToken).ConfigureAwait(false);
                return;
            case "game-launcher.experiences.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                _navigation.Push(GameLauncherRoute.Experiences, action.SourceElementId);
                return;
            case "game-launcher.experiences.back":
                _navigation.Back(action.SourceElementId);
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
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "game-launcher.add.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "game-launcher.running.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(GameLauncherRoute.Running, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _query = InstalledRegistrations;
                        _favoriteFilter = false;
                        _recentMode = GameLauncherRecentMode.Off;
                        _fixedRows = GameLauncherFixedRows.Empty;
                        _fixedRowsRevision++;
                        _runningRevision = null;
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "game-launcher.running.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "game-launcher.hidden.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(GameLauncherRoute.Hidden, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _query = InstalledGames;
                        _favoriteFilter = false;
                        _recentMode = GameLauncherRecentMode.Off;
                        _fixedRows = GameLauncherFixedRows.Empty;
                        _fixedRowsRevision++;
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "game-launcher.hidden.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "game-launcher.manual.toggle":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (GameLauncherRoute.AddGames or
                        GameLauncherRoute.Running)) return;
                if (_navigation.Value.Route == GameLauncherRoute.Running)
                    await AddRunningAsync(action.SourceElementId, cancellationToken)
                        .ConfigureAwait(false);
                else
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
        const string experiencePrefix = "game-launcher.experience.select.";
        if (action.ActionId.StartsWith(experiencePrefix, StringComparison.Ordinal) &&
            LifecycleState == WidgetLifecycleState.Interactive)
        {
            var experience = GameLauncherExperienceIdentity.Parse(
                action.ActionId[experiencePrefix.Length..]);
            await MutateOrganizationAsync(
                    state => GameLauncherOrganizationPolicy.SelectExperience(
                        state, experience),
                    $"Experience set to {GameLauncherExperienceIdentity.Label(experience)}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public override async ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Context == ControllerInputContext.OpenWidget &&
            input.Phase is ControllerEventPhase.Pressed or ControllerEventPhase.Repeated &&
            _navigation.Value.Route == GameLauncherRoute.Library)
        {
            GameLauncherPresentationState state;
            lock (_gate) state = CapturePresentationStateLocked(GameLauncherRoute.Library);
            var model = GameLauncherHeroRailPolicy.Project(
                state, state.HeroSavedId, state.HeroIndex);
            var selected = GameLauncherHeroRailPolicy.SelectFromInput(
                model, input.FocusedElementId, input.Button);
            if (selected is not null)
            {
                var index = Enumerable.Range(0, model.Items.Count)
                    .First(candidate => ReferenceEquals(model.Items[candidate], selected));
                var changed = false;
                lock (_gate)
                {
                    if (_navigation.Value.Route == GameLauncherRoute.Library &&
                        (!string.Equals(_heroSavedId, selected.Display.SavedId,
                                StringComparison.Ordinal) || _heroIndex != index))
                    {
                        _heroSavedId = selected.Display.SavedId;
                        _heroIndex = index;
                        changed = true;
                    }
                }
                if (changed) Invalidate();
            }
        }
        return await base.OnControllerInputAsync(input, cancellationToken)
            .ConfigureAwait(false);
    }

    private void TryMovePage(
        WidgetActionEvent action,
        WidgetCursorDirection direction)
    {
        var expectedButtonId = direction == WidgetCursorDirection.Before
            ? "game-launcher.previous"
            : "game-launcher.next";
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            action.SourceElementId is not GameLauncherPresentation.ScrollId &&
            action.SourceElementId != expectedButtonId)
            return;
        var snapshot = _library.Snapshot;
        if (snapshot.Status != WidgetPagedResourceStatus.Ready ||
            direction == WidgetCursorDirection.Before && !snapshot.HasBefore ||
            direction == WidgetCursorDirection.After && !snapshot.HasAfter)
            return;
        Operations.Cancel("game-launcher.launch-lifecycle");
        _ = _library.Move(direction, GameLauncherPresentation.ScrollId);
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

    private WidgetOperationHandle? ReloadQuery()
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return null;
        Operations.Cancel("game-launcher.launch-lifecycle");
        lock (_gate)
        {
            _status = "Applying library filters…";
            _launchingSavedId = null;
            _launchGeneration++;
            _fixedRows = GameLauncherFixedRows.Empty;
            _fixedRowsRevision++;
            _preferLibraryContentFocus = false;
        }
        _library.Reset(invalidate: false);
        var operation = _library.EnsureLoaded();
        Invalidate();
        return operation;
    }

    private async Task ReloadQueryAsync()
    {
        var operation = ReloadQuery();
        if (operation is { } admitted)
            await admitted.Completion.ConfigureAwait(false);
    }

    private async Task ReturnToLibraryAsync()
    {
        lock (_gate)
        {
            _query = InstalledGames;
            _favoriteFilter = false;
            _recentMode = GameLauncherRecentMode.Off;
            _fixedRows = GameLauncherFixedRows.Empty;
            _fixedRowsRevision++;
        }
        await ReloadQueryAsync().ConfigureAwait(false);
        string? restoredSavedId;
        lock (_gate)
        {
            restoredSavedId = _pendingRestoredSavedId;
            _pendingRestoredSavedId = null;
        }
        var restored = restoredSavedId is null ? null : _library.Snapshot.Items
            .FirstOrDefault(item => string.Equals(
                item.Value.SavedId, restoredSavedId, StringComparison.Ordinal));
        if (restored is not null)
            _library.SelectAnchor(restored.Key, invalidate: false);
        lock (_gate) _preferLibraryContentFocus = restoredSavedId is not null;
        Invalidate();
    }

    private WidgetAppLibraryQuery EffectiveQueryLocked(GameLauncherRoute route)
    {
        IEnumerable<string>? savedIds = route == GameLauncherRoute.Hidden
            ? _organization.ExcludedSavedIds
            : null;
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
            query = EffectiveQueryLocked(route);
            organization = _organization;
            recentMode = _recentMode;
            favoriteFilter = _favoriteFilter;
        }
        if (route == GameLauncherRoute.Running)
        {
            if (direction is not null || cursor is not null)
                throw new InvalidOperationException("Running apps are a single bounded page.");
            var observed = await HostServices.AppLibrary.ObserveRunningAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var running = observed.Items.Select(candidate => GameLauncherItem.From(
                new WidgetAppLibraryItem(candidate.SavedId, candidate.SavedId,
                    new WidgetAppLibraryPresentation(
                        candidate.DisplayName,
                        candidate.Kind,
                        new WidgetAppLibrarySourceReference(
                            "source-running", candidate.SourceAttribution),
                        new WidgetAppLibraryAvailability(
                            WidgetAppLibraryAvailabilityState.StaleSource,
                            false, "confirmation_required"),
                        new WidgetAppLibraryArtworkSet([]),
                        Metadata: null,
                        new WidgetAppLibraryCapabilitySet([]),
                        ActiveOperation: null)))).Take(limit).ToArray();
            lock (_gate)
            {
                if (_navigation.Value.Route != GameLauncherRoute.Running)
                    throw new OperationCanceledException(cancellationToken);
                _runningRevision = observed.Revision;
                _sourceObservations = [];
                _status = running.Length == 0
                    ? "No visible applications match the installed library"
                    : $"{running.Length} visible installed application{(running.Length == 1 ? "" : "s")}";
            }
            return new(running, null, null);
        }
        if (route == GameLauncherRoute.Hidden && organization.ExcludedSavedIds.Count == 0)
        {
            lock (_gate) _status = "No hidden games";
            return new([], null, null);
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
                    item.Presentation.Kind == WidgetAppLibraryKind.Game)
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
                        item.Presentation.Kind != WidgetAppLibraryKind.Game &&
                        MatchesFixedQuery(item, query))
                    .Select(GameLauncherItem.From)
                    .Take(GameLauncherPrivateState.MaximumManualItems)
                    .ToArray();
            fixedRows = new(recent, manual);
        }
        lock (_gate)
            foreach (var source in page.Sources.Select(source => source.DisplayName)
                         .Concat(page.Items.Select(item => item.Presentation.Source.DisplayName)))
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
                _status = $"Pending · {selected.Value.Presentation.DisplayName}";
            }
            collectionRevision = _library.Snapshot.Revision;
            Invalidate();
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    [selected.Value.SavedId], lifetime.Token).ConfigureAwait(false);
            var current = resolved.SingleOrDefault(item => string.Equals(
                item.SavedId, selected.Value.SavedId, StringComparison.Ordinal));
            var stillCurrent = IsCurrentResolved(selected.Key);
            if (current is null || !stillCurrent ||
                current.Presentation.Availability.State !=
                    WidgetAppLibraryAvailabilityState.Installed ||
                !current.Presentation.Availability.IsLaunchable ||
                !current.Presentation.Capabilities.Supports(WidgetAppLibraryAction.Launch))
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
                    _status = LaunchStatus(
                        selected.Value.Presentation.DisplayName,
                        observation.State);
                }
            }
            if (accepted && observation.State !=
                WidgetAppLaunchObservationState.RequestAccepted)
                await SaveStateAsync(state => GameLauncherOrganizationPolicy.RecordRecent(
                        state, new GameLauncherDisplayItem(selected.Value.SavedId,
                            selected.Value.Presentation.DisplayName,
                            selected.Value.Presentation.Source.DisplayName)),
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

    private async Task<bool> SetHiddenAsync(
        string sourceElementId,
        bool hidden,
        CancellationToken cancellationToken)
    {
        GameLauncherDisplayItem? display;
        if (hidden)
        {
            display = DisplayForSource(sourceElementId);
        }
        else
        {
            lock (_gate)
            {
                var savedId = _organization.ExcludedSavedIds.FirstOrDefault(candidate =>
                    string.Equals(GameLauncherIdentity.FocusId(
                            "hidden", GameLauncherIdentity.Key(candidate)), sourceElementId,
                        StringComparison.Ordinal));
                display = savedId is null ? null : DisplayForSavedLocked(savedId);
            }
        }
        if (display is null) return false;
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.SetExcluded(state, display, hidden),
            hidden ? $"Hidden {display.DisplayName}" : $"Restored {display.DisplayName}",
            cancellationToken).ConfigureAwait(false);
        bool applied;
        lock (_gate)
            applied = _organization.ExcludedSavedIds.Contains(
                display.SavedId, StringComparer.Ordinal) == hidden;
        if (applied)
        {
            if (!hidden)
                lock (_gate) _pendingRestoredSavedId = display.SavedId;
            await ReloadQueryAsync().ConfigureAwait(false);
        }
        return applied;
    }

    private async Task ToggleManualAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var item = _library.Snapshot.Items.FirstOrDefault(candidate => string.Equals(
            GameLauncherIdentity.FocusId("add", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        if (item is null) return;
        if (item.Presentation.Kind == WidgetAppLibraryKind.Game)
        {
            lock (_gate) _status = "Games are included automatically";
            Invalidate();
            return;
        }
        await SetManualCurrentAsync(item.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddRunningAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var candidate = _library.Snapshot.Items.FirstOrDefault(item => string.Equals(
            GameLauncherIdentity.FocusId("add", item.Key), sourceElementId,
            StringComparison.Ordinal));
        if (candidate is null) return;
        string? revision;
        lock (_gate)
        {
            if (_navigation.Value.Route != GameLauncherRoute.Running ||
                GameLauncherOrganizationPolicy.ReferencedSavedIds(_organization)
                    .Contains(candidate.Value.SavedId, StringComparer.Ordinal)) return;
            revision = _runningRevision;
        }
        if (revision is null) return;
        var current = await HostServices.AppLibrary.ConfirmRunningAsync(
            candidate.Value.SavedId, revision, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            lock (_gate) _status = "Running app changed · refresh and try again";
            Invalidate();
            return;
        }
        lock (_gate)
            if (_navigation.Value.Route != GameLauncherRoute.Running ||
                !string.Equals(_runningRevision, revision, StringComparison.Ordinal)) return;
        await SetManualCurrentAsync(current, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetManualCurrentAsync(
        WidgetAppLibraryItem item,
        CancellationToken cancellationToken)
    {
        if (item.Presentation.Kind == WidgetAppLibraryKind.Game)
        {
            lock (_gate) _status = "Games are included automatically";
            Invalidate();
            return;
        }
        var display = new GameLauncherDisplayItem(item.SavedId,
            item.Presentation.DisplayName, item.Presentation.Source.DisplayName);
        bool included;
        lock (_gate)
        {
            if (_organization.RecentSavedIds.Contains(display.SavedId, StringComparer.Ordinal) ||
                _organization.ExcludedSavedIds.Contains(display.SavedId, StringComparer.Ordinal))
            {
                _status = "App is already retained in the library";
                return;
            }
            included = _organization.ManualSavedIds.Contains(
                display.SavedId, StringComparer.Ordinal);
        }
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.SetManual(state, display, !included),
            included ? $"Removed {display.DisplayName}" : $"Added {display.DisplayName}",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GameLauncherVariantActionResult> ToggleVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var current = DisplayForSource(sourceElementId);
        if (current is null) return GameLauncherVariantActionResult.Rejected;
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
                first = DisplayForCurrentSavedLocked(_variantSeedSavedId!);
                existing = first is null ? null : _organization.VariantGroups.FirstOrDefault(group =>
                    group.SavedIds.Contains(first.SavedId, StringComparer.Ordinal) &&
                    group.SavedIds.Contains(current.SavedId, StringComparer.Ordinal));
                _variantSeedSavedId = null;
            }
        }
        if (started)
        {
            Invalidate();
            return GameLauncherVariantActionResult.Started;
        }
        if (first is null || first.SavedId == current.SavedId)
        {
            lock (_gate) _status = first is null
                ? "The first selected game is no longer available"
                : "Choose a different game for the second variant";
            Invalidate();
            return GameLauncherVariantActionResult.Rejected;
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
        return GameLauncherVariantActionResult.Completed;
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
            item.Value.SavedId,
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName);
    }

    private GameLauncherPresentationState CapturePresentationStateLocked(
        GameLauncherRoute route) => new(
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
        route,
        _fixedRows,
        _sourceObservations,
        _heroSavedId,
        _heroIndex);

    private void OpenDetails(string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != GameLauncherRoute.Library) return;
        GameLauncherFixedRows fixedRows;
        lock (_gate) fixedRows = _fixedRows;
        var selection = GameLauncherDetailsPolicy.Select(
            sourceElementId, _library.Snapshot, fixedRows);
        if (selection is null) return;
        lock (_gate)
        {
            _detailsSelection = selection;
            _heroSavedId = selection.SavedId;
            _preferLibraryContentFocus = false;
        }
        if (_navigation.Push(GameLauncherRoute.Details, sourceElementId) !=
            WidgetNavigationResult.Changed)
            lock (_gate) _detailsSelection = null;
    }

    private string? ResolveActionSource(string sourceElementId)
    {
        GameLauncherDetailsSelection? selection;
        GameLauncherFixedRows fixedRows;
        lock (_gate)
        {
            selection = _detailsSelection;
            fixedRows = _fixedRows;
        }
        return GameLauncherDetailsPolicy.ResolveActionSource(
            selection, sourceElementId, _library.Snapshot, fixedRows);
    }

    private void SelectHeroForSource(string sourceElementId)
    {
        if (_navigation.Value.Route != GameLauncherRoute.Library) return;
        GameLauncherFixedRows fixedRows;
        lock (_gate) fixedRows = _fixedRows;
        var selected = _library.Snapshot.Items.Concat(fixedRows.All)
            .FirstOrDefault(item => string.Equals(
                GameLauncherIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
        if (selected is null) return;
        var changed = false;
        lock (_gate)
        {
            if (!string.Equals(_heroSavedId, selected.Value.SavedId,
                    StringComparison.Ordinal))
            {
                _heroSavedId = selected.Value.SavedId;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private void CloseDetails(bool preferLibraryContentFocus = false)
    {
        lock (_gate)
        {
            _detailsSelection = null;
            _preferLibraryContentFocus = preferLibraryContentFocus;
        }
        _navigation.Back();
    }

    private GameLauncherDisplayItem? DisplayForSavedLocked(string savedId) =>
        _organization.Items.FirstOrDefault(item => item.SavedId == savedId) ??
        _library.Snapshot.Items.Where(item => item.Value.SavedId == savedId)
            .Select(item => new GameLauncherDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName))
            .FirstOrDefault();

    private GameLauncherDisplayItem? DisplayForCurrentSavedLocked(string savedId) =>
        _library.Snapshot.Items.Concat(_fixedRows.All)
            .Where(item => string.Equals(item.Value.SavedId, savedId,
                StringComparison.Ordinal))
            .Select(item => new GameLauncherDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName))
            .FirstOrDefault();

    private bool IsCurrentResolved(WidgetCollectionItemKey key)
    {
        if (_library.Snapshot.Items.Any(item => item.Key == key)) return true;
        lock (_gate) return _fixedRows.All.Any(item => item.Key == key);
    }

    private static bool MatchesFixedQuery(
        WidgetAppLibraryItem item,
        WidgetAppLibraryQuery query) =>
        (query.SearchText is null || item.Presentation.DisplayName.Contains(
            query.SearchText, StringComparison.OrdinalIgnoreCase)) &&
        (query.SourceAttribution is null || string.Equals(
            item.Presentation.Source.DisplayName, query.SourceAttribution,
            StringComparison.OrdinalIgnoreCase)) &&
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
