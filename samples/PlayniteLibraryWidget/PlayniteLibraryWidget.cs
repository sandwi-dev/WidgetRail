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
    private readonly WidgetResource<IReadOnlyList<PlayniteLibraryItem>> _hiddenRows;
    private readonly WidgetResource<PlayniteBridgeConnectionResult> _playniteConnection;
    private readonly WidgetNavigator<PlayniteLibraryRoute> _navigation;
    private readonly WidgetModel<PlayniteLibraryRenderState> _model;
    private PlayniteLibraryPrivateState _organization = PlayniteLibraryPrivateState.Empty;
    private PlayniteLibraryAuthorityProjection _livePlayniteAuthority =
        PlayniteLibraryAuthorityProjection.Empty;
    private PlayniteLibraryAuthorityProjection _presentationAuthority =
        PlayniteLibraryAuthorityProjection.Empty;
    private long _queryAuthorityGeneration;
    private long _authorityRevision;
    private bool _hasLivePlayniteAuthority;
    private readonly List<PlayniteLibraryCategory>
        _createdCategoriesPendingReconciliation = [];
    private long _stateRevision;
    private readonly Dictionary<string, PlayniteLibraryLaunchState> _launchStates =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _launchStateRecency = [];
    private readonly PlayniteLibraryLaunchGenerationOwner _launchGeneration = new();
    private readonly object _pendingBackFocusGate = new();
    private long _browseReloadAttempt;
    private readonly Dictionary<(long Sequence, string ScopeId), string>
        _pendingBackFocus = [];

    internal PlayniteLibraryWidget(
        IPlayniteLibraryApplicationService application,
        IPlayniteBridgeClient? playniteClient = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _playniteClient = playniteClient;
        _model = CreateModel(PlayniteLibraryRenderState.Initial(InstalledGames));
        _navigation = CreateNavigator("playnite-library.navigation", PlayniteLibraryRoute.Library,
            maximumDepth: 2, maximumRoutes: 8);
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
                new(PlayniteLibraryPresentation.AlternateBrowseScrollId,
                    item => item.Key,
                    item => PlayniteLibraryIdentity.FocusId("grid", item.Key),
                    "playnite-library.empty.action"),
                new(PlayniteLibraryPresentation.HomeRailId, item => item.Key,
                    item => PlayniteLibraryIdentity.FocusId("grid", item.Key),
                    "playnite-library.empty.action")
                {
                    EstimatedItemExtent = 212,
                },
            ],
        });
        _hiddenRows = CreateResource<IReadOnlyList<PlayniteLibraryItem>>(
            "playnite-library.hidden", new()
            {
                Load = LoadHiddenRowsAsync,
                MapError = _ => new WidgetResourceError(
                    "hidden_unavailable", "Hidden games could not be refreshed."),
                CacheDuration = TimeSpan.Zero,
                Lifetime = WidgetOperationLifetime.Active,
                RetainLastGoodValue = true,
            });
        _playniteConnection = CreateResource<PlayniteBridgeConnectionResult>(
            "playnite-library.playnite.connection", new()
            {
                Load = LoadPlayniteConnectionAsync,
                MapError = _ => new WidgetResourceError(
                    "playnite_unavailable", "Playnite Bridge could not be checked."),
                CacheDuration = TimeSpan.Zero,
                Lifetime = WidgetOperationLifetime.Active,
                RetainLastGoodValue = true,
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
    internal Task WhenHiddenRowsIdleAsync(CancellationToken cancellationToken = default) =>
        _hiddenRows.WhenIdleAsync(cancellationToken);

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        var local = _model.Value;
        PinRenderedArtwork(navigation.Route, local);
        if (navigation.Route == PlayniteLibraryRoute.PlayniteConnection)
        {
            var connection = CapturePlayniteConnection(local);
            var connectionView = PlayniteLibraryConnectionPresentation.Render(connection);
            var connectionRoot = _navigation.Scope(navigation, connectionView.Root);
            return connectionView with
            {
                Root = connectionRoot,
                InitialFocusId = ResolveInitialFocus(
                    connectionRoot, connectionView.InitialFocusId,
                    connectionView.InitialFocusId, navigation.InputScopeId,
                    allowDisabledRequestedTarget: false),
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
        var initialFocusId = navigation.Route == PlayniteLibraryRoute.Categories ||
            navigation.Route == PlayniteLibraryRoute.Browse &&
            local.BrowseInitialFocusId is not null
            ? view.InitialFocusId
            : local.PreferLibraryContentFocus
            ? view.InitialFocusId
            : navigation.InitialFocusId ?? view.InitialFocusId;
        initialFocusId = ResolveInitialFocus(
            root, initialFocusId, view.InitialFocusId, navigation.InputScopeId,
            allowDisabledRequestedTarget: navigation.Route == PlayniteLibraryRoute.Browse);
        return view with
        {
            Root = root,
            InitialFocusId = initialFocusId,
            ActiveInputScopeId = navigation.InputScopeId,
        };
    }

    private void PinRenderedArtwork(
        PlayniteLibraryRoute route,
        PlayniteLibraryRenderState local)
    {
        var live = _library.Snapshot.Items;
        var fixedRows = local.FixedRows.All.ToArray();
        var retainedHome = local.RetainedHomeCollection?.Items ?? [];
        var retainedBrowse = local.ActiveBrowseReload?.RetainedCollection?.Items ?? [];
        var hidden = _hiddenRows.Snapshot.Value ?? [];
        IEnumerable<PlayniteLibraryItem> rendered = route switch
        {
            PlayniteLibraryRoute.Library when local.RetainedHomeCollection is not null =>
                retainedHome.Concat(fixedRows),
            PlayniteLibraryRoute.Library => live.Concat(fixedRows),
            PlayniteLibraryRoute.Browse when retainedBrowse.Count != 0 => retainedBrowse,
            PlayniteLibraryRoute.Browse => live,
            PlayniteLibraryRoute.Hidden => hidden.Concat(live).Concat(fixedRows),
            _ => [],
        };
        IEnumerable<PlayniteLibraryItem> retained = route switch
        {
            PlayniteLibraryRoute.Library when local.RetainedHomeCollection is not null =>
                live.Concat(hidden),
            PlayniteLibraryRoute.Browse => live.Concat(retainedBrowse)
                .Concat(retainedHome).Concat(fixedRows).Concat(hidden),
            PlayniteLibraryRoute.Hidden => retainedHome,
            _ => retainedHome.Concat(hidden),
        };
        _application.PinArtworkHandles(rendered.Concat(retained)
            .SelectMany(item => item.Presentation.Artwork.Items)
            .Select(artwork => artwork.Handle)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
    }

    private static string? ResolveInitialFocus(
        WidgetElement root,
        string? requestedId,
        string? routeFallbackId,
        string activeScopeId,
        bool allowDisabledRequestedTarget)
    {
        var requested = FindFocusTarget(root, requestedId, activeScopeId, null);
        if (requested is not null &&
            (allowDisabledRequestedTarget || !requested.Value.Disabled))
            return requested.Value.Id;

        var routeFallback = FindFocusTarget(
            root, routeFallbackId, activeScopeId, null);
        if (routeFallback is { Disabled: false }) return routeFallback.Value.Id;

        var enabled = FindFirstFocusTarget(
            root, activeScopeId, null, requireEnabled: true);
        if (enabled is not null) return enabled.Value.Id;

        return routeFallback?.Id ?? FindFirstFocusTarget(
            root, activeScopeId, null, requireEnabled: false)?.Id;
    }

    private static FocusTarget? FindFocusTarget(
        WidgetElement element,
        string? id,
        string activeScopeId,
        string? inheritedScopeId)
    {
        if (id is null) return null;
        foreach (var target in FindFocusTargets(
                     element, activeScopeId, inheritedScopeId))
            if (string.Equals(target.Id, id, StringComparison.Ordinal))
                return target;
        return null;
    }

    private static FocusTarget? FindFirstFocusTarget(
        WidgetElement element,
        string activeScopeId,
        string? inheritedScopeId,
        bool requireEnabled)
    {
        foreach (var target in FindFocusTargets(
                     element, activeScopeId, inheritedScopeId))
            if (!requireEnabled || !target.Disabled)
                return target;
        return null;
    }

    private static IEnumerable<FocusTarget> FindFocusTargets(
        WidgetElement element,
        string activeScopeId,
        string? inheritedScopeId)
    {
        switch (element)
        {
            case BackgroundSurfaceElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Content, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case FocusPresentationSurfaceElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Content, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case CollectionItemElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Child, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case FocusBackgroundElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Child, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case FocusPresentationElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Child, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case ResponsiveBranchElement wrapper:
                foreach (var target in FindFocusTargets(
                             wrapper.Child, activeScopeId, inheritedScopeId))
                    yield return target;
                yield break;
            case ContainerElement container:
            {
                var scopeId = container.InputScopeId ?? inheritedScopeId;
                if (scopeId is not null &&
                    !string.Equals(scopeId, activeScopeId, StringComparison.Ordinal))
                    yield break;
                foreach (var child in container.Children)
                foreach (var target in FindFocusTargets(child, activeScopeId, scopeId))
                    yield return target;
                yield break;
            }
            case ButtonElement button:
                yield return new(button.Id, button.IsDisabled == true);
                yield break;
            case SelectElement select:
                yield return new(select.Id, select.IsDisabled == true);
                yield break;
            case ActionSurfaceElement surface:
                yield return new(surface.Id, surface.IsDisabled == true);
                yield break;
            case TextEntryElement entry:
                yield return new(entry.Id, entry.IsDisabled);
                yield break;
            case SliderElement slider:
                yield return new(slider.Id, slider.IsDisabled == true);
                yield break;
            case ScrubberElement scrubber:
                yield return new(scrubber.SliderId, scrubber.IsDisabled == true);
                yield break;
        }
    }

    private readonly record struct FocusTarget(string Id, bool Disabled);

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _ = Operations.RunLatest("playnite-library.warm-state",
            async context =>
            {
                await LoadWarmStateAsync(context.CancellationToken).ConfigureAwait(false);
                if (_navigation.Value.Route == PlayniteLibraryRoute.Hidden)
                    _ = _hiddenRows.Refresh();
                else
                {
                    var route = _navigation.Value;
                    var refresh = _library.Refresh();
                    _ = ClearRetainedHomeAfterCommittedHomeAsync(
                        refresh, route.Revision, context.CancellationToken);
                }
            },
            WidgetOperationLifetime.Active);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (current != WidgetLifecycleState.Interactive)
            ClearPendingBackFocus();
        if (current == WidgetLifecycleState.Interactive ||
            previous == WidgetLifecycleState.Interactive &&
            current == WidgetLifecycleState.Visible)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        ClearPendingBackFocus();
        RetirePlayniteConnection(clearPresentation: false);
        lock (_gate)
        {
            _launchGeneration.Invalidate();
            RetireLiveQueryAuthorityLocked();
        }
        _model.Update(state => state with
        {
            LaunchingSavedId = null,
            PlayniteBusy = false,
            ActiveBrowseReload = null,
        });
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        ClearPendingBackFocus();
        _library.Reset(invalidate: false);
        _playniteClient?.Dispose();
        return _application.DisposeAsync();
    }

    public override async ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var capture = input.Context == ControllerInputContext.OpenWidget &&
            input.Button == ControllerButton.B &&
            input.Phase == ControllerEventPhase.Pressed &&
            input.FocusedElementId is { Length: > 0 } &&
            input.ActiveInputScopeId is { Length: > 0 } &&
            _navigation.Value.CanGoBack;
        if (capture)
        {
            lock (_pendingBackFocusGate)
            {
                if (_pendingBackFocus.Count < ActionQueueCapacity)
                    _pendingBackFocus[(input.Sequence, input.ActiveInputScopeId!)] =
                        input.FocusedElementId!;
            }
        }

        var handled = await base.OnControllerInputAsync(input, cancellationToken)
            .ConfigureAwait(false);
        if (!handled && capture)
            RemovePendingBackFocus(input.Sequence, input.ActiveInputScopeId!);
        return handled;
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
        if (action.ActionId.StartsWith(
                PlayniteLibraryActions.SourceOptionPrefix, StringComparison.Ordinal))
        {
            await SelectSourceOptionAsync(action).ConfigureAwait(false);
            return;
        }
        if (action.ActionId.StartsWith(
                PlayniteLibraryActions.SortOptionPrefix, StringComparison.Ordinal))
        {
            await SelectSortOptionAsync(action).ConfigureAwait(false);
            return;
        }
        if (TryResolveCategoryMembershipAction(action.ActionId, out var categoryId))
        {
            await ToggleCategoryMembershipAsync(
                    categoryId, action.SourceElementId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        if (string.Equals(action.ActionId, PlayniteLibraryActions.CategoryAll,
                StringComparison.Ordinal))
        {
            SelectBrowseCategory(null, action.SourceElementId);
            return;
        }
        if (PlayniteLibraryActions.TryParseCategoryOpen(
                action.ActionId, out var openCategoryId))
        {
            OpenCategory(openCategoryId, action.SourceElementId);
            return;
        }
        var routeBeforeBack = _navigation.Value.Route;
        var backFocus = TakePendingBackFocus(action);
        if (_navigation.TryHandleBack(action, backFocus ?? action.SourceElementId))
        {
            if (routeBeforeBack == PlayniteLibraryRoute.PlayniteConnection)
                RetirePlayniteConnection();
            if (routeBeforeBack == PlayniteLibraryRoute.Hidden)
            {
                _hiddenRows.Reset();
                _model.Update(state => state with
                {
                    PendingRestoredSavedId = null,
                    PreferLibraryContentFocus = true,
                });
                return;
            }
            await ReturnToLibraryAsync().ConfigureAwait(false);
            return;
        }
        if (TryHandleBrowsePagination(action)) return;
        if (_library.TryHandlePagination(action, out _))
        {
            Operations.Cancel("playnite-library.launch-lifecycle");
            return;
        }
        switch (action.ActionId)
        {
            case PlayniteLibraryActions.RefreshSource:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    ResolveActionSource(action.SourceElementId) is null) return;
                Operations.Cancel("playnite-library.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case PlayniteLibraryActions.Previous:
                TryMovePage(action, WidgetCursorDirection.Before);
                return;
            case PlayniteLibraryActions.Next:
                TryMovePage(action, WidgetCursorDirection.After);
                return;
            case PlayniteLibraryActions.Refresh:
                Operations.Cancel("playnite-library.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case PlayniteLibraryActions.Retry:
                _ = _library.Retry();
                return;
            case PlayniteLibraryActions.Launch:
                if (ResolveActionSource(action.SourceElementId) is { } launchSource)
                    await LaunchAsync(launchSource, cancellationToken).ConfigureAwait(false);
                return;
            case PlayniteLibraryActions.Favorite:
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (ResolveActionSource(action.SourceElementId) is { } favoriteSource)
                    await ToggleFavoriteAsync(favoriteSource, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case PlayniteLibraryActions.Hide:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                        PlayniteLibraryRoute.Browse) ||
                    ResolveActionSource(action.SourceElementId) is not { } hideSource) return;
                if (await SetHiddenAsync(hideSource, hidden: true, cancellationToken)
                        .ConfigureAwait(false))
                {
                    _model.Update(state => state with { PreferLibraryContentFocus = true });
                }
                return;
            case PlayniteLibraryActions.Restore:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Hidden) return;
                await SetHiddenAsync(action.SourceElementId, hidden: false, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case PlayniteOpenActionId:
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                RetainHomeCollectionForRouteTransition();
                if (_navigation.Push(PlayniteLibraryRoute.PlayniteConnection,
                        action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    _model.Update(state => state with { PlayniteFeedback = null });
                    var probe = _playniteConnection.Refresh();
                    await probe.Completion.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
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
            case PlayniteLibraryActions.HiddenOpen:
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(PlayniteLibraryRoute.Hidden, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    _model.Update(state => state with
                    {
                        PendingRestoredSavedId = null,
                        PreferLibraryContentFocus = false,
                    });
                    _ = _hiddenRows.Refresh();
                }
                return;
            case PlayniteLibraryActions.HiddenBack:
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    _hiddenRows.Reset();
                    _model.Update(state => state with
                    {
                        PendingRestoredSavedId = null,
                        PreferLibraryContentFocus = true,
                    });
                }
                return;
            case PlayniteLibraryActions.SearchCommit:
                if (action.CommittedText is null) return;
                _model.Update(state => state with { SearchExpanded = true });
                if (_navigation.Value.Route == PlayniteLibraryRoute.Hidden)
                {
                    _model.Update(state => state with
                    {
                        HiddenQuery = state.HiddenQuery with
                        {
                            SearchText = NormalizeSearch(action.CommittedText),
                        },
                    });
                }
                else
                {
                    ReplaceQuery(_model.Value.Collection.Query with
                    {
                        SearchText = NormalizeSearch(action.CommittedText),
                    }, resetBrowseViewport:
                        _navigation.Value.Route == PlayniteLibraryRoute.Browse,
                        browseFocusId: "playnite-library.search");
                }
                return;
            case PlayniteLibraryActions.QueryClear:
                if (_navigation.Value.Route == PlayniteLibraryRoute.Hidden)
                {
                    _model.Update(state => state with
                    {
                        SearchExpanded = false,
                        HiddenQuery = InstalledGames,
                    });
                    return;
                }
                var resetBrowseViewport = _navigation.Value.Route ==
                    PlayniteLibraryRoute.Browse;
                var clearReload = resetBrowseViewport ? CreateBrowseReload() : null;
                var cleared = _model.Update(state =>
                {
                    var collection = _navigation.Value.Route ==
                        PlayniteLibraryRoute.Library
                        ? state.Collection.ClearQuery(InstalledGames)
                        : state.Collection.Reset(InstalledGames);
                    var semanticChange = !SameCollectionQuery(
                        state.Collection, collection) ||
                        resetBrowseViewport && state.ActiveCategoryId is not null;
                    return (state with
                    {
                        SearchExpanded = false,
                        Collection = collection,
                        ActiveCategoryId = resetBrowseViewport ? null : state.ActiveCategoryId,
                        AlternateBrowseViewport = resetBrowseViewport && semanticChange
                        ? !state.AlternateBrowseViewport
                        : state.AlternateBrowseViewport,
                        BrowseInitialFocusId = resetBrowseViewport && semanticChange
                            ? PlayniteLibraryActions.QueryClear
                            : state.BrowseInitialFocusId,
                        ActiveBrowseReload = resetBrowseViewport && semanticChange
                            ? clearReload
                            : state.ActiveBrowseReload,
                    }, semanticChange);
                });
                if (cleared.Result)
                    ReloadQuery(retainCurrentItems: !resetBrowseViewport);
                return;
            case PlayniteLibraryActions.BrowseOpen:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                RetainHomeCollectionForRouteTransition();
                if (_navigation.Push(PlayniteLibraryRoute.Browse, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                    _model.Update(state => state with
                    {
                        SearchExpanded = false,
                        AlternateBrowseViewport = !state.AlternateBrowseViewport,
                    });
                return;
            case PlayniteLibraryActions.FavoritesFilter:
                if (!TryOpenBrowse(action.SourceElementId)) return;
                var favoriteReload = CreateBrowseReload();
                _model.Update(state => state with
                {
                    Collection = state.Collection.ToggleFavorites(InstalledGames),
                    AlternateBrowseViewport = !state.AlternateBrowseViewport,
                    BrowseInitialFocusId = PlayniteLibraryActions.FavoritesFilter,
                    ActiveBrowseReload = favoriteReload,
                });
                ReloadQuery(retainCurrentItems: false);
                return;
            case PlayniteLibraryActions.RecentlyPlayedFilter:
                if (!TryOpenBrowse(action.SourceElementId)) return;
                var recentReload = CreateBrowseReload();
                _model.Update(state => state with
                {
                    Collection = state.Collection.ToggleRecentlyPlayed(InstalledGames),
                    AlternateBrowseViewport = !state.AlternateBrowseViewport,
                    BrowseInitialFocusId = PlayniteLibraryActions.RecentlyPlayedFilter,
                    ActiveBrowseReload = recentReload,
                });
                ReloadQuery(retainCurrentItems: false);
                return;
            case PlayniteLibraryActions.CategoriesOpen:
                OpenCategories(action.SourceElementId);
                return;
            case PlayniteLibraryActions.CollectionPrevious:
                await SwitchCollectionAsync(
                        PlayniteLibraryCollectionDirection.Previous,
                        action.SourceElementId)
                    .ConfigureAwait(false);
                return;
            case PlayniteLibraryActions.CollectionNext:
                await SwitchCollectionAsync(
                        PlayniteLibraryCollectionDirection.Next,
                        action.SourceElementId)
                    .ConfigureAwait(false);
                return;
            case PlayniteLibraryActions.CategoriesBack:
                if (_navigation.Value.Route != PlayniteLibraryRoute.Categories) return;
                _model.Update(state => state with { PreferLibraryContentFocus = true });
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    await ReturnToLibraryAsync(preferContentFocus: true)
                        .ConfigureAwait(false);
                }
                return;
            case PlayniteLibraryActions.CategoryCreate:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route != PlayniteLibraryRoute.Categories ||
                    action.CommittedText is null) return;
                await CreateCategoryAsync(action.CommittedText, cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
    }

    private bool TryResolveCategoryMembershipAction(
        string actionId,
        out string categoryId)
    {
        categoryId = string.Empty;
        if (!PlayniteLibraryActions.TryParseCategoryMembership(actionId, out var candidate))
            return false;
        lock (_gate)
        {
            if (!PresentationOrganizationLocked().Categories.Any(category =>
                    string.Equals(category.Id, candidate, StringComparison.Ordinal)))
                return false;
        }
        categoryId = candidate;
        return true;
    }

    private string? TakePendingBackFocus(WidgetActionEvent action)
    {
        if (action.ControllerButton != ControllerButton.B ||
            action.Phase != ControllerEventPhase.Pressed ||
            action.InputScopeId is not { Length: > 0 } scopeId)
            return null;
        string? focusId;
        lock (_pendingBackFocusGate)
        {
            if (!_pendingBackFocus.Remove((action.Sequence, scopeId), out focusId))
                return null;
        }
        return string.Equals(action.ActionId, _navigation.Value.BackActionId,
            StringComparison.Ordinal) ? focusId : null;
    }

    private void RemovePendingBackFocus(long sequence, string scopeId)
    {
        lock (_pendingBackFocusGate)
            _pendingBackFocus.Remove((sequence, scopeId));
    }

    private void ClearPendingBackFocus()
    {
        lock (_pendingBackFocusGate)
            _pendingBackFocus.Clear();
    }

    private void TryMovePage(
        WidgetActionEvent action,
        WidgetCursorDirection direction)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
            return;
        var sourceScrollId = action.SourceElementId is
                PlayniteLibraryPresentation.HomeRailId or
                PlayniteLibraryPresentation.ScrollId
            ? action.SourceElementId
            : _navigation.Value.Route == PlayniteLibraryRoute.Browse &&
              string.Equals(action.SourceElementId,
                  PlayniteLibraryPresentation.BrowseScrollId(
                      _model.Value.AlternateBrowseViewport),
                  StringComparison.Ordinal)
                ? action.SourceElementId
                : null;
        if (sourceScrollId is null) return;
        var snapshot = _library.Snapshot;
        if (snapshot.Status != WidgetPagedResourceStatus.Ready ||
            direction == WidgetCursorDirection.Before && !snapshot.HasBefore ||
            direction == WidgetCursorDirection.After && !snapshot.HasAfter)
            return;
        Operations.Cancel("playnite-library.launch-lifecycle");
        _ = _library.Move(direction, sourceScrollId);
    }

    private bool TryHandleBrowsePagination(WidgetActionEvent action)
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.Browse ||
            !string.Equals(
                action.SourceElementId,
                PlayniteLibraryPresentation.BrowseScrollId(
                    _model.Value.AlternateBrowseViewport),
                StringComparison.Ordinal))
            return false;
        var direction = action.ActionId switch
        {
            "playnite-library.library.cursor.before" =>
                (WidgetCursorDirection?)WidgetCursorDirection.Before,
            "playnite-library.library.cursor.after" => WidgetCursorDirection.After,
            _ => null,
        };
        if (direction is null) return false;
        TryMovePage(action, direction.Value);
        return true;
    }

    private static string? NormalizeSearch(string value)
    {
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool SameCollectionQuery(
        PlayniteLibraryCollectionState left,
        PlayniteLibraryCollectionState right) =>
        left.Query == right.Query &&
        left.Selection == right.Selection &&
        left.FavoriteFilter == right.FavoriteFilter &&
        left.RecentlyPlayed == right.RecentlyPlayed;

    private ValueTask SelectSourceOptionAsync(WidgetActionEvent action)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Browse ||
            !string.Equals(action.SourceElementId,
                PlayniteLibraryActions.SourceFilter, StringComparison.Ordinal))
            return ValueTask.CompletedTask;
        var local = _model.Value;
        string? source;
        if (string.Equals(action.ActionId,
                PlayniteLibraryActions.SourceAll, StringComparison.Ordinal))
        {
            source = null;
        }
        else
        {
            string[] choices;
            lock (_gate)
                choices = PlayniteLibrarySourceCatalog.SelectOptions(
                    PresentationOrganizationLocked().ProvenSources,
                    local.SourceObservations,
                    local.Collection.Query.SourceAttribution);
            source = choices.FirstOrDefault(choice => string.Equals(
                PlayniteLibraryActions.SourceOption(choice),
                action.ActionId,
                StringComparison.Ordinal));
            if (source is null) return ValueTask.CompletedTask;
        }
        ReplaceQuery(local.Collection.Query with { SourceAttribution = source },
            resetBrowseViewport: true,
            browseFocusId: PlayniteLibraryActions.SourceFilter);
        return ValueTask.CompletedTask;
    }

    private ValueTask SelectSortOptionAsync(WidgetActionEvent action)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Browse ||
            _model.Value.Collection.RecentlyPlayed ||
            !string.Equals(action.SourceElementId,
                PlayniteLibraryActions.SortFilter, StringComparison.Ordinal))
            return ValueTask.CompletedTask;
        var sort = action.ActionId switch
        {
            PlayniteLibraryActions.SortDisplayName =>
                (WidgetAppLibrarySortOrder?)WidgetAppLibrarySortOrder.DisplayName,
            PlayniteLibraryActions.SortDisplayNameDescending =>
                WidgetAppLibrarySortOrder.DisplayNameDescending,
            PlayniteLibraryActions.SortSourceThenDisplayName =>
                WidgetAppLibrarySortOrder.SourceThenDisplayName,
            _ => null,
        };
        if (sort is null) return ValueTask.CompletedTask;
        ReplaceQuery(_model.Value.Collection.Query with { Sort = sort.Value },
            resetBrowseViewport: true,
            browseFocusId: PlayniteLibraryActions.SortFilter);
        return ValueTask.CompletedTask;
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
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return false;
        RetainHomeCollectionForRouteTransition();
        return _navigation.Push(PlayniteLibraryRoute.Browse, sourceElementId) ==
            WidgetNavigationResult.Changed;
    }

    private bool RetainHomeCollectionForRouteTransition()
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return false;
        var snapshot = _library.Snapshot;
        if (snapshot.Items.Count != 0)
            _model.Update(state => state with { RetainedHomeCollection = snapshot });
        return true;
    }

    private string[] ProvenSourcesLocked() =>
        _organization.ProvenSources.ToArray();

    private void ReplaceQuery(
        WidgetAppLibraryQuery query,
        bool force = false,
        bool resetBrowseViewport = false,
        string? browseFocusId = null)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var browseReload = resetBrowseViewport ? CreateBrowseReload() : null;
        var update = _model.Update(state => !force && query == state.Collection.Query
            ? state
            : state with
            {
                Collection = state.Collection with { Query = query },
                AlternateBrowseViewport = resetBrowseViewport
                    ? !state.AlternateBrowseViewport
                    : state.AlternateBrowseViewport,
                BrowseInitialFocusId = resetBrowseViewport
                    ? browseFocusId
                    : state.BrowseInitialFocusId,
                ActiveBrowseReload = resetBrowseViewport
                    ? browseReload
                    : state.ActiveBrowseReload,
            });
        if (!update.Changed) return;
        if (_navigation.Value.Route != PlayniteLibraryRoute.Hidden)
            ReloadQuery(retainCurrentItems: !resetBrowseViewport);
    }

    private WidgetOperationHandle? ReloadQuery(
        bool preserveContentFocus = false,
        bool retainCurrentItems = true)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return null;
        Operations.Cancel("playnite-library.launch-lifecycle");
        _launchGeneration.Invalidate();
        _model.Update(state => state with
        {
            Status = "Applying library filters…",
            LaunchingSavedId = null,
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            PreferLibraryContentFocus = preserveContentFocus &&
                state.PreferLibraryContentFocus,
        });
        WidgetOperationHandle operation;
        if (retainCurrentItems)
        {
            operation = _library.Refresh();
        }
        else
        {
            _library.Reset(invalidate: false);
            operation = _library.EnsureLoaded();
        }
        if (_model.Value.ActiveBrowseReload is { } browseReload)
            _ = ClearBrowseReloadAfterTerminalAsync(operation, browseReload.AttemptId);
        return operation;
    }

    private PlayniteLibraryBrowseReload CreateBrowseReload()
    {
        var retainedFallback = _model.Value.ActiveBrowseReload?.RetainedCollection;
        var snapshot = _library.Snapshot;
        var retained = snapshot.Status == WidgetPagedResourceStatus.Ready
            ? snapshot with
            {
                Before = null,
                After = null,
                RequestedFocusId = null,
                FirstItemIndex = null,
                TotalItemCount = null,
                WindowGeneration = 0,
                WindowChange = default,
            }
            : retainedFallback;
        return new(Interlocked.Increment(ref _browseReloadAttempt), retained);
    }

    private async Task ClearBrowseReloadAfterTerminalAsync(
        WidgetOperationHandle operation,
        long attemptId)
    {
        try
        {
            await operation.Completion.ConfigureAwait(false);
        }
        finally
        {
            _model.Update(state => state.ActiveBrowseReload?.AttemptId == attemptId
                ? state with { ActiveBrowseReload = null }
                : state);
        }
    }

    private async Task ReloadQueryAsync(
        bool preserveContentFocus = false,
        bool retainCurrentItems = false)
    {
        var operation = ReloadQuery(preserveContentFocus, retainCurrentItems);
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
            BrowseInitialFocusId = null,
            ActiveBrowseReload = null,
        });
        var replacement = ReloadQuery(preferContentFocus, retainCurrentItems: true);
        if (replacement is { } admitted)
            await ClearRetainedHomeAfterCommittedHomeAsync(
                admitted, _navigation.Value.Revision, ActiveCancellationToken)
                .ConfigureAwait(false);
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

    private async Task ClearRetainedHomeAfterCommittedHomeAsync(
        WidgetOperationHandle operation,
        long routeRevision,
        CancellationToken cancellationToken)
    {
        var result = await operation.Completion.ConfigureAwait(false);
        var route = _navigation.Value;
        if (cancellationToken.IsCancellationRequested ||
            result.Status != WidgetOperationStatus.Succeeded ||
            route.Route != PlayniteLibraryRoute.Library ||
            route.Revision != routeRevision ||
            _library.Snapshot.Status != WidgetPagedResourceStatus.Ready)
            return;
        _model.Update(state => state with { RetainedHomeCollection = null });
    }

    private WidgetAppLibraryQuery EffectiveQueryLocked(
        PlayniteLibraryRoute route,
        PlayniteLibraryRenderState local)
    {
        var organization = PresentationOrganizationLocked();
        IEnumerable<string>? savedIds = null;
        if (route == PlayniteLibraryRoute.Browse && local.ActiveCategoryId is not null)
            savedIds = PlayniteLibraryCategoryPolicy.Find(
                organization, local.ActiveCategoryId)?.SavedIds ?? [];
        if (local.Collection.FavoriteFilter)
            savedIds = savedIds is null
                ? organization.FavoriteSavedIds
                : savedIds.Intersect(organization.FavoriteSavedIds,
                    StringComparer.Ordinal);
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

    private void RetireCurrentQueryAuthorityLocked()
    {
        _queryAuthorityGeneration++;
        _authorityRevision++;
        _livePlayniteAuthority = PlayniteLibraryAuthorityProjection.Empty;
        _hasLivePlayniteAuthority = false;
    }

    private void RetireLiveQueryAuthorityLocked()
    {
        RetireCurrentQueryAuthorityLocked();
        _createdCategoriesPendingReconciliation.Clear();
    }

    private void EnsureQueryAuthorityCurrent(
        long generation,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (generation == _queryAuthorityGeneration) return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException("The Playnite query authority was retired.",
            cancellationToken);
    }

    private bool TryGetLivePlayniteAuthorityLocked(
        out PlayniteLibraryAuthorityProjection authority)
    {
        authority = _livePlayniteAuthority;
        return _hasLivePlayniteAuthority;
    }

    private bool TryPublishQueryAuthority(
        long generation,
        long authorityRevision,
        PlayniteLibraryAuthorityProjection authority,
        bool retainedLastGood,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (generation == _queryAuthorityGeneration &&
                authorityRevision == _authorityRevision)
            {
                authority = ReconcileCreatedCategoryLocked(authority, retainedLastGood);
                _livePlayniteAuthority = authority;
                _presentationAuthority = authority;
                _hasLivePlayniteAuthority = true;
                _authorityRevision++;
                return true;
            }
            if (generation == _queryAuthorityGeneration) return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException("The Playnite query authority was retired.",
            cancellationToken);
    }

    private void PublishAuthorityMutationLocked(
        PlayniteLibraryAuthorityProjection authority)
    {
        _livePlayniteAuthority = authority;
        _presentationAuthority = authority;
        _authorityRevision++;
    }

    private void AdmitCreatedCategoryLocked(PlayniteLibraryCategory created)
    {
        if (!_hasLivePlayniteAuthority || _livePlayniteAuthority.Categories.Any(category =>
                string.Equals(category.Id, created.Id, StringComparison.Ordinal) ||
                string.Equals(category.Name, created.Name,
                    StringComparison.OrdinalIgnoreCase))) return;
        if (_livePlayniteAuthority.Categories.Count >=
            PlayniteLibraryPrivateState.MaximumCategories) return;
        _createdCategoriesPendingReconciliation.Add(created);
        PublishAuthorityMutationLocked(_livePlayniteAuthority with
        {
            Categories = _livePlayniteAuthority.Categories.Append(created)
                .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(category => category.Id, StringComparer.Ordinal)
                .ToArray(),
        });
    }

    private PlayniteLibraryAuthorityProjection ReconcileCreatedCategoryLocked(
        PlayniteLibraryAuthorityProjection authority,
        bool retainedLastGood)
    {
        if (_createdCategoriesPendingReconciliation.Count == 0) return authority;
        if (!retainedLastGood)
        {
            _createdCategoriesPendingReconciliation.Clear();
            return authority;
        }
        var categories = authority.Categories.ToList();
        foreach (var created in _createdCategoriesPendingReconciliation)
        {
            if (categories.Count >= PlayniteLibraryPrivateState.MaximumCategories) break;
            if (categories.Any(category =>
                    string.Equals(category.Id, created.Id, StringComparison.Ordinal) ||
                    string.Equals(category.Name, created.Name,
                        StringComparison.OrdinalIgnoreCase))) continue;
            categories.Add(created);
        }
        return authority with
        {
            Categories = categories
                .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(category => category.Id, StringComparer.Ordinal)
                .ToArray(),
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
                PlayniteLibraryTitlePolicy.RequiresReset(stored.Value) ||
                stored.Value?.RecentSavedIds is { Count: > 0 }))
            {
                var reset = await PlayniteLibraryStateStore.SaveAsync(
                        state => PlayniteLibraryStateMutation.Apply(state),
                        (state, expected, token) => _application.WriteStateAsync(
                            state, expected, token),
                        token => _application.ReadStateAsync(token),
                        stored.Value!, stored.Revision, cancellationToken)
                    .ConfigureAwait(false);
                normalized = reset.State;
                revision = reset.Revision;
            }
            lock (_gate)
            {
                _organization = normalized;
                _stateRevision = revision;
            }
            if (_library.Snapshot.Items.Count == 0)
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
            // A readiness probe cannot erase the retained non-authorizing
            // presentation projection. The current provider query remains the
            // only path that can replace it.
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
        long queryAuthorityGeneration;
        long authorityRevision;
        var route = _navigation.Value.Route;
        var local = _model.Value;
        lock (_gate)
        {
            if (direction is null) RetireCurrentQueryAuthorityLocked();
            queryAuthorityGeneration = _queryAuthorityGeneration;
            authorityRevision = _authorityRevision;
            collectionState = local.Collection;
            query = EffectiveQueryLocked(route, local);
            organization = PresentationOrganizationLocked();
        }
        var categoryName = route == PlayniteLibraryRoute.Browse &&
            local.ActiveCategoryId is not null
            ? PlayniteLibraryCategoryPolicy.Find(organization, local.ActiveCategoryId)?.Name
            : null;
        var result = await _application.QueryWithAuthorityAsync(
                query,
                new(route switch
                {
                    _ when collectionState.RecentlyPlayed =>
                        PlayniteLibraryQueryScope.RecentlyPlayed,
                    PlayniteLibraryRoute.Library => PlayniteLibraryQueryScope.Home,
                    PlayniteLibraryRoute.Browse when local.ActiveCategoryId is not null =>
                        PlayniteLibraryQueryScope.Category,
                    _ => PlayniteLibraryQueryScope.Library,
                }, categoryName),
                requestCursor,
                direction,
                limit,
                refresh: direction is null,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureQueryAuthorityCurrent(queryAuthorityGeneration, cancellationToken);
        var page = result.Page;
        lock (_gate) organization = PresentationOrganizationLocked(result.Authority);
        var rawItems = page.Items.Select(PlayniteLibraryItem.From).ToArray();
        var fixedRows = PlayniteLibraryFixedRows.Empty;
        var rawFixedRows = PlayniteLibraryFixedRows.Empty;
        if (direction is null)
        {
            var titleMatchIds = PlayniteLibraryTitlePolicy.SearchMatches(
                organization, query.SearchText);
            var fixedSavedIds = titleMatchIds
                .Concat(route == PlayniteLibraryRoute.Library
                    ? organization.ManualSavedIds
                    : [])
                .Distinct(StringComparer.Ordinal)
                .Take(WidgetAppLibraryService.MaximumSavedItems)
                .ToArray();
            IReadOnlyList<WidgetAppLibraryItem> resolved = fixedSavedIds.Length == 0
                ? Array.Empty<WidgetAppLibraryItem>()
                : await _application.ResolveSavedAsync(
                    fixedSavedIds, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureQueryAuthorityCurrent(queryAuthorityGeneration, cancellationToken);
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
                EnsureQueryAuthorityCurrent(queryAuthorityGeneration, cancellationToken);
                lock (_gate) organization = PresentationOrganizationLocked(result.Authority);
            }
            PlayniteLibraryItem[] recent = [];
            PlayniteLibraryItem[] manual = route != PlayniteLibraryRoute.Library
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
                recent.Select(item => item.WithProjectedValue(
                    PlayniteLibraryTitlePolicy.Project(organization, item.Value))).ToArray(),
                manual.Select(item => item.WithProjectedValue(
                    PlayniteLibraryTitlePolicy.Project(organization, item.Value))).ToArray(),
                titleMatches);
        }
        var retainedForProjection = direction is null
            ? rawFixedRows
            : PlayniteLibraryFixedRows.Empty;
        var projectionItems = rawItems.Concat(retainedForProjection.All)
            .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
            .ToArray();
        var reconcileSources = false;
        if (route == _navigation.Value.Route)
            reconcileSources = _model.Update(state =>
            {
                if (state.Collection != collectionState) return (state, false);
                var observations = PlayniteLibrarySourceCatalog.RetainObservations(
                    state.SourceObservations, page.Sources);
                return (ReferenceEquals(observations, state.SourceObservations)
                        ? state
                        : state with { SourceObservations = observations },
                    true);
            }).Result;
        var projectionSaved = await PersistProjectionAsync(
                projectionItems, reconcileSources, collectionState, direction,
                rawItems, page.Sources, page.Before is null && page.After is null,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureQueryAuthorityCurrent(queryAuthorityGeneration, cancellationToken);
        _ = TryPublishQueryAuthority(queryAuthorityGeneration, authorityRevision,
            result.Authority, result.RetainedLastGood, cancellationToken);
        var items = rawItems.Select(item => item.WithProjectedValue(
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

    private async ValueTask<IReadOnlyList<PlayniteLibraryItem>> LoadHiddenRowsAsync(
        CancellationToken cancellationToken)
    {
        string[] hiddenSavedIds;
        lock (_gate)
            hiddenSavedIds = PresentationOrganizationLocked().ExcludedSavedIds
                .Distinct(StringComparer.Ordinal)
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .ToArray();
        if (hiddenSavedIds.Length == 0) return [];

        var requested = hiddenSavedIds.ToHashSet(StringComparer.Ordinal);
        var resolved = await _application.ResolveSavedAsync(
                hiddenSavedIds, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return resolved
            .Where(item => requested.Contains(item.SavedId) &&
                item.Presentation.Kind == WidgetAppLibraryKind.Game &&
                item.Presentation.Availability.State ==
                    WidgetAppLibraryAvailabilityState.Installed)
            .DistinctBy(item => item.SavedId, StringComparer.Ordinal)
            .Select(PlayniteLibraryItem.From)
            .ToArray();
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
        var handle = Operations.RunSingleFlight(
            "playnite-library.launch-lifecycle",
            context => new ValueTask(LaunchAfterAdmissionAsync(
                sourceElementId, generationReady.Task,
                context.CancellationToken, cancellationToken)),
            WidgetOperationLifetime.Active);
        if (!_launchGeneration.CompleteAdmission(handle, generationReady)) return;
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
                if (_launchGeneration.IsCurrent(generation) &&
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
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (requestCancellation.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (selected is not null && _launchGeneration.IsCurrent(generation) &&
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
                if (_launchGeneration.IsCurrent(generation))
                    _model.Update(state => state with { LaunchingSavedId = null });
        }
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
            if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return;
            favorite = !authority.FavoriteGameIds.Contains(
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
                    if (!TryGetLivePlayniteAuthorityLocked(out var authority))
                    {
                        applied = false;
                        return;
                    }
                    var values = authority.FavoriteGameIds
                        .Where(value => value != display.SavedId).ToList();
                    if (favorite) values.Add(display.SavedId);
                    PublishAuthorityMutationLocked(authority with
                    {
                        FavoriteGameIds = values,
                    });
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
        if (!hidden)
        {
            await _library.WhenIdleAsync(cancellationToken).ConfigureAwait(false);
            if (LifecycleState != WidgetLifecycleState.Interactive ||
                _navigation.Value.Route != PlayniteLibraryRoute.Hidden)
                return false;
        }
        PlayniteLibraryDisplayItem? display;
        if (hidden)
        {
            display = DisplayForSource(sourceElementId);
        }
        else
        {
            lock (_gate)
            {
                if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return false;
                var savedId = authority.HiddenGameIds.FirstOrDefault(candidate =>
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
                    if (!TryGetLivePlayniteAuthorityLocked(out var authority))
                    {
                        applied = false;
                        return false;
                    }
                    var values = authority.HiddenGameIds
                        .Where(value => value != display.SavedId).ToList();
                    if (hidden) values.Add(display.SavedId);
                    PublishAuthorityMutationLocked(authority with { HiddenGameIds = values });
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
            if (hidden || _navigation.Value.Route != PlayniteLibraryRoute.Hidden)
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
            lock (_gate)
            {
                if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return;
                current = authority.CompletionStatuses.GetValueOrDefault(display.SavedId);
            }
            var index = current is null ? -1 : statuses.ToList().FindIndex(value =>
                string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
            var next = statuses[(index + 1) % statuses.Count];
            var changed = await _application.SetCompletionStatusAsync(
                    display.SavedId, next, cancellationToken).ConfigureAwait(false);
            if (changed is null) return;
            lock (_gate)
            {
                if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return;
                var values = new Dictionary<string, string?>(
                    authority.CompletionStatuses, StringComparer.Ordinal)
                {
                    [display.SavedId] = next,
                };
                PublishAuthorityMutationLocked(authority with
                {
                    CompletionStatuses = values,
                });
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
            if (_organization.ExcludedSavedIds.Contains(
                    display.SavedId, StringComparer.Ordinal))
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

    private async Task<bool> PersistProjectionAsync(
        IReadOnlyList<PlayniteLibraryItem> items,
        bool reconcileSources,
        PlayniteLibraryCollectionState collectionState,
        WidgetCursorDirection? direction,
        IReadOnlyList<PlayniteLibraryItem> sourceItems,
        IReadOnlyList<WidgetAppLibrarySource> sourceObservations,
        bool completeCatalog,
        CancellationToken cancellationToken) => (await SaveStateAsync(
                state =>
                {
                    var projected = PlayniteLibraryOrganizationPolicy.ProjectPage(
                        state, items);
                    if (reconcileSources)
                        projected = projected with
                        {
                            ProvenSources = PlayniteLibrarySourceCatalog.Reconcile(
                                state.ProvenSources, collectionState, direction,
                                sourceItems, sourceObservations, completeCatalog),
                        };
                    return PlayniteLibraryStateMutation.Apply(projected);
                },
                cancellationToken).ConfigureAwait(false)).Saved;

    private async Task<bool> MutateOrganizationAsync(
        Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
        string success,
        CancellationToken cancellationToken,
        string failure = "Organization change was not saved")
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        _model.Update(state => state with { OrganizationBusy = true });
        var result = PlayniteLibraryPersistenceResult.PolicyRejected;
        try
        {
            result = await SaveStateAsync(apply, lifetime.Token).ConfigureAwait(false);
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
                    ? result.Saved ? success : PersistenceDiagnostic(result, failure)
                    : state.Status,
            });
        }
        return result.Saved;
    }

    private async Task<PlayniteLibraryPersistenceResult> SaveStateAsync(
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
            return saved.Saved
                ? PlayniteLibraryPersistenceResult.SavedResult
                : PlayniteLibraryPersistenceResult.PolicyRejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WidgetCapabilityException exception)
        {
            return new(PlayniteLibraryPersistenceOutcome.CapabilityFailure,
                exception.ErrorCode);
        }
        catch (Exception)
        {
            return PlayniteLibraryPersistenceResult.UnexpectedFailure;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    internal static string PersistenceDiagnostic(
        PlayniteLibraryPersistenceResult result,
        string rejectedMessage) => result.Outcome switch
        {
            PlayniteLibraryPersistenceOutcome.PolicyRejected => rejectedMessage,
            PlayniteLibraryPersistenceOutcome.CapabilityFailure
                when result.Code == "state_conflict" =>
                "Organization changed elsewhere; try again",
            PlayniteLibraryPersistenceOutcome.CapabilityFailure =>
                "Organization could not be saved",
            PlayniteLibraryPersistenceOutcome.UnexpectedFailure =>
                "Organization could not be saved",
            _ => rejectedMessage,
        };


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
        var sourceCollection = route switch
        {
            PlayniteLibraryRoute.Library when local.RetainedHomeCollection is { } retainedHome =>
                retainedHome,
            PlayniteLibraryRoute.Browse when
                local.ActiveBrowseReload?.RetainedCollection is { } retainedBrowse =>
                retainedBrowse,
            _ => _library.Snapshot,
        };
        var collection = sourceCollection with
        {
            Items = sourceCollection.Items.Select(item => item.WithProjectedValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
        };
        var fixedRows = new PlayniteLibraryFixedRows(
            local.FixedRows.Recent.Select(item => item.WithProjectedValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            local.FixedRows.Manual.Select(item => item.WithProjectedValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            local.FixedRows.TitleMatches.Select(item => item.WithProjectedValue(
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
        route == PlayniteLibraryRoute.Hidden
            ? local.HiddenQuery
            : local.Collection.Query,
        local.Collection.RecentlyPlayed,
        local.Collection.FavoriteFilter,
        route,
        fixedRows,
        _hiddenRows.Snapshot.Value ?? [],
        local.SourceObservations,
        local.HeroSavedId,
        local.HeroIndex)
    {
        ActiveCategoryId = local.ActiveCategoryId,
        SearchExpanded = local.SearchExpanded,
        AlternateBrowseViewport = local.AlternateBrowseViewport,
        BrowseInitialFocusId = local.BrowseInitialFocusId,
        BrowseRetained = route == PlayniteLibraryRoute.Browse &&
            local.ActiveBrowseReload?.RetainedCollection is not null,
        Collections = PlayniteLibraryCollectionPolicy.Options(
            organization, ProvenSourcesLocked(), local.Collection.Selection),
        CompletionStatuses = _presentationAuthority.CompletionStatuses,
        CategoryFeedback = local.CategoryFeedback,
        CategoryFeedbackSucceeded = local.CategoryFeedbackSucceeded,
    };
    }

    private PlayniteLibraryPrivateState PresentationOrganizationLocked(
        PlayniteLibraryAuthorityProjection? authority = null)
    {
        authority ??= _presentationAuthority;
        return
        _organization with
        {
            FavoriteSavedIds = authority.FavoriteGameIds,
            ExcludedSavedIds = authority.HiddenGameIds,
            Categories = authority.Categories,
        };
    }

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
                PlayniteLibraryRoute.Browse)) return;
        RetainHomeCollectionForRouteTransition();
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library)
            _navigation.Back(sourceElementId);
        _model.Update(state => state with
        {
            PreferLibraryContentFocus = false,
            BrowseInitialFocusId = null,
            ActiveBrowseReload = null,
        });
        _navigation.Push(PlayniteLibraryRoute.Categories, sourceElementId);
    }

    private void OpenCategory(string categoryId, string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        if (_navigation.Value.Route == PlayniteLibraryRoute.Browse)
        {
            SelectBrowseCategory(categoryId, sourceElementId);
            return;
        }
        if (_navigation.Value.Route != PlayniteLibraryRoute.Categories) return;
        if (_navigation.Back(sourceElementId) != WidgetNavigationResult.Changed) return;
        if (_navigation.Push(PlayniteLibraryRoute.Browse, sourceElementId) !=
            WidgetNavigationResult.Changed) return;
        SelectBrowseCategory(categoryId, sourceElementId);
    }

    private void SelectBrowseCategory(string? categoryId, string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Browse) return;
        lock (_gate)
            if (categoryId is not null && PlayniteLibraryCategoryPolicy.Find(
                    PresentationOrganizationLocked(), categoryId) is null) return;
        var local = _model.Value;
        if (string.Equals(local.ActiveCategoryId, categoryId, StringComparison.Ordinal)) return;
        var reload = CreateBrowseReload();
        _model.Update(state => state with
        {
            ActiveCategoryId = categoryId,
            Collection = state.Collection.Reset(InstalledGames),
            FixedRows = PlayniteLibraryFixedRows.Empty,
            FixedRowsRevision = state.FixedRowsRevision + 1,
            PreferLibraryContentFocus = false,
            AlternateBrowseViewport = !state.AlternateBrowseViewport,
            BrowseInitialFocusId = PlayniteLibraryActions.CategoryFilter,
            ActiveBrowseReload = reload,
        });
        ReloadQuery(retainCurrentItems: false);
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
                CategoryFeedback =
                    $"Category name must be 1–{PlayniteLibraryPrivateState.MaximumCategoryNameLength} characters",
                CategoryFeedbackSucceeded = false,
            });
            return;
        }
        lock (_gate)
        {
            if (!_hasLivePlayniteAuthority) return;
        }
        _model.Update(state => state with
        {
            OrganizationBusy = true,
            CategoryFeedback = null,
        });
        string? status = null;
        try
        {
            var created = await _application.CreateCategoryAsync(
                    normalized, cancellationToken).ConfigureAwait(false);
            if (created is not null)
                await _library.Refresh().Completion.ConfigureAwait(false);
            lock (_gate)
            {
                if (created is not null) AdmitCreatedCategoryLocked(created);
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
                CategoryFeedback = status,
                CategoryFeedbackSucceeded = status?.StartsWith(
                    "Created category ", StringComparison.Ordinal) == true,
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
        string? categoryName;
        lock (_gate)
        {
            if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return;
            var category = PlayniteLibraryCategoryPolicy.Find(
                PresentationOrganizationLocked(authority), categoryId);
            categoryName = category?.Name;
        }
        if (categoryName is null) return;
        var included = false;
        lock (_gate)
        {
            if (!TryGetLivePlayniteAuthorityLocked(out var authority)) return;
            included = PlayniteLibraryCategoryPolicy.Find(
                    PresentationOrganizationLocked(authority), categoryId) is { } category &&
                PlayniteLibraryCategoryPolicy.Contains(category, display.SavedId);
        }
        _model.Update(state => state with
        {
            OrganizationBusy = true,
            CategoryFeedback = null,
        });
        string? status = null;
        try
        {
            var changed = await _application.SetCategoryMembershipAsync(
                    display.SavedId, categoryName, !included, cancellationToken)
                .ConfigureAwait(false);
            if (changed is null)
            {
                status = "Playnite did not accept the category change";
            }
            else
            {
                await _library.Refresh().Completion.ConfigureAwait(false);
                lock (_gate)
                {
                    var confirmedCategory = PlayniteLibraryCategoryPolicy.Find(
                        PresentationOrganizationLocked(), categoryId);
                    var confirmedIncluded = confirmedCategory is not null &&
                        PlayniteLibraryCategoryPolicy.Contains(
                            confirmedCategory, display.SavedId);
                    status = confirmedIncluded == !included
                        ? included
                        ? $"Removed {display.DisplayName} from {categoryName}"
                        : $"Added {display.DisplayName} to {categoryName}"
                        : "Playnite did not confirm the category change";
                }
            }
        }
        finally
        {
            _model.Update(state => state with
            {
                OrganizationBusy = false,
                Status = status ?? state.Status,
                CategoryFeedback = status,
                CategoryFeedbackSucceeded = status?.StartsWith(
                    "Added ", StringComparison.Ordinal) == true ||
                    status?.StartsWith("Removed ", StringComparison.Ordinal) == true,
            });
        }
    }

    private ValueTask SwitchCollectionAsync(
        PlayniteLibraryCollectionDirection direction,
        string sourceElementId)
    {
        var route = _navigation.Value.Route;
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            route is not (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse))
            return ValueTask.CompletedTask;

        PlayniteLibraryPrivateState organization;
        string? currentCategoryId;
        var local = _model.Value;
        lock (_gate)
        {
            if (local.OrganizationBusy) return ValueTask.CompletedTask;
            organization = PresentationOrganizationLocked();
            currentCategoryId = route == PlayniteLibraryRoute.Browse
                ? local.ActiveCategoryId
                : null;
        }
        if (organization.Categories.Count == 0) return ValueTask.CompletedTask;
        var targetCategoryId = PlayniteLibraryCategoryPolicy.Cycle(
            organization.Categories, currentCategoryId, direction);

        Operations.Cancel("playnite-library.launch-lifecycle");
        if (targetCategoryId is null)
        {
            if (route != PlayniteLibraryRoute.Browse) return ValueTask.CompletedTask;
            SelectBrowseCategory(null, sourceElementId);
            return ValueTask.CompletedTask;
        }

        if (PlayniteLibraryCategoryPolicy.Find(organization, targetCategoryId) is null)
                return ValueTask.CompletedTask;
        if (route == PlayniteLibraryRoute.Library)
        {
            RetainHomeCollectionForRouteTransition();
            if (_navigation.Push(PlayniteLibraryRoute.Browse, sourceElementId) !=
                    WidgetNavigationResult.Changed)
                return ValueTask.CompletedTask;
        }
        SelectBrowseCategory(targetCategoryId, sourceElementId);
        return ValueTask.CompletedTask;
    }

    private PlayniteLibraryDisplayItem? DisplayForSavedLocked(string savedId) =>
        _organization.Items.FirstOrDefault(item => item.SavedId == savedId) ??
        (_hiddenRows.Snapshot.Value ?? []).Where(item =>
                string.Equals(item.Value.SavedId, savedId, StringComparison.Ordinal))
            .Select(item => new PlayniteLibraryDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName))
            .FirstOrDefault() ??
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
            WidgetPagedResourceStatus.Refreshing when snapshot.Items.Count == 0 =>
                "Refreshing installed games…",
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
