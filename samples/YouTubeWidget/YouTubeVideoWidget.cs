using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

/// <summary>A link-to-play YouTube client over the generic embedded-media contract.</summary>
public sealed partial class YouTubeVideoWidget : Widget
{
    internal const string SessionId = "youtube-video.primary";
    internal const string LinkActionId = "youtube.link.commit";
    internal const string ToggleActionId = "youtube.playback.toggle";
    internal const string SeekBackwardActionId = "youtube.playback.seek-backward";
    internal const string SeekForwardActionId = "youtube.playback.seek-forward";
    internal const string SeekActionId = "youtube.playback.seek";
    internal const string VolumeActionId = "youtube.playback.volume";
    internal const string CaptionsActionId = "youtube.player.captions";
    internal const string PlaybackRateActionId = "youtube.player.settings.playback-rate";
    internal const string MutedActionId = "youtube.player.settings.muted";
    internal const string LoopActionId = "youtube.player.settings.loop";
    internal const string OpenInYouTubeActionId = "youtube.player.settings.open-youtube";
    internal const string PlayerSettingsBackActionId = "youtube.player.settings.back";
    internal const string PlaybackRateBackActionId = "youtube.player.settings.playback-rate.back";
    internal const string CaptionsBackActionId = "youtube.player.captions.back";
    private const string PlaybackRateOptionPrefix = "youtube.player.settings.rate.";
    // Fullscreen is a host-owned mode. The package declares the surface may be
    // presented that way and offers this reserved entry action; the host owns
    // the state and returns to this layout on B.
    internal const string EnterFullscreenActionId = "host.embeddedMediaSession.enterFullscreen";
    private const string FullscreenFocusId = "youtube.player.fullscreen";
    private const string PendingFeedbackKey = "youtube.playback.feedback";
    private const double SeekStepSeconds = 10;
    private static readonly TimeSpan PendingFeedbackThreshold = TimeSpan.FromMilliseconds(250);
    private readonly WidgetModel<YouTubeWidgetState> _model;
    private readonly TimeProvider _timeProvider;

    // One committed state is captured here and threaded through every render
    // helper. Rereading the model per helper would let a concurrent commit tear
    // one frame across the header, the transport, and the media session.
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
        var fullscreenActionEnabled = showFullscreenAction && error is null && !mediaLoading;
        var captionsActionEnabled = showFullscreenAction && transportActionsAvailable;
        var media = CreateMediaSession(
            videoId, playback.PendingCommand, showFullscreenAction);

