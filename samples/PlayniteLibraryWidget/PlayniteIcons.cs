using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteIcons
{
    internal static WidgetIcon Home { get; } = Icon("home", WidgetGlyph.Play);
    internal static WidgetIcon Library { get; } = Icon("library", WidgetGlyph.Fullscreen);
    internal static WidgetIcon Overview { get; } = Icon("overview", WidgetGlyph.Connection);
    internal static WidgetIcon Information { get; } = Icon("information", WidgetGlyph.Connection);
    internal static WidgetIcon Links { get; } = Icon("links", WidgetGlyph.Connection);
    internal static WidgetIcon Achievements { get; } = Icon("achievements", WidgetGlyph.Check);
    internal static WidgetIcon Activity { get; } = Icon("activity", WidgetGlyph.Rewind);
    internal static WidgetIcon Game { get; } = Icon("game", WidgetGlyph.Play);
    internal static WidgetIcon Back { get; } = Icon("back", WidgetGlyph.Previous);
    internal static WidgetIcon Hidden { get; } = Icon("hidden", WidgetGlyph.Fullscreen);

    private static WidgetIcon Icon(string name, WidgetGlyph fallback) =>
        WidgetIcon.PackageSvg("playnite." + name, WidgetPackageIconColorMode.ThemeTint, fallback);
}
