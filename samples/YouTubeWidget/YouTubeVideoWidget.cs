using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

/// <summary>A link-to-play YouTube client over the generic embedded-media contract.</summary>
public sealed partial class YouTubeVideoWidget : Widget
{
    internal const string SurfaceId = "youtube-video.primary";
    internal const string LinkActionId = "youtube.link.commit";
    internal const string ToggleActionId = "youtube.playback.toggle";
    internal const string SeekBackwardActionId = "youtube.playback.seek-backward";
    internal const string SeekForwardActionId = "youtube.playback.seek-forward";
    internal const string SeekActionId = "youtube.playback.seek";
    internal const string VolumeActionId = "youtube.playback.volume";
    // Fullscreen is a host-owned mode. The package declares the surface may be
    // presented that way and offers this reserved entry action; the host owns
    // the state and returns to this layout on B.
    internal const string EnterFullscreenActionId = "host.embeddedMedia.enterFullscreen";
    private const string FullscreenFocusId = "youtube.player.fullscreen";
    private const string PendingFeedbackKey = "youtube.playback.feedback";
    private const double SeekStepSeconds = 10;
    private static readonly TimeSpan PendingFeedbackThreshold = TimeSpan.FromMilliseconds(250);
    private readonly WidgetModel<YouTubeWidgetState> _model;
    private readonly TimeProvider _timeProvider;

    // One committed state is captured here and threaded through every render
    // helper. Rereading the model per helper would let a concurrent commit tear
    // one frame across the header, the transport, and the media surface.
    public override WidgetView Render() => RenderApplication(_model.Value);

