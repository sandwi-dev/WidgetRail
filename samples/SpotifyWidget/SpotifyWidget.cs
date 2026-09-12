using WidgetRail.WidgetProtocol;
using System.Diagnostics;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

public enum SpotifyWidgetViewState
{
    Initial,
    Loading,
    Unconfigured,
    Disconnected,
    Authorizing,
    Ready,
    PermissionDenied,
    ServiceUnavailable,
    Error,
}

public enum SpotifyDestination
{
    Player,
    Queue,
    Playlists,
    Devices,
    Search,
}

/// <summary>
/// Controller-first Spotify community widget. All Spotify access crosses the
/// public typed SDK; the widget never receives OAuth tokens or a client secret.
/// </summary>
public sealed class SpotifyWidget : Widget
{
    private static readonly TimeSpan PlayingPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PausedPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ErrorPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetupRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueueLoadTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DevicesCacheLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OptimisticReconciliationLifetime = TimeSpan.FromSeconds(12);
    private static readonly IReadOnlyList<SpotifyAuthorizationScope> SpotifyScopes =
        SpotifyApplicationContract.RequiredAuthorizationScopes;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ISpotifyApplicationService _spotify;
    private readonly TimeProvider _timeProvider;
    private readonly ISpotifyRuntimeDiagnostics _runtimeDiagnostics;
    private readonly PinnedLayoutHandle _compactPinnedLayout;
    private readonly PinnedLayoutHandle _upNextPinnedLayout;
    private readonly WidgetNavigator<SpotifyRoute> _navigation;
    private SpotifyWidgetViewState _viewState = SpotifyWidgetViewState.Initial;
    private SpotifyAuthorizationState _authorizationState =
        SpotifyAuthorizationState.Disconnected;
    private SpotifyPlaybackSummary? _playback;
    private readonly WidgetCursorResource<SpotifyMediaCollectionItem> _queue;
    private readonly WidgetCursorResource<SpotifyPlaylistCollectionItem> _playlists;
    private readonly WidgetCursorResource<SpotifyMediaCollectionItem> _playlistItems;
    private readonly SpotifyMediaOccurrencePolicy _queueOccurrences =
        new(SpotifyApplicationContract.MaximumQueueItems * 2);
    private readonly SpotifyMediaOccurrencePolicy _playlistOccurrences =
        new(SpotifyCollectionPolicy.MaximumRetainedItems);
    private SpotifyDevicesSummary? _devices;
    private SpotifyLocalPlaybackSummary? _localPlayback;
    private string? _preferredPlaybackDeviceId;
    private DateTimeOffset? _devicesCachedAt;
    private bool _pageLoading;
    private bool _localPlaybackBusy;
    private long _localPlaybackOperationGeneration;
    private long _localPlaybackSummaryRevision;
    private SpotifyLocalPlaybackSummary? _localPlaybackOperationBaseline;
    private string? _localPlaybackFeedback;
    private string? _pageError;
    private SpotifyPlaylistSelection? _playlistSelection;
    private SpotifySelectedPlaylistPageSource? _playlistPageSource;
    private long? _playlistItemsSelectionGeneration;
    private long _playlistSelectionGeneration;
    private long _presentationCaptureSequence;
    private long _queueLoadOperationSequence;
    private SpotifyPlaybackOperation? _pendingOperation;
    private long _pendingOperationSequence;
    private long _playbackOperationSequence;
    private long _playbackObservationSequence;
    private SpotifyOptimisticPlaybackReconciliation? _optimisticReconciliation;
    private string _status = "Spotify loads when this widget becomes visible";
    private SpotifyRefreshWarning? _refreshWarning;
    private int _consecutiveRefreshFailures;
    private long _setupViewGeneration;
    private bool _setupBusy;
    private long _activeGeneration;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _pollWake = new(0, 1);
    private int _playbackSettlementReads;
    private string? _expectedPlaybackUri;
    private ToastElement? _actionToast;
    private readonly WidgetTimedMutation _toastExpiry;
    private Task? _pollTask;
    private Task? _progressTask;
    private readonly object _authorizationGate = new();
    private Task? _authorizationTask;

    public SpotifyWidget(
        ISpotifyApplicationService spotify,
        TimeProvider? timeProvider = null)
        : this(spotify, timeProvider, SpotifyRuntimeDiagnostics.None)
    {
    }

