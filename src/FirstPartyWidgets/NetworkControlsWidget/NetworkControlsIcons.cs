using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal static class NetworkControlsIcons
{
    internal static WidgetIcon Back { get; } = WidgetIcon.PackageSvg(
        "network-controls.back", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Previous);
    internal static WidgetIcon Bluetooth { get; } = WidgetIcon.PackageSvg(
        "network-controls.bluetooth", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Connection);
}
