using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

internal static class GamesAppsIcons
{
    internal static WidgetIcon Library { get; } = Icon("library", WidgetGlyph.Play);
    internal static WidgetIcon Add { get; } = Icon("add", WidgetGlyph.Connection);
    internal static WidgetIcon Running { get; } = Icon("running", WidgetGlyph.Fullscreen);
    internal static WidgetIcon Application { get; } = Icon("application", WidgetGlyph.Play);
    internal static WidgetIcon PreviousPage { get; } = Icon("previous-page", WidgetGlyph.Previous);
    internal static WidgetIcon NextPage { get; } = Icon("next-page", WidgetGlyph.Next);

    private static WidgetIcon Icon(string name, WidgetGlyph fallback) =>
        WidgetIcon.PackageSvg("games-apps." + name, WidgetPackageIconColorMode.ThemeTint, fallback);
}
