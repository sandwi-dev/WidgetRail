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
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly WidgetCursorResource<GameLauncherItem> _library;
    private GameLauncherPrivateState _organization = GameLauncherPrivateState.Empty;
    private long _stateRevision;
    private string? _variantSeedSavedId;
    private bool _organizationBusy;
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
        get { lock (_gate) return _organization.Items.ToArray(); }
    }
    internal GameLauncherPrivateState Organization
    {
        get { lock (_gate) return _organization; }
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
                _organization,
                StatusLocked(_library.Snapshot),
                _launchingSavedId,
                _organizationBusy,
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
            _variantSeedSavedId = null;
            _organizationBusy = false;
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
            case "game-launcher.favorite":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await ToggleFavoriteAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.variant":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await ToggleVariantAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.prefer":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await PreferVariantAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "game-launcher.organization.reset":
                if (LifecycleState != WidgetLifecycleState.Interactive) return;
                await MutateOrganizationAsync(GameLauncherOrganizationPolicy.Clear,
                    "Organization cleared", cancellationToken).ConfigureAwait(false);
                return;
        }
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
        var projectionSaved = await PersistProjectionAsync(items, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate) _status = !projectionSaved
            ? "Games loaded · organization was not saved"
            : items.Length == 0 ? "No installed games" :
                $"{items.Length}{(page.After is null ? string.Empty : "+")} games in the current window";
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

    private async Task ToggleFavoriteAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var display = DisplayForSource(sourceElementId);
        if (display is null) return;
        bool favorite;
        lock (_gate) favorite = !_organization.FavoriteSavedIds.Contains(
            display.SavedId, StringComparer.Ordinal);
        await MutateOrganizationAsync(
            state => GameLauncherOrganizationPolicy.SetFavorite(state, display, favorite),
            favorite ? $"Favorited {display.DisplayName}" : $"Removed {display.DisplayName} from favorites",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ToggleVariantAsync(
        string sourceElementId,
        CancellationToken cancellationToken)
    {
        var current = DisplayForSource(sourceElementId);
        if (current is null) return;
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
                first = DisplayForSavedLocked(_variantSeedSavedId!);
                existing = first is null ? null : _organization.VariantGroups.FirstOrDefault(group =>
                    group.SavedIds.Contains(first.SavedId, StringComparer.Ordinal) &&
                    group.SavedIds.Contains(current.SavedId, StringComparer.Ordinal));
                _variantSeedSavedId = null;
            }
        }
        if (started) { Invalidate(); return; }
        if (first is null || first.SavedId == current.SavedId)
        {
            lock (_gate) _status = "Choose two distinct variants";
            Invalidate();
            return;
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
        var item = _library.Snapshot.Items.FirstOrDefault(candidate => string.Equals(
            GameLauncherIdentity.FocusId("grid", candidate.Key), sourceElementId,
            StringComparison.Ordinal));
        return item is null ? null : new(
            item.Value.SavedId, item.Value.DisplayName, item.Value.SourceAttribution);
    }

    private GameLauncherDisplayItem? DisplayForSavedLocked(string savedId) =>
        _organization.Items.FirstOrDefault(item => item.SavedId == savedId) ??
        _library.Snapshot.Items.Where(item => item.Value.SavedId == savedId)
            .Select(item => new GameLauncherDisplayItem(
                item.Value.SavedId, item.Value.DisplayName, item.Value.SourceAttribution))
            .FirstOrDefault();

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
