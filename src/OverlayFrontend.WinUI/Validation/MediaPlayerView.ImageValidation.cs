using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    // Controlled encoded response bytes for native playback/lifecycle fixtures.
    // The real HTTPS path and bridge authority have separate native/pipe tests.
    internal Func<Uri, NativeArtworkPayload?>? ValidationImagePayload { get; set; }
    // Only the synthetic native fixture substitutes bytes after both real pipes
    // authorize the exact HTTPS declaration. Absent from shipping builds.
    internal static Func<Uri, Uri>? ValidationVideoSource { get; set; }
}
