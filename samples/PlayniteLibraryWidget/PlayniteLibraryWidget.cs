using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Text;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// Controller-first complete installed game library. The trusted provider owns
/// discovery and launch authority; this widget retains only a bounded cursor
/// window plus a non-authorizing display projection.
/// </summary>
public sealed partial class PlayniteLibraryWidget : Widget
{
    public const int PageSize = WidgetAppLibraryService.MaximumPageSize;
    public const int MaximumRetainedItems = 192;
    internal const int MaximumRetainedLaunchStates = 32;
    internal const int MaximumKnownSourcesForCollections =
        PlayniteLibraryPrivateState.MaximumProvenSources;
    private static readonly WidgetAppLibraryQuery InstalledGames = new(
        InstalledOnly: true,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly IPlayniteLibraryApplicationService _application;
    private readonly WidgetCursorResource<PlayniteLibraryItem> _library;
    private readonly WidgetNavigator<PlayniteLibraryRoute> _navigation;
    private readonly WidgetModel<PlayniteLibraryRenderState> _model;
    private PlayniteLibraryPrivateState _organization = PlayniteLibraryPrivateState.Empty;
    private PlayniteLibraryAuthorityProjection _playniteAuthority =
        PlayniteLibraryAuthorityProjection.Empty;
    private long _stateRevision;
    private readonly Dictionary<string, PlayniteLibraryLaunchState> _launchStates =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _launchStateRecency = [];
    private readonly PlayniteLibraryLaunchPersistenceCoordinator _launchPersistence = new();

    internal PlayniteLibraryWidget(
        IPlayniteLibraryApplicationService application,
        IPlayniteBridgeClient? playniteClient = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _playniteClient = playniteClient;
        _model = CreateModel(PlayniteLibraryRenderState.Initial(InstalledGames));
        _navigation = CreateNavigator("playnite-library.navigation", PlayniteLibraryRoute.Library,
            maximumDepth: 1, maximumRoutes: 8);
        _library = CreateCursorResource<PlayniteLibraryItem>("playnite-library.library", new()
        {
            PageSize = PageSize,
            MaximumRetainedItems = MaximumRetainedItems,
            PaginationThreshold = 2,
            LoadPage = LoadPageAsync,
            MapError = MapError,
            Viewports =
            [
                new(PlayniteLibraryPresentation.ScrollId, item => item.Key,
                    item => PlayniteLibraryIdentity.FocusId("grid", item.Key),
                    "playnite-library.empty.action"),
            ],
        });
    }

    internal WidgetCursorResourceSnapshot<PlayniteLibraryItem> Collection => _library.Snapshot;
    internal int RetainedCursorCount => _library.RetainedCursorCount;
    internal IReadOnlyList<PlayniteLibraryDisplayItem> WarmItems
    {
        get { lock (_gate) return _organization.Items.ToArray(); }
    }
    internal PlayniteLibraryPrivateState Organization
    {
        get { lock (_gate) return _organization; }
    }
    internal int RetainedLaunchStateCount
    {
        get { lock (_gate) return _launchStates.Count; }
    }
    internal WidgetModelSnapshot<PlayniteLibraryRenderState> RenderState => _model.Snapshot;
    internal Task WhenLibraryIdleAsync(CancellationToken cancellationToken = default) =>
        _library.WhenIdleAsync(cancellationToken);
    internal Task WhenWarmStateIdleAsync(CancellationToken cancellationToken = default) =>
        Operations.WhenIdleAsync("playnite-library.warm-state", cancellationToken);

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        var local = _model.Value;
        if (navigation.Route == PlayniteLibraryRoute.PlayniteConnection)
        {
            var connection = CapturePlayniteConnection(local);
            var connectionView = PlayniteLibraryConnectionPresentation.Render(connection);
            return connectionView with
            {
                Root = _navigation.Scope(navigation, connectionView.Root),
                InitialFocusId = connectionView.InitialFocusId,
                ActiveInputScopeId = navigation.InputScopeId,
            };
        }

        PlayniteLibraryPresentationState state;
        lock (_gate)
        {
            state = CapturePresentationStateLocked(navigation.Route, local);
        }
        var view = PlayniteLibraryPresentation.Render(state);
        var root = _navigation.Scope(navigation, view.Root);
        return view with
        {
            Root = root,
            InitialFocusId = navigation.Route is PlayniteLibraryRoute.Categories or
                    PlayniteLibraryRoute.Category
                ? view.InitialFocusId
                : local.PreferLibraryContentFocus
                ? view.InitialFocusId
                : navigation.InitialFocusId ?? view.InitialFocusId,
            ActiveInputScopeId = navigation.InputScopeId,
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _ = Operations.RunLatest("playnite-library.warm-state",
            async context =>
            {
                await LoadWarmStateAsync(context.CancellationToken).ConfigureAwait(false);
                _ = _library.Refresh();
            },
            WidgetOperationLifetime.Active);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (current == WidgetLifecycleState.Interactive ||
            previous == WidgetLifecycleState.Interactive &&
            current == WidgetLifecycleState.Visible)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        RetirePlayniteConnection(clearPresentation: false);
        lock (_gate)
        {
            _launchPersistence.Invalidate();
            _playniteAuthority = PlayniteLibraryAuthorityProjection.Empty;
        }
        _model.Update(state => state with
        {
            LaunchingSavedId = null,
            VariantSeedSavedId = null,
            OrganizationBusy = true,
            PlayniteBusy = false,
            Status = "Revalidating installed games…",
        });
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        _library.Reset(invalidate: false);
        _playniteClient?.Dispose();
        return _application.DisposeAsync();
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActionId.StartsWith(
                PlayniteLibraryCollectionPolicy.ActionPrefix, StringComparison.Ordinal))
        {
            SelectCollection(action.ActionId);
            return;
        }
        if (action.ActionId.StartsWith("playnite-library.category.",
                StringComparison.Ordinal) &&
            !action.ActionId.StartsWith("playnite-library.category.open.",
                StringComparison.Ordinal))
        {
            await ToggleCategoryMembershipAsync(
                    action.ActionId["playnite-library.category.".Length..],
                    action.SourceElementId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var routeBeforeBack = _navigation.Value.Route;
        if (_navigation.TryHandleBack(action, action.SourceElementId))
        {
            if (routeBeforeBack == PlayniteLibraryRoute.PlayniteConnection)
                RetirePlayniteConnection();
            await ReturnToLibraryAsync().ConfigureAwait(false);
            return;
        }
        if (_library.TryHandlePagination(action, out _))
        {
            Operations.Cancel("playnite-library.launch-lifecycle");
            return;
        }
        switch (action.ActionId)
        {
            case "playnite-library.refresh-source":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    ResolveActionSource(action.SourceElementId) is null) return;
                Operations.Cancel("playnite-library.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case "playnite-library.previous":
                TryMovePage(action, WidgetCursorDirection.Before);
                return;
            case "playnite-library.next":
                TryMovePage(action, WidgetCursorDirection.After);
                return;
            case "playnite-library.refresh":
                Operations.Cancel("playnite-library.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case "playnite-library.retry":
                _ = _library.Retry();
                return;
            case "playnite-library.launch":
                if (ResolveActionSource(action.SourceElementId) is { } launchSource)
                    await LaunchAsync(launchSource, cancellationToken).ConfigureAwait(false);
                return;
            case "playnite-library.favorite":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } favoriteSource)
                    await ToggleFavoriteAsync(favoriteSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "playnite-library.hide":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                        PlayniteLibraryRoute.Category) ||
                    ResolveActionSource(action.SourceElementId) is not { } hideSource) return;
                if (await SetHiddenAsync(hideSource, hidden: true, cancellationToken)
                        .ConfigureAwait(false))
                {
                    _model.Update(state => state with { PreferLibraryContentFocus = true });
                }
                return;
            case "playnite-library.restore":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Hidden) return;
                await SetHiddenAsync(action.SourceElementId, hidden: false, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "playnite-library.variant":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } variantSource)
                    await ToggleVariantAsync(variantSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "playnite-library.prefer":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } preferSource)
                    await PreferVariantAsync(preferSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "playnite-library.organization.reset":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(PlayniteLibraryOrganizationPolicy.Clear,
                    "Organization cleared", cancellationToken).ConfigureAwait(false);
                return;
            case PlayniteOpenActionId:
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Value.Route == PlayniteLibraryRoute.Management)
                    _navigation.Back(action.SourceElementId);
                if (_navigation.Push(PlayniteLibraryRoute.PlayniteConnection,
                        action.SourceElementId) == WidgetNavigationResult.Changed)
                    await RefreshPlayniteConnectionAsync(cancellationToken)
                        .ConfigureAwait(false);
                return;
            case PlayniteBackActionId:
                if (_navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
                RetirePlayniteConnection();
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case PlayniteRefreshActionId:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
                await RefreshPlayniteConnectionAsync(cancellationToken).ConfigureAwait(false);
                return;
            case PlayniteSaveActionId when action.CommittedText is { } token:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
                await SavePlayniteCredentialAsync(token, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case PlayniteDeleteActionId:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
                await DeletePlayniteCredentialAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "playnite-library.hidden.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(PlayniteLibraryRoute.Hidden, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    _model.Update(state => state with
                    {
                        Collection = state.Collection.Reset(InstalledGames),
                        FixedRows = PlayniteLibraryFixedRows.Empty,
                        FixedRowsRevision = state.FixedRowsRevision + 1,
                        PendingRestoredSavedId = null,
                        PreferLibraryContentFocus = false,
                    });
                    ReloadQuery();
                }
                return;
            case "playnite-library.hidden.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "playnite-library.recent.clear":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(PlayniteLibraryOrganizationPolicy.ClearRecent,
                    "Recent launches cleared", cancellationToken).ConfigureAwait(false);
                _model.Update(state => state.Collection.Selection.Kind ==
                        PlayniteLibraryCollectionKind.Recent
                    ? state with
                    {
                        Collection = state.Collection.Select(
                            PlayniteLibraryCollectionPolicy.AllInstalled, InstalledGames),
                    }
                    : state);
                ReloadQuery();
                return;
            case "playnite-library.search.commit":
                if (action.CommittedText is null) return;
                _model.Update(state => state with { SearchExpanded = true });
                ReplaceQuery(_model.Value.Collection.Query with
                {
                    SearchText = NormalizeSearch(action.CommittedText),
                });
                return;
            case "playnite-library.query.clear":
                _model.Update(state => state with
                {
                    SearchExpanded = false,
                    Collection = _navigation.Value.Route == PlayniteLibraryRoute.Library
                        ? state.Collection.ClearQuery(InstalledGames)
                        : state.Collection.Reset(InstalledGames),
                });
                ReloadQuery();
                return;
            case "playnite-library.search.open":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                        PlayniteLibraryRoute.Browse)) return;
                if (_navigation.Value.Route == PlayniteLibraryRoute.Library &&
                    _navigation.Push(PlayniteLibraryRoute.Browse, action.SourceElementId) !=
                        WidgetNavigationResult.Changed) return;
                _model.Update(state => state with { SearchExpanded = true });
                return;
            case "playnite-library.browse.open":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                if (_navigation.Push(PlayniteLibraryRoute.Browse, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                    _model.Update(state => state with { SearchExpanded = false });
                return;
            case "playnite-library.browse.back":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Browse) return;
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync(preferContentFocus: true).ConfigureAwait(false);
                return;
            case "playnite-library.filter.favorites":
                if (!TryOpenBrowse(action.SourceElementId)) return;
                _model.Update(state => state with
                {
                    Collection = state.Collection.ToggleFavorites(InstalledGames),
                });
                ReloadQuery();
                return;
            case "playnite-library.filter.recent":
                if (!TryOpenBrowse(action.SourceElementId)) return;
                _model.Update(state => state with
                {
                    Collection = state.Collection.CycleRecent(InstalledGames),
                });
                ReloadQuery();
                return;
            case "playnite-library.filter.source":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Browse) return;
                ReplaceQuery(_model.Value.Collection.Query with
                {
                    SourceAttribution = NextSource(),
                });
                return;
            case "playnite-library.filter.sort":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Browse) return;
                _model.Update(state => state with
                {
                    Collection = state.Collection with
                    {
                        Query = state.Collection.Query with
                        {
                            Sort = state.Collection.Query.Sort switch
                            {
                                WidgetAppLibrarySortOrder.DisplayName =>
                                    WidgetAppLibrarySortOrder.DisplayNameDescending,
                                WidgetAppLibrarySortOrder.DisplayNameDescending =>
                                    WidgetAppLibrarySortOrder.SourceThenDisplayName,
                                _ => WidgetAppLibrarySortOrder.DisplayName,
                            },
                        },
                    },
                });
                ReloadQuery();
                return;
            case "playnite-library.categories.open":
                OpenCategories(action.SourceElementId);
                return;
            case "playnite-library.management.open":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                _navigation.Push(PlayniteLibraryRoute.Management, action.SourceElementId);
                return;
            case "playnite-library.management.back":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Management) return;
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync(preferContentFocus: true).ConfigureAwait(false);
                return;
            case "playnite-library.collection.previous":
                await SwitchCollectionAsync(
                        PlayniteLibraryCollectionDirection.Previous,
                        action.SourceElementId)
                    .ConfigureAwait(false);
                return;
            case "playnite-library.collection.next":
                await SwitchCollectionAsync(
                        PlayniteLibraryCollectionDirection.Next,
                        action.SourceElementId)
                    .ConfigureAwait(false);
                return;
            case "playnite-library.categories.back":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Categories) return;
                _model.Update(state => state with { PreferLibraryContentFocus = true });
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    await ReturnToLibraryAsync(preferContentFocus: true)
                        .ConfigureAwait(false);
                }
                return;
            case "playnite-library.category.back":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Category) return;
                _model.Update(state => state with
                {
                    ActiveCategoryId = null,
                    PreferLibraryContentFocus = true,
                });
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    await ReturnToLibraryAsync(preferContentFocus: true)
                        .ConfigureAwait(false);
                }
                return;
            case "playnite-library.category.create":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Categories ||
                    action.CommittedText is null) return;
                await CreateCategoryAsync(action.CommittedText, cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
        const string categoryOpenPrefix = "playnite-library.category.open.";
        if (action.ActionId.StartsWith(categoryOpenPrefix, StringComparison.Ordinal))
        {
            OpenCategory(action.ActionId[categoryOpenPrefix.Length..], action.SourceElementId);
            return;
        }
    }

    private void TryMovePage(
        WidgetActionEvent action,
        WidgetCursorDirection direction)
    {
        var expectedButtonId = direction == WidgetCursorDirection.Before
            ? "playnite-library.previous"
            : "playnite-library.next";
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            action.SourceElementId is not PlayniteLibraryPresentation.ScrollId &&
            action.SourceElementId is not PlayniteLibraryPresentation.HomeRailId &&
            action.SourceElementId != expectedButtonId)
            return;
        var snapshot = _library.Snapshot;
        if (snapshot.Status != WidgetPagedResourceStatus.Ready ||
            direction == WidgetCursorDirection.Before && !snapshot.HasBefore ||
            direction == WidgetCursorDirection.After && !snapshot.HasAfter)
            return;
        Operations.Cancel("playnite-library.launch-lifecycle");
        _ = _library.Move(direction, PlayniteLibraryPresentation.ScrollId);
    }

    private static string? NormalizeSearch(string value)
    {
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private string? NextSource()
    {
        var local = _model.Value;
        lock (_gate)
        {
            var choices = ProvenSourcesLocked();
            if (choices.Length == 0) return null;
            if (local.Collection.Query.SourceAttribution is null) return choices[0];
            var index = Array.FindIndex(choices, value => string.Equals(
                value, local.Collection.Query.SourceAttribution,
                StringComparison.OrdinalIgnoreCase));
            return index < 0 || index + 1 == choices.Length ? null : choices[index + 1];
        }
    }

    private void SelectCollection(string actionId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
        PlayniteLibraryPrivateState organization;
        string[] provenSources;
        lock (_gate)
        {
            organization = PresentationOrganizationLocked();
            provenSources = ProvenSourcesLocked();
        }
        var update = _model.Update(state =>
        {
            var selected = PlayniteLibraryCollectionPolicy.Resolve(actionId,
                PlayniteLibraryCollectionPolicy.Options(
                    organization, provenSources, state.Collection.Selection));
            return selected is null || selected == state.Collection.Selection
                ? state
                : state with
                {
                    Collection = state.Collection.Select(selected, InstalledGames),
                };
        });
        if (!update.Changed) return;
        ReloadQuery(preserveContentFocus: true);
    }

    private bool TryOpenBrowse(string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return false;
        if (_navigation.Value.Route == PlayniteLibraryRoute.Browse) return true;
        return _navigation.Value.Route == PlayniteLibraryRoute.Library &&
            _navigation.Push(PlayniteLibraryRoute.Browse, sourceElementId) ==
                WidgetNavigationResult.Changed;
    }

    private string[] ProvenSourcesLocked() =>
        _organization.ProvenSources.ToArray();

    private void ReplaceQuery(WidgetAppLibraryQuery query, bool force = false)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var update = _model.Update(state => !force && query == state.Collection.Query
            ? state
            : state with { Collection = state.Collection with { Query = query } });
        if (!update.Changed) return;
        ReloadQuery();
    }

    private WidgetOperationHandle? ReloadQuery(bool preserveContentFocus = false)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return null;
        Operations.Cancel("playnite-library.launch-lifecycle");
        _launchPersistence.Invalidate();
        _model.Update(state => state with
        {
            Status = "Applying library filters…",
            LaunchingSavedId = null,
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            PreferLibraryContentFocus = preserveContentFocus &&
                state.PreferLibraryContentFocus,
        });
        _library.Reset(invalidate: false);
        var operation = _library.EnsureLoaded();
        return operation;
    }

    private async Task ReloadQueryAsync(bool preserveContentFocus = false)
    {
        var operation = ReloadQuery(preserveContentFocus);
        if (operation is { } admitted)
            await admitted.Completion.ConfigureAwait(false);
    }

    private async Task ReturnToLibraryAsync(bool preferContentFocus = true)
    {
        _model.Update(state => state with
        {
            Collection = state.Collection.Reset(InstalledGames),
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            ActiveCategoryId = null,
            PreferLibraryContentFocus = preferContentFocus,
        });
        await ReloadQueryAsync(preferContentFocus).ConfigureAwait(false);
        var restored = _model.Update(state =>
            (state with { PendingRestoredSavedId = null }, state.PendingRestoredSavedId));
        var restoredSavedId = restored.Result;
        var restoredItem = restoredSavedId is null ? null : _library.Snapshot.Items
            .FirstOrDefault(item => string.Equals(
                item.Value.SavedId, restoredSavedId, StringComparison.Ordinal));
        if (restoredItem is not null)
            _library.SelectAnchor(restoredItem.Key, invalidate: false);
        _model.Update(state => state with
        {
            PreferLibraryContentFocus = preferContentFocus || restoredSavedId is not null,
        });
    }

    private WidgetAppLibraryQuery EffectiveQueryLocked(
        PlayniteLibraryRoute route,
        PlayniteLibraryRenderState local)
    {
        var organization = PresentationOrganizationLocked();
        IEnumerable<string>? savedIds = route == PlayniteLibraryRoute.Hidden
            ? organization.ExcludedSavedIds
            : null;
        if (route == PlayniteLibraryRoute.Category)
            savedIds = PlayniteLibraryCategoryPolicy.Find(
                organization, local.ActiveCategoryId)?.SavedIds ?? [];
        if (local.Collection.FavoriteFilter) savedIds = organization.FavoriteSavedIds;
        if (local.Collection.RecentMode == PlayniteLibraryRecentMode.RecentOnly)
            savedIds = savedIds is null
                ? _organization.RecentSavedIds
                : savedIds.Intersect(_organization.RecentSavedIds, StringComparer.Ordinal);
        if (local.Collection.ManualFilter)
            savedIds = savedIds is null
                ? _organization.ManualSavedIds
                : savedIds.Intersect(_organization.ManualSavedIds, StringComparer.Ordinal);
        return local.Collection.Query with
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
            var stored = await _application.ReadStateAsync(cancellationToken)
                .ConfigureAwait(false);
            var normalized = PlayniteLibraryOrganizationPolicy.Normalize(
                stored.Exists ? stored.Value : null);
            var revision = stored.Revision;
            if (stored.Exists && (ReferenceEquals(
                    normalized, PlayniteLibraryPrivateState.Empty) ||
                PlayniteLibraryCategoryPolicy.RequiresReset(stored.Value) ||
                PlayniteLibraryTitlePolicy.RequiresReset(stored.Value)))
            {
                var reset = await _application.WriteStateAsync(
                        normalized, stored.Revision, cancellationToken)
                    .ConfigureAwait(false);
                revision = reset.Revision;
            }
            lock (_gate)
            {
                _organization = normalized;
                _stateRevision = revision;
            }
            _model.Update(state => state with
            {
                Status = normalized.Items.Count == 0
                    ? "Loading installed games…"
                    : $"Checking {normalized.Items.Count} saved display rows…",
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lock (_gate) _organization = PlayniteLibraryPrivateState.Empty;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async ValueTask<WidgetCursorPage<PlayniteLibraryItem>> LoadPageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        CancellationToken cancellationToken)
    {
        var requestCursor = direction is null ? null : cursor;
        WidgetAppLibraryQuery query;
        PlayniteLibraryCollectionState collectionState;
        PlayniteLibraryPrivateState organization;
        var route = _navigation.Value.Route;
        var local = _model.Value;
        lock (_gate)
        {
            collectionState = local.Collection;
            query = EffectiveQueryLocked(route, local);
            organization = PresentationOrganizationLocked();
        }
        if (route == PlayniteLibraryRoute.Hidden && organization.ExcludedSavedIds.Count == 0)
        {
            _model.Update(state => state with { Status = "No hidden games" });
            return new([], null, null);
        }
        if (route == PlayniteLibraryRoute.Category &&
            PlayniteLibraryCategoryPolicy.Find(organization, local.ActiveCategoryId) is not
                { SavedIds.Count: > 0 })
        {
            _model.Update(state => state with { Status = "Category is empty" });
            return new([], null, null);
        }
        var categoryName = route == PlayniteLibraryRoute.Category
            ? PlayniteLibraryCategoryPolicy.Find(organization, local.ActiveCategoryId)?.Name
            : null;
        var result = await _application.QueryWithAuthorityAsync(
                query,
                new(route switch
                {
                    PlayniteLibraryRoute.Hidden => PlayniteLibraryQueryScope.Hidden,
                    PlayniteLibraryRoute.Category => PlayniteLibraryQueryScope.Category,
                    _ => PlayniteLibraryQueryScope.Library,
                }, categoryName),
                requestCursor,
                direction,
                limit,
                refresh: direction is null,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var page = result.Page;
        lock (_gate)
        {
            _playniteAuthority = result.Authority;
            organization = PresentationOrganizationLocked();
        }
        var rawItems = page.Items.Select(PlayniteLibraryItem.From).ToArray();
        var fixedRows = PlayniteLibraryFixedRows.Empty;
        var rawFixedRows = PlayniteLibraryFixedRows.Empty;
        if (direction is null)
        {
            var titleMatchIds = PlayniteLibraryTitlePolicy.SearchMatches(
                organization, query.SearchText);
            var fixedSavedIds = titleMatchIds
                .Concat(route == PlayniteLibraryRoute.Library
                    ? collectionState.ManualFilter
                        ? organization.ManualSavedIds
                        : organization.RecentSavedIds.Concat(organization.ManualSavedIds)
                    : [])
                .Distinct(StringComparer.Ordinal)
                .Take(WidgetAppLibraryService.MaximumSavedItems)
                .ToArray();
            IReadOnlyList<WidgetAppLibraryItem> resolved = fixedSavedIds.Length == 0
                ? Array.Empty<WidgetAppLibraryItem>()
                : await _application.ResolveSavedAsync(
                    fixedSavedIds, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var resolvedBySavedId = resolved.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var automaticManualGames = route == PlayniteLibraryRoute.Library
                ? organization.ManualSavedIds
                .Where(savedId => resolvedBySavedId.TryGetValue(savedId, out var item) &&
                    item.Presentation.Kind == WidgetAppLibraryKind.Game)
                .ToHashSet(StringComparer.Ordinal)
                : [];
            if (automaticManualGames.Count != 0)
            {
                await SaveStateAsync(
                    state => PlayniteLibraryOrganizationPolicy.RemoveAutomaticManualGames(
                        state, automaticManualGames), cancellationToken).ConfigureAwait(false);
                lock (_gate) organization = _organization;
            }
            PlayniteLibraryItem[] recent = route != PlayniteLibraryRoute.Library ||
                collectionState.ManualFilter ||
                collectionState.RecentMode == PlayniteLibraryRecentMode.Off
                ? []
                : organization.RecentSavedIds
                    .Select(savedId => resolvedBySavedId.GetValueOrDefault(savedId))
                    .OfType<WidgetAppLibraryItem>()
                    .Where(item => MatchesFixedQuery(
                        PlayniteLibraryTitlePolicy.Project(organization, item), query))
                    .Select(PlayniteLibraryItem.From)
                    .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
                    .ToArray();
            PlayniteLibraryItem[] manual = route != PlayniteLibraryRoute.Library ||
                collectionState.RecentMode == PlayniteLibraryRecentMode.RecentOnly
                ? []
                : organization.ManualSavedIds
                    .Select(savedId => resolvedBySavedId.GetValueOrDefault(savedId))
                    .OfType<WidgetAppLibraryItem>()
                    .Where(item =>
                        item.Presentation.Kind != WidgetAppLibraryKind.Game &&
                        MatchesFixedQuery(
                            PlayniteLibraryTitlePolicy.Project(organization, item), query))
                    .Select(PlayniteLibraryItem.From)
                    .Take(PlayniteLibraryPrivateState.MaximumManualItems)
                    .ToArray();
            var occupied = recent.Concat(manual).Select(item => item.Value.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            var titleMatches = titleMatchIds
                .Where(savedId => !occupied.Contains(savedId))
                .Select(savedId => resolvedBySavedId.GetValueOrDefault(savedId))
                .OfType<WidgetAppLibraryItem>()
                .Select(item => PlayniteLibraryTitlePolicy.Project(organization, item))
                .Where(item => MatchesFixedQuery(item, query))
                .Select(PlayniteLibraryItem.From)
                .Take(WidgetAppLibraryService.MaximumSavedItems)
                .ToArray();
            rawFixedRows = new(recent, manual,
                titleMatches.Select(item => resolvedBySavedId[item.Value.SavedId])
                    .Select(PlayniteLibraryItem.From).ToArray());
            fixedRows = new(
                recent.Select(item => item.WithValue(
                    PlayniteLibraryTitlePolicy.Project(organization, item.Value))).ToArray(),
                manual.Select(item => item.WithValue(
                    PlayniteLibraryTitlePolicy.Project(organization, item.Value))).ToArray(),
                titleMatches);
        }
        var retainedForProjection = direction is null
            ? rawFixedRows
            : PlayniteLibraryFixedRows.Empty;
        var projectionItems = rawItems.Concat(retainedForProjection.All)
            .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
            .ToArray();
        string[]? provenSources = null;
        if (route == _navigation.Value.Route)
            provenSources = _model.Update(state =>
            {
                if (state.Collection != collectionState) return (state, (string[]?)null);
                var reconciled = PlayniteLibrarySourceCatalog.Reconcile(
                    organization.ProvenSources, collectionState, direction, rawItems,
                    page.Sources, page.Before is null && page.After is null);
                return (state with { SourceObservations = page.Sources.ToArray() },
                    (string[]?)reconciled);
            }).Result;
        var projectionSaved = await PersistProjectionAsync(
                projectionItems, provenSources, cancellationToken)
            .ConfigureAwait(false);
        var items = rawItems.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(organization, item.Value)))
            .Where(item => MatchesFixedQuery(item.Value, query))
            .ToArray();
        _model.Update(state =>
        {
            var current = direction is null && route == _navigation.Value.Route &&
                state.Collection == collectionState
                ? state with
                {
                    FixedRows = fixedRows,
                    FixedRowsRevision = state.FixedRowsRevision + 1,
                }
                : state;
            return current with
            {
                Status = !projectionSaved
                    ? "Games loaded · organization was not saved"
                    : items.Length == 0 && fixedRows.All.Any() ? "Saved games resolved" :
                        items.Length == 0 ? "No installed games" :
                        $"{items.Length}{(page.After is null ? string.Empty : "+")} games in the current catalog window",
            };
        });
        return new(items,
            page.Before is null ? null : new WidgetCollectionCursor(page.Before),
            page.After is null ? null : new WidgetCollectionCursor(page.After));
    }

    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default) =>
        _application.OwnsArtworkContent
            ? _application.ResolveArtworkAsync(handle, cancellationToken)
            : ValueTask.FromResult<WidgetEncodedArtwork?>(null);

    private async Task LaunchAsync(string sourceElementId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var generationReady = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = Operations.RunLatest(
            "playnite-library.launch-lifecycle",
            context => new ValueTask(LaunchAfterAdmissionAsync(
                sourceElementId, generationReady.Task,
                context.CancellationToken, cancellationToken)),
            WidgetOperationLifetime.Active);
        if (!_launchPersistence.CompleteAdmission(handle, generationReady)) return;
        await handle.Completion.ConfigureAwait(false);
    }

    private async Task LaunchAfterAdmissionAsync(
        string sourceElementId,
        Task<long> generationReady,
        CancellationToken activeLifetime,
        CancellationToken requestCancellation)
    {
        var generation = await generationReady.WaitAsync(activeLifetime).ConfigureAwait(false);
        await LaunchCoreAsync(sourceElementId, generation, activeLifetime, requestCancellation)
            .ConfigureAwait(false);
    }

    private async Task LaunchCoreAsync(
        string sourceElementId,
        long generation,
        CancellationToken activeLifetime,
        CancellationToken requestCancellation)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation, activeLifetime);
        PlayniteLibraryItem? selected = null;
        long collectionRevision = 0;
        long fixedRowsRevision = 0;
        try
        {
            var local = _model.Value;
            var fixedRows = local.FixedRows;
            fixedRowsRevision = local.FixedRowsRevision;
            selected = _library.Snapshot.Items.Concat(fixedRows.All).FirstOrDefault(item =>
                string.Equals(
                PlayniteLibraryIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
            if (selected is null) return;
            if (_library.Snapshot.Items.Any(item => item.Key == selected.Key))
                _library.SelectAnchor(selected.Key, invalidate: false);
            lock (_gate)
            {
                RemoveLaunchStateLocked(selected.Value.SavedId);
            }
            _model.Update(state => state with
            {
                LaunchingSavedId = selected.Value.SavedId,
                Status = $"Pending · {selected.Value.Presentation.DisplayName}",
            });
            collectionRevision = _library.Snapshot.Revision;
            var resolved = await _application.ResolveSavedAsync(
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
            var observation = await _application.LaunchObservedAsync(
                    current.AppId,
                    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
                    lifetime.Token).ConfigureAwait(false);
            var accepted = false;
            lock (_gate)
            {
                if (_launchPersistence.IsCurrent(generation) &&
                    collectionRevision == _library.Snapshot.Revision &&
                    IsCurrentResolved(selected.Key))
                {
                    var modelUpdate = _model.Update(state =>
                        fixedRowsRevision != state.FixedRowsRevision
                            ? (state, false)
                            : (state with
                            {
                                Status = LaunchStatus(
                                    selected.Value.Presentation.DisplayName,
                                    observation.State),
                            }, true));
                    accepted = modelUpdate.Result;
                    if (accepted)
                        SetLaunchStateLocked(selected.Value.SavedId,
                            ToLaunchState(observation.State));
                }
            }
            if (accepted)
                await _launchPersistence.CommitRecentAsync(
                    generation,
                    observation.State,
                    new PlayniteLibraryDisplayItem(selected.Value.SavedId,
                        selected.Value.Presentation.DisplayName,
                        selected.Value.Presentation.Source.DisplayName),
                    SaveStateAsync,
                    CaptureCurrentRecent,
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
                if (selected is not null && _launchPersistence.IsCurrent(generation) &&
                    collectionRevision == _library.Snapshot.Revision &&
                    IsCurrentResolved(selected.Key))
                {
                    var modelUpdate = _model.Update(state =>
                        fixedRowsRevision != state.FixedRowsRevision
                            ? (state, false)
                            : (state with
                            {
                                Status = $"Failed · {LaunchError(exception)}",
                            }, true));
                    if (modelUpdate.Result)
                        SetLaunchStateLocked(
                            selected.Value.SavedId, PlayniteLibraryLaunchState.Failed);
                }
            }
        }
        finally
        {
            lock (_gate)
                if (_launchPersistence.IsCurrent(generation))
                    _model.Update(state => state with { LaunchingSavedId = null });
        }
    }

    private IReadOnlyList<string> CaptureCurrentRecent()
    {
        lock (_gate) return _organization.RecentSavedIds.ToArray();
    }

    private void SetLaunchStateLocked(string savedId, PlayniteLibraryLaunchState state)
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

    private static PlayniteLibraryLaunchState ToLaunchState(
        WidgetAppLaunchObservationState state) => state switch
        {
            WidgetAppLaunchObservationState.RequestAccepted =>
                PlayniteLibraryLaunchState.RequestAccepted,
            WidgetAppLaunchObservationState.LauncherStarted =>
                PlayniteLibraryLaunchState.LauncherStarted,
            WidgetAppLaunchObservationState.Running => PlayniteLibraryLaunchState.Running,
            WidgetAppLaunchObservationState.Ended => PlayniteLibraryLaunchState.Ended,
            _ => PlayniteLibraryLaunchState.RequestAccepted,
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
        lock (_gate)
        {
            favorite = !_playniteAuthority.FavoriteGameIds.Contains(
                display.SavedId, StringComparer.Ordinal);
        }
        var admitted = _model.Update(state =>
            (state with { OrganizationBusy = true }, state.Collection.FavoriteFilter));
        var filteringFavorites = admitted.Result;
        var applied = false;
        string? status = null;
        try
        {
            applied = await _application.SetFavoriteAsync(
                    display.SavedId, favorite, cancellationToken).ConfigureAwait(false) is not null;
            if (applied)
                lock (_gate)
                {
                    var values = _playniteAuthority.FavoriteGameIds
                        .Where(value => value != display.SavedId).ToList();
                    if (favorite) values.Add(display.SavedId);
                    _playniteAuthority = _playniteAuthority with
                    {
                        FavoriteGameIds = values,
                    };
                    status = favorite
                        ? $"Favorited {display.DisplayName}"
                        : $"Removed {display.DisplayName} from favorites";
                }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
            });
        }
        if (applied && filteringFavorites) ReloadQuery();
    }

    private async Task<bool> SetHiddenAsync(
        string sourceElementId,
        bool hidden,
        CancellationToken cancellationToken)
    {
        PlayniteLibraryDisplayItem? display;
        if (hidden)
        {
            display = DisplayForSource(sourceElementId);
        }
        else
        {
            lock (_gate)
            {
                var savedId = _playniteAuthority.HiddenGameIds.FirstOrDefault(candidate =>
                    string.Equals(PlayniteLibraryIdentity.FocusId(
                            "hidden", PlayniteLibraryIdentity.Key(candidate)), sourceElementId,
                        StringComparison.Ordinal));
                display = savedId is null ? null : DisplayForSavedLocked(savedId);
            }
        }
        if (display is null) return false;
        _model.Update(state => state with { OrganizationBusy = true });
        var applied = false;
        string? status = null;
        try
        {
            applied = await _application.SetHiddenAsync(
                    display.SavedId, hidden, cancellationToken).ConfigureAwait(false) is not null;
            if (applied)
                lock (_gate)
                {
                    var values = _playniteAuthority.HiddenGameIds
                        .Where(value => value != display.SavedId).ToList();
                    if (hidden) values.Add(display.SavedId);
                    _playniteAuthority = _playniteAuthority with { HiddenGameIds = values };
                    status = hidden
                        ? $"Hidden {display.DisplayName}"
                        : $"Restored {display.DisplayName}";
                }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
            });
        }
        if (applied)
        {
            if (!hidden)
                _model.Update(state => state with
                {
                    PendingRestoredSavedId = display.SavedId,
                });
            await ReloadQueryAsync().ConfigureAwait(false);
        }
        return applied;
    }

    private async Task CycleCompletionStatusAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var display = DisplayForSource(sourceElementId);
        if (display is null) return;
        _model.Update(state => state with { OrganizationBusy = true });
        string? status = null;
        try
        {
            var statuses = await _application.GetCompletionStatusesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (statuses.Count == 0)
            {
                status = "No Playnite completion statuses are available";
                return;
            }
            string? current;
            lock (_gate) current = _playniteAuthority.CompletionStatuses
                .GetValueOrDefault(display.SavedId);
            var index = current is null ? -1 : statuses.ToList().FindIndex(value =>
                string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
            var next = statuses[(index + 1) % statuses.Count];
            var changed = await _application.SetCompletionStatusAsync(
                    display.SavedId, next, cancellationToken).ConfigureAwait(false);
            if (changed is null) return;
            lock (_gate)
            {
                var values = new Dictionary<string, string?>(
                    _playniteAuthority.CompletionStatuses, StringComparer.Ordinal)
                {
                    [display.SavedId] = next,
                };
                _playniteAuthority = _playniteAuthority with
                {
                    CompletionStatuses = values,
                };
                status = $"Completion · {next}";
            }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
            });
        }
    }

    private async Task ToggleManualAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var item = _library.Snapshot.Items.FirstOrDefault(candidate => string.Equals(
            PlayniteLibraryIdentity.FocusId("add", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        if (item is null) return;
        if (item.Presentation.Kind == WidgetAppLibraryKind.Game)
        {
            _model.Update(state => state with
            {
                Status = "Games are included automatically",
            });
            return;
        }
        await SetManualCurrentAsync(item.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetManualCurrentAsync(
        WidgetAppLibraryItem item,
        CancellationToken cancellationToken)
    {
        if (item.Presentation.Kind == WidgetAppLibraryKind.Game)
        {
            _model.Update(state => state with
            {
                Status = "Games are included automatically",
            });
            return;
        }
        var display = new PlayniteLibraryDisplayItem(item.SavedId,
            item.Presentation.DisplayName, item.Presentation.Source.DisplayName);
        bool included;
        lock (_gate)
        {
            if (_organization.RecentSavedIds.Contains(display.SavedId, StringComparer.Ordinal) ||
                _organization.ExcludedSavedIds.Contains(display.SavedId, StringComparer.Ordinal))
            {
                _model.Update(state => state with
                {
                    Status = "App is already retained in the library",
                });
                return;
            }
            included = _organization.ManualSavedIds.Contains(
                display.SavedId, StringComparer.Ordinal);
        }
        await MutateOrganizationAsync(
            state => PlayniteLibraryOrganizationPolicy.SetManual(state, display, !included),
            included ? $"Removed {display.DisplayName}" : $"Added {display.DisplayName}",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlayniteLibraryVariantActionResult> ToggleVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var current = DisplayForSource(sourceElementId);
        if (current is null) return PlayniteLibraryVariantActionResult.Rejected;
        var transition = _model.Update(state => state.VariantSeedSavedId is null
            ? (state with
            {
                VariantSeedSavedId = current.SavedId,
                Status = $"Variant selection started with {current.DisplayName}",
            }, (Started: true, Seed: (string?)null))
            : (state with { VariantSeedSavedId = null },
                (Started: false, Seed: state.VariantSeedSavedId)));
        if (transition.Result.Started)
            return PlayniteLibraryVariantActionResult.Started;

        PlayniteLibraryDisplayItem? first;
        PlayniteLibraryVariantGroup? existing;
        lock (_gate)
        {
            first = DisplayForCurrentSavedLocked(transition.Result.Seed!);
            existing = first is null ? null : _organization.VariantGroups.FirstOrDefault(group =>
                group.SavedIds.Contains(first.SavedId, StringComparer.Ordinal) &&
                group.SavedIds.Contains(current.SavedId, StringComparer.Ordinal));
        }
        if (first is null || first.SavedId == current.SavedId)
        {
            _model.Update(state => state with
            {
                Status = first is null
                    ? "The first selected game is no longer available"
                    : "Choose a different game for the second variant",
            });
            return PlayniteLibraryVariantActionResult.Rejected;
        }
        await MutateOrganizationAsync(
            existing is null
                ? state => PlayniteLibraryOrganizationPolicy.Pair(state, first, current)
                : state => PlayniteLibraryOrganizationPolicy.Unmerge(
                    state, existing.Id, current.SavedId),
            existing is null
                ? $"Grouped {first.DisplayName} with {current.DisplayName}"
                : $"Removed {current.DisplayName} from its variant group",
            cancellationToken).ConfigureAwait(false);
        return PlayniteLibraryVariantActionResult.Completed;
    }

    private async Task PreferVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var display = DisplayForSource(sourceElementId);
        if (display is null) return;
        PlayniteLibraryVariantGroup? group;
        lock (_gate) group = PlayniteLibraryOrganizationPolicy.GroupFor(
            _organization, display.SavedId);
        if (group is null)
        {
            _model.Update(state => state with
            {
                Status = "Group variants before choosing a preferred launch",
            });
            return;
        }
        await MutateOrganizationAsync(
            state => PlayniteLibraryOrganizationPolicy.Prefer(state, group.Id, display.SavedId),
            $"Preferred variant: {display.DisplayName}", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> PersistProjectionAsync(
        IReadOnlyList<PlayniteLibraryItem> items,
        IReadOnlyList<string>? provenSources,
        CancellationToken cancellationToken) => await SaveStateAsync(
            state => PlayniteLibraryStateMutation.Apply(
                PlayniteLibraryOrganizationPolicy.ProjectPage(state, items) with
                {
                    ProvenSources = provenSources ?? state.ProvenSources,
                }),
            cancellationToken).ConfigureAwait(false);

    private async Task<bool> MutateOrganizationAsync(
        Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
        string success,
        CancellationToken cancellationToken,
        string failure = "Organization change was not saved")
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        _model.Update(state => state with { OrganizationBusy = true });
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
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = LifecycleState is WidgetLifecycleState.Visible or
                    WidgetLifecycleState.Interactive
                    ? saved ? success : failure
                    : state.Status,
            });
        }
        return saved;
    }

    private async Task<bool> SaveStateAsync(
        Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlayniteLibraryPrivateState baseline;
            long revision;
            lock (_gate)
            {
                baseline = _organization;
                revision = _stateRevision;
            }
            var saved = await PlayniteLibraryStateStore.SaveAsync(
                    apply,
                    (state, expected, token) => _application.WriteStateAsync(
                        state, expected, token),
                    token => _application.ReadStateAsync(token),
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

    private PlayniteLibraryDisplayItem? DisplayForSource(string sourceElementId)
    {
        var fixedRows = _model.Value.FixedRows;
        var item = _library.Snapshot.Items.Concat(fixedRows.All)
            .FirstOrDefault(candidate => string.Equals(
            PlayniteLibraryIdentity.FocusId("grid", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        return item is null ? null : new(
            item.Value.SavedId,
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName);
    }

    private PlayniteLibraryPresentationState CapturePresentationStateLocked(
        PlayniteLibraryRoute route,
        PlayniteLibraryRenderState local)
    {
        var organization = PlayniteLibraryTitlePolicy.Project(
            PresentationOrganizationLocked());
        var collection = _library.Snapshot with
        {
            Items = _library.Snapshot.Items.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
        };
        var fixedRows = new PlayniteLibraryFixedRows(
            local.FixedRows.Recent.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            local.FixedRows.Manual.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            local.FixedRows.TitleMatches.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray());
        return new(
        collection,
        organization,
        StatusLocked(_library.Snapshot, local),
        local.LaunchingSavedId,
        new Dictionary<string, PlayniteLibraryLaunchState>(
            _launchStates, StringComparer.Ordinal),
        local.OrganizationBusy,
        LifecycleState == WidgetLifecycleState.Interactive,
        local.Collection.Query,
        local.Collection.RecentMode,
        local.Collection.FavoriteFilter,
        route,
        fixedRows,
        local.SourceObservations,
        local.HeroSavedId,
        local.HeroIndex)
    {
        ActiveCategoryId = local.ActiveCategoryId,
        SearchExpanded = local.SearchExpanded,
        Collections = PlayniteLibraryCollectionPolicy.Options(
            organization, ProvenSourcesLocked(), local.Collection.Selection),
        CompletionStatuses = _playniteAuthority.CompletionStatuses,
    };
    }

    private PlayniteLibraryPrivateState PresentationOrganizationLocked() =>
        _organization with
        {
            FavoriteSavedIds = _playniteAuthority.FavoriteGameIds,
            ExcludedSavedIds = _playniteAuthority.HiddenGameIds,
            Categories = _playniteAuthority.Categories,
        };

    private string? ResolveActionSource(string sourceElementId)
    {
        var item = _library.Snapshot.Items.Concat(_model.Value.FixedRows.All)
            .FirstOrDefault(candidate => string.Equals(
                PlayniteLibraryIdentity.FocusId("grid", candidate.Key), sourceElementId,
                StringComparison.Ordinal));
        return item is null ? null : sourceElementId;
    }

    private void SelectHeroForSource(string sourceElementId)
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return;
        var fixedRows = _model.Value.FixedRows;
        var selected = _library.Snapshot.Items.Concat(fixedRows.All)
            .FirstOrDefault(item => string.Equals(
                PlayniteLibraryIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
        if (selected is null) return;
        _model.Update(state => string.Equals(state.HeroSavedId,
                selected.Value.SavedId, StringComparison.Ordinal)
            ? state
            : state with { HeroSavedId = selected.Value.SavedId });
    }

    private void OpenCategories(string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                PlayniteLibraryRoute.Browse or PlayniteLibraryRoute.Management or
                PlayniteLibraryRoute.Category)) return;
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library)
            _navigation.Back(sourceElementId);
        _model.Update(state => state with
        {
            ActiveCategoryId = null,
            PreferLibraryContentFocus = false,
        });
        _navigation.Push(PlayniteLibraryRoute.Categories, sourceElementId);
    }

    private void OpenCategory(string categoryId, string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Categories) return;
        lock (_gate)
            if (PlayniteLibraryCategoryPolicy.Find(
                    PresentationOrganizationLocked(), categoryId) is null) return;
        _navigation.Back(sourceElementId);
        _model.Update(state => state with
        {
            ActiveCategoryId = categoryId,
            Collection = state.Collection.Reset(InstalledGames),
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            PreferLibraryContentFocus = false,
        });
        if (_navigation.Push(PlayniteLibraryRoute.Category, sourceElementId) ==
            WidgetNavigationResult.Changed)
            ReloadQuery();
    }

    private async Task CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalized = PlayniteLibraryCategoryPolicy.NormalizeName(name);
        if (normalized is null)
        {
            _model.Update(state => state with
            {
                Status = $"Category name must be 1–{PlayniteLibraryPrivateState.MaximumCategoryNameLength} characters",
            });
            return;
        }
        _model.Update(state => state with { OrganizationBusy = true });
        string? status = null;
        try
        {
            var created = await _application.CreateCategoryAsync(
                    normalized, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (created is not null && !_playniteAuthority.Categories.Any(category =>
                        string.Equals(category.Name, created.Name,
                            StringComparison.OrdinalIgnoreCase)))
                    _playniteAuthority = _playniteAuthority with
                    {
                        Categories = _playniteAuthority.Categories.Append(created).ToArray(),
                    };
                status = created is null
                    ? "Category was not created"
                    : $"Created category {normalized}";
            }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
            });
        }
    }

    private async Task ToggleCategoryMembershipAsync(
        string categoryId,
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            ResolveActionSource(sourceElementId) is not { } exactSource ||
            DisplayForSource(exactSource) is not { } display) return;
        bool included;
        string? categoryName;
        lock (_gate)
        {
            var category = PlayniteLibraryCategoryPolicy.Find(
                PresentationOrganizationLocked(), categoryId);
            categoryName = category?.Name;
            included = category is not null &&
                PlayniteLibraryCategoryPolicy.Contains(category, display.SavedId);
        }
        if (categoryName is null) return;
        var names = _playniteAuthority.Categories
            .Where(category => category.SavedIds.Contains(
                display.SavedId, StringComparer.Ordinal))
            .Select(category => category.Name)
            .Where(name => !string.Equals(name, categoryName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!included) names.Add(categoryName);
        _model.Update(state => state with { OrganizationBusy = true });
        string? status = null;
        try
        {
            var changed = await _application.SetCategoriesAsync(
                    display.SavedId, names, cancellationToken).ConfigureAwait(false);
            if (changed is not null)
            {
                lock (_gate)
                {
                    _playniteAuthority = _playniteAuthority with
                    {
                        Categories = _playniteAuthority.Categories.Select(category =>
                            category.Id != categoryId ? category : category with
                            {
                                SavedIds = included
                                    ? category.SavedIds.Where(value =>
                                        value != display.SavedId).ToArray()
                                    : category.SavedIds.Append(display.SavedId).ToArray(),
                            }).ToArray(),
                    };
                    status = included
                        ? $"Removed {display.DisplayName} from {categoryName}"
                        : $"Added {display.DisplayName} to {categoryName}";
                }
            }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
            });
        }
    }

    private async Task SwitchCollectionAsync(
        PlayniteLibraryCollectionDirection direction,
        string sourceElementId)
    {
        var route = _navigation.Value.Route;
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            route is not (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Category)) return;

        string? focusedSavedId = null;
        if (ResolveActionSource(sourceElementId) is { } exactSource)
            focusedSavedId = DisplayForSource(exactSource)?.SavedId;

        PlayniteLibraryPrivateState organization;
        string? currentCategoryId;
        var local = _model.Value;
        lock (_gate)
        {
            if (local.OrganizationBusy) return;
            organization = PresentationOrganizationLocked();
            currentCategoryId = route == PlayniteLibraryRoute.Category
                ? local.ActiveCategoryId
                : null;
            focusedSavedId ??= local.HeroSavedId;
        }
        if (organization.Categories.Count == 0) return;
        var targetCategoryId = PlayniteLibraryCategoryPolicy.Cycle(
            organization.Categories, currentCategoryId, direction);

        Operations.Cancel("playnite-library.launch-lifecycle");
        if (targetCategoryId is null)
        {
            if (route != PlayniteLibraryRoute.Category) return;
            _model.Update(state => state with
            {
                ActiveCategoryId = null,
                HeroSavedId = focusedSavedId,
                PreferLibraryContentFocus = true,
            });
            if (_navigation.Back(sourceElementId) == WidgetNavigationResult.Changed)
                await ReturnToLibraryAsync(preferContentFocus: true).ConfigureAwait(false);
            return;
        }

        lock (_gate)
            if (PlayniteLibraryCategoryPolicy.Find(_organization, targetCategoryId) is null)
                return;
        _model.Update(state => state with
        {
            ActiveCategoryId = targetCategoryId,
            HeroSavedId = focusedSavedId,
            Collection = state.Collection.Reset(InstalledGames),
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            PreferLibraryContentFocus = false,
        });
        if (route == PlayniteLibraryRoute.Library &&
            _navigation.Push(PlayniteLibraryRoute.Category, sourceElementId) !=
                WidgetNavigationResult.Changed)
            return;
        ReloadQuery();
    }

    private PlayniteLibraryDisplayItem? DisplayForSavedLocked(string savedId) =>
        _organization.Items.FirstOrDefault(item => item.SavedId == savedId) ??
        _library.Snapshot.Items.Where(item => item.Value.SavedId == savedId)
            .Select(item => new PlayniteLibraryDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName))
            .FirstOrDefault();

    private PlayniteLibraryDisplayItem? DisplayForCurrentSavedLocked(string savedId) =>
        _library.Snapshot.Items.Concat(_model.Value.FixedRows.All)
            .Where(item => string.Equals(item.Value.SavedId, savedId,
                StringComparison.Ordinal))
            .Select(item => new PlayniteLibraryDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName))
            .FirstOrDefault();

    private bool IsCurrentResolved(WidgetCollectionItemKey key)
    {
        if (_library.Snapshot.Items.Any(item => item.Key == key)) return true;
        return _model.Value.FixedRows.All.Any(item => item.Key == key);
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

    private string StatusLocked(
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot,
        PlayniteLibraryRenderState local) =>
        snapshot.Status switch
        {
            WidgetPagedResourceStatus.Loading when _organization.Items.Count == 0 =>
                "Loading installed games…",
            WidgetPagedResourceStatus.Refreshing => "Refreshing installed games…",
            WidgetPagedResourceStatus.LoadingAdjacent => "Loading more games…",
            WidgetPagedResourceStatus.Error when snapshot.Items.Count != 0 =>
                $"{snapshot.Items.Count} games · some sources unavailable",
            WidgetPagedResourceStatus.Error => "Installed game library unavailable",
            _ => local.Status,
        };

    private static WidgetResourceError MapError(Exception exception) => exception switch
    {
        WidgetCapabilityException capability when capability.ErrorCode is
            "permission_denied" or "capability_not_declared" or "capability_revoked" =>
            new("permission_denied", "Allow Playnite Library access in Settings."),
        WidgetCapabilityException capability when capability.ErrorCode == "lifecycle_denied" =>
            new("lifecycle_denied", "Return to Playnite Library to load installed games."),
        WidgetCapabilityException capability when capability.ErrorCode is
            "platform_unavailable" or "source_unavailable" or "offline" =>
            new("library_offline", "The installed game library is offline. Try again."),
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
            "Allow Playnite Library launch access in Settings",
        WidgetCapabilityException => "The selected game could not be opened",
        _ => "The selected game could not be opened",
    };
}
