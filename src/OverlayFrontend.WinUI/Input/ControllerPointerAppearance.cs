namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal readonly record struct ControllerPointerAppearance(uint Fill, uint Outline)
{
    internal const double Diameter = 16;
    internal const double Border = 2;
    internal static ControllerPointerAppearance Default => new(0xFFFFFFFF, 0xFF000000);
}
