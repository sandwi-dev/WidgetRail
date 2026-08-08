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

    private static readonly WidgetSurfaceHints LibrarySurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 820,
        PreferredHeight = 430,
        MinimumWidth = 420,
        MinimumHeight = 300,
    };

    private static readonly WidgetSurfaceHints CatalogSurface = LibrarySurface with
    {
        PreferredHeight = 450,
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
    private IReadOnlyDictionary<string, string> _appByElementId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _curatedSavedIds = [];
    private GamesAppsViewState _viewState = GamesAppsViewState.Initial;
    private GamesAppsPage _page = GamesAppsPage.Library;
    private string _status = "Your launch library loads when this widget becomes visible";
    private string? _selectedAppId;
    private string? _launchingAppId;
    private bool _loadingMore;
    private bool _hasLibrarySnapshot;
    private int? _nextOffset;
    private CancellationTokenSource? _activeRun;
    private CancellationTokenSource? _toastLifetime;
    private ToastNotice? _toast;
    private long _generation;
    private long _toastGeneration;
    private long _stateRevision;

    private sealed record PersistedLibraryState(
        int Version,
        IReadOnlyList<string> SavedIds,
        string? SelectedSavedId);

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
        int? nextOffset;
        GamesAppsPage page;
        IReadOnlyList<string> curatedAppIds;
        ToastNotice? toast;
        lock (_gate)
        {
            items = _items;
            state = _viewState;
            status = _status;
            selectedAppId = _selectedAppId;
            launchingAppId = _launchingAppId;
            loadingMore = _loadingMore;
            nextOffset = _nextOffset;
            page = _page;
            curatedAppIds = _curatedSavedIds.ToArray();
            toast = _toast;
        }

        var headerChildren = new List<WidgetElement>
        {
                UI.Text("LIBRARY", "games.eyebrow", "Installed application library")
                    .Classes("games-eyebrow"),
                UI.Text("Games & Apps", "games.title", "Games and Apps")
                    .Classes("games-title"),
                UI.Text(status, "games.status", status).Classes(
                    "games-status",
                    state == GamesAppsViewState.Ready ? "is-ready" :
                    state is GamesAppsViewState.PermissionDenied or GamesAppsViewState.Error
                        ? "is-error" : "is-neutral"),
        };
        if (toast is not null)
            headerChildren.Add(UI.Toast(
                toast.Title, toast.Message, toast.Tone, "games.toast", toast.Duration));
        var header = UI.Stack("games.header", headerChildren.ToArray())
            .Classes("games-header");

        if (state != GamesAppsViewState.Ready)
            return RenderState(header, state);

        return page == GamesAppsPage.Catalog
            ? RenderCatalog(header, items, curatedAppIds, selectedAppId, launchingAppId,
                loadingMore, nextOffset)
            : RenderLibrary(header, items, curatedAppIds, selectedAppId, launchingAppId);
    }

    private WidgetView RenderLibrary(
        StackElement header,
        IReadOnlyList<WidgetAppLibraryItem> items,
        IReadOnlyList<string> curatedAppIds,
        string? selectedAppId,
        string? launchingAppId)
    {
        var byId = items.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var curated = curatedAppIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        if (curated.Length == 0)
        {
            lock (_gate) _appByElementId = new Dictionary<string, string>(StringComparer.Ordinal);
            var add = UI.Button("Add app", "games.open-catalog", "games.open-catalog")
                .Icon(WidgetGlyph.Play, "Choose applications for your library")
                .Disabled(LifecycleState != WidgetLifecycleState.Interactive)
                .Classes("games-retry");
            var emptyRoot = UI.Stack("games.root",
                    header,
                    UI.Stack("games.state",
                            UI.Text("Build your library", "games.state.title", "Build your library")
                                .Classes("games-state-title"),
                            UI.Text("Choose only the games and applications you want in the overlay.",
                                    "games.state.help", "Choose applications for your library")
                                .Classes("games-state-help"),
                            add)
                        .Classes("games-state-card"))
                .InputScope("games-apps")
                .Classes("games-apps-widget", "has-state");
            return new WidgetView(emptyRoot, "games.open-catalog", Surface: StateSurface);
        }

        var elementIds = curated.Select(item => ElementId(item.AppId)).ToArray();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<WidgetElement>(curated.Length + 1);
        for (var index = 0; index < curated.Length; index++)
        {
            var item = curated[index];
            var id = elementIds[index];
            map[id] = item.AppId;
            var isOpening = string.Equals(
                launchingAppId, item.AppId, StringComparison.Ordinal);
            var tileState = isOpening ? "Opening…" : "Ready";
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
                .Disabled(launchingAppId is not null ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .Selected(string.Equals(selectedAppId, item.AppId, StringComparison.Ordinal))
                .FocusUp(index == 0 ? id : elementIds[index - 1])
                .FocusDown(index + 1 < curated.Length
                    ? elementIds[index + 1]
                    : "games.open-catalog")
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("games-card-action", "games-app-row",
                    item.Kind == WidgetAppLibraryKind.Game ? "is-game" : "is-application");
            rows.Add(tile);
        }

        rows.Add(UI.Button("Add app", "games.open-catalog", "games.open-catalog")
            .Icon(WidgetGlyph.Play, $"Browse {items.Count} available applications")
            .Disabled(launchingAppId is not null ||
                LifecycleState != WidgetLifecycleState.Interactive)
            .FocusUp(elementIds[^1])
            .FocusDown("games.open-catalog")
            .FocusLeft("games.open-catalog")
            .FocusRight("games.open-catalog")
            .Classes("games-card-action", "games-app-row", "games-load-more"));
        lock (_gate) _appByElementId = map;

        var selected = curated.FirstOrDefault(item =>
            string.Equals(item.AppId, selectedAppId, StringComparison.Ordinal)) ?? curated[0];
        var root = UI.Stack("games.root",
                header,
                UI.Row("games.section.heading",
                        UI.Text("YOUR LIBRARY", "games.section.label", "Your library")
                            .Classes("games-section-label"),
                        UI.Text($"{curated.Length} saved",
                                "games.section.count", $"{curated.Length} saved applications")
                            .Classes("games-section-count"))
                    .Classes("games-section-heading"),
                UI.VerticalScroll("games.library.scroll", rows.ToArray())
                    .Classes("games-library-scroll"))
            .InputScope("games-apps")
            .Shortcut(ControllerButton.Y, RetryActionId)
            .Classes("games-apps-widget");
        return new WidgetView(root, ElementId(selected.AppId), Surface: LibrarySurface);
    }

    private WidgetView RenderCatalog(
        StackElement header,
        IReadOnlyList<WidgetAppLibraryItem> items,
        IReadOnlyList<string> curatedAppIds,
        string? selectedAppId,
        string? launchingAppId,
        bool loadingMore,
        int? nextOffset)
    {
        var curated = curatedAppIds.ToHashSet(StringComparer.Ordinal);
        var elementIds = items.Select(item => CatalogElementId(item.AppId)).ToArray();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<WidgetElement>(items.Count + (nextOffset is null ? 0 : 1));
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = elementIds[index];
            map[id] = item.AppId;
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
                .FocusUp(index == 0 ? id : elementIds[index - 1])
                .FocusDown(down)
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("games-card-action", "games-app-row",
                    saved ? "is-saved" : "is-available"));
        }
        if (nextOffset is not null)
        {
            rows.Add(UI.Button("Load more", "games.load-more", "games.load-more")
                .Icon(WidgetGlyph.Refresh, $"Load more than {items.Count} applications")
                .Busy(loadingMore)
                .Disabled(launchingAppId is not null || loadingMore ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp(elementIds[^1])
                .FocusDown("games.load-more")
                .FocusLeft("games.load-more")
                .FocusRight("games.load-more")
                .Classes("games-card-action", "games-app-row", "games-load-more"));
        }
        lock (_gate) _appByElementId = map;

        var selected = items.FirstOrDefault(item =>
            string.Equals(item.AppId, selectedAppId, StringComparison.Ordinal)) ?? items[0];
        var scope = UI.Stack("games.catalog",
                UI.Row("games.section.heading",
                        UI.Text("ADD APPLICATIONS", "games.section.label", "Add applications")
                            .Classes("games-section-label"),
                        UI.Text($"{items.Count}{(nextOffset is null ? string.Empty : "+")} available",
                                "games.section.count", $"{items.Count} applications available")
                            .Classes("games-section-count"))
                    .Classes("games-section-heading"),
                UI.VerticalScroll("games.library.scroll", rows.ToArray())
                    .Classes("games-library-scroll"))
            .InputScope("games.catalog")
            .Shortcut(ControllerButton.B, "back")
            .Classes("games-catalog");
        var root = UI.Stack("games.root", header, scope).Classes("games-apps-widget");
        return new WidgetView(root, CatalogElementId(selected.AppId),
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
                    _page = GamesAppsPage.Library;
                    _items = _libraryItems;
                    _selectedAppId = ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
                    _status = LibraryStatusLocked();
                }
                Invalidate();
                return;
            case "games.open-catalog":
                await OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.toggle-curation":
                string? catalogAppId;
                lock (_gate) _appByElementId.TryGetValue(action.SourceElementId, out catalogAppId);
                if (catalogAppId is not null)
                    await ToggleCuratedAsync(catalogAppId, cancellationToken).ConfigureAwait(false);
                return;
            case "games.remove":
                string? curatedAppId;
                lock (_gate) _appByElementId.TryGetValue(action.SourceElementId, out curatedAppId);
                if (curatedAppId is not null)
                    await RemoveCuratedAsync(curatedAppId, cancellationToken).ConfigureAwait(false);
                return;
            case RetryActionId:
                await RetryActiveRunAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.load-more":
                await LoadMoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            case "games.launch":
                string? appId;
                lock (_gate) _appByElementId.TryGetValue(action.SourceElementId, out appId);
                if (appId is not null)
                    await LaunchAsync(appId, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task ToggleCuratedAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        IReadOnlyList<string>? savedIds = null;
        string? selectedSavedId = null;
        string? toastMessage = null;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(candidate =>
                string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
            if (item is null) return;
            _selectedAppId = item.AppId;
            if (_curatedSavedIds.Remove(item.SavedId))
            {
                _libraryItems = _libraryItems
                    .Where(candidate => !string.Equals(
                        candidate.SavedId, item.SavedId, StringComparison.Ordinal)).ToArray();
                _status = $"Removed {item.DisplayName} from your library";
                toastMessage = _status;
            }
            else
            {
                _curatedSavedIds.Add(item.SavedId);
                _libraryItems = _libraryItems.Append(item).ToArray();
                _status = $"Added {item.DisplayName} to your library";
                toastMessage = _status;
            }
            savedIds = _curatedSavedIds.ToArray();
            selectedSavedId = item.SavedId;
        }
        ShowToast("Library updated", toastMessage!, ToastTone.Success);
        await PersistLibraryAsync(savedIds, selectedSavedId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveCuratedAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        IReadOnlyList<string>? savedIds = null;
        string? selectedSavedId = null;
        string? toastMessage = null;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(candidate =>
                string.Equals(candidate.AppId, appId, StringComparison.Ordinal));
            if (item is null || !_curatedSavedIds.Remove(item.SavedId)) return;
            _libraryItems = _libraryItems
                .Where(candidate => !string.Equals(
                    candidate.SavedId, item.SavedId, StringComparison.Ordinal)).ToArray();
            _items = _libraryItems;
            _selectedAppId = ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
            _status = $"Removed {item.DisplayName} from your library";
            toastMessage = _status;
            savedIds = _curatedSavedIds.ToArray();
            selectedSavedId = ResolveCuratedItemsLocked().FirstOrDefault()?.SavedId;
        }
        ShowToast("Library updated", toastMessage!, ToastTone.Success);
        await PersistLibraryAsync(savedIds, selectedSavedId, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<WidgetAppLibraryItem> ResolveCuratedItemsLocked()
    {
        var byId = _libraryItems.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        return _curatedSavedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }

    private string LibraryStatusLocked()
    {
        var count = ResolveCuratedItemsLocked().Count;
        return count == 0
            ? "Your library is empty · choose only the apps you want here"
            : $"{count} saved {(count == 1 ? "application" : "applications")} · most recently opened first";
    }

    private WidgetView RenderState(StackElement header, GamesAppsViewState state)
    {
        var (title, help) = state switch
        {
            GamesAppsViewState.Initial =>
                ("Your library", "Saved games and applications appear here."),
            GamesAppsViewState.Loading =>
                (_page == GamesAppsPage.Catalog
                    ? "Loading applications"
                    : "Loading your library",
                 _page == GamesAppsPage.Catalog
                    ? "The host is reading the bounded catalog only while you add an application."
                    : "The host is resolving only applications you previously saved."),
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
        var stateChildren = new List<WidgetElement>();
        stateChildren.Add(state == GamesAppsViewState.Loading
            ? UI.LoadingIndicator(
                    "games.state.loading",
                    _page == GamesAppsPage.Catalog
                        ? "Loading available applications"
                        : "Loading saved applications")
                .Classes("games-state-loading")
            : UI.Icon(WidgetGlyph.Play, "games.state.icon", "Application library")
                .Classes("games-state-icon"));
        stateChildren.Add(UI.Text(title, "games.state.title", title).Classes("games-state-title"));
        stateChildren.Add(UI.Text(help, "games.state.help", help).Classes("games-state-help"));
        string? initialFocus = null;
        if (state is not (GamesAppsViewState.Initial or GamesAppsViewState.Loading))
        {
            stateChildren.Add(UI.Button("Try again", RetryActionId, "games.retry")
                .Icon(WidgetGlyph.Refresh, "Reload installed applications")
                .Disabled(!IsActive)
                .Classes("games-retry"));
            initialFocus = "games.retry";
        }
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.state", stateChildren.ToArray())
                    .Classes("games-state-card"))
            .InputScope("games-apps")
            .Classes("games-apps-widget", "has-state");
        return new WidgetView(root, initialFocus, Surface: StateSurface);
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        StopActiveRun();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
        _activeRun = lifetime;
        var generation = Interlocked.Increment(ref _generation);
        bool hasLibrarySnapshot;
        lock (_gate)
        {
            _page = GamesAppsPage.Library;
            _items = _libraryItems;
            _nextOffset = null;
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
                _status = "Preparing your saved library";
            }
            _launchingAppId = null;
            _loadingMore = false;
            hasLibrarySnapshot = _hasLibrarySnapshot;
        }
        if (hasLibrarySnapshot) return;
        _ = LoadSavedLibraryAsync(generation, lifetime.Token);
        _ = ShowColdLoadingAfterDelayAsync(generation, lifetime.Token);
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
                _status = "Loading your saved library…";
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
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                ActiveCancellationToken, cancellationToken);
            _activeRun = lifetime;
            var generation = Interlocked.Increment(ref _generation);
            lock (_gate)
            {
                _viewState = GamesAppsViewState.Loading;
                _page = GamesAppsPage.Library;
                _status = "Reloading your saved library…";
                _launchingAppId = null;
                _loadingMore = false;
            }
            Invalidate();
            await LoadSavedLibraryAsync(generation, lifetime.Token).ConfigureAwait(false);
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
        var lifetime = Interlocked.Exchange(ref _activeRun, null);
        lifetime?.Cancel();
        lifetime?.Dispose();
        lock (_gate)
        {
            _launchingAppId = null;
            _loadingMore = false;
        }
    }

    private async Task LoadSavedLibraryAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            var persisted = await HostServices.PrivateState.ReadAsync<PersistedLibraryState>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var state = NormalizePersistedState(persisted.Exists ? persisted.Value : null);
            var resolved = state.SavedIds.Count == 0
                ? Array.Empty<WidgetAppLibraryItem>()
                : await HostServices.AppLibrary.ResolveSavedAsync(state.SavedIds, cancellationToken)
                    .ConfigureAwait(false);
            var normalized = NormalizeResolved(resolved, state.SavedIds);
            lock (_gate)
            {
                if (Interlocked.Read(ref _generation) != generation) return;
                var liveSelectedSavedId = _libraryItems.FirstOrDefault(item =>
                    string.Equals(item.AppId, _selectedAppId, StringComparison.Ordinal))?.SavedId;
                _stateRevision = persisted.Revision;
                _curatedSavedIds.Clear();
                _curatedSavedIds.AddRange(normalized.Select(item => item.SavedId));
                _items = normalized;
                _libraryItems = normalized;
                _nextOffset = null;
                var selectedSavedId = liveSelectedSavedId ?? state.SelectedSavedId;
                _selectedAppId = normalized.FirstOrDefault(item =>
                    string.Equals(item.SavedId, selectedSavedId,
                        StringComparison.Ordinal))?.AppId ?? normalized.FirstOrDefault()?.AppId;
                _hasLibrarySnapshot = true;
                _viewState = GamesAppsViewState.Ready;
                _status = LibraryStatusLocked();
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplyError(exception, generation);
        }
    }

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
            long generation;
            lock (_gate)
            {
                generation = Interlocked.Read(ref _generation);
                _page = GamesAppsPage.Catalog;
                _viewState = GamesAppsViewState.Loading;
                _status = "Loading applications you can add…";
                _loadingMore = true;
            }
            Invalidate();
            var page = await HostServices.AppLibrary.GetPageAsync(0, PageSize, commandLifetime.Token)
                .ConfigureAwait(false);
            ApplyPage(page, append: false, generation, requestedOffset: 0);
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
            ApplyPage(page, append: true, generation, offset.Value);
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

    private async Task LaunchAsync(string appId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        var acquired = false;
        WidgetAppLibraryItem? selected = null;
        IReadOnlyList<string>? persistedOrder = null;
        try
        {
            acquired = await _commandGate.WaitAsync(0, commandLifetime.Token).ConfigureAwait(false);
            if (!acquired) return;
            lock (_gate)
            {
                selected = _items.FirstOrDefault(item => item.AppId == appId);
                if (selected is null || LifecycleState != WidgetLifecycleState.Interactive)
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
                _launchingAppId = null;
                _status = $"Opened {selected.DisplayName}";
            }
            ShowToast("Application opened", $"Opened {selected.DisplayName}", ToastTone.Success);
            await PersistLibraryAsync(
                persistedOrder, selected.SavedId, commandLifetime.Token).ConfigureAwait(false);
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
        bool append,
        long generation,
        int requestedOffset)
    {
        var normalized = Normalize(page?.Items);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            var emptyCatalog = !append && _page == GamesAppsPage.Catalog && normalized.Count == 0;
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
                var priorIds = _items.Select(item => item.AppId).ToHashSet(StringComparer.Ordinal);
                var added = append
                    ? normalized.Where(item => priorIds.Add(item.AppId))
                        .Take(Math.Max(0, MaximumItems - _items.Count)).ToArray()
                    : normalized.Take(MaximumItems).ToArray();
                var combined = append ? _items.Concat(added).ToArray() : added;
                var firstNew = append ? added.FirstOrDefault() : null;
                _items = combined;
                _nextOffset = combined.Length < MaximumItems &&
                    page?.NextOffset is int next &&
                    next > requestedOffset && next <= MaximumItems
                        ? next
                        : null;
                if (firstNew is not null)
                    _selectedAppId = firstNew.AppId;
                else if (_selectedAppId is null || !_items.Any(item => item.AppId == _selectedAppId))
                    _selectedAppId = _page == GamesAppsPage.Catalog
                        ? _items.FirstOrDefault()?.AppId
                        : ResolveCuratedItemsLocked().FirstOrDefault()?.AppId;
                _viewState = _items.Count == 0 ? GamesAppsViewState.Empty : GamesAppsViewState.Ready;
                _status = _items.Count == 0
                    ? "No launchable Start Menu apps found"
                    : _page == GamesAppsPage.Catalog
                        ? $"{_items.Count}{(_nextOffset is null ? string.Empty : "+")} applications available"
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

    private static PersistedLibraryState NormalizePersistedState(PersistedLibraryState? state)
    {
        if (state is null || state.Version != 1 || state.SavedIds is null)
            return new PersistedLibraryState(1, [], null);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var saved = state.SavedIds
            .Where(id => IsOpaqueId(id) && seen.Add(id))
            .Take(WidgetAppLibraryService.MaximumSavedItems)
            .ToArray();
        var selected = state.SelectedSavedId is { } candidate &&
                       saved.Contains(candidate, StringComparer.Ordinal)
            ? candidate
            : saved.FirstOrDefault();
        return new PersistedLibraryState(1, saved, selected);
    }

    private static IReadOnlyList<WidgetAppLibraryItem> NormalizeResolved(
        IReadOnlyList<WidgetAppLibraryItem>? items,
        IReadOnlyList<string> requestedSavedIds)
    {
        var requested = requestedSavedIds.ToHashSet(StringComparer.Ordinal);
        var seenApp = new HashSet<string>(StringComparer.Ordinal);
        var seenSaved = new HashSet<string>(StringComparer.Ordinal);
        var bySaved = (items ?? [])
            .Where(item => item is not null && IsOpaqueId(item.AppId) &&
                IsOpaqueId(item.SavedId) && requested.Contains(item.SavedId) &&
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

    private async Task PersistLibraryAsync(
        IReadOnlyList<string> savedIds,
        string? selectedSavedId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizePersistedState(
            new PersistedLibraryState(1, savedIds, selectedSavedId));
        long revision;
        lock (_gate) revision = _stateRevision;
        try
        {
            var mutation = await HostServices.PrivateState.WriteAsync(
                normalized, revision, cancellationToken: cancellationToken).ConfigureAwait(false);
            lock (_gate) _stateRevision = mutation.Revision;
        }
        catch (WidgetCapabilityException exception) when (exception.ErrorCode == "state_conflict")
        {
            var latest = await HostServices.PrivateState.ReadJsonAsync(cancellationToken)
                .ConfigureAwait(false);
            var mutation = await HostServices.PrivateState.WriteAsync(
                normalized, latest.Revision, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _stateRevision = mutation.Revision;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lock (_gate) _status = "Library changed for this session · saving failed";
            Invalidate();
        }
    }

    private static bool IsOpaqueId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');

    private void ApplyError(Exception exception, long generation)
    {
        var (state, status) = ErrorState(exception);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            if (_hasLibrarySnapshot)
            {
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
        CancellationTokenSource? previous;
        var generation = Interlocked.Increment(ref _toastGeneration);
        lock (_gate)
        {
            previous = _toastLifetime;
            _toastLifetime = lifetime;
            _toast = new ToastNotice(title, message, tone, duration);
        }
        previous?.Cancel();
        previous?.Dispose();
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
            lifetime.Dispose();
        }
    }

    private void ClearToast(bool invalidate)
    {
        Interlocked.Increment(ref _toastGeneration);
        CancellationTokenSource? lifetime;
        bool changed;
        lock (_gate)
        {
            lifetime = _toastLifetime;
            _toastLifetime = null;
            changed = _toast is not null;
            _toast = null;
        }
        lifetime?.Cancel();
        lifetime?.Dispose();
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
