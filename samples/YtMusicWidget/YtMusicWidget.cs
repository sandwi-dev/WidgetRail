using System.Globalization;
using System.Diagnostics;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.YtMusicWidget;

public enum YtMusicWidgetConnectionState
{
    Disconnected,
    Connecting,
    Pairing,
    Connected,
    Error,
}

public class YtMusicWidget : Widget
{
    private const double ProgressDriftToleranceSeconds = 0.12;
    private const double SeekSnapThresholdSeconds = 3;
    private const double ForwardCorrectionRate = 0.35;
    private const string ConnectAction = "connect";
    private const string PairAction = "pair";
    private const string RefreshAction = "refresh";
    private const string TransportRefreshOperation = "ytmusic.transport-refresh";
    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 760,
        PreferredHeight = 440,
        MinimumWidth = 480,
        MinimumHeight = 340,
    };
    private static readonly IReadOnlyList<WidgetQuickAction> ConnectedQuickActions =
    [
        new(ControllerButton.LeftBumper, "previous", "Previous track", LoopbackControlAuthority()),
        new(ControllerButton.X, "toggle-playback", "Play or pause", LoopbackControlAuthority()),
        new(ControllerButton.RightBumper, "next", "Next track", LoopbackControlAuthority()),
    ];
    private IYtMusicClient? _client;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly YtMusicUpdatePolicy _updatePolicy;
    private YtMusicWidgetConnectionState _connectionState;
    private YtMusicPlaybackSnapshot _snapshot = YtMusicPlaybackSnapshot.Empty;
    private long _snapshotTimestamp;
    private double _pendingForwardCorrectionSeconds;
    private bool _hasProgressSnapshot;
    private readonly List<PendingOptimisticState> _pendingOptimistic = [];
    private string _status = "Connect to YTMDesktop2 to begin";
    private string? _pairingCode;
    private int _autoConnectStarted;
    private Task? _autoConnectTask;
    private Task? _progressLoop;
    private Task? _pollLoop;

    public YtMusicWidget(
        IYtMusicClient? client = null,
        YtMusicUpdatePolicy? updatePolicy = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _updatePolicy = updatePolicy ?? new YtMusicUpdatePolicy();
        _updatePolicy.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshotTimestamp = _timeProvider.GetTimestamp();
    }

    public YtMusicWidgetConnectionState ConnectionState
    {
        get { lock (_stateLock) return _connectionState; }
    }

    private static WidgetQuickActionCapability LoopbackControlAuthority() => new(
        WidgetLoopbackCapabilities.CapabilityId(YtmDesktopApiClient.CompanionPort),
        WidgetLoopbackCapabilities.PostJsonOperation);

    private IYtMusicClient Client
    {
        get
        {
            var current = Volatile.Read(ref _client);
            if (current is not null) return current;
            var created = new YtmDesktopApiClient(HostServices);
            var winner = Interlocked.CompareExchange(ref _client, created, null);
            if (winner is null) return created;
            created.Dispose();
            return winner;
        }
    }

    public override WidgetView Render()
    {
        YtMusicWidgetConnectionState connection;
        YtMusicPlaybackSnapshot snapshot;
        HashSet<YtMusicCommand> pendingCommands;
        string status;
        string? pairingCode;
        lock (_stateLock)
        {
            connection = _connectionState;
            snapshot = ProjectProgress(_snapshot, _snapshotTimestamp);
            pendingCommands = _pendingOptimistic.Select(item => item.Command).ToHashSet();
            status = _status;
            pairingCode = _pairingCode;
        }

        if (connection is YtMusicWidgetConnectionState.Connecting or YtMusicWidgetConnectionState.Pairing)
        {
            var children = new List<WidgetElement>
            {
                Header(status, connection),
                UI.Text(connection == YtMusicWidgetConnectionState.Pairing
                        ? "Approve the code in YTMDesktop2. The widget will continue automatically."
                        : "Checking the local companion API and loading now playing…",
                    "loading-detail", "YT Music connection in progress").Classes("loading-detail"),
            };
            if (!string.IsNullOrWhiteSpace(pairingCode))
                children.Add(UI.Text(pairingCode, "pairing-code", $"Pairing code {pairingCode}").Classes("pairing-code"));
            return new WidgetView(
                UI.Stack("ytmusic-root", children.ToArray()).Classes("ytmusic-widget", "is-loading"),
                Surface: StandardSurface);
        }

        if (connection != YtMusicWidgetConnectionState.Connected)
        {
            var primaryId = connection == YtMusicWidgetConnectionState.Error ? "retry" : "connect";
            var primaryLabel = connection == YtMusicWidgetConnectionState.Error ? "Retry" : "Connect";
            return new WidgetView(
                UI.Stack("ytmusic-root",
                    Header(status, connection),
                    UI.Text("YTMDesktop2 owns playback; this widget talks only to its loopback API.",
                        "connection-help", "Connection help").Classes("connection-help"),
                    UI.Row("connection-actions",
                        UI.Button(primaryLabel, ConnectAction, primaryId)
                            .FocusRight("pair")
                            .Classes("primary-action"),
                        UI.Button("Pair device", PairAction, "pair")
                            .FocusLeft(primaryId)
                            .Classes("connection-action"))
                    .Classes("connection-actions"))
                .Classes("ytmusic-widget", "is-disconnected"),
                InitialFocusId: primaryId,
                Surface: StandardSurface);
        }

        var duration = snapshot.DurationSeconds > 0 ? snapshot.DurationSeconds : 1;
        var position = Math.Clamp(snapshot.PositionSeconds, 0, duration);
        var playLabel = snapshot.IsPlaying ? "Pause" : "Play";
        var shuffleEnabled = snapshot.IsShuffleEnabled is true;
        var repeatMode = snapshot.RepeatMode ?? YtMusicRepeatMode.Off;
        var ratingBusy = pendingCommands.Contains(YtMusicCommand.Like) ||
                         pendingCommands.Contains(YtMusicCommand.Dislike);
        WidgetElement artwork = string.IsNullOrWhiteSpace(snapshot.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, "artwork-placeholder", "No album artwork")
                .Classes("artwork-placeholder")
            : UI.Image(snapshot.ArtworkUrl, "album-artwork", $"Album artwork for {snapshot.Title}", ImageFit.Cover)
                .Classes("artwork-image");
        return new WidgetView(
            UI.VerticalScroll("ytmusic-root",
                Header(status, connection),
                UI.Row("media-layout",
                    UI.Stack("artwork-frame", artwork).Classes("artwork-frame"),
                    UI.Stack("media-details",
                        UI.Stack("track-details",
                            UI.Text(snapshot.Title, "track-title", $"Track {snapshot.Title}").Classes("track-title"),
                            UI.Text(snapshot.Artist, "track-artist", $"Artist {snapshot.Artist}").Classes("track-artist"),
                            UI.Text(snapshot.Album, "track-album", string.IsNullOrWhiteSpace(snapshot.Album) ? "No album" : $"Album {snapshot.Album}")
                                .Classes("track-album")).Classes("track-details"),
                        UI.Row("progress-row",
                            UI.Text(FormatTime(position), "position-text", "Current playback position").Classes("time"),
                            UI.Progress(position, duration, "track-progress", $"{FormatTime(position)} of {FormatTime(snapshot.DurationSeconds)}")
                                .Classes("track-progress"),
                            UI.Text(FormatTime(snapshot.DurationSeconds), "duration-text", "Track duration").Classes("time"))
                            .Classes("progress-row"))
                        .Classes("media-details"))
                    .Classes("media-layout"),
                UI.Row("primary-actions",
                    UI.Button("", "previous", "previous")
                        .Icon(WidgetGlyph.Previous, "Previous track")
                        .FocusRight("play-pause").FocusDown("shuffle")
                        .Classes("transport-action"),
                    UI.Button("", "toggle-playback", "play-pause")
                        .Icon(snapshot.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play, playLabel)
                        .FocusLeft("previous").FocusRight("next").FocusDown("like")
                        .Classes("play-action", snapshot.IsPlaying ? "is-playing" : "is-paused"),
                    UI.Button("", "next", "next")
                        .Icon(WidgetGlyph.Next, "Next track")
                        .FocusLeft("play-pause").FocusRight("refresh").FocusDown("dislike")
                        .Classes("transport-action"),
                    UI.Button("", RefreshAction, "refresh")
                        .Icon(WidgetGlyph.Refresh, "Refresh now playing")
                        .FocusLeft("next").FocusDown("repeat")
                        .Classes("refresh-action")),
                UI.Row("secondary-actions",
                    UI.Button("", "shuffle", "shuffle")
                        .Icon(WidgetGlyph.Shuffle, shuffleEnabled ? "Turn shuffle off" : "Turn shuffle on")
                        .Selected(shuffleEnabled).Busy(pendingCommands.Contains(YtMusicCommand.Shuffle))
                        .FocusUp("previous").FocusRight("like")
                        .Classes("secondary-action", shuffleEnabled ? "is-active" : "is-inactive"),
                    UI.Button("", "like", "like")
                        .Icon(WidgetGlyph.Like, snapshot.IsLiked ? "Unlike track" : "Like track")
                        .Selected(snapshot.IsLiked).Busy(ratingBusy)
                        .FocusUp("play-pause").FocusLeft("shuffle").FocusRight("dislike")
                        .Classes("secondary-action", snapshot.IsLiked ? "is-active" : "is-inactive"),
                    UI.Button("", "dislike", "dislike")
                        .Icon(WidgetGlyph.Dislike, snapshot.IsDisliked ? "Remove dislike" : "Dislike track")
                        .Selected(snapshot.IsDisliked).Busy(ratingBusy)
                        .FocusUp("next").FocusLeft("like").FocusRight("repeat")
                        .Classes("secondary-action", snapshot.IsDisliked ? "is-active" : "is-inactive"),
                    UI.Button("", "repeat", "repeat")
                        .Icon(repeatMode == YtMusicRepeatMode.One
                                ? WidgetGlyph.RepeatOne
                                : WidgetGlyph.Repeat,
                            RepeatAccessibilityLabel(repeatMode))
                        .Selected(repeatMode != YtMusicRepeatMode.Off).Busy(pendingCommands.Contains(YtMusicCommand.Repeat))
                        .FocusUp("refresh").FocusLeft("dislike")
                        .Classes("secondary-action", repeatMode == YtMusicRepeatMode.Off ? "is-inactive" : "is-active",
                            $"repeat-{repeatMode.ToString().ToLowerInvariant()}")))
                .Shortcut(ControllerButton.LeftBumper, "previous")
                .Shortcut(ControllerButton.X, "toggle-playback")
                .Shortcut(ControllerButton.RightBumper, "next")
                .Shortcut(ControllerButton.Y, RefreshAction)
                .Classes("ytmusic-widget", "is-connected"),
            InitialFocusId: "play-pause",
            QuickActions: ConnectedQuickActions,
            Surface: StandardSurface);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        if (Interlocked.CompareExchange(ref _autoConnectStarted, 1, 0) == 0)
            _autoConnectTask = AutoConnectAsync(activeLifetime);
        _progressLoop = RunPeriodicUpdatesWhileActiveAsync(
            _updatePolicy.ProgressInterval,
            _ =>
            {
                YtMusicPlaybackSnapshot snapshot;
                lock (_stateLock) snapshot = _snapshot;
                if (snapshot.IsPlaying && ConnectionState == YtMusicWidgetConnectionState.Connected)
                    Invalidate();
                return ValueTask.CompletedTask;
            },
            invalidateAfterTick: false);
        _pollLoop = RunPeriodicUpdatesWhileActiveAsync(
            _updatePolicy.PollInterval,
            PollConnectedStateAsync,
            invalidateAfterTick: false);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        Trace.WriteLine($"YT Music widget lifecycle: {previous} -> {current}");
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        var tasks = new[] { _autoConnectTask, _progressLoop, _pollLoop }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        _autoConnectTask = null;
        _progressLoop = null;
        _pollLoop = null;
        if (tasks.Length != 0)
            await Task.WhenAll(tasks).WaitAsync(transitionToken).ConfigureAwait(false);
        if (ConnectionState != YtMusicWidgetConnectionState.Connected)
            Interlocked.Exchange(ref _autoConnectStarted, 0);
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        var client = Interlocked.Exchange(ref _client, null);
        (client as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task AutoConnectAsync(CancellationToken activeLifetime)
    {
        var entered = false;
        try
        {
            await _actionGate.WaitAsync(activeLifetime).ConfigureAwait(false);
            entered = true;
            if (ConnectionState == YtMusicWidgetConnectionState.Disconnected)
                await ConnectAsync(activeLifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation is the normal cancellation path.
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            if (entered) _actionGate.Release();
        }
    }

    private async ValueTask PollConnectedStateAsync(CancellationToken activeLifetime)
    {
        if (ConnectionState != YtMusicWidgetConnectionState.Connected) return;
        if (!await _clientGate.WaitAsync(0, activeLifetime).ConfigureAwait(false)) return;
        try
        {
            await FetchConnectedSnapshotAsync(force: false, activeLifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation is the normal cancellation path.
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            await SetAuthorizationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            SetConnectedStatus("Playback update delayed · retrying…");
        }
        finally
        {
            _clientGate.Release();
        }
    }

    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (action.ActionId)
            {
                case ConnectAction:
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case PairAction:
                    await PairAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case RefreshAction:
                    await RefreshAsync("Refreshing now playing…", cancellationToken).ConfigureAwait(false);
                    break;
                case "toggle-playback":
                    await RunCommandAsync(YtMusicCommand.TogglePlayback, "Toggling playback…", cancellationToken).ConfigureAwait(false);
                    break;
                case "previous":
                    await RunCommandAsync(YtMusicCommand.Previous, "Previous track…", cancellationToken).ConfigureAwait(false);
                    break;
                case "next":
                    await RunCommandAsync(YtMusicCommand.Next, "Next track…", cancellationToken).ConfigureAwait(false);
                    break;
                case "like":
                    await RunCommandAsync(YtMusicCommand.Like, "Updating like…", cancellationToken).ConfigureAwait(false);
                    break;
                case "dislike":
                    await RunCommandAsync(YtMusicCommand.Dislike, "Updating dislike…", cancellationToken).ConfigureAwait(false);
                    break;
                case "shuffle":
                    await RunCommandAsync(YtMusicCommand.Shuffle, "Toggling shuffle…", cancellationToken).ConfigureAwait(false);
                    break;
                case "repeat":
                    await RunCommandAsync(YtMusicCommand.Repeat, "Changing repeat mode…", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        SetState(YtMusicWidgetConnectionState.Connecting, "Connecting to YTMDesktop2…", pairingCode: null);
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var status = await Client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (status.AuthRequired && !status.HasCredential)
                {
                    SetState(YtMusicWidgetConnectionState.Disconnected, "Connected · pairing required", pairingCode: null);
                    return;
                }
                await FetchConnectedSnapshotAsync(
                    force: true,
                    cancellationToken,
                    establishConnection: true).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            SetState(YtMusicWidgetConnectionState.Disconnected, "Connection canceled", pairingCode: null);
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            await SetAuthorizationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetError(exception);
        }
    }

    private async Task PairAsync(CancellationToken cancellationToken)
    {
        SetState(YtMusicWidgetConnectionState.Pairing, "Requesting a pairing code…", pairingCode: null);
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pairing = await Client.RequestPairingCodeAsync(cancellationToken).ConfigureAwait(false);
                SetState(YtMusicWidgetConnectionState.Pairing, "Approve this code in YTMDesktop2", pairing.Code);
                await Client.CompletePairingAsync(pairing.Code, cancellationToken).ConfigureAwait(false);
                await FetchConnectedSnapshotAsync(
                    force: true,
                    cancellationToken,
                    establishConnection: true).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            SetState(YtMusicWidgetConnectionState.Disconnected, "Pairing canceled", pairingCode: null);
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            await SetAuthorizationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetError(exception);
        }
    }

    private async Task RefreshAsync(string message, CancellationToken cancellationToken)
    {
        if (ConnectionState != YtMusicWidgetConnectionState.Connected) return;
        SetConnectedStatus(message);
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FetchConnectedSnapshotAsync(force: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (exception is YtMusicAuthorizationRequiredException)
                await SetAuthorizationRequiredAsync().ConfigureAwait(false);
            else
                SetError(exception);
        }
    }

    private async Task RunCommandAsync(
        YtMusicCommand command,
        string message,
        CancellationToken cancellationToken)
    {
        if (ConnectionState != YtMusicWidgetConnectionState.Connected) return;
        var isTransport = command is
            YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next;
        PendingOptimisticState optimistic;
        lock (_stateLock)
        {
            optimistic = ApplyOptimisticState(command, message);
            _pendingOptimistic.RemoveAll(candidate => SameStateFeature(candidate.Command, command));
            _pendingOptimistic.Add(optimistic);
        }
        Invalidate();
        try
        {
            await Client.SendCommandAsync(
                command,
                cancellationToken,
                optimistic.ToggleState).ConfigureAwait(false);
            if (isTransport)
            {
                ScheduleTransportRefresh(command);
                return;
            }

            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FetchConnectedSnapshotAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
            finally { _clientGate.Release(); }
        }
        catch (OperationCanceledException)
        {
            RollBackOptimisticState(optimistic, "Command canceled");
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            RollBackOptimisticState(optimistic, "Authorization expired");
            await SetAuthorizationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RollBackOptimisticState(optimistic, SafeStatus(exception));
        }
    }

    private void ScheduleTransportRefresh(YtMusicCommand command)
    {
        // Once the companion accepts a playback command, reconciliation belongs
        // to the active widget lifecycle rather than the completed action
        // request. The public operation lane cancels stale work, bounds it to
        // one running plus one replacement, and drains it before deactivation.
        WidgetOperationHandle operation;
        lock (_stateLock)
        {
            if (_connectionState != YtMusicWidgetConnectionState.Connected) return;
            // Admission shares the state lock with attempt-local commits. A
            // replacement therefore cannot become current between an older
            // attempt's final currency check and its state mutation.
            operation = Operations.RunLatest(
                TransportRefreshOperation,
                context => RunTransportRefreshBurstAsync(command, context),
                WidgetOperationLifetime.Active);
        }
        if (!operation.IsAccepted)
            SetConnectedStatus("Track update will refresh when the widget is active");
    }

    private async ValueTask RunTransportRefreshBurstAsync(
        YtMusicCommand command,
        WidgetOperationContext context)
    {
        var cancellationToken = context.CancellationToken;
        try
        {
            for (var attempt = 0; attempt < _updatePolicy.TransportRefreshAttempts; attempt++)
            {
                var delay = _updatePolicy.TransportRefreshInitialDelay +
                            (_updatePolicy.TransportRefreshDelayStep * attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (!context.IsCurrent) return;

                try
                {
                    await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var applied = await FetchConnectedSnapshotAsync(
                            force: false,
                            cancellationToken,
                            context).ConfigureAwait(false);
                        if (!applied) return;
                    }
                    finally
                    {
                        _clientGate.Release();
                    }
                }
                catch (YtMusicAuthorizationRequiredException)
                {
                    TrySetAuthorizationRequired(context);
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    TrySetConnectedStatus(context, "Track update delayed · retrying…");
                    continue;
                }

                if (TransportTransitionResolved(command)) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer transport command or lifecycle exit superseded this burst.
        }
    }

    private bool TransportTransitionResolved(YtMusicCommand command)
    {
        lock (_stateLock)
        {
            // FetchConnectedSnapshotAsync removes the pending state only when
            // the unmerged companion snapshot confirms it or its bounded
            // confirmation window expires. Inspecting the rendered snapshot
            // here would mistake our optimistic value for server confirmation.
            if (_pendingOptimistic.Any(candidate =>
                    SameStateFeature(candidate.Command, command)))
                return false;
            return command == YtMusicCommand.TogglePlayback ||
                   _snapshot.HasCompleteMetadata && MetadataMatchesState(_snapshot);
        }
    }

    private async Task<bool> FetchConnectedSnapshotAsync(
        bool force,
        CancellationToken cancellationToken,
        WidgetOperationContext? operationContext = null,
        bool establishConnection = false)
    {
        var snapshot = await Client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (operationContext is { IsCurrent: false }) return false;
        lock (_stateLock)
        {
            if (operationContext is { IsCurrent: false }) return false;
            if (!establishConnection &&
                _connectionState != YtMusicWidgetConnectionState.Connected)
                return false;
            snapshot = PreserveStableMetadata(_snapshot, snapshot);
            snapshot = PreserveUnavailableToggleState(_snapshot, snapshot);
            var now = _timeProvider.GetTimestamp();
            if (force)
            {
                _pendingOptimistic.Clear();
            }
            else
            {
                for (var index = _pendingOptimistic.Count - 1; index >= 0; index--)
                {
                    var pending = _pendingOptimistic[index];
                    var withinConfirmationWindow =
                        _timeProvider.GetElapsedTime(now, pending.DeadlineTimestamp) > TimeSpan.Zero;
                    if (Confirms(pending, snapshot) || !withinConfirmationWindow)
                    {
                        _pendingOptimistic.RemoveAt(index);
                    }
                    else
                    {
                        snapshot = MergeExpectedState(snapshot, pending);
                    }
                }
            }
            snapshot = ReconcileProgress(snapshot, now, force);
            _snapshot = snapshot;
            _snapshotTimestamp = now;
            _connectionState = YtMusicWidgetConnectionState.Connected;
            if (_pendingOptimistic.Count == 0)
                _status = snapshot.IsPlaying ? "Playing through YTMDesktop2" : "Connected to YTMDesktop2";
            _pairingCode = null;
        }
        Invalidate();
        return true;
    }

    private PendingOptimisticState ApplyOptimisticState(YtMusicCommand command, string message)
    {
        var now = _timeProvider.GetTimestamp();
        var beforeProjection = ProjectProgressState(
            _snapshot,
            _snapshotTimestamp,
            _pendingForwardCorrectionSeconds,
            now);
        var before = beforeProjection.Snapshot;
        bool? toggleState = command switch
        {
            YtMusicCommand.Like => !before.IsLiked,
            YtMusicCommand.Dislike => !before.IsDisliked,
            _ => null,
        };
        var expected = command switch
        {
            YtMusicCommand.TogglePlayback => before with { IsPlaying = !before.IsPlaying },
            YtMusicCommand.Previous or YtMusicCommand.Next => before with { PositionSeconds = 0 },
            YtMusicCommand.Like => before with
            {
                IsLiked = toggleState!.Value,
                IsDisliked = toggleState.Value ? false : before.IsDisliked,
            },
            YtMusicCommand.Dislike => before with
            {
                IsLiked = toggleState!.Value ? false : before.IsLiked,
                IsDisliked = toggleState.Value,
            },
            YtMusicCommand.Shuffle => before with
            {
                IsShuffleEnabled = !(before.IsShuffleEnabled ?? false),
            },
            YtMusicCommand.Repeat => before with
            {
                RepeatMode = NextRepeatMode(before.RepeatMode ?? YtMusicRepeatMode.Off),
            },
            _ => before,
        };
        _snapshot = expected;
        _snapshotTimestamp = now;
        _pendingForwardCorrectionSeconds = command is
            YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next
                ? 0
                : beforeProjection.RemainingForwardCorrectionSeconds;
        _hasProgressSnapshot = true;
        _status = message;
        var confirmationWindow = command is YtMusicCommand.Previous or YtMusicCommand.Next
            ? _updatePolicy.TransportConfirmationWindow
            : _updatePolicy.OptimisticConfirmationWindow;
        var deadline = now + ToTimestampTicks(confirmationWindow);
        return new PendingOptimisticState(
            command,
            before,
            expected,
            deadline,
            toggleState,
            now,
            beforeProjection.RemainingForwardCorrectionSeconds);
    }

    private void RollBackOptimisticState(PendingOptimisticState optimistic, string message)
    {
        lock (_stateLock)
        {
            var index = _pendingOptimistic.FindIndex(candidate => ReferenceEquals(candidate, optimistic));
            if (index < 0) return;
            _pendingOptimistic.RemoveAt(index);
            var now = _timeProvider.GetTimestamp();
            var currentProjection = ProjectProgressState(
                _snapshot,
                _snapshotTimestamp,
                _pendingForwardCorrectionSeconds,
                now);
            var beforeProjection = ProjectProgressState(
                optimistic.Before,
                optimistic.StartedTimestamp,
                optimistic.BeforeForwardCorrectionSeconds,
                now);
            _snapshot = RestorePreviousState(
                currentProjection.Snapshot,
                optimistic,
                beforeProjection.Snapshot);
            _pendingForwardCorrectionSeconds = optimistic.Command is
                YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next
                    ? beforeProjection.RemainingForwardCorrectionSeconds
                    : currentProjection.RemainingForwardCorrectionSeconds;
            _snapshotTimestamp = now;
            _connectionState = YtMusicWidgetConnectionState.Connected;
            _status = message;
        }
        Invalidate();
    }

    private static bool Confirms(PendingOptimisticState pending, YtMusicPlaybackSnapshot authoritative) =>
        pending.Command switch
        {
            YtMusicCommand.Next =>
                !string.Equals(authoritative.TrackId, pending.Before.TrackId, StringComparison.Ordinal) ||
                !string.Equals(authoritative.Title, pending.Before.Title, StringComparison.Ordinal) ||
                !string.Equals(authoritative.Artist, pending.Before.Artist, StringComparison.Ordinal),
            YtMusicCommand.Previous =>
                authoritative.PositionSeconds <= 1.5 ||
                !string.Equals(authoritative.TrackId, pending.Before.TrackId, StringComparison.Ordinal) ||
                !string.Equals(authoritative.Title, pending.Before.Title, StringComparison.Ordinal) ||
                !string.Equals(authoritative.Artist, pending.Before.Artist, StringComparison.Ordinal),
            YtMusicCommand.TogglePlayback => authoritative.IsPlaying == pending.Expected.IsPlaying,
            YtMusicCommand.Like or YtMusicCommand.Dislike =>
                TrackChanged(pending.Before, authoritative) ||
                (authoritative.IsLiked == pending.Expected.IsLiked &&
                 authoritative.IsDisliked == pending.Expected.IsDisliked),
            YtMusicCommand.Shuffle =>
                authoritative.IsShuffleEnabled == pending.Expected.IsShuffleEnabled,
            YtMusicCommand.Repeat => authoritative.RepeatMode == pending.Expected.RepeatMode,
            _ => true,
        };

    private static YtMusicPlaybackSnapshot MergeExpectedState(
        YtMusicPlaybackSnapshot authoritative,
        PendingOptimisticState pending) => pending.Command switch
        {
            YtMusicCommand.TogglePlayback => authoritative with
            {
                IsPlaying = pending.Expected.IsPlaying,
            },
            YtMusicCommand.Previous or YtMusicCommand.Next => authoritative with
            {
                PositionSeconds = pending.Expected.PositionSeconds,
            },
            YtMusicCommand.Like or YtMusicCommand.Dislike => authoritative with
            {
                IsLiked = pending.Expected.IsLiked,
                IsDisliked = pending.Expected.IsDisliked,
            },
            YtMusicCommand.Shuffle => authoritative with
            {
                IsShuffleEnabled = pending.Expected.IsShuffleEnabled,
            },
            YtMusicCommand.Repeat => authoritative with
            {
                RepeatMode = pending.Expected.RepeatMode,
            },
            _ => authoritative,
        };

    private static YtMusicPlaybackSnapshot RestorePreviousState(
        YtMusicPlaybackSnapshot current,
        PendingOptimisticState pending,
        YtMusicPlaybackSnapshot projectedBefore) => pending.Command switch
        {
            YtMusicCommand.TogglePlayback => current with
            {
                IsPlaying = pending.Before.IsPlaying,
                PositionSeconds = projectedBefore.PositionSeconds,
            },
            YtMusicCommand.Previous or YtMusicCommand.Next => current with
            {
                PositionSeconds = projectedBefore.PositionSeconds,
            },
            YtMusicCommand.Like or YtMusicCommand.Dislike => current with
            {
                IsLiked = pending.Before.IsLiked,
                IsDisliked = pending.Before.IsDisliked,
            },
            YtMusicCommand.Shuffle => current with
            {
                IsShuffleEnabled = pending.Before.IsShuffleEnabled,
            },
            YtMusicCommand.Repeat => current with { RepeatMode = pending.Before.RepeatMode },
            _ => current,
        };

    private static bool SameStateFeature(YtMusicCommand left, YtMusicCommand right) =>
        left == right ||
        left is YtMusicCommand.Like or YtMusicCommand.Dislike &&
        right is YtMusicCommand.Like or YtMusicCommand.Dislike;

    private static bool TrackChanged(
        YtMusicPlaybackSnapshot before,
        YtMusicPlaybackSnapshot authoritative) =>
        !string.IsNullOrWhiteSpace(authoritative.TrackId) &&
        !string.IsNullOrWhiteSpace(before.TrackId) &&
        !string.Equals(authoritative.TrackId, before.TrackId, StringComparison.Ordinal);

    private static YtMusicPlaybackSnapshot PreserveUnavailableToggleState(
        YtMusicPlaybackSnapshot current,
        YtMusicPlaybackSnapshot authoritative) => authoritative with
        {
            IsShuffleEnabled = authoritative.IsShuffleEnabled ?? current.IsShuffleEnabled,
            RepeatMode = authoritative.RepeatMode ?? current.RepeatMode,
        };

    private static YtMusicPlaybackSnapshot PreserveStableMetadata(
        YtMusicPlaybackSnapshot current,
        YtMusicPlaybackSnapshot authoritative)
    {
        if (authoritative.HasCompleteMetadata && MetadataMatchesState(authoritative))
            return authoritative;

        if (!current.HasCompleteMetadata)
        {
            return authoritative with
            {
                Title = authoritative.IsPlaying ? "Loading track…" : "YouTube Music",
                Artist = authoritative.IsPlaying
                    ? "Waiting for YTMDesktop2 metadata"
                    : "No track loaded in YTMDesktop2",
                Album = string.Empty,
                ArtworkUrl = string.Empty,
                MetadataTrackId = string.Empty,
                HasCompleteMetadata = false,
            };
        }

        return authoritative with
        {
            Title = current.Title,
            Artist = current.Artist,
            Album = current.Album,
            ArtworkUrl = current.ArtworkUrl,
            MetadataTrackId = MetadataIdentity(current),
            HasCompleteMetadata = true,
        };
    }

    private static bool MetadataMatchesState(YtMusicPlaybackSnapshot snapshot)
    {
        if (!snapshot.HasCompleteMetadata) return false;
        var metadataTrackId = MetadataIdentity(snapshot);
        return string.IsNullOrWhiteSpace(snapshot.TrackId) ||
               string.IsNullOrWhiteSpace(metadataTrackId) ||
               string.Equals(snapshot.TrackId, metadataTrackId, StringComparison.Ordinal);
    }

    private static string MetadataIdentity(YtMusicPlaybackSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.MetadataTrackId)
            ? snapshot.TrackId
            : snapshot.MetadataTrackId;

    private YtMusicPlaybackSnapshot ReconcileProgress(
        YtMusicPlaybackSnapshot authoritative,
        long now,
        bool force)
    {
        authoritative = ClampProgress(authoritative);
        var currentProjection = ProjectProgressState(
            _snapshot,
            _snapshotTimestamp,
            _pendingForwardCorrectionSeconds,
            now);
        var current = currentProjection.Snapshot;
        var trackChanged = _hasProgressSnapshot &&
                           !string.IsNullOrWhiteSpace(current.TrackId) &&
                           !string.IsNullOrWhiteSpace(authoritative.TrackId) &&
                           !string.Equals(
                               current.TrackId,
                               authoritative.TrackId,
                               StringComparison.Ordinal);
        var drift = authoritative.PositionSeconds - current.PositionSeconds;
        var mustAnchor = force ||
                         !_hasProgressSnapshot ||
                         trackChanged ||
                         !authoritative.IsPlaying ||
                         !current.IsPlaying ||
                         Math.Abs(drift) >= SeekSnapThresholdSeconds;
        _hasProgressSnapshot = true;
        if (mustAnchor)
        {
            _pendingForwardCorrectionSeconds = 0;
            return authoritative;
        }

        if (drift > ProgressDriftToleranceSeconds)
        {
            _pendingForwardCorrectionSeconds = Math.Max(
                currentProjection.RemainingForwardCorrectionSeconds,
                drift);
        }
        else if (drift < -ProgressDriftToleranceSeconds)
        {
            // Small backward movement is normally the age of the HTTP snapshot,
            // not a user seek. Keep the monotonic presentation and drop any
            // forward correction that is no longer supported by the server.
            _pendingForwardCorrectionSeconds = 0;
        }
        else
        {
            _pendingForwardCorrectionSeconds =
                currentProjection.RemainingForwardCorrectionSeconds;
        }

        return authoritative with
        {
            PositionSeconds = Math.Clamp(
                current.PositionSeconds,
                0,
                Math.Max(0, authoritative.DurationSeconds)),
        };
    }

    private static YtMusicPlaybackSnapshot ClampProgress(YtMusicPlaybackSnapshot snapshot)
    {
        var duration = double.IsFinite(snapshot.DurationSeconds)
            ? Math.Max(0, snapshot.DurationSeconds)
            : 0;
        var position = double.IsFinite(snapshot.PositionSeconds)
            ? Math.Clamp(snapshot.PositionSeconds, 0, duration)
            : 0;
        return snapshot with
        {
            PositionSeconds = position,
            DurationSeconds = duration,
        };
    }

    private static YtMusicRepeatMode NextRepeatMode(YtMusicRepeatMode mode) => mode switch
    {
        YtMusicRepeatMode.Off => YtMusicRepeatMode.All,
        YtMusicRepeatMode.All => YtMusicRepeatMode.One,
        _ => YtMusicRepeatMode.Off,
    };

    private static string RepeatAccessibilityLabel(YtMusicRepeatMode mode) => mode switch
    {
        YtMusicRepeatMode.All => "Repeat all · change repeat mode",
        YtMusicRepeatMode.One => "Repeat one · change repeat mode",
        _ => "Repeat off · change repeat mode",
    };

    private YtMusicPlaybackSnapshot ProjectProgress(YtMusicPlaybackSnapshot snapshot, long observedTimestamp)
        => ProjectProgressState(
            snapshot,
            observedTimestamp,
            _pendingForwardCorrectionSeconds,
            _timeProvider.GetTimestamp()).Snapshot;

    private ProgressProjection ProjectProgressState(
        YtMusicPlaybackSnapshot snapshot,
        long observedTimestamp,
        double forwardCorrectionSeconds,
        long now)
    {
        snapshot = ClampProgress(snapshot);
        if (!snapshot.IsPlaying || snapshot.DurationSeconds <= 0)
            return new ProgressProjection(snapshot, 0);
        var elapsed = _timeProvider.GetElapsedTime(observedTimestamp, now).TotalSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0)
            return new ProgressProjection(snapshot, Math.Max(0, forwardCorrectionSeconds));
        var correction = Math.Min(
            Math.Max(0, forwardCorrectionSeconds),
            elapsed * ForwardCorrectionRate);
        var projected = snapshot with
        {
            PositionSeconds = Math.Clamp(
                snapshot.PositionSeconds + elapsed + correction,
                0,
                snapshot.DurationSeconds),
        };
        return new ProgressProjection(
            projected,
            Math.Max(0, forwardCorrectionSeconds - correction));
    }

    private long ToTimestampTicks(TimeSpan interval) =>
        (long)Math.Ceiling(interval.TotalSeconds * _timeProvider.TimestampFrequency);

    private void SetConnectedStatus(string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_connectionState != YtMusicWidgetConnectionState.Connected) return;
            changed = !string.Equals(_status, message, StringComparison.Ordinal);
            _status = message;
        }
        if (changed) Invalidate();
    }

    private bool TrySetConnectedStatus(WidgetOperationContext context, string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (!context.IsCurrent ||
                _connectionState != YtMusicWidgetConnectionState.Connected)
                return false;
            changed = !string.Equals(_status, message, StringComparison.Ordinal);
            _status = message;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TrySetAuthorizationRequired(WidgetOperationContext context)
    {
        lock (_stateLock)
        {
            if (!context.IsCurrent) return false;
            _pendingOptimistic.Clear();
            _connectionState = YtMusicWidgetConnectionState.Disconnected;
            _status = "Authorization expired · pair device";
            _pairingCode = null;
        }
        // This current delegate returns immediately, so canceling its own lane
        // is unnecessary. Avoiding a second lane-wide cancellation also keeps
        // a replacement admitted after this atomic commit from being revoked.
        Invalidate();
        return true;
    }

    private void SetState(YtMusicWidgetConnectionState state, string status, string? pairingCode)
    {
        lock (_stateLock)
        {
            _connectionState = state;
            _status = status;
            _pairingCode = pairingCode;
        }
        Invalidate();
    }

    private void SetError(Exception exception)
    {
        SetState(YtMusicWidgetConnectionState.Error, SafeStatus(exception), pairingCode: null);
    }

    private Task SetAuthorizationRequiredAsync()
    {
        Operations.Cancel(TransportRefreshOperation);
        lock (_stateLock) _pendingOptimistic.Clear();
        SetState(
            YtMusicWidgetConnectionState.Disconnected,
            "Authorization expired · pair device",
            pairingCode: null);
        return Task.CompletedTask;
    }

    private static string SafeStatus(Exception exception)
    {
        var message = exception switch
        {
            WidgetCapabilityUnavailableException =>
                "Overlay services are unavailable · reload the widget",
            WidgetCapabilityException capability => capability.ErrorCode switch
            {
                "permission_denied" or "capability_revoked" =>
                    "Local API access is blocked · review YT Music permissions",
                "loopback_timeout" =>
                    "YTMDesktop2 did not respond · try again",
                "loopback_unavailable" =>
                    "YTMDesktop2 is not running · start it and retry",
                "lifecycle_denied" =>
                    "YT Music control paused because the widget is no longer active",
                "capability_not_declared" or "invalid_declaration" or "unsupported_capability" =>
                    "This addon needs a compatible overlay version",
                "response_too_large" or "invalid_response" or "invalid_backend_data" or
                    "malformed_response" =>
                    "YTMDesktop2 returned an invalid response · try again",
                "platform_unavailable" =>
                    "Local companion access is unavailable · try again",
                _ => "YT Music request failed · try again",
            },
            YtMusicServiceException service => service.StatusCode switch
            {
                404 => "YTMDesktop2 API is unavailable · update or restart YTMDesktop2",
                408 or 504 => "YTMDesktop2 did not respond · try again",
                429 => "YTMDesktop2 is busy · try again shortly",
                >= 500 => "YTMDesktop2 reported an error · try again",
                _ => "YTMDesktop2 rejected the request · try again",
            },
            _ when exception.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) =>
                "Authorization required · select Pair device",
            _ => "YT Music request failed · try again",
        };
        if (message.Length > 500) message = message[..500];
        return message;
    }

    private sealed record PendingOptimisticState(
        YtMusicCommand Command,
        YtMusicPlaybackSnapshot Before,
        YtMusicPlaybackSnapshot Expected,
        long DeadlineTimestamp,
        bool? ToggleState,
        long StartedTimestamp,
        double BeforeForwardCorrectionSeconds);

    private sealed record ProgressProjection(
        YtMusicPlaybackSnapshot Snapshot,
        double RemainingForwardCorrectionSeconds);

    private static StackElement Header(string status, YtMusicWidgetConnectionState connection) =>
        UI.Stack("header",
            UI.Text("YT MUSIC", "widget-title", "YouTube Music").Classes("widget-title"),
            UI.Text(status, "connection-status", status).Classes(
                "connection-status",
                connection == YtMusicWidgetConnectionState.Connected ? "is-connected" : "is-disconnected"));

    public static string FormatTime(double seconds)
    {
        var safeSeconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var value = TimeSpan.FromSeconds(safeSeconds);
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
