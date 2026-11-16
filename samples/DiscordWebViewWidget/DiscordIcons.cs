using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

internal static class DiscordIcons
{
    internal static WidgetIcon Mark=>WidgetIcon.PackageSvg("discord.mark",WidgetPackageIconColorMode.OriginalColor,WidgetGlyph.Connection);
    internal static WidgetIcon Home=>Get("home",WidgetGlyph.Connection);
    internal static WidgetIcon Voice=>Get("voice",WidgetGlyph.Volume);
    internal static WidgetIcon Info=>Get("info",WidgetGlyph.Connection);
    internal static WidgetIcon Channels=>Get("channels",WidgetGlyph.Connection);
    internal static WidgetIcon Screen=>Get("screen",WidgetGlyph.Fullscreen);
    internal static WidgetIcon Watching=>Get("watching",WidgetGlyph.Play);
    internal static WidgetIcon Microphone=>Get("microphone",WidgetGlyph.Microphone);
    internal static WidgetIcon Muted=>Get("muted",WidgetGlyph.Muted);
    internal static WidgetIcon Leave=>Get("leave",WidgetGlyph.Connection);
    internal static WidgetIcon Pin=>Get("pin",WidgetGlyph.Fullscreen);
    internal static WidgetIcon ManagePin=>Get("manage-pin",WidgetGlyph.Settings);
    internal static WidgetIcon Person=>Get("person",WidgetGlyph.Connection);
    internal static WidgetIcon Refresh=>Get("refresh",WidgetGlyph.Refresh);
    internal static WidgetIcon Account=>Get("account",WidgetGlyph.Connection);
    internal static WidgetIcon Activity=>Get("activity",WidgetGlyph.Play);
    internal static WidgetIcon Share=>Get("share",WidgetGlyph.Fullscreen);
    private static WidgetIcon Get(string name,WidgetGlyph fallback)=>
        WidgetIcon.PackageSvg("discord."+name,WidgetPackageIconColorMode.ThemeTint,fallback);
}
