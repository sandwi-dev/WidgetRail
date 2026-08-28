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
    private const double SeekStepSeconds = 10;
    private readonly object _gate = new();
    private long _commandSequence;
    private long _eventSequence;
    private string _link = string.Empty;
    private string? _videoId;
    private string? _validationError;
    private string? _playbackError;
    private EmbeddedMediaPlaybackState _state = EmbeddedMediaPlaybackState.Ready;
    private double _position;
    private double _duration;
    private double _volume = 0.8;
    private EmbeddedMediaPlaybackCommand? _pendingCommand;

    public override WidgetView Render() => RenderApplication();

    private WidgetView RenderPlayer()
    {
        string link;
        string? videoId;
        string? error;
        EmbeddedMediaPlaybackState state;
        double position;
        double duration;
        double volume;
        EmbeddedMediaPlaybackCommand? pending;
        lock (_gate)
        {
            link = _link;
            videoId = _videoId;
            error = _validationError ?? _playbackError;
            state = _state;
            position = _position;
            duration = _duration;
            volume = _volume;
            pending = _pendingCommand;
        }

        var media = CreateMediaSurface(videoId, pending, retainSessionWhenHidden: false);

        var linkEntry = UI.TextEntry(
                link,
                "Paste a youtube.com or youtu.be link",
                LinkActionId,
                "youtube.link",
                ProtocolConstants.MaximumTextEntryLength)
            .FocusDown("youtube.playback.toggle")
            .Classes("youtube-link");
        var toggle = UI.Button(
                state == EmbeddedMediaPlaybackState.Playing ? "Pause" : "Play",
                ToggleActionId,
                "youtube.playback.toggle")
            .Disabled(videoId is null || pending is not null)
            .FocusUp("youtube.link")
            .FocusRight("youtube.playback.seek-backward")
            .Classes("youtube-primary");
        var seekBack = UI.Button("-10s", SeekBackwardActionId, "youtube.playback.seek-backward")
            .Disabled(videoId is null || pending is not null)
            .FocusUp("youtube.timeline")
            .FocusLeft("youtube.playback.toggle")
            .FocusRight("youtube.playback.seek-forward")
            .Classes("youtube-secondary");
        var seekForward = UI.Button("+10s", SeekForwardActionId, "youtube.playback.seek-forward")
            .Disabled(videoId is null || pending is not null)
            .FocusUp("youtube.timeline")
            .FocusLeft("youtube.playback.seek-backward")
            .Classes("youtube-secondary");
        var timeline = UI.Slider(
                position,
                0,
                Math.Max(1, duration),
                Math.Min(SeekStepSeconds, Math.Max(1, duration)),
                SeekActionId,
                "youtube.timeline",
                $"Playback position. {FormatTime(position)} of {FormatTime(duration)}",
                $"{FormatTime(position)} of {FormatTime(duration)}")
            .RequireControllerActivation()
            .Disabled(videoId is null)
            .Busy(pending is not null)
            .FocusUp("youtube.playback.seek-backward")
            .FocusDown("youtube.volume")
            .Classes("youtube-slider");
        var volumeControl = UI.Slider(
                volume,
                0,
                1,
                0.05,
                VolumeActionId,
                "youtube.volume",
                $"Volume {Math.Round(volume * 100)} percent",
                $"{Math.Round(volume * 100)} percent")
            .RequireControllerActivation()
            .Disabled(videoId is null)
            .Busy(pending is not null)
            .FocusUp("youtube.timeline")
            .Classes("youtube-slider", "youtube-volume");

        var status = error ?? StatusText(videoId, state, pending is not null);
        var root = UI.Stack(
                "youtube.root",
                UI.Stack(
                        "youtube.header",
                        UI.Row("youtube.player.heading-row",
                            UI.Button("Back", BackActionId, "youtube.player.back")
                                .Classes("youtube-tertiary"),
                            UI.Text("YOUTUBE VIDEO", "youtube.eyebrow").Classes("youtube-eyebrow")),
                        UI.Text("Link to play", "youtube.title").Classes("youtube-title"),
                        UI.Text(status, "youtube.status")
                            .Classes("youtube-status", error is null ? "is-normal" : "is-error"))
                    .Classes("youtube-header"),
                linkEntry,
                UI.MediaViewport(media, "youtube.viewport").Classes("youtube-viewport"),
                UI.Row("youtube.controls", toggle, seekBack, seekForward).Classes("youtube-controls"),
                UI.Row(
                        "youtube.timeline-row",
                        UI.Text(FormatTime(position), "youtube.position").Classes("youtube-time"),
                        timeline,
                        UI.Text(FormatTime(duration), "youtube.duration").Classes("youtube-time", "is-end"))
                    .Classes("youtube-slider-row"),
                UI.Row(
                        "youtube.volume-row",
                        UI.Text("Volume", "youtube.volume-label").Classes("youtube-label"),
                        volumeControl)
                    .Classes("youtube-slider-row"))
            .InputScope("youtube.root")
            .Classes("youtube-root");
        return new WidgetView(
            root,
            InitialFocusId: "youtube.link",
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
        bool retainSessionWhenHidden) => new()
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
        CompactPinnedSeekStepSeconds = SeekStepSeconds,
        PendingCommand = pendingCommand,
        RetainSessionWhenHidden = retainSessionWhenHidden,
    };

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryHandleApplicationAction(action)) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (action.ActionId == LinkActionId && action.CommittedText is { } committed)
            {
                _link = committed.Trim();
                _playbackError = null;
                if (!YouTubeLinkParser.TryParse(_link, out var parsedVideoId))
                {
                    _validationError = "Enter a supported youtube.com or youtu.be video link.";
                    Invalidate();
                    return ValueTask.CompletedTask;
                }
                _validationError = null;
                _videoId = parsedVideoId;
                _route = YouTubeRoute.Player;
                _position = 0;
                _duration = 0;
                QueueCommand(EmbeddedMediaPlaybackCommandKind.Load, parsedVideoId);
                return ValueTask.CompletedTask;
            }

            if (_videoId is not { } videoId || _pendingCommand is not null)
                return ValueTask.CompletedTask;
            switch (action.ActionId)
            {
                case ToggleActionId:
                    QueueCommand(
                        _state == EmbeddedMediaPlaybackState.Playing
                            ? EmbeddedMediaPlaybackCommandKind.Pause
                            : EmbeddedMediaPlaybackCommandKind.Play,
                        videoId);
                    break;
                case SeekBackwardActionId:
                    QueueCommand(
                        EmbeddedMediaPlaybackCommandKind.Seek,
                        videoId,
                        position: Math.Max(0, _position - SeekStepSeconds));
                    break;
                case SeekForwardActionId:
                    QueueCommand(
                        EmbeddedMediaPlaybackCommandKind.Seek,
                        videoId,
                        position: Math.Min(Math.Max(0, _duration), _position + SeekStepSeconds));
                    break;
                case SeekActionId when action.RequestedValue is { } requested &&
                                           double.IsFinite(requested):
                    QueueCommand(
                        EmbeddedMediaPlaybackCommandKind.Seek,
                        videoId,
                        position: Math.Clamp(requested, 0, Math.Max(0, _duration)));
                    break;
                case VolumeActionId when action.RequestedValue is { } requested &&
                                             double.IsFinite(requested):
                    QueueCommand(
                        EmbeddedMediaPlaybackCommandKind.SetVolume,
                        videoId,
                        volume: Math.Clamp(requested, 0, 1));
                    break;
            }
        }
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(playbackEvent.SurfaceId, SurfaceId, StringComparison.Ordinal))
            return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (playbackEvent.Sequence <= _eventSequence ||
                _videoId is not { } videoId ||
                !string.Equals(playbackEvent.MediaKey, videoId, StringComparison.Ordinal))
                return ValueTask.CompletedTask;
            if (playbackEvent.CommandSequence > 0 &&
                (_pendingCommand is not { } pending ||
                 pending.Sequence != playbackEvent.CommandSequence ||
                 !string.Equals(pending.MediaKey, videoId, StringComparison.Ordinal)))
                return ValueTask.CompletedTask;

            _eventSequence = playbackEvent.Sequence;
            _state = playbackEvent.State;
            _position = Math.Max(0, playbackEvent.PositionSeconds);
            _duration = Math.Max(0, playbackEvent.DurationSeconds);
            _volume = Math.Clamp(playbackEvent.Volume, 0, 1);
            _playbackError = playbackEvent.State == EmbeddedMediaPlaybackState.Error
                ? PlaybackErrorText(playbackEvent.ErrorCode)
                : null;
            if (_pendingCommand is { } current &&
                playbackEvent.CommandSequence == current.Sequence)
                _pendingCommand = null;
        }
        return ValueTask.CompletedTask;
    }

    private void QueueCommand(
        EmbeddedMediaPlaybackCommandKind kind,
        string videoId,
        double? position = null,
        double? volume = null)
    {
        _playbackError = null;
        _pendingCommand = new EmbeddedMediaPlaybackCommand
        {
            Sequence = ++_commandSequence,
            Kind = kind,
            MediaKey = videoId,
            PositionSeconds = position,
            Volume = volume,
        };
        Invalidate();
    }

    private static string StatusText(
        string? videoId,
        EmbeddedMediaPlaybackState state,
        bool pending) =>
        videoId is null ? "Paste a public embeddable YouTube video link." :
        pending || state == EmbeddedMediaPlaybackState.Loading ? "Loading YouTube video…" :
        state switch
        {
            EmbeddedMediaPlaybackState.Playing => "Playing",
            EmbeddedMediaPlaybackState.Paused => "Paused",
            EmbeddedMediaPlaybackState.Ended => "Playback ended",
            EmbeddedMediaPlaybackState.Error => "YouTube playback failed",
            _ => "Ready — press Play to start",
        };

    private static string PlaybackErrorText(string? errorCode) => errorCode switch
    {
        "invalid-video" => "YouTube rejected this video ID.",
        "video-private-or-missing" => "This video is private, unavailable, or no longer exists.",
        "embedding-disabled" => "The video owner does not allow embedded playback.",
        "client-identity-rejected" => "YouTube could not verify this desktop client.",
        "player-api-load-failed" => "The YouTube player could not be loaded. Check the network and retry.",
        "playback-unavailable" => "YouTube could not play this video in the embedded player.",
        "command-unsupported" => "This YouTube player control is unavailable.",
        _ => "YouTube playback failed. Try another public embeddable video.",
    };

    private static string FormatTime(double seconds)
    {
        var value = Math.Max(0, (int)Math.Round(seconds));
        return $"{value / 60}:{value % 60:00}";
    }
}
