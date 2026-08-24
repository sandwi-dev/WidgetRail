using WidgetRail.WidgetProtocol;
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
}

/// <summary>
/// Controller-first Spotify community widget. All Spotify access crosses the
/// public typed SDK; the widget never receives OAuth tokens or a client secret.
/// </summary>
public sealed class SpotifyWidget : Widget
{
    private static readonly TimeSpan PlayingPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PausedPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ErrorPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetupRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevicesCacheLifetime = TimeSpan.FromSeconds(20);
    private const int MaximumQueuePlaybackReconciliationAttempts = 2;
    private static readonly IReadOnlyList<SpotifyAuthorizationScope> SpotifyScopes =
    [
        SpotifyAuthorizationScope.PlaybackStateRead,
        SpotifyAuthorizationScope.PlaybackStateControl,
        SpotifyAuthorizationScope.LocalPlayback,
        SpotifyAuthorizationScope.PlaylistsRead,
    ];

    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ISpotifyApplicationService _spotify;
    private readonly TimeProvider _timeProvider;
    private readonly ISpotifyRuntimeDiagnostics _runtimeDiagnostics;
    private SpotifyWidgetViewState _viewState = SpotifyWidgetViewState.Initial;
    private SpotifyAuthorizationState _authorizationState =
        SpotifyAuthorizationState.Disconnected;
    private SpotifyPlaybackSummary? _playback;
    private SpotifyDestination _destination;
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
    private string? _pageError;
    private string? _readyInitialFocusId = "spotify.play-toggle";
    private SpotifyPlaylistSelection? _playlistSelection;
    private SpotifySelectedPlaylistPageSource? _playlistPageSource;
    private long? _playlistItemsSelectionGeneration;
    private long _playlistSelectionGeneration;
    private long _presentationCaptureSequence;
    private SpotifyPlaybackOperation? _pendingOperation;
    private string _status = "Spotify loads when this widget becomes visible";
    private SpotifyRefreshWarning? _refreshWarning;
    private int _consecutiveRefreshFailures;
    private bool _showSetup;
    private long _setupViewGeneration;
    private bool _setupBusy;
    private bool _upNextPinnedLayoutSelected;
    private long _activeGeneration;
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
        ISpotifyRuntimeDiagnostics runtimeDiagnostics)
    {
        _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runtimeDiagnostics = runtimeDiagnostics ??
            throw new ArgumentNullException(nameof(runtimeDiagnostics));
        _queue = CreateCursorResource<SpotifyMediaCollectionItem>("spotify.queue", new()
        {
            PageSize = SpotifyApplicationContract.MaximumQueueItems,
            MaximumRetainedItems = SpotifyApplicationContract.MaximumQueueItems * 2,
            LoadPage = LoadQueuePageAsync,
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.queue.scroll.wide", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", "wide", item.Key),
                    "spotify.queue.empty.wide"),
                new("spotify.queue.scroll.compact", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", "compact", item.Key),
                    "spotify.queue.empty.compact"),
            ],
        });
        _playlists = CreateCursorResource<SpotifyPlaylistCollectionItem>(
            "spotify.playlists", new()
        {
            PageSize = SpotifyCollectionPolicy.PageSize,
            MaximumRetainedItems = SpotifyCollectionPolicy.MaximumRetainedItems,
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
                new("spotify.playlists.scroll.wide", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.item", "wide", item.Key),
                    "spotify.page.sparse.playlist.wide")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
                new("spotify.playlists.scroll.compact", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.item", "compact", item.Key),
                    "spotify.page.sparse.playlist.compact")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
            ],
        });
        _playlistItems = CreateCursorResource<SpotifyMediaCollectionItem>(
            "spotify.playlist.items", new()
        {
            PageSize = SpotifyCollectionPolicy.PageSize,
            MaximumRetainedItems = SpotifyCollectionPolicy.MaximumRetainedItems,
            PaginationThreshold = SpotifyCollectionPolicy.PaginationThreshold,
            LoadPage = LoadSelectedPlaylistCursorPageAsync,
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.playlist.detail.scroll.wide", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.track", "wide", item.Key),
                    "spotify.playlist.play.wide")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
                new("spotify.playlist.detail.scroll.compact", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.track", "compact", item.Key),
                    "spotify.playlist.play.compact")
                {
                    EstimatedItemExtent = SpotifyCollectionPolicy.EstimatedItemExtent,
                },
            ],
        });
    }

    public SpotifyWidgetViewState ViewState { get { lock (_gate) return _viewState; } }
    public string Status { get { lock (_gate) return _status; } }
    public SpotifyPlaybackSummary? Playback { get { lock (_gate) return _playback; } }
    public SpotifyDestination Destination { get { lock (_gate) return _destination; } }

    public static string FormatTime(long milliseconds) =>
        SpotifyPresentation.FormatTime(milliseconds);

    public override WidgetView Render()
    {
        try
        {
            return SpotifyPresentation.Render(CapturePresentationState());
        }
        catch (Exception exception)
        {
            _runtimeDiagnostics.Record(
                "render-failed",
                SpotifyRuntimeDiagnostics.Code(exception));
            throw;
        }
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
            else if (_destination == SpotifyDestination.Playlists)
                _playlists.EnsureLoaded();
            else if (_destination == SpotifyDestination.Queue ||
                     _upNextPinnedLayoutSelected)
                _queue.EnsureLoaded();
        }
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Interlocked.Increment(ref _activeGeneration);
        var tasks = new[] { _pollTask, _progressTask }
            .Where(task => task is not null).Cast<Task>().ToArray();
        _pollTask = null;
        _progressTask = null;
        if (tasks.Length != 0)
            await Task.WhenAll(tasks).WaitAsync(transitionToken).ConfigureAwait(false);
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        lock (_gate) _upNextPinnedLayoutSelected = false;
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
        string? layoutId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = string.Equals(
            layoutId, SpotifyPresentation.UpNextPinnedLayoutId,
            StringComparison.Ordinal);
        bool changed;
        bool reset;
        bool load;
        lock (_gate)
        {
            changed = _upNextPinnedLayoutSelected != selected;
            _upNextPinnedLayoutSelected = selected;
            reset = changed && !selected && _destination != SpotifyDestination.Queue;
            load = changed && selected && IsActive &&
                _viewState == SpotifyWidgetViewState.Ready;
        }
        if (!changed) return ValueTask.CompletedTask;
        if (reset) _queue.Reset(invalidate: false);
        else if (load) _queue.EnsureLoaded();
        Invalidate();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var intent = SpotifyRouteActionPolicy.Classify(action);
        if (TryHandlePageAction(action, intent)) return;

        switch (intent.Kind)
        {
            case SpotifyActionKind.SetupOpen:
                lock (_gate)
                {
                    _showSetup = true;
                    _setupViewGeneration++;
                }
                Invalidate();
                break;
            case SpotifyActionKind.SetupClose:
                lock (_gate) _showSetup = false;
                Invalidate();
                break;
            case SpotifyActionKind.SetupDone:
                lock (_gate) _showSetup = false;
                Invalidate();
                if (IsActive)
                    await RunCommandOperationAsync(CheckConfigurationAsync, cancellationToken)
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
                await RunCommandOperationAsync(RefreshAsync, cancellationToken)
                    .ConfigureAwait(false);
                RefreshVisibleCollection(action.InputScopeId);
                break;
            case SpotifyActionKind.Playback:
                var playbackOperation = intent.PlaybackOperation is null
                    ? SpotifyPlaybackPolicy.ResolveToggle(Playback)
                    : intent.PlaybackOperation!.Value;
                await RunCommandOperationAsync(token => ExecuteAsync(
                    playbackOperation, null, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.Seek:
                await RunCommandOperationAsync(token => ExecuteAsync(
                    SpotifyPlaybackOperation.Seek,
                    intent.RequestedPositionMs, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Player:
                CancelPageOperation();
                Navigate(SpotifyDestination.Player, action.SourceElementId);
                break;
            case SpotifyActionKind.PlaylistBack:
                CancelPageOperation();
                _playlists.ClearRequestedFocus(invalidate: false);
                lock (_gate)
                {
                    var returnFocusId = _playlistSelection?.ReturnFocusId;
                    ClearPlaylistSelectionLocked();
                    _pageLoading = false;
                    _pageError = null;
                    _readyInitialFocusId = returnFocusId;
                }
                Invalidate();
                break;
            case SpotifyActionKind.Noop:
                break;
            case SpotifyActionKind.LocalStart:
                await RunCommandOperationAsync(token => ControlLocalPlaybackAsync(
                    new(SpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), token), cancellationToken).ConfigureAwait(false);
                break;
            case SpotifyActionKind.LocalStop:
                await RunCommandOperationAsync(token => ControlLocalPlaybackAsync(
                    new(SpotifyLocalPlaybackOperation.Stop), token), cancellationToken)
                    .ConfigureAwait(false);
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
        lock (_gate) _showSetup = false;
        Invalidate();
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
        lock (_gate)
        {
            if (_destination == SpotifyDestination.Playlists)
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
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Queue:
                Navigate(SpotifyDestination.Queue, action.SourceElementId);
                _queue.EnsureLoaded();
                return true;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Playlists:
                Navigate(SpotifyDestination.Playlists, action.SourceElementId);
                _playlists.EnsureLoaded();
                return true;
            case SpotifyActionKind.Navigate when
                intent.Destination == SpotifyDestination.Devices:
                StartPageOperation(operation => NavigateAndLoadAsync(
                    SpotifyDestination.Devices, action.SourceElementId, operation));
                return true;
            case SpotifyActionKind.PageRetry:
                lock (_gate)
                {
                    if (_playlistSelection is not null)
                    {
                        _playlistItems.Retry();
                        return true;
                    }
                    if (_destination == SpotifyDestination.Playlists)
                    {
                        _playlists.Retry();
                        return true;
                    }
                    if (_destination == SpotifyDestination.Queue)
                    {
                        _queue.Retry();
                        return true;
                    }
                    else
                    {
                        var mode = action.SourceElementId.Contains(".compact.",
                            StringComparison.Ordinal) ? "compact" : "wide";
                        _readyInitialFocusId = SpotifyRouteActionPolicy.NavigationFocusId(
                            _destination, mode);
                    }
                }
                StartPageOperation(ReloadCurrentPageAsync);
                return true;
        }
        if (intent.Kind != SpotifyActionKind.PlaylistOpen || intent.ItemKey is not { } playlistKey)
            return false;
        OpenPlaylist(playlistKey, action.SourceElementId);
        return true;
    }

    private async Task RunCommandOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            SetCommandStatus("Spotify could not complete that action");
        }
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
        SpotifyDestination destination;
        bool detail;
        bool ready;
        lock (_gate)
        {
            destination = _destination;
            detail = _playlistSelection is not null;
            ready = _viewState == SpotifyWidgetViewState.Ready;
        }
        if (!ready) return;
        if (string.Equals(inputScopeId, SpotifyPresentation.UpNextPinnedScope,
                StringComparison.Ordinal))
            _queue.Refresh();
        else if (destination == SpotifyDestination.Queue)
            _queue.Refresh();
        else if (destination == SpotifyDestination.Playlists)
        {
            if (detail) _playlistItems.Refresh();
            else _playlists.Refresh();
        }
    }

    private void InvalidateQueueCollection()
    {
        SpotifyDestination destination;
        bool upNextPinnedLayoutSelected;
        lock (_gate)
        {
            destination = _destination;
            upNextPinnedLayoutSelected = _upNextPinnedLayoutSelected;
        }
        if (destination == SpotifyDestination.Queue || upNextPinnedLayoutSelected)
            _queue.Refresh();
        else _queue.Reset(invalidate: false);
    }

    private async ValueTask<WidgetCursorPage<SpotifyMediaCollectionItem>> LoadQueuePageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int _,
        CancellationToken cancellationToken)
    {
        if (cursor is not null || direction is not null)
            throw new InvalidOperationException("Spotify queue does not expose adjacent cursors.");
        for (var attempt = 0; attempt < MaximumQueuePlaybackReconciliationAttempts; attempt++)
        {
            var queue = await _spotify.GetQueueAsync(cancellationToken).ConfigureAwait(false);
            if (!QueueMatchesLatestPlayback(queue.CurrentlyPlaying)) continue;

            var occurrenceRequest = _queueOccurrences.BeginPage("queue", 0, direction);
            var items = _queueOccurrences.NormalizePage(
                occurrenceRequest, queue.Items, []);
            return new(items, null, null);
        }

        throw new InvalidOperationException(
            "Spotify queue did not converge with the current playback item.");
    }

    private bool QueueMatchesLatestPlayback(SpotifyMediaItemSummary? currentlyPlaying)
    {
        lock (_gate)
            return PlaybackQueueIdentity(_playback) == QueuePlaybackIdentity(currentlyPlaying);
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

                var interval = PollInterval();
                await Task.Delay(interval, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private TimeSpan PollInterval()
    {
        lock (_gate)
        {
            if (_refreshWarning is { } warning) return warning.RetryDelay;
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

    private async Task RefreshPlaybackAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _activeGeneration)) return;
            lock (_gate)
            {
                if (_authorizationState != SpotifyAuthorizationState.Connected) return;
            }
            try
            {
                var playback = await _spotify.GetPlaybackAsync(cancellationToken)
                    .ConfigureAwait(false);
                SetState(generation, SpotifyWidgetViewState.Ready,
                    playback.IsAvailable ? "Live from Spotify" : "Connected · no active playback",
                    playback, refreshDemandedQueueOnPlaybackChange: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                ApplyRefreshFailure(generation, exception);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

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

            var playback = await _spotify.GetPlaybackAsync(cancellationToken)
                .ConfigureAwait(false);
            SetState(generation, SpotifyWidgetViewState.Ready,
                playback.IsAvailable ? "Live from Spotify" : "Connected · no active playback",
                playback);
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
            var playlists = SpotifyCursorPresentation<SpotifyPlaylistCollectionItem>.Capture(
                _playlists,
                "spotify.playlists",
                "spotify.playlists.scroll.wide",
                "spotify.playlists.scroll.compact");
            SpotifyPlaylistDetailPresentation? detail = null;
            if (_playlistSelection is { } selection &&
                _playlistItemsSelectionGeneration == selection.Key.Generation)
                detail = new(selection,
                    SpotifyCursorPresentation<SpotifyMediaCollectionItem>.Capture(
                        _playlistItems,
                        "spotify.playlist.items",
                        "spotify.playlist.detail.scroll.wide",
                        "spotify.playlist.detail.scroll.compact"));
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
                _showSetup,
                _setupViewGeneration,
                _setupBusy,
                _destination,
                _queue.Snapshot,
                playlists,
                detail,
                _devices,
                _localPlayback,
                _pageLoading,
                _pageError,
                _readyInitialFocusId);
        }
    }

    private void Navigate(SpotifyDestination destination, string sourceElementId)
    {
        _playlists.ClearRequestedFocus(invalidate: false);
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _destination = destination;
            _pageError = null;
            _pageLoading = false;
            _readyInitialFocusId = sourceElementId;
        }
        Invalidate();
    }

    private async Task NavigateAndLoadAsync(
        SpotifyDestination destination,
        string sourceElementId,
        WidgetOperationContext operation)
    {
        _playlists.ClearRequestedFocus(invalidate: false);
        var shouldLoad = false;
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _destination = destination;
            _pageError = null;
            _readyInitialFocusId = sourceElementId;
            shouldLoad = destination switch
            {
                SpotifyDestination.Devices => SpotifyRouteActionPolicy.DevicesNeedLoad(
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
        SpotifyDestination destination;
        lock (_gate)
        {
            destination = _destination;
            _pageLoading = true;
            _pageError = null;
        }
        Invalidate();
        return LoadDestinationAsync(destination, operation);
    }

    private async Task LoadDestinationAsync(
        SpotifyDestination destination,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            switch (destination)
            {
                case SpotifyDestination.Devices:
                    var devicesTask = _spotify.GetDevicesAsync(cancellationToken).AsTask();
                    var localTask = _spotify.GetLocalPlaybackAsync(cancellationToken).AsTask();
                    await Task.WhenAll(devicesTask, localTask).ConfigureAwait(false);
                    if (!operation.IsCurrent) return;
                    lock (_gate)
                    {
                        _devices = devicesTask.Result;
                        _localPlayback = localTask.Result;
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

    private void OpenPlaylist(WidgetCollectionItemKey key, string sourceElementId)
    {
        var mode = sourceElementId.Contains(".compact.", StringComparison.Ordinal)
            ? "compact" : "wide";
        lock (_gate)
        {
            var playlist = _playlists.Snapshot.Items
                .FirstOrDefault(item => item.Key == key)?.Value;
            if (_destination != SpotifyDestination.Playlists || playlist is null) return;
            _playlists.SelectAnchor(key, invalidate: false);
            _playlistItems.Reset(invalidate: false);
            _playlistOccurrences.Reset();
            var generation = checked(++_playlistSelectionGeneration);
            _pageError = null;
            _playlistSelection = new(
                new(playlist.PlaylistId, generation), playlist, mode, sourceElementId);
            _playlistPageSource = new(_spotify, _playlistSelection.Key);
            _playlistItemsSelectionGeneration = generation;
            _readyInitialFocusId = $"spotify.playlist.play.{mode}";
            _playlistItems.EnsureLoaded();
        }
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
        _playlists.Reset(invalidate: false);
        ClearPlaylistSelectionLocked();
        _queue.Reset(invalidate: false);
        _queueOccurrences.Reset();
        _devices = null;
        _localPlayback = null;
        _devicesCachedAt = null;
        _preferredPlaybackDeviceId = null;
        _pageLoading = false;
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
                await ControlLocalPlaybackAsync(
                    new(SpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), cancellationToken).ConfigureAwait(false);
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
        }
        catch (SpotifyApplicationException exception)
        {
            SetCommandStatus(SpotifyPlaybackPolicy.SafeMessage(
                exception, "Spotify could not switch devices"));
        }
    }

    private async Task ControlLocalPlaybackAsync(
        SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) _pageLoading = true;
            Invalidate();
            var local = await _spotify.ControlLocalPlaybackAsync(
                command, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _localPlayback = local;
                _pageLoading = false;
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
            Invalidate();
        }
        catch (SpotifyApplicationException exception)
        {
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
                _status = exception.Code switch
                {
                    "platform_unavailable" =>
                        "Return focus to Devices, then try Play here again.",
                    "resource_not_found" =>
                        "Spotify could not activate this playback device.",
                    _ => SpotifyPlaybackPolicy.SafeMessage(
                        exception, "Local Spotify playback could not be updated"),
                };
            }
            Invalidate();
        }
    }

    private async Task PlayQueueItemAsync(
        WidgetCollectionItemKey key,
        CancellationToken cancellationToken)
    {
        var item = _queue.Snapshot.Items.FirstOrDefault(candidate => candidate.Key == key)?.Value;
        if (item is null || !item.IsPlayable) return;
        _queue.SelectAnchor(key, invalidate: false);
        await StartPlaybackAsync(new(null, [item.Uri], DeviceId: PlaybackDeviceId()),
            "Playing selected queue item",
            cancellationToken).ConfigureAwait(false);
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
            InvalidateQueueCollection();
            SetCommandStatus(successStatus);
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        SpotifyPlaybackSummary? before;
        SpotifyPlaybackCommand? command;
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
            _pendingOperation = operation;
            _playback = SpotifyPlaybackPolicy.ApplyOptimistic(
                before!, command, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            _status = SpotifyPlaybackPolicy.OperationStatus(operation);
        }
        Invalidate();
        try
        {
            await _spotify.ControlPlaybackAsync(command, cancellationToken)
                .ConfigureAwait(false);
            var invalidateQueue = false;
            lock (_gate)
            {
                _pendingOperation = null;
                _status = "Updated in Spotify";
                if (operation is SpotifyPlaybackOperation.Next or
                    SpotifyPlaybackOperation.Previous)
                    invalidateQueue = true;
            }
            if (invalidateQueue) InvalidateQueueCollection();
            Invalidate();
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOptimistic(before);
        }
        catch (SpotifyApplicationException exception)
        {
            RestoreOptimistic(before, SpotifyPlaybackPolicy.SafeMessage(exception,
                exception.Code == "forbidden"
                    ? "Spotify did not allow playback control"
                    : "Spotify rejected that control"));
        }
    }

    private void RestoreOptimistic(SpotifyPlaybackSummary? playback, string? status = null)
    {
        lock (_gate)
        {
            _playback = playback;
            _pendingOperation = null;
            if (status is not null) _status = status;
        }
        Invalidate();
    }

    private void SetState(
        long generation,
        SpotifyWidgetViewState state,
        string status,
        SpotifyPlaybackSummary? playback,
        bool refreshDemandedQueueOnPlaybackChange = false)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        bool playbackIdentityChanged;
        lock (_gate)
        {
            playbackIdentityChanged = refreshDemandedQueueOnPlaybackChange &&
                PlaybackQueueIdentity(_playback) != PlaybackQueueIdentity(playback);
            _viewState = state;
            _status = status;
            _playback = playback;
            _pendingOperation = null;
            _refreshWarning = null;
            _consecutiveRefreshFailures = 0;
        }
        Invalidate();
        if (playbackIdentityChanged) InvalidateQueueCollection();
    }

    private static (bool Available, string? Uri) PlaybackQueueIdentity(
        SpotifyPlaybackSummary? playback) =>
        playback is { IsAvailable: true }
            ? (true, playback.Item?.Uri)
            : (false, null);

    private static (bool Available, string? Uri) QueuePlaybackIdentity(
        SpotifyMediaItemSummary? currentlyPlaying) =>
        currentlyPlaying is null
            ? (false, null)
            : (true, currentlyPlaying.Uri);

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
                _pendingOperation = null;
                invalidate = true;
            }
            else
            {
                _consecutiveRefreshFailures = 0;
                _refreshWarning = null;
                _viewState = failure.FallbackState;
                _status = failure.Status;
                _playback = null;
                _pendingOperation = null;
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

}
