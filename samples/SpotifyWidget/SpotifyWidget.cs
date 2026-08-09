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
public sealed class SpotifyWidget : Widget
{
    private const string InputScope = "spotify.window";
    private const string SetupScope = "spotify.setup";
    private static readonly TimeSpan PlayingPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PausedPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ErrorPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetupRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueueCacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevicesCacheLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlaylistCacheLifetime = TimeSpan.FromMinutes(5);
    private const int CollectionPageSize = 12;
    private const int MaximumCachedCollectionPages = 6;
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
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private SpotifyWidgetViewState _viewState = SpotifyWidgetViewState.Initial;
    private WidgetSpotifyAuthorizationState _authorizationState =
        WidgetSpotifyAuthorizationState.Disconnected;
    private WidgetSpotifyPlaybackSummary? _playback;
    private SpotifyDestination _destination;
    private WidgetSpotifyQueueSummary? _queue;
    private WidgetSpotifyPlaylistPageSummary? _playlists;
    private WidgetSpotifyPlaylistItemsSummary? _playlistDetail;
    private readonly Dictionary<int, WidgetSpotifyPlaylistPageSummary> _playlistPages = [];
    private readonly Dictionary<int, WidgetSpotifyPlaylistItemsSummary> _playlistItemPages = [];
    private WidgetSpotifyDevicesSummary? _devices;
    private WidgetSpotifyLocalPlaybackSummary? _localPlayback;
    private string? _preferredPlaybackDeviceId;
    private DateTimeOffset? _queueCachedAt;
    private DateTimeOffset? _playlistsCachedAt;
    private DateTimeOffset? _devicesCachedAt;
    private bool _pageLoading;
    private string? _pageError;
    private string? _readyInitialFocusId = "spotify.play-toggle";
    private string? _playlistReturnFocusId;
    private WidgetSpotifyPlaylistSummary? _selectedPlaylist;
    private string? _selectedPlaylistMode;
    private WidgetSpotifyPlaybackOperation? _pendingOperation;
    private string _status = "Spotify loads when this widget becomes visible";
    private bool _showSetup;
    private long _setupViewGeneration;
    private long _activeGeneration;
    private Task? _pollTask;
    private Task? _progressTask;
    private readonly object _backgroundOperationGate = new();
    private CancellationTokenSource? _pageOperationCancellation;
    private Task? _pageOperationTask;
    private Task? _commandOperationTask;
    private long _pageOperationGeneration;
    private readonly object _authorizationGate = new();
    private Task? _authorizationTask;

