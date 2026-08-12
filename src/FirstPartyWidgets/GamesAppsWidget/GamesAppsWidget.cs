using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

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
    public const int ColdLoadingDelayMilliseconds = 150;
    public const int PageSize = 32;
    private const int MaximumCuratedItems = GamesAppsLibraryPolicy.MaximumCuratedItems;
    public const int MaximumItems =
        MaximumCuratedItems + GamesAppsLibraryPolicy.MaximumExcludedGames;
    private const string LibraryLoadOperationKey = "games.library.load";
    private static readonly WidgetAppLibraryQuery AllInstalledQuery = new();
    private static readonly WidgetAppLibraryQuery InstalledGamesQuery =
        new(Kind: WidgetAppLibraryKind.Game);

    private sealed record LibraryPersistenceResult(bool Saved, bool Rejected);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private IReadOnlyList<WidgetAppLibraryItem> _items = [];
    private IReadOnlyList<WidgetAppLibraryItem> _libraryItems = [];
    private readonly HashSet<string> _resolvedSavedIds = new(StringComparer.Ordinal);
    private GamesAppsViewState _viewState = GamesAppsViewState.Initial;
    private GamesAppsPage _page = GamesAppsPage.Library;
    private string _status = "Your launch library loads when this widget becomes visible";
    private string? _selectedAppId;
    private string? _launchingAppId;
    private bool _loadingMore;
    private bool _libraryMutationBusy;
    private bool _hasLibrarySnapshot;
    private GamesAppsCatalogState _catalog = GamesAppsCatalogState.Empty;
    private string? _runningRevision;
    private CancellationTokenSource? _toastLifetime;
    private GamesAppsToastNotice? _toast;
    private long _generation;
    private long _toastGeneration;
    private long _stateRevision;
    private GamesAppsLibraryState _persistedLibraryState = new(3, [], null);

    public GamesAppsViewState ViewState { get { lock (_gate) return _viewState; } }
    public IReadOnlyList<WidgetAppLibraryItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }
    public string? SelectedAppId { get { lock (_gate) return _selectedAppId; } }
    public bool HasNextPage { get { lock (_gate) return _catalog.After is not null; } }
    public GamesAppsPage Page { get { lock (_gate) return _page; } }
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
        GamesAppsPresentationState presentation;
        lock (_gate)
        {
            presentation = new GamesAppsPresentationState(
                _viewState,
                _page,
                _status,
                _items.ToArray(),
                _libraryItems.Select(item => item.SavedId).ToArray(),
                _resolvedSavedIds.ToHashSet(StringComparer.Ordinal),
                _selectedAppId,
                _launchingAppId,
                _loadingMore || _libraryMutationBusy,
                _libraryMutationBusy,
                _catalog.After is not null,
                _catalog.CanLoadPrevious,
                LifecycleState,
                _toast);
        }
        return GamesAppsPresentation.Render(presentation);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        StartActiveRun(activeLifetime);
        return ValueTask.CompletedTask;
    }

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
        if (previous is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive ||
            current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
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
            case "back":
                lock (_gate)
                {
                    if (_page == GamesAppsPage.Library) return;
                    var selectedSavedId = _items.FirstOrDefault(item => string.Equals(
                        item.AppId, _selectedAppId, StringComparison.Ordinal))?.SavedId;
                    _page = GamesAppsPage.Library;
                    _items = _libraryItems;
                    _selectedAppId = _libraryItems.FirstOrDefault(item => string.Equals(
                            item.SavedId, selectedSavedId, StringComparison.Ordinal))?.AppId ??
                        ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
                    _status = LibraryStatusLocked();
                }
                Invalidate();
                return;
            case "games.open-catalog":
                await OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.open-running":
                await OpenRunningAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.toggle-curation":
                string? catalogAppId = null;
                lock (_gate)
                {
                    if (_page is GamesAppsPage.Catalog or GamesAppsPage.Running)
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
                    if (_page == GamesAppsPage.Library)
                        curatedAppId = _items.FirstOrDefault(item => string.Equals(
                            GamesAppsPresentation.LibraryElementId(item.SavedId), action.SourceElementId,
                            StringComparison.Ordinal))?.AppId;
                }
                if (curatedAppId is not null)
                    await RemoveCuratedAsync(curatedAppId, cancellationToken).ConfigureAwait(false);
                return;
            case RetryActionId:
                await RetryActiveRunAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.load-more":
                await LoadMoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.previous-page":
                await LoadPreviousPageAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.launch":
                string? appId = null;
                lock (_gate)
                {
                    if (_page == GamesAppsPage.Library)
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
                if (_page is not (GamesAppsPage.Catalog or GamesAppsPage.Running)) return;
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
                if (_page != GamesAppsPage.Library) return;
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

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        StopActiveRun();
        var generation = Interlocked.Increment(ref _generation);
        bool hasLibrarySnapshot;
        lock (_gate)
        {
            _page = GamesAppsPage.Library;
            if (_hasLibrarySnapshot)
                ApplyPersistedStateLocked(_persistedLibraryState, _stateRevision, []);
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
            hasLibrarySnapshot = _hasLibrarySnapshot;
        }
        _ = Operations.RunLatest(
            LibraryLoadOperationKey,
            context => LoadSavedLibraryRunAsync(
                generation, showColdLoading: !hasLibrarySnapshot, context),
            WidgetOperationLifetime.Active);
    }

    private async ValueTask LoadSavedLibraryRunAsync(
        long generation,
        bool showColdLoading,
        WidgetOperationContext context)
    {
        var load = LoadSavedLibraryAsync(generation, context.CancellationToken);
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

    private async Task RetryActiveRunAsync(CancellationToken cancellationToken)
    {
        if (!IsActive || ActiveCancellationToken.IsCancellationRequested) return;

        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!acquired || !IsActive || ActiveCancellationToken.IsCancellationRequested) return;

            StopActiveRun();
            var generation = Interlocked.Increment(ref _generation);
            lock (_gate)
            {
                _viewState = _hasLibrarySnapshot
                    ? GamesAppsViewState.Ready
                    : GamesAppsViewState.Loading;
                _page = GamesAppsPage.Library;
                _items = _libraryItems;
                _status = _hasLibrarySnapshot
                    ? LibraryStatusLocked() + " · refreshing"
                    : "Reloading your saved library…";
                _launchingAppId = null;
                _loadingMore = false;
            }
            Invalidate();
            var operation = Operations.RunLatest(
                LibraryLoadOperationKey,
                context => LoadSavedLibraryRunAsync(
                    generation, showColdLoading: false, context),
                WidgetOperationLifetime.Active);
            await operation.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || ActiveCancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            if (acquired) _commandGate.Release();
        }
    }

    private void StopActiveRun()
    {
        Interlocked.Increment(ref _generation);
        ClearToast(invalidate: false);
        Operations.Cancel(LibraryLoadOperationKey);
        lock (_gate)
        {
            _launchingAppId = null;
            _loadingMore = false;
            _libraryMutationBusy = false;
        }
    }

    private async Task LoadSavedLibraryAsync(long generation, CancellationToken cancellationToken)
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
                    if (_page == GamesAppsPage.Library)
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
                catalog = await ReadCatalogAsync(cancellationToken).ConfigureAwait(false);
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
                    if (_page == GamesAppsPage.Library)
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
                if (_page == GamesAppsPage.Library)
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
                    PageSize, refresh: cursor is null, cancellationToken)
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

    private async Task OpenCatalogAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token).ConfigureAwait(false);
            if (!acquired) return;
            Operations.Cancel(LibraryLoadOperationKey);
            await Operations.WhenIdleAsync(LibraryLoadOperationKey, commandLifetime.Token)
                .ConfigureAwait(false);
            long generation;
            lock (_gate)
            {
                generation = Interlocked.Increment(ref _generation);
                _page = GamesAppsPage.Catalog;
                _viewState = GamesAppsViewState.Loading;
                _status = "Loading applications you can add…";
                _loadingMore = true;
            }
            Invalidate();
            var page = await HostServices.AppLibrary.QueryAsync(
                    AllInstalledQuery, limit: PageSize, refresh: true,
                    cancellationToken: commandLifetime.Token).ConfigureAwait(false);
            ApplyPage(page, GamesAppsCatalogPageTransition.Initial, generation);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            var message = ApplyCommandError(exception, "Applications could not be loaded");
            ShowToast("Catalog unavailable", message, ToastTone.Danger);
            lock (_gate)
            {
                _page = GamesAppsPage.Library;
                _viewState = GamesAppsViewState.Ready;
            }
            Invalidate();
        }
        finally
        {
            if (acquired)
            {
                lock (_gate) _loadingMore = false;
                _commandGate.Release();
                Invalidate();
            }
        }
    }

    private async Task OpenRunningAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        if (!await _commandGate.WaitAsync(0, lifetime.Token).ConfigureAwait(false)) return;
        try
        {
            lock (_gate)
            {
                Interlocked.Increment(ref _generation);
                _page = GamesAppsPage.Running;
                _viewState = GamesAppsViewState.Loading;
                _status = "Checking visible installed applications…";
                _loadingMore = true;
            }
            Invalidate();
            var observed = await HostServices.AppLibrary.ObserveRunningAsync(lifetime.Token)
                .ConfigureAwait(false);
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
                if (_page != GamesAppsPage.Running) return;
                _runningRevision = observed.Revision;
                _items = items;
                _selectedAppId = items.FirstOrDefault()?.AppId;
                _viewState = GamesAppsViewState.Ready;
                _status = items.Length == 0
                    ? "No visible applications match your installed library"
                    : $"{items.Length} visible installed application{(items.Length == 1 ? "" : "s")}";
                _catalog = GamesAppsCatalogState.Empty;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            ShowToast("Running apps unavailable",
                ApplyCommandError(exception, "Running apps could not be checked"),
                ToastTone.Warning);
            lock (_gate) { _page = GamesAppsPage.Library; _items = _libraryItems; }
        }
        finally
        {
            lock (_gate) _loadingMore = false;
            _commandGate.Release();
            Invalidate();
        }
    }

    private async Task AddRunningAsync(string savedId, CancellationToken cancellationToken)
    {
        string? revision;
        lock (_gate)
        {
            if (_page != GamesAppsPage.Running ||
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
            if (_page != GamesAppsPage.Running || _runningRevision != revision) return;
            _items = _items.Select(item => item.SavedId == savedId ? current : item).ToArray();
        }
        await ToggleCuratedAsync(current.AppId, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token).ConfigureAwait(false);
            if (!acquired) return;

            WidgetCollectionCursor? cursor;
            long generation;
            lock (_gate)
            {
                cursor = _catalog.After;
                generation = Interlocked.Read(ref _generation);
                if (cursor is null || _page != GamesAppsPage.Catalog ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                    return;
                _loadingMore = true;
                _status = "Loading more installed apps…";
            }
            Invalidate();
            var page = await HostServices.AppLibrary.QueryAsync(
                    AllInstalledQuery, cursor, WidgetCursorDirection.After,
                    PageSize, cancellationToken: commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, GamesAppsCatalogPageTransition.Next, generation);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            var message = ApplyCommandError(exception, "More apps could not be loaded");
            ShowToast("Could not load more", message, ToastTone.Danger);
        }
        finally
        {
            if (acquired)
            {
                lock (_gate) _loadingMore = false;
                _commandGate.Release();
                Invalidate();
            }
        }
    }

    private async Task LoadPreviousPageAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token).ConfigureAwait(false);
            if (!acquired) return;

            WidgetCollectionCursor cursor;
            long generation;
            lock (_gate)
            {
                if (!_catalog.CanLoadPrevious || _page != GamesAppsPage.Catalog ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                    return;
                cursor = _catalog.Before!.Value;
                generation = Interlocked.Read(ref _generation);
                _loadingMore = true;
                _status = "Loading the previous application page…";
            }
            Invalidate();
            var page = await HostServices.AppLibrary.QueryAsync(
                    AllInstalledQuery, cursor, WidgetCursorDirection.Before,
                    PageSize, cancellationToken: commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, GamesAppsCatalogPageTransition.Previous, generation);
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            var message = ApplyCommandError(exception, "The previous page could not be loaded");
            ShowToast("Could not load page", message, ToastTone.Danger);
        }
        finally
        {
            if (acquired)
            {
                lock (_gate) _loadingMore = false;
                _commandGate.Release();
                Invalidate();
            }
        }
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
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            var result = GamesAppsCatalogPolicy.ApplyPage(
                _catalog, page, transition, PageSize);
            var emptyCatalog = result.EmptyInitial && _page == GamesAppsPage.Catalog;
            if (emptyCatalog)
            {
                _page = GamesAppsPage.Library;
                _items = _libraryItems;
                _catalog = GamesAppsCatalogState.Empty;
                _selectedAppId = _items.FirstOrDefault()?.AppId;
                _viewState = GamesAppsViewState.Ready;
                _status = "No additional launchable applications were found";
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
                _selectedAppId = _items.FirstOrDefault()?.AppId;
                _viewState = _items.Count == 0 ? GamesAppsViewState.Empty : GamesAppsViewState.Ready;
                _status = _items.Count == 0
                    ? "No launchable applications or games found"
                    : _page == GamesAppsPage.Catalog
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
        if (_page == GamesAppsPage.Library) _items = _libraryItems;
        _selectedAppId = SelectAppId(
            _libraryItems, state.SelectedSavedId, selectedSavedId);
    }

    private void ApplyError(Exception exception, long generation)
    {
        var (state, status) = ErrorState(exception);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            if (_hasLibrarySnapshot)
            {
                ApplyPersistedStateLocked(_persistedLibraryState, _stateRevision, []);
                _page = GamesAppsPage.Library;
                _items = _libraryItems;
                _catalog = GamesAppsCatalogState.Empty;
                _launchingAppId = null;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked() + " · refresh unavailable";
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
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ActiveCancellationToken);
        var generation = Interlocked.Increment(ref _toastGeneration);
        lock (_gate)
        {
            var previous = _toastLifetime;
            _toastLifetime = lifetime;
            _toast = new GamesAppsToastNotice(title, message, tone, duration);
            // The expiry task owns disposal. Cancel while holding the state
            // lock so it cannot dispose the prior source between capture and
            // cancellation.
            previous?.Cancel();
        }
        Invalidate();
        _ = ExpireToastAsync(generation, duration, lifetime);
    }

    private async Task ExpireToastAsync(
        long generation,
        TimeSpan duration,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(duration, lifetime.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (Interlocked.Read(ref _toastGeneration) != generation ||
                    !ReferenceEquals(_toastLifetime, lifetime))
                    return;
                _toast = null;
                _toastLifetime = null;
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            // Active-lifecycle cancellation can reach this task before
            // StopActiveRun calls ClearToast. Drop the shared reference while
            // holding the state lock before disposal so cleanup can never try
            // to cancel an already-disposed source.
            lock (_gate)
            {
                if (ReferenceEquals(_toastLifetime, lifetime))
                    _toastLifetime = null;
            }
            lifetime.Dispose();
        }
    }

    private void ClearToast(bool invalidate)
    {
        Interlocked.Increment(ref _toastGeneration);
        bool changed;
        lock (_gate)
        {
            var lifetime = _toastLifetime;
            _toastLifetime = null;
            changed = _toast is not null;
            _toast = null;
            // ExpireToastAsync is the sole disposer. Cancellation stays under
            // the lock to prevent a completion/disposal race.
            lifetime?.Cancel();
        }
        if (changed && invalidate) Invalidate();
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
