using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.MediaSessions;

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
    private enum FailureStage
    {
        SnapshotRead,
        SubscriptionOpen,
        SubscriptionRead,
    }

    private sealed record FailureProjection(
        MediaSessionsViewState State,
        string Code,
        string LoadStatus,
        string RetainedStatus);

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

    private readonly TimeProvider _timeProvider;
    private readonly WidgetModel<State> _model;
    private readonly WidgetOptimisticCommand<
        State,
        WidgetMediaSessionCommand,
        CommandExecution,
        bool> _transportCommand;
    private Task? _progressLoop;
    private long _runGeneration;

    private sealed record State(
        IReadOnlyList<WidgetMediaSession> Sessions,
        string? SelectedSessionId,
        WidgetMediaSessionCommand? PendingCommand,
        MediaSessionsViewState ViewState,
        string Status,
        long SnapshotRevision,
        bool LiveUpdatesAvailable,
        bool ReloadInFlight)
    {
        internal static State Initial { get; } = new(
            [],
            null,
            null,
            MediaSessionsViewState.Initial,
            "Media sessions load when this widget becomes visible",
            0,
            false,
            false);
    }

    public MediaSessionsWidget(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _model = CreateModel(State.Initial);
        _transportCommand = CreateOptimisticCommand(
            "media.transport",
            _model,
            new WidgetOptimisticCommandOptions<
                State,
                WidgetMediaSessionCommand,
                CommandExecution,
                bool>
            {
                Policy = WidgetCommandPolicy.SingleFlight,
                Apply = PrepareCommand,
                Execute = async (execution, context) =>
                {
                    await HostServices.Media.ControlAsync(
                        execution.Session!.SessionId,
                        execution.Command,
                        context.CancellationToken)
                        .ConfigureAwait(false);
                    return true;
                },
                Reconcile = (state, execution, _) =>
                    !IsCommandCurrent(state, execution)
                        ? state
                        : state with
                        {
                            PendingCommand = null,
                            Status = "Updated by Windows media controls",
                        },
                Rollback = RollBackCommand,
                MapError = MapCommandError,
                Fail = FailCommand,
            });
    }

    public MediaSessionsViewState ViewState => _model.Value.ViewState;
    public string? SelectedSessionId => _model.Value.SelectedSessionId;
    public IReadOnlyList<WidgetMediaSession> Sessions => _model.Value.Sessions;
    public string Status => _model.Value.Status;
    public bool LiveUpdatesAvailable => _model.Value.LiveUpdatesAvailable;

    public override WidgetView Render()
    {
        var model = _model.Value;
        var sessions = model.Sessions;
        var selectedId = model.SelectedSessionId;
        var pending = model.PendingCommand;
        var state = model.ViewState;
        var status = model.Status;
        var reloadInFlight = model.ReloadInFlight;

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
        var selectedPill = SessionElementId(selected.SessionId);
        var pillIds = sessions.Select(item => SessionElementId(item.SessionId)).ToArray();
        var pills = new WidgetElement[sessions.Count];
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var id = pillIds[index];
            pills[index] = UI.Button(session.AppName, "media.select", id)
                .Icon(session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                    ? WidgetGlyph.Pause : WidgetGlyph.Music,
                    $"{session.AppName}. {session.Title}. Press A to select")
                .PersistFocusAs(id + ".focus")
                .Selected(string.Equals(session.SessionId, selected.SessionId, StringComparison.Ordinal))
                .FocusLeft(pillIds[Math.Max(0, index - 1)])
                .FocusRight(pillIds[Math.Min(sessions.Count - 1, index + 1)])
                .FocusDown("media.play-toggle")
                .Classes("media-session-pill",
                    session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
                        ? "is-playing" : "is-idle");
        }
        var position = ProjectPosition(selected);
        var duration = Math.Max(0, selected.DurationMilliseconds);
        var toggleEnabled = CanToggle(selected);
        var toggleLabel = selected.PlaybackStatus == WidgetMediaPlaybackStatus.Playing
            ? "Pause" : "Play";
        var togglePending = pending is WidgetMediaSessionCommand.TogglePlayPause or
            WidgetMediaSessionCommand.Play or WidgetMediaSessionCommand.Pause;
        var reconnectId = model.LiveUpdatesAvailable ? null : "media.retry.live";
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
        if (reconnectId is not null)
        {
            previous = previous.FocusDown(reconnectId);
            toggle = toggle.FocusDown(reconnectId);
            next = next.FocusDown(reconnectId);
        }

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

        var content = new List<WidgetElement>
        {
                header,
                UI.HorizontalScroll("media.session-scroll", pills)
                    .RememberChildFocus(selectedPill)
                    .Classes("media-session-list"),
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
                UI.Row("media.controls", previous, toggle, next).Classes("media-controls"),
        };
        if (reconnectId is not null)
            content.Add(UI.Button("Try again", "media.retry", reconnectId)
                .Icon(WidgetGlyph.Refresh, "Reconnect live media updates")
                .FocusUp("media.play-toggle")
                .Classes("media-retry"));
        var root = UI.Stack("media.root", content.ToArray())
            .InputScope("media-sessions")
            .Classes("media-sessions-widget");
        if (selected.CanPrevious)
            root = root.Shortcut(
                ControllerButton.LeftBumper, "media.previous", label: "Previous track");
        if (toggleEnabled)
            root = root.Shortcut(ControllerButton.X, "media.toggle", label: toggleLabel);
        if (selected.CanNext)
            root = root.Shortcut(
                ControllerButton.RightBumper, "media.next", label: "Next track");

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
        StartActiveRun();
        _progressLoop = RunPeriodicUpdatesWhileActiveAsync(
            TimeSpan.FromMilliseconds(250),
            _ =>
            {
                var selected = Selected(_model.Value);
                if (selected?.PlaybackStatus != WidgetMediaPlaybackStatus.Playing ||
                    selected.DurationMilliseconds <= 0) return ValueTask.CompletedTask;
                Invalidate();
                return ValueTask.CompletedTask;
            },
            invalidateAfterTick: false);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        _progressLoop = null;
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
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
            if (IsActive) StartActiveRun();
            return;
        }
        if (action.ActionId == "media.select")
        {
            _model.Update(state =>
            {
                var selected = state.Sessions.FirstOrDefault(item => string.Equals(
                    SessionElementId(item.SessionId), action.SourceElementId,
                    StringComparison.Ordinal));
                return selected is null ? state : state with
                {
                    SelectedSessionId = selected.SessionId,
                    Status = $"Selected {selected.AppName}",
                };
            });
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
        var handle = _transportCommand.Run(command.Value);
        if (handle.Admission != WidgetOperationAdmission.Joined)
            await handle.Completion.ConfigureAwait(false);
    }

    private WidgetCommandProjection<State, CommandExecution> PrepareCommand(
        State state,
        WidgetMediaSessionCommand command)
    {
        var generation = Interlocked.Read(ref _runGeneration);
        var session = Selected(state);
        var execution = new CommandExecution(
            session,
            command,
            generation,
            state.SnapshotRevision);
        if (state.PendingCommand is not null)
            return new(state, execution, ShouldExecute: false);
        if (session is null || !Supports(session, command))
            return new(state with
            {
                Status = "That action is not available for this media session",
            }, execution, ShouldExecute: false);

        var sessions = state.Sessions;
        if (command is WidgetMediaSessionCommand.Play or
            WidgetMediaSessionCommand.Pause or
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
            sessions = state.Sessions.Select(item => item.SessionId == session.SessionId
                ? item with
                {
                    PlaybackStatus = targetStatus,
                    PositionMilliseconds = projected,
                    CapturedAtUnixMilliseconds = _timeProvider.GetUtcNow()
                        .ToUnixTimeMilliseconds(),
                }
                : item).ToArray();
        }

        return new(state with
        {
            Sessions = sessions,
            PendingCommand = command,
            Status = command switch
            {
                WidgetMediaSessionCommand.Previous => "Going to previous track…",
                WidgetMediaSessionCommand.Next => "Going to next track…",
                WidgetMediaSessionCommand.Pause => "Pausing…",
                _ => "Resuming playback…",
            },
        }, execution);
    }

    private State RollBackCommand(State state, State baseline, CommandExecution execution)
    {
        if (!IsCommandCurrent(state, execution)) return state;
        var sessions = state.Sessions;
        if (state.SnapshotRevision == execution.SnapshotRevision &&
            execution.Session is { } session &&
            baseline.Sessions.FirstOrDefault(item => item.SessionId == session.SessionId)
                is { } prior)
        {
            sessions = state.Sessions.Select(item => item.SessionId == session.SessionId
                ? prior
                : item).ToArray();
        }
        return state with
        {
            Sessions = sessions,
            PendingCommand = null,
            Status = baseline.Status,
        };
    }

    private State FailCommand(
        State state,
        State baseline,
        CommandExecution execution,
        WidgetCommandError error) =>
        !IsCommandCurrent(state, execution)
            ? state
            : RollBackCommand(state, baseline, execution) with
            {
                Status = error.Message,
            };

    private bool IsCommandCurrent(State state, CommandExecution execution) =>
        Interlocked.Read(ref _runGeneration) == execution.RunGeneration &&
        state.SnapshotRevision == execution.SnapshotRevision &&
        execution.Session is { } admitted &&
        string.Equals(Selected(state)?.SessionId, admitted.SessionId,
            StringComparison.Ordinal);

    private static WidgetCommandError MapCommandError(Exception exception)
    {
        var message = exception switch
        {
            WidgetCapabilityUnavailableException => "Media control service unavailable",
            WidgetCapabilityException capability => capability.ErrorCode switch
            {
                "permission_denied" or "capability_revoked" =>
                    "Media control permission is off",
                "lifecycle_denied" => "Enter the widget before controlling playback",
                "resource_not_found" => "That media session ended",
                "not_supported" => "That action is no longer supported",
                _ => "Windows rejected the media action",
            },
            _ => "Windows rejected the media action",
        };
        return new("media_command_failed", message);
    }

    private sealed record CommandExecution(
        WidgetMediaSession? Session,
        WidgetMediaSessionCommand Command,
        long RunGeneration,
        long SnapshotRevision);

    private WidgetOperationHandle StartActiveRun()
    {
        return Operations.RunSingleFlight(
            "media.reload",
            async context =>
            {
                var generation = context.Generation;
                Interlocked.Exchange(ref _runGeneration, generation);
                _model.Update(state => state with
                {
                    ReloadInFlight = true,
                    ViewState = state.Sessions.Count == 0
                        ? MediaSessionsViewState.Loading
                        : MediaSessionsViewState.Ready,
                    Status = state.Sessions.Count == 0
                        ? "Loading Windows media sessions…"
                        : "Refreshing current media…",
                    LiveUpdatesAvailable = false,
                });
                try
                {
                    await ObserveAsync(generation, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _model.Update(state =>
                        Interlocked.Read(ref _runGeneration) == generation &&
                        state.ReloadInFlight
                            ? state with { ReloadInFlight = false }
                            : state);
                }
            },
            WidgetOperationLifetime.Active);
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
                var sessions = await HostServices.Media.GetSessionsAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Apply(sessions, generation, liveUpdatesAvailable: true);
                hasCurrentSnapshot = true;
                await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                                   .WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Apply(change.Sessions, generation, liveUpdatesAvailable: true);
                }
                SetLiveUpdateFailure(
                    new WidgetCapabilityException("channel_closed", "Media service disconnected."),
                    generation,
                    FailureStage.SubscriptionRead);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (hasCurrentSnapshot)
                    SetLiveUpdateFailure(exception, generation, FailureStage.SubscriptionRead);
                else
                    SetLoadFailure(exception, generation, FailureStage.SnapshotRead);
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
            var sessions = await HostServices.Media.GetSessionsAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Apply(sessions, generation, liveUpdatesAvailable: false);
            SetLiveUpdateFailure(subscriptionError, generation, FailureStage.SubscriptionOpen);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception readError)
        {
            SetLoadFailure(readError, generation, FailureStage.SnapshotRead);
        }
    }

    private void Apply(
        IReadOnlyList<WidgetMediaSession>? incoming,
        long generation,
        bool liveUpdatesAvailable = true)
    {
        var normalized = Normalize(incoming);
        _model.Update(state =>
        {
            if (Interlocked.Read(ref _runGeneration) != generation) return state;
            var selectedSessionId = state.SelectedSessionId;
            if (selectedSessionId is null ||
                !normalized.Any(item => item.SessionId == selectedSessionId))
                selectedSessionId = normalized.FirstOrDefault(item => item.IsCurrent)?.SessionId ??
                    normalized.FirstOrDefault(item =>
                        item.PlaybackStatus == WidgetMediaPlaybackStatus.Playing)?.SessionId ??
                    normalized.FirstOrDefault()?.SessionId;
            return state with
            {
                Sessions = normalized,
                SnapshotRevision = state.SnapshotRevision + 1,
                LiveUpdatesAvailable = liveUpdatesAvailable,
                SelectedSessionId = selectedSessionId,
                PendingCommand = null,
                ReloadInFlight = false,
                ViewState = normalized.Count == 0
                    ? MediaSessionsViewState.Empty
                    : MediaSessionsViewState.Ready,
                Status = normalized.Count == 0 ? "No active Windows media sessions" :
                normalized.Count == 1 ? "1 active media session · live" :
                $"{normalized.Count} active media sessions · live",
            };
        });
    }

    private static IReadOnlyList<WidgetMediaSession> Normalize(
        IReadOnlyList<WidgetMediaSession>? incoming)
    {
        if (incoming is null) return [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<WidgetMediaSession>(Math.Min(incoming.Count, 32));
        foreach (var item in incoming.Take(32))
        {
            if (item is null || string.IsNullOrWhiteSpace(item.SessionId) ||
                !ids.Add(item.SessionId))
                throw new WidgetCapabilityException(
                    "invalid_backend_data", "Media session identity was invalid.");
            normalized.Add(NormalizeSession(item));
        }
        return normalized;
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
        var session = Selected(_model.Value);
        if (session is null) return null;
        if (session.CanTogglePlayPause) return WidgetMediaSessionCommand.TogglePlayPause;
        if (session.PlaybackStatus == WidgetMediaPlaybackStatus.Playing && session.CanPause)
            return WidgetMediaSessionCommand.Pause;
        return session.CanPlay ? WidgetMediaSessionCommand.Play : null;
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

    private static WidgetMediaSession? Selected(State state) =>
        state.Sessions.FirstOrDefault(item => string.Equals(
            item.SessionId, state.SelectedSessionId, StringComparison.Ordinal));

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

    private void SetLoadFailure(
        Exception exception,
        long generation,
        FailureStage stage)
    {
        var failure = ClassifyFailure(exception);
        _model.Update(current =>
        {
            if (Interlocked.Read(ref _runGeneration) != generation) return current;
            if (current.Sessions.Count != 0)
                return current with
                {
                    ViewState = MediaSessionsViewState.Ready,
                    Status = FormatFailure(failure.RetainedStatus, stage, failure.Code),
                    PendingCommand = null,
                    LiveUpdatesAvailable = false,
                    ReloadInFlight = false,
                };
            return current with
            {
                ViewState = failure.State,
                Status = FormatFailure(failure.LoadStatus, stage, failure.Code),
                Sessions = [],
                PendingCommand = null,
                LiveUpdatesAvailable = false,
                ReloadInFlight = false,
            };
        });
    }

    private void SetLiveUpdateFailure(
        Exception exception,
        long generation,
        FailureStage stage)
    {
        var failure = ClassifyFailure(exception);
        _model.Update(state =>
        {
            if (Interlocked.Read(ref _runGeneration) != generation) return state;
            return state with
            {
                LiveUpdatesAvailable = false,
                Status = state.Sessions.Count == 0
                    ? FormatFailure(
                        "No active Windows media sessions · live updates unavailable",
                        stage,
                        failure.Code)
                    : FormatFailure(failure.RetainedStatus, stage, failure.Code),
            };
        });
    }

    private static FailureProjection ClassifyFailure(Exception exception)
    {
        if (exception is WidgetCapabilityUnavailableException)
            return new(
                MediaSessionsViewState.ServiceUnavailable,
                "capability_unavailable",
                "Media service unavailable",
                "Current media shown · live updates unavailable");
        if (exception is TimeoutException)
            return new(
                MediaSessionsViewState.ChannelClosed,
                "request_timeout",
                "Media request timed out",
                "Current media shown · refresh timed out");
        if (exception is OperationCanceledException)
            return new(
                MediaSessionsViewState.ChannelClosed,
                "request_canceled",
                "Media request was canceled",
                "Current media shown · refresh was canceled");
        if (exception is not WidgetCapabilityException capability)
            return new(
                MediaSessionsViewState.Error,
                "unexpected_failure",
                "Media sessions could not be loaded",
                "Current media shown · live updates unavailable");
        return capability.ErrorCode switch
        {
            "permission_denied" or "capability_not_declared" or "capability_revoked" =>
                new(MediaSessionsViewState.PermissionDenied, capability.ErrorCode,
                    "Media access is off", "Current media shown · live update access is off"),
            "lifecycle_denied" => new(MediaSessionsViewState.LifecycleDenied,
                "lifecycle_denied", "Media access is paused",
                "Current media shown · live updates are paused"),
            "channel_closed" or "request_canceled" or "request_limit" =>
                new(MediaSessionsViewState.ChannelClosed, capability.ErrorCode,
                    "Media service disconnected",
                    "Current media shown · live updates disconnected"),
            "platform_unavailable" => new(MediaSessionsViewState.ServiceUnavailable,
                "platform_unavailable", "Windows media controls unavailable",
                "Current media shown · live updates unavailable"),
            "malformed_event" or "malformed_response" or "unsupported_protocol" or
                "protocol_violation" or "invalid_backend_data" or "response_too_large" =>
                new(MediaSessionsViewState.Error, capability.ErrorCode,
                    "Media response was invalid",
                    "Current media shown · live update response was invalid"),
            _ => new(MediaSessionsViewState.Error, "capability_failure",
                "Media sessions could not be loaded",
                "Current media shown · live updates unavailable"),
        };
    }

    private static string FormatFailure(
        string message,
        FailureStage stage,
        string code) =>
        $"{message} · {stage switch
        {
            FailureStage.SnapshotRead => "snapshot-read",
            FailureStage.SubscriptionOpen => "subscription-open",
            FailureStage.SubscriptionRead => "subscription-read",
            _ => "unknown-stage",
        }}/{code}";

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
