using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

public enum GamesAppsViewState
{
    Initial,
    Loading,
    Ready,
    Empty,
    PermissionDenied,
    LifecycleDenied,
    ServiceUnavailable,
    Error,
}

public enum GamesAppsPage
{
    Library,
    Catalog,
    Running,
}

/// <summary>
/// Controller-first installed application library. Widget code receives only
/// bounded names, conservative kinds, and opaque host IDs. Listing and launch
/// remain separate consent-gated broker operations.
/// </summary>
public sealed class GamesAppsWidget : Widget
{
    private const string RetryActionId = "games.retry";
    private const string RefreshCatalogActionId = "games.refresh-catalog";
    public const int ColdLoadingDelayMilliseconds = 150;
    public const int PageSize = 32;
    private const int MaximumCuratedItems = GamesAppsLibraryPolicy.MaximumCuratedItems;
    public const int MaximumItems =
        MaximumCuratedItems + GamesAppsLibraryPolicy.MaximumExcludedGames;
    private const string LibraryLoadOperationKey = "games.library.load";
    private const string PageLoadOperationKey = "games.page.load";
    private static readonly TimeSpan MinimumInitialRouteLoadingDuration =
        TimeSpan.FromSeconds(1);
    private static readonly WidgetAppLibraryQuery AllInstalledQuery = new();
    private static readonly WidgetAppLibraryQuery InstalledGamesQuery =
        new(Kind: WidgetAppLibraryKind.Game);
    private static readonly GamesAppsPage[] RootPages =
    [
        GamesAppsPage.Library,
        GamesAppsPage.Catalog,
        GamesAppsPage.Running,
    ];

    private sealed record LibraryPersistenceResult(bool Saved, bool Rejected);

    private readonly object _gate = new();
    private readonly WidgetNavigator<GamesAppsPage> _navigation;
    private readonly WidgetTimedMutation _toastExpiry;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private IReadOnlyList<WidgetAppLibraryItem> _items = [];
    private IReadOnlyList<WidgetAppLibraryItem> _libraryItems = [];
    private readonly HashSet<string> _resolvedSavedIds = new(StringComparer.Ordinal);
    private GamesAppsViewState _viewState = GamesAppsViewState.Initial;
    private string _status = "Your launch library loads when this widget becomes visible";
    private string? _selectedAppId;
    private string? _launchingAppId;
    private bool _loadingMore;
    private bool _libraryMutationBusy;
    private bool _catalogRefreshBusy;
    private bool _hasLibrarySnapshot;
    private GamesAppsCatalogState _catalog = GamesAppsCatalogState.Empty;
    private string? _runningRevision;
    private GamesAppsToastNotice? _toast;
    private long _generation;
    private long _stateRevision;
    private GamesAppsLibraryState _persistedLibraryState = new(3, [], null);

    public GamesAppsWidget() : this(TimeProvider.System) { }

    internal GamesAppsWidget(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _navigation = CreateNavigatorWithOptions(
            "games.navigation",
            GamesAppsPage.Library,
            new WidgetNavigatorOptions<GamesAppsPage>
            {
                SharedRootScopeId = "games-apps",
                RootRoutes = RootPages,
            });
        _toastExpiry = CreateTimedMutation(
            WidgetOperationLifetime.Active,
            _timeProvider);
    }