        var linkEntry = UI.TextEntry(
                playback.Link,
                "Enter youtube.com or youtu.be link here",
                LinkActionId,
                "youtube.link",
                ProtocolConstants.MaximumTextEntryLength)
            .FocusUp("youtube.player.back")
            .FocusDown("youtube.controls")
            .Classes("youtube-link", playback.Link.Length == 0 ? "is-empty" : "has-value");
        var playing = playback.PlaybackSemantic == EmbeddedMediaPlaybackState.Playing;
        var toggle = UI.Button("", ToggleActionId, "youtube.playback.toggle")
            .Icon(playing ? WidgetGlyph.Pause : WidgetGlyph.Play, playing ? "Pause" : "Play")
            .Disabled(controlsUnavailable)
            .Busy(busyControl == PendingMediaControl.TogglePlayback)
            .FocusUp("youtube.link")
            .FocusRight(showFullscreenAction ? FullscreenFocusId : "youtube.timeline")
            .Classes("youtube-primary", "youtube-transport-button", "youtube-play-toggle");
        var fullscreen = UI.Button("", EnterFullscreenActionId, FullscreenFocusId)
            .Icon(WidgetGlyph.Connection, "Fullscreen")
            .Disabled(!fullscreenActionEnabled)
            .FocusUp("youtube.link")
            .FocusLeft("youtube.playback.toggle")
            .FocusRight(CaptionsActionId)
            .Classes("youtube-secondary", "youtube-transport-button");
        var captions = UI.Button("", CaptionsActionId, CaptionsActionId)
            .Icon(WidgetGlyph.Settings, "Captions and player settings")
            .Disabled(!captionsActionEnabled)
            .FocusUp("youtube.link")
            .FocusLeft(FullscreenFocusId)
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
            .FocusLeft(showFullscreenAction ? CaptionsActionId : "youtube.playback.toggle")
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
        var headerActions = new List<WidgetElement>
        {
            UI.Text(status, "youtube.status")
                .Classes("youtube-status", "youtube-status-pill", statusClass),
        };
        var transportControls = new List<WidgetElement> { toggle };
        if (showFullscreenAction)
        {
            transportControls.Add(fullscreen);
            transportControls.Add(captions);
        }
        transportControls.Add(UI.Row(
                "youtube.timeline-group",
                UI.Text(FormatTime(playback.Position), "youtube.position")
                    .Classes("youtube-time"),
                timeline,
                UI.Text(FormatTime(playback.Duration), "youtube.duration")
                    .Classes("youtube-time", "is-end"))
            .Classes("youtube-timeline-group"));
        transportControls.Add(volumeControl);
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
                                UI.Row("youtube.controls", transportControls.ToArray())
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
        { EmbeddedMediaSession = media };
    }

    private static EmbeddedMediaSession CreateMediaSession(
        string? videoId,
        EmbeddedMediaPlaybackCommand? pendingCommand,
        bool overlayFullscreen = true) => new()
    {
        Id = SessionId,
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
        SupportedPresentations = overlayFullscreen
            ? [MediaPresentationKind.OverlayFullscreen,
               MediaPresentationKind.CompactPinned]
            : [MediaPresentationKind.CompactPinned],
        MediaSeekStepSeconds = SeekStepSeconds,
        PendingCommand = pendingCommand,
    };

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryHandleApplicationAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId == LinkActionId && action.CommittedText is { } committed)
        {
            RunPlaybackLatest(new(
                YouTubePlaybackIntent.CommitLink,
                Text: committed));
            return ValueTask.CompletedTask;
        }

        var isActive = IsActive;
        var seekStep = SeekStepSeconds;
        var request = action.ActionId switch
        {
            ToggleActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.TogglePlayback, isActive),
            SeekBackwardActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SeekBackward, isActive, seekStep),
            SeekForwardActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SeekForward, isActive, seekStep),
            SeekActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SeekAbsolute, isActive, seekStep,
                action.RequestedValue),
            VolumeActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SetVolume, isActive, seekStep,
                action.RequestedValue),
            MutedActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SetMuted, isActive),
            LoopActionId => new YouTubePlaybackRequest(
                YouTubePlaybackIntent.SetLoop, isActive),
            _ when TryParsePlaybackRateAction(action.ActionId, out var playbackRate) =>
                new YouTubePlaybackRequest(
                    YouTubePlaybackIntent.SetPlaybackRate, isActive,
                    RequestedValue: playbackRate),
            _ => null,
        };
        if (request is not null) RunPlayback(request);
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(playbackEvent.SessionId, SessionId, StringComparison.Ordinal))
            return ValueTask.CompletedTask;
        // The SDK-owned correlation owner rejects stale, foreign, and unknown
        // command reports before the pure playback transition sees them.
        _playbackCommand.Observe(playbackEvent);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Projects one single-flight transport intent. The declaration stays visible
    /// while this returns RejectedPending, preserving held-controller bindings.
    /// </summary>
    private void RunPlayback(YouTubePlaybackRequest request)
    {
        var run = _playbackCommand.Run(request);
        if (run.IsAccepted) SchedulePendingFeedback(run.Sequence);
    }

    /// <summary>Replaces an older load intent with the newest link or result.</summary>
    private void RunPlaybackLatest(YouTubePlaybackRequest request) =>
        _playbackCommand.RunLatest(request);

    private static WidgetOutOfBandCommandProjection<YouTubeWidgetState, string>
        ApplyPlaybackRequest(
            YouTubeWidgetState state,
            YouTubePlaybackRequest request,
            long sequence)
    {
        YouTubeWidgetState next;
        switch (request.Intent)
        {
            case YouTubePlaybackIntent.CommitLink:
                next = state.WithCommittedLink(request.Text ?? string.Empty, sequence);
                break;
            case YouTubePlaybackIntent.SelectResult when
                request.ReturnFocusId is { } returnFocusId &&
                request.VideoId is { } selectedVideoId:
                next = state.WithSelectedResult(returnFocusId, selectedVideoId, sequence);
                break;
            default:
                var preferenceIntent = request.Intent is
                    YouTubePlaybackIntent.SetPlaybackRate or
                    YouTubePlaybackIntent.SetMuted or
                    YouTubePlaybackIntent.SetLoop;
                if (state.Playback.VideoId is not { } videoId ||
                    !(preferenceIntent
                        ? state.CanDispatchPlayerSetting(request.IsActive)
                        : state.CanDispatchTransportAction(request.IsActive)))
                    return new(state, state.Playback.VideoId ?? "youtube.none",
                        ShouldStart: false);
                var playback = state.Playback;
                next = request.Intent switch
                {
                    YouTubePlaybackIntent.TogglePlayback => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            sequence,
                            playback.PlaybackSemantic == EmbeddedMediaPlaybackState.Playing
                                ? EmbeddedMediaPlaybackCommandKind.Pause
                                : EmbeddedMediaPlaybackCommandKind.Play,
                            videoId,
                            PendingMediaControl.TogglePlayback)),
                    YouTubePlaybackIntent.SeekBackward => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.Seek,
                            videoId,
                            PendingMediaControl.SeekBackward,
                            position: Math.Max(0, playback.Position - request.SeekStep))),
                    YouTubePlaybackIntent.SeekForward => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.Seek,
                            videoId,
                            PendingMediaControl.SeekForward,
                            position: Math.Min(Math.Max(0, playback.Duration),
                                playback.Position + request.SeekStep))),
                    YouTubePlaybackIntent.SeekAbsolute when
                        request.RequestedValue is { } requested && double.IsFinite(requested) =>
                        state.WithPlayback(current => current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.Seek,
                            videoId,
                            PendingMediaControl.Timeline,
                            position: Math.Clamp(requested, 0,
                                Math.Max(0, playback.Duration)))),
                    YouTubePlaybackIntent.SetVolume when
                        request.RequestedValue is { } requested && double.IsFinite(requested) =>
                        state.WithPlayback(current => current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.SetVolume,
                            videoId,
                            PendingMediaControl.Volume,
                            volume: Math.Clamp(requested, 0, 1))),
                    YouTubePlaybackIntent.SetPlaybackRate when
                        request.RequestedValue is { } requested &&
                        IsCanonicalPlaybackRate(requested) =>
                        state.WithPlayback(current => current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                            videoId,
                            PendingMediaControl.PlaybackRate,
                            playbackRate: requested)),
                    YouTubePlaybackIntent.SetMuted => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.SetMuted,
                            videoId,
                            PendingMediaControl.Muted,
                            muted: !playback.Muted)),
                    YouTubePlaybackIntent.SetLoop => state.WithPlayback(current =>
                        current.WithQueuedCommand(
                            sequence,
                            EmbeddedMediaPlaybackCommandKind.SetLoop,
                            videoId,
                            PendingMediaControl.Loop,
                            loop: !playback.Loop)),
                    _ => state,
                };
                break;
        }

        var pending = next.Playback.PendingCommand;
        return new(next, pending?.MediaKey ?? next.Playback.VideoId ?? "youtube.none",
            ShouldStart: pending?.Sequence == sequence);
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

    private static bool IsCanonicalPlaybackRate(double value) =>
        CanonicalPlaybackRates.Contains(value);

    private static bool TryParsePlaybackRateAction(string actionId, out double playbackRate)
    {
        playbackRate = 0;
        if (!actionId.StartsWith(PlaybackRateOptionPrefix, StringComparison.Ordinal))
            return false;
        return double.TryParse(actionId[PlaybackRateOptionPrefix.Length..],
                   System.Globalization.NumberStyles.AllowDecimalPoint,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out playbackRate) &&
               IsCanonicalPlaybackRate(playbackRate);
    }

    private static readonly double[] CanonicalPlaybackRates = [0.5, 0.75, 1, 1.25, 1.5, 2];
}
