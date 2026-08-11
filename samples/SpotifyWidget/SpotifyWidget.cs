using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

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
public sealed partial class SpotifyWidget : Widget
{
    private const string InputScope = "spotify.window";
    private const string SetupScope = "spotify.setup";
    private static readonly TimeSpan PlayingPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PausedPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ErrorPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetupRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevicesCacheLifetime = TimeSpan.FromSeconds(20);
    private const int CollectionPageSize = 12;
    private const int QueuePageSize = 50;
    private const int MaximumRetainedCollectionItems = CollectionPageSize * 2;
    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Adaptive,
        PreferredWidth = 980,
        PreferredHeight = 560,
        MinimumWidth = 620,
        MinimumHeight = 400,
    };
    private static readonly WidgetQuickActionCapability PlaybackControlAuthority = new(
        WidgetSpotifyCapabilities.PlaybackControlCapabilityId,
        WidgetSpotifyCapabilities.PlaybackControlOperationId);
    private static readonly IReadOnlyList<WidgetSpotifyAuthorizationScope> SpotifyScopes =
    [
        WidgetSpotifyAuthorizationScope.PlaybackStateRead,
        WidgetSpotifyAuthorizationScope.PlaybackStateControl,
        WidgetSpotifyAuthorizationScope.LocalPlayback,
        WidgetSpotifyAuthorizationScope.PlaylistsRead,
    ];

    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private SpotifyWidgetViewState _viewState = SpotifyWidgetViewState.Initial;
    private WidgetSpotifyAuthorizationState _authorizationState =
        WidgetSpotifyAuthorizationState.Disconnected;
    private WidgetSpotifyPlaybackSummary? _playback;
    private SpotifyDestination _destination;
    private readonly WidgetCursorResource<SpotifyMediaCollectionItem> _queue;
    private readonly WidgetCursorResource<SpotifyPlaylistCollectionItem> _playlists;
    private readonly WidgetCursorResource<SpotifyMediaCollectionItem> _playlistItems;
    private readonly SpotifyMediaOccurrencePolicy _queueOccurrences =
        new(QueuePageSize * 2);
    private readonly SpotifyMediaOccurrencePolicy _playlistOccurrences =
        new(MaximumRetainedCollectionItems);
    private WidgetSpotifyDevicesSummary? _devices;
    private WidgetSpotifyLocalPlaybackSummary? _localPlayback;
    private string? _preferredPlaybackDeviceId;
    private DateTimeOffset? _devicesCachedAt;
    private bool _pageLoading;
    private string? _pageError;
    private string? _readyInitialFocusId = "spotify.play-toggle";
    private SpotifyPlaylistSelection? _playlistSelection;
    private long? _playlistItemsSelectionGeneration;
    private long _playlistSelectionGeneration;
    private long _presentationCaptureSequence;
    private WidgetSpotifyPlaybackOperation? _pendingOperation;
    private string _status = "Spotify loads when this widget becomes visible";
    private SpotifyRefreshWarning? _refreshWarning;
    private int _consecutiveRefreshFailures;
    private bool _showSetup;
    private long _setupViewGeneration;
    private long _activeGeneration;
    private Task? _pollTask;
    private Task? _progressTask;
    private readonly object _authorizationGate = new();
    private Task? _authorizationTask;

    public SpotifyWidget(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _queue = CreateCursorResource<SpotifyMediaCollectionItem>("spotify.queue", new()
        {
            PageSize = QueuePageSize,
            MaximumRetainedItems = QueuePageSize * 2,
            LoadPage = async (cursor, direction, limit, token) =>
            {
                if (cursor is not null || direction is not null)
                    throw new InvalidOperationException("Spotify queue does not expose adjacent cursors.");
                var occurrenceRequest = _queueOccurrences.BeginPage("queue", 0, direction);
                var queue = await HostServices.Spotify.GetQueueAsync(token).ConfigureAwait(false);
                var items = _queueOccurrences.NormalizePage(
                    occurrenceRequest, queue.Items, []);
                return new(items, null, null);
            },
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
            PageSize = CollectionPageSize,
            MaximumRetainedItems = MaximumRetainedCollectionItems,
            LoadPage = async (cursor, _, limit, token) =>
            {
                var offset = SpotifyCollectionIdentity.Offset(cursor);
                var page = await HostServices.Spotify.GetPlaylistsAsync(offset, limit, token)
                    .ConfigureAwait(false);
                var items = page.Items.Select(item => new SpotifyPlaylistCollectionItem(
                    item, SpotifyCollectionIdentity.Playlist(item.PlaylistId))).ToArray();
                return SpotifyCollectionIdentity.Page(items, page.Offset, page.Limit, page.Total);
            },
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.playlists.scroll.wide", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.item", "wide", item.Key),
                    "spotify.page.sparse.playlist.wide"),
                new("spotify.playlists.scroll.compact", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.item", "compact", item.Key),
                    "spotify.page.sparse.playlist.compact"),
            ],
        });
        _playlistItems = CreateCursorResource<SpotifyMediaCollectionItem>(
            "spotify.playlist.items", new()
        {
            PageSize = CollectionPageSize,
            MaximumRetainedItems = MaximumRetainedCollectionItems,
            LoadPage = LoadSelectedPlaylistCursorPageAsync,
            MapError = SpotifyResourceError,
            Viewports =
            [
                new("spotify.playlist.detail.scroll.wide", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.track", "wide", item.Key),
                    "spotify.playlist.play.wide"),
                new("spotify.playlist.detail.scroll.compact", item => item.Key,
                    item => SpotifyCollectionIdentity.FocusId(
                        "spotify.playlist.track", "compact", item.Key),
                    "spotify.playlist.play.compact"),
            ],
        });
    }

    public SpotifyWidgetViewState ViewState { get { lock (_gate) return _viewState; } }
    public string Status { get { lock (_gate) return _status; } }
    public WidgetSpotifyPlaybackSummary? Playback { get { lock (_gate) return _playback; } }
    public SpotifyDestination Destination { get { lock (_gate) return _destination; } }

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
            else if (_destination == SpotifyDestination.Queue)
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
        Task? authorization;
        lock (_authorizationGate) authorization = _authorizationTask;
        var tasks = new[] { authorization }
            .Where(task => task is not null).Cast<Task>().ToArray();
        if (tasks.Length == 0) return;
        try
        {
            await Task.WhenAll(tasks).WaitAsync(shutdownToken).ConfigureAwait(false);
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

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryHandlePageAction(action)) return;

        switch (action.ActionId)
        {
            case "spotify.setup.open":
                lock (_gate)
                {
                    _showSetup = true;
                    _setupViewGeneration++;
                }
                Invalidate();
                break;
            case "spotify.setup.close":
                lock (_gate) _showSetup = false;
                Invalidate();
                break;
            case "spotify.setup.done":
                lock (_gate) _showSetup = false;
                Invalidate();
                if (IsActive)
                    await RunCommandOperationAsync(CheckConfigurationAsync, cancellationToken)
                        .ConfigureAwait(false);
                break;
            case "spotify.connect":
            case "spotify.connect.features":
                StartAuthorization();
                break;
            case "spotify.disconnect":
                await RunCommandOperationAsync(DisconnectAsync, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "spotify.retry":
            case "spotify.refresh":
                await RunCommandOperationAsync(RefreshAsync, cancellationToken)
                    .ConfigureAwait(false);
                RefreshVisibleCollection();
                break;
            case "spotify.play-toggle":
                await RunCommandOperationAsync(token => ExecuteAsync(
                    ResolveToggleOperation(), null, token), cancellationToken).ConfigureAwait(false);
                break;
            case "spotify.previous":
                await RunCommandOperationAsync(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.Previous, null, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "spotify.next":
                await RunCommandOperationAsync(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.Next, null, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "spotify.shuffle":
                await RunCommandOperationAsync(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.SetShuffle, null, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "spotify.repeat":
                await RunCommandOperationAsync(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.SetRepeat, null, token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "spotify.seek":
                if (action.RequestedValue is { } requested && double.IsFinite(requested))
                    await RunCommandOperationAsync(token => ExecuteAsync(
                        WidgetSpotifyPlaybackOperation.Seek,
                        Math.Max(0, (long)Math.Round(requested)), token), cancellationToken)
                        .ConfigureAwait(false);
                break;
            case "spotify.nav.player":
                CancelPageOperation();
                Navigate(SpotifyDestination.Player, action.SourceElementId);
                break;
            case "spotify.playlist.back":
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
            case "spotify.page.noop":
                break;
            case "spotify.local.start":
                await RunCommandOperationAsync(token => ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), token), cancellationToken).ConfigureAwait(false);
                break;
            case "spotify.local.stop":
                await RunCommandOperationAsync(token => ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.Stop), token), cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                if (TryParseIndexedAction(action.ActionId, "spotify.device.select.",
                        out var deviceIndex))
                    await RunCommandOperationAsync(
                        token => SelectDeviceAsync(deviceIndex, token), cancellationToken)
                        .ConfigureAwait(false);
                else if (TryParseCollectionAction(action.ActionId, "spotify.queue.play.",
                             out var queueKey))
                    await RunCommandOperationAsync(
                        token => PlayQueueItemAsync(queueKey, token), cancellationToken)
                        .ConfigureAwait(false);
                else if (TryParseCollectionAction(action.ActionId, "spotify.playlist.track.",
                             out var trackKey))
                    await RunCommandOperationAsync(
                        token => PlayPlaylistTrackAsync(trackKey, token), cancellationToken)
                        .ConfigureAwait(false);
                else if (action.ActionId == "spotify.playlist.play")
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

    private bool TryHandlePageAction(WidgetActionEvent action)
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
        switch (action.ActionId)
        {
            case "spotify.nav.queue":
                Navigate(SpotifyDestination.Queue, action.SourceElementId);
                _queue.EnsureLoaded();
                return true;
            case "spotify.nav.playlists":
                Navigate(SpotifyDestination.Playlists, action.SourceElementId);
                _playlists.EnsureLoaded();
                return true;
            case "spotify.nav.devices":
                StartPageOperation(operation => NavigateAndLoadAsync(
                    SpotifyDestination.Devices, action.SourceElementId, operation));
                return true;
            case "spotify.page.retry":
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
                        _readyInitialFocusId = NavId(_destination, mode);
                    }
                }
                StartPageOperation(ReloadCurrentPageAsync);
                return true;
        }
        if (!TryParseCollectionAction(action.ActionId, "spotify.playlist.open.",
                out var playlistKey))
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

    private void RefreshVisibleCollection()
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
        if (destination == SpotifyDestination.Queue) _queue.Refresh();
        else if (destination == SpotifyDestination.Playlists)
        {
            if (detail) _playlistItems.Refresh();
            else _playlists.Refresh();
        }
    }

    private void InvalidateQueueCollection()
    {
        SpotifyDestination destination;
        lock (_gate) destination = _destination;
        if (destination == SpotifyDestination.Queue) _queue.Refresh();
        else _queue.Reset(invalidate: false);
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
                if (_authorizationState != WidgetSpotifyAuthorizationState.Connected) return;
            }
            try
            {
                var playback = await HostServices.Spotify.GetPlaybackAsync(cancellationToken)
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
            var configuration = await HostServices.Spotify.GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!configuration.IsConfigured)
            {
                ClearPageCaches();
                SetState(generation, SpotifyWidgetViewState.Unconfigured,
                    "Add your Spotify developer Client ID to continue", null);
                return;
            }

            var authorization = await HostServices.Spotify.GetAuthorizationAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            switch (authorization.State)
            {
                case WidgetSpotifyAuthorizationState.Unconfigured:
                    SetState(generation, SpotifyWidgetViewState.Unconfigured,
                        "Add your Spotify developer Client ID to continue", null);
                    return;
                case WidgetSpotifyAuthorizationState.Disconnected:
                case WidgetSpotifyAuthorizationState.ReauthorizationRequired:
                    ClearPageCaches();
                    SetState(generation, SpotifyWidgetViewState.Disconnected,
                        authorization.DisplayMessage ?? (authorization.State ==
                            WidgetSpotifyAuthorizationState.ReauthorizationRequired
                                ? "Spotify needs you to reconnect"
                                : "Connect your Spotify account when you are ready"), null);
                    return;
                case WidgetSpotifyAuthorizationState.Authorizing:
                    SetState(generation, SpotifyWidgetViewState.Authorizing,
                        "Finish signing in through your browser", null);
                    return;
            }

            var playback = await HostServices.Spotify.GetPlaybackAsync(cancellationToken)
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
            var authorization = await HostServices.Spotify.ConnectAsync(
                SpotifyScopes, cancellationToken).ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            if (authorization.State != WidgetSpotifyAuthorizationState.Connected)
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
        catch (WidgetCapabilityException exception)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                exception.ErrorCode is "permission_denied" or "capability_revoked"
                    ? SpotifyWidgetViewState.PermissionDenied
                    : SpotifyWidgetViewState.Disconnected,
                SafeMessage(exception, "Spotify connection was not completed"), null);
        }
        catch (WidgetCapabilityUnavailableException)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                SpotifyWidgetViewState.ServiceUnavailable, "Spotify provider unavailable", null);
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
            await HostServices.Spotify.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            ClearPageCaches();
            SetState(Volatile.Read(ref _activeGeneration), SpotifyWidgetViewState.Disconnected,
                "Disconnected from Spotify", null);
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(SafeMessage(exception, "Spotify could not disconnect"));
        }
    }

    private SpotifyPresentationState CapturePresentationState()
    {
        lock (_gate)
        {
            var playlists = _playlists.Snapshot;
            SpotifyPlaylistDetailPresentation? detail = null;
            if (_playlistSelection is { } selection &&
                _playlistItemsSelectionGeneration == selection.Key.Generation)
                detail = new(selection, _playlistItems.Snapshot);
            return new(
                new(++_presentationCaptureSequence, playlists.Revision,
                    detail?.Items.Revision ?? 0, detail?.Selection.Key.Generation),
                _viewState,
                ProjectPlayback(_playback),
                _pendingOperation,
                _status,
                _refreshWarning,
                _showSetup,
                _setupViewGeneration,
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

    private static bool TryParseCollectionAction(
        string action,
        string prefix,
        out WidgetCollectionItemKey key)
    {
        key = default;
        if (!action.StartsWith(prefix, StringComparison.Ordinal) ||
            action.Length == prefix.Length) return false;
        try
        {
            key = new(action[prefix.Length..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

}
