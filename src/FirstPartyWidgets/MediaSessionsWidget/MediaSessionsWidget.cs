using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.MediaSessions;

public enum MediaSessionsViewState
{
    Initial,
    Loading,
    Ready,
    Empty,
    PermissionDenied,
    LifecycleDenied,
    ServiceUnavailable,
    ChannelClosed,
    Error,
}

/// <summary>
/// Compact controller-first Windows now-playing surface. It consumes only
/// sanitized GSMTC DTOs and interpolates progress locally between native events.
/// </summary>
public sealed class MediaSessionsWidget : Widget
{
    private static readonly WidgetQuickActionCapability DashboardMediaControl = new(
        WidgetMediaCapabilities.Control.CapabilityId,
        WidgetMediaCapabilities.Control.OperationId);
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 580,
        PreferredHeight = 400,
        MinimumWidth = 360,
        MinimumHeight = 330,
    };

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<WidgetMediaSession> _sessions = [];
    private IReadOnlyDictionary<string, string> _sessionByElement =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private string? _selectedSessionId;
    private WidgetMediaSessionCommand? _pendingCommand;
    private MediaSessionsViewState _viewState = MediaSessionsViewState.Initial;
    private string _status = "Media sessions load when this widget becomes visible";
    private CancellationTokenSource? _runLifetime;
    private Task? _progressLoop;
    private long _runGeneration;
    private long _snapshotRevision;
    private bool _liveUpdatesAvailable;
    private bool _reloadInFlight;

    public MediaSessionsWidget(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public MediaSessionsViewState ViewState { get { lock (_gate) return _viewState; } }
    public string? SelectedSessionId { get { lock (_gate) return _selectedSessionId; } }
    public IReadOnlyList<WidgetMediaSession> Sessions { get { lock (_gate) return _sessions.ToArray(); } }
    public string Status { get { lock (_gate) return _status; } }
    public bool LiveUpdatesAvailable { get { lock (_gate) return _liveUpdatesAvailable; } }

    public override WidgetView Render()
    {
        IReadOnlyList<WidgetMediaSession> sessions;
        string? selectedId;
        WidgetMediaSessionCommand? pending;
        MediaSessionsViewState state;
        string status;
        bool reloadInFlight;
        lock (_gate)
        {
            sessions = _sessions;
            selectedId = _selectedSessionId;
            pending = _pendingCommand;
            state = _viewState;
            status = _status;
            reloadInFlight = _reloadInFlight;
        }

        var header = UI.Stack("media.header",
                UI.Text("CONTROL CENTER", "media.eyebrow", "Control Center")
                    .Classes("media-eyebrow"),
                UI.Row("media.heading",
                        UI.Text("Now Playing", "media.title", "Windows media sessions")
                            .Classes("media-title"),
                        UI.Text(status, "media.status", status).Classes(
                            "media-status",
                            state == MediaSessionsViewState.Ready ? "is-live" :
                            state is MediaSessionsViewState.PermissionDenied or
                                MediaSessionsViewState.Error ? "is-error" : "is-neutral"))
                    .Classes("media-heading"))
            .Classes("media-header");

        if (state != MediaSessionsViewState.Ready || sessions.Count == 0)
            return RenderState(header, state, reloadInFlight);

        var selected = sessions.FirstOrDefault(item =>
            string.Equals(item.SessionId, selectedId, StringComparison.Ordinal)) ?? sessions[0];
        var elementMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var pillIds = sessions.Select(item => SessionElementId(item.SessionId)).ToArray();
        var pills = new WidgetElement[sessions.Count];
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var id = pillIds[index];
            elementMap[id] = session.SessionId;
            pills[index] = UI.Button(session.AppName, "media.select", id)
                .Icon(session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                    ? WidgetGlyph.Pause : WidgetGlyph.Music,
                    $"{session.AppName}. {session.Title}. Press A to select")
                .Selected(string.Equals(session.SessionId, selected.SessionId, StringComparison.Ordinal))
                .FocusLeft(pillIds[Math.Max(0, index - 1)])
                .FocusRight(pillIds[Math.Min(sessions.Count - 1, index + 1)])
                .FocusDown("media.play-toggle")
                .Classes("media-session-pill",
                    session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                        ? "is-playing" : "is-idle");
        }
        lock (_gate) _sessionByElement = elementMap;

        var position = ProjectPosition(selected);
        var duration = Math.Max(0, selected.DurationMilliseconds);
        var toggleEnabled = CanToggle(selected);
        var toggleLabel = selected.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
            ? "Pause" : "Play";
        var selectedPill = SessionElementId(selected.SessionId);
        var togglePending = pending is WidgetMediaSessionCommand.TogglePlayPause or
            WidgetMediaSessionCommand.Play or WidgetMediaSessionCommand.Pause;
        var previous = UI.Button("", "media.previous", "media.previous")
            .Icon(WidgetGlyph.Previous, selected.CanPrevious ? "Previous track" : "Previous unavailable")
            .Disabled(!selected.CanPrevious || pending == WidgetMediaSessionCommand.Previous)
            .Busy(pending == WidgetMediaSessionCommand.Previous)
            .FocusUp(selectedPill).FocusRight("media.play-toggle")
            .Classes("media-transport", "media-previous");
        var toggle = UI.Button("", "media.toggle", "media.play-toggle")
            .Icon(selected.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                ? WidgetGlyph.Pause : WidgetGlyph.Play,
                toggleEnabled ? toggleLabel : "Play or pause unavailable")
            .Disabled(!toggleEnabled || togglePending)
            .Busy(togglePending)
            .FocusUp(selectedPill).FocusLeft("media.previous").FocusRight("media.next")
            .Classes("media-play", selected.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                ? "is-playing" : "is-paused");
        var next = UI.Button("", "media.next", "media.next")
            .Icon(WidgetGlyph.Next, selected.CanNext ? "Next track" : "Next unavailable")
            .Disabled(!selected.CanNext || pending == WidgetMediaSessionCommand.Next)
            .Busy(pending == WidgetMediaSessionCommand.Next)
            .FocusUp(selectedPill).FocusLeft("media.play-toggle")
            .Classes("media-transport", "media-next");

        WidgetElement artwork = string.IsNullOrWhiteSpace(selected.ArtworkPngBase64)
            ? UI.Icon(WidgetGlyph.Music, "media.artwork-placeholder",
                    $"No artwork available for {selected.Title}")
                .Classes("media-artwork", "media-artwork-placeholder")
            : UI.InlinePngImage(
                    selected.ArtworkPngBase64,
                    "media.artwork",
                    $"Artwork for {selected.Title}",
                    ImageFit.Cover)
                .Classes("media-artwork", "media-artwork-image");

        var root = UI.Stack("media.root",
                header,
                UI.HorizontalScroll("media.session-scroll", pills).Classes("media-session-list"),
                UI.Row("media.now-playing",
                        artwork,
                        UI.Stack("media.track-details",
                                UI.Text(selected.Title, "media.track-title", $"Track {selected.Title}")
                                    .Classes("media-track-title"),
                                UI.Text(selected.Artist, "media.track-artist", $"Artist {selected.Artist}")
                                    .Classes("media-track-artist"),
                                UI.Row("media.timeline",
                                        UI.Text(FormatTime(position), "media.position", "Current position")
                                            .Classes("media-time"),
                                        UI.Progress(position, Math.Max(1, duration), "media.progress",
                                                $"{FormatTime(position)} of {FormatTime(duration)}")
                                            .Classes("media-progress"),
                                        UI.Text(FormatTime(duration), "media.duration", "Duration")
                                            .Classes("media-time", "is-duration"))
                                    .Classes("media-timeline"))
                            .Classes("media-track-details"))
                    .Classes("media-now-playing"),
                UI.Row("media.controls", previous, toggle, next).Classes("media-controls"))
            .InputScope("media-sessions")
            .Classes("media-sessions-widget");
        if (selected.CanPrevious) root = root.Shortcut(ControllerButton.LeftBumper, "media.previous");
        if (toggleEnabled) root = root.Shortcut(ControllerButton.X, "media.toggle");
        if (selected.CanNext) root = root.Shortcut(ControllerButton.RightBumper, "media.next");

        var quickActions = new List<WidgetQuickAction>();
        if (selected.CanPrevious)
            quickActions.Add(new(
                ControllerButton.LeftBumper, "media.previous", "Previous track",
                DashboardMediaControl));
        if (toggleEnabled)
            quickActions.Add(new(
                ControllerButton.X, "media.toggle", toggleLabel,
                DashboardMediaControl));
        if (selected.CanNext)
            quickActions.Add(new(
                ControllerButton.RightBumper, "media.next", "Next track",
                DashboardMediaControl));
        return new WidgetView(root, "media.play-toggle", QuickActions: quickActions,
            Surface: CompactSurface);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        StartActiveRun(activeLifetime);
        _progressLoop = RunPeriodicUpdatesWhileActiveAsync(
            TimeSpan.FromMilliseconds(250),
            _ =>
            {
                lock (_gate)
                {
                    var selected = SelectedLocked();
                    if (selected?.PlaybackStatus != WidgetMediaPlaybackStatus.Playing ||
                        selected.DurationMilliseconds <= 0) return ValueTask.CompletedTask;
                }
                Invalidate();
                return ValueTask.CompletedTask;
            },
            invalidateAfterTick: false);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        _progressLoop = null;
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        _progressLoop = null;
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
        if (action.ActionId == "media.retry")
        {
            if (IsActive) StartActiveRun(ActiveCancellationToken, rejectIfLoading: true);
            return;
        }
        if (action.ActionId == "media.select")
        {
            lock (_gate)
            {
                if (_sessionByElement.TryGetValue(action.SourceElementId, out var sessionId) &&
                    _sessions.Any(item => item.SessionId == sessionId))
                {
                    _selectedSessionId = sessionId;
                    _status = $"Selected {_sessions.First(item => item.SessionId == sessionId).AppName}";
                }
            }
            Invalidate();
            return;
        }

        var command = action.ActionId switch
        {
            "media.previous" => WidgetMediaSessionCommand.Previous,
            "media.next" => WidgetMediaSessionCommand.Next,
            "media.toggle" => ResolveToggleCommand(),
            _ => (WidgetMediaSessionCommand?)null,
        };
        if (command is null) return;
        await ExecuteCommandAsync(command.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteCommandAsync(
        WidgetMediaSessionCommand command, CancellationToken cancellationToken)
    {
        WidgetMediaSession? session;
        long generation;
        long snapshotRevision;
        var unavailable = false;
        var commandAlreadyPending = false;
        lock (_gate)
        {
            session = SelectedLocked();
            generation = _runGeneration;
            snapshotRevision = _snapshotRevision;
            if (_pendingCommand is not null)
            {
                commandAlreadyPending = true;
            }
            else if (session is null || !Supports(session, command))
            {
                _status = "That action is not available for this media session";
                unavailable = true;
            }
            else
            {
                _pendingCommand = command;
                _status = command switch
                {
                    WidgetMediaSessionCommand.Previous => "Going to previous track…",
                    WidgetMediaSessionCommand.Next => "Going to next track…",
                    WidgetMediaSessionCommand.Pause => "Pausing…",
                    _ => "Resuming playback…",
                };
            }
        }
        if (commandAlreadyPending) return;
        Invalidate();
        if (unavailable || session is null) return;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, ActiveCancellationToken);
            await HostServices.Media.ControlAsync(session.SessionId, command, linked.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_runGeneration != generation) return;
                _pendingCommand = null;
                _status = "Updated by Windows media controls";
                if (_snapshotRevision == snapshotRevision &&
                    command is WidgetMediaSessionCommand.Play or WidgetMediaSessionCommand.Pause or
                        WidgetMediaSessionCommand.TogglePlayPause)
                {
                    var projected = ProjectPosition(session);
                    var targetStatus = command switch
                    {
                        WidgetMediaSessionCommand.Play => WidgetMediaPlaybackStatus.Playing,
                        WidgetMediaSessionCommand.Pause => WidgetMediaPlaybackStatus.Paused,
                        _ => session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                            ? WidgetMediaPlaybackStatus.Paused
                            : WidgetMediaPlaybackStatus.Playing,
                    };
                    _sessions = _sessions.Select(item => item.SessionId == session.SessionId
                        ? item with
                        {
                            PlaybackStatus = targetStatus,
                            PositionMilliseconds = projected,
                            CapturedAtUnixMilliseconds = _timeProvider.GetUtcNow()
                                .ToUnixTimeMilliseconds(),
                        }
                        : item).ToArray();
                }
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (ActiveCancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetCommandError(generation, "Media control service unavailable");
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandError(generation, exception.ErrorCode switch
            {
                "permission_denied" or "capability_revoked" =>
                    "Media control permission is off",
                "lifecycle_denied" => "Enter the widget before controlling playback",
                "resource_not_found" => "That media session ended",
                "not_supported" => "That action is no longer supported",
                _ => "Windows rejected the media action",
            });
        }
        finally
        {
            var changed = false;
            lock (_gate)
            {
                if (_runGeneration == generation && _pendingCommand == command)
                {
                    _pendingCommand = null;
                    changed = true;
                }
            }
            if (changed) Invalidate();
        }
    }

    private void StartActiveRun(
        CancellationToken activeLifetime,
        bool rejectIfLoading = false)
    {
        if (rejectIfLoading)
        {
            lock (_gate)
            {
                // Controller repeats and stale rendered snapshots can deliver a
                // second activation before the loading snapshot reaches the host.
                // Do not cancel the fresh native request that is already running.
                if (_reloadInFlight) return;
                _reloadInFlight = true;
            }
        }
        var prior = Interlocked.Exchange(ref _runLifetime, null);
        prior?.Cancel();
        prior?.Dispose();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
        _runLifetime = lifetime;
        var generation = Interlocked.Increment(ref _runGeneration);
        lock (_gate)
        {
            _reloadInFlight = true;
            _viewState = MediaSessionsViewState.Loading;
            _status = "Loading Windows media sessions…";
            _liveUpdatesAvailable = false;
        }
        Invalidate();
        _ = ObserveAsync(generation, lifetime.Token);
    }

    private void StopActiveRun()
    {
        var lifetime = Interlocked.Exchange(ref _runLifetime, null);
        lifetime?.Cancel();
        lifetime?.Dispose();
        lock (_gate) _reloadInFlight = false;
    }

    private async Task ObserveAsync(long generation, CancellationToken cancellationToken)
    {
        IWidgetCapabilitySubscription<WidgetMediaSessionsChanged>? subscription = null;
        try
        {
            subscription = await HostServices.Media
                .OpenSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception subscriptionError)
        {
            // A current-state read and the live event channel are independent
            // transports. A transient subscription failure must not suppress a
            // valid Windows snapshot that is already available to the widget.
            await LoadSnapshotWithoutLiveUpdatesAsync(
                    generation, subscriptionError, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (subscription is null) return;
        await using (subscription.ConfigureAwait(false))
        {
            var hasCurrentSnapshot = false;
            try
            {
                Apply(await HostServices.Media.GetSessionsAsync(cancellationToken)
                    .ConfigureAwait(false), generation, liveUpdatesAvailable: true);
                hasCurrentSnapshot = true;
                await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                                   .WithCancellation(cancellationToken).ConfigureAwait(false))
                    Apply(change.Sessions, generation, liveUpdatesAvailable: true);
                SetLiveUpdateFailure(
                    new WidgetCapabilityException("channel_closed", "Media service disconnected."),
                    generation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (hasCurrentSnapshot)
                    SetLiveUpdateFailure(exception, generation);
                else
                    SetLoadFailure(exception, generation);
            }
        }
    }

    private async Task LoadSnapshotWithoutLiveUpdatesAsync(
        long generation,
        Exception subscriptionError,
        CancellationToken cancellationToken)
    {
        try
        {
            Apply(await HostServices.Media.GetSessionsAsync(cancellationToken).ConfigureAwait(false),
                generation, liveUpdatesAvailable: false);
            SetLiveUpdateFailure(subscriptionError, generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception readError) { SetLoadFailure(readError, generation); }
    }

    private void Apply(
        IReadOnlyList<WidgetMediaSession>? incoming,
        long generation,
        bool liveUpdatesAvailable = true)
    {
        var normalized = Normalize(incoming);
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _sessions = normalized;
            _snapshotRevision++;
            _liveUpdatesAvailable = liveUpdatesAvailable;
            if (_selectedSessionId is null || !normalized.Any(item => item.SessionId == _selectedSessionId))
                _selectedSessionId = normalized.FirstOrDefault(item => item.IsCurrent)?.SessionId ??
                    normalized.FirstOrDefault(item =>
                        item.PlaybackStatus == WidgetMediaPlaybackStatus.Playing)?.SessionId ??
                    normalized.FirstOrDefault()?.SessionId;
            _pendingCommand = null;
            _reloadInFlight = false;
            _viewState = normalized.Count == 0 ? MediaSessionsViewState.Empty : MediaSessionsViewState.Ready;
            _status = normalized.Count == 0 ? "No active Windows media sessions" :
                normalized.Count == 1 ? "1 active media session · live" :
                $"{normalized.Count} active media sessions · live";
        }
        Invalidate();
    }

    private static IReadOnlyList<WidgetMediaSession> Normalize(
        IReadOnlyList<WidgetMediaSession>? incoming)
    {
        if (incoming is null) return [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return incoming.Where(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.SessionId) && ids.Add(item.SessionId))
            .Take(32)
            .Select(NormalizeSession).ToArray();
    }

    private static WidgetMediaSession NormalizeSession(WidgetMediaSession item)
    {
        var duration = Math.Clamp(item.DurationMilliseconds, 0,
            (long)TimeSpan.FromDays(7).TotalMilliseconds);
        return item with
        {
            AppName = Clean(item.AppName, "Media app"),
            Title = Clean(item.Title, "Unknown title"),
            Artist = Clean(item.Artist, "Unknown artist"),
            DurationMilliseconds = duration,
            PositionMilliseconds = Math.Clamp(item.PositionMilliseconds, 0, duration),
            PlaybackRate = double.IsFinite(item.PlaybackRate)
                ? Math.Clamp(item.PlaybackRate, 0, 16) : 1,
        };
    }

    private static string Clean(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var clean = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return clean.Length switch { 0 => fallback, <= 160 => clean, _ => clean[..160] };
    }

    private long ProjectPosition(WidgetMediaSession session)
    {
        var position = session.PositionMilliseconds;
        if (session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing &&
            session.CapturedAtUnixMilliseconds > 0)
        {
            var elapsed = Math.Max(0,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - session.CapturedAtUnixMilliseconds);
            position += (long)Math.Round(elapsed * session.PlaybackRate);
        }
        return Math.Clamp(position, 0, Math.Max(0, session.DurationMilliseconds));
    }

    private WidgetMediaSessionCommand? ResolveToggleCommand()
    {
        lock (_gate)
        {
            var session = SelectedLocked();
            if (session is null) return null;
            if (session.CanTogglePlayPause) return WidgetMediaSessionCommand.TogglePlayPause;
            if (session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing && session.CanPause)
                return WidgetMediaSessionCommand.Pause;
            return session.CanPlay ? WidgetMediaSessionCommand.Play : null;
        }
    }

    private static bool CanToggle(WidgetMediaSession session) =>
        session.CanTogglePlayPause ||
        session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing && session.CanPause ||
        session.PlaybackStatus != WidgetMediaPlaybackStatus.Playing && session.CanPlay;

    private static bool Supports(WidgetMediaSession session, WidgetMediaSessionCommand command) =>
        command switch
        {
            WidgetMediaSessionCommand.Play => session.CanPlay,
            WidgetMediaSessionCommand.Pause => session.CanPause,
            WidgetMediaSessionCommand.TogglePlayPause => session.CanTogglePlayPause,
            WidgetMediaSessionCommand.Previous => session.CanPrevious,
            WidgetMediaSessionCommand.Next => session.CanNext,
            _ => false,
        };

    private WidgetMediaSession? SelectedLocked() => _sessions.FirstOrDefault(item =>
        string.Equals(item.SessionId, _selectedSessionId, StringComparison.Ordinal));

    private WidgetView RenderState(
        StackElement header,
        MediaSessionsViewState state,
        bool reloadInFlight)
    {
        var (title, help) = state switch
        {
            MediaSessionsViewState.Initial or MediaSessionsViewState.Loading =>
                ("Listening for media", "Windows-compatible players will appear automatically."),
            MediaSessionsViewState.Empty =>
                ("Nothing is playing", "Start media in an app that supports Windows media controls."),
            MediaSessionsViewState.PermissionDenied =>
                ("Media access is off", "Allow Now Playing in Settings > Permissions."),
            MediaSessionsViewState.LifecycleDenied =>
                ("Media access paused", "Return to this widget to resume live media updates."),
            MediaSessionsViewState.ServiceUnavailable =>
                ("Windows media controls unavailable", "This Windows session did not expose GSMTC."),
            MediaSessionsViewState.ChannelClosed =>
                ("Media service disconnected", "Retry the trusted Windows media service."),
            _ => ("Media unavailable", "Try again; no app identity or process details were exposed."),
        };
        var retry = UI.Button(reloadInFlight ? "Loading…" : "Try again", "media.retry", "media.retry")
            .Icon(WidgetGlyph.Refresh, "Reload media sessions")
            .Disabled(!IsActive || reloadInFlight)
            .Busy(reloadInFlight)
            .Classes("media-retry");
        var root = UI.Stack("media.root", header,
                UI.Stack("media.state",
                        UI.Icon(WidgetGlyph.Music, "media.state.icon", "Media")
                            .Classes("media-state-icon"),
                        UI.Text(title, "media.state.title", title).Classes("media-state-title"),
                        UI.Text(help, "media.state.help", help).Classes("media-state-help"),
                        retry)
                    .Classes("media-state-card"))
            .InputScope("media-sessions")
            .Classes("media-sessions-widget", "has-state");
        return new WidgetView(root, "media.retry", Surface: CompactSurface);
    }

    private void SetError(MediaSessionsViewState state, string status, long generation)
    {
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _viewState = state;
            _status = status;
            _sessions = [];
            _pendingCommand = null;
            _liveUpdatesAvailable = false;
            _reloadInFlight = false;
        }
        Invalidate();
    }

    private void SetLoadFailure(Exception exception, long generation)
    {
        var (state, status) = ClassifyFailure(exception, liveUpdatesOnly: false);
        SetError(state, status, generation);
    }

    private void SetLiveUpdateFailure(Exception exception, long generation)
    {
        var (_, status) = ClassifyFailure(exception, liveUpdatesOnly: true);
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _liveUpdatesAvailable = false;
            _status = status;
        }
        Invalidate();
    }

    private static (MediaSessionsViewState State, string Status) ClassifyFailure(
        Exception exception,
        bool liveUpdatesOnly)
    {
        if (exception is WidgetCapabilityUnavailableException)
            return (MediaSessionsViewState.ServiceUnavailable,
                liveUpdatesOnly ? "Current media shown · live updates unavailable" :
                    "Media service unavailable");
        if (exception is not WidgetCapabilityException capability)
            return (MediaSessionsViewState.Error,
                liveUpdatesOnly ? "Current media shown · live updates unavailable" :
                    "Media sessions could not be loaded");
        return capability.ErrorCode switch
        {
            "permission_denied" or "capability_not_declared" or "capability_revoked" =>
                (MediaSessionsViewState.PermissionDenied,
                    liveUpdatesOnly ? "Current media shown · live update access is off" :
                        "Media access is off"),
            "lifecycle_denied" => (MediaSessionsViewState.LifecycleDenied,
                liveUpdatesOnly ? "Current media shown · live updates are paused" :
                    "Media access is paused"),
            "channel_closed" => (MediaSessionsViewState.ChannelClosed,
                liveUpdatesOnly ? "Current media shown · live updates disconnected" :
                    "Media service disconnected"),
            "platform_unavailable" => (MediaSessionsViewState.ServiceUnavailable,
                liveUpdatesOnly ? "Current media shown · live updates unavailable" :
                    "Windows media controls unavailable"),
            "malformed_event" or "malformed_response" or "unsupported_protocol" =>
                (MediaSessionsViewState.Error,
                    liveUpdatesOnly ? "Current media shown · live update response was invalid" :
                        "Media response was invalid"),
            _ => (MediaSessionsViewState.Error,
                liveUpdatesOnly ? "Current media shown · live updates unavailable" :
                    "Media sessions could not be loaded"),
        };
    }

    private void SetCommandError(long generation, string status)
    {
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _pendingCommand = null;
            _status = status;
        }
        Invalidate();
    }

    private static string SessionElementId(string opaqueId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return "media.session." + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string FormatTime(long milliseconds)
    {
        var seconds = Math.Max(0, milliseconds / 1000);
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}