    public SpotifyWidget(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public SpotifyWidgetViewState ViewState { get { lock (_gate) return _viewState; } }
    public string Status { get { lock (_gate) return _status; } }
    public WidgetSpotifyPlaybackSummary? Playback { get { lock (_gate) return _playback; } }
    public SpotifyDestination Destination { get { lock (_gate) return _destination; } }

    public override WidgetView Render()
    {
        SpotifyWidgetViewState state;
        WidgetSpotifyPlaybackSummary? playback;
        WidgetSpotifyPlaybackOperation? pending;
        string status;
        bool showSetup;
        SpotifyDestination destination;
        WidgetSpotifyQueueSummary? queue;
        WidgetSpotifyPlaylistPageSummary? playlists;
        WidgetSpotifyPlaylistItemsSummary? playlistDetail;
        WidgetSpotifyDevicesSummary? devices;
        WidgetSpotifyLocalPlaybackSummary? localPlayback;
        bool pageLoading;
        string? pageError;
        string? readyInitialFocusId;
        lock (_gate)
        {
            state = _viewState;
            playback = ProjectPlayback(_playback);
            pending = _pendingOperation;
            status = _status;
            showSetup = _showSetup;
            destination = _destination;
            queue = _queue;
            playlists = _playlists;
            playlistDetail = _playlistDetail;
            devices = _devices;
            localPlayback = _localPlayback;
            pageLoading = _pageLoading;
            pageError = _pageError;
            readyInitialFocusId = _readyInitialFocusId;
        }

        if (showSetup)
            return RenderSetup(status, Volatile.Read(ref _setupViewGeneration));
        var header = Header(status, state);
        return state switch
        {
            SpotifyWidgetViewState.Unconfigured => RenderUnconfigured(header),
            SpotifyWidgetViewState.Disconnected => RenderDisconnected(header, false),
            SpotifyWidgetViewState.Authorizing => RenderAuthorizing(header),
            SpotifyWidgetViewState.PermissionDenied => RenderPermissionDenied(header),
            SpotifyWidgetViewState.ServiceUnavailable => RenderError(
                header, "Spotify service unavailable",
                "The trusted Spotify provider is not available. Try again after the host recovers."),
            SpotifyWidgetViewState.Error => RenderError(
                header, "Spotify could not be loaded",
                "The provider returned an unexpected error. Retry without leaving the overlay."),
            SpotifyWidgetViewState.Ready when playback is { IsAvailable: true, Item: not null } =>
                RenderConnected(header, playback, pending, destination, queue, playlists,
                    playlistDetail, devices, localPlayback, pageLoading, pageError,
                    readyInitialFocusId),
            SpotifyWidgetViewState.Ready => RenderConnected(header, playback, pending,
                destination, queue, playlists, playlistDetail, devices, localPlayback,
                pageLoading, pageError, readyInitialFocusId),
            _ => RenderLoading(header),
        };
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
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Interlocked.Increment(ref _activeGeneration);
        Task? pageOperation;
        Task? commandOperation;
        lock (_backgroundOperationGate)
        {
            _pageOperationCancellation?.Cancel();
            pageOperation = _pageOperationTask;
            commandOperation = _commandOperationTask;
        }
        var tasks = new[] { _pollTask, _progressTask, pageOperation, commandOperation }
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
        Task? pageOperation;
        Task? commandOperation;
        lock (_backgroundOperationGate)
        {
            _pageOperationCancellation?.Cancel();
            pageOperation = _pageOperationTask;
            commandOperation = _commandOperationTask;
        }
        var tasks = new[] { authorization, pageOperation, commandOperation }
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

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryHandlePageAction(action)) return ValueTask.CompletedTask;

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
                if (IsActive) StartCommandOperation(CheckConfigurationAsync);
                break;
            case "spotify.connect":
            case "spotify.connect.features":
                StartAuthorization();
                break;
            case "spotify.disconnect":
                StartCommandOperation(DisconnectAsync);
                break;
            case "spotify.retry":
            case "spotify.refresh":
                StartCommandOperation(RefreshAsync);
                break;
            case "spotify.play-toggle":
                StartCommandOperation(token => ExecuteAsync(
                    ResolveToggleOperation(), null, token));
                break;
            case "spotify.previous":
                StartCommandOperation(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.Previous, null, token));
                break;
            case "spotify.next":
                StartCommandOperation(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.Next, null, token));
                break;
            case "spotify.shuffle":
                StartCommandOperation(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.SetShuffle, null, token));
                break;
            case "spotify.repeat":
                StartCommandOperation(token => ExecuteAsync(
                    WidgetSpotifyPlaybackOperation.SetRepeat, null, token));
                break;
            case "spotify.seek":
                if (action.RequestedValue is { } requested && double.IsFinite(requested))
                    StartCommandOperation(token => ExecuteAsync(
                        WidgetSpotifyPlaybackOperation.Seek,
                        Math.Max(0, (long)Math.Round(requested)), token));
                break;
            case "spotify.nav.player":
                CancelPageOperation();
                Navigate(SpotifyDestination.Player, action.SourceElementId);
                break;
            case "spotify.playlist.back":
                CancelPageOperation();
                lock (_gate)
                {
                    _playlistDetail = null;
                    _selectedPlaylist = null;
                    _selectedPlaylistMode = null;
                    _pageLoading = false;
                    _pageError = null;
                    _readyInitialFocusId = _playlistReturnFocusId;
                }
                Invalidate();
                break;
            case "spotify.local.start":
                StartCommandOperation(token => ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), token));
                break;
            case "spotify.local.stop":
                StartCommandOperation(token => ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.Stop), token));
                break;
            default:
                if (TryParseIndexedAction(action.ActionId, "spotify.device.select.",
                        out var deviceIndex))
                    StartCommandOperation(token => SelectDeviceAsync(deviceIndex, token));
                else if (TryParseIndexedAction(action.ActionId, "spotify.queue.play.",
                             out var queueIndex))
                    StartCommandOperation(token => PlayQueueItemAsync(queueIndex, token));
                else if (TryParseIndexedAction(action.ActionId, "spotify.playlist.track.",
                             out var trackIndex))
                    StartCommandOperation(token => PlayPlaylistTrackAsync(trackIndex, token));
                else if (action.ActionId == "spotify.playlist.play")
                    StartCommandOperation(PlayPlaylistAsync);
                break;
        }
        return ValueTask.CompletedTask;
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
        switch (action.ActionId)
        {
            case "spotify.nav.queue":
                StartPageOperation((generation, token) => NavigateAndLoadAsync(
                    SpotifyDestination.Queue, action.SourceElementId, generation, token));
                return true;
            case "spotify.nav.playlists":
                StartPageOperation((generation, token) => NavigateAndLoadAsync(
                    SpotifyDestination.Playlists, action.SourceElementId, generation, token));
                return true;
            case "spotify.nav.devices":
                StartPageOperation((generation, token) => NavigateAndLoadAsync(
                    SpotifyDestination.Devices, action.SourceElementId, generation, token));
                return true;
            case "spotify.page.retry":
                lock (_gate)
                {
                    if (_selectedPlaylist is null)
                    {
                        var mode = action.SourceElementId.Contains(".compact.",
                            StringComparison.Ordinal) ? "compact" : "wide";
                        _readyInitialFocusId = NavId(_destination, mode);
                    }
                }
                StartPageOperation(ReloadCurrentPageAsync);
                return true;
            case "spotify.playlists.previous":
                StartPageOperation((generation, token) => LoadPlaylistPageAsync(
                    -1, ModeFromSource(action.SourceElementId), generation, token),
                    replaceRunning: false);
                return true;
            case "spotify.playlists.more":
                StartPageOperation((generation, token) => LoadPlaylistPageAsync(
                    1, ModeFromSource(action.SourceElementId), generation, token),
                    replaceRunning: false);
                return true;
            case "spotify.playlist.previous":
                StartPageOperation((generation, token) => LoadPlaylistItemsPageAsync(
                    -1, ModeFromSource(action.SourceElementId), generation, token),
                    replaceRunning: false);
                return true;
            case "spotify.playlist.more":
                StartPageOperation((generation, token) => LoadPlaylistItemsPageAsync(
                    1, ModeFromSource(action.SourceElementId), generation, token),
                    replaceRunning: false);
                return true;
        }
        if (!TryParseIndexedAction(action.ActionId, "spotify.playlist.open.",
                out var playlistIndex))
            return false;
        StartPageOperation((generation, token) => OpenPlaylistAsync(
            playlistIndex, action.SourceElementId, generation, token));
        return true;
    }

    private void StartCommandOperation(Func<CancellationToken, Task> operation)
    {
        lock (_backgroundOperationGate)
        {
            if (_commandOperationTask is { IsCompleted: false }) return;
            _commandOperationTask = RunCommandOperationAsync(operation, ActiveCancellationToken);
        }
    }

    private async Task RunCommandOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _actionGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            SetCommandStatus("Spotify could not complete that action");
        }
    }

    private void StartPageOperation(
        Func<long, CancellationToken, Task> operation,
        bool replaceRunning = true)
    {
        lock (_backgroundOperationGate)
        {
            if (!replaceRunning && _pageOperationTask is { IsCompleted: false }) return;
            _pageOperationCancellation?.Cancel();
            _pageOperationCancellation?.Dispose();
            _pageOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                ActiveCancellationToken);
            var generation = Interlocked.Increment(ref _pageOperationGeneration);
            _pageOperationTask = RunPageOperationAsync(
                operation, generation, _pageOperationCancellation.Token);
        }
    }

    private static async Task RunPageOperationAsync(
        Func<long, CancellationToken, Task> operation,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void CancelPageOperation()
    {
        Interlocked.Increment(ref _pageOperationGeneration);
        lock (_backgroundOperationGate) _pageOperationCancellation?.Cancel();
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
            catch (WidgetCapabilityUnavailableException)
            {
                SetState(generation, SpotifyWidgetViewState.ServiceUnavailable,
                    "Spotify provider unavailable", null);
            }
            catch (WidgetCapabilityException exception)
            {
                var permission = exception.ErrorCode is "permission_denied" or
                    "capability_revoked";
                var disconnected = exception.ErrorCode is "authorization_expired" or
                    "authorization_scope_required";
                SetState(generation,
                    permission ? SpotifyWidgetViewState.PermissionDenied :
                    disconnected ? SpotifyWidgetViewState.Disconnected :
                    SpotifyWidgetViewState.Error,
                    SafeMessage(exception, permission ? "Spotify permission is off" :
                        disconnected ? "Spotify needs you to reconnect" :
                        "Spotify update failed"), disconnected ? null : Playback);
            }
            catch (Exception)
            {
                SetState(generation, SpotifyWidgetViewState.Error,
                    "Spotify update failed", Playback);
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
                lock (_gate) ClearPageCachesLocked();
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
                    lock (_gate) ClearPageCachesLocked();
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
        catch (WidgetCapabilityUnavailableException)
        {
            SetState(generation, SpotifyWidgetViewState.ServiceUnavailable,
                "Spotify provider unavailable", null);
        }
        catch (WidgetCapabilityException exception)
        {
            var permission = exception.ErrorCode is "permission_denied" or "capability_revoked";
            var disconnected = exception.ErrorCode is "authorization_expired" or
                "authorization_scope_required";
            SetState(generation,
                permission ? SpotifyWidgetViewState.PermissionDenied :
                disconnected ? SpotifyWidgetViewState.Disconnected : SpotifyWidgetViewState.Error,
                SafeMessage(exception, permission ? "Spotify permission is off" :
                    disconnected ? "Spotify needs you to reconnect" : "Spotify update failed"),
                disconnected ? null : Playback);
        }
        catch (Exception)
        {
            SetState(generation, SpotifyWidgetViewState.Error,
                "Spotify update failed", Playback);
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
            lock (_gate) ClearPageCachesLocked();
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
            lock (_gate) ClearPageCachesLocked();
            SetState(Volatile.Read(ref _activeGeneration), SpotifyWidgetViewState.Disconnected,
                "Disconnected from Spotify", null);
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(SafeMessage(exception, "Spotify could not disconnect"));
        }
    }

    private void Navigate(SpotifyDestination destination, string sourceElementId)
    {
        lock (_gate)
        {
            _destination = destination;
            _playlistDetail = null;
            _selectedPlaylist = null;
            _selectedPlaylistMode = null;
            _pageError = null;
            _pageLoading = false;
            _readyInitialFocusId = sourceElementId;
        }
        Invalidate();
    }

    private async Task NavigateAndLoadAsync(
        SpotifyDestination destination,
        string sourceElementId,
        long generation,
        CancellationToken cancellationToken)
    {
        var shouldLoad = false;
        lock (_gate)
        {
            _destination = destination;
            _playlistDetail = null;
            _selectedPlaylist = null;
            _selectedPlaylistMode = null;
            _pageError = null;
            _readyInitialFocusId = sourceElementId;
            shouldLoad = destination switch
            {
                SpotifyDestination.Queue => _queue is null ||
                    !IsFresh(_queueCachedAt, QueueCacheLifetime),
                SpotifyDestination.Playlists => _playlists is null ||
                    !IsFresh(_playlistsCachedAt, PlaylistCacheLifetime),
                SpotifyDestination.Devices => _devices is null || _localPlayback is null ||
                    !IsFresh(_devicesCachedAt, DevicesCacheLifetime),
                _ => false,
            };
            _pageLoading = shouldLoad;
        }
        Invalidate();
        if (shouldLoad) await LoadDestinationAsync(destination, generation, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task ReloadCurrentPageAsync(long generation, CancellationToken cancellationToken)
    {
        SpotifyDestination destination;
        WidgetSpotifyPlaylistSummary? selectedPlaylist;
        string selectedMode;
        lock (_gate)
        {
            destination = _destination;
            selectedPlaylist = _selectedPlaylist;
            selectedMode = _selectedPlaylistMode ?? "wide";
            _pageLoading = true;
            _pageError = null;
        }
        Invalidate();
        return destination == SpotifyDestination.Playlists && selectedPlaylist is not null
            ? LoadPlaylistDetailAsync(selectedPlaylist, selectedMode, generation,
                cancellationToken)
            : LoadDestinationAsync(destination, generation, cancellationToken);
    }

    private async Task LoadDestinationAsync(
        SpotifyDestination destination,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (destination)
            {
                case SpotifyDestination.Queue:
                    var queue = await HostServices.Spotify.GetQueueAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!IsPageOperationCurrent(generation)) return;
                    lock (_gate)
                    {
                        _queue = queue;
                        _queueCachedAt = _timeProvider.GetUtcNow();
                    }
                    break;
                case SpotifyDestination.Playlists:
                    var playlists = await HostServices.Spotify.GetPlaylistsAsync(
                        0, CollectionPageSize, cancellationToken)
                        .ConfigureAwait(false);
                    if (!IsPageOperationCurrent(generation)) return;
                    lock (_gate)
                    {
                        _playlists = playlists;
                        _playlistPages.Clear();
                        CachePage(_playlistPages, playlists.Offset, playlists);
                        _playlistsCachedAt = _timeProvider.GetUtcNow();
                    }
                    break;
                case SpotifyDestination.Devices:
                    var devicesTask = HostServices.Spotify.GetDevicesAsync(cancellationToken).AsTask();
                    var localTask = HostServices.Spotify.GetLocalPlaybackAsync(cancellationToken).AsTask();
                    await Task.WhenAll(devicesTask, localTask).ConfigureAwait(false);
                    if (!IsPageOperationCurrent(generation)) return;
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
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = "This Spotify feature is unavailable in the trusted host.";
            }
        }
        catch (WidgetCapabilityException exception)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = PageError(exception);
            }
        }
        catch (Exception)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = "Spotify could not load this page. Try again.";
            }
        }
        if (IsPageOperationCurrent(generation)) Invalidate();
    }

    private async Task OpenPlaylistAsync(
        int index,
        string sourceElementId,
        long generation,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistSummary? playlist;
        var mode = sourceElementId.Contains(".compact.", StringComparison.Ordinal)
            ? "compact" : "wide";
        lock (_gate)
        {
            playlist = ItemAt(_playlists?.Items, index);
            if (playlist is null) return;
            _playlistItemPages.Clear();
            _pageLoading = true;
            _pageError = null;
            _playlistReturnFocusId = sourceElementId;
            _selectedPlaylist = playlist;
            _selectedPlaylistMode = mode;
            _readyInitialFocusId = sourceElementId;
        }
        Invalidate();
        await LoadPlaylistDetailAsync(playlist, mode, generation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task LoadPlaylistDetailAsync(
        WidgetSpotifyPlaylistSummary playlist,
        string mode,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await HostServices.Spotify.GetPlaylistItemsAsync(
                playlist.PlaylistId, 0, CollectionPageSize,
                cancellationToken).ConfigureAwait(false);
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _playlistDetail = detail;
                _playlistItemPages.Clear();
                CachePage(_playlistItemPages, detail.Offset, detail);
                _pageLoading = false;
                _pageError = null;
                _readyInitialFocusId = $"spotify.playlist.play.{mode}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _playlistDetail = null;
                _pageLoading = false;
                _pageError = exception is WidgetCapabilityException capability
                    ? PageError(capability)
                    : "Spotify could not load this playlist. Try again.";
                _readyInitialFocusId = $"spotify.page.error.{mode}.action";
            }
        }
        if (IsPageOperationCurrent(generation)) Invalidate();
    }

    private async Task LoadPlaylistPageAsync(
        int direction,
        string mode,
        long generation,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistPageSummary? before;
        WidgetSpotifyPlaylistPageSummary? cached = null;
        int targetOffset;
        lock (_gate)
        {
            before = _playlists;
            if (before is null || _pageLoading) return;
            targetOffset = direction > 0
                ? before.Offset + Math.Max(1, before.Limit)
                : Math.Max(0, before.Offset - CollectionPageSize);
            if (targetOffset == before.Offset || targetOffset >= before.Total) return;
            if (_playlistPages.TryGetValue(targetOffset, out cached))
            {
                _playlists = cached;
                _readyInitialFocusId = PlaylistFocusId(cached, mode, direction);
                _pageError = null;
            }
            else
            {
                _pageLoading = true;
            }
        }
        Invalidate();
        if (cached is not null) return;
        try
        {
            var next = await HostServices.Spotify.GetPlaylistsAsync(targetOffset,
                CollectionPageSize, cancellationToken)
                .ConfigureAwait(false);
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _playlists = next;
                CachePage(_playlistPages, next.Offset, next);
                _playlistsCachedAt = _timeProvider.GetUtcNow();
                _pageLoading = false;
                _pageError = null;
                _readyInitialFocusId = PlaylistFocusId(next, mode, direction);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = exception is WidgetCapabilityException capability
                    ? PageError(capability)
                    : "Spotify could not load that playlist page.";
            }
        }
        if (IsPageOperationCurrent(generation)) Invalidate();
    }

    private async Task LoadPlaylistItemsPageAsync(
        int direction,
        string mode,
        long generation,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistItemsSummary? before;
        WidgetSpotifyPlaylistItemsSummary? cached = null;
        int targetOffset;
        lock (_gate)
        {
            before = _playlistDetail;
            if (before is null || _pageLoading) return;
            targetOffset = direction > 0
                ? before.Offset + Math.Max(1, before.Limit)
                : Math.Max(0, before.Offset - CollectionPageSize);
            if (targetOffset == before.Offset || targetOffset >= before.Total) return;
            if (_playlistItemPages.TryGetValue(targetOffset, out cached))
            {
                _playlistDetail = cached;
                _readyInitialFocusId = PlaylistTrackFocusId(cached, mode, direction);
                _pageError = null;
            }
            else
            {
                _pageLoading = true;
            }
        }
        Invalidate();
        if (cached is not null) return;
        try
        {
            var next = await HostServices.Spotify.GetPlaylistItemsAsync(
                before.Playlist.PlaylistId, targetOffset,
                CollectionPageSize, cancellationToken)
                .ConfigureAwait(false);
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _playlistDetail = next;
                CachePage(_playlistItemPages, next.Offset, next);
                _pageLoading = false;
                _pageError = null;
                _readyInitialFocusId = PlaylistTrackFocusId(next, mode, direction);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsPageOperationCurrent(generation)) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = exception is WidgetCapabilityException capability
                    ? PageError(capability)
                    : "Spotify could not load that playlist page.";
            }
        }
        if (IsPageOperationCurrent(generation)) Invalidate();
    }

    private bool IsFresh(DateTimeOffset? cachedAt, TimeSpan lifetime) =>
        cachedAt is { } value && _timeProvider.GetUtcNow() - value < lifetime;

    private static string ModeFromSource(string sourceElementId) =>
        sourceElementId.Contains(".compact.", StringComparison.Ordinal) ||
        sourceElementId.EndsWith(".compact", StringComparison.Ordinal)
            ? "compact"
            : "wide";

    private static string PlaylistFocusId(
        WidgetSpotifyPlaylistPageSummary page,
        string mode,
        int direction)
    {
        var localIndex = direction > 0 ? 0 : Math.Max(0, page.Items.Count - 1);
        return $"spotify.playlist.item.{mode}.{page.Offset + localIndex}";
    }

    private static string PlaylistTrackFocusId(
        WidgetSpotifyPlaylistItemsSummary page,
        string mode,
        int direction)
    {
        var localIndex = direction > 0 ? 0 : Math.Max(0, page.Items.Count - 1);
        return $"spotify.playlist.track.{mode}.{page.Offset + localIndex}";
    }

    private static void CachePage<T>(Dictionary<int, T> cache, int offset, T page)
    {
        cache[offset] = page;
        while (cache.Count > MaximumCachedCollectionPages)
            cache.Remove(cache.Keys.First());
    }

    private bool IsPageOperationCurrent(long generation) =>
        generation == Volatile.Read(ref _pageOperationGeneration);

    private async Task SelectDeviceAsync(int index, CancellationToken cancellationToken)
    {
        WidgetSpotifyDeviceSummary? device;
        lock (_gate) device = ItemAt(_devices?.Devices, index);
        if (device is null || device.IsRestricted) return;
        try
        {
            if (device.IsLocalHost)
            {
                await ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), cancellationToken).ConfigureAwait(false);
                return;
            }
            await HostServices.Spotify.TransferPlaybackAsync(
                device.DeviceId, true, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _preferredPlaybackDeviceId = device.DeviceId;
                _devices = MarkActiveDevice(_devices, device.DeviceId);
                _devicesCachedAt = _timeProvider.GetUtcNow();
                _status = $"Playing on {device.Name}";
            }
            Invalidate();
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(SafeMessage(exception, "Spotify could not switch devices"));
        }
    }

    private async Task ControlLocalPlaybackAsync(
        WidgetSpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) _pageLoading = true;
            Invalidate();
            var local = await HostServices.Spotify.ControlLocalPlaybackAsync(
                command, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _localPlayback = local;
                _pageLoading = false;
                _pageError = null;
                _status = local.DisplayMessage ?? "Local Spotify playback updated";
                var localDeviceId = _devices?.Devices
                    .FirstOrDefault(device => device.IsLocalHost)?.DeviceId;
                if (command.Operation == WidgetSpotifyLocalPlaybackOperation.Stop)
                {
                    if (_preferredPlaybackDeviceId == localDeviceId)
                        _preferredPlaybackDeviceId = null;
                    _devices = MarkActiveDevice(_devices, null);
                }
                else if (localDeviceId is not null)
                {
                    _preferredPlaybackDeviceId = localDeviceId;
                    _devices = MarkActiveDevice(_devices, localDeviceId);
                }
                _devicesCachedAt = _timeProvider.GetUtcNow();
            }
            Invalidate();
        }
        catch (WidgetCapabilityException exception)
        {
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
                _status = exception.ErrorCode switch
                {
                    "lifecycle_denied" =>
                        "Return focus to Devices, then try Play here again.",
                    "resource_not_found" =>
                        "Spotify could not activate this playback device.",
                    _ => SafeMessage(exception, "Local Spotify playback could not be updated"),
                };
            }
            Invalidate();
        }
    }

    private async Task PlayQueueItemAsync(int index, CancellationToken cancellationToken)
    {
        WidgetSpotifyMediaItemSummary? item;
        lock (_gate) item = ItemAt(_queue?.Items, index);
        if (item is null || !item.IsPlayable) return;
        await StartPlaybackAsync(new(null, [item.Uri], DeviceId: PlaybackDeviceId()),
            "Playing selected queue item",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistAsync(CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistItemsSummary? detail;
        lock (_gate) detail = _playlistDetail;
        if (detail is null) return;
        await StartPlaybackAsync(new(detail.Playlist.Uri, null,
                DeviceId: PlaybackDeviceId()),
            $"Playing {detail.Playlist.Name}", cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistTrackAsync(int index, CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistItemsSummary? detail;
        lock (_gate) detail = _playlistDetail;
        var item = ItemAt(detail?.Items, index);
        if (detail is null || item is null || !item.IsPlayable) return;
        await StartPlaybackAsync(new(detail.Playlist.Uri, null,
                DeviceId: PlaybackDeviceId(), OffsetUri: item.Uri),
            $"Playing {item.Title}", cancellationToken).ConfigureAwait(false);
    }

    private async Task StartPlaybackAsync(
        StartWidgetSpotifyPlaybackRequest request,
        string successStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await HostServices.Spotify.StartPlaybackAsync(request, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _queueCachedAt = null;
            SetCommandStatus(successStatus);
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(exception.ErrorCode == "resource_not_found"
                ? "No active Spotify device. Open Devices and choose where to play."
                : SafeMessage(exception, "Spotify could not start playback"));
        }
    }

    private string? PlaybackDeviceId()
    {
        lock (_gate) return _preferredPlaybackDeviceId;
    }

    private static WidgetSpotifyDevicesSummary? MarkActiveDevice(
        WidgetSpotifyDevicesSummary? devices,
        string? activeDeviceId) => devices is null ? null : devices with
    {
        Devices = devices.Devices.Select(device => device with
        {
            IsActive = activeDeviceId is not null &&
                string.Equals(device.DeviceId, activeDeviceId, StringComparison.Ordinal),
        }).ToArray(),
    };

    private static bool TryParseIndexedAction(string action, string prefix, out int index)
    {
        index = -1;
        return action.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(action.AsSpan(prefix.Length), out index) && index >= 0;
    }

    private static T? ItemAt<T>(IReadOnlyList<T>? items, int index) where T : class =>
        items is not null && index >= 0 && index < items.Count ? items[index] : null;

    private static string PageError(WidgetCapabilityException exception) => exception.ErrorCode switch
    {
        "permission_denied" or "capability_revoked" =>
            "This optional Spotify capability is disabled in widget settings.",
        "authorization_scope_required" =>
            "Reconnect Spotify to grant the scope required for this page.",
        "premium_required" =>
            "Spotify Premium is required for playback and device transfer.",
        _ => SafeMessage(exception, "Spotify could not load this page. Try again."),
    };

    private async Task ExecuteAsync(
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaybackSummary? before;
        WidgetSpotifyPlaybackCommand? command;
        lock (_gate)
        {
            before = _playback;
            command = BuildCommand(before, operation, requestedPosition);
            if (command is null || _pendingOperation is not null)
            {
                _status = "That Spotify control is not available";
                Invalidate();
                return;
            }
            _pendingOperation = operation;
            _playback = ApplyOptimistic(before!, command);
            _status = OperationStatus(operation);
        }
        Invalidate();
        try
        {
            await HostServices.Spotify.ControlPlaybackAsync(command, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _pendingOperation = null;
                _status = "Updated in Spotify";
                if (operation is WidgetSpotifyPlaybackOperation.Next or
                    WidgetSpotifyPlaybackOperation.Previous)
                    _queueCachedAt = null;
            }
            Invalidate();
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOptimistic(before);
        }
        catch (WidgetCapabilityException exception)
        {
            RestoreOptimistic(before, SafeMessage(exception,
                exception.ErrorCode is "permission_denied" or "capability_revoked"
                    ? "Playback control permission is off"
                    : "Spotify rejected that control"));
        }
        catch (WidgetCapabilityUnavailableException)
        {
            RestoreOptimistic(before, "Spotify playback control is unavailable");
        }
    }

    private void RestoreOptimistic(WidgetSpotifyPlaybackSummary? playback, string? status = null)
    {
        lock (_gate)
        {
            _playback = playback;
            _pendingOperation = null;
            if (status is not null) _status = status;
        }
        Invalidate();
    }

    private void ClearPageCachesLocked()
    {
        _queue = null;
        _playlists = null;
        _playlistDetail = null;
        _playlistPages.Clear();
        _playlistItemPages.Clear();
        _devices = null;
        _localPlayback = null;
        _queueCachedAt = null;
        _playlistsCachedAt = null;
        _devicesCachedAt = null;
        _preferredPlaybackDeviceId = null;
        _selectedPlaylist = null;
        _selectedPlaylistMode = null;
        _playlistReturnFocusId = null;
        _pageLoading = false;
        _pageError = null;
    }

    private WidgetSpotifyPlaybackOperation ResolveToggleOperation()
    {
        lock (_gate) return _playback?.IsPlaying == true
            ? WidgetSpotifyPlaybackOperation.Pause
            : WidgetSpotifyPlaybackOperation.Play;
    }

    private static WidgetSpotifyPlaybackCommand? BuildCommand(
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition)
    {
        if (playback is not { IsAvailable: true }) return null;
        var blocked = playback.DisallowedActions;
        return operation switch
        {
            WidgetSpotifyPlaybackOperation.Play when !blocked.Resuming => new(operation),
            WidgetSpotifyPlaybackOperation.Pause when !blocked.Pausing => new(operation),
            WidgetSpotifyPlaybackOperation.Next when !blocked.SkippingNext => new(operation),
            WidgetSpotifyPlaybackOperation.Previous when !blocked.SkippingPrevious => new(operation),
            WidgetSpotifyPlaybackOperation.Seek when !blocked.Seeking && requestedPosition is { } value =>
                new(operation, PositionMilliseconds: Math.Clamp(value, 0,
                    Math.Max(0, playback.DurationMilliseconds))),
            WidgetSpotifyPlaybackOperation.SetShuffle when !blocked.TogglingShuffle =>
                new(operation, Enabled: !playback.ShuffleState),
            WidgetSpotifyPlaybackOperation.SetRepeat => NextRepeatCommand(playback),
            _ => null,
        };
    }

    private static WidgetSpotifyPlaybackCommand? NextRepeatCommand(
        WidgetSpotifyPlaybackSummary playback)
    {
        var blocked = playback.DisallowedActions;
        var next = playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off when !blocked.TogglingRepeatContext =>
                WidgetSpotifyRepeatState.Context,
            WidgetSpotifyRepeatState.Context when !blocked.TogglingRepeatTrack =>
                WidgetSpotifyRepeatState.Track,
            WidgetSpotifyRepeatState.Track => WidgetSpotifyRepeatState.Off,
            _ => (WidgetSpotifyRepeatState?)null,
        };
        return next is null ? null : new(
            WidgetSpotifyPlaybackOperation.SetRepeat, RepeatState: next);
    }

    private WidgetSpotifyPlaybackSummary ApplyOptimistic(
        WidgetSpotifyPlaybackSummary playback,
        WidgetSpotifyPlaybackCommand command)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var projected = ProjectPlayback(playback)!;
        return command.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play => projected with
            {
                IsPlaying = true,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Pause => projected with
            {
                IsPlaying = false,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Seek => projected with
            {
                ProgressMilliseconds = command.PositionMilliseconds!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetShuffle => projected with
            {
                ShuffleState = command.Enabled!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetRepeat => projected with
            {
                RepeatState = command.RepeatState!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            _ => projected,
        };
    }

    private WidgetSpotifyPlaybackSummary? ProjectPlayback(WidgetSpotifyPlaybackSummary? playback)
    {
        if (playback is not { IsAvailable: true, IsPlaying: true } ||
            playback.DurationMilliseconds <= 0) return playback;
        var elapsed = Math.Max(0, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() -
            playback.CapturedAtUnixMilliseconds);
        return playback with
        {
            ProgressMilliseconds = Math.Clamp(playback.ProgressMilliseconds + elapsed,
                0, playback.DurationMilliseconds),
            CapturedAtUnixMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
        };
    }

    private void SetState(
        long generation,
        SpotifyWidgetViewState state,
        string status,
        WidgetSpotifyPlaybackSummary? playback)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        lock (_gate)
        {
            _viewState = state;
            _status = status;
            _playback = playback;
            _pendingOperation = null;
        }
        Invalidate();
    }

    private void SetCommandStatus(string status)
    {
        lock (_gate) _status = status;
        Invalidate();
    }

    private static string SafeMessage(Exception exception, string fallback)
    {
        var message = exception.Message?.Trim();
        return string.IsNullOrWhiteSpace(message) || message.Length > 160 ||
            message.Any(char.IsControl) ? fallback : message;
    }

    private static string OperationStatus(WidgetSpotifyPlaybackOperation operation) => operation switch
    {
        WidgetSpotifyPlaybackOperation.Play => "Resuming…",
        WidgetSpotifyPlaybackOperation.Pause => "Pausing…",
        WidgetSpotifyPlaybackOperation.Next => "Skipping forward…",
        WidgetSpotifyPlaybackOperation.Previous => "Going back…",
        WidgetSpotifyPlaybackOperation.Seek => "Seeking…",
        WidgetSpotifyPlaybackOperation.SetShuffle => "Updating shuffle…",
        WidgetSpotifyPlaybackOperation.SetRepeat => "Updating repeat…",
        _ => "Updating Spotify…",
    };

    private static StackElement Header(string status, SpotifyWidgetViewState state) =>
        UI.Stack("spotify.header",
                UI.Text("SPOTIFY", "spotify.eyebrow", "Spotify").Classes("spotify-eyebrow"),
                UI.Row("spotify.heading",
                        UI.Text("Music", "spotify.title", "Spotify music").Classes("spotify-title"),
                        UI.Text(status, "spotify.status", status).Classes("spotify-status",
                            state is SpotifyWidgetViewState.Error or
                                SpotifyWidgetViewState.PermissionDenied
                                ? "is-error" : state == SpotifyWidgetViewState.Ready
                                    ? "is-live" : "is-neutral"))
                    .Classes("spotify-heading"))
            .Classes("spotify-header");

    private static WidgetView RenderLoading(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Loading Spotify", "Checking your local account state…"))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderUnconfigured(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Client ID required",
                    "Add your own Spotify developer Client ID. No client secret belongs in this widget.",
                    UI.Button("Setup instructions", "spotify.setup.open", "spotify.setup.open")
                        .Icon(WidgetGlyph.Settings, "Open Spotify setup instructions")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.setup.open", Surface: StandardSurface);

    private static WidgetView RenderDisconnected(StackElement header, bool reconnect) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, reconnect ? "Reconnect Spotify" : "Connect Spotify",
                    "Spotify opens a browser and uses PKCE. Your credentials stay in the trusted host.",
                    UI.Row("spotify.connect-actions",
                            UI.Button(reconnect ? "Reconnect" : "Connect", "spotify.connect",
                                    "spotify.connect")
                                .Icon(WidgetGlyph.Play, "Connect Spotify account")
                                .FocusRight("spotify.setup.open")
                                .Classes("spotify-primary", "spotify-responsive-action"),
                            UI.Button("Setup", "spotify.setup.open", "spotify.setup.open")
                                .Icon(WidgetGlyph.Settings, "Open setup instructions")
                                .FocusLeft("spotify.connect")
                                .Classes("spotify-secondary", "spotify-responsive-action"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.connect", Surface: StandardSurface);

    private static WidgetView RenderAuthorizing(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Finish in your browser",
                    "The overlay is waiting for Spotify. Canceling or closing the browser will leave you disconnected."))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderPermissionDenied(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Spotify permission is off",
                    "Allow the declared Spotify capabilities in Settings, then retry.",
                    UI.Button("Retry", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Retry Spotify")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderError(StackElement header, string title, string detail) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, title, detail,
                    UI.Button("Try again", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Try Spotify again")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderIdle(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Nothing playing",
                    "Start playback in Spotify on one of your devices, then refresh here.",
                    UI.Row("spotify.idle-actions",
                            UI.Button("Refresh", "spotify.refresh", "spotify.refresh")
                                .Icon(WidgetGlyph.Refresh, "Refresh Spotify playback")
                                .FocusRight("spotify.disconnect")
                                .Classes("spotify-primary", "spotify-responsive-action"),
                            UI.Button("Disconnect", "spotify.disconnect", "spotify.disconnect")
                                .FocusLeft("spotify.refresh")
                                .Classes("spotify-secondary", "spotify-responsive-action"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.refresh", Surface: StandardSurface);

    private static WidgetView RenderSetup(string status, long setupViewGeneration)
    {
        var instructions = UI.Stack("spotify.setup-card",
                        UI.Text("Spotify setup", "spotify.setup-title",
                                "Spotify developer app setup")
                            .Classes("spotify-state-title", "spotify-setup-title"),
                        // Keep the only focus target at the top of the scroll
                        // surface. Placing it after the instructions makes the
                        // renderer correctly reveal that focused descendant,
                        // which opens the page already scrolled past its title.
                        UI.Button("Check configuration", "spotify.setup.done",
                                "spotify.setup.done")
                            .Classes("spotify-primary"),
                        UI.Text("1. Create an app in the Spotify developer dashboard.",
                                "spotify.setup-step-1").Classes("spotify-setup-step"),
                        UI.Text($"2. Add this exact redirect URI: {WidgetSpotifyService.ExactRedirectUri}",
                                "spotify.setup-step-2").Classes("spotify-setup-step"),
                        UI.Text("3. Save only the public Client ID; never enter a Client Secret.",
                                "spotify.setup-step-3").Classes("spotify-setup-step"),
                        UI.CodeText("dotnet run --project .\\tools\\GbarCli\\GbarCli.csproj -- config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples",
                                "spotify.setup-command", "Client ID configuration command")
                            .AddClasses("spotify-setup-command"))
                    .Classes("spotify-setup-card");
        var setupScroll = UI.VerticalScroll(
                $"spotify.setup-scroll.{setupViewGeneration}", instructions)
            .InputScope(SetupScope)
            .Shortcut(ControllerButton.B, "spotify.setup.close")
            .Classes("spotify-setup-scroll");
        var root = UI.Stack("spotify.setup-root",
                Header(status, SpotifyWidgetViewState.Unconfigured),
                setupScroll)
            .Classes("spotify-widget", "spotify-setup");
        return new WidgetView(root, "spotify.setup.done", ActiveInputScopeId: SetupScope,
            Surface: StandardSurface);
    }

    private static WidgetView RenderConnected(
        StackElement header,
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation? pending,
        SpotifyDestination destination,
        WidgetSpotifyQueueSummary? queue,
        WidgetSpotifyPlaylistPageSummary? playlists,
        WidgetSpotifyPlaylistItemsSummary? playlistDetail,
        WidgetSpotifyDevicesSummary? devices,
        WidgetSpotifyLocalPlaybackSummary? localPlayback,
        bool pageLoading,
        string? pageError,
        string? initialFocusId)
    {
        var wide = UI.Row("spotify.shell.wide",
                NavigationRail(destination, "wide"),
                UI.Stack("spotify.player.wide", PlayerPanel(playback, pending, "wide"))
                    .Classes("spotify-player-pane"),
                UI.Stack("spotify.context.wide",
                        DestinationPage(destination, queue, playlists, playlistDetail, devices,
                            localPlayback, pageLoading, pageError, "wide"))
                    .Classes("spotify-context-pane"))
            .Classes("spotify-shell", "spotify-shell-wide")
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly);
        var compact = UI.Stack("spotify.shell.compact",
                NavigationTabs(destination, "compact"),
                UI.Stack("spotify.context.compact",
                        destination == SpotifyDestination.Player
                            ? UI.VerticalScroll("spotify.player.compact.scroll",
                                    PlayerPanel(playback, pending, "compact"))
                                .Classes("spotify-compact-player-scroll")
                            : DestinationPage(destination, queue, playlists, playlistDetail,
                                devices, localPlayback, pageLoading, pageError, "compact"))
                    .Classes("spotify-compact-pane"))
            .Classes("spotify-shell", "spotify-shell-compact")
            .VisibleWhen(ResponsiveVisibility.CompactOnly);

        var root = UI.Stack("spotify.root", header, wide, compact)
            .InputScope(InputScope)
            .Classes("spotify-widget", "is-ready");
        if (playlistDetail is not null && destination == SpotifyDestination.Playlists)
            root = root.Shortcut(ControllerButton.B, "spotify.playlist.back");
        if (playback is { IsAvailable: true })
        {
            var disallowed = playback.DisallowedActions;
            var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
            if (!disallowed.SkippingPrevious)
                root = root.Shortcut(ControllerButton.LeftBumper, "spotify.previous");
            if (!toggleBlocked)
                root = root.Shortcut(ControllerButton.X, "spotify.play-toggle");
            if (!disallowed.SkippingNext)
                root = root.Shortcut(ControllerButton.RightBumper, "spotify.next");
        }
        root = root.Shortcut(ControllerButton.Y, "spotify.refresh");

        var quickActions = new List<WidgetQuickAction>();
        if (playback is { IsAvailable: true })
        {
            var blocked = playback.DisallowedActions;
            if (!blocked.SkippingPrevious)
                quickActions.Add(new(ControllerButton.LeftBumper, "spotify.previous",
                    "Previous track", PlaybackControlAuthority));
            if (!(playback.IsPlaying ? blocked.Pausing : blocked.Resuming))
                quickActions.Add(new(ControllerButton.X, "spotify.play-toggle",
                    playback.IsPlaying ? "Pause" : "Play", PlaybackControlAuthority));
            if (!blocked.SkippingNext)
                quickActions.Add(new(ControllerButton.RightBumper, "spotify.next",
                    "Next track", PlaybackControlAuthority));
        }
        var resolvedInitialFocus = initialFocusId == "spotify.play-toggle" &&
            playback is not { IsAvailable: true, Item: not null }
                ? "spotify.player.empty.wide.action"
                : initialFocusId;
        return new WidgetView(root, resolvedInitialFocus, quickActions,
            ActiveInputScopeId: InputScope, Surface: StandardSurface);
    }

    private static WidgetElement NavigationRail(SpotifyDestination selected, string mode) =>
        UI.Stack($"spotify.navigation.{mode}",
                NavigationButton(SpotifyDestination.Player, selected, mode, WidgetGlyph.Music),
                NavigationButton(SpotifyDestination.Queue, selected, mode, WidgetGlyph.Next),
                NavigationButton(SpotifyDestination.Playlists, selected, mode, WidgetGlyph.Music),
                NavigationButton(SpotifyDestination.Devices, selected, mode, WidgetGlyph.Connection))
            .Classes("spotify-navigation", "spotify-navigation-rail");

    private static WidgetElement NavigationTabs(SpotifyDestination selected, string mode) =>
        UI.SegmentedTabs($"spotify.navigation.{mode}", NavId(selected, mode),
                new(NavId(SpotifyDestination.Player, mode), "Player", "spotify.nav.player"),
                new(NavId(SpotifyDestination.Queue, mode), "Queue", "spotify.nav.queue"),
                new(NavId(SpotifyDestination.Playlists, mode), "Playlists", "spotify.nav.playlists"),
                new(NavId(SpotifyDestination.Devices, mode), "Devices", "spotify.nav.devices"))
            .Classes("spotify-navigation-tabs");

    private static ButtonElement NavigationButton(
        SpotifyDestination destination,
        SpotifyDestination selected,
        string mode,
        WidgetGlyph glyph) =>
        UI.Button(DestinationLabel(destination), $"spotify.nav.{DestinationToken(destination)}",
                NavId(destination, mode))
            .Icon(glyph, $"Open {DestinationLabel(destination)}")
            .Selected(destination == selected)
            .Classes("spotify-nav-button");

    private static WidgetElement PlayerPanel(
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation? pending,
        string mode)
    {
        if (playback is not { IsAvailable: true, Item: not null })
            return UI.EmptyState("Nothing playing",
                    "Choose a playlist or start Spotify on a device.",
                    $"spotify.player.empty.{mode}",
                    new ComponentAction("Refresh", "spotify.refresh", WidgetGlyph.Refresh),
                    WidgetGlyph.Music)
                .Classes("spotify-player-card");

        var item = playback.Item;
        var duration = Math.Max(1, playback.DurationMilliseconds);
        var position = Math.Clamp(playback.ProgressMilliseconds, 0, duration);
        var disallowed = playback.DisallowedActions;
        var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
        // Preserve the original wide-player IDs so focus restoration and
        // controller gestures survive the 0.2 navigation-shell upgrade.
        var legacy = mode == "wide";
        var prefix = legacy ? "spotify" : $"spotify.player.{mode}";
        var artworkId = legacy ? "spotify.artwork" : $"{prefix}.artwork";
        var placeholderId = legacy ? "spotify.artwork-placeholder" : $"{prefix}.artwork-placeholder";
        var frameId = legacy ? "spotify.artwork-frame" : $"{prefix}.artwork-frame";
        var detailsId = legacy ? "spotify.details" : $"{prefix}.details";
        var titleId = legacy ? "spotify.track-title" : $"{prefix}.title";
        var subtitleId = legacy ? "spotify.track-subtitle" : $"{prefix}.subtitle";
        var contextId = legacy ? "spotify.context" : $"{prefix}.context";
        var controlsId = legacy ? "spotify.primary-controls" : $"{prefix}.controls";
        var attributionId = legacy ? "spotify.attribution" : $"{prefix}.attribution";
        var previousId = $"{prefix}.previous";
        var toggleId = $"{prefix}.play-toggle";
        var nextId = $"{prefix}.next";
        var shuffleId = $"{prefix}.shuffle";
        var repeatId = $"{prefix}.repeat";
        var seekId = $"{prefix}.seek";
        var seekSliderId = $"{seekId}.slider";
        WidgetElement artwork = string.IsNullOrWhiteSpace(item.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, placeholderId, "No artwork")
                .Classes("spotify-artwork-placeholder")
            : UI.Image(item.ArtworkUrl, artworkId, $"Artwork for {item.Title}",
                    ImageFit.Cover)
                .Classes("spotify-artwork");
        var previous = UI.IconButton(WidgetGlyph.Previous, "spotify.previous",
                previousId, "Previous track", size: IconButtonSize.Medium)
            .Disabled(disallowed.SkippingPrevious)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Previous)
            .FocusLeft(shuffleId).FocusUp(seekSliderId).FocusRight(toggleId)
            .Classes("spotify-transport");
        var toggle = UI.IconButton(playback.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play,
                "spotify.play-toggle", toggleId,
                playback.IsPlaying ? "Pause" : "Play", IconButtonVariant.Primary,
                IconButtonSize.Large)
            .Disabled(toggleBlocked)
            .Busy(pending is WidgetSpotifyPlaybackOperation.Play or
                WidgetSpotifyPlaybackOperation.Pause)
            .FocusLeft(previousId).FocusUp(seekSliderId).FocusRight(nextId)
            .Classes("spotify-play");
        var next = UI.IconButton(WidgetGlyph.Next, "spotify.next", nextId,
                "Next track", size: IconButtonSize.Medium)
            .Disabled(disallowed.SkippingNext)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Next)
            .FocusLeft(toggleId).FocusUp(seekSliderId).FocusRight(repeatId)
            .Classes("spotify-transport");
        var shuffle = UI.IconButton(WidgetGlyph.Shuffle, "spotify.shuffle",
                shuffleId, "Toggle shuffle", size: IconButtonSize.Small)
            .Selected(playback.ShuffleState)
            .Disabled(disallowed.TogglingShuffle)
            .FocusUp(seekSliderId).FocusRight(previousId)
            .Classes("spotify-secondary-action");
        var repeat = UI.IconButton(WidgetGlyph.Repeat, "spotify.repeat",
                repeatId, $"Repeat {playback.RepeatState.ToString().ToLowerInvariant()}",
                size: IconButtonSize.Small)
            .Selected(playback.RepeatState != WidgetSpotifyRepeatState.Off)
            .Disabled(RepeatUnavailable(playback))
            .FocusUp(seekSliderId).FocusLeft(nextId)
            .Classes("spotify-secondary-action");
        var seek = UI.Scrubber(TimeSpan.FromMilliseconds(position),
                TimeSpan.FromMilliseconds(duration), TimeSpan.FromSeconds(5), "spotify.seek",
                seekId, "Spotify playback position")
            .Disabled(disallowed.Seeking)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Seek)
            .FocusDown(toggleId)
            .RequireControllerActivation()
            .AddClasses("spotify-scrubber");

        return UI.Stack($"{prefix}.card",
                UI.Stack(frameId, artwork).Classes("spotify-artwork-frame"),
                UI.Stack(detailsId,
                        UI.Text(item.Title, titleId, item.Title)
                            .Classes("spotify-track-title"),
                        UI.Text(item.Subtitle, subtitleId, item.Subtitle)
                            .Classes("spotify-track-subtitle"),
                        UI.Text(item.ContextName ?? "Spotify", contextId,
                                item.ContextName ?? "Spotify")
                            .Classes("spotify-context"))
                    .Classes("spotify-details"),
                seek,
                UI.Row(controlsId, shuffle, previous, toggle, next, repeat)
                    .Classes("spotify-primary-controls"),
                UI.Text(playback.Attribution, attributionId, "Powered by Spotify")
                    .Classes("spotify-attribution"))
            .Classes("spotify-player-card");
    }

    private static WidgetElement DestinationPage(
        SpotifyDestination destination,
        WidgetSpotifyQueueSummary? queue,
        WidgetSpotifyPlaylistPageSummary? playlists,
        WidgetSpotifyPlaylistItemsSummary? playlistDetail,
        WidgetSpotifyDevicesSummary? devices,
        WidgetSpotifyLocalPlaybackSummary? localPlayback,
        bool loading,
        string? error,
        string mode) => destination switch
        {
            SpotifyDestination.Player => PlayerOverview(mode),
            SpotifyDestination.Queue => QueuePage(queue, loading, error, mode),
            SpotifyDestination.Playlists => PlaylistsPage(playlists, playlistDetail,
                loading, error, mode),
            SpotifyDestination.Devices => DevicesPage(devices, localPlayback,
                loading, error, mode),
            _ => PlayerOverview(mode),
        };

    private static WidgetElement PlayerOverview(string mode) =>
        UI.Stack($"spotify.player-overview.{mode}",
                UI.SectionHeader("Now playing", $"spotify.player-overview.header.{mode}",
                    "SPOTIFY", "Playback stays available while you browse."),
                UI.Button("Refresh playback", "spotify.refresh", $"spotify.refresh.{mode}")
                    .Icon(WidgetGlyph.Refresh, "Refresh playback")
                    .Classes("spotify-page-action"),
                UI.Button("Disconnect account", "spotify.disconnect",
                        $"spotify.disconnect.{mode}")
                    .Classes("spotify-page-action", "is-quiet"))
            .Classes("spotify-page", "spotify-player-overview");

    private static WidgetElement QueuePage(
        WidgetSpotifyQueueSummary? queue, bool loading, string? error, string mode)
    {
        if (loading && queue is null) return LoadingPage("Loading queue", mode);
        if (error is not null) return PageFailure("Queue unavailable", error, mode);
        if (queue is null || queue.Items.Count == 0)
            return UI.EmptyState("Queue is empty", "Spotify has no upcoming items.",
                $"spotify.queue.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Next).Classes("spotify-page");
        var rows = queue.Items.Select((item, index) => MediaRow(item,
            $"spotify.queue.play.{index}", $"spotify.queue.item.{mode}.{index}"))
            .ToArray();
        return UI.Stack($"spotify.queue.page.{mode}",
                UI.SectionHeader("Up next", $"spotify.queue.header.{mode}", "QUEUE",
                    queue.IsTruncated ? "Showing Spotify's next items." :
                        $"{queue.Items.Count} upcoming items"),
                UI.VerticalScroll($"spotify.queue.scroll.{mode}", rows)
                    .Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistsPage(
        WidgetSpotifyPlaylistPageSummary? playlists,
        WidgetSpotifyPlaylistItemsSummary? detail,
        bool loading,
        string? error,
        string mode)
    {
        if (loading && detail is null && playlists is null)
            return LoadingPage("Loading playlists", mode);
        if (error is not null) return PageFailure("Playlists unavailable", error, mode);
        if (detail is not null) return PlaylistDetail(detail, loading, mode);
        if (playlists is null || playlists.Items.Count == 0)
            return UI.EmptyState("No playlists", "Your Spotify library has no playlists.",
                $"spotify.playlists.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Music).Classes("spotify-page");
        var rows = playlists.Items.Select((playlist, index) => UI.MediaTile(
                playlist.Name, $"{playlist.ItemCount} items", $"spotify.playlist.open.{index}",
                $"spotify.playlist.item.{mode}.{playlists.Offset + index}", playlist.OwnerName,
                playlist.Description, Artwork(playlist.ArtworkUrl, playlist.Name),
                $"Open playlist {playlist.Name}"))
            .Select(row => row.Classes("spotify-media-row"))
            .ToList<WidgetElement>();
        var first = playlists.Offset + 1;
        var last = playlists.Offset + playlists.Items.Count;
        var scroll = PaginatedCollectionScroll(
            $"spotify.playlists.scroll.{mode}", rows,
            playlists.Offset > 0 ? "spotify.playlists.previous" : null,
            last < playlists.Total ? "spotify.playlists.more" : null);
        return UI.Stack($"spotify.playlists.page.{mode}",
                UI.SectionHeader("Your playlists", $"spotify.playlists.header.{mode}",
                    "LIBRARY", $"{first}–{last} of {playlists.Total} playlists"),
                scroll.Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistDetail(
        WidgetSpotifyPlaylistItemsSummary detail, bool loading, string mode)
    {
        var rows = detail.Items.Select((item, index) => MediaRow(item,
            $"spotify.playlist.track.{index}",
            $"spotify.playlist.track.{mode}.{detail.Offset + index}"))
            .ToList<WidgetElement>();
        var last = detail.Offset + detail.Items.Count;
        var scroll = PaginatedCollectionScroll(
            $"spotify.playlist.detail.scroll.{mode}", rows,
            detail.Offset > 0 ? "spotify.playlist.previous" : null,
            last < detail.Total ? "spotify.playlist.more" : null);
        return UI.Stack($"spotify.playlist.detail.{mode}",
                UI.SectionHeader(detail.Playlist.Name, $"spotify.playlist.detail.header.{mode}",
                        "PLAYLIST", detail.Playlist.Description,
                        UI.Button("Play", "spotify.playlist.play",
                                $"spotify.playlist.play.{mode}")
                            .Icon(WidgetGlyph.Play, $"Play {detail.Playlist.Name}")
                            .Busy(loading).Classes("spotify-page-action",
                                "spotify-playlist-header-action"))
                    .Classes("spotify-playlist-header"),
                scroll.Classes("spotify-page-scroll"))
            .Classes("spotify-page", "spotify-playlist-detail");
    }

    private static ScrollElement PaginatedCollectionScroll(
        string id,
        IReadOnlyList<WidgetElement> rows,
        string? nearStartActionId,
        string? nearEndActionId)
    {
        var scroll = UI.VerticalScroll(id, rows.ToArray());
        return nearStartActionId is null && nearEndActionId is null
            ? scroll
            : scroll.Paginate(nearStartActionId, nearEndActionId, threshold: 1);
    }

    private static WidgetElement DevicesPage(
        WidgetSpotifyDevicesSummary? devices,
        WidgetSpotifyLocalPlaybackSummary? local,
        bool loading,
        string? error,
        string mode)
    {
        if (loading && devices is null) return LoadingPage("Loading devices", mode);
        if (error is not null) return PageFailure("Devices unavailable", error, mode);
        var rows = new List<WidgetElement>();
        if (local is not null)
        {
            var localActive = local.State == WidgetSpotifyLocalPlaybackState.Active;
            var localStarting = local.State == WidgetSpotifyLocalPlaybackState.Starting;
            var localNeedsReconnect = local.State ==
                WidgetSpotifyLocalPlaybackState.ReauthorizationRequired;
            rows.Add(UI.SettingsRow("This overlay",
                    new ComponentAction(localActive ? "Stop" : localStarting ? "Starting…" :
                        localNeedsReconnect ? "Reconnect" : "Play here",
                        localActive ? "spotify.local.stop" : localNeedsReconnect
                            ? "spotify.connect.features" : "spotify.local.start",
                        localActive ? WidgetGlyph.Pause : localNeedsReconnect
                            ? WidgetGlyph.Refresh : WidgetGlyph.Play),
                    $"spotify.local.{mode}",
                    local.DisplayMessage ?? "Web Playback SDK audio stays in the trusted host.",
                    local.DeviceName,
                    LocalStateLabel(local.State),
                    LocalStateTone(local.State),
                    isDisabled: local.State is WidgetSpotifyLocalPlaybackState.PremiumRequired or
                        WidgetSpotifyLocalPlaybackState.Unavailable || localStarting,
                    isBusy: loading || localStarting,
                    glyph: WidgetGlyph.Music).Classes("spotify-device-row"));
        }
        if (devices is not null)
        {
            rows.AddRange(devices.Devices.Select((device, index) => (device, index))
                .Where(entry => !entry.device.IsLocalHost)
                .Select(entry => UI.ChoiceRow(
                        entry.device.Name, $"spotify.device.select.{entry.index}",
                        $"spotify.device.{mode}.{entry.index}", entry.device.IsActive,
                        entry.device.IsRestricted, false, WidgetGlyph.Connection,
                        $"{entry.device.Name}, {entry.device.Type}" )
                    .Classes("spotify-device-row")));
        }
        if (rows.Count == 0)
            return UI.EmptyState("No Spotify devices", "Open Spotify on another device or play here.",
                $"spotify.devices.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Connection).Classes("spotify-page");
        return UI.Stack($"spotify.devices.page.{mode}",
                UI.SectionHeader("Playback devices", $"spotify.devices.header.{mode}",
                    "DEVICES", "Move playback without exposing Spotify device IDs."),
                UI.VerticalScroll($"spotify.devices.scroll.{mode}", rows.ToArray())
                    .Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static WidgetElement LoadingPage(string label, string mode) =>
        UI.Stack($"spotify.page.loading.{mode}",
                UI.LoadingIndicator($"spotify.page.loading.indicator.{mode}", label),
                UI.Text(label, $"spotify.page.loading.label.{mode}", label))
            .Classes("spotify-page", "spotify-page-loading");

    private static WidgetElement PageFailure(string title, string error, string mode) =>
        UI.Alert(title, error, AlertTone.Warning, $"spotify.page.error.{mode}",
                new ComponentAction(
                    error.Contains("Reconnect", StringComparison.OrdinalIgnoreCase)
                        ? "Reconnect" : "Try again",
                    error.Contains("Reconnect", StringComparison.OrdinalIgnoreCase)
                        ? "spotify.connect.features" : "spotify.page.retry",
                    WidgetGlyph.Refresh))
            .Classes("spotify-page", "spotify-page-error");

    private static ActionSurfaceElement MediaRow(
        WidgetSpotifyMediaItemSummary item,
        string action,
        string id) => UI.MediaTile(item.Title, FormatTime(item.DurationMilliseconds),
            action, id, item.Subtitle, null, Artwork(item.ArtworkUrl, item.Title),
            $"Play {item.Title} by {item.Subtitle}")
        .Disabled(!item.IsPlayable)
        .Classes("spotify-media-row");

    private static TileArtwork Artwork(string? url, string label) =>
        string.IsNullOrWhiteSpace(url)
            ? TileArtwork.FromGlyph(WidgetGlyph.Music, label)
            : TileArtwork.FromHttps(url, label);

    private static string LocalStateLabel(WidgetSpotifyLocalPlaybackState state) => state switch
    {
        WidgetSpotifyLocalPlaybackState.Active => "Playing here",
        WidgetSpotifyLocalPlaybackState.Ready => "Ready",
        WidgetSpotifyLocalPlaybackState.Starting => "Starting",
        WidgetSpotifyLocalPlaybackState.PremiumRequired => "Premium required",
        WidgetSpotifyLocalPlaybackState.ReauthorizationRequired => "Reconnect required",
        WidgetSpotifyLocalPlaybackState.Unavailable => "Unavailable",
        WidgetSpotifyLocalPlaybackState.Error => "Error",
        _ => "Stopped",
    };

    private static StatusTone LocalStateTone(WidgetSpotifyLocalPlaybackState state) => state switch
    {
        WidgetSpotifyLocalPlaybackState.Active or WidgetSpotifyLocalPlaybackState.Ready =>
            StatusTone.Success,
        WidgetSpotifyLocalPlaybackState.Starting => StatusTone.Info,
        WidgetSpotifyLocalPlaybackState.PremiumRequired or
            WidgetSpotifyLocalPlaybackState.ReauthorizationRequired => StatusTone.Warning,
        WidgetSpotifyLocalPlaybackState.Error => StatusTone.Danger,
        _ => StatusTone.Neutral,
    };

    private static string NavId(SpotifyDestination destination, string mode) =>
        $"spotify.nav.{mode}.{DestinationToken(destination)}";

    private static string DestinationToken(SpotifyDestination destination) =>
        destination.ToString().ToLowerInvariant();

    private static string DestinationLabel(SpotifyDestination destination) => destination switch
    {
        SpotifyDestination.Player => "Player",
        SpotifyDestination.Queue => "Queue",
        SpotifyDestination.Playlists => "Playlists",
        SpotifyDestination.Devices => "Devices",
        _ => "Spotify",
    };

    private static WidgetElement StateCard(
        WidgetGlyph glyph,
        string title,
        string detail,
        WidgetElement? action = null)
    {
        var children = new List<WidgetElement>
        {
            UI.Icon(glyph, "spotify.state-icon", title).Classes("spotify-state-icon"),
            UI.Text(title, "spotify.state-title", title).Classes("spotify-state-title"),
            UI.Text(detail, "spotify.state-detail", detail).Classes("spotify-state-detail"),
        };
        if (action is not null) children.Add(action);
        var card = UI.Stack("spotify.state-card", children.ToArray())
            .Classes("spotify-state-card");
        return UI.VerticalScroll("spotify.state-scroll", card)
            .Classes("spotify-state-scroll");
    }

    private static bool RepeatUnavailable(WidgetSpotifyPlaybackSummary playback) =>
        playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off => playback.DisallowedActions.TogglingRepeatContext,
            WidgetSpotifyRepeatState.Context => playback.DisallowedActions.TogglingRepeatTrack,
            _ => false,
        };

    public static string FormatTime(long milliseconds)
    {
        var totalSeconds = Math.Max(0, milliseconds / 1_000);
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }
}
