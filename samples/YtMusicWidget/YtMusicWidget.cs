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
    private const string ConnectAction = "connect";
    private const string PairAction = "pair";
    private const string RefreshAction = "refresh";
    private static readonly IReadOnlyList<WidgetQuickAction> ConnectedQuickActions =
    [
        new(ControllerButton.LeftBumper, "previous", "Previous track"),
        new(ControllerButton.X, "toggle-playback", "Play or pause"),
        new(ControllerButton.RightBumper, "next", "Next track"),
    ];
    private readonly IYtMusicClient _client;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly YtMusicUpdatePolicy _updatePolicy;
    private YtMusicWidgetConnectionState _connectionState;
    private YtMusicPlaybackSnapshot _snapshot = YtMusicPlaybackSnapshot.Empty;
    private long _snapshotTimestamp;
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
        _client = client ?? new YtmDesktopApiClient();
        _updatePolicy = updatePolicy ?? new YtMusicUpdatePolicy();
        _updatePolicy.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshotTimestamp = _timeProvider.GetTimestamp();
    }

    public YtMusicWidgetConnectionState ConnectionState
    {
        get { lock (_stateLock) return _connectionState; }
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
            return new WidgetView(UI.Stack("ytmusic-root", children.ToArray()).Classes("ytmusic-widget", "is-loading"));
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
                InitialFocusId: primaryId);
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
            UI.Stack("ytmusic-root",
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
                        .Shortcut(ControllerButton.LeftBumper).Classes("transport-action"),
                    UI.Button("", "toggle-playback", "play-pause")
                        .Icon(snapshot.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play, playLabel)
                        .FocusLeft("previous").FocusRight("next").FocusDown("like")
                        .Shortcut(ControllerButton.X).Classes("play-action", snapshot.IsPlaying ? "is-playing" : "is-paused"),
                    UI.Button("", "next", "next")
                        .Icon(WidgetGlyph.Next, "Next track")
                        .FocusLeft("play-pause").FocusRight("refresh").FocusDown("dislike")
                        .Shortcut(ControllerButton.RightBumper).Classes("transport-action"),
                    UI.Button("", RefreshAction, "refresh")
                        .Icon(WidgetGlyph.Refresh, "Refresh now playing")
                        .FocusLeft("next").FocusDown("repeat")
                        .Shortcut(ControllerButton.Y).Classes("refresh-action")),
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
                        .Icon(WidgetGlyph.Repeat, RepeatAccessibilityLabel(repeatMode))
                        .Selected(repeatMode != YtMusicRepeatMode.Off).Busy(pendingCommands.Contains(YtMusicCommand.Repeat))
                        .FocusUp("refresh").FocusLeft("dislike")
                        .Classes("secondary-action", repeatMode == YtMusicRepeatMode.Off ? "is-inactive" : "is-active",
                            $"repeat-{repeatMode.ToString().ToLowerInvariant()}")))
                .Classes("ytmusic-widget", "is-connected"),
            InitialFocusId: "play-pause",
            QuickActions: ConnectedQuickActions);
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
                var status = await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (status.AuthRequired)
                {
                    SetState(YtMusicWidgetConnectionState.Disconnected, "Connected · pairing required", pairingCode: null);
                    return;
                }
                await FetchConnectedSnapshotAsync(force: true, cancellationToken).ConfigureAwait(false);
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
                var pairing = await _client.RequestPairingCodeAsync(cancellationToken).ConfigureAwait(false);
                SetState(YtMusicWidgetConnectionState.Pairing, "Approve this code in YTMDesktop2", pairing.Code);
                await _client.CompletePairingAsync(pairing.Code, cancellationToken).ConfigureAwait(false);
                await FetchConnectedSnapshotAsync(force: true, cancellationToken).ConfigureAwait(false);
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
            SetError(exception);
        }
    }

    private async Task RunCommandAsync(
        YtMusicCommand command,
        string message,
        CancellationToken cancellationToken)
    {
        if (ConnectionState != YtMusicWidgetConnectionState.Connected) return;
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
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _client.SendCommandAsync(
                    command,
                    cancellationToken,
                    optimistic.ToggleState).ConfigureAwait(false);
                await FetchConnectedSnapshotAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            RollBackOptimisticState(optimistic, "Command canceled");
            throw;
        }
        catch (Exception exception)
        {
            RollBackOptimisticState(optimistic, SafeStatus(exception));
        }
    }

    private async Task FetchConnectedSnapshotAsync(bool force, CancellationToken cancellationToken)
    {
        var snapshot = await _client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
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
            _snapshot = snapshot;
            _snapshotTimestamp = now;
            _connectionState = YtMusicWidgetConnectionState.Connected;
            if (_pendingOptimistic.Count == 0)
                _status = snapshot.IsPlaying ? "Playing through YTMDesktop2" : "Connected to YTMDesktop2";
            _pairingCode = null;
        }
        Invalidate();
    }

    private PendingOptimisticState ApplyOptimisticState(YtMusicCommand command, string message)
    {
        var before = _snapshot;
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
        var now = _timeProvider.GetTimestamp();
        _snapshot = expected;
        _snapshotTimestamp = now;
        _status = message;
        var deadline = now + ToTimestampTicks(_updatePolicy.OptimisticConfirmationWindow);
        return new PendingOptimisticState(command, before, expected, deadline, toggleState);
    }

    private void RollBackOptimisticState(PendingOptimisticState optimistic, string message)
    {
        lock (_stateLock)
        {
            var index = _pendingOptimistic.FindIndex(candidate => ReferenceEquals(candidate, optimistic));
            if (index < 0) return;
            _pendingOptimistic.RemoveAt(index);
            _snapshot = RestorePreviousState(_snapshot, optimistic);
            _snapshotTimestamp = _timeProvider.GetTimestamp();
            _connectionState = YtMusicWidgetConnectionState.Connected;
            _status = message;
        }
        Invalidate();
    }

    private static bool Confirms(PendingOptimisticState pending, YtMusicPlaybackSnapshot authoritative) =>
        pending.Command switch
        {
            YtMusicCommand.Next or YtMusicCommand.Previous =>
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
        PendingOptimisticState pending) => pending.Command switch
        {
            YtMusicCommand.TogglePlayback => current with { IsPlaying = pending.Before.IsPlaying },
            YtMusicCommand.Previous or YtMusicCommand.Next => current with
            {
                PositionSeconds = pending.Before.PositionSeconds,
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
    {
        if (!snapshot.IsPlaying || snapshot.DurationSeconds <= 0) return snapshot;
        var elapsed = _timeProvider.GetElapsedTime(observedTimestamp, _timeProvider.GetTimestamp()).TotalSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0) return snapshot;
        return snapshot with
        {
            PositionSeconds = Math.Clamp(snapshot.PositionSeconds + elapsed, 0, snapshot.DurationSeconds),
        };
    }

    private long ToTimestampTicks(TimeSpan interval) =>
        (long)Math.Ceiling(interval.TotalSeconds * _timeProvider.TimestampFrequency);

    private void SetConnectedStatus(string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            changed = _connectionState != YtMusicWidgetConnectionState.Connected ||
                      !string.Equals(_status, message, StringComparison.Ordinal);
            _connectionState = YtMusicWidgetConnectionState.Connected;
            _status = message;
        }
        if (changed) Invalidate();
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

    private static string SafeStatus(Exception exception)
    {
        var message = exception.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
            ? "Authorization required · select Pair device"
            : exception.Message;
        if (message.Length > 500) message = message[..500];
        return message;
    }

    private sealed record PendingOptimisticState(
        YtMusicCommand Command,
        YtMusicPlaybackSnapshot Before,
        YtMusicPlaybackSnapshot Expected,
        long DeadlineTimestamp,
        bool? ToggleState);

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
