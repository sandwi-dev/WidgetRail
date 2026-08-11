using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

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
    private static readonly WidgetAppLibraryQuery InstalledGames = new(
        InstalledOnly: true,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly WidgetCursorResource<GameLauncherItem> _library;
    private IReadOnlyList<GameLauncherDisplayItem> _warmItems = [];
    private string _status = "Game Launcher loads when visible";
    private string? _launchingSavedId;

    public GameLauncherWidget()
    {
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
        get { lock (_gate) return _warmItems.ToArray(); }
    }
    internal Task WhenLibraryIdleAsync(CancellationToken cancellationToken = default) =>
        _library.WhenIdleAsync(cancellationToken);
    internal Task WhenWarmStateIdleAsync(CancellationToken cancellationToken = default) =>
        Operations.WhenIdleAsync("game-launcher.warm-state", cancellationToken);

    public override WidgetView Render()
    {
        GameLauncherPresentationState state;
        lock (_gate)
            state = new(
                _library.Snapshot,
                _warmItems.ToArray(),
                StatusLocked(_library.Snapshot),
                _launchingSavedId,
                LifecycleState == WidgetLifecycleState.Interactive);
        return GameLauncherPresentation.Render(state);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _ = _library.EnsureLoaded();
        _ = Operations.RunLatest("game-launcher.warm-state",
            context => LoadWarmStateAsync(context.CancellationToken),
            WidgetOperationLifetime.Active);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        _library.Reset(invalidate: false);
        lock (_gate)
        {
            _launchingSavedId = null;
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
        if (_library.TryHandlePagination(action, out _)) return;
        switch (action.ActionId)
        {
            case "game-launcher.previous":
                _ = _library.Move(WidgetCursorDirection.Before, GameLauncherPresentation.ScrollId);
                return;
            case "game-launcher.next":
                _ = _library.Move(WidgetCursorDirection.After, GameLauncherPresentation.ScrollId);
                return;
            case "game-launcher.refresh":
                _ = _library.Refresh();
                return;
            case "game-launcher.retry":
                _ = _library.Retry();
                return;
            case "game-launcher.launch":
                await LaunchAsync(action.SourceElementId, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async ValueTask LoadWarmStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await HostServices.PrivateState.ReadAsync<GameLauncherPrivateState>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var normalized = GameLauncherPrivateState.Normalize(
                stored.Exists ? stored.Value : null);
            lock (_gate)
            {
                _warmItems = normalized.Items;
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
            lock (_gate) _warmItems = [];
        }
    }

    private async ValueTask<WidgetCursorPage<GameLauncherItem>> LoadPageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        CancellationToken cancellationToken)
    {
        var requestCursor = direction is null ? null : cursor;
        var page = await HostServices.AppLibrary.QueryAsync(
                InstalledGames,
                requestCursor,
                direction,
                limit,
                refresh: direction is null,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var items = page.Items.Select(GameLauncherItem.From).ToArray();
        var display = GameLauncherPrivateState.FromPage(items);
        await HostServices.PrivateState.WriteAsync(
                display, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _warmItems = display.Items;
            _status = items.Length == 0 ? "No installed games" :
                $"{items.Length}{(page.After is null ? string.Empty : "+")} games in the current window";
        }
        return new(items,
            page.Before is null ? null : new WidgetCollectionCursor(page.Before),
            page.After is null ? null : new WidgetCollectionCursor(page.After));
    }

    private async Task LaunchAsync(string sourceElementId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        if (!await _launchGate.WaitAsync(0, lifetime.Token).ConfigureAwait(false)) return;
        GameLauncherItem? selected = null;
        try
        {
            selected = _library.Snapshot.Items.FirstOrDefault(item => string.Equals(
                GameLauncherIdentity.FocusId("grid", item.Key), sourceElementId,
                StringComparison.Ordinal));
            if (selected is null) return;
            _library.SelectAnchor(selected.Key, invalidate: false);
            lock (_gate)
            {
                _launchingSavedId = selected.Value.SavedId;
                _status = $"Opening {selected.Value.DisplayName}…";
            }
            Invalidate();
            var resolved = await HostServices.AppLibrary.ResolveSavedAsync(
                    [selected.Value.SavedId], lifetime.Token).ConfigureAwait(false);
            var current = resolved.SingleOrDefault(item => string.Equals(
                item.SavedId, selected.Value.SavedId, StringComparison.Ordinal));
            var stillCurrent = _library.Snapshot.Items.Any(item => item.Key == selected.Key);
            if (current is null || !stillCurrent)
                throw new WidgetCapabilityException(
                    "app_not_found", "The selected game is no longer available.");
            await HostServices.AppLibrary.LaunchAsync(
                    current.AppId,
                    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
                    lifetime.Token).ConfigureAwait(false);
            lock (_gate) _status = $"Opened {selected.Value.DisplayName}";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception exception)
        {
            lock (_gate) _status = LaunchError(exception);
        }
        finally
        {
            lock (_gate) _launchingSavedId = null;
            _launchGate.Release();
            Invalidate();
        }
    }

    private string StatusLocked(WidgetCursorResourceSnapshot<GameLauncherItem> snapshot) =>
        snapshot.Status switch
        {
            WidgetPagedResourceStatus.Loading when _warmItems.Count == 0 =>
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
