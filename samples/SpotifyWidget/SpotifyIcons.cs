using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal static class SpotifyIcons
{
    internal static WidgetIcon Devices { get; } = Icon("devices", WidgetGlyph.Connection);
    internal static WidgetIcon Search { get; } = Icon("search", WidgetGlyph.Music);
    internal static WidgetIcon Queue { get; } = Icon("queue", WidgetGlyph.Next);
    internal static WidgetIcon Playlists { get; } = Icon("playlists", WidgetGlyph.Music);
    internal static WidgetIcon SignIn { get; } = Icon("sign-in", WidgetGlyph.Connection);
    internal static WidgetIcon Stop { get; } = Icon("stop", WidgetGlyph.Pause);

    private static WidgetIcon Icon(string name, WidgetGlyph fallback) =>
        WidgetIcon.PackageSvg("spotify." + name, WidgetPackageIconColorMode.ThemeTint, fallback);
}
