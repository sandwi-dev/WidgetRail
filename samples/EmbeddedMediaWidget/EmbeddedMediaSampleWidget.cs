using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.EmbeddedMediaWidget;

/// <summary>Provider-neutral native shell over one sealed local media plane.</summary>
public sealed class EmbeddedMediaSampleWidget : Widget
{
    private const string SurfaceId = "embedded-media-sample.primary";

    public override WidgetView Render()
    {
        var media = new EmbeddedMediaSurface
        {
            Id = SurfaceId,
            AccessibleName = "Provider-neutral local media viewport",
            EntryAsset = "payload/media/adapter.html",
            Surface = new WidgetSurfaceHints { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 180 },
            AspectRatio = 16.0 / 9.0,
            Resources = [new EmbeddedMediaResource { Path = "payload/media/adapter.html", ContentType = "text/html" }],
            Commands = [EmbeddedMediaCommand.Activate, EmbeddedMediaCommand.TogglePlayback, EmbeddedMediaCommand.Previous, EmbeddedMediaCommand.Next, EmbeddedMediaCommand.SeekBackward, EmbeddedMediaCommand.SeekForward],
        };
        var back = UI.Button("Back", "host.embeddedMedia.back", "media-shell.back").FocusDown("media-shell.previous").Classes("media-shell-back");
        var previous = UI.Button("", "host.embeddedMedia.previous", "media-shell.previous").Icon(WidgetGlyph.Previous, "Previous scene").FocusUp("media-shell.back").FocusRight("media-shell.seek-back").Classes("media-shell-transport");
        var seekBack = UI.Button("", "host.embeddedMedia.seekBackward", "media-shell.seek-back").Icon(WidgetGlyph.Previous, "Seek backward ten seconds").FocusUp("media-shell.back").FocusLeft("media-shell.previous").FocusRight("media-shell.play").Classes("media-shell-transport");
        var play = UI.Button("", "host.embeddedMedia.togglePlayback", "media-shell.play").Icon(WidgetGlyph.Play, "Play or pause local media").FocusUp("media-shell.back").FocusLeft("media-shell.seek-back").FocusRight("media-shell.seek-forward").Classes("media-shell-play");
        var seekForward = UI.Button("", "host.embeddedMedia.seekForward", "media-shell.seek-forward").Icon(WidgetGlyph.Next, "Seek forward ten seconds").FocusUp("media-shell.back").FocusLeft("media-shell.play").FocusRight("media-shell.next").Classes("media-shell-transport");
        var next = UI.Button("", "host.embeddedMedia.next", "media-shell.next").Icon(WidgetGlyph.Next, "Next scene").FocusUp("media-shell.back").FocusLeft("media-shell.seek-forward").Classes("media-shell-transport");
        return new WidgetView(
            UI.Stack("media-shell.root",
                UI.Row("media-shell.header", UI.Stack("media-shell.heading",
                    UI.Text("LOCAL MEDIA", "media-shell.eyebrow").Classes("media-shell-eyebrow"),
                    UI.Text("Aurora Signal", "media-shell.title").Classes("media-shell-title"),
                    UI.Text("Sealed provider-neutral playback", "media-shell.status").Classes("media-shell-status")).Classes("media-shell-heading"), back).Classes("media-shell-header"),
                UI.MediaViewport(media, "media-shell.viewport").Classes("media-shell-viewport"),
                UI.Row("media-shell.timeline", UI.Text("0:00", "media-shell.position").Classes("media-shell-time"), UI.Progress(18, 60, "media-shell.progress", "18 seconds of 60 seconds").Classes("media-shell-progress"), UI.Text("1:00", "media-shell.duration").Classes("media-shell-time", "is-end")).Classes("media-shell-timeline"),
                UI.Row("media-shell.controls", previous, seekBack, play, seekForward, next).Classes("media-shell-controls")).Classes("media-shell-root"),
            InitialFocusId: "media-shell.play", ActiveInputScopeId: "media-shell.root",
            Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard, PreferredWidth = 760, PreferredHeight = 610, MinimumWidth = 440, MinimumHeight = 410 })
        { EmbeddedMedia = media };
    }
}