    public GamesAppsViewState ViewState { get { lock (_gate) return _viewState; } }
    public IReadOnlyList<WidgetAppLibraryItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }
    public string? SelectedAppId { get { lock (_gate) return _selectedAppId; } }
    public bool HasNextPage { get { lock (_gate) return _catalog.After is not null; } }
    public GamesAppsPage Page => _navigation.Value.RootRoute;
    public IReadOnlyList<WidgetAppLibraryItem> CuratedItems
    {
        get
        {
            lock (_gate)
            {
                var byId = _libraryItems.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
                return _persistedLibraryState.SavedIds.Where(byId.ContainsKey)
                    .Select(id => byId[id]).ToArray();
            }
        }
    }

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        GamesAppsPresentationState presentation;
        lock (_gate)
        {
            presentation = new GamesAppsPresentationState(
                navigation,
                _viewState,
                _status,
                _items.ToArray(),
                _libraryItems.Select(item => item.SavedId).ToArray(),
                _resolvedSavedIds.ToHashSet(StringComparer.Ordinal),
                _selectedAppId,
                _launchingAppId,
                _loadingMore || _libraryMutationBusy,
                _libraryMutationBusy,
                _catalogRefreshBusy,
                _catalog.After is not null,
                _catalog.CanLoadPrevious,
                LifecycleState,
                _toast);
        }
        var view = GamesAppsPresentation.Render(presentation);
        var scoped = _navigation.Scope(navigation, view.Root);
        return view with
        {
            Root = scoped,
            ActiveInputScopeId = navigation.InputScopeId,
            InitialFocusId = navigation.Revision == 0
                ? view.InitialFocusId
                : navigation.InitialFocusId,
            FocusGroupEntryRequest = navigation.FocusGroupEntryRequest,
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime) =>
        StartActiveRunAsync(activeLifetime);

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        var wasVisible = previous is
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive;
        var isVisible = current is
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive;
        // OnActivated publishes the first entering-visible projection after it
        // restores retained rows. Publishing here would admit the cold extent
        // before that ordered restore completes.
        if (wasVisible || !isVisible)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.ActionId)
        {
            case "games.open-library":
                if (LifecycleState == WidgetLifecycleState.Interactive)
                    OpenLibrary(enterRememberedContent: true);
                return;
            case "games.open-catalog":
                OpenCatalog(
                    enterRememberedContent: true,
                    force: false);
                return;
            case "games.open-running":
                OpenRunning(
                    enterRememberedContent: true,
                    force: false);
                return;
            case "games.section.previous":
                SwitchRoot(-1);
                return;
            case "games.section.next":
                SwitchRoot(1);
                return;
            case "games.toggle-curation":
                string? catalogAppId = null;
                lock (_gate)
                {
                    if (Page is GamesAppsPage.Catalog or GamesAppsPage.Running)
                        catalogAppId = _items.FirstOrDefault(item => string.Equals(
                            GamesAppsPresentation.CatalogElementId(item.SavedId), action.SourceElementId,
                            StringComparison.Ordinal))?.AppId;
                }
                if (catalogAppId is not null)
                {
                    if (Page == GamesAppsPage.Running)
                        await AddRunningAsync(catalogAppId, cancellationToken).ConfigureAwait(false);
                    else
                        await ToggleCuratedAsync(catalogAppId, cancellationToken).ConfigureAwait(false);
                }
                return;
            case "games.remove":
                string? curatedAppId = null;
                lock (_gate)
                {
                    if (Page == GamesAppsPage.Library)
                        curatedAppId = _items.FirstOrDefault(item => string.Equals(
                            GamesAppsPresentation.LibraryElementId(item.SavedId), action.SourceElementId,
                            StringComparison.Ordinal))?.AppId;
                }
                if (curatedAppId is not null)
                    await RemoveCuratedAsync(curatedAppId, cancellationToken).ConfigureAwait(false);
                return;
            case RetryActionId:
            case RefreshCatalogActionId:
                RefreshCurrentRoute();
                return;
            case "games.load-more":
                LoadMore();
                return;
            case "games.previous-page":
                LoadPreviousPage();
                return;
            case "games.launch":
                string? appId = null;
                lock (_gate)
                {
                    if (Page == GamesAppsPage.Library)
                    {
                        var selected = _items.FirstOrDefault(item => string.Equals(
                            GamesAppsPresentation.LibraryElementId(item.SavedId), action.SourceElementId,
                            StringComparison.Ordinal));
                        if (selected is not null && _resolvedSavedIds.Contains(selected.SavedId))
                            appId = selected.AppId;
                    }
                }
                if (appId is not null)
                    await LaunchAsync(appId, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task ToggleCuratedAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token)
                .ConfigureAwait(false);
            if (!acquired || LifecycleState != WidgetLifecycleState.Interactive) return;
            long generation;
            long revision;
            GamesAppsLibraryState baseline;
            WidgetAppLibraryItem? item;
            GamesAppsLibraryState desiredState;
            string toastMessage;
            IReadOnlyList<WidgetAppLibraryItem> candidateItems;
            var toastTone = ToastTone.Success;
            var rejected = false;
            lock (_gate)
            {
                if (Page is not (GamesAppsPage.Catalog or GamesAppsPage.Running)) return;
                item = _items.FirstOrDefault(candidate =>
                    string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
                if (item is null) return;
                generation = Interlocked.Read(ref _generation);
                revision = _stateRevision;
                baseline = _persistedLibraryState;
                candidateItems = _libraryItems.Concat(_items)
                    .DistinctBy(candidate => candidate.SavedId, StringComparer.Ordinal).ToArray();
                var isVisibleMember = _libraryItems.Any(candidate => string.Equals(
                    candidate.SavedId, item.SavedId, StringComparison.Ordinal));
                var mutation = GamesAppsLibraryPolicy.Toggle(
                    baseline,
                    item,
                    isVisibleMember,
                    ResolveCuratedItemsLocked().Select(candidate => candidate.SavedId).ToArray());
                desiredState = mutation.State;
                rejected = !mutation.Accepted;
                toastTone = rejected ? ToastTone.Warning : ToastTone.Success;
                toastMessage = mutation.Rejection switch
                {
                    GamesAppsLibraryMutationRejection.LibraryFull =>
                        $"Your library can hold {MaximumCuratedItems} items",
                    GamesAppsLibraryMutationRejection.ExclusionStorageFull =>
                        "Add a previously excluded game back before removing another",
                    _ => isVisibleMember
                        ? $"Removed {GamesAppsAppLibraryPresentation.DisplayName(item)}"
                        : $"Added {GamesAppsAppLibraryPresentation.DisplayName(item)} to your library",
                };
                _libraryMutationBusy = !rejected;
            }
            Invalidate();
            if (rejected)
            {
                ShowToast("Library unchanged", toastMessage, toastTone);
                return;
            }
            var persistence = await PersistLibraryAsync(
                    desiredState.SavedIds,
                    desiredState.AutoGameSavedIds,
                    desiredState.ExcludedGameSavedIds,
                    desiredState.SelectedSavedId,
                    commandLifetime.Token, candidateItems,
                    generation, baseline, revision)
                .ConfigureAwait(false);
            commandLifetime.Token.ThrowIfCancellationRequested();
            if (persistence.Saved)
            {
                lock (_gate) _status = toastMessage;
            }
            ShowToast(
                persistence.Saved ? "Library updated" : "Library not saved",
                persistence.Saved
                    ? toastMessage
                    : persistence.Rejected
                        ? "Add a previously excluded game back before removing another"
                        : "The durable library could not be updated",
                persistence.Saved ? toastTone : ToastTone.Danger);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            if (acquired)
            {
                lock (_gate) _libraryMutationBusy = false;
                _commandGate.Release();
                Invalidate();
            }
        }
    }

    private async Task RemoveCuratedAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token)
                .ConfigureAwait(false);
            if (!acquired || LifecycleState != WidgetLifecycleState.Interactive) return;
            long generation;
            long revision;
            GamesAppsLibraryState baseline;
            WidgetAppLibraryItem? item;
            GamesAppsLibraryState desiredState;
            IReadOnlyList<WidgetAppLibraryItem> candidateItems;
            string toastMessage;
            var rejected = false;
            lock (_gate)
            {
                if (Page != GamesAppsPage.Library) return;
                item = _items.FirstOrDefault(candidate =>
                    string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
                if (item is null || !_persistedLibraryState.SavedIds.Contains(
                        item.SavedId, StringComparer.Ordinal)) return;
                generation = Interlocked.Read(ref _generation);
                revision = _stateRevision;
                baseline = _persistedLibraryState;
                candidateItems = _libraryItems.ToArray();
                var mutation = GamesAppsLibraryPolicy.Remove(
                    baseline,
                    item,
                    ResolveCuratedItemsLocked().Select(candidate => candidate.SavedId).ToArray());
                desiredState = mutation.State;
                rejected = !mutation.Accepted;
                toastMessage = rejected
                    ? "Add a previously excluded game back before removing another"
                    : $"Removed {GamesAppsAppLibraryPresentation.DisplayName(item)}";
                _libraryMutationBusy = !rejected;
            }
            Invalidate();
            if (rejected)
            {
                ShowToast("Library unchanged", toastMessage, ToastTone.Warning);
                return;
            }
            var persistence = await PersistLibraryAsync(
                    desiredState.SavedIds,
                    desiredState.AutoGameSavedIds,
                    desiredState.ExcludedGameSavedIds,
                    desiredState.SelectedSavedId,
                    commandLifetime.Token, candidateItems,
                    generation, baseline, revision)
                .ConfigureAwait(false);
            commandLifetime.Token.ThrowIfCancellationRequested();
            if (persistence.Saved)
            {
                lock (_gate) _status = toastMessage;
            }
            ShowToast(
                persistence.Saved ? "Library updated" : "Library not saved",
                persistence.Saved
                    ? toastMessage
                    : persistence.Rejected
                        ? "Add a previously excluded game back before removing another"
                        : "The durable library could not be updated",
                persistence.Saved ? ToastTone.Success : ToastTone.Danger);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            if (acquired)
            {
                lock (_gate) _libraryMutationBusy = false;
                _commandGate.Release();
                Invalidate();
            }
        }
    }

    private IReadOnlyList<WidgetAppLibraryItem> ResolveCuratedItemsLocked()
    {
        var byId = _libraryItems.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        return _persistedLibraryState.SavedIds.Where(byId.ContainsKey)
            .Select(id => byId[id]).ToArray();
    }

    private string LibraryStatusLocked()
    {
        var curated = ResolveCuratedItemsLocked();
        var count = curated.Count;
        var checking = curated.Count(item => !_resolvedSavedIds.Contains(item.SavedId));
        return count == 0
            ? "Your library is empty · choose only the apps you want here"
            : checking != 0
                ? $"{count} saved · checking {checking} launch " +
                  (checking == 1 ? "entry" : "entries")
            : $"{count} saved · {curated.Count(item =>
                GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game)} " +
              "games · recent first";
    }

    private async ValueTask StartActiveRunAsync(CancellationToken activeLifetime)
    {
        StopActiveRun();
        if (Page == GamesAppsPage.Catalog)
        {
            bool retainLaterPage;
            lock (_gate)
                retainLaterPage = _viewState == GamesAppsViewState.Ready &&
                    _catalog.CanLoadPrevious;
            if (retainLaterPage)
            {
                Invalidate();
                return;
            }
            OpenCatalog(
                enterRememberedContent: false,
                force: true,
                resumeActiveRoot: true);
            return;
        }
        if (Page == GamesAppsPage.Running)
        {
            OpenRunning(
                enterRememberedContent: false,
                force: true,
                resumeActiveRoot: true);
            return;
        }
        _ = _navigation.NavigateRoot(
            GamesAppsPage.Library,
            GamesAppsPresentation.FocusGroupId(GamesAppsPage.Library));
        var generation = Interlocked.Increment(ref _generation);
        bool restoreRetainedSnapshot;
        lock (_gate)
        {
            _items = _libraryItems;
            _catalog = GamesAppsCatalogState.Empty;
            if (_hasLibrarySnapshot)
            {
                if (_selectedAppId is null || !_items.Any(item =>
                        string.Equals(item.AppId, _selectedAppId, StringComparison.Ordinal)))
                    _selectedAppId = ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked();
            }
            else
            {
                _viewState = GamesAppsViewState.Initial;
                _status = "Preparing your game library";
            }
            _launchingAppId = null;
            _loadingMore = false;
            _libraryMutationBusy = false;
            restoreRetainedSnapshot = !_hasLibrarySnapshot;
        }
        if (restoreRetainedSnapshot)
            await RestoreRetainedLibraryAsync(generation, activeLifetime)
                .ConfigureAwait(false);

        bool hasLibrarySnapshot;
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            _items = _libraryItems;
            if (_hasLibrarySnapshot)
            {
                if (_selectedAppId is null || !_items.Any(item =>
                        string.Equals(item.AppId, _selectedAppId, StringComparison.Ordinal)))
                    _selectedAppId = ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked();
            }
            hasLibrarySnapshot = _hasLibrarySnapshot;
        }
        Invalidate();
        if (!restoreRetainedSnapshot) return;
        _ = Operations.RunLatest(
            LibraryLoadOperationKey,
            context => LoadSavedLibraryRunAsync(
                generation, showColdLoading: !hasLibrarySnapshot,
                refreshCatalog: false, context),
            WidgetOperationLifetime.Active);
    }

    private async ValueTask RestoreRetainedLibraryAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var persisted = await HostServices.PrivateState.ReadAsync<GamesAppsLibraryState>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var state = GamesAppsLibraryPolicy.Normalize(
                persisted.Exists ? persisted.Value : null);
            if (state.DisplayItems.Count == 0) return;

            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    state.SavedIds, cancellationToken).ConfigureAwait(false);
            var current = GamesAppsLibraryPolicy.NormalizeResolved(
                resolved, state.SavedIds);
            lock (_gate)
            {
                if (Interlocked.Read(ref _generation) != generation) return;
                ApplyPersistedStateLocked(state, persisted.Revision, current);
                _hasLibrarySnapshot = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The existing active reconciliation owns typed failure projection.
        }
    }

    private async ValueTask LoadSavedLibraryRunAsync(
        long generation,
        bool showColdLoading,
        bool refreshCatalog,
        WidgetOperationContext context)
    {
        var load = LoadSavedLibraryAsync(
            generation, refreshCatalog, context.CancellationToken);
        if (!showColdLoading)
        {
            await load.ConfigureAwait(false);
            return;
        }
        await Task.WhenAll(
                load,
                ShowColdLoadingAfterDelayAsync(generation, context.CancellationToken))
            .ConfigureAwait(false);
    }

    private async Task ShowColdLoadingAfterDelayAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ColdLoadingDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (Interlocked.Read(ref _generation) != generation || _hasLibrarySnapshot ||
                    _viewState != GamesAppsViewState.Initial)
                    return;
                _viewState = GamesAppsViewState.Loading;
                _status = "Discovering trusted games…";
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RefreshCurrentRoute()
    {
        switch (Page)
        {
            case GamesAppsPage.Catalog:
                OpenCatalog(enterRememberedContent: false, force: true);
                break;
            case GamesAppsPage.Running:
                OpenRunning(enterRememberedContent: false, force: true);
                break;
            default:
                RefreshLibrary();
                break;
        }
    }

    private void RefreshLibrary()
    {
        if (!IsActive || ActiveCancellationToken.IsCancellationRequested) return;
        StopActiveRun();
        var generation = Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _viewState = _hasLibrarySnapshot
                ? GamesAppsViewState.Ready
                : GamesAppsViewState.Loading;
            _items = _libraryItems;
            _status = _hasLibrarySnapshot
                ? LibraryStatusLocked() + " · refreshing"
                : "Reloading your saved library…";
            _launchingAppId = null;
            _loadingMore = false;
            _catalogRefreshBusy = true;
        }
        Invalidate();
        var operation = Operations.RunLatest(
            LibraryLoadOperationKey,
            async context =>
            {
                var acquired = false;
                try
                {
                    await _commandGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
                    acquired = true;
                    await LoadSavedLibraryRunAsync(
                            generation, showColdLoading: false,
                            refreshCatalog: true, context)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (acquired) _commandGate.Release();
                    var changed = false;
                    lock (_gate)
                    {
                        if (Interlocked.Read(ref _generation) == generation)
                        {
                            _catalogRefreshBusy = false;
                            changed = true;
                        }
                    }
                    if (changed) Invalidate();
                }
            },
            WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
        {
            lock (_gate)
                if (Interlocked.Read(ref _generation) == generation)
                    _catalogRefreshBusy = false;
            Invalidate();
        }
    }

    private void StopActiveRun()
    {
        Interlocked.Increment(ref _generation);
        Operations.Cancel(LibraryLoadOperationKey);
        Operations.Cancel(PageLoadOperationKey);
        _toastExpiry.Cancel();
        lock (_gate)
        {
            _toast = null;
            _launchingAppId = null;
            _loadingMore = false;
            _libraryMutationBusy = false;
            _catalogRefreshBusy = false;
        }
    }

    private async Task LoadSavedLibraryAsync(
        long generation,
        bool refreshCatalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var persisted = await HostServices.PrivateState.ReadAsync<GamesAppsLibraryState>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var state = GamesAppsLibraryPolicy.Normalize(
                persisted.Exists ? persisted.Value : null);
            if (state.DisplayItems.Count != 0)
            {
                lock (_gate)
                {
                    if (Interlocked.Read(ref _generation) != generation) return;
                    ApplyPersistedStateLocked(state, persisted.Revision);
                    _hasLibrarySnapshot = true;
                    if (Page == GamesAppsPage.Library)
                    {
                        _viewState = GamesAppsViewState.Ready;
                        _status = LibraryStatusLocked();
                    }
                }
                Invalidate();
            }
            IReadOnlyList<WidgetAppLibraryItem> catalog;
            try
            {
                catalog = await ReadCatalogAsync(refreshCatalog, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (state.SavedIds.Count != 0)
            {
                var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                        state.SavedIds, cancellationToken).ConfigureAwait(false);
                var fallback = GamesAppsLibraryPolicy.NormalizeResolved(
                    resolved, state.SavedIds);
                lock (_gate)
                {
                    if (Interlocked.Read(ref _generation) != generation) return;
                    ApplyPersistedStateLocked(state, persisted.Revision, fallback);
                    _hasLibrarySnapshot = true;
                    if (Page == GamesAppsPage.Library)
                    {
                        _catalog = GamesAppsCatalogState.Empty;
                        _viewState = GamesAppsViewState.Ready;
                        _status = LibraryStatusLocked() + " · game discovery unavailable";
                    }
                }
                Invalidate();
                return;
            }

            var liveSelectedSavedId = default(string);
            lock (_gate)
                liveSelectedSavedId = _libraryItems.FirstOrDefault(item =>
                    string.Equals(item.AppId, _selectedAppId, StringComparison.Ordinal))?.SavedId;

            var preliminary = GamesAppsLibraryPolicy.Reconcile(
                persisted.Exists ? persisted.Value : null, catalog, liveSelectedSavedId);
            var detailedItems = await ResolveCuratedDetailsAsync(
                preliminary.VisibleItems,
                preliminary.State.SavedIds,
                cancellationToken).ConfigureAwait(false);
            var authoritativeItems = detailedItems;
            var reconciliation = GamesAppsLibraryPolicy.Reconcile(
                persisted.Exists ? persisted.Value : null,
                authoritativeItems,
                liveSelectedSavedId);
            var desiredState = reconciliation.State;
            LibraryPersistenceResult persistence;
            if (reconciliation.StateChanged)
            {
                persistence = await PersistLibraryAsync(
                        desiredState.SavedIds,
                        desiredState.AutoGameSavedIds,
                        desiredState.ExcludedGameSavedIds,
                        desiredState.SelectedSavedId,
                        cancellationToken,
                        authoritativeItems,
                        generation,
                        state,
                        persisted.Revision)
                    .ConfigureAwait(false);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (Interlocked.Read(ref _generation) != generation) return;
                    ApplyPersistedStateLocked(
                        desiredState, persisted.Revision, reconciliation.VisibleItems);
                }
                persistence = new LibraryPersistenceResult(Saved: true, Rejected: false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            int committedAddedGames;
            lock (_gate)
            {
                if (Interlocked.Read(ref _generation) != generation) return;
                if (!persistence.Saved && !persistence.Rejected)
                    ApplyPersistedStateLocked(state, persisted.Revision, authoritativeItems);
                _hasLibrarySnapshot = true;
                if (Page == GamesAppsPage.Library)
                {
                    _catalog = GamesAppsCatalogState.Empty;
                    _viewState = GamesAppsViewState.Ready;
                    _status = persistence.Saved
                        ? LibraryStatusLocked()
                        : persistence.Rejected
                            ? "Game exclusion storage is full"
                            : LibraryStatusLocked() + " · saving failed";
                }
                committedAddedGames = persistence.Saved
                    ? reconciliation.AddedGameSavedIds.Count(id =>
                        _persistedLibraryState.AutoGameSavedIds.Contains(
                            id, StringComparer.Ordinal) &&
                        _libraryItems.Any(item => item.SavedId == id &&
                            GamesAppsAppLibraryPresentation.Kind(item) ==
                                WidgetAppLibraryKind.Game))
                    : 0;
            }
            Invalidate();
            if (committedAddedGames != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != generation) return;
                ShowToast(
                    "Games added",
                    committedAddedGames == 1
                        ? "Added 1 trusted game to your library"
                        : $"Added {committedAddedGames} trusted games to your library",
                    ToastTone.Info);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplyError(exception, generation);
        }
    }

    private async Task<IReadOnlyList<WidgetAppLibraryItem>> ResolveCuratedDetailsAsync(
        IReadOnlyList<WidgetAppLibraryItem> fallbackItems,
        IReadOnlyList<string> curatedSavedIds,
        CancellationToken cancellationToken)
    {
        if (curatedSavedIds.Count == 0) return [];
        try
        {
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    curatedSavedIds, cancellationToken)
                .ConfigureAwait(false);
            var detailedBySaved = GamesAppsLibraryPolicy.NormalizeResolved(
                    resolved, curatedSavedIds)
                .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
            return curatedSavedIds.Where(detailedBySaved.ContainsKey)
                .Select(savedId => detailedBySaved[savedId])
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is WidgetCapabilityException or
            ArgumentException or InvalidOperationException)
        {
            return fallbackItems;
        }
    }

    private async Task<IReadOnlyList<WidgetAppLibraryItem>> ReadCatalogAsync(
        bool refresh,
        CancellationToken cancellationToken)
    {
        var items = new List<WidgetAppLibraryItem>(MaximumItems);
        var appIds = new HashSet<string>(StringComparer.Ordinal);
        var savedIds = new HashSet<string>(StringComparer.Ordinal);
        var requestedCursors = new HashSet<string>(StringComparer.Ordinal);
        WidgetCollectionCursor? cursor = null;
        while (items.Count < MaximumItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await HostServices.AppLibrary.QueryAsync(
                    InstalledGamesQuery, cursor,
                    cursor is null ? null : WidgetCursorDirection.After,
                    PageSize, refresh: cursor is null && refresh, cancellationToken)
                .ConfigureAwait(false);
            if (page?.Items is null || page.Items.Count > PageSize) throw MalformedCatalog();
            foreach (var item in GamesAppsCatalogPolicy.Normalize(page.Items, PageSize))
            {
                if (!appIds.Add(item.AppId) || !savedIds.Add(item.SavedId))
                    throw MalformedCatalog();
                items.Add(item);
                if (items.Count == MaximumItems) break;
            }
            if (page.After is not { } next) break;
            if (!requestedCursors.Add(next)) throw MalformedCatalog();
            cursor = new WidgetCollectionCursor(next);
        }
        return items;
    }

    private static WidgetCapabilityException MalformedCatalog() =>
        new("malformed_response", "The app library provider returned an invalid catalog.");

    private static string? SelectAppId(
        IReadOnlyList<WidgetAppLibraryItem> items,
        string? selectedSavedId,
        string? liveSelectedSavedId) =>
        items.FirstOrDefault(item => string.Equals(
            item.SavedId, selectedSavedId, StringComparison.Ordinal))?.AppId ??
        items.FirstOrDefault(item => string.Equals(
            item.SavedId, liveSelectedSavedId, StringComparison.Ordinal))?.AppId ??
        items.FirstOrDefault()?.AppId;

    private void SwitchRoot(int offset)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var current = Array.IndexOf(RootPages, Page);
        if (current < 0)
            throw new InvalidOperationException("The current Games & Apps root is not registered.");
        var destination = RootPages[(current + offset + RootPages.Length) % RootPages.Length];
        switch (destination)
        {
            case GamesAppsPage.Library:
                OpenLibrary(enterRememberedContent: true);
                break;
            case GamesAppsPage.Catalog:
                OpenCatalog(enterRememberedContent: true, force: false);
                break;
            case GamesAppsPage.Running:
                OpenRunning(enterRememberedContent: true, force: false);
                break;
        }
    }

    private void OpenLibrary(bool enterRememberedContent)
    {
        Operations.Cancel(PageLoadOperationKey);
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _items = _libraryItems;
            _catalog = GamesAppsCatalogState.Empty;
            _runningRevision = null;
            _loadingMore = false;
            _catalogRefreshBusy = false;
            _toast = null;
            _viewState = _hasLibrarySnapshot
                ? GamesAppsViewState.Ready
                : GamesAppsViewState.Initial;
            _status = _hasLibrarySnapshot
                ? LibraryStatusLocked()
                : "Preparing your game library";
            if (_selectedAppId is null || !_items.Any(item => string.Equals(
                    item.AppId, _selectedAppId, StringComparison.Ordinal)))
                _selectedAppId = ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
        }
        _ = enterRememberedContent
            ? _navigation.NavigateRoot(
                GamesAppsPage.Library,
                GamesAppsPresentation.FocusGroupId(GamesAppsPage.Library))
            : _navigation.NavigateRoot(GamesAppsPage.Library);
        Invalidate();
    }

    private void OpenCatalog(
        bool enterRememberedContent,
        bool force,
        bool resumeActiveRoot = false)
    {
        if (!resumeActiveRoot && LifecycleState != WidgetLifecycleState.Interactive) return;
        if (resumeActiveRoot && Page != GamesAppsPage.Catalog)
            throw new InvalidOperationException(
                "Only the current Catalog root can resume on activation.");
        if (Page == GamesAppsPage.Catalog && !force) return;
        Operations.Cancel(LibraryLoadOperationKey);
        var sameRoute = Page == GamesAppsPage.Catalog;
        var enforceMinimumLoading = !sameRoute;
        long generation;
        lock (_gate)
        {
            generation = Interlocked.Increment(ref _generation);
            if (!sameRoute)
            {
                _items = [];
                _catalog = GamesAppsCatalogState.Empty;
            }
            _viewState = force && sameRoute && _items.Count != 0
                ? GamesAppsViewState.Ready
                : GamesAppsViewState.Loading;
            _status = "Loading applications you can add…";
            _loadingMore = true;
            _catalogRefreshBusy = false;
            _toast = null;
        }
        if (!resumeActiveRoot)
        {
            _ = enterRememberedContent
                ? _navigation.NavigateRoot(
                    GamesAppsPage.Catalog,
                    GamesAppsPresentation.FocusGroupId(GamesAppsPage.Catalog))
                : _navigation.NavigateRoot(GamesAppsPage.Catalog);
        }
        var navigation = _navigation.Value;
        Invalidate();
        SchedulePageOperation(
            GamesAppsPage.Catalog,
            navigation,
            generation,
            async cancellationToken =>
            {
                var page = await AwaitInitialRouteLoadingAsync(
                    HostServices.AppLibrary.QueryAsync(
                        AllInstalledQuery, limit: PageSize, refresh: force,
                        cancellationToken: cancellationToken).AsTask(),
                    enforceMinimumLoading,
                    cancellationToken).ConfigureAwait(false);
                ApplyPage(page, GamesAppsCatalogPageTransition.Initial, generation);
            },
            "Catalog unavailable",
            "Applications could not be loaded",
            ToastTone.Danger);
    }

    private void OpenRunning(
        bool enterRememberedContent,
        bool force,
        bool resumeActiveRoot = false)
    {
        if (!resumeActiveRoot && LifecycleState != WidgetLifecycleState.Interactive) return;
        if (resumeActiveRoot && Page != GamesAppsPage.Running)
            throw new InvalidOperationException(
                "Only the current Running root can resume on activation.");
        if (Page == GamesAppsPage.Running && !force) return;
        Operations.Cancel(LibraryLoadOperationKey);
        var sameRoute = Page == GamesAppsPage.Running;
        var enforceMinimumLoading = !sameRoute;
        long generation;
        lock (_gate)
        {
            generation = Interlocked.Increment(ref _generation);
            if (!sameRoute)
            {
                _items = [];
                _runningRevision = null;
            }
            _viewState = force && sameRoute && _items.Count != 0
                ? GamesAppsViewState.Ready
                : GamesAppsViewState.Loading;
            _status = "Checking visible installed applications…";
            _loadingMore = true;
            _catalogRefreshBusy = false;
            _toast = null;
        }
        if (!resumeActiveRoot)
        {
            _ = enterRememberedContent
                ? _navigation.NavigateRoot(
                    GamesAppsPage.Running,
                    GamesAppsPresentation.FocusGroupId(GamesAppsPage.Running))
                : _navigation.NavigateRoot(GamesAppsPage.Running);
        }
        var navigation = _navigation.Value;
        Invalidate();
        SchedulePageOperation(
            GamesAppsPage.Running,
            navigation,
            generation,
            async cancellationToken =>
            {
                var observed = await AwaitInitialRouteLoadingAsync(
                    HostServices.AppLibrary.ObserveRunningAsync(cancellationToken).AsTask(),
                    enforceMinimumLoading,
                    cancellationToken).ConfigureAwait(false);
                var items = observed.Items.Select(item => new WidgetAppLibraryItem(
                    item.SavedId,
                    item.SavedId,
                    new WidgetAppLibraryPresentation(
                        item.DisplayName,
                        item.Kind,
                        new WidgetAppLibrarySourceReference(
                            "source-running", item.SourceAttribution),
                        new WidgetAppLibraryAvailability(
                            WidgetAppLibraryAvailabilityState.StaleSource,
                            false, "confirmation_required"),
                        new WidgetAppLibraryArtworkSet([]),
                        Metadata: null,
                        new WidgetAppLibraryCapabilitySet([]),
                        ActiveOperation: null))).ToArray();
                lock (_gate)
                {
                    if (Interlocked.Read(ref _generation) != generation ||
                        Page != GamesAppsPage.Running) return;
                    _runningRevision = observed.Revision;
                    _items = items;
                    _selectedAppId = items.FirstOrDefault()?.AppId;
                    _viewState = GamesAppsViewState.Ready;
                    _status = items.Length == 0
                        ? "No visible applications match your installed library"
                        : $"{items.Length} visible installed application{(items.Length == 1 ? "" : "s")}";
                    _catalog = GamesAppsCatalogState.Empty;
                }
                Invalidate();
            },
            "Running apps unavailable",
            "Running apps could not be checked",
            ToastTone.Warning);
    }

    private async Task<T> AwaitInitialRouteLoadingAsync<T>(
        Task<T> load,
        bool enforceMinimumLoading,
        CancellationToken cancellationToken)
    {
        if (!enforceMinimumLoading)
            return await load.ConfigureAwait(false);

        var minimumDisplay = Task.Delay(
            MinimumInitialRouteLoadingDuration,
            _timeProvider,
            cancellationToken);
        await Task.WhenAll(load, minimumDisplay).ConfigureAwait(false);
        return await load.ConfigureAwait(false);
    }

    private void SchedulePageOperation(
        GamesAppsPage route,
        WidgetNavigationSnapshot<GamesAppsPage> navigation,
        long generation,
        Func<CancellationToken, Task> work,
        string errorTitle,
        string errorFallback,
        ToastTone errorTone)
    {
        var operation = Operations.RunLatest(
            PageLoadOperationKey,
            async context =>
            {
                using var routeLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                    context.CancellationToken,
                    navigation.RootRouteCancellationToken);
                var acquired = false;
                try
                {
                    await _commandGate.WaitAsync(routeLifetime.Token).ConfigureAwait(false);
                    acquired = true;
                    if (!IsCurrentPageOperation(route, generation, context)) return;
                    await work(routeLifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (routeLifetime.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    if (IsCurrentPageOperation(route, generation, context))
                        ApplyPageOperationError(
                            generation, exception, errorTitle, errorFallback, errorTone);
                }
                finally
                {
                    if (acquired) _commandGate.Release();
                    CompletePageOperation(generation);
                }
            },
            WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
            RejectPageOperation(generation, errorFallback);
    }

    private bool IsCurrentPageOperation(
        GamesAppsPage route,
        long generation,
        WidgetOperationContext context)
    {
        if (!context.IsCurrent ||
            context.CancellationToken.IsCancellationRequested ||
            Page != route)
            return false;
        lock (_gate)
            return Interlocked.Read(ref _generation) == generation;
    }

    private void CompletePageOperation(long generation)
    {
        var changed = false;
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) == generation)
            {
                _loadingMore = false;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private void RejectPageOperation(long generation, string status)
    {
        var changed = false;
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) == generation)
            {
                _loadingMore = false;
                _viewState = GamesAppsViewState.Ready;
                _status = status;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private void ApplyPageOperationError(
        long generation,
        Exception exception,
        string title,
        string fallback,
        ToastTone tone)
    {
        var (_, status) = ErrorState(exception);
        var message = status == "App library request failed" ? fallback : status;
        var notice = new GamesAppsToastNotice(
            title, message, tone, UI.DefaultToastDuration);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            _loadingMore = false;
            _viewState = GamesAppsViewState.Ready;
            _status = message;
            _toast = notice;
        }
        Invalidate();
        ScheduleToastExpiry(notice);
    }

    private async Task AddRunningAsync(string savedId, CancellationToken cancellationToken)
    {
        string? revision;
        lock (_gate)
        {
            if (Page != GamesAppsPage.Running ||
                _libraryItems.Any(item => item.SavedId == savedId)) return;
            revision = _runningRevision;
        }
        if (revision is null) return;
        var current = await HostServices.AppLibrary.ConfirmRunningAsync(
            savedId, revision, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            ShowToast("App changed", "Refresh running apps and try again", ToastTone.Warning);
            return;
        }
        lock (_gate)
        {
            if (Page != GamesAppsPage.Running || _runningRevision != revision) return;
            _items = _items.Select(item => item.SavedId == savedId ? current : item).ToArray();
        }
        await ToggleCuratedAsync(current.AppId, cancellationToken).ConfigureAwait(false);
    }

    private void LoadMore()
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var navigation = _navigation.Value;
        if (navigation.RootRoute != GamesAppsPage.Catalog) return;
        WidgetCollectionCursor? cursor;
        long generation;
        lock (_gate)
        {
            cursor = _catalog.After;
            if (cursor is null || Page != GamesAppsPage.Catalog ||
                LifecycleState != WidgetLifecycleState.Interactive)
                return;
            generation = Interlocked.Increment(ref _generation);
            _loadingMore = true;
            _status = "Loading more installed apps…";
        }
        Invalidate();
        SchedulePageOperation(
            GamesAppsPage.Catalog,
            navigation,
            generation,
            async cancellationToken =>
            {
                var page = await HostServices.AppLibrary.QueryAsync(
                        AllInstalledQuery, cursor.Value, WidgetCursorDirection.After,
                        PageSize, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                ApplyPage(page, GamesAppsCatalogPageTransition.Next, generation);
            },
            "Could not load more",
            "More apps could not be loaded",
            ToastTone.Danger);
    }

    private void LoadPreviousPage()
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        var navigation = _navigation.Value;
        if (navigation.RootRoute != GamesAppsPage.Catalog) return;
        WidgetCollectionCursor cursor;
        long generation;
        lock (_gate)
        {
            if (!_catalog.CanLoadPrevious || Page != GamesAppsPage.Catalog ||
                LifecycleState != WidgetLifecycleState.Interactive)
                return;
            cursor = _catalog.Before!.Value;
            generation = Interlocked.Increment(ref _generation);
            _loadingMore = true;
            _status = "Loading the previous application page…";
        }
        Invalidate();
        SchedulePageOperation(
            GamesAppsPage.Catalog,
            navigation,
            generation,
            async cancellationToken =>
            {
                var page = await HostServices.AppLibrary.QueryAsync(
                        AllInstalledQuery, cursor, WidgetCursorDirection.Before,
                        PageSize, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                ApplyPage(page, GamesAppsCatalogPageTransition.Previous, generation);
            },
            "Could not load page",
            "The previous page could not be loaded",
            ToastTone.Danger);
    }

    private async Task LaunchAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        WidgetAppLibraryItem? selected = null;
        IReadOnlyList<string>? persistedOrder = null;
        IReadOnlyList<string>? persistedAutoGames = null;
        IReadOnlyList<string>? persistedExclusions = null;
        IReadOnlyList<WidgetAppLibraryItem>? persistenceCandidates = null;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token).ConfigureAwait(false);
            if (!acquired) return;
            lock (_gate)
            {
                selected = _items.FirstOrDefault(item => item.AppId == appId);
                if (selected is null || !_resolvedSavedIds.Contains(selected.SavedId) ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                    return;
                _selectedAppId = selected.AppId;
                _launchingAppId = selected.AppId;
                _status = $"Opening {GamesAppsAppLibraryPresentation.DisplayName(selected)}…";
            }
            Invalidate();
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    [selected.SavedId], commandLifetime.Token)
                .ConfigureAwait(false);
            var current = resolved.SingleOrDefault(item => string.Equals(
                item.SavedId, selected.SavedId, StringComparison.Ordinal));
            if (current is null || !GamesAppsAppLibraryPresentation.CanLaunch(current))
                throw new WidgetCapabilityException(
                    "app_not_found", "The selected app is no longer available.");
            await HostServices.AppLibrary.LaunchAsync(
                    current.AppId,
                    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
                    commandLifetime.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                var desired = GamesAppsLibraryPolicy.MoveToFront(
                    _persistedLibraryState, selected.SavedId);
                var bySaved = _libraryItems.ToDictionary(
                    item => item.SavedId, StringComparer.Ordinal);
                _libraryItems = desired.SavedIds.Where(bySaved.ContainsKey)
                    .Select(savedId => bySaved[savedId]).ToArray();
                _items = _libraryItems;
                persistedOrder = desired.SavedIds;
                persistedAutoGames = desired.AutoGameSavedIds;
                persistedExclusions = desired.ExcludedGameSavedIds;
                persistenceCandidates = _libraryItems.ToArray();
                _launchingAppId = null;
                _status = $"Opened {GamesAppsAppLibraryPresentation.DisplayName(selected)}";
            }
            ShowToast("Application opened",
                $"Opened {GamesAppsAppLibraryPresentation.DisplayName(selected)}",
                ToastTone.Success);
            await PersistLibraryAsync(
                persistedOrder,
                persistedAutoGames,
                persistedExclusions,
                selected.SavedId,
                commandLifetime.Token,
                persistenceCandidates).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            lock (_gate) _launchingAppId = null;
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            lock (_gate) _launchingAppId = null;
            var message = ApplyCommandError(exception, selected is null
                ? "The selected app could not be opened"
                : $"{GamesAppsAppLibraryPresentation.DisplayName(selected)} could not be opened");
            ShowToast("Application not opened", message, ToastTone.Danger);
            return;
        }
        finally
        {
            if (acquired) _commandGate.Release();
        }
        Invalidate();
    }

    private void ApplyPage(
        WidgetAppLibraryPage? page,
        GamesAppsCatalogPageTransition transition,
        long generation)
    {
        var pageRoute = Page;
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            var selectedSavedId = _items.FirstOrDefault(item => string.Equals(
                item.AppId, _selectedAppId, StringComparison.Ordinal))?.SavedId;
            var result = GamesAppsCatalogPolicy.ApplyPage(
                _catalog, page, transition, PageSize);
            var emptyCatalog = result.EmptyInitial && pageRoute == GamesAppsPage.Catalog;
            if (emptyCatalog)
            {
                _items = [];
                _catalog = result.State;
                _selectedAppId = null;
                _viewState = GamesAppsViewState.Ready;
                _status = "No applications are available to add";
            }
            else
            {
                if (result.EmptyContinuation)
                {
                    _catalog = result.State;
                    _status = "No more launchable applications were found";
                    return;
                }
                _catalog = result.State;
                _items = result.State.Items;
                _selectedAppId = _items.FirstOrDefault(item => string.Equals(
                        item.SavedId, selectedSavedId, StringComparison.Ordinal))?.AppId ??
                    _items.FirstOrDefault()?.AppId;
                _viewState = _items.Count == 0 ? GamesAppsViewState.Empty : GamesAppsViewState.Ready;
                _status = _items.Count == 0
                    ? "No launchable applications or games found"
                    : pageRoute == GamesAppsPage.Catalog
                        ? CatalogStatus(_items, _catalog.After is not null)
                        : LibraryStatusLocked();
            }
        }
        Invalidate();
    }

    private async Task<LibraryPersistenceResult> PersistLibraryAsync(
        IReadOnlyList<string> savedIds,
        IReadOnlyList<string> autoGameSavedIds,
        IReadOnlyList<string> excludedGameSavedIds,
        string? selectedSavedId,
        CancellationToken cancellationToken,
        IReadOnlyList<WidgetAppLibraryItem>? candidateItems = null,
        long? requiredGeneration = null,
        GamesAppsLibraryState? baselineOverride = null,
        long? revisionOverride = null)
    {
        long revision;
        GamesAppsLibraryState baseline;
        lock (_gate)
        {
            revision = revisionOverride ?? _stateRevision;
            baseline = baselineOverride ?? _persistedLibraryState;
        }
        var normalized = GamesAppsLibraryPolicy.Normalize(
            new GamesAppsLibraryState(3, savedIds, selectedSavedId)
            {
                AutoGameSavedIds = autoGameSavedIds,
                ExcludedGameSavedIds = excludedGameSavedIds,
                DisplayItems = GamesAppsLibraryPolicy.BuildDisplayItems(
                    baseline,
                    candidateItems,
                    new GamesAppsLibraryState(3, savedIds, selectedSavedId)
                    {
                        AutoGameSavedIds = autoGameSavedIds,
                        ExcludedGameSavedIds = excludedGameSavedIds,
                    }),
            });
        try
        {
            var save = await GamesAppsLibraryStore.SaveAsync(
                    (state, expectedRevision, token) => HostServices.PrivateState.WriteAsync(
                        state, expectedRevision, cancellationToken: token),
                    token => HostServices.PrivateState.ReadAsync<GamesAppsLibraryState>(
                        cancellationToken: token),
                    baseline,
                    normalized,
                    revision,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (requiredGeneration is { } expected &&
                    Interlocked.Read(ref _generation) != expected)
                    return new LibraryPersistenceResult(
                        Saved: false,
                        Rejected: save.Status == GamesAppsLibrarySaveStatus.Rejected);
                ApplyPersistedStateLocked(save.State, save.Revision, candidateItems);
                if (save.Status == GamesAppsLibrarySaveStatus.Rejected)
                    _status = "Game exclusion storage is full";
            }
            Invalidate();
            return new LibraryPersistenceResult(
                Saved: save.Status == GamesAppsLibrarySaveStatus.Saved,
                Rejected: save.Status == GamesAppsLibrarySaveStatus.Rejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lock (_gate) _status = "Library update was not saved";
            Invalidate();
            return new LibraryPersistenceResult(Saved: false, Rejected: false);
        }
    }

    private void ApplyPersistedStateLocked(
        GamesAppsLibraryState state,
        long revision,
        IReadOnlyList<WidgetAppLibraryItem>? candidateItems = null)
    {
        var selectedSavedId = _libraryItems.FirstOrDefault(item =>
            string.Equals(item.AppId, _selectedAppId, StringComparison.Ordinal))?.SavedId;
        _persistedLibraryState = state;
        _stateRevision = revision;
        var candidates = candidateItems ?? _libraryItems
            .Where(item => _resolvedSavedIds.Contains(item.SavedId))
            .ToArray();
        var projection = GamesAppsLibraryPolicy.Project(state, candidates);
        _resolvedSavedIds.Clear();
        _resolvedSavedIds.UnionWith(projection.ResolvedSavedIds);
        _libraryItems = projection.Items;
        if (Page == GamesAppsPage.Library) _items = _libraryItems;
        _selectedAppId = SelectAppId(
            _libraryItems, state.SelectedSavedId, selectedSavedId);
    }

    private void ApplyError(Exception exception, long generation)
    {
        var (state, status) = ErrorState(exception);
        var returnToLibrary = false;
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            if (_hasLibrarySnapshot)
            {
                _items = _libraryItems;
                _catalog = GamesAppsCatalogState.Empty;
                _launchingAppId = null;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked() + " · refresh unavailable";
                returnToLibrary = true;
            }
            else
            {
                _viewState = state;
                _status = status;
                _items = [];
                _catalog = GamesAppsCatalogState.Empty;
                _launchingAppId = null;
            }
        }
        if (returnToLibrary)
            _ = _navigation.NavigateRoot(GamesAppsPage.Library);
        Invalidate();
    }

    private string ApplyCommandError(Exception exception, string fallback)
    {
        var (_, status) = ErrorState(exception);
        var message = status == "App library request failed" ? fallback : status;
        lock (_gate) _status = message;
        Invalidate();
        return message;
    }

    private void ShowToast(string title, string message, ToastTone tone)
    {
        var duration = UI.DefaultToastDuration;
        var notice = new GamesAppsToastNotice(title, message, tone, duration);
        lock (_gate)
            _toast = notice;
        Invalidate();
        ScheduleToastExpiry(notice);
    }

    private void ScheduleToastExpiry(GamesAppsToastNotice notice)
    {
        _ = _toastExpiry.ScheduleLatest(
            notice.Duration,
            () =>
            {
                var changed = false;
                lock (_gate)
                {
                    if (ReferenceEquals(_toast, notice))
                    {
                        _toast = null;
                        changed = true;
                    }
                }
                if (changed) Invalidate();
            });
    }

    private static (GamesAppsViewState State, string Status) ErrorState(Exception exception)
    {
        if (exception is WidgetCapabilityUnavailableException)
            return (GamesAppsViewState.ServiceUnavailable, "App library unavailable");
        if (exception is WidgetCapabilityException capability)
        {
            return capability.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" or "capability_revoked" =>
                    (GamesAppsViewState.PermissionDenied, "Allow Games & Apps access in Settings"),
                "lifecycle_denied" =>
                    (GamesAppsViewState.LifecycleDenied, "App library is paused"),
                "platform_unavailable" =>
                    (GamesAppsViewState.ServiceUnavailable, "App library unavailable"),
                "app_not_found" =>
                    (GamesAppsViewState.Error, "The selected app is no longer installed"),
                _ => (GamesAppsViewState.Error, "App library request failed"),
            };
        }
        return (GamesAppsViewState.Error, "App library request failed");
    }

    private static string CatalogStatus(
        IReadOnlyList<WidgetAppLibraryItem> items,
        bool hasMore)
    {
        var games = items.Count(item =>
            GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game);
        return $"{items.Count}{(hasMore ? "+" : string.Empty)} available · " +
               $"{games} {(games == 1 ? "game" : "games")} loaded";
    }

}