    private WidgetView RenderPlayer(YouTubeWidgetState state)
    {
        var playback = state.Playback;
        var videoId = playback.VideoId;
        var error = playback.Error;
        var seekBuffering = playback.IsSeekBuffering;
        var mediaLoading = playback.IsMediaLoading;
        var busyControl = playback.BusyControl;
        var transportActionsAvailable = state.CanDeclareTransportAction(IsActive);
        var controlsUnavailable = !transportActionsAvailable;
        var showFullscreenAction = state.Route == YouTubeRoute.Player && videoId is not null;
        var fullscreenActionEnabled = error is null && !mediaLoading;
        // Declared only on the route that also offers the reserved entry action,
        // so the capability never outlives a way to reach it.
        var media = CreateMediaSurface(videoId, playback.PendingCommand,
            retainSessionWhenHidden: false, overlayFullscreenCapable: showFullscreenAction);

        var linkEntry = UI.TextEntry(
                playback.Link,
                "Enter youtube.com or youtu.be link here",
                LinkActionId,
                "youtube.link",
                ProtocolConstants.MaximumTextEntryLength)
            .FocusUp(showFullscreenAction && fullscreenActionEnabled
                ? FullscreenFocusId : "youtube.player.back")
            .FocusDown("youtube.controls")
            .Classes("youtube-link", playback.Link.Length == 0 ? "is-empty" : "has-value");
        var playing = playback.PlaybackSemantic == EmbeddedMediaPlaybackState.Playing;
        var toggle = UI.Button("", ToggleActionId, "youtube.playback.toggle")
            .Icon(playing ? WidgetGlyph.Pause : WidgetGlyph.Play, playing ? "Pause" : "Play")
            .Disabled(controlsUnavailable)
            .Busy(busyControl == PendingMediaControl.TogglePlayback)
            .FocusUp("youtube.link")
            .FocusRight("youtube.playback.seek-backward")
            .Classes("youtube-primary", "youtube-transport-button", "youtube-play-toggle");
        var seekBack = UI.Button("", SeekBackwardActionId, "youtube.playback.seek-backward")
            .Icon(WidgetGlyph.Rewind, "Seek backward 10 seconds")
            .Disabled(controlsUnavailable)
            .FocusUp("youtube.link")
            .FocusLeft("youtube.playback.toggle")
            .FocusRight("youtube.playback.seek-forward")
            .Classes("youtube-secondary", "youtube-transport-button");
        var seekForward = UI.Button("", SeekForwardActionId, "youtube.playback.seek-forward")
            .Icon(WidgetGlyph.FastForward, "Seek forward 10 seconds")
            .Disabled(controlsUnavailable)
            .FocusUp("youtube.link")
            .FocusLeft("youtube.playback.seek-backward")
            .FocusRight("youtube.timeline")
            .Classes("youtube-secondary", "youtube-transport-button");
        var timeline = UI.Slider(
                playback.Position,
                0,
                Math.Max(1, playback.Duration),
                Math.Min(SeekStepSeconds, Math.Max(1, playback.Duration)),
                SeekActionId,
                "youtube.timeline",
                $"Playback position. {FormatTime(playback.Position)} of {FormatTime(playback.Duration)}",
                $"{FormatTime(playback.Position)} of {FormatTime(playback.Duration)}")
            .RequireControllerActivation()
            .Disabled(controlsUnavailable)
            .Busy(busyControl == PendingMediaControl.Timeline)
            .FocusUp("youtube.link")
            .FocusLeft("youtube.playback.seek-forward")
            .FocusRight("youtube.volume")
            .Classes("youtube-slider");
        var volumeControl = UI.Slider(
                playback.Volume,
                0,
                1,
                0.05,
                VolumeActionId,
                "youtube.volume",
                $"Volume {Math.Round(playback.Volume * 100)} percent",
                $"{Math.Round(playback.Volume * 100)} percent")
            .RequireControllerActivation()
            .Disabled(controlsUnavailable)
            .Busy(busyControl == PendingMediaControl.Volume)
            .FocusUp("youtube.link")
            .FocusLeft("youtube.timeline")
            .Classes("youtube-slider", "youtube-volume");

        var status = error ?? (seekBuffering
            ? "Buffering YouTube video…"
            : StatusText(videoId, playback.State, mediaLoading));
        var statusClass = error is not null ? "is-error" :
            mediaLoading || seekBuffering ? "is-busy" :
            playback.State == EmbeddedMediaPlaybackState.Playing ? "is-playing" : "is-normal";
        IReadOnlyList<WidgetQuickAction>? quickActions = transportActionsAvailable
                ?
                [
                    new WidgetQuickAction(ControllerButton.X, ToggleActionId, "Play or pause"),
                    new WidgetQuickAction(ControllerButton.LeftTrigger, SeekBackwardActionId,
                        "Seek backward 10 seconds",
                        RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
                    new WidgetQuickAction(ControllerButton.RightTrigger, SeekForwardActionId,
                        "Seek forward 10 seconds",
                        RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
                ]
                : null;
        var back = UI.Button("Back", BackActionId, "youtube.player.back")
            .FocusDown("youtube.link")
            .Classes("youtube-route-button", "youtube-player-back");
        var headerActions = new List<WidgetElement>();
        if (showFullscreenAction)
        {
            if (fullscreenActionEnabled) back = back.FocusRight(FullscreenFocusId);
            headerActions.Add(UI.Button(
                    "Fullscreen", EnterFullscreenActionId, FullscreenFocusId)
                .Disabled(!fullscreenActionEnabled)
                .FocusLeft("youtube.player.back")
                .FocusDown("youtube.link")
                .Classes("youtube-route-button"));
        }
        headerActions.Add(UI.Text(status, "youtube.status")
            .Classes("youtube-status", "youtube-status-pill", statusClass));
        var root = UI.Stack(
                "youtube.root",
                UI.Row(
                        "youtube.header",
                        back,
                        UI.Stack("youtube.player.heading-copy",
                            UI.Text("YOUTUBE", "youtube.eyebrow").Classes("youtube-eyebrow"),
                            UI.Text(videoId is null ? "Play a link" : "Now playing", "youtube.title")
                                .Classes("youtube-title"))
                            .Classes("youtube-appbar-copy"),
                        UI.Row("youtube.player.header-actions", headerActions.ToArray())
                            .Classes("youtube-route-actions"))
                    .Classes("youtube-header", "youtube-appbar"),
                linkEntry,
                UI.Stack("youtube.player-shell",
                        UI.MediaViewport(media, "youtube.viewport").Classes("youtube-viewport"),
                        UI.Stack("youtube.control-deck",
                                UI.Row("youtube.controls",
                                        toggle,
                                        seekBack,
                                        seekForward,
                                        UI.Row(
                                                "youtube.timeline-group",
                                                UI.Text(FormatTime(playback.Position), "youtube.position")
                                                    .Classes("youtube-time"),
                                                timeline,
                                                UI.Text(FormatTime(playback.Duration), "youtube.duration")
                                                    .Classes("youtube-time", "is-end"))
                                            .Classes("youtube-timeline-group"),
                                        volumeControl)
                                    .RememberChildFocus("youtube.playback.toggle")
                                    .Classes("youtube-controls"))
                            .Classes("youtube-control-deck", videoId is null ? "is-unavailable" : "is-ready"))
                    .Classes("youtube-player-shell"))
            .InputScope("youtube.root")
            .Classes("youtube-root");
        if (transportActionsAvailable)
        {
            root = root
                .Shortcut(ControllerButton.X, ToggleActionId)
                .Shortcut(ControllerButton.LeftTrigger, SeekBackwardActionId,
                    repeatPolicy: ControllerActionRepeatPolicy.WhileHeld)
                .Shortcut(ControllerButton.RightTrigger, SeekForwardActionId,
                    repeatPolicy: ControllerActionRepeatPolicy.WhileHeld);
        }
        return new WidgetView(
            root,
            InitialFocusId: "youtube.playback.toggle",
            QuickActions: quickActions,
            ActiveInputScopeId: "youtube.root",
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 760,
                PreferredHeight = 680,
                MinimumWidth = 420,
                MinimumHeight = 540,
            })
        { EmbeddedMedia = media };
    }

    private static EmbeddedMediaSurface CreateMediaSurface(
        string? videoId,
        EmbeddedMediaPlaybackCommand? pendingCommand,
        bool retainSessionWhenHidden,
        bool overlayFullscreenCapable = false) => new()
    {
        Id = SurfaceId,
        AccessibleName = videoId is null
            ? "YouTube player. No video loaded."
            : $"YouTube player for video {videoId}",
        EntryAsset = "payload/media/adapter.html",
        Surface = new WidgetSurfaceHints
        {
            PreferredWidth = 640,
            PreferredHeight = 360,
            MinimumWidth = 356,
            MinimumHeight = 200,
        },
        AspectRatio = 16.0 / 9.0,
        Resources =
        [
            new EmbeddedMediaResource
            {
                Path = "payload/media/adapter.html",
                ContentType = "text/html",
            },
            new EmbeddedMediaResource
            {
                Path = "payload/media/adapter-runtime.js",
                ContentType = "application/javascript",
            },
        ],
        Commands =
        [
            EmbeddedMediaCommand.TogglePlayback,
            EmbeddedMediaCommand.SeekBackward,
            EmbeddedMediaCommand.SeekForward,
        ],
        AllowedFrameOrigins =
        [
            "https://www.youtube.com",
            "https://fonts.gstatic.com",
            "https://googleads.g.doubleclick.net",
            "https://static.doubleclick.net",
            "https://ssl.gstatic.com",
            "https://www.google.com",
            "https://jnn-pa.googleapis.com",
            "https://i.ytimg.com",
        ],
        AllowedFrameDomainFamilies =
        [
            "youtube.com",
            "googlevideo.com",
            "ytimg.com",
            "gstatic.com",
        ],
        CompactPinnedPresentation = true,
        MediaSeekStepSeconds = SeekStepSeconds,
        PendingCommand = pendingCommand,
        RetainSessionWhenHidden = retainSessionWhenHidden,
        OverlayFullscreenCapable = overlayFullscreenCapable,
    };

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryHandleApplicationAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId == LinkActionId && action.CommittedText is { } committed)
        {
            CommitPlayback(state => state.WithCommittedLink(committed));
            return ValueTask.CompletedTask;
        }

