using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class MusicIcons
{
    internal static WidgetIcon Home { get; } = Icon("home");
    internal static WidgetIcon Search { get; } = Icon("search");
    internal static WidgetIcon Library { get; } = Icon("library");
    internal static WidgetIcon Queue { get; } = Icon("queue");

    internal static WidgetIcon ForTab(string tab) => tab switch
    {
        "home" => Home, "search" => Search, "library" => Library, "queue" => Queue,
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    private static WidgetIcon Icon(string name) =>
        WidgetIcon.PackageSvg("ytmusic." + name, WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Music);
}
