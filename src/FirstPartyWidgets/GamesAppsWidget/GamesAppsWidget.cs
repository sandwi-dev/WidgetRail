using System.Security.Cryptography;
using System.Text;
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
    public const int MaximumItems = 512;
    private const int MaximumCuratedItems = GamesAppsLibraryStateReconciler.MaximumCuratedItems;
    private const int MaximumExcludedGames = GamesAppsLibraryStateReconciler.MaximumExcludedGames;
    private const string LibraryLoadOperationKey = "games.library.load";

    private sealed record LibraryPersistenceResult(bool Saved, bool Rejected);
    private enum CatalogPageTransition { Initial, Next, Previous }

    private static readonly WidgetSurfaceHints LibrarySurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 820,
        PreferredHeight = 600,
        MinimumWidth = 420,
        MinimumHeight = 300,
    };

    private static readonly WidgetSurfaceHints CatalogSurface = LibrarySurface with
    {
        MinimumHeight = 320,
    };

    private static readonly WidgetSurfaceHints StateSurface = LibrarySurface with
    {
        PreferredHeight = 280,
        MinimumHeight = 250,
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private IReadOnlyList<WidgetAppLibraryItem> _items = [];
    private IReadOnlyList<WidgetAppLibraryItem> _libraryItems = [];
    private readonly List<string> _curatedSavedIds = [];
    private readonly List<string> _autoGameSavedIds = [];
    private readonly List<string> _excludedGameSavedIds = [];
    private readonly HashSet<string> _resolvedSavedIds = new(StringComparer.Ordinal);
    private GamesAppsViewState _viewState = GamesAppsViewState.Initial;
    private GamesAppsPage _page = GamesAppsPage.Library;
    private string _status = "Your launch library loads when this widget becomes visible";
    private string? _selectedAppId;
    private string? _launchingAppId;
    private bool _loadingMore;
    private bool _libraryMutationBusy;
    private bool _hasLibrarySnapshot;
    private int? _nextOffset;
    private int _catalogOffset;
    private readonly List<int> _catalogBackOffsets = [];
    private CancellationTokenSource? _toastLifetime;
    private ToastNotice? _toast;
    private long _generation;
    private long _toastGeneration;
    private long _stateRevision;
    private GamesAppsLibraryState _persistedLibraryState = new(3, [], null);

    private sealed record ToastNotice(
        string Title,
        string Message,
        ToastTone Tone,
        TimeSpan Duration);

    public GamesAppsViewState ViewState { get { lock (_gate) return _viewState; } }
    public IReadOnlyList<WidgetAppLibraryItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }
    public string? SelectedAppId { get { lock (_gate) return _selectedAppId; } }
    public int? NextOffset { get { lock (_gate) return _nextOffset; } }
    public GamesAppsPage Page { get { lock (_gate) return _page; } }
    public IReadOnlyList<WidgetAppLibraryItem> CuratedItems
    {
        get
        {
            lock (_gate)
            {
                var byId = _libraryItems.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
                return _curatedSavedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            }
        }
    }

    public override WidgetView Render()
    {
        IReadOnlyList<WidgetAppLibraryItem> items;
        GamesAppsViewState state;
        string status;
        string? selectedAppId;
        string? launchingAppId;
        bool loadingMore;
        bool libraryMutationBusy;
        int? nextOffset;
        bool canLoadPrevious;
        GamesAppsPage page;
        IReadOnlyList<string> curatedAppIds;
        IReadOnlySet<string> resolvedSavedIds;
        ToastNotice? toast;
        lock (_gate)
        {
            items = _items;
            state = _viewState;
            status = _status;
            selectedAppId = _selectedAppId;
            launchingAppId = _launchingAppId;
            loadingMore = _loadingMore;
            libraryMutationBusy = _libraryMutationBusy;
            nextOffset = _nextOffset;
            canLoadPrevious = _catalogBackOffsets.Count != 0;
            page = _page;
            curatedAppIds = _libraryItems.Select(item => item.SavedId).ToArray();
            resolvedSavedIds = _resolvedSavedIds.ToHashSet(StringComparer.Ordinal);
            toast = _toast;
        }

        var headerChildren = new List<WidgetElement>
        {
                UI.Text(page == GamesAppsPage.Catalog ? "CATALOG" : "LIBRARY",
                        "games.eyebrow", page == GamesAppsPage.Catalog
                            ? "Add applications catalog"
                            : "Installed application library")
                    .Classes("games-eyebrow"),
                UI.Text("Games & Apps", "games.title", "Games and Apps")
                    .Classes("games-title"),
                UI.Text(status, "games.status", status).Classes(
                    "games-status",
                    state == GamesAppsViewState.Ready ? "is-ready" :
                    state is GamesAppsViewState.PermissionDenied or
                        GamesAppsViewState.LifecycleDenied or
                        GamesAppsViewState.ServiceUnavailable or GamesAppsViewState.Error
                        ? "is-error" : "is-neutral"),
        };
        if (toast is not null)
            headerChildren.Add(UI.Toast(
                toast.Title, toast.Message, toast.Tone, "games.toast", toast.Duration));
        var header = UI.Stack("games.header", headerChildren.ToArray())
            .Classes("games-header");

        if (state != GamesAppsViewState.Ready)
            return RenderState(header, state, page);

        return page == GamesAppsPage.Catalog
            ? RenderCatalog(header, items, curatedAppIds, selectedAppId, launchingAppId,
                loadingMore || libraryMutationBusy, nextOffset, canLoadPrevious)
            : RenderLibrary(
                header, items, curatedAppIds, resolvedSavedIds, selectedAppId, launchingAppId,
                libraryMutationBusy);
    }

    private WidgetView RenderLibrary(
        StackElement header,
        IReadOnlyList<WidgetAppLibraryItem> items,
        IReadOnlyList<string> curatedAppIds,
        IReadOnlySet<string> resolvedSavedIds,
        string? selectedAppId,
        string? launchingAppId,
        bool libraryMutationBusy)
    {
        var byId = items.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var curated = curatedAppIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        if (curated.Length == 0)
        {
            var empty = ConfigureStateAction(UI.EmptyState(
                    "Build your library",
                    "Trusted games appear automatically. Add other applications when you want them.",
                    "games.state",
                    new ComponentAction(
                        "Add applications", "games.open-catalog", WidgetGlyph.Play),
                    WidgetGlyph.Play),
                libraryMutationBusy || LifecycleState != WidgetLifecycleState.Interactive);
            var emptyRoot = UI.Stack("games.root",
                    header,
                    UI.Stack("games.content", empty).Classes("games-content", "games-state-shell"))
                .InputScope("games-apps")
                .Classes("games-apps-widget", "has-state");
            return new WidgetView(emptyRoot, "games.state.action", Surface: StateSurface);
        }

        var elementIds = curated.Select(item => ElementId(item.SavedId)).ToArray();
        var rows = new List<WidgetElement>(curated.Length + 1);
        for (var index = 0; index < curated.Length; index++)
        {
            var item = curated[index];
            var id = elementIds[index];
            var isOpening = string.Equals(
                launchingAppId, item.AppId, StringComparison.Ordinal);
            var isResolved = resolvedSavedIds.Contains(item.SavedId);
            var tileState = isOpening ? "Opening…" : isResolved ? "Ready" : "Checking…";
            var tile = UI.AppTile(
                    item.DisplayName,
                    tileState,
                    "games.launch",
                    id,
                    subtitle: AppKindLabel(item.Kind),
                    artwork: AppArtwork(item, $"{item.DisplayName} icon"),
                    accessibilityLabel:
                        $"{item.DisplayName}, {AppKindLabel(item.Kind)}, {tileState}")
                .Shortcut(ControllerButton.X, actionId: "games.remove")
                .Busy(isOpening)
                .Disabled(!isResolved || launchingAppId is not null || libraryMutationBusy ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .Selected(string.Equals(selectedAppId, item.AppId, StringComparison.Ordinal))
                .FocusUp(index == 0 ? id : elementIds[index - 1])
                .FocusDown(index + 1 < curated.Length
                    ? elementIds[index + 1]
                    : "games.open-catalog")
                .FocusLeft(id)
                .FocusRight(id)
                .AddClasses("games-card-action", "games-app-row",
                    item.Kind == WidgetAppLibraryKind.Game ? "is-game" : "is-application");
            rows.Add(tile);
        }

        rows.Add(UI.Button("Add applications", "games.open-catalog", "games.open-catalog")
            .Icon(WidgetGlyph.Play, $"Browse {items.Count} available applications")
            .Disabled(launchingAppId is not null || libraryMutationBusy ||
                LifecycleState != WidgetLifecycleState.Interactive)
            .FocusUp(elementIds[^1])
            .FocusDown("games.open-catalog")
            .FocusLeft("games.open-catalog")
            .FocusRight("games.open-catalog")
            .Classes("games-card-action", "games-app-row", "games-load-more"));
        var selected = curated.FirstOrDefault(item =>
            string.Equals(item.AppId, selectedAppId, StringComparison.Ordinal)) ?? curated[0];
        var count = UI.StatusBadge(
            $"{curated.Length} saved",
            StatusTone.Info,
            "games.section.count");
        var section = UI.SectionHeader(
                "Your library",
                "games.section",
                eyebrow: "GAMES + APPLICATIONS",
                description: "A opens · X removes · Y refreshes",
                trailing: count)
            .AddClasses("games-section-heading");
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.content",
                        section,
                        UI.VerticalScroll("games.library.scroll", rows.ToArray())
                            .Classes("games-library-scroll"))
                    .Classes("games-content"))
            .InputScope("games-apps")
            .Shortcut(ControllerButton.Y, RetryActionId)
            .Classes("games-apps-widget");
        return new WidgetView(root, ElementId(selected.SavedId), Surface: LibrarySurface);
    }

    private WidgetView RenderCatalog(
        StackElement header,
        IReadOnlyList<WidgetAppLibraryItem> items,
        IReadOnlyList<string> curatedAppIds,
        string? selectedAppId,
        string? launchingAppId,
        bool loadingMore,
        int? nextOffset,
        bool canLoadPrevious)
    {
        var curated = curatedAppIds.ToHashSet(StringComparer.Ordinal);
        var elementIds = items.Select(item => CatalogElementId(item.SavedId)).ToArray();
        var rows = new List<WidgetElement>(
            items.Count + (nextOffset is null ? 0 : 1) + (canLoadPrevious ? 1 : 0));
        if (canLoadPrevious)
        {
            rows.Add(UI.Button(
                    "Previous page", "games.previous-page", "games.previous-page")
                .Icon(WidgetGlyph.Previous, "Return to the previous application page")
                .Busy(loadingMore)
                .Disabled(launchingAppId is not null || loadingMore ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp("games.previous-page")
                .FocusDown(elementIds[0])
                .FocusLeft("games.previous-page")
                .FocusRight("games.previous-page")
                .Classes("games-card-action", "games-page-action"));
        }
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = elementIds[index];
            var saved = curated.Contains(item.SavedId);
            var down = index + 1 < items.Count
                ? elementIds[index + 1]
                : nextOffset is not null ? "games.load-more" : id;
            rows.Add(UI.AppTile(
                    item.DisplayName,
                    saved ? "Saved" : "Available",
                    "games.toggle-curation",
                    id,
                    subtitle: AppKindLabel(item.Kind),
                    artwork: AppArtwork(item, $"{item.DisplayName} icon"),
                    accessibilityLabel: saved
                        ? $"{item.DisplayName}, saved, A removes from library"
                        : $"{item.DisplayName}, available, A adds to library")
                .Selected(saved)
                .Disabled(launchingAppId is not null || loadingMore ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp(index == 0
                    ? canLoadPrevious ? "games.previous-page" : id
                    : elementIds[index - 1])
                .FocusDown(down)
                .FocusLeft(id)
                .FocusRight(id)
                .AddClasses("games-card-action", "games-app-row",
                    saved ? "is-saved" : "is-available"));
        }
        if (nextOffset is not null)
        {
            rows.Add(UI.Button("Next page", "games.load-more", "games.load-more")
                .Icon(WidgetGlyph.Refresh, "Load the next application page")
                .Busy(loadingMore)
                .Disabled(launchingAppId is not null || loadingMore ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp(elementIds[^1])
                .FocusDown("games.load-more")
                .FocusLeft("games.load-more")
                .FocusRight("games.load-more")
                .Classes("games-card-action", "games-page-action", "games-load-more"));
        }
        var selected = items.FirstOrDefault(item =>
            string.Equals(item.AppId, selectedAppId, StringComparison.Ordinal)) ?? items[0];
        var count = UI.StatusBadge(
            $"{items.Count}{(nextOffset is null ? string.Empty : "+")} available",
            StatusTone.Info,
            "games.section.count");
        var section = UI.SectionHeader(
                "Add applications",
                "games.section",
                eyebrow: "CATALOG",
                description: "A adds or removes · B returns",
                trailing: count)
            .AddClasses("games-section-heading");
        var scope = UI.Stack("games.catalog",
                section,
                UI.VerticalScroll("games.library.scroll", rows.ToArray())
                    .Classes("games-library-scroll"))
            .InputScope("games.catalog")
            .Shortcut(ControllerButton.B, "back")
            .Classes("games-catalog");
        var root = UI.Stack("games.root", header,
                UI.Stack("games.content", scope).Classes("games-content"))
            .Classes("games-apps-widget");
        return new WidgetView(root, CatalogElementId(selected.SavedId),
            ActiveInputScopeId: "games.catalog", Surface: CatalogSurface);
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
                    if (_page != GamesAppsPage.Catalog) return;
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
            case "games.toggle-curation":
                string? catalogAppId = null;
                lock (_gate)
                {
                    if (_page == GamesAppsPage.Catalog)
                        catalogAppId = _items.FirstOrDefault(item => string.Equals(
                            CatalogElementId(item.SavedId), action.SourceElementId,
                            StringComparison.Ordinal))?.AppId;
                }
                if (catalogAppId is not null)
                    await ToggleCuratedAsync(catalogAppId, cancellationToken).ConfigureAwait(false);
                return;
            case "games.remove":
                string? curatedAppId = null;
                lock (_gate)
                {
                    if (_page == GamesAppsPage.Library)
                        curatedAppId = _items.FirstOrDefault(item => string.Equals(
                            ElementId(item.SavedId), action.SourceElementId,
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
                            ElementId(item.SavedId), action.SourceElementId,
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
            IReadOnlyList<string> savedIds;
            IReadOnlyList<string> autoGameSavedIds;
            IReadOnlyList<string> excludedGameSavedIds;
            string? selectedSavedId;
            string toastMessage;
            IReadOnlyList<WidgetAppLibraryItem> candidateItems;
            var toastTone = ToastTone.Success;
            var rejected = false;
            lock (_gate)
            {
                if (_page != GamesAppsPage.Catalog) return;
                item = _items.FirstOrDefault(candidate =>
                    string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
                if (item is null) return;
                generation = Interlocked.Read(ref _generation);
                revision = _stateRevision;
                baseline = _persistedLibraryState;
                var desiredSaved = _curatedSavedIds.ToList();
                var desiredAutomatic = _autoGameSavedIds.ToList();
                var desiredExcluded = _excludedGameSavedIds.ToList();
                candidateItems = _libraryItems.Concat(_items)
                    .DistinctBy(candidate => candidate.SavedId, StringComparer.Ordinal).ToArray();
                var isVisibleMember = _libraryItems.Any(candidate => string.Equals(
                    candidate.SavedId, item.SavedId, StringComparison.Ordinal));
                if (isVisibleMember)
                {
                    if (item.Kind == WidgetAppLibraryKind.Game &&
                        !TryAddExcludedGame(desiredExcluded, item.SavedId))
                    {
                        rejected = true;
                        toastMessage =
                            "Add a previously excluded game back before removing another";
                        toastTone = ToastTone.Warning;
                        selectedSavedId = baseline.SelectedSavedId;
                    }
                    else
                    {
                        desiredSaved.Remove(item.SavedId);
                        desiredAutomatic.Remove(item.SavedId);
                        selectedSavedId = NearestSurvivingSavedIdLocked(
                            item.SavedId, desiredSaved);
                        toastMessage = $"Removed {item.DisplayName}";
                    }
                }
                else
                {
                    if (desiredSaved.Count >= MaximumCuratedItems)
                    {
                        rejected = true;
                        toastMessage = $"Your library can hold {MaximumCuratedItems} items";
                        toastTone = ToastTone.Warning;
                        selectedSavedId = baseline.SelectedSavedId;
                    }
                    else
                    {
                        desiredExcluded.Remove(item.SavedId);
                        desiredAutomatic.Remove(item.SavedId);
                        if (!desiredSaved.Contains(item.SavedId, StringComparer.Ordinal))
                            desiredSaved.Add(item.SavedId);
                        selectedSavedId = item.SavedId;
                        toastMessage = $"Added {item.DisplayName} to your library";
                    }
                }
                savedIds = desiredSaved;
                autoGameSavedIds = desiredAutomatic;
                excludedGameSavedIds = desiredExcluded;
                _libraryMutationBusy = !rejected;
            }
            Invalidate();
            if (rejected)
            {
                ShowToast("Library unchanged", toastMessage, toastTone);
                return;
            }
            var persistence = await PersistLibraryAsync(
                    savedIds, autoGameSavedIds, excludedGameSavedIds,
                    selectedSavedId, commandLifetime.Token, candidateItems,
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
            IReadOnlyList<string> savedIds;
            IReadOnlyList<string> autoGameSavedIds;
            IReadOnlyList<string> excludedGameSavedIds;
            string? selectedSavedId;
            IReadOnlyList<WidgetAppLibraryItem> candidateItems;
            string toastMessage;
            var rejected = false;
            lock (_gate)
            {
                if (_page != GamesAppsPage.Library) return;
                item = _items.FirstOrDefault(candidate =>
                    string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
                if (item is null || !_curatedSavedIds.Contains(
                        item.SavedId, StringComparer.Ordinal)) return;
                generation = Interlocked.Read(ref _generation);
                revision = _stateRevision;
                baseline = _persistedLibraryState;
                var desiredSaved = _curatedSavedIds.ToList();
                var desiredAutomatic = _autoGameSavedIds.ToList();
                var desiredExcluded = _excludedGameSavedIds.ToList();
                candidateItems = _libraryItems.ToArray();
                if (item.Kind == WidgetAppLibraryKind.Game &&
                    !TryAddExcludedGame(desiredExcluded, item.SavedId))
                {
                    rejected = true;
                    selectedSavedId = baseline.SelectedSavedId;
                    toastMessage =
                        "Add a previously excluded game back before removing another";
                }
                else
                {
                    desiredSaved.Remove(item.SavedId);
                    desiredAutomatic.Remove(item.SavedId);
                    selectedSavedId = NearestSurvivingSavedIdLocked(
                        item.SavedId, desiredSaved);
                    toastMessage = $"Removed {item.DisplayName}";
                }
                savedIds = desiredSaved;
                autoGameSavedIds = desiredAutomatic;
                excludedGameSavedIds = desiredExcluded;
                _libraryMutationBusy = !rejected;
            }
            Invalidate();
            if (rejected)
            {
                ShowToast("Library unchanged", toastMessage, ToastTone.Warning);
                return;
            }
            var persistence = await PersistLibraryAsync(
                    savedIds, autoGameSavedIds, excludedGameSavedIds,
                    selectedSavedId, commandLifetime.Token, candidateItems,
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

    private static bool TryAddExcludedGame(List<string> excludedSavedIds, string savedId)
    {
        if (excludedSavedIds.Contains(savedId, StringComparer.Ordinal)) return true;
        if (excludedSavedIds.Count == MaximumExcludedGames) return false;
        excludedSavedIds.Add(savedId);
        return true;
    }

    private string? NearestSurvivingSavedIdLocked(
        string removedSavedId,
        IReadOnlyList<string> survivingSavedIds)
    {
        var visible = ResolveCuratedItemsLocked();
        var removedIndex = visible.ToList().FindIndex(item => string.Equals(
            item.SavedId, removedSavedId, StringComparison.Ordinal));
        var surviving = visible.Where(item => survivingSavedIds.Contains(
                item.SavedId, StringComparer.Ordinal))
            .Select(item => item.SavedId)
            .ToArray();
        if (surviving.Length == 0) return survivingSavedIds.FirstOrDefault();
        return surviving[Math.Clamp(removedIndex, 0, surviving.Length - 1)];
    }

    private IReadOnlyList<WidgetAppLibraryItem> ResolveCuratedItemsLocked()
    {
        var byId = _libraryItems.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        return _curatedSavedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
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
            : $"{count} saved · {curated.Count(item => item.Kind == WidgetAppLibraryKind.Game)} " +
              "games · recent first";
    }

    private WidgetView RenderState(
        StackElement header,
        GamesAppsViewState state,
        GamesAppsPage page)
    {
        var (title, help) = state switch
        {
            GamesAppsViewState.Initial =>
                ("Your library", "Trusted installed games are added automatically."),
            GamesAppsViewState.Loading =>
                (page == GamesAppsPage.Catalog
                    ? "Loading applications"
                    : "Loading your library",
                 page == GamesAppsPage.Catalog
                    ? "The host is reading the bounded catalog only while you add an application."
                    : "The host is reconciling trusted games and applications you saved."),
            GamesAppsViewState.Empty =>
                ("No launchable apps found", "No executable Start Menu registrations were available."),
            GamesAppsViewState.PermissionDenied =>
                ("App library access is off", "Allow Games & Apps in Settings > Permissions."),
            GamesAppsViewState.LifecycleDenied =>
                ("App library is paused", "Return to this widget to load installed apps."),
            GamesAppsViewState.ServiceUnavailable =>
                ("App library unavailable", "The trusted Windows application catalog is unavailable."),
            _ => ("Installed apps could not be loaded", "Try again. No paths or command lines were exposed."),
        };
        StackElement stateSurface;
        string? initialFocus;
        if (state is GamesAppsViewState.Initial or GamesAppsViewState.Loading)
        {
            stateSurface = UI.Card("games.state",
                    state == GamesAppsViewState.Loading
                        ? UI.LoadingIndicator(
                                "games.state.loading",
                                page == GamesAppsPage.Catalog
                                    ? "Loading available applications"
                                    : "Loading saved applications")
                            .Classes("games-state-loading")
                        : UI.Icon(WidgetGlyph.Play, "games.state.icon", "Application library")
                            .Classes("games-state-icon"),
                    UI.Text(title, "games.state.title", title).Classes("games-state-title"),
                    UI.Text(help, "games.state.help", help).Classes("games-state-help"))
                .AddClasses("games-state-surface", "games-state-progress");
            initialFocus = null;
        }
        else if (state == GamesAppsViewState.Empty)
        {
            stateSurface = ConfigureStateAction(UI.EmptyState(
                    title,
                    help,
                    "games.state",
                    new ComponentAction("Try again", RetryActionId, WidgetGlyph.Refresh),
                    WidgetGlyph.Play),
                !IsActive);
            initialFocus = "games.state.action";
        }
        else
        {
            var tone = state is GamesAppsViewState.PermissionDenied or
                GamesAppsViewState.LifecycleDenied
                    ? AlertTone.Warning
                    : AlertTone.Danger;
            stateSurface = ConfigureStateAction(UI.Alert(
                title,
                help,
                tone,
                "games.state",
                new ComponentAction("Try again", RetryActionId, WidgetGlyph.Refresh)),
                !IsActive);
            initialFocus = "games.state.action";
        }
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.content", stateSurface)
                    .Classes("games-content", "games-state-shell"))
            .InputScope("games-apps")
            .Classes("games-apps-widget", "has-state");
        return new WidgetView(root, initialFocus, Surface: StateSurface);
    }

    private static StackElement ConfigureStateAction(
        StackElement surface,
        bool disabled) => surface with
    {
        Children = surface.Children.Select(child => child is ButtonElement button
            ? button.Disabled(disabled).AddClasses("games-primary-action")
            : child).ToArray(),
        StyleClasses = surface.StyleClasses.Concat(["games-state-surface"]).ToArray(),
    };

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
            _nextOffset = null;
            _catalogOffset = 0;
            _catalogBackOffsets.Clear();
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
            var state = GamesAppsLibraryStateReconciler.Normalize(
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
                var fallback = NormalizeResolved(resolved, state.SavedIds);
                lock (_gate)
                {
                    if (Interlocked.Read(ref _generation) != generation) return;
                    ApplyPersistedStateLocked(state, persisted.Revision, fallback);
                    _hasLibrarySnapshot = true;
                    if (_page == GamesAppsPage.Library)
                    {
                        _nextOffset = null;
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

            var preliminary = GamesAppsLibraryStateReconciler.Reconcile(
                persisted.Exists ? persisted.Value : null, catalog, liveSelectedSavedId);
            var detailedItems = await ResolveCuratedDetailsAsync(
                preliminary.VisibleItems,
                preliminary.State.SavedIds,
                cancellationToken).ConfigureAwait(false);
            var detailedBySaved = detailedItems.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var authoritativeItems = catalog.Select(item =>
                    detailedBySaved.TryGetValue(item.SavedId, out var detailed)
                        ? detailed
                        : item)
                .ToArray();
            var reconciliation = GamesAppsLibraryStateReconciler.Reconcile(
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
                    _nextOffset = null;
                    _viewState = GamesAppsViewState.Ready;
                    _status = persistence.Saved
                        ? LibraryStatusLocked()
                        : persistence.Rejected
                            ? "Game exclusion storage is full"
                            : LibraryStatusLocked() + " · saving failed";
                }
                committedAddedGames = persistence.Saved
                    ? reconciliation.AddedGameSavedIds.Count(id =>
                        _autoGameSavedIds.Contains(id, StringComparer.Ordinal) &&
                        _libraryItems.Any(item => item.SavedId == id &&
                            item.Kind == WidgetAppLibraryKind.Game))
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
        IReadOnlyList<WidgetAppLibraryItem> catalogItems,
        IReadOnlyList<string> curatedSavedIds,
        CancellationToken cancellationToken)
    {
        if (catalogItems.Count == 0) return [];
        try
        {
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    catalogItems.Select(item => item.SavedId).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            var detailedBySaved = NormalizeResolved(
                    resolved, catalogItems.Select(item => item.SavedId).ToArray())
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
            return catalogItems;
        }
    }

    private async Task<IReadOnlyList<WidgetAppLibraryItem>> ReadCatalogAsync(
        CancellationToken cancellationToken)
    {
        var items = new List<WidgetAppLibraryItem>(MaximumItems);
        var appIds = new HashSet<string>(StringComparer.Ordinal);
        var savedIds = new HashSet<string>(StringComparer.Ordinal);
        var requestedOffsets = new HashSet<int>();
        var offset = 0;
        while (items.Count < MaximumItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!requestedOffsets.Add(offset)) throw MalformedCatalog();
            var page = await HostServices.AppLibrary.GetPageAsync(
                    offset, PageSize, cancellationToken).ConfigureAwait(false);
            if (page?.Items is null || page.Items.Count > PageSize) throw MalformedCatalog();
            foreach (var item in Normalize(page.Items))
            {
                if (!appIds.Add(item.AppId) || !savedIds.Add(item.SavedId))
                    throw MalformedCatalog();
                items.Add(item);
                if (items.Count == MaximumItems) break;
            }
            if (page.NextOffset is not int next) break;
            if (next <= offset || next > MaximumItems) throw MalformedCatalog();
            offset = next;
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
            var page = await HostServices.AppLibrary.GetPageAsync(0, PageSize, commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, CatalogPageTransition.Initial, generation, requestedOffset: 0);
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

            int? offset;
            long generation;
            lock (_gate)
            {
                offset = _nextOffset;
                generation = Interlocked.Read(ref _generation);
                if (offset is null || _page != GamesAppsPage.Catalog ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                    return;
                _loadingMore = true;
                _status = "Loading more installed apps…";
            }
            Invalidate();
            var page = await HostServices.AppLibrary.GetPageAsync(
                    offset.Value, PageSize, commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, CatalogPageTransition.Next, generation, offset.Value);
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

            int offset;
            long generation;
            lock (_gate)
            {
                if (_catalogBackOffsets.Count == 0 || _page != GamesAppsPage.Catalog ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                    return;
                offset = _catalogBackOffsets[^1];
                generation = Interlocked.Read(ref _generation);
                _loadingMore = true;
                _status = "Loading the previous application page…";
            }
            Invalidate();
            var page = await HostServices.AppLibrary.GetPageAsync(
                    offset, PageSize, commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, CatalogPageTransition.Previous, generation, offset);
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
                _status = $"Opening {selected.DisplayName}…";
            }
            Invalidate();
            await HostServices.AppLibrary.LaunchAsync(
                    appId,
                    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
                    commandLifetime.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_curatedSavedIds.Remove(selected.SavedId))
                    _curatedSavedIds.Insert(0, selected.SavedId);
                var bySaved = _libraryItems.ToDictionary(
                    item => item.SavedId, StringComparer.Ordinal);
                _libraryItems = _curatedSavedIds.Where(bySaved.ContainsKey)
                    .Select(savedId => bySaved[savedId]).ToArray();
                _items = _libraryItems;
                persistedOrder = _curatedSavedIds.ToArray();
                persistedAutoGames = _autoGameSavedIds.ToArray();
                persistedExclusions = _excludedGameSavedIds.ToArray();
                persistenceCandidates = _libraryItems.ToArray();
                _launchingAppId = null;
                _status = $"Opened {selected.DisplayName}";
            }
            ShowToast("Application opened", $"Opened {selected.DisplayName}", ToastTone.Success);
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
                : $"{selected.DisplayName} could not be opened");
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
        CatalogPageTransition transition,
        long generation,
        int requestedOffset)
    {
        var normalized = Normalize(page?.Items);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            var emptyCatalog = transition == CatalogPageTransition.Initial &&
                _page == GamesAppsPage.Catalog && normalized.Count == 0;
            if (emptyCatalog)
            {
                _page = GamesAppsPage.Library;
                _items = _libraryItems;
                _nextOffset = null;
                _selectedAppId = _items.FirstOrDefault()?.AppId;
                _viewState = GamesAppsViewState.Ready;
                _status = "No additional launchable applications were found";
            }
            else
            {
                if (normalized.Count == 0 && transition != CatalogPageTransition.Initial)
                {
                    if (transition == CatalogPageTransition.Next)
                        _nextOffset = null;
                    _status = "No more launchable applications were found";
                    return;
                }
                if (transition == CatalogPageTransition.Initial)
                {
                    _catalogBackOffsets.Clear();
                }
                else if (transition == CatalogPageTransition.Next)
                {
                    _catalogBackOffsets.Add(_catalogOffset);
                }
                else if (_catalogBackOffsets.Count != 0 &&
                    _catalogBackOffsets[^1] == requestedOffset)
                {
                    _catalogBackOffsets.RemoveAt(_catalogBackOffsets.Count - 1);
                }
                _catalogOffset = requestedOffset;
                _items = normalized.Take(PageSize).ToArray();
                _nextOffset = requestedOffset + _items.Count < MaximumItems &&
                    page?.NextOffset is int next &&
                    next > requestedOffset && next <= MaximumItems
                        ? next
                        : null;
                _selectedAppId = _items.FirstOrDefault()?.AppId;
                _viewState = _items.Count == 0 ? GamesAppsViewState.Empty : GamesAppsViewState.Ready;
                _status = _items.Count == 0
                    ? "No launchable applications or games found"
                    : _page == GamesAppsPage.Catalog
                        ? CatalogStatus(_items, _nextOffset is not null)
                        : LibraryStatusLocked();
            }
        }
        Invalidate();
    }

    private static IReadOnlyList<WidgetAppLibraryItem> Normalize(
        IReadOnlyList<WidgetAppLibraryItem>? items)
    {
        if (items is null) return [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return items.Where(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.AppId) &&
                !string.IsNullOrWhiteSpace(item.SavedId) &&
                !string.IsNullOrWhiteSpace(item.DisplayName) && ids.Add(item.AppId))
            .Take(PageSize)
            .Select(item => item with
            {
                DisplayName = item.DisplayName.Trim().Length > 120
                    ? item.DisplayName.Trim()[..120]
                    : item.DisplayName.Trim(),
            })
            .ToArray();
    }

    private static IReadOnlyList<WidgetAppLibraryItem> NormalizeResolved(
        IReadOnlyList<WidgetAppLibraryItem>? items,
        IReadOnlyList<string> requestedSavedIds)
    {
        var requested = requestedSavedIds.ToHashSet(StringComparer.Ordinal);
        var seenApp = new HashSet<string>(StringComparer.Ordinal);
        var seenSaved = new HashSet<string>(StringComparer.Ordinal);
        var bySaved = (items ?? [])
            .Where(item => item is not null && GamesAppsLibraryStateReconciler.IsOpaqueId(item.AppId) &&
                GamesAppsLibraryStateReconciler.IsOpaqueId(item.SavedId) && requested.Contains(item.SavedId) &&
                !string.IsNullOrWhiteSpace(item.DisplayName) && seenApp.Add(item.AppId) &&
                seenSaved.Add(item.SavedId))
            .ToDictionary(item => item.SavedId, item => item, StringComparer.Ordinal);
        return requestedSavedIds.Where(bySaved.ContainsKey).Select(savedId =>
        {
            var item = bySaved[savedId];
            var name = item.DisplayName.Trim();
            return item with { DisplayName = name.Length > 120 ? name[..120] : name };
        }).ToArray();
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
        var normalized = GamesAppsLibraryStateReconciler.Normalize(
            new GamesAppsLibraryState(3, savedIds, selectedSavedId)
            {
                AutoGameSavedIds = autoGameSavedIds,
                ExcludedGameSavedIds = excludedGameSavedIds,
                DisplayItems = BuildDisplayItems(
                    baseline, candidateItems, savedIds, autoGameSavedIds,
                    excludedGameSavedIds),
            });
        try
        {
            var attempted = normalized;
            for (var attempt = 0; attempt != 2; attempt++)
            {
                try
                {
                    var mutation = await HostServices.PrivateState.WriteAsync(
                            attempted, revision, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (requiredGeneration is { } expected &&
                            Interlocked.Read(ref _generation) != expected)
                            return new LibraryPersistenceResult(Saved: false, Rejected: false);
                        ApplyPersistedStateLocked(attempted, mutation.Revision, candidateItems);
                    }
                    Invalidate();
                    return new LibraryPersistenceResult(Saved: true, Rejected: false);
                }
                catch (WidgetCapabilityException exception) when (
                    exception.ErrorCode == "state_conflict" && attempt == 0)
                {
                    var latestSnapshot = await HostServices.PrivateState
                        .ReadAsync<GamesAppsLibraryState>(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    var latest = GamesAppsLibraryStateReconciler.Normalize(
                        latestSnapshot.Exists ? latestSnapshot.Value : null);
                    var merge = GamesAppsLibraryStateReconciler.Merge(
                        baseline, normalized, latest);
                    if (!merge.Accepted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lock (_gate)
                        {
                            if (requiredGeneration is { } expected &&
                                Interlocked.Read(ref _generation) != expected)
                                return new LibraryPersistenceResult(
                                    Saved: false, Rejected: true);
                            ApplyPersistedStateLocked(
                                latest, latestSnapshot.Revision, candidateItems);
                            _status = "Game exclusion storage is full";
                        }
                        Invalidate();
                        return new LibraryPersistenceResult(Saved: false, Rejected: true);
                    }
                    attempted = merge.State;
                    revision = latestSnapshot.Revision;
                }
            }
            throw new InvalidOperationException("Private state retry bound was exceeded.");
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
        _curatedSavedIds.Clear();
        _curatedSavedIds.AddRange(state.SavedIds);
        _autoGameSavedIds.Clear();
        _autoGameSavedIds.AddRange(state.AutoGameSavedIds);
        _excludedGameSavedIds.Clear();
        _excludedGameSavedIds.AddRange(state.ExcludedGameSavedIds);
        var automatic = state.AutoGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var excluded = state.ExcludedGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var candidates = candidateItems ?? _libraryItems
            .Where(item => _resolvedSavedIds.Contains(item.SavedId))
            .ToArray();
        var bySaved = candidates
            .DistinctBy(item => item.SavedId, StringComparer.Ordinal)
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var displayBySaved = state.DisplayItems
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        _resolvedSavedIds.Clear();
        _libraryItems = state.SavedIds.Where(savedId =>
                !excluded.Contains(savedId) &&
                (bySaved.TryGetValue(savedId, out var item)
                    ? !automatic.Contains(savedId) || item.Kind == WidgetAppLibraryKind.Game
                    : displayBySaved.ContainsKey(savedId)))
            .Select(savedId =>
            {
                if (bySaved.TryGetValue(savedId, out var resolved))
                {
                    _resolvedSavedIds.Add(savedId);
                    return resolved;
                }
                return ProjectedItem(displayBySaved[savedId]);
            }).ToArray();
        if (_page == GamesAppsPage.Library) _items = _libraryItems;
        _selectedAppId = SelectAppId(
            _libraryItems, state.SelectedSavedId, selectedSavedId);
    }

    private static WidgetAppLibraryItem ProjectedItem(GamesAppsPersistedDisplayItem item) => new(
        PendingAppId(item.SavedId),
        item.DisplayName,
        item.Kind)
    {
        SavedId = item.SavedId,
    };

    private static IReadOnlyList<GamesAppsPersistedDisplayItem> BuildDisplayItems(
        GamesAppsLibraryState baseline,
        IReadOnlyList<WidgetAppLibraryItem>? candidates,
        IReadOnlyList<string> savedIds,
        IReadOnlyList<string> automaticIds,
        IReadOnlyList<string> excludedIds)
    {
        var saved = savedIds.ToHashSet(StringComparer.Ordinal);
        var automatic = automaticIds.ToHashSet(StringComparer.Ordinal);
        var excluded = excludedIds.ToHashSet(StringComparer.Ordinal);
        var display = baseline.DisplayItems
            .Where(item => saved.Contains(item.SavedId) && !excluded.Contains(item.SavedId))
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        foreach (var item in candidates ?? [])
        {
            if (!saved.Contains(item.SavedId) || excluded.Contains(item.SavedId)) continue;
            if (automatic.Contains(item.SavedId) && item.Kind != WidgetAppLibraryKind.Game)
                display.Remove(item.SavedId);
            else
                display[item.SavedId] = GamesAppsLibraryStateReconciler.ToDisplayItem(item);
        }
        return savedIds.Where(display.ContainsKey)
            .Select(savedId => display[savedId])
            .ToArray();
    }

    private static string PendingAppId(string savedId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(savedId));
        return "pending." + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
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
                _nextOffset = null;
                _launchingAppId = null;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked() + " · refresh unavailable";
            }
            else
            {
                _viewState = state;
                _status = status;
                _items = [];
                _nextOffset = null;
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
            _toast = new ToastNotice(title, message, tone, duration);
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

    private static string ElementId(string opaqueId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return "games.item." + Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
    }

    private static string AppKindLabel(WidgetAppLibraryKind kind) =>
        kind == WidgetAppLibraryKind.Game ? "Game" : "Application";

    private static string CatalogStatus(
        IReadOnlyList<WidgetAppLibraryItem> items,
        bool hasMore)
    {
        var games = items.Count(item => item.Kind == WidgetAppLibraryKind.Game);
        return $"{items.Count}{(hasMore ? "+" : string.Empty)} available · " +
               $"{games} {(games == 1 ? "game" : "games")} loaded";
    }

    private static TileArtwork AppArtwork(WidgetAppLibraryItem item, string accessibilityLabel) =>
        item.IconPngBase64 is { Length: > 0 } png
            ? TileArtwork.FromInlinePng(png, accessibilityLabel, ImageFit.Contain)
            : TileArtwork.FromGlyph(WidgetGlyph.Play, accessibilityLabel);

    private static string CatalogElementId(string opaqueId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return "games.catalog.item." +
            Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
    }
}