    internal SpotifyWidget(
        ISpotifyApplicationService spotify,
        TimeProvider? timeProvider,
        ISpotifyRuntimeDiagnostics runtimeDiagnostics,
        int collectionPageSize = SpotifyCollectionPolicy.PageSize,
        int collectionRetainedTarget = SpotifyCollectionPolicy.RetainedItemTarget)
    {
        _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runtimeDiagnostics = runtimeDiagnostics ??
            throw new ArgumentNullException(nameof(runtimeDiagnostics));
        _navigation = CreateNavigatorWithOptions(
            "spotify.navigation",
            SpotifyRoute.Search,
            new WidgetNavigatorOptions<SpotifyRoute>
            {
                SharedRootScopeId = SpotifyPresentation.InputScope,
                RootRoutes =
                [
                    SpotifyRoute.Search,
                    SpotifyRoute.Queue,
                    SpotifyRoute.Playlists,
                    SpotifyRoute.Devices,
                ],
                RouteScopeIds = new Dictionary<SpotifyRoute, string>
                {
                    [SpotifyRoute.PlaylistDetail] = "spotify.playlist.detail",
                    [SpotifyRoute.Setup] = "spotify.setup",
                },
            });
        _toastExpiry = CreateTimedMutation(WidgetOperationLifetime.Active, _timeProvider);
        _search = CreateSearchResource();
        _compactPinnedLayout = CreatePinnedLayoutHandle(
            SpotifyPresentation.CompactPinnedLayoutId,
            SpotifyPresentation.CompactPinnedLayoutName,
            SpotifyPresentation.CompactPinnedSurface,
            activeInputScopeId: SpotifyPresentation.CompactPinnedScope);
        _upNextPinnedLayout = CreatePinnedLayoutHandle(
            SpotifyPresentation.UpNextPinnedLayoutId,
            SpotifyPresentation.UpNextPinnedLayoutName,
            SpotifyPresentation.UpNextPinnedSurface,
            activeInputScopeId: SpotifyPresentation.UpNextPinnedScope);
        _queue = CreateCursorResource<SpotifyMediaCollectionItem>("spotify.queue", new()
        {
            PageSize = SpotifyApplicationContract.MaximumQueueItems,
            MaximumRetainedItems = SpotifyApplicationContract.MaximumQueueItems * 2,
            LoadPage = LoadQueuePageAsync,
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.queue.scroll", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", "shared", item.Key),
                    "spotify.queue.empty.shared"),
            ],
        });
        _playlists = CreateCursorResource<SpotifyPlaylistCollectionItem>(
            "spotify.playlists", new()
        {
            PageSize = collectionPageSize,
            MaximumRetainedItems = WidgetCursorResource<SpotifyMediaCollectionItem>.MaximumRetainedItems,
            RetainedItemTarget = collectionRetainedTarget,
            PaginationThreshold = SpotifyCollectionPolicy.PaginationThreshold,
            LoadPage = async (cursor, _, limit, token) =>
            {
                var offset = SpotifyCollectionIdentity.Offset(cursor);
                var page = await _spotify.GetPlaylistsAsync(offset, limit, token)
                    .ConfigureAwait(false);
                var items = page.Items.Select(item => new SpotifyPlaylistCollectionItem(
                    item, SpotifyCollectionIdentity.Playlist(item.PlaylistId))).ToArray();
                return SpotifyCollectionIdentity.Page(
                    items, page.Offset, page.Limit, page.Total,
                    page.HasAuthoritativeWindow);
            },
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.playlists.scroll", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.item", "shared", item.Key),
                    "spotify.page.sparse.playlist.shared")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
            ],
        });
        _playlistItems = CreateCursorResource<SpotifyMediaCollectionItem>(
            "spotify.playlist.items", new()
        {
            PageSize = collectionPageSize,
            MaximumRetainedItems = WidgetCursorResource<SpotifyMediaCollectionItem>.MaximumRetainedItems,
            RetainedItemTarget = collectionRetainedTarget,
            PaginationThreshold = SpotifyCollectionPolicy.PaginationThreshold,
            LoadPage = LoadSelectedPlaylistCursorPageAsync,
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.playlist.detail.scroll", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.track", "shared", item.Key),
                    "spotify.playlist.play.shared")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
            ],
        });
    }

    public SpotifyWidgetViewState ViewState { get { lock (_gate) return _viewState; } }
    public string Status { get { lock (_gate) return _status; } }
    public SpotifyPlaybackSummary? Playback { get { lock (_gate) return _playback; } }
    public SpotifyDestination Destination =>
        SpotifyRouteActionPolicy.Destination(_navigation.Value.RootRoute);

    public static string FormatTime(long milliseconds) =>
        SpotifyPresentation.FormatTime(milliseconds);

    public override WidgetView Render()
    {
        try
        {
            var presentation = CapturePresentationState();
            var view = SpotifyPresentation.Render(
                presentation, _compactPinnedLayout, _upNextPinnedLayout);
            if (presentation.Navigation.Route != SpotifyRoute.Setup &&
                presentation.ViewState != SpotifyWidgetViewState.Ready)
                return view;
            var root = _navigation.Scope(presentation.Navigation, view.Root);
            var requestedFocus = presentation.Navigation.InitialFocusId ??
                view.InitialFocusId;
            return view with
            {
                Root = root,
                InitialFocusId = ResolveCurrentFocus(
                    root, presentation.Navigation.InputScopeId,
                    requestedFocus, view.InitialFocusId),
                ActiveInputScopeId = presentation.Navigation.InputScopeId,
                FocusGroupEntryRequest = presentation.Navigation.FocusGroupEntryRequest,
            };
        }
        catch (Exception exception)
        {
            _runtimeDiagnostics.Record(
                "render-failed",
                SpotifyRuntimeDiagnostics.Code(exception));
            throw;
        }
    }

    private static string? ResolveCurrentFocus(
        ContainerElement root,
        string inputScopeId,
        string? requestedFocus,
        string? fallbackFocus)
    {
        if (WidgetFocusTargetLookup.Resolve(
                root, requestedFocus, inputScopeId).IsEnabled)
            return requestedFocus;
        if (WidgetFocusTargetLookup.Resolve(
                root, fallbackFocus, inputScopeId).IsEnabled)
            return fallbackFocus;
        return WidgetFocusTargetLookup.First(root, inputScopeId).Id;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        var generation = Interlocked.Increment(ref _activeGeneration);
        _pollTask = RunAdaptivePollingAsync(generation, activeLifetime);
        _progressTask = RunPeriodicUpdatesWhileActiveAsync(
            ProgressInterval,
            _ =>
            {
                lock (_gate)
                {
                    if (_viewState != SpotifyWidgetViewState.Ready ||
                        _playback?.IsPlaying != true) return ValueTask.CompletedTask;
                }
                Invalidate();
                return ValueTask.CompletedTask;
            },
            invalidateAfterTick: false);
        lock (_gate)
        {
            if (_playlistSelection is not null)
                _playlistItems.EnsureLoaded();
            else if (_navigation.Value.RootRoute == SpotifyRoute.Search && _searchQuery.Length > 0)
                _search.EnsureLoaded();
            else if (_navigation.Value.RootRoute == SpotifyRoute.Playlists)
                _playlists.EnsureLoaded();
            else if (_navigation.Value.RootRoute == SpotifyRoute.Queue ||
                     IsUpNextPinnedLayoutDemanded())
                _queue.EnsureLoaded();
        }
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Interlocked.Increment(ref _activeGeneration);
        _toastExpiry.Cancel();
        lock (_gate)
        {
            ClearOptimisticReconciliationLocked();
            _actionToast = null;
            _playbackSettlementReads = 0;
        }
        var tasks = new[] { _pollTask, _progressTask }
            .Where(task => task is not null).Cast<Task>().ToArray();
        _pollTask = null;
        _progressTask = null;
        if (tasks.Length != 0)
            await Task.WhenAll(tasks).WaitAsync(transitionToken).ConfigureAwait(false);
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        lock (_gate) ClearOptimisticReconciliationLocked();
        Task? authorization;
        lock (_authorizationGate) authorization = _authorizationTask;
        var tasks = new[] { authorization }
            .Where(task => task is not null).Cast<Task>().ToArray();
        try
        {
            if (tasks.Length != 0)
                await Task.WhenAll(tasks).WaitAsync(shutdownToken).ConfigureAwait(false);
            await _spotify.DisposeAsync().AsTask().WaitAsync(shutdownToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested) { }
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

    public override ValueTask OnPinnedLayoutSelectionChangedAsync(
        string? _selectedLayoutId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_upNextPinnedLayout.IsSelected) return ValueTask.CompletedTask;

        var selectionToken = _upNextPinnedLayout.SelectionCancellationToken;
        selectionToken.ThrowIfCancellationRequested();
        _ = selectionToken.Register(
            static state => ((SpotifyWidget)state!).ReleaseUpNextPinnedLayoutDemand(),
            this);

        bool load;
        lock (_gate)
        {
            load = IsActive && _viewState == SpotifyWidgetViewState.Ready;
        }
        if (load) _queue.EnsureLoaded();
        return ValueTask.CompletedTask;
    }

    private void ReleaseUpNextPinnedLayoutDemand()
    {
        bool reset;
        lock (_gate) reset = _navigation.Value.RootRoute != SpotifyRoute.Queue;
        if (reset) _queue.Reset(invalidate: false);
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var intent = SpotifyRouteActionPolicy.Classify(action);
        if (await TryHandleQueueAdditionAsync(action, cancellationToken).ConfigureAwait(false)) return;
        if (await TryHandleSearchActionAsync(action, cancellationToken).ConfigureAwait(false)) return;
        if (TryHandleNavigationBack(action)) return;
        if (TryHandlePageAction(action, intent)) return;

        switch (intent.Kind)
        {
            case SpotifyActionKind.SetupOpen:
                lock (_gate) _setupViewGeneration++;
                _navigation.PushFromAction(SpotifyRoute.Setup, action,
                    action.FocusedElementId ?? action.SourceElementId);
                break;
            case SpotifyActionKind.SetupClose:
                _navigation.Back(action.FocusedElementId ?? action.SourceElementId);
                break;
            case SpotifyActionKind.SetupDone:
                _navigation.Back(action.FocusedElementId ?? action.SourceElementId);
                if (IsActive)
                    await RunCommandOperationAsync(CheckConfigurationAsync, cancellationToken, serialize: false)
                        .ConfigureAwait(false);
                break;
            case SpotifyActionKind.SetupOpenDashboard:
                await RunSetupActionAsync(
                    _spotify.OpenDeveloperDashboardAsync,
                    "Opening Spotify developer dashboard…",
                    "Spotify developer dashboard opened",
                    cancellationToken).ConfigureAwait(false);
                break;
            case SpotifyActionKind.SetupCopyRedirect:
                await RunSetupActionAsync(
                    _spotify.CopyRedirectUriAsync,
                    "Copying redirect URI…",
                    "Redirect URI copied",
                    cancellationToken).ConfigureAwait(false);
                break;
            case SpotifyActionKind.SetupConfigureClient:
                if (action.CommittedText is { } clientId)
                    await ConfigureClientIdAsync(clientId, cancellationToken)
                        .ConfigureAwait(false);
                break;
            case SpotifyActionKind.Connect:
                StartAuthorization();
                break;
            case SpotifyActionKind.Disconnect:
                await RunCommandOperationAsync(DisconnectAsync, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.Refresh:
                await RunCommandOperationAsync(RefreshAsync, cancellationToken, serialize: false)
                    .ConfigureAwait(false);
                RefreshVisibleCollection(action.InputScopeId);
                break;
            case SpotifyActionKind.Playback:
                var playbackOperation = intent.PlaybackOperation is null
                    ? SpotifyPlaybackPolicy.ResolveToggle(Playback)
                    : intent.PlaybackOperation!.Value;
                await RunCommandOperationAsync(token => ExecuteAsync(
                    playbackOperation, null, action, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.Seek:
                await RunCommandOperationAsync(token => ExecuteAsync(
                    SpotifyPlaybackOperation.Seek,
                    intent.RequestedPositionMs, action, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.PreviousSection:
                NavigateSection(-1);
                break;
            case SpotifyActionKind.NextSection:
                NavigateSection(1);
                break;
            case SpotifyActionKind.PlaylistBack:
                BackFromPlaylist(action.SourceElementId);
                break;
            case SpotifyActionKind.Noop:
                break;
            case SpotifyActionKind.LocalStart:
                StartLocalPlaybackOperation(new(
                    SpotifyLocalPlaybackOperation.StartAndTransfer,
                    ContinuePlaying: true));
                break;
            case SpotifyActionKind.LocalStop:
                StartLocalPlaybackOperation(new(SpotifyLocalPlaybackOperation.Stop));
                break;
            case SpotifyActionKind.DeviceSelect:
                await RunCommandOperationAsync(
                    token => SelectDeviceAsync(intent.ItemIndex, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.QueuePlay:
                await RunCommandOperationAsync(
                    token => PlayQueueItemAsync(intent.ItemKey!.Value, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.PlaylistTrack:
                await RunCommandOperationAsync(
                    token => PlayPlaylistTrackAsync(intent.ItemKey!.Value, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.PlaylistPlay:
                await RunCommandOperationAsync(PlayPlaylistAsync, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private void RecordActionDiagnostic(
        WidgetActionEvent action,
        string boundary,
        string code) =>
        _runtimeDiagnostics.Record(boundary, code, Math.Max(0, action.Sequence),
            Math.Max(0, Volatile.Read(ref _activeGeneration)));

    private async Task CheckConfigurationAsync(CancellationToken cancellationToken)
    {
        using var refreshLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        refreshLifetime.CancelAfter(SetupRefreshTimeout);
        try
        {
            await RefreshAsync(refreshLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (refreshLifetime.IsCancellationRequested) { }
        if (refreshLifetime.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested &&
            !ActiveCancellationToken.IsCancellationRequested)
            SetState(Volatile.Read(ref _activeGeneration),
                SpotifyWidgetViewState.ServiceUnavailable,
                "Spotify configuration refresh timed out", null);
    }

    private async Task ConfigureClientIdAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var configured = await RunSetupActionAsync(
            async token =>
            {
                _ = await _spotify.ConfigureClientAsync(clientId, token)
                    .ConfigureAwait(false);
            },
            "Saving public Spotify Client ID…",
            "Spotify Client ID saved",
            cancellationToken).ConfigureAwait(false);
        if (!configured) return;
        if (_navigation.Value.Route == SpotifyRoute.Setup)
            _navigation.Back("spotify.setup.client-id");
        if (IsActive)
            await CheckConfigurationAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RunSetupActionAsync(
        Func<CancellationToken, ValueTask> action,
        string pendingStatus,
        string successStatus,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_setupBusy) return false;
            _setupBusy = true;
            _status = pendingStatus;
        }
        Invalidate();
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            lock (_gate) _status = successStatus;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate) _status = "Spotify setup canceled";
            return false;
        }
        catch (SpotifyApplicationException exception)
        {
            lock (_gate)
                _status = SpotifyPlaybackPolicy.SafeMessage(
                    exception, "Spotify setup could not be completed");
            return false;
        }
        catch (Exception)
        {
            lock (_gate) _status = "Spotify setup could not be completed";
            return false;
        }
        finally
        {
            lock (_gate) _setupBusy = false;
            Invalidate();
        }
    }

    private bool TryHandlePageAction(
        WidgetActionEvent action,
        SpotifyActionIntent intent)
    {
        var route = _navigation.Value;
        lock (_gate)
        {
            if (route.RootRoute == SpotifyRoute.Search && _search.TryHandlePagination(action, out _)) return true;
            if (route.RootRoute == SpotifyRoute.Playlists)
            {
                if (_playlistSelection is null &&
                    _playlists.TryHandlePagination(action, out _))
                    return true;
                if (_playlistSelection is not null &&
                    _playlistItems.TryHandlePagination(action, out _))
                    return true;
            }
        }
        switch (intent.Kind)
        {
            case SpotifyActionKind.Navigate when intent.Destination == SpotifyDestination.Search:
                Navigate(SpotifyRoute.Search);
                if (_searchQuery.Length > 0) _search.EnsureLoaded();
                return true;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Queue:
                Navigate(SpotifyRoute.Queue);
                _queue.EnsureLoaded();
                return true;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Playlists:
                Navigate(SpotifyRoute.Playlists);
                _playlists.EnsureLoaded();
                return true;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Devices:
                Navigate(SpotifyRoute.Devices);
                StartPageOperation(operation => LoadDestinationIfNeededAsync(
                    SpotifyRoute.Devices, operation));
                return true;
            case SpotifyActionKind.PageRetry:
                lock (_gate)
                {
                    if (route.RootRoute == SpotifyRoute.Search) { _search.Retry(); return true; }
                    if (_playlistSelection is not null)
                    {
                        _playlistItems.Retry();
                        return true;
                    }
                    if (route.RootRoute == SpotifyRoute.Playlists)
                    {
                        _playlists.Retry();
                        return true;
                    }
                    if (route.RootRoute == SpotifyRoute.Queue)
                    {
                        _queue.Retry();
                        return true;
                    }
                }
                StartPageOperation(ReloadCurrentPageAsync);
                return true;
        }
        if (intent.Kind != SpotifyActionKind.PlaylistOpen || intent.ItemKey is not { } playlistKey)
            return false;
        OpenPlaylist(playlistKey, action);
        return true;
    }

    private async Task RunCommandOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool serialize = true)
    {
        if (serialize && !await _commandGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            SetCommandStatus("Wait for the current Spotify action to finish");
            return;
        }
        try
        {
            if (serialize) lock (_gate) _playbackSettlementReads = 0;
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            SetCommandStatus("Spotify could not complete that action");
        }
        finally { if (serialize) _commandGate.Release(); }
    }

    private void StartPageOperation(
        Func<WidgetOperationContext, Task> operation,
        bool replaceRunning = true)
    {
        const string key = "spotify.page";
        if (!replaceRunning && Operations.IsBusy(key)) return;
        Operations.RunLatest(key,
            context => new ValueTask(operation(context)),
            WidgetOperationLifetime.Active);
    }

    private void CancelPageOperation() => Operations.Cancel("spotify.page");

    private void RefreshVisibleCollection(string? inputScopeId)
    {
        var route = _navigation.Value;
        bool detail;
        bool ready;
        lock (_gate)
        {
            detail = _playlistSelection is not null;
            ready = _viewState == SpotifyWidgetViewState.Ready;
        }
        if (!ready) return;
        if (string.Equals(inputScopeId, SpotifyPresentation.UpNextPinnedScope,
                StringComparison.Ordinal))
            _queue.Refresh();
        else if (route.RootRoute == SpotifyRoute.Queue)
            _queue.Refresh();
        else if (route.RootRoute == SpotifyRoute.Search && _searchQuery.Length > 0)
            _search.Refresh();
        else if (route.RootRoute == SpotifyRoute.Playlists)
        {
            if (detail) _playlistItems.Refresh();
            else _playlists.Refresh();
        }
    }

    private void InvalidateQueueCollection()
    {
        var route = _navigation.Value;
        if (route.RootRoute == SpotifyRoute.Queue || IsUpNextPinnedLayoutDemanded())
            _queue.Refresh();
        else _queue.Reset(invalidate: false);
    }

    private bool IsUpNextPinnedLayoutDemanded()
    {
        var selectionToken = _upNextPinnedLayout.SelectionCancellationToken;
        return _upNextPinnedLayout.IsSelected && !selectionToken.IsCancellationRequested;
    }

    private async ValueTask<WidgetCursorPage<SpotifyMediaCollectionItem>> LoadQueuePageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int _,
        CancellationToken cancellationToken)
    {
        if (cursor is not null || direction is not null)
            throw new InvalidOperationException("Spotify queue does not expose adjacent cursors.");
        var operation = Interlocked.Increment(ref _queueLoadOperationSequence);
        var generation = Math.Max(0, Volatile.Read(ref _activeGeneration));
        var started = Stopwatch.GetTimestamp();
        using var queueLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        queueLifetime.CancelAfter(QueueLoadTimeout);
        try
        {
            var queue = await _spotify.GetQueueAsync(queueLifetime.Token)
                .ConfigureAwait(false);
            var occurrenceRequest = _queueOccurrences.BeginPage("queue", 0, direction);
            var items = _queueOccurrences.NormalizePage(
                occurrenceRequest, queue.Items, []);
            return new(items, null, null);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested &&
                  queueLifetime.IsCancellationRequested)
        {
            _runtimeDiagnostics.Record(
                "queue-refresh", "deadline", operation, generation,
                ElapsedMilliseconds(started));
            throw new SpotifyApplicationException(
                "spotify_timeout", "Spotify queue did not respond in time.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _runtimeDiagnostics.Record(
                "queue-refresh", QueueFailureDiagnosticCode(exception),
                operation, generation, ElapsedMilliseconds(started));
            throw;
        }
    }

    private static long ElapsedMilliseconds(long started) =>
        Math.Max(0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    private static string QueueFailureDiagnosticCode(Exception exception)
    {
        var code = SpotifyRuntimeDiagnostics.Code(exception);
        return code.Length <= 56 ? "failure-" + code : "failure";
    }

    private void StartAuthorization()
    {
        lock (_authorizationGate)
        {
            if (_authorizationTask is { IsCompleted: false }) return;
            // Browser authorization intentionally belongs to the Created-to-Destroying
            // widget lifetime. The host acknowledges the input immediately, and taking
            // the browser foreground may move the overlay to Background without
            // canceling the already-authorized OAuth request.
            _authorizationTask = ConnectAsync(WidgetLifetimeToken);
        }
    }

    private async Task RunAdaptivePollingAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        var first = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   generation == Volatile.Read(ref _activeGeneration))
            {
                if (first)
                {
                    first = false;
                    await RefreshCoreAsync(generation, cancellationToken, loading: false)
                        .ConfigureAwait(false);
                }
                else
                {
                    await RefreshPlaybackAsync(generation, cancellationToken)
                        .ConfigureAwait(false);
                }

                lock (_gate)
                {
                    if (_playbackSettlementReads > 0)
                    {
                        if (_playback is { IsAvailable: true, IsPlaying: true } &&
                            (_expectedPlaybackUri is null || _playback.Item?.Uri == _expectedPlaybackUri))
                            _playbackSettlementReads = 0;
                        else _playbackSettlementReads--;
                    }
                }
                // A command wakes and reschedules this one poller. It does not
                // start another loop or leave the previous idle delay pending.
                while (await _pollWake.WaitAsync(PollInterval(), cancellationToken)
                           .ConfigureAwait(false)) { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private TimeSpan PollInterval()
    {
        lock (_gate)
        {
            if (_refreshWarning is { } warning) return warning.RetryDelay;
            if (_playbackSettlementReads > 0)
                return TimeSpan.FromSeconds(1 << (3 - _playbackSettlementReads));
            if (_viewState is SpotifyWidgetViewState.Error or
                SpotifyWidgetViewState.ServiceUnavailable or
                SpotifyWidgetViewState.PermissionDenied or
                SpotifyWidgetViewState.Disconnected or
                SpotifyWidgetViewState.Unconfigured)
                return ErrorPollInterval;
            return _playback?.IsPlaying == true ? PlayingPollInterval :
                _playback?.IsAvailable == true ? PausedPollInterval : IdlePollInterval;
        }
    }

    private async Task<bool> RefreshPlaybackAsync(
        long generation,
        CancellationToken cancellationToken,
        bool refreshDemandedQueueOnPlaybackChange = true)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _activeGeneration)) return false;
            bool refreshLocalPlayback;
            long localPlaybackRevision;
            lock (_gate)
            {
                if (_authorizationState != SpotifyAuthorizationState.Connected) return false;
                refreshLocalPlayback = _localPlayback is not null && !_localPlaybackBusy;
                localPlaybackRevision = _localPlaybackSummaryRevision;
            }
            try
            {
                var observationSequence = Interlocked.Increment(ref _playbackObservationSequence);
                var playback = await _spotify.GetPlaybackAsync(cancellationToken)
                    .ConfigureAwait(false);
                SpotifyLocalPlaybackSummary? localPlayback = null;
                if (refreshLocalPlayback)
                    localPlayback = await _spotify.GetLocalPlaybackAsync(cancellationToken)
                        .ConfigureAwait(false);
                if (generation != Volatile.Read(ref _activeGeneration)) return false;
                if (localPlayback is not null)
                    lock (_gate)
                    {
                        if (generation == Volatile.Read(ref _activeGeneration) &&
                            !_localPlaybackBusy &&
                            _localPlaybackSummaryRevision == localPlaybackRevision)
                        {
                            _localPlayback = localPlayback;
                            AdvanceLocalPlaybackSummaryRevisionLocked();
                        }
                    }
                SetState(generation, SpotifyWidgetViewState.Ready,
                    playback.IsAvailable ? "Live from Spotify" : "Connected · no active playback",
                    playback, refreshDemandedQueueOnPlaybackChange, observationSequence);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                ApplyRefreshFailure(generation, exception);
                return false;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void AdvanceLocalPlaybackSummaryRevisionLocked() =>
        _localPlaybackSummaryRevision = checked(_localPlaybackSummaryRevision + 1);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _activeGeneration);
        await RefreshCoreAsync(generation, cancellationToken, loading: true).ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(
        long generation,
        CancellationToken cancellationToken,
        bool loading)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _activeGeneration)) return;
            await RefreshCoreSingleAsync(generation, cancellationToken, loading)
                .ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreSingleAsync(
        long generation,
        CancellationToken cancellationToken,
        bool loading)
    {
        if (loading)
        {
            lock (_gate) _status = "Refreshing Spotify…";
            Invalidate();
        }
        try
        {
            var configuration = await _spotify.GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!configuration.IsConfigured)
            {
                ClearPageCaches();
                SetState(generation, SpotifyWidgetViewState.Unconfigured,
                    "Add your Spotify developer Client ID to continue", null);
                return;
            }

            var authorization = await _spotify.GetAuthorizationAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            switch (authorization.State)
            {
                case SpotifyAuthorizationState.Unconfigured:
                    SetState(generation, SpotifyWidgetViewState.Unconfigured,
                        "Add your Spotify developer Client ID to continue", null);
                    return;
                case SpotifyAuthorizationState.Disconnected:
                case SpotifyAuthorizationState.ReauthorizationRequired:
                    ClearPageCaches();
                    SetState(generation, SpotifyWidgetViewState.Disconnected,
                        authorization.DisplayMessage ?? (authorization.State ==
                            SpotifyAuthorizationState.ReauthorizationRequired
                                ? "Spotify needs you to reconnect"
                                : "Connect your Spotify account when you are ready"), null);
                    return;
                case SpotifyAuthorizationState.Authorizing:
                    SetState(generation, SpotifyWidgetViewState.Authorizing,
                        "Finish signing in through your browser", null);
                    return;
            }

            var observationSequence = Interlocked.Increment(ref _playbackObservationSequence);
            var playback = await _spotify.GetPlaybackAsync(cancellationToken)
                .ConfigureAwait(false);
            SetState(generation, SpotifyWidgetViewState.Ready,
                playback.IsAvailable ? "Live from Spotify" : "Connected · no active playback",
                playback, observationSequence: observationSequence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ApplyRefreshFailure(generation, exception);
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _viewState = SpotifyWidgetViewState.Authorizing;
            _status = "Opening Spotify sign-in…";
        }
        Invalidate();
        try
        {
            var authorization = await _spotify.ConnectAsync(
                SpotifyScopes, cancellationToken).ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            if (authorization.State != SpotifyAuthorizationState.Connected)
            {
                SetState(Volatile.Read(ref _activeGeneration),
                    SpotifyWidgetViewState.Disconnected,
                    authorization.DisplayMessage ?? "Spotify connection was not completed", null);
                return;
            }
            ClearPageCaches();
            if (IsActive)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                SetState(Volatile.Read(ref _activeGeneration),
                    SpotifyWidgetViewState.Ready,
                    "Connected · reopen the overlay for playback", null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (SpotifyApplicationException exception)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                exception.Code == "forbidden"
                    ? SpotifyWidgetViewState.PermissionDenied
                    : SpotifyWidgetViewState.Disconnected,
                SpotifyPlaybackPolicy.SafeMessage(
                    exception, "Spotify connection was not completed"), null);
        }
        catch (Exception)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                SpotifyWidgetViewState.Error, "Spotify connection failed", null);
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _spotify.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            ClearPageCaches();
            SetState(Volatile.Read(ref _activeGeneration), SpotifyWidgetViewState.Disconnected,
                "Disconnected from Spotify", null);
        }
        catch (SpotifyApplicationException exception)
        {
            SetCommandStatus(SpotifyPlaybackPolicy.SafeMessage(
                exception, "Spotify could not disconnect"));
        }
    }

    private SpotifyPresentationState CapturePresentationState()
    {
        lock (_gate)
        {
            var navigation = _navigation.Value;
            var playlists = SpotifyCursorPresentation<SpotifyPlaylistCollectionItem>.Capture(
                _playlists,
                "spotify.playlists",
                "spotify.playlists.scroll");
            SpotifyPlaylistDetailPresentation? detail = null;
            if (_playlistSelection is { } selection &&
                _playlistItemsSelectionGeneration == selection.Key.Generation)
                detail = new(selection,
                    SpotifyCursorPresentation<SpotifyMediaCollectionItem>.Capture(
                        _playlistItems,
                        "spotify.playlist.items",
                        "spotify.playlist.detail.scroll"));
            return new(
                new(++_presentationCaptureSequence, playlists.Snapshot.Revision,
                    detail?.Items.Snapshot.Revision ?? 0,
                    detail?.Selection.Key.Generation),
                _viewState,
                SpotifyPlaybackPolicy.Project(
                    _playback, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()),
                _pendingOperation,
                _status,
                _refreshWarning,
                _setupViewGeneration,
                _setupBusy,
                navigation,
                _queue.Snapshot,
                playlists,
                detail,
                _devices,
                _localPlayback,
                _localPlaybackBusy,
                _localPlaybackFeedback,
                _pageLoading,
                _pageError,
                new SpotifySearchPresentation(_searchQuery, _searchKind,
                    SpotifyCursorPresentation<SpotifySearchCollectionItem>.Capture(
                        _search, "spotify.search", "spotify.search.scroll")), _actionToast);
        }
    }

    private bool TryHandleNavigationBack(WidgetActionEvent action)
    {
        var navigation = _navigation.Value;
        if (navigation.Route == SpotifyRoute.PlaylistDetail &&
            action.Phase == ControllerEventPhase.Pressed &&
            string.Equals(action.ActionId, navigation.BackActionId, StringComparison.Ordinal) &&
            string.Equals(action.InputScopeId, navigation.InputScopeId,
                StringComparison.Ordinal))
        {
            CancelPageOperation();
            _playlists.ClearRequestedFocus(invalidate: false);
            lock (_gate)
            {
                ClearPlaylistSelectionLocked();
                _pageLoading = false;
                _pageError = null;
            }
        }
        return _navigation.TryHandleBack(action, action.FocusedElementId);
    }

    private void BackFromPlaylist(string sourceElementId)
    {
        if (_navigation.Value.Route != SpotifyRoute.PlaylistDetail) return;
        CancelPageOperation();
        _playlists.ClearRequestedFocus(invalidate: false);
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _pageLoading = false;
            _pageError = null;
        }
        _navigation.Back(sourceElementId);
    }

    private void Navigate(
        SpotifyRoute destination,
        string? sourceFocusId = null,
        string? focusGroupId = null)
    {
        if (destination is not (SpotifyRoute.Search or SpotifyRoute.Queue or SpotifyRoute.Playlists or
                SpotifyRoute.Devices)) return;
        CancelPageOperation();
        _playlists.ClearRequestedFocus(invalidate: false);
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _pageError = null;
            _pageLoading = false;
        }
        if (focusGroupId is not null)
            _navigation.NavigateRoot(destination, focusGroupId);
        else if (sourceFocusId is null)
            _navigation.NavigateRoot(destination);
        else
            _navigation.Navigate(destination, sourceFocusId);
    }

    private void NavigateSection(int offset)
    {
        var navigation = _navigation.Value;
        if (!SpotifyRouteActionPolicy.CanSwitchSection(
                navigation.Route, navigation.Depth)) return;
        SpotifyRoute[] sections =
        [
            SpotifyRoute.Search,
            SpotifyRoute.Queue,
            SpotifyRoute.Playlists,
            SpotifyRoute.Devices,
        ];
        var current = Array.IndexOf(sections, navigation.RootRoute);
        if (current < 0) return;
        var destination = sections[
            (current + offset + sections.Length) % sections.Length];
        Navigate(destination,
            focusGroupId: SpotifyRouteActionPolicy.FocusGroupId(destination));
        switch (_navigation.Value.RootRoute)
        {
            case SpotifyRoute.Search:
                if (_searchQuery.Length > 0) _search.EnsureLoaded();
                break;
            case SpotifyRoute.Queue:
                _queue.EnsureLoaded();
                break;
            case SpotifyRoute.Playlists:
                _playlists.EnsureLoaded();
                break;
            case SpotifyRoute.Devices:
                StartPageOperation(operation => LoadDestinationIfNeededAsync(
                    SpotifyRoute.Devices, operation));
                break;
        }
    }

    private async Task LoadDestinationIfNeededAsync(
        SpotifyRoute destination,
        WidgetOperationContext operation)
    {
        var shouldLoad = false;
        lock (_gate)
        {
            _pageError = null;
            shouldLoad = destination switch
            {
                SpotifyRoute.Devices => SpotifyRouteActionPolicy.DevicesNeedLoad(
                    _devices, _localPlayback, _devicesCachedAt,
                    _timeProvider.GetUtcNow(), DevicesCacheLifetime),
                _ => false,
            };
            _pageLoading = shouldLoad;
        }
        Invalidate();
        if (shouldLoad) await LoadDestinationAsync(destination, operation)
            .ConfigureAwait(false);
    }

    private Task ReloadCurrentPageAsync(WidgetOperationContext operation)
    {
        var destination = _navigation.Value.RootRoute;
        lock (_gate)
        {
            _pageLoading = true;
            _pageError = null;
        }
        Invalidate();
        return LoadDestinationAsync(destination, operation);
    }

    private async Task LoadDestinationAsync(
        SpotifyRoute destination,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            switch (destination)
            {
                case SpotifyRoute.Devices:
                    var devicesTask = _spotify.GetDevicesAsync(cancellationToken).AsTask();
                    var localTask = _spotify.GetLocalPlaybackAsync(cancellationToken).AsTask();
                    await Task.WhenAll(devicesTask, localTask).ConfigureAwait(false);
                    if (!operation.IsCurrent) return;
                    lock (_gate)
                    {
                        _devices = devicesTask.Result;
                        _localPlayback = localTask.Result;
                        AdvanceLocalPlaybackSummaryRevisionLocked();
                        if (!_localPlaybackBusy) _localPlaybackFeedback = null;
                        _preferredPlaybackDeviceId = devicesTask.Result.Devices
                            .FirstOrDefault(device => device.IsActive && !device.IsRestricted)
                            ?.DeviceId ?? _preferredPlaybackDeviceId;
                        _devicesCachedAt = _timeProvider.GetUtcNow();
                    }
                    break;
            }
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (SpotifyApplicationException exception)
        {
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = PageError(exception);
            }
        }
        catch (Exception)
        {
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = "Spotify could not load this page. Try again.";
            }
        }
        if (operation.IsCurrent) Invalidate();
    }

    private void OpenPlaylist(WidgetCollectionItemKey key, WidgetActionEvent action)
    {
        if (_navigation.Value.Route != SpotifyRoute.Playlists) return;
        lock (_gate)
        {
            var playlist = _playlists.Snapshot.Items
                .FirstOrDefault(item => item.Key == key)?.Value;
            if (playlist is null) return;
            _playlists.SelectAnchor(key, invalidate: false);
            _playlistItems.Reset(invalidate: false);
            _playlistOccurrences.Reset();
            var generation = checked(++_playlistSelectionGeneration);
            _pageError = null;
            _playlistSelection = new(
                new(playlist.PlaylistId, generation), playlist);
            _playlistPageSource = new(_spotify, _playlistSelection.Key);
            _playlistItemsSelectionGeneration = generation;
        }
        var result = _navigation.PushFromAction(
            SpotifyRoute.PlaylistDetail, action,
            action.FocusedElementId ?? action.SourceElementId);
        if (result == WidgetNavigationResult.Changed)
        {
            _playlistItems.EnsureLoaded();
            return;
        }
        lock (_gate) ClearPlaylistSelectionLocked();
    }

    private async ValueTask<WidgetCursorPage<SpotifyMediaCollectionItem>>
        LoadSelectedPlaylistCursorPageAsync(
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            CancellationToken cancellationToken)
    {
        var offset = SpotifyCollectionIdentity.Offset(cursor);
        SpotifyPlaylistSelection? selection;
        SpotifySelectedPlaylistPageSource? pageSource;
        lock (_gate)
        {
            selection = _playlistSelection;
            pageSource = _playlistPageSource;
        }
        if (selection is null || pageSource is null)
            throw new InvalidOperationException("No Spotify playlist is selected.");
        var occurrenceRequest = _playlistOccurrences.BeginPage(
            selection.Key.PlaylistId, offset, direction);
        var page = await pageSource.LoadAsync(offset, limit, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_playlistSelection?.Key != selection.Key ||
                !ReferenceEquals(_playlistPageSource, pageSource))
                throw new OperationCanceledException(cancellationToken);
            _playlistSelection = selection with { Playlist = page.Playlist };
        }
        var items = _playlistOccurrences.NormalizePage(
            occurrenceRequest,
            page.Items.Items,
            _playlistItems.Snapshot.Items.Select(item => item.Key).ToArray());
        return SpotifyCollectionIdentity.Page(
            items, page.Items.Offset, page.Items.Limit, page.Items.Total,
            page.Items.HasAuthoritativeWindow);
    }

    private void ClearPageCaches()
    {
        lock (_gate) ClearPageCachesLocked();
    }

    private void ClearPageCachesLocked()
    {
        _search.Reset(invalidate: false);
        _searchQuery = string.Empty;
        ++_searchGeneration;
        _playlists.Reset(invalidate: false);
        ClearPlaylistSelectionLocked();
        _queue.Reset(invalidate: false);
        _queueOccurrences.Reset();
        _devices = null;
        _localPlayback = null;
        AdvanceLocalPlaybackSummaryRevisionLocked();
        _devicesCachedAt = null;
        _preferredPlaybackDeviceId = null;
        _pageLoading = false;
        _localPlaybackBusy = false;
        _localPlaybackOperationGeneration = 0;
        _localPlaybackOperationBaseline = null;
        _localPlaybackFeedback = null;
        _pageError = null;
    }

    private void ClearPlaylistSelectionLocked()
    {
        _playlistItems.Reset(invalidate: false);
        _playlistOccurrences.Reset();
        _playlistSelection = null;
        _playlistPageSource = null;
        _playlistItemsSelectionGeneration = null;
    }


    private async Task SelectDeviceAsync(int index, CancellationToken cancellationToken)
    {
        SpotifyDeviceSummary? device;
        lock (_gate) device = ItemAt(_devices?.Devices, index);
        if (device is null || device.IsRestricted) return;
        try
        {
            if (device.IsLocalHost)
            {
                StartLocalPlaybackOperation(new(
                    SpotifyLocalPlaybackOperation.StartAndTransfer,
                    ContinuePlaying: true));
                return;
            }
            await _spotify.TransferPlaybackAsync(
                device.DeviceId, true, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _preferredPlaybackDeviceId = device.DeviceId;
                _devices = SpotifyPlaybackPolicy.MarkActiveDevice(_devices, device.DeviceId);
                _devicesCachedAt = _timeProvider.GetUtcNow();
                _status = $"Playing on {device.Name}";
            }
            Invalidate();
            await RefreshPlaybackAsync(
                    Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
            ReconcilePlaybackAfterStart(forceObservation: true);
        }
        catch (SpotifyApplicationException exception)
        {
            SetCommandStatus(SpotifyPlaybackPolicy.SafeMessage(
                exception, "Spotify could not switch devices"));
        }
    }

    private void StartLocalPlaybackOperation(SpotifyLocalPlaybackCommand command)
    {
        const string operationKey = "spotify.local-playback";
        var handle = Operations.RunSingleFlight(
            operationKey,
            async context =>
            {
                await _commandGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
                try
                {
                    lock (_gate) _playbackSettlementReads = 0;
                    await ControlLocalPlaybackAsync(command, context).ConfigureAwait(false);
                }
                finally { _commandGate.Release(); }
            },
            WidgetOperationLifetime.Active);
        if (!handle.IsAccepted)
            SetCommandStatus("Local Spotify playback is not available right now");
    }

    private async ValueTask ControlLocalPlaybackAsync(
        SpotifyLocalPlaybackCommand command,
        WidgetOperationContext operation)
    {
        var operationGeneration = operation.Generation;
        var commandSucceeded = false;
        lock (_gate)
        {
            if (!operation.IsCurrent) return;
            AdvanceLocalPlaybackSummaryRevisionLocked();
            _localPlaybackOperationGeneration = operationGeneration;
            _localPlaybackOperationBaseline = _localPlayback;
            _localPlaybackBusy = true;
            _localPlaybackFeedback = null;
            _status = command.Operation == SpotifyLocalPlaybackOperation.Stop
                ? "Stopping Spotify playback on this PC…"
                : "Starting Spotify playback on this PC…";
        }
        Invalidate();
        try
        {
            var local = await _spotify.ControlLocalPlaybackAsync(
                command, operation.CancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!operation.IsCurrent ||
                    _localPlaybackOperationGeneration != operationGeneration) return;
                _localPlayback = local;
                _localPlaybackFeedback = null;
                _pageError = null;
                _status = local.DisplayMessage ?? "Local Spotify playback updated";
                var localDeviceId = _devices?.Devices
                    .FirstOrDefault(device => device.IsLocalHost)?.DeviceId;
                if (command.Operation == SpotifyLocalPlaybackOperation.Stop)
                {
                    if (_preferredPlaybackDeviceId == localDeviceId)
                        _preferredPlaybackDeviceId = null;
                    _devices = SpotifyPlaybackPolicy.MarkActiveDevice(_devices, null);
                }
                else if (localDeviceId is not null)
                {
                    _preferredPlaybackDeviceId = localDeviceId;
                    _devices = SpotifyPlaybackPolicy.MarkActiveDevice(_devices, localDeviceId);
                }
                _devicesCachedAt = _timeProvider.GetUtcNow();
            }
            commandSucceeded = true;
            if (command.Operation == SpotifyLocalPlaybackOperation.StartAndTransfer)
            {
                await RefreshPlaybackAsync(
                        Volatile.Read(ref _activeGeneration),
                        operation.CancellationToken)
                    .ConfigureAwait(false);
                ReconcilePlaybackAfterStart(forceObservation: true);
            }
        }
        catch (SpotifyApplicationException exception)
        {
            lock (_gate)
            {
                if (_localPlaybackOperationGeneration != operationGeneration) return;
                _pageError = null;
                _localPlayback = _localPlaybackOperationBaseline;
                _localPlaybackFeedback = exception.Code switch
                {
                    "platform_unavailable" =>
                        "Return focus to Devices, then try Play here again.",
                    "resource_not_found" =>
                        "Spotify could not activate this playback device.",
                    _ => SpotifyPlaybackPolicy.SafeMessage(
                        exception, "Local Spotify playback could not be updated"),
                };
                _status = _localPlaybackFeedback;
            }
            _runtimeDiagnostics.Record(
                "local-playback-widget", exception.Code, operationGeneration,
                Math.Max(0, Volatile.Read(ref _activeGeneration)));
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested &&
                  commandSucceeded)
        {
            // The device transfer already succeeded. Lifecycle cancellation of
            // its follow-up observation must not roll back that accepted state.
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            lock (_gate)
                if (_localPlaybackOperationGeneration == operationGeneration)
                {
                    _localPlayback = _localPlaybackOperationBaseline;
                    _localPlaybackFeedback = null;
                    _status = "Local Spotify playback canceled";
                }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (_gate)
            {
                if (_localPlaybackOperationGeneration != operationGeneration) return;
                _pageError = null;
                _localPlayback = _localPlaybackOperationBaseline;
                _localPlaybackFeedback =
                    "Local Spotify playback could not be updated";
                _status = _localPlaybackFeedback;
            }
            _runtimeDiagnostics.Record(
                "local-playback-widget", SpotifyRuntimeDiagnostics.Code(exception),
                operationGeneration,
                Math.Max(0, Volatile.Read(ref _activeGeneration)));
        }
        finally
        {
            var publish = false;
            lock (_gate)
            {
                if (_localPlaybackOperationGeneration == operationGeneration)
                {
                    _localPlaybackOperationGeneration = 0;
                    _localPlaybackOperationBaseline = null;
                    _localPlaybackBusy = false;
                    publish = true;
                }
            }
            if (publish) Invalidate();
        }
    }

    private async Task PlayQueueItemAsync(
        WidgetCollectionItemKey key,
        CancellationToken cancellationToken)
    {
        var items = _queue.Snapshot.Items;
        var index = items.ToList().FindIndex(candidate => candidate.Key == key);
        if (index < 0 || !items[index].Value.IsPlayable) return;
        _queue.SelectAnchor(key, invalidate: false);
        if (index == 0)
        {
            await _spotify.ControlPlaybackAsync(new(SpotifyPlaybackOperation.Next), cancellationToken)
                .ConfigureAwait(false);
            var refreshed = await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration),
                cancellationToken, refreshDemandedQueueOnPlaybackChange: false).ConfigureAwait(false);
            if (refreshed) InvalidateQueueCollection();
            ReconcilePlaybackAfterStart(items[index].Value.Uri);
            return;
        }
        // The API cannot seek to a queued occurrence. Preserve the bounded
        // cached suffix, including duplicates, without guessing its context.
        var uris = items.Skip(index).Where(candidate => candidate.Value.IsPlayable)
            .Take(SpotifyApplicationContract.MaximumQueueItems)
            .Select(candidate => candidate.Value.Uri).ToArray();
        await StartPlaybackAsync(new(null, uris, DeviceId: PlaybackDeviceId()),
            "Playing from here · using the loaded queue", cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistAsync(CancellationToken cancellationToken)
    {
        SpotifyPlaylistSummary? playlist;
        lock (_gate) playlist = _playlistSelection?.Playlist;
        if (playlist is null) return;
        await StartPlaybackAsync(new(playlist.Uri, null,
                DeviceId: PlaybackDeviceId()),
            $"Playing {playlist.Name}", cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistTrackAsync(
        WidgetCollectionItemKey key,
        CancellationToken cancellationToken)
    {
        SpotifyPlaylistSummary? playlist;
        SpotifyMediaItemSummary? item;
        lock (_gate)
        {
            playlist = _playlistSelection?.Playlist;
            item = playlist is not null
                ? _playlistItems.Snapshot.Items
                    .FirstOrDefault(candidate => candidate.Key == key)?.Value
                : null;
        }
        if (playlist is null || item is null || !item.IsPlayable) return;
        _playlistItems.SelectAnchor(key, invalidate: false);
        await StartPlaybackAsync(new(playlist.Uri, null,
                DeviceId: PlaybackDeviceId(), OffsetUri: item.Uri),
            $"Playing {item.Title}", cancellationToken).ConfigureAwait(false);
    }

    private async Task StartPlaybackAsync(
        StartSpotifyPlaybackRequest request,
        string successStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await _spotify.StartPlaybackAsync(request, cancellationToken)
                .ConfigureAwait(false);
            SetCommandStatus(successStatus);
            var refreshed = await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken,
                refreshDemandedQueueOnPlaybackChange: false).ConfigureAwait(false);
            if (refreshed) InvalidateQueueCollection();
            ReconcilePlaybackAfterStart(request.OffsetUri ?? request.ItemUris?.FirstOrDefault());
        }
        catch (SpotifyApplicationException exception)
        {
            SetCommandStatus(exception.Code == "resource_not_found"
                ? "No active Spotify device. Open Devices and choose where to play."
                : SpotifyPlaybackPolicy.SafeMessage(
                    exception, "Spotify could not start playback"));
        }
    }

    private string? PlaybackDeviceId()
    {
        lock (_gate) return _preferredPlaybackDeviceId;
    }

    private static T? ItemAt<T>(IReadOnlyList<T>? items, int index) where T : class =>
        items is not null && index >= 0 && index < items.Count ? items[index] : null;

    private static string PageError(SpotifyApplicationException exception) => exception.Code switch
    {
        "forbidden" =>
            "Spotify did not allow this action for the current account.",
        "authorization_scope_required" =>
            "Reconnect Spotify to grant the scope required for this page.",
        "premium_required" =>
            "Spotify Premium is required for playback and device transfer.",
        _ => SpotifyPlaybackPolicy.SafeMessage(
            exception, "Spotify could not load this page. Try again."),
    };

    private static WidgetResourceError SpotifyResourceError(Exception exception) => new(
        "spotify_page_error",
        exception is SpotifyApplicationException capability
            ? PageError(capability)
            : "Spotify could not load this page. Try again.");

    private async Task ExecuteAsync(
        SpotifyPlaybackOperation operation,
        long? requestedPosition,
        WidgetActionEvent action,
        CancellationToken cancellationToken)
    {
        SpotifyPlaybackSummary? before;
        SpotifyPlaybackCommand? command;
        long operationSequence;
        lock (_gate)
        {
            before = _playback;
            command = SpotifyPlaybackPolicy.BuildCommand(
                before, operation, requestedPosition);
            if (command is null || _pendingOperation is not null)
            {
                _status = "That Spotify control is not available";
                Invalidate();
                return;
            }
            _optimisticReconciliation = null;
            operationSequence = ++_playbackOperationSequence;
            _pendingOperation = operation;
            _pendingOperationSequence = operationSequence;
            _playback = SpotifyPlaybackPolicy.ApplyOptimistic(
                before!, command, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            _status = SpotifyPlaybackPolicy.OperationStatus(operation);
        }
        Invalidate();
        try
        {
            await _spotify.ControlPlaybackAsync(command, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_pendingOperation != operation ||
                    _pendingOperationSequence != operationSequence) return;
                _pendingOperation = null;
                _pendingOperationSequence = 0;
                if (SpotifyPlaybackPolicy.HasOptimisticPresentation(operation) &&
                    _playback is { IsAvailable: true } projectedPlayback)
                {
                    _optimisticReconciliation = new(
                        operationSequence,
                        operation,
                        projectedPlayback,
                        Volatile.Read(ref _playbackObservationSequence),
                        _timeProvider.GetUtcNow() + OptimisticReconciliationLifetime);
                }
                _status = "Updated in Spotify";
            }
            Invalidate();

            // The Web API acknowledges all transport controls without a
            // playback representation. Retain only accepted optimistic field
            // projections until adaptive polling obtains Spotify's next
            // authoritative observation; the immediate player read can still
            // report the pre-command state and otherwise makes every surface
            // briefly revert the shared field. Next/Previous do not project a
            // track, so their immediate read remains responsible for queue
            // reconciliation.
            if (SpotifyPlaybackPolicy.HasOptimisticPresentation(operation))
                return;

            var refreshQueueAfterPlayback = operation is SpotifyPlaybackOperation.Next or
                SpotifyPlaybackOperation.Previous;
            var playbackRefreshed = await RefreshPlaybackAsync(
                    Volatile.Read(ref _activeGeneration), cancellationToken,
                    refreshDemandedQueueOnPlaybackChange: !refreshQueueAfterPlayback)
                .ConfigureAwait(false);
            if (refreshQueueAfterPlayback && playbackRefreshed)
                InvalidateQueueCollection();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOptimistic(before, operationSequence, action);
        }
        catch (SpotifyApplicationException exception)
        {
            RecordActionDiagnostic(action, "playback-provider", PlaybackFailureCode(exception));
            RestoreOptimistic(before, operationSequence, action, SpotifyPlaybackPolicy.SafeMessage(exception,
                exception.Code == "forbidden"
                    ? "Spotify did not allow playback control"
                    : "Spotify rejected that control"));
        }
        catch (Exception exception)
        {
            RecordActionDiagnostic(action, "playback-provider", PlaybackFailureCode(exception));
            RestoreOptimistic(before, operationSequence, action);
            throw;
        }
    }

    private void RestoreOptimistic(
        SpotifyPlaybackSummary? playback,
        long operationSequence,
        WidgetActionEvent action,
        string? status = null)
    {
        lock (_gate)
        {
            if (_pendingOperationSequence != operationSequence) return;
            _playback = playback;
            _pendingOperation = null;
            _pendingOperationSequence = 0;
            _optimisticReconciliation = null;
            if (status is not null) _status = status;
        }
        Invalidate();
    }

    private static string PlaybackFailureCode(Exception exception)
    {
        var code = SpotifyRuntimeDiagnostics.Code(exception);
        return code.Length <= 56 ? "failed-" + code : "failed";
    }

    private void SetState(
        long generation,
        SpotifyWidgetViewState state,
        string status,
        SpotifyPlaybackSummary? playback,
        bool refreshDemandedQueueOnPlaybackChange = false,
        long observationSequence = 0)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        bool playbackIdentityChanged;
        lock (_gate)
        {
            var admittedPlayback = AdmitPlaybackObservationLocked(
                playback, observationSequence);
            playbackIdentityChanged = refreshDemandedQueueOnPlaybackChange &&
                PlaybackQueueIdentity(_playback) != PlaybackQueueIdentity(admittedPlayback);
            _viewState = state;
            _status = status;
            _playback = admittedPlayback;
            _refreshWarning = null;
            _consecutiveRefreshFailures = 0;
            if (state != SpotifyWidgetViewState.Ready || playback is null)
                ClearOptimisticReconciliationLocked();
        }
        Invalidate();
        if (playbackIdentityChanged) InvalidateQueueCollection();
    }

    private static (bool Available, string? Uri) PlaybackQueueIdentity(
        SpotifyPlaybackSummary? playback) =>
        playback is { IsAvailable: true }
            ? (true, playback.Item?.Uri)
            : (false, null);

    private SpotifyPlaybackSummary? AdmitPlaybackObservationLocked(
        SpotifyPlaybackSummary? observed,
        long observationSequence)
    {
        if (_pendingOperation is { } pendingOperation)
            return SpotifyPlaybackPolicy.MergeOptimisticPresentation(
                _playback, observed, pendingOperation);

        var reconciliation = _optimisticReconciliation;
        if (reconciliation is null || observed is null) return observed;

        var now = _timeProvider.GetUtcNow();
        if (now >= reconciliation.ExpiresAt ||
            observationSequence > reconciliation.ObservationBoundary ||
            SpotifyPlaybackPolicy.MatchesOptimisticPresentation(observed, reconciliation))
        {
            _optimisticReconciliation = null;
            return observed;
        }

        // This request began before the status-only command response and did
        // not yet reflect the accepted field. Preserve only that owned field;
        // all independent observation data is admitted immediately.
        return SpotifyPlaybackPolicy.MergeOptimisticPresentation(
            reconciliation.ProjectedPlayback, observed, reconciliation.Operation);
    }

    private void ClearOptimisticReconciliationLocked()
    {
        _optimisticReconciliation = null;
    }

    private void ApplyRefreshFailure(long generation, Exception exception)
    {
        var failure = SpotifyRefreshFailurePolicy.Classify(exception);
        var invalidate = false;
        lock (_gate)
        {
            if (generation != Volatile.Read(ref _activeGeneration)) return;
            if (failure.Disposition == SpotifyRefreshFailureDisposition.Transient &&
                _viewState == SpotifyWidgetViewState.Ready)
            {
                _consecutiveRefreshFailures = Math.Min(
                    SpotifyRefreshFailurePolicy.MaximumTrackedFailures,
                    _consecutiveRefreshFailures + 1);
                _refreshWarning = SpotifyRefreshFailurePolicy.CreateWarning(
                    failure, _consecutiveRefreshFailures);
                _status = _refreshWarning.Status;
                invalidate = true;
            }
            else
            {
                _consecutiveRefreshFailures = 0;
                _refreshWarning = null;
                _viewState = failure.FallbackState;
                _status = failure.Status;
                _playback = null;
                ClearOptimisticReconciliationLocked();
                if (failure.Disposition != SpotifyRefreshFailureDisposition.Transient)
                {
                    ClearPageCachesLocked();
                    if (failure.Disposition ==
                        SpotifyRefreshFailureDisposition.AuthorizationRequired)
                        _authorizationState =
                            SpotifyAuthorizationState.ReauthorizationRequired;
                }
                invalidate = true;
            }
        }
        if (invalidate) Invalidate();
    }

    private void SetCommandStatus(string status)
    {
        lock (_gate) _status = status;
        Invalidate();
    }


    private readonly WidgetCursorResource<SpotifySearchCollectionItem> _search;
    private string _searchQuery = string.Empty;
    private SpotifySearchKind _searchKind = SpotifySearchKind.Track;
    private long _searchGeneration;

    private WidgetCursorResource<SpotifySearchCollectionItem> CreateSearchResource() =>
        CreateCursorResource<SpotifySearchCollectionItem>("spotify.search", new()
        {
            PageSize = 10,
            MaximumRetainedItems = WidgetCursorResource<SpotifySearchCollectionItem>.MaximumRetainedItems,
            RetainedItemTarget = 60,
            PaginationThreshold = 2,
            LoadPage = async (cursor, _, limit, token) =>
            {
                string query;
                SpotifySearchKind kind;
                long generation;
                lock (_gate) { query = _searchQuery; kind = _searchKind; generation = _searchGeneration; }
                if (query.Length == 0) return new WidgetCursorPage<SpotifySearchCollectionItem>([], null, null);
                var page = await _spotify.SearchAsync(query, kind,
                    SpotifyCollectionIdentity.Offset(cursor), limit, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Search rankings and totals are live, not a stable indexed
                // library. Identify returned occurrences and keep remote offsets
                // only in the request cursors, not in the host's virtual extent.
                var items = page.Items.Select((item, index) => new SpotifySearchCollectionItem(item,
                    new WidgetCollectionItemKey("search." + generation + "." + (page.Offset + index) + "." +
                        SpotifyCollectionIdentity.Media(item.Uri).Value))).ToArray();
                return SpotifyCollectionIdentity.Page(items, page.Offset, page.Limit,
                    page.Total, hasAuthoritativeWindow: false);
            },
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.search.scroll", item => item.Key,
                    item => "spotify.search.item." + item.Key.Value, "spotify.search.query")
                { EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent },
            ],
        });

    private async ValueTask<bool> TryHandleSearchActionAsync(WidgetActionEvent action,
        CancellationToken cancellationToken)
    {
        if (action.ActionId is not ("spotify.search.query" or "spotify.search.clear") && !action.ActionId.StartsWith("spotify.search.type.", StringComparison.Ordinal) && !action.ActionId.StartsWith("spotify.search.play.", StringComparison.Ordinal)) return false;
        if (_navigation.Value.Route != SpotifyRoute.Search) return true;
        lock (_gate) if (_viewState != SpotifyWidgetViewState.Ready) return true;
        if (action.ActionId == "spotify.search.query" && action.CommittedText is { } text)
        {
            var query = text.Trim();
            if (query.Length > ProtocolConstants.MaximumTextEntryLength || query.Any(char.IsControl)) return true;
            ResetSearch(query, null);
        }
        else if (action.ActionId == "spotify.search.clear") ResetSearch(string.Empty, null);
        else if (action.ActionId.StartsWith("spotify.search.type.", StringComparison.Ordinal) &&
            Enum.TryParse<SpotifySearchKind>(action.ActionId["spotify.search.type.".Length..], out var kind) &&
            Enum.IsDefined(kind))
        {
            lock (_gate) if (kind == _searchKind) return true;
            ResetSearch(null, kind);
        }
        else if (action.ActionId.StartsWith("spotify.search.play.", StringComparison.Ordinal))
        {
            var key = action.ActionId["spotify.search.play.".Length..];
            var item = _search.Snapshot.Items.FirstOrDefault(item => item.Key.Value == key)?.Value;
            if (item is not { IsPlayable: true }) return true;
            await RunCommandOperationAsync(token => StartPlaybackAsync(
                item.Kind == SpotifySearchKind.Track
                    ? new(null, [item.Uri], DeviceId: PlaybackDeviceId())
                    : new(item.Uri, null, DeviceId: PlaybackDeviceId()),
                "Playing " + item.Title, token), cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private void ResetSearch(string? query, SpotifySearchKind? kind)
    {
        lock (_gate)
        {
            if (query is not null) _searchQuery = query;
            if (kind is { } value) _searchKind = value;
            ++_searchGeneration;
            _search.Reset(invalidate: false);
            if (_searchQuery.Length > 0) _search.EnsureLoaded();
        }
        Invalidate();
    }

    private void ReconcilePlaybackAfterStart(string? expectedUri = null, bool forceObservation = false)
    {
        lock (_gate)
        {
            _expectedPlaybackUri = expectedUri;
            _playbackSettlementReads = !forceObservation && _playback is { IsAvailable: true, IsPlaying: true } &&
                (expectedUri is null || _playback.Item?.Uri == expectedUri) ? 0 : 3;
        }
        if (_pollWake.CurrentCount == 0)
            try { _pollWake.Release(); } catch (SemaphoreFullException) { }
    }

    private async ValueTask<bool> TryHandleQueueAdditionAsync(
        WidgetActionEvent action, CancellationToken cancellationToken)
    {
        const string searchPrefix = "spotify.search.enqueue.";
        const string playlistPrefix = "spotify.playlist.enqueue.";
        string? uri;
        if (action.ActionId.StartsWith(searchPrefix, StringComparison.Ordinal))
        {
            if (_navigation.Value.Route != SpotifyRoute.Search) return true;
            var key = action.ActionId[searchPrefix.Length..];
            var item = _search.Snapshot.Items.FirstOrDefault(item => item.Key.Value == key)?.Value;
            uri = item is { IsPlayable: true, Kind: SpotifySearchKind.Track } ? item.Uri : null;
        }
        else if (action.ActionId.StartsWith(playlistPrefix, StringComparison.Ordinal))
        {
            if (_navigation.Value.Route != SpotifyRoute.PlaylistDetail) return true;
            var key = action.ActionId[playlistPrefix.Length..];
            var item = _playlistItems.Snapshot.Items.FirstOrDefault(item => item.Key.Value == key)?.Value;
            uri = item is { IsPlayable: true } ? item.Uri : null;
        }
        else return false;
        if (uri is null) return true;
        await RunCommandOperationAsync(async token =>
        {
            try
            {
                await _spotify.AddToQueueAsync(uri, PlaybackDeviceId(), token).ConfigureAwait(false);
                InvalidateQueueCollection();
                ShowActionToast("Added to queue", "Your track is in Spotify’s queue.", ToastTone.Success);
            }
            catch (SpotifyApplicationException exception)
            {
                ShowActionToast("Could not add to queue", SpotifyPlaybackPolicy.SafeMessage(
                    exception, "Check Spotify’s queue before trying again."), ToastTone.Warning);
            }
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ShowActionToast(string title, string message, ToastTone tone)
    {
        var toast = UI.Toast(title, message, tone, "spotify.action-toast");
        lock (_gate) _actionToast = toast;
        Invalidate();
        _ = _toastExpiry.ScheduleLatest(UI.DefaultToastDuration, () =>
        {
            lock (_gate) if (ReferenceEquals(_actionToast, toast)) _actionToast = null;
            Invalidate();
        });
    }

}
