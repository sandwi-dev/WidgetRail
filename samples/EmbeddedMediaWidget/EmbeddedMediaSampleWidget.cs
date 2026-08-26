using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.EmbeddedMediaWidget;

/// <summary>Provider-neutral native shell over one sealed local media plane.</summary>
public sealed class EmbeddedMediaSampleWidget : Widget
{
    private const string SurfaceId = "embedded-media-sample.primary";
    private readonly object _gate = new();
    private long _commandSequence;
    private long _eventSequence;
    private int _scene;
    private string _mediaKey = "aurora-tone-0";
    private EmbeddedMediaPlaybackState _state = EmbeddedMediaPlaybackState.Ready;
    private double _position;
    private double _duration = 60;
    private EmbeddedMediaPlaybackCommand? _pendingCommand;

    public override WidgetView Render()
    {
        EmbeddedMediaPlaybackState playbackState;
        double position;
        double duration;
        int scene;
        EmbeddedMediaPlaybackCommand? pending;
        lock (_gate)
        {
            playbackState = _state;
            position = _position;
            duration = _duration;
            scene = _scene;
            pending = _pendingCommand;
        }
        var media = new EmbeddedMediaSurface
        {
            Id = SurfaceId,
            AccessibleName = "Provider-neutral local media viewport",
            EntryAsset = "payload/media/adapter.html",
            Surface = new WidgetSurfaceHints { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 180 },
            AspectRatio = 16.0 / 9.0,
            Resources = [new EmbeddedMediaResource { Path = "payload/media/adapter.html", ContentType = "text/html" }],
            Commands = [EmbeddedMediaCommand.Activate, EmbeddedMediaCommand.TogglePlayback, EmbeddedMediaCommand.Previous, EmbeddedMediaCommand.Next, EmbeddedMediaCommand.SeekBackward, EmbeddedMediaCommand.SeekForward],
            PendingCommand = pending,
        };
        var back = UI.Button("Back", "host.embeddedMedia.back", "media-shell.back").FocusDown("media-shell.previous").Classes("media-shell-back");
        var previous = UI.Button("", "host.embeddedMedia.previous", "media-shell.previous").Icon(WidgetGlyph.Previous, "Previous scene").FocusUp("media-shell.back").FocusRight("media-shell.seek-back").Classes("media-shell-transport");
        var seekBack = UI.Button("", "host.embeddedMedia.seekBackward", "media-shell.seek-back").Icon(WidgetGlyph.Previous, "Seek backward ten seconds").FocusUp("media-shell.back").FocusLeft("media-shell.previous").FocusRight("media-shell.play").Classes("media-shell-transport");
        var playing = playbackState == EmbeddedMediaPlaybackState.Playing;
        var play = UI.Button("", "host.embeddedMedia.togglePlayback", "media-shell.play").Icon(playing ? WidgetGlyph.Pause : WidgetGlyph.Play, playing ? "Pause local media" : "Play local media").FocusUp("media-shell.back").FocusLeft("media-shell.seek-back").FocusRight("media-shell.seek-forward").Classes("media-shell-play");
        var seekForward = UI.Button("", "host.embeddedMedia.seekForward", "media-shell.seek-forward").Icon(WidgetGlyph.Next, "Seek forward ten seconds").FocusUp("media-shell.back").FocusLeft("media-shell.play").FocusRight("media-shell.next").Classes("media-shell-transport");
        var next = UI.Button("", "host.embeddedMedia.next", "media-shell.next").Icon(WidgetGlyph.Next, "Next scene").FocusUp("media-shell.back").FocusLeft("media-shell.seek-forward").Classes("media-shell-transport");
        return new WidgetView(
            UI.Stack("media-shell.root",
                UI.Row("media-shell.header", UI.Stack("media-shell.heading",
                    UI.Text("LOCAL MEDIA", "media-shell.eyebrow").Classes("media-shell-eyebrow"),
                    UI.Text($"Aurora Signal {(scene % 8) + 1}", "media-shell.title").Classes("media-shell-title"),
                    UI.Text(StatusText(playbackState), "media-shell.status").Classes("media-shell-status")).Classes("media-shell-heading"), back).Classes("media-shell-header"),
                UI.MediaViewport(media, "media-shell.viewport").Classes("media-shell-viewport"),
                UI.Row("media-shell.timeline", UI.Text(FormatTime(position), "media-shell.position").Classes("media-shell-time"), UI.Progress(position, Math.Max(1, duration), "media-shell.progress", $"{FormatTime(position)} of {FormatTime(duration)}").Classes("media-shell-progress"), UI.Text(FormatTime(duration), "media-shell.duration").Classes("media-shell-time", "is-end")).Classes("media-shell-timeline"),
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
            EmbeddedMediaPlaybackCommandKind? kind = null;
            double? position = null;
            switch (action.ActionId)
            {
                case "host.embeddedMedia.togglePlayback":
                    kind = _state == EmbeddedMediaPlaybackState.Playing
                        ? EmbeddedMediaPlaybackCommandKind.Pause
                        : EmbeddedMediaPlaybackCommandKind.Play;
                    break;
                case "host.embeddedMedia.seekBackward":
                    kind = EmbeddedMediaPlaybackCommandKind.Seek;
                    position = Math.Max(0, _position - 10);
                    break;
                case "host.embeddedMedia.seekForward":
                    kind = EmbeddedMediaPlaybackCommandKind.Seek;
                    position = Math.Min(_duration, _position + 10);
                    break;
                case "host.embeddedMedia.previous":
                    _scene = (_scene + 7) % 8;
                    _mediaKey = $"aurora-tone-{_scene}";
                    kind = EmbeddedMediaPlaybackCommandKind.Load;
                    break;
                case "host.embeddedMedia.next":
                    _scene = (_scene + 1) % 8;
                    _mediaKey = $"aurora-tone-{_scene}";
                    kind = EmbeddedMediaPlaybackCommandKind.Load;
                    break;
            }
            if (kind is { } commandKind)
            {
                _pendingCommand = new EmbeddedMediaPlaybackCommand
                {
                    Sequence = ++_commandSequence,
                    Kind = commandKind,
                    MediaKey = _mediaKey,
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
            _eventSequence = playbackEvent.Sequence;
            _mediaKey = playbackEvent.MediaKey;
            _state = playbackEvent.State;
            _position = playbackEvent.PositionSeconds;
            _duration = playbackEvent.DurationSeconds;
            if (_pendingCommand is { } pending &&
                playbackEvent.CommandSequence == pending.Sequence)
                _pendingCommand = null;
        }
        return ValueTask.CompletedTask;
    }

    private static string StatusText(EmbeddedMediaPlaybackState state) => state switch
    {
        EmbeddedMediaPlaybackState.Playing => "Playing sealed local tone",
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
}
