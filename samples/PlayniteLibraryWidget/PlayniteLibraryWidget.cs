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
    private static readonly WidgetAppLibraryQuery InstalledRegistrations = new(
        InstalledOnly: false,
        Kind: null,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly IPlayniteLibraryApplicationService _application;
    private readonly WidgetCursorResource<PlayniteLibraryItem> _library;
    private readonly WidgetNavigator<PlayniteLibraryRoute> _navigation;
    private PlayniteLibraryPrivateState _organization = PlayniteLibraryPrivateState.Empty;
    private PlayniteLibraryAuthorityProjection _playniteAuthority =
        PlayniteLibraryAuthorityProjection.Empty;
    private long _stateRevision;
    private string? _variantSeedSavedId;
    private bool _organizationBusy;
    private string _status = "Playnite Library loads when visible";
    private string? _launchingSavedId;
    private readonly Dictionary<string, PlayniteLibraryLaunchState> _launchStates =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _launchStateRecency = [];
    private readonly PlayniteLibraryLaunchPersistenceCoordinator _launchPersistence = new();
    private PlayniteLibraryCollectionState _collectionState = new(InstalledGames);
    private PlayniteLibraryFixedRows _fixedRows = PlayniteLibraryFixedRows.Empty;
    private IReadOnlyList<WidgetAppLibrarySource> _sourceObservations = [];
    private PlayniteLibraryDetailsSelection? _detailsSelection;
    private PlayniteLibraryDetailsSelection? _actionSheetSelection;
    private PlayniteLibraryDetailsSelection? _titleEditorSelection;
    private string? _activeCategoryId;
    private string? _runningRevision;
    private long _fixedRowsRevision;
    private string? _pendingRestoredSavedId;
    private bool _preferLibraryContentFocus;
    private string? _heroSavedId;
    private int _heroIndex;

    internal PlayniteLibraryWidget(
        IPlayniteLibraryApplicationService application,
        IPlayniteBridgeClient? playniteClient = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _playniteClient = playniteClient;
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
    internal Task WhenLibraryIdleAsync(CancellationToken cancellationToken = default) =>
        _library.WhenIdleAsync(cancellationToken);
    internal Task WhenWarmStateIdleAsync(CancellationToken cancellationToken = default) =>
        Operations.WhenIdleAsync("playnite-library.warm-state", cancellationToken);

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        if (navigation.Route == PlayniteLibraryRoute.PlayniteConnection)
        {
            PlayniteLibraryConnectionState connection;
            lock (_gate) connection = CapturePlayniteConnectionLocked();
            var connectionView = PlayniteLibraryConnectionPresentation.Render(connection);
            return connectionView with
            {
                Root = _navigation.Scope(navigation, (StackElement)connectionView.Root),
                InitialFocusId = connectionView.InitialFocusId,
                ActiveInputScopeId = navigation.InputScopeId,
            };
        }

        PlayniteLibraryPresentationState state;
        PlayniteLibraryDetailsState? details = null;
        PlayniteLibraryDetailsState? actionSheet = null;
        PlayniteLibraryTitleEditorState? titleEditor = null;
        bool preferLibraryContentFocus;
        lock (_gate)
        {
            state = CapturePresentationStateLocked(navigation.Route);
            if (navigation.Route == PlayniteLibraryRoute.Details && _detailsSelection is { } selected)
                details = PlayniteLibraryDetailsPolicy.Project(selected, state.Collection,
                    state.FixedRows, state.Organization, _launchingSavedId, _launchStates,
                    _status, _variantSeedSavedId, _organizationBusy,
                    LifecycleState == WidgetLifecycleState.Interactive,
                    state.CompletionStatuses);
            if (_actionSheetSelection is { } actionSelection)
                actionSheet = PlayniteLibraryDetailsPolicy.Project(actionSelection,
                    state.Collection, state.FixedRows, state.Organization, _launchingSavedId,
                    _launchStates, _status, _variantSeedSavedId, _organizationBusy,
                    LifecycleState == WidgetLifecycleState.Interactive,
                    state.CompletionStatuses);
            if (_titleEditorSelection is { } titleSelection)
                titleEditor = PlayniteLibraryTitleEditor.Project(
                    titleSelection, _organization,
                    LifecycleState == WidgetLifecycleState.Interactive,
                    _organizationBusy);
            preferLibraryContentFocus = _preferLibraryContentFocus;
        }
        var view = titleEditor is not null
            ? PlayniteLibraryTitleEditor.Render(titleEditor)
            : actionSheet is not null
            ? PlayniteLibraryActionSheet.Render(actionSheet, state.Organization.Categories)
            : details is null
            ? PlayniteLibraryPresentation.Render(state)
            : PlayniteLibraryDetailsPresentation.Render(details);
        var root = titleEditor is not null || actionSheet is not null
            ? view.Root
            : _navigation.Scope(navigation, (StackElement)view.Root);
        return view with
        {
            Root = root,
            InitialFocusId = titleEditor is not null || actionSheet is not null
                ? view.InitialFocusId
                : navigation.Route is PlayniteLibraryRoute.Categories or
                    PlayniteLibraryRoute.Category
                ? view.InitialFocusId
                : preferLibraryContentFocus
                ? view.InitialFocusId
                : navigation.InitialFocusId ?? view.InitialFocusId,
            ActiveInputScopeId = titleEditor is not null
                ? PlayniteLibraryTitleEditor.ScopeId
                : actionSheet is not null ? PlayniteLibraryActionSheet.ScopeId
                : navigation.InputScopeId,
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _ = Operations.RunLatest("playnite-library.warm-state",
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
        RetirePlayniteConnection();
        if (_navigation.Value.Route == PlayniteLibraryRoute.Details)
            _navigation.Back();
        lock (_gate)
        {
            _launchingSavedId = null;
            _launchPersistence.Invalidate();
            _variantSeedSavedId = null;
            _organizationBusy = false;
            _fixedRows = PlayniteLibraryFixedRows.Empty;
            _sourceObservations = [];
            _playniteAuthority = PlayniteLibraryAuthorityProjection.Empty;
            _detailsSelection = null;
            _actionSheetSelection = null;
            _titleEditorSelection = null;
            _activeCategoryId = null;
            _fixedRowsRevision++;
            _status = "Playnite Library is paused";
        }
        Invalidate();
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
        SelectHeroForSource(action.SourceElementId);
        if (TitleEditorIsOpen())
        {
            if (action.ActionId == PlayniteLibraryTitleEditor.CloseAction)
                CloseTitleEditor();
            else if (action.ActionId == PlayniteLibraryTitleEditor.CommitAction &&
                     action.CommittedText is not null)
                await SetTitleOverrideAsync(action.CommittedText, cancellationToken)
                    .ConfigureAwait(false);
            else if (action.ActionId == PlayniteLibraryTitleEditor.ResetAction)
                await SetTitleOverrideAsync(null, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (ActionSheetIsOpen() && !IsActionSheetAction(action.ActionId)) return;
        if (action.ActionId.StartsWith(
                PlayniteLibraryCollectionPolicy.ActionPrefix, StringComparison.Ordinal))
        {
            SelectCollection(action.ActionId);
            return;
        }
        if (action.ActionId == PlayniteLibraryActionSheet.CloseAction)
        {
            CloseActionSheet();
            return;
        }
        if (action.ActionId.StartsWith(
                PlayniteLibraryActionSheet.CategoryActionPrefix, StringComparison.Ordinal))
        {
            await ToggleCategoryMembershipAsync(
                    action.ActionId[PlayniteLibraryActionSheet.CategoryActionPrefix.Length..],
                    action.SourceElementId,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var routeBeforeBack = _navigation.Value.Route;
        if (_navigation.TryHandleBack(action, action.SourceElementId))
        {
            if (routeBeforeBack == PlayniteLibraryRoute.Details)
            {
                lock (_gate) _detailsSelection = null;
            }
            else
            {
                if (routeBeforeBack == PlayniteLibraryRoute.PlayniteConnection)
                    RetirePlayniteConnection();
                await ReturnToLibraryAsync().ConfigureAwait(false);
            }
            return;
        }
        if (_library.TryHandlePagination(action, out _))
        {
            Operations.Cancel("playnite-library.launch-lifecycle");
            return;
        }
        switch (action.ActionId)
        {
            case PlayniteLibraryActionSheet.OpenAction:
                OpenActionSheet(action.SourceElementId);
                return;
            case PlayniteLibraryActionSheet.RefreshSourceAction:
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    ResolveActionSource(action.SourceElementId) is null) return;
                CloseActionSheet();
                Operations.Cancel("playnite-library.launch-lifecycle");
                _ = _library.Refresh();
                return;
            case PlayniteLibraryActionSheet.ManageCategoriesAction:
                OpenCategories(action.SourceElementId);
                return;
            case PlayniteLibraryActionSheet.EditTitleAction:
                OpenTitleEditor();
                return;
            case PlayniteLibraryActionSheet.DetailsAction:
                OpenDetails(action.SourceElementId);
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
                        PlayniteLibraryRoute.Details or PlayniteLibraryRoute.Category) ||
                    ResolveActionSource(action.SourceElementId) is not { } hideSource) return;
                if (await SetHiddenAsync(hideSource, hidden: true, cancellationToken)
                        .ConfigureAwait(false))
                {
                    var detailsRoute = _navigation.Value.Route == PlayniteLibraryRoute.Details;
                    if (!detailsRoute)
                        lock (_gate) _preferLibraryContentFocus = true;
                    CloseActionSheet();
                    if (detailsRoute)
                        CloseDetails(preferLibraryContentFocus: true);
                }
                return;
            case "playnite-library.completion.next":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    ResolveActionSource(action.SourceElementId) is not { } completionSource)
                    return;
                await CycleCompletionStatusAsync(completionSource, cancellationToken)
                    .ConfigureAwait(false);
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
                {
                    var detailsRoute = _navigation.Value.Route == PlayniteLibraryRoute.Details;
                    var sheetOpen = ActionSheetIsOpen();
                    var result = await ToggleVariantAsync(variantSource, cancellationToken)
                        .ConfigureAwait(false);
                    if (sheetOpen && result == PlayniteLibraryVariantActionResult.Started)
                        CloseActionSheet();
                    if (detailsRoute && result == PlayniteLibraryVariantActionResult.Started &&
                        _navigation.Value.Route == PlayniteLibraryRoute.Details)
                        CloseDetails();
                }
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
            case "playnite-library.add.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(PlayniteLibraryRoute.AddGames, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _collectionState = _collectionState.Reset(InstalledRegistrations);
                        _fixedRows = PlayniteLibraryFixedRows.Empty;
                        _fixedRowsRevision++;
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "playnite-library.add.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "playnite-library.running.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(PlayniteLibraryRoute.Running, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _collectionState = _collectionState.Reset(InstalledRegistrations);
                        _fixedRows = PlayniteLibraryFixedRows.Empty;
                        _fixedRowsRevision++;
                        _runningRevision = null;
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "playnite-library.running.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "playnite-library.hidden.open":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                if (_navigation.Push(PlayniteLibraryRoute.Hidden, action.SourceElementId) ==
                    WidgetNavigationResult.Changed)
                {
                    lock (_gate)
                    {
                        _collectionState = _collectionState.Reset(InstalledGames);
                        _fixedRows = PlayniteLibraryFixedRows.Empty;
                        _fixedRowsRevision++;
                        _pendingRestoredSavedId = null;
                        _preferLibraryContentFocus = false;
                    }
                    ReloadQuery();
                }
                return;
            case "playnite-library.hidden.back":
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                    await ReturnToLibraryAsync().ConfigureAwait(false);
                return;
            case "playnite-library.manual.toggle":
                if (LifecycleState != WidgetLifecycleState.Interactive ||
                    _navigation.Value.Route is not (PlayniteLibraryRoute.AddGames or
                        PlayniteLibraryRoute.Running)) return;
                if (_navigation.Value.Route == PlayniteLibraryRoute.Running)
                    await AddRunningAsync(action.SourceElementId, cancellationToken)
                        .ConfigureAwait(false);
                else
                    await ToggleManualAsync(action.SourceElementId, cancellationToken)
                        .ConfigureAwait(false);
                return;
            case "playnite-library.recent.clear":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(PlayniteLibraryOrganizationPolicy.ClearRecent,
                    "Recent launches cleared", cancellationToken).ConfigureAwait(false);
                lock (_gate)
                    if (_collectionState.Selection.Kind == PlayniteLibraryCollectionKind.Recent)
                        _collectionState = _collectionState.Select(
                            PlayniteLibraryCollectionPolicy.AllInstalled, InstalledGames);
                ReloadQuery();
                return;
            case "playnite-library.search.commit":
                if (action.CommittedText is null) return;
                ReplaceQuery(_collectionState.Query with
                {
                    SearchText = NormalizeSearch(action.CommittedText),
                });
                return;
            case "playnite-library.query.clear":
                lock (_gate)
                {
                    _collectionState = _navigation.Value.Route == PlayniteLibraryRoute.Library
                        ? _collectionState.ClearQuery(InstalledGames)
                        : _collectionState.Reset(
                            _navigation.Value.Route == PlayniteLibraryRoute.AddGames
                                ? InstalledRegistrations
                                : InstalledGames);
                }
                ReloadQuery();
                return;
            case "playnite-library.filter.favorites":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                lock (_gate)
                {
                    _collectionState = _collectionState.ToggleFavorites(InstalledGames);
                }
                ReloadQuery();
                return;
            case "playnite-library.filter.recent":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                lock (_gate)
                {
                    _collectionState = _collectionState.CycleRecent(InstalledGames);
                }
                ReloadQuery();
                return;
            case "playnite-library.filter.source":
                ReplaceQuery(_collectionState.Query with
                {
                    SourceAttribution = NextSource(),
                });
                return;
            case "playnite-library.filter.sort":
                ReplaceQuery(_collectionState.Query with
                {
                    Sort = _collectionState.Query.Sort switch
                {
                    WidgetAppLibrarySortOrder.DisplayName =>
                        WidgetAppLibrarySortOrder.DisplayNameDescending,
                    WidgetAppLibrarySortOrder.DisplayNameDescending =>
                        WidgetAppLibrarySortOrder.SourceThenDisplayName,
                    _ => WidgetAppLibrarySortOrder.DisplayName,
                }});
                return;
            case "playnite-library.categories.open":
                OpenCategories(action.SourceElementId);
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
                lock (_gate) _preferLibraryContentFocus = true;
                if (_navigation.Back(action.SourceElementId) == WidgetNavigationResult.Changed)
                {
                    await ReturnToLibraryAsync(preferContentFocus: true)
                        .ConfigureAwait(false);
                }
                return;
            case "playnite-library.category.back":
                if (_navigation.Value.Route != PlayniteLibraryRoute.Category) return;
                lock (_gate)
                {
                    _activeCategoryId = null;
                    _preferLibraryContentFocus = true;
                }
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

    public override async ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Context == ControllerInputContext.OpenWidget &&
            input.Phase is ControllerEventPhase.Pressed or ControllerEventPhase.Repeated &&
            _navigation.Value.Route == PlayniteLibraryRoute.Library)
        {
            PlayniteLibraryPresentationState state;
            lock (_gate) state = CapturePresentationStateLocked(PlayniteLibraryRoute.Library);
            var model = PlayniteLibraryHeroRailPolicy.Project(
                state, state.HeroSavedId, state.HeroIndex);
            var selected = PlayniteLibraryHeroRailPolicy.SelectFromInput(
                model, input.FocusedElementId, input.Button);
            if (selected is not null)
            {
                var index = Enumerable.Range(0, model.Items.Count)
                    .First(candidate => ReferenceEquals(model.Items[candidate], selected));
                var changed = false;
                lock (_gate)
                {
                    if (_navigation.Value.Route == PlayniteLibraryRoute.Library &&
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
            ? "playnite-library.previous"
            : "playnite-library.next";
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            action.SourceElementId is not PlayniteLibraryPresentation.ScrollId &&
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
        lock (_gate)
        {
            var choices = ProvenSourcesLocked();
            if (choices.Length == 0) return null;
            if (_collectionState.Query.SourceAttribution is null) return choices[0];
            var index = Array.FindIndex(choices, value => string.Equals(
                value, _collectionState.Query.SourceAttribution,
                StringComparison.OrdinalIgnoreCase));
            return index < 0 || index + 1 == choices.Length ? null : choices[index + 1];
        }
    }

    private void SelectCollection(string actionId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
        PlayniteLibraryCollectionSelection? selected;
        lock (_gate)
        {
            var current = _collectionState.Selection;
            selected = PlayniteLibraryCollectionPolicy.Resolve(actionId,
                PlayniteLibraryCollectionPolicy.Options(
                    PresentationOrganizationLocked(), ProvenSourcesLocked(), current));
            if (selected is null || selected == current) return;
            _collectionState = _collectionState.Select(selected, InstalledGames);
        }
        ReloadQuery(preserveContentFocus: true);
    }

    private string[] ProvenSourcesLocked() =>
        _organization.ProvenSources.ToArray();

    private void ReplaceQuery(WidgetAppLibraryQuery query, bool force = false)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            !force && query == _collectionState.Query) return;
        lock (_gate)
        {
            _collectionState = _collectionState with { Query = query };
        }
        ReloadQuery();
    }

    private WidgetOperationHandle? ReloadQuery(bool preserveContentFocus = false)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return null;
        Operations.Cancel("playnite-library.launch-lifecycle");
        lock (_gate)
        {
            _status = "Applying library filters…";
            _launchingSavedId = null;
            _launchPersistence.Invalidate();
            _fixedRows = PlayniteLibraryFixedRows.Empty;
            _fixedRowsRevision++;
            if (!preserveContentFocus) _preferLibraryContentFocus = false;
        }
        _library.Reset(invalidate: false);
        var operation = _library.EnsureLoaded();
        Invalidate();
        return operation;
    }

    private async Task ReloadQueryAsync(bool preserveContentFocus = false)
    {
        var operation = ReloadQuery(preserveContentFocus);
        if (operation is { } admitted)
            await admitted.Completion.ConfigureAwait(false);
    }

    private async Task ReturnToLibraryAsync(bool preferContentFocus = false)
    {
        lock (_gate)
        {
            _collectionState = _collectionState.Reset(InstalledGames);
            _fixedRows = PlayniteLibraryFixedRows.Empty;
            _fixedRowsRevision++;
            _activeCategoryId = null;
            _preferLibraryContentFocus = preferContentFocus;
        }
        await ReloadQueryAsync(preferContentFocus).ConfigureAwait(false);
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
        lock (_gate) _preferLibraryContentFocus =
            preferContentFocus || restoredSavedId is not null;
        Invalidate();
    }

    private WidgetAppLibraryQuery EffectiveQueryLocked(PlayniteLibraryRoute route)
    {
        var organization = PresentationOrganizationLocked();
        IEnumerable<string>? savedIds = route == PlayniteLibraryRoute.Hidden
            ? organization.ExcludedSavedIds
            : null;
        if (route == PlayniteLibraryRoute.Category)
            savedIds = PlayniteLibraryCategoryPolicy.Find(
                organization, _activeCategoryId)?.SavedIds ?? [];
        if (_collectionState.FavoriteFilter) savedIds = organization.FavoriteSavedIds;
        if (_collectionState.RecentMode == PlayniteLibraryRecentMode.RecentOnly)
            savedIds = savedIds is null
                ? _organization.RecentSavedIds
                : savedIds.Intersect(_organization.RecentSavedIds, StringComparer.Ordinal);
        if (_collectionState.ManualFilter)
            savedIds = savedIds is null
                ? _organization.ManualSavedIds
                : savedIds.Intersect(_organization.ManualSavedIds, StringComparer.Ordinal);
        return _collectionState.Query with
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
        lock (_gate)
        {
            collectionState = _collectionState;
            query = EffectiveQueryLocked(route);
            organization = PresentationOrganizationLocked();
        }
        if (route == PlayniteLibraryRoute.Running)
        {
            if (direction is not null || cursor is not null)
                throw new InvalidOperationException("Running apps are a single bounded page.");
            var observed = await _application.ObserveRunningAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var running = observed.Items.Select(candidate => PlayniteLibraryItem.From(
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
                if (_navigation.Value.Route != PlayniteLibraryRoute.Running)
                    throw new OperationCanceledException(cancellationToken);
                _runningRevision = observed.Revision;
                _sourceObservations = [];
                _status = running.Length == 0
                    ? "No visible applications match the installed library"
                    : $"{running.Length} visible installed application{(running.Length == 1 ? "" : "s")}";
            }
            return new(running, null, null);
        }
        if (route == PlayniteLibraryRoute.Hidden && organization.ExcludedSavedIds.Count == 0)
        {
            lock (_gate) _status = "No hidden games";
            return new([], null, null);
        }
        if (route == PlayniteLibraryRoute.Category &&
            PlayniteLibraryCategoryPolicy.Find(organization, _activeCategoryId) is not
                { SavedIds.Count: > 0 })
        {
            lock (_gate) _status = "Category is empty";
            return new([], null, null);
        }
        var categoryName = route == PlayniteLibraryRoute.Category
            ? PlayniteLibraryCategoryPolicy.Find(organization, _activeCategoryId)?.Name
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
        var rawArtwork = await ResolveArtworkAsync(page.Items, cancellationToken)
            .ConfigureAwait(false);
        var rawItems = page.Items.Select(item => ProjectArtwork(
            item, rawArtwork.GetValueOrDefault(item.SavedId))).ToArray();
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
            var resolvedArtwork = await ResolveArtworkAsync(resolved, cancellationToken)
                .ConfigureAwait(false);
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
                    .Select(item => ProjectArtwork(
                        item, resolvedArtwork.GetValueOrDefault(item.SavedId)))
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
                    .Select(item => ProjectArtwork(
                        item, resolvedArtwork.GetValueOrDefault(item.SavedId)))
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
                .Select(item => ProjectArtwork(
                    item, resolvedArtwork.GetValueOrDefault(item.SavedId)))
                .Take(WidgetAppLibraryService.MaximumSavedItems)
                .ToArray();
            rawFixedRows = new(recent, manual,
                titleMatches.Select(item => resolvedBySavedId[item.Value.SavedId])
                    .Select(item => ProjectArtwork(
                        item, resolvedArtwork.GetValueOrDefault(item.SavedId))).ToArray());
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
        lock (_gate)
        {
            if (route == _navigation.Value.Route && collectionState == _collectionState)
            {
                _sourceObservations = page.Sources.ToArray();
                provenSources = PlayniteLibrarySourceCatalog.Reconcile(
                    organization.ProvenSources, collectionState, direction, rawItems,
                    page.Sources, page.Before is null && page.After is null);
            }
        }
        var projectionSaved = await PersistProjectionAsync(
                projectionItems, provenSources, cancellationToken)
            .ConfigureAwait(false);
        var items = rawItems.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(organization, item.Value)))
            .Where(item => MatchesFixedQuery(item.Value, query))
            .ToArray();
        lock (_gate)
        {
            if (direction is null &&
                route == _navigation.Value.Route && collectionState == _collectionState)
            {
                _fixedRows = fixedRows;
                _fixedRowsRevision++;
            }
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

    private async ValueTask<IReadOnlyDictionary<string, string>> ResolveArtworkAsync(
        IReadOnlyList<WidgetAppLibraryItem> items,
        CancellationToken cancellationToken)
    {
        var content = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!_application.OwnsArtworkContent) return content;
        foreach (var item in items)
        {
            var artwork = item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
            if (artwork is null) continue;
            var png = await _application.ResolveArtworkAsync(artwork, cancellationToken)
                .ConfigureAwait(false);
            if (png is not null) content[item.SavedId] = png;
        }
        return content;
    }

    private PlayniteLibraryItem ProjectArtwork(
        WidgetAppLibraryItem item,
        string? pngBase64)
    {
        if (!_application.OwnsArtworkContent)
            return PlayniteLibraryItem.From(item, pngBase64);
        return PlayniteLibraryItem.From(item with
        {
            Presentation = item.Presentation with
            {
                Artwork = new WidgetAppLibraryArtworkSet([]),
            },
        }, pngBase64);
    }

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
            PlayniteLibraryFixedRows fixedRows;
            lock (_gate)
            {
                fixedRows = _fixedRows;
                fixedRowsRevision = _fixedRowsRevision;
            }
            selected = _library.Snapshot.Items.Concat(fixedRows.All).FirstOrDefault(item =>
                string.Equals(
                PlayniteLibraryIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
            if (selected is null) return;
            if (_library.Snapshot.Items.Any(item => item.Key == selected.Key))
                _library.SelectAnchor(selected.Key, invalidate: false);
            lock (_gate)
            {
                _launchingSavedId = selected.Value.SavedId;
                RemoveLaunchStateLocked(selected.Value.SavedId);
                _status = $"Pending · {selected.Value.Presentation.DisplayName}";
            }
            collectionRevision = _library.Snapshot.Revision;
            Invalidate();
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
                    fixedRowsRevision == _fixedRowsRevision &&
                    IsCurrentResolved(selected.Key))
                {
                    SetLaunchStateLocked(
                        selected.Value.SavedId, PlayniteLibraryLaunchState.Failed);
                    _status = $"Failed · {LaunchError(exception)}";
                }
            }
        }
        finally
        {
            lock (_gate)
                if (_launchPersistence.IsCurrent(generation)) _launchingSavedId = null;
            Invalidate();
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
        bool filteringFavorites;
        lock (_gate)
        {
            favorite = !_playniteAuthority.FavoriteGameIds.Contains(
                display.SavedId, StringComparer.Ordinal);
            filteringFavorites = _collectionState.FavoriteFilter;
            _organizationBusy = true;
        }
        Invalidate();
        var applied = false;
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
                    _status = favorite
                        ? $"Favorited {display.DisplayName}"
                        : $"Removed {display.DisplayName} from favorites";
                }
        }
        finally
        {
            lock (_gate) _organizationBusy = false;
            Invalidate();
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
        lock (_gate) _organizationBusy = true;
        Invalidate();
        var applied = false;
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
                    _status = hidden
                        ? $"Hidden {display.DisplayName}"
                        : $"Restored {display.DisplayName}";
                }
        }
        finally
        {
            lock (_gate) _organizationBusy = false;
            Invalidate();
        }
        if (applied)
        {
            if (!hidden)
                lock (_gate) _pendingRestoredSavedId = display.SavedId;
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
        lock (_gate) _organizationBusy = true;
        Invalidate();
        try
        {
            var statuses = await _application.GetCompletionStatusesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (statuses.Count == 0)
            {
                lock (_gate) _status = "No Playnite completion statuses are available";
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
                _status = $"Completion · {next}";
            }
        }
        finally
        {
            lock (_gate) _organizationBusy = false;
            Invalidate();
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
            PlayniteLibraryIdentity.FocusId("add", item.Key), sourceElementId,
            StringComparison.Ordinal));
        if (candidate is null) return;
        string? revision;
        lock (_gate)
        {
            if (_navigation.Value.Route != PlayniteLibraryRoute.Running ||
                PlayniteLibraryOrganizationPolicy.ReferencedSavedIds(_organization)
                    .Contains(candidate.Value.SavedId, StringComparer.Ordinal)) return;
            revision = _runningRevision;
        }
        if (revision is null) return;
        var current = await _application.ConfirmRunningAsync(
            candidate.Value.SavedId, revision, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            lock (_gate) _status = "Running app changed · refresh and try again";
            Invalidate();
            return;
        }
        lock (_gate)
            if (_navigation.Value.Route != PlayniteLibraryRoute.Running ||
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
        var display = new PlayniteLibraryDisplayItem(item.SavedId,
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
        PlayniteLibraryDisplayItem? first;
        PlayniteLibraryVariantGroup? existing;
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
            return PlayniteLibraryVariantActionResult.Started;
        }
        if (first is null || first.SavedId == current.SavedId)
        {
            lock (_gate) _status = first is null
                ? "The first selected game is no longer available"
                : "Choose a different game for the second variant";
            Invalidate();
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
            lock (_gate) _status = "Group variants before choosing a preferred launch";
            Invalidate();
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
                    _status = saved ? success : failure;
            }
            Invalidate();
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
        PlayniteLibraryFixedRows fixedRows;
        lock (_gate) fixedRows = _fixedRows;
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
        PlayniteLibraryRoute route)
    {
        var organization = PlayniteLibraryTitlePolicy.Project(
            PresentationOrganizationLocked());
        var collection = _library.Snapshot with
        {
            Items = _library.Snapshot.Items.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
        };
        var fixedRows = new PlayniteLibraryFixedRows(
            _fixedRows.Recent.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            _fixedRows.Manual.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray(),
            _fixedRows.TitleMatches.Select(item => item.WithValue(
                PlayniteLibraryTitlePolicy.Project(_organization, item.Value))).ToArray());
        return new(
        collection,
        organization,
        StatusLocked(_library.Snapshot),
        _launchingSavedId,
        new Dictionary<string, PlayniteLibraryLaunchState>(
            _launchStates, StringComparer.Ordinal),
        _organizationBusy,
        LifecycleState == WidgetLifecycleState.Interactive,
        _collectionState.Query,
        _collectionState.RecentMode,
        _collectionState.FavoriteFilter,
        route,
        fixedRows,
        _sourceObservations,
        _heroSavedId,
        _heroIndex)
    {
        ActiveCategoryId = _activeCategoryId,
        Collections = PlayniteLibraryCollectionPolicy.Options(
            organization, ProvenSourcesLocked(), _collectionState.Selection),
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

    private void OpenDetails(string sourceElementId)
    {
        var route = _navigation.Value.Route;
        if (LifecycleState != WidgetLifecycleState.Interactive || route is not
            (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Category or
                PlayniteLibraryRoute.Details)) return;
        PlayniteLibraryDetailsSelection? retainedSelection;
        PlayniteLibraryFixedRows fixedRows;
        lock (_gate)
        {
            retainedSelection = _actionSheetSelection;
            fixedRows = _fixedRows;
        }
        var snapshot = _library.Snapshot;
        var fromActionSheet = retainedSelection is not null && string.Equals(
            sourceElementId, PlayniteLibraryActionSheet.DetailsItemId,
            StringComparison.Ordinal);
        var selection = fromActionSheet
            ? PlayniteLibraryDetailsPolicy.ResolveActionSource(
                retainedSelection, sourceElementId, snapshot, fixedRows) is not null
                ? retainedSelection
                : null
            : PlayniteLibraryDetailsPolicy.Select(sourceElementId, snapshot, fixedRows);
        if (selection is null)
        {
            if (fromActionSheet) CloseActionSheet();
            return;
        }
        lock (_gate)
        {
            if (fromActionSheet) _actionSheetSelection = null;
            _detailsSelection = selection;
            _heroSavedId = selection.SavedId;
            _preferLibraryContentFocus = false;
        }
        if (route == PlayniteLibraryRoute.Details)
        {
            Invalidate();
            return;
        }
        if (_navigation.Push(PlayniteLibraryRoute.Details, selection.ReturnFocusId) !=
            WidgetNavigationResult.Changed)
            lock (_gate) _detailsSelection = null;
    }

    private string? ResolveActionSource(string sourceElementId)
    {
        PlayniteLibraryDetailsSelection? selection;
        PlayniteLibraryFixedRows fixedRows;
        lock (_gate)
        {
            selection = _actionSheetSelection ?? _detailsSelection;
            fixedRows = _fixedRows;
        }
        return PlayniteLibraryDetailsPolicy.ResolveActionSource(
            selection, sourceElementId, _library.Snapshot, fixedRows);
    }

    private void SelectHeroForSource(string sourceElementId)
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return;
        PlayniteLibraryFixedRows fixedRows;
        lock (_gate) fixedRows = _fixedRows;
        var selected = _library.Snapshot.Items.Concat(fixedRows.All)
            .FirstOrDefault(item => string.Equals(
                PlayniteLibraryIdentity.FocusId("grid", item.Key), sourceElementId,
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

    private void OpenActionSheet(string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                PlayniteLibraryRoute.Details or PlayniteLibraryRoute.Category)) return;
        PlayniteLibraryDetailsSelection? selection;
        if (_navigation.Value.Route == PlayniteLibraryRoute.Details)
        {
            lock (_gate) selection = _detailsSelection;
        }
        else
        {
            PlayniteLibraryFixedRows fixedRows;
            lock (_gate) fixedRows = _fixedRows;
            selection = PlayniteLibraryDetailsPolicy.Select(
                sourceElementId, _library.Snapshot, fixedRows);
        }
        if (selection is null) return;
        lock (_gate)
        {
            _actionSheetSelection = selection;
            _heroSavedId = selection.SavedId;
        }
        Invalidate();
    }

    private bool ActionSheetIsOpen()
    {
        lock (_gate) return _actionSheetSelection is not null;
    }

    private static bool IsActionSheetAction(string actionId) => actionId is
        PlayniteLibraryActionSheet.CloseAction or
        PlayniteLibraryActionSheet.RefreshSourceAction or
        PlayniteLibraryActionSheet.ManageCategoriesAction or
        PlayniteLibraryActionSheet.EditTitleAction or
        PlayniteLibraryActionSheet.DetailsAction or
        "playnite-library.favorite" or "playnite-library.hide" or
        "playnite-library.variant" or "playnite-library.prefer" ||
        actionId.StartsWith(
            PlayniteLibraryActionSheet.CategoryActionPrefix, StringComparison.Ordinal);

    private void CloseActionSheet()
    {
        var changed = false;
        lock (_gate)
        {
            if (_actionSheetSelection is not null)
            {
                _actionSheetSelection = null;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private bool TitleEditorIsOpen()
    {
        lock (_gate) return _titleEditorSelection is not null;
    }

    private void OpenTitleEditor()
    {
        lock (_gate)
            if (_actionSheetSelection is { } selection)
                _titleEditorSelection = selection;
        Invalidate();
    }

    private void CloseTitleEditor()
    {
        var changed = false;
        lock (_gate)
        {
            if (_titleEditorSelection is not null)
            {
                _titleEditorSelection = null;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private async Task SetTitleOverrideAsync(
        string? title,
        CancellationToken cancellationToken)
    {
        PlayniteLibraryDetailsSelection? selection;
        PlayniteLibraryDisplayItem? providerDisplay;
        lock (_gate)
        {
            selection = _titleEditorSelection;
            providerDisplay = selection is null
                ? null
                : DisplayForSavedLocked(selection.SavedId);
        }
        if (selection is null || providerDisplay is null) return;
        if (!string.IsNullOrWhiteSpace(title) &&
            PlayniteLibraryTitlePolicy.NormalizeTitle(title) is null)
        {
            lock (_gate) _status =
                $"Title must be 1–{PlayniteLibraryPrivateState.MaximumTitleLength} characters";
            Invalidate();
            return;
        }
        var normalized = PlayniteLibraryTitlePolicy.NormalizeTitle(title);
        var saved = await MutateOrganizationAsync(
                state => PlayniteLibraryTitlePolicy.Set(state, providerDisplay, normalized),
                normalized is null
                    ? $"Reset title to {providerDisplay.DisplayName}"
                    : $"Title set to {normalized}",
                cancellationToken,
                "Title was not saved · organization changed or reached the 64 KiB budget")
            .ConfigureAwait(false);
        if (saved) _ = _library.Refresh();
    }

    private void OpenCategories(string sourceElementId)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive ||
            _navigation.Value.Route is not (PlayniteLibraryRoute.Library or
                PlayniteLibraryRoute.Details or PlayniteLibraryRoute.Category)) return;
        CloseActionSheet();
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library)
            _navigation.Back(sourceElementId);
        lock (_gate)
        {
            _activeCategoryId = null;
            _preferLibraryContentFocus = false;
        }
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
        lock (_gate)
        {
            _activeCategoryId = categoryId;
            _collectionState = _collectionState.Reset(InstalledGames);
            _fixedRows = PlayniteLibraryFixedRows.Empty;
            _fixedRowsRevision++;
            _preferLibraryContentFocus = false;
        }
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
            lock (_gate) _status =
                $"Category name must be 1–{PlayniteLibraryPrivateState.MaximumCategoryNameLength} characters";
            Invalidate();
            return;
        }
        lock (_gate) _organizationBusy = true;
        Invalidate();
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
                _status = created is null
                    ? "Category was not created"
                    : $"Created category {normalized}";
            }
        }
        finally
        {
            lock (_gate) _organizationBusy = false;
            Invalidate();
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
        lock (_gate) _organizationBusy = true;
        Invalidate();
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
                    _status = included
                        ? $"Removed {display.DisplayName} from {categoryName}"
                        : $"Added {display.DisplayName} to {categoryName}";
                }
            }
        }
        finally
        {
            lock (_gate) _organizationBusy = false;
            Invalidate();
        }
    }

    private async Task SwitchCollectionAsync(
        PlayniteLibraryCollectionDirection direction,
        string sourceElementId)
    {
        var route = _navigation.Value.Route;
        if (LifecycleState != WidgetLifecycleState.Interactive || ActionSheetIsOpen() ||
            route is not (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Category)) return;

        string? focusedSavedId = null;
        if (ResolveActionSource(sourceElementId) is { } exactSource)
            focusedSavedId = DisplayForSource(exactSource)?.SavedId;

        PlayniteLibraryPrivateState organization;
        string? currentCategoryId;
        lock (_gate)
        {
            if (_organizationBusy) return;
            organization = PresentationOrganizationLocked();
            currentCategoryId = route == PlayniteLibraryRoute.Category
                ? _activeCategoryId
                : null;
            focusedSavedId ??= _heroSavedId;
        }
        if (organization.Categories.Count == 0) return;
        var targetCategoryId = PlayniteLibraryCategoryPolicy.Cycle(
            organization.Categories, currentCategoryId, direction);

        Operations.Cancel("playnite-library.launch-lifecycle");
        if (targetCategoryId is null)
        {
            if (route != PlayniteLibraryRoute.Category) return;
            lock (_gate)
            {
                _activeCategoryId = null;
                _heroSavedId = focusedSavedId;
                _preferLibraryContentFocus = true;
            }
            if (_navigation.Back(sourceElementId) == WidgetNavigationResult.Changed)
                await ReturnToLibraryAsync(preferContentFocus: true).ConfigureAwait(false);
            return;
        }

        lock (_gate)
        {
            if (PlayniteLibraryCategoryPolicy.Find(_organization, targetCategoryId) is null)
                return;
            _activeCategoryId = targetCategoryId;
            _heroSavedId = focusedSavedId;
            _collectionState = _collectionState.Reset(InstalledGames);
            _fixedRows = PlayniteLibraryFixedRows.Empty;
            _fixedRowsRevision++;
            _preferLibraryContentFocus = false;
        }
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
        _library.Snapshot.Items.Concat(_fixedRows.All)
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

    private string StatusLocked(WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot) =>
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
