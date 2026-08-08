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

/// <summary>
/// Controller-first installed application library. Widget code receives only
/// bounded names, conservative kinds, and opaque host IDs. Listing and launch
/// remain separate consent-gated broker operations.
/// </summary>
public sealed class GamesAppsWidget : Widget
{
    public const int PageSize = 32;
    public const int MaximumItems = 512;

    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 820,
        PreferredHeight = 430,
        MinimumWidth = 420,
        MinimumHeight = 320,
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private IReadOnlyList<WidgetAppLibraryItem> _items = [];
    private IReadOnlyDictionary<string, string> _appByElementId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private GamesAppsViewState _viewState = GamesAppsViewState.Initial;
    private string _status = "Installed apps load when this widget becomes visible";
    private string? _selectedAppId;
    private string? _launchingAppId;
    private bool _loadingMore;
    private int? _nextOffset;
    private CancellationTokenSource? _activeRun;
    private long _generation;

    public GamesAppsViewState ViewState { get { lock (_gate) return _viewState; } }
    public IReadOnlyList<WidgetAppLibraryItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }
    public string? SelectedAppId { get { lock (_gate) return _selectedAppId; } }
    public int? NextOffset { get { lock (_gate) return _nextOffset; } }

    public override WidgetView Render()
    {
        IReadOnlyList<WidgetAppLibraryItem> items;
        GamesAppsViewState state;
        string status;
        string? selectedAppId;
        string? launchingAppId;
        bool loadingMore;
        int? nextOffset;
        lock (_gate)
        {
            items = _items;
            state = _viewState;
            status = _status;
            selectedAppId = _selectedAppId;
            launchingAppId = _launchingAppId;
            loadingMore = _loadingMore;
            nextOffset = _nextOffset;
        }

        var header = UI.Stack("games.header",
                UI.Text("LIBRARY", "games.eyebrow", "Installed application library")
                    .Classes("games-eyebrow"),
                UI.Text("Games & Apps", "games.title", "Games and Apps")
                    .Classes("games-title"),
                UI.Text(status, "games.status", status).Classes(
                    "games-status",
                    state == GamesAppsViewState.Ready ? "is-ready" :
                    state is GamesAppsViewState.PermissionDenied or GamesAppsViewState.Error
                        ? "is-error" : "is-neutral"))
            .Classes("games-header");

        if (state != GamesAppsViewState.Ready || items.Count == 0)
            return RenderState(header, state);

        var elementIds = items.Select(item => ElementId(item.AppId)).ToArray();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var cards = new List<WidgetElement>(items.Count + (nextOffset is null ? 0 : 1));
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = elementIds[index];
            map[id] = item.AppId;
            var kind = item.Kind == WidgetAppLibraryKind.Game ? "GAME" : "APPLICATION";
            var right = index + 1 < items.Count
                ? elementIds[index + 1]
                : nextOffset is not null ? "games.load-more" : id;
            var button = UI.Button(item.DisplayName, "games.launch", id)
                .Icon(WidgetGlyph.Play, $"Launch {item.DisplayName}")
                .Busy(string.Equals(launchingAppId, item.AppId, StringComparison.Ordinal))
                .Disabled(launchingAppId is not null || loadingMore ||
                    LifecycleState != WidgetLifecycleState.Interactive)
                .Selected(string.Equals(selectedAppId, item.AppId, StringComparison.Ordinal))
                .FocusLeft(index == 0 ? id : elementIds[index - 1])
                .FocusRight(right)
                .FocusUp(id)
                .Classes("games-card-action");
            cards.Add(UI.Stack(id + ".card",
                    button,
                    UI.Row(id + ".meta",
                            UI.Text(kind, id + ".kind", kind).Classes("games-card-kind"),
                            UI.Text("A  LAUNCH", id + ".hint", $"Press A to launch {item.DisplayName}")
                                .Classes("games-card-hint"))
                        .Classes("games-card-meta"))
                .Classes("games-card"));
        }

        if (nextOffset is not null)
        {
            cards.Add(UI.Stack("games.load-more.card",
                    UI.Button("Load more", "games.load-more", "games.load-more")
                        .Icon(WidgetGlyph.Refresh, "Load more installed applications")
                        .Busy(loadingMore)
                        .Disabled(launchingAppId is not null || loadingMore ||
                            LifecycleState != WidgetLifecycleState.Interactive)
                        .FocusLeft(items.Count == 0 ? "games.load-more" : elementIds[^1])
                        .FocusRight("games.load-more")
                        .FocusUp("games.load-more")
                        .Classes("games-card-action", "games-load-more"),
                    UI.Text($"{items.Count} loaded", "games.load-more.count",
                            $"{items.Count} applications loaded")
                        .Classes("games-card-kind"))
                .Classes("games-card", "is-load-more"));
        }
        lock (_gate) _appByElementId = map;

        var selected = items.FirstOrDefault(item =>
            string.Equals(item.AppId, selectedAppId, StringComparison.Ordinal)) ?? items[0];
        var root = UI.Stack("games.root",
                header,
                UI.Row("games.section.heading",
                        UI.Text("INSTALLED", "games.section.label", "Installed applications")
                            .Classes("games-section-label"),
                        UI.Text($"{items.Count}{(nextOffset is null ? string.Empty : "+")} apps",
                                "games.section.count", $"{items.Count} installed applications loaded")
                            .Classes("games-section-count"))
                    .Classes("games-section-heading"),
                UI.HorizontalScroll("games.library.scroll", cards.ToArray())
                    .Classes("games-library-scroll"))
            .InputScope("games-apps")
            .Classes("games-apps-widget");
        return new WidgetView(root, ElementId(selected.AppId), Surface: StandardSurface);
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
            case "retry":
                if (IsActive) StartActiveRun(ActiveCancellationToken);
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

    private WidgetView RenderState(StackElement header, GamesAppsViewState state)
    {
        var (title, help) = state switch
        {
            GamesAppsViewState.Initial or GamesAppsViewState.Loading =>
                ("Loading installed apps", "The host is reading your bounded Start Menu catalog."),
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
        var retry = UI.Button("Try again", "retry", "games.retry")
            .Icon(WidgetGlyph.Refresh, "Reload installed applications")
            .Disabled(!IsActive)
            .Classes("games-retry");
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.state",
                        UI.Icon(WidgetGlyph.Play, "games.state.icon", "Application library")
                            .Classes("games-state-icon"),
                        UI.Text(title, "games.state.title", title).Classes("games-state-title"),
                        UI.Text(help, "games.state.help", help).Classes("games-state-help"),
                        retry)
                    .Classes("games-state-card"))
            .InputScope("games-apps")
            .Classes("games-apps-widget", "has-state");
        return new WidgetView(root, "games.retry", Surface: StandardSurface);
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        StopActiveRun();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
        _activeRun = lifetime;
        var generation = Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _viewState = GamesAppsViewState.Loading;
            _status = "Loading installed applications…";
            _launchingAppId = null;
            _loadingMore = false;
        }
        Invalidate();
        _ = LoadFirstPageAsync(generation, lifetime.Token);
    }

    private void StopActiveRun()
    {
        Interlocked.Increment(ref _generation);
        var lifetime = Interlocked.Exchange(ref _activeRun, null);
        lifetime?.Cancel();
        lifetime?.Dispose();
        lock (_gate)
        {
            _launchingAppId = null;
            _loadingMore = false;
        }
    }

    private async Task LoadFirstPageAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            var page = await HostServices.AppLibrary.GetPageAsync(0, PageSize, cancellationToken)
                .ConfigureAwait(false);
            ApplyPage(page, append: false, generation, requestedOffset: 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplyError(exception, generation);
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
                if (offset is null || LifecycleState != WidgetLifecycleState.Interactive)
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
            ApplyCommandError(exception, "More apps could not be loaded");
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
            await HostServices.AppLibrary.LaunchAsync(appId, commandLifetime.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _launchingAppId = null;
                _status = $"Opened {selected.DisplayName}";
            }
        }
        catch (OperationCanceledException) when (commandLifetime.IsCancellationRequested)
        {
            lock (_gate) _launchingAppId = null;
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            lock (_gate) _launchingAppId = null;
            ApplyCommandError(exception, selected is null
                ? "The selected app could not be opened"
                : $"{selected.DisplayName} could not be opened");
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
                _selectedAppId = _items.FirstOrDefault()?.AppId;
            _viewState = _items.Count == 0 ? GamesAppsViewState.Empty : GamesAppsViewState.Ready;
            _status = _items.Count == 0
                ? "No launchable Start Menu apps found"
                : $"{_items.Count}{(_nextOffset is null ? string.Empty : "+")} installed apps · paths stay private";
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

    private void ApplyError(Exception exception, long generation)
    {
        var (state, status) = ErrorState(exception);
        lock (_gate)
        {
            if (Interlocked.Read(ref _generation) != generation) return;
            _viewState = state;
            _status = status;
            _items = [];
            _nextOffset = null;
            _launchingAppId = null;
        }
        Invalidate();
    }

    private void ApplyCommandError(Exception exception, string fallback)
    {
        var (_, status) = ErrorState(exception);
        lock (_gate) _status = status == "App library request failed" ? fallback : status;
        Invalidate();
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
}
