using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.EmbeddedMediaWidget;

/// <summary>Provider-neutral native shell over two sealed local media items.</summary>
public sealed class EmbeddedMediaSampleWidget : Widget
{
    private const string SurfaceId = "embedded-media-sample.primary";
    private const double SeekStepSeconds = 2;
    private static readonly MediaItem[] MediaItems =
    [
        new("aurora-video-0", "Aurora Signal"),
        new("horizon-video-1", "Horizon Grid"),
    ];
    private readonly object _gate = new();
    private long _commandSequence;
    private long _eventSequence;
    private int _activeMediaIndex;
    private string _mediaKey = MediaItems[0].Key;
    private EmbeddedMediaPlaybackState _state = EmbeddedMediaPlaybackState.Ready;
    private double _position;
    private double _duration = 60;
    private EmbeddedMediaPlaybackCommand? _pendingCommand;

    public override WidgetView Render()
    {
        EmbeddedMediaPlaybackState playbackState;
        double position;
        double duration;
        int activeMediaIndex;
        EmbeddedMediaPlaybackCommand? pending;
        lock (_gate)
        {
            playbackState = _state;
            position = _position;
            duration = _duration;
            activeMediaIndex = _activeMediaIndex;
            pending = _pendingCommand;
        }
        var media = new EmbeddedMediaSurface
        {
            Id = SurfaceId,
            AccessibleName = "Provider-neutral local media viewport",
            EntryAsset = "payload/media/adapter.html",
            Surface = new WidgetSurfaceHints { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 180 },
            AspectRatio = 16.0 / 9.0,
            Resources =
            [
                new EmbeddedMediaResource { Path = "payload/media/adapter.html", ContentType = "text/html" },
                new EmbeddedMediaResource { Path = "payload/media/sample.mp4", ContentType = "video/mp4" },
                new EmbeddedMediaResource { Path = "payload/media/horizon.mp4", ContentType = "video/mp4" },
            ],
            Commands = [EmbeddedMediaCommand.Activate, EmbeddedMediaCommand.TogglePlayback, EmbeddedMediaCommand.Previous, EmbeddedMediaCommand.Next, EmbeddedMediaCommand.SeekBackward, EmbeddedMediaCommand.SeekForward],
            CompactPinnedPresentation = true,
            CompactPinnedSeekStepSeconds = SeekStepSeconds,
            PendingCommand = pending,
        };
        var back = UI.Button("Back", "host.embeddedMedia.back", "media-shell.back").FocusDown("media-shell.timeline.slider").Classes("media-shell-back");
        var previous = UI.Button("", "host.embeddedMedia.previous", "media-shell.previous").Icon(WidgetGlyph.Previous, "Previous video").FocusUp("media-shell.timeline.slider").FocusRight("media-shell.seek-back").Classes("media-shell-transport");
        var seekBack = UI.Button("-2s", "host.embeddedMedia.seekBackward", "media-shell.seek-back").FocusUp("media-shell.timeline.slider").FocusLeft("media-shell.previous").FocusRight("media-shell.play").Classes("media-shell-transport", "media-shell-quick-seek");
        var playing = playbackState == EmbeddedMediaPlaybackState.Playing;
        var play = UI.Button("", "host.embeddedMedia.togglePlayback", "media-shell.play").Icon(playing ? WidgetGlyph.Pause : WidgetGlyph.Play, playing ? "Pause local media" : "Play local media").FocusUp("media-shell.timeline.slider").FocusLeft("media-shell.seek-back").FocusRight("media-shell.seek-forward").Classes("media-shell-play");
        var seekForward = UI.Button("+2s", "host.embeddedMedia.seekForward", "media-shell.seek-forward").FocusUp("media-shell.timeline.slider").FocusLeft("media-shell.play").FocusRight("media-shell.next").Classes("media-shell-transport", "media-shell-quick-seek");
        var next = UI.Button("", "host.embeddedMedia.next", "media-shell.next").Icon(WidgetGlyph.Next, "Next video").FocusUp("media-shell.timeline.slider").FocusLeft("media-shell.seek-forward").Classes("media-shell-transport");
        var timeline = UI.Slider(
                position,
                0,
                Math.Max(1, duration),
                SeekStepSeconds,
                "host.embeddedMedia.seek",
                "media-shell.timeline.slider",
                $"Playback position. {FormatTime(position)} of {FormatTime(duration)}",
                $"{FormatTime(position)} of {FormatTime(duration)}")
            .RequireControllerActivation()
            .FocusUp("media-shell.back")
            .FocusDown("media-shell.play")
            .Busy(pending is not null)
            .Classes("media-shell-slider");
        return new WidgetView(
            UI.Stack("media-shell.root",
                UI.Row("media-shell.header", UI.Stack("media-shell.heading",
                    UI.Text("LOCAL MEDIA", "media-shell.eyebrow").Classes("media-shell-eyebrow"),
                    UI.Text(MediaItems[activeMediaIndex].Title, "media-shell.title").Classes("media-shell-title"),
                    UI.Text(StatusText(playbackState), "media-shell.status").Classes("media-shell-status")).Classes("media-shell-heading"), back).Classes("media-shell-header"),
                UI.MediaViewport(media, "media-shell.viewport").Classes("media-shell-viewport"),
                UI.Row("media-shell.timeline", UI.Text(FormatTime(position), "media-shell.position").Classes("media-shell-time"), timeline, UI.Text(FormatTime(duration), "media-shell.duration").Classes("media-shell-time", "is-end")).Classes("media-shell-timeline"),
                UI.Row("media-shell.controls", previous, seekBack, play, seekForward, next).Classes("media-shell-controls")).Classes("media-shell-root"),
            InitialFocusId: "media-shell.play", ActiveInputScopeId: "media-shell.root",
            Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard, PreferredWidth = 760, PreferredHeight = 610, MinimumWidth = 440, MinimumHeight = 410 })
        { EmbeddedMedia = media };
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_pendingCommand is not null)
                return ValueTask.CompletedTask;
            EmbeddedMediaPlaybackCommandKind? kind = null;
            double? position = null;
            var commandMediaKey = _mediaKey;
            switch (action.ActionId)
            {
                case "host.embeddedMedia.togglePlayback":
                    kind = _state == EmbeddedMediaPlaybackState.Playing
                        ? EmbeddedMediaPlaybackCommandKind.Pause
                        : EmbeddedMediaPlaybackCommandKind.Play;
                    break;
                case "host.embeddedMedia.seekBackward":
                    kind = EmbeddedMediaPlaybackCommandKind.Seek;
                    position = Math.Max(0, _position - SeekStepSeconds);
                    break;
                case "host.embeddedMedia.seekForward":
                    kind = EmbeddedMediaPlaybackCommandKind.Seek;
                    position = Math.Min(_duration, _position + SeekStepSeconds);
                    break;
                case "host.embeddedMedia.seek" when action.RequestedValue is { } requested &&
                                                         double.IsFinite(requested):
                    kind = EmbeddedMediaPlaybackCommandKind.Seek;
                    position = Math.Clamp(requested, 0, Math.Max(0, _duration));
                    break;
                case "host.embeddedMedia.previous":
                    commandMediaKey = MediaItems[
                        (_activeMediaIndex + MediaItems.Length - 1) % MediaItems.Length].Key;
                    kind = EmbeddedMediaPlaybackCommandKind.Load;
                    break;
                case "host.embeddedMedia.next":
                    commandMediaKey = MediaItems[
                        (_activeMediaIndex + 1) % MediaItems.Length].Key;
                    kind = EmbeddedMediaPlaybackCommandKind.Load;
                    break;
            }
            if (kind is { } commandKind)
            {
                _pendingCommand = new EmbeddedMediaPlaybackCommand
                {
                    Sequence = ++_commandSequence,
                    Kind = commandKind,
                    MediaKey = commandMediaKey,
                    PositionSeconds = position,
                };
                Invalidate();
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
            if (playbackEvent.Sequence <= _eventSequence) return ValueTask.CompletedTask;
            if (playbackEvent.CommandSequence > 0 &&
                (_pendingCommand is not { } pending ||
                 pending.Sequence != playbackEvent.CommandSequence ||
                 !string.Equals(pending.MediaKey, playbackEvent.MediaKey,
                     StringComparison.Ordinal)))
                return ValueTask.CompletedTask;
            _eventSequence = playbackEvent.Sequence;
            if (_pendingCommand is { Kind: EmbeddedMediaPlaybackCommandKind.Load } load &&
                playbackEvent.CommandSequence == load.Sequence)
            {
                var loadedIndex = Array.FindIndex(MediaItems, item =>
                    string.Equals(item.Key, playbackEvent.MediaKey,
                        StringComparison.Ordinal));
                if (loadedIndex >= 0)
                {
                    _activeMediaIndex = loadedIndex;
                    _mediaKey = MediaItems[loadedIndex].Key;
                }
            }
            _state = playbackEvent.State;
            _position = playbackEvent.PositionSeconds;
            _duration = playbackEvent.DurationSeconds;
            if (_pendingCommand is { } current &&
                playbackEvent.CommandSequence == current.Sequence)
                _pendingCommand = null;
        }
        return ValueTask.CompletedTask;
    }

    private static string StatusText(EmbeddedMediaPlaybackState state) => state switch
    {
        EmbeddedMediaPlaybackState.Playing => "Playing sealed local video",
        EmbeddedMediaPlaybackState.Paused => "Paused",
        EmbeddedMediaPlaybackState.Loading => "Loading sealed media",
        EmbeddedMediaPlaybackState.Error => "Local media unavailable",
        _ => "Ready — sealed provider-neutral playback",
    };

    private static string FormatTime(double seconds)
    {
        var value = Math.Max(0, (int)Math.Round(seconds));
        return $"{value / 60}:{value % 60:00}";
    }

    private sealed record MediaItem(string Key, string Title);
}