        var isActive = IsActive;
        var seekStep = CurrentMediaSeekStepSeconds;
        CommitPlayback(state =>
        {
            if (state.Playback.VideoId is not { } videoId ||
                !state.CanDispatchTransportAction(isActive))
                return state;
            var playback = state.Playback;
            return action.ActionId switch
            {
                ToggleActionId => state.WithPlayback(current => current.WithQueuedCommand(
                    playback.PlaybackSemantic == EmbeddedMediaPlaybackState.Playing
                        ? EmbeddedMediaPlaybackCommandKind.Pause
                        : EmbeddedMediaPlaybackCommandKind.Play,
                    videoId,
                    PendingMediaControl.TogglePlayback)),
                SeekBackwardActionId => state.WithPlayback(current => current.WithQueuedCommand(
                    EmbeddedMediaPlaybackCommandKind.Seek,
                    videoId,
                    PendingMediaControl.SeekBackward,
                    position: Math.Max(0, playback.Position - seekStep))),
                SeekForwardActionId => state.WithPlayback(current => current.WithQueuedCommand(
                    EmbeddedMediaPlaybackCommandKind.Seek,
                    videoId,
                    PendingMediaControl.SeekForward,
                    position: Math.Min(
                        Math.Max(0, playback.Duration), playback.Position + seekStep))),
                SeekActionId when action.RequestedValue is { } requested &&
                    double.IsFinite(requested) => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            EmbeddedMediaPlaybackCommandKind.Seek,
                            videoId,
                            PendingMediaControl.Timeline,
                            position: Math.Clamp(requested, 0, Math.Max(0, playback.Duration)))),
                VolumeActionId when action.RequestedValue is { } requested &&
                    double.IsFinite(requested) => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            EmbeddedMediaPlaybackCommandKind.SetVolume,
                            videoId,
                            PendingMediaControl.Volume,
                            volume: Math.Clamp(requested, 0, 1))),
                _ => state,
            };
        });
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(playbackEvent.SurfaceId, SurfaceId, StringComparison.Ordinal))
            return ValueTask.CompletedTask;
        // A completing report retires its own command and the busy projection with
        // it; the feedback timer for that sequence can no longer commit anything.
        _model.Update(state => state.WithPlayback(
            playback => playback.WithPlaybackEvent(playbackEvent)));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Commits one playback transition and starts pending feedback for a command
    /// the same committed revision produced, so the timer never races a reread.
    /// </summary>
    private void CommitPlayback(Func<YouTubeWidgetState, YouTubeWidgetState> transition)
    {
        var update = _model.Update<long?>(state =>
        {
            var next = transition(state);
            var queued = next.Playback.PendingCommand is { } pending &&
                pending.Sequence != state.Playback.PendingCommand?.Sequence &&
                next.Playback.PendingControl != PendingMediaControl.None
                    ? pending.Sequence
                    : (long?)null;
            return (next, queued);
        });
        if (update.Result is { } sequence) SchedulePendingFeedback(sequence);
    }

    /// <summary>
    /// Latest-wins so a newer command supersedes the previous timer, and Active so
    /// the runtime cancels it when the widget stops being interactive. A timer
    /// that outlives its own command commits nothing: the sequence no longer
    /// matches the pending command.
    /// </summary>
    private void SchedulePendingFeedback(long commandSequence) =>
        Operations.RunLatest(PendingFeedbackKey, async context =>
        {
            await Task.Delay(PendingFeedbackThreshold, _timeProvider, context.CancellationToken)
                .ConfigureAwait(false);
            if (!context.IsCurrent) return;
            _model.Update(state => state.WithPlayback(
                playback => playback.WithPendingFeedback(commandSequence)));
        });

    private static string StatusText(
        string? videoId,
        EmbeddedMediaPlaybackState state,
        bool loading) =>
        videoId is null ? "Paste YouTube link to start" :
        loading ? "Loading YouTube video…" :
        state switch
        {
            EmbeddedMediaPlaybackState.Playing => "Playing",
            EmbeddedMediaPlaybackState.Paused => "Paused",
            EmbeddedMediaPlaybackState.Ended => "Playback ended",
            EmbeddedMediaPlaybackState.Error => "YouTube playback failed",
            _ => "Ready — press Play to start",
        };

    private static string FormatTime(double seconds)
    {
        var value = Math.Max(0, (int)Math.Round(seconds));
        return $"{value / 60}:{value % 60:00}";
    }
}
