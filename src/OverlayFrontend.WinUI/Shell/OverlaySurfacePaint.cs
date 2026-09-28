using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static class OverlaySurfacePaint
{
    internal static Color Panel(OverlaySurfaceAppearance policy, IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles)
    {
        if (policy.HighContrast) return new UISettings().GetColorValue(UIColorType.Background);
        if (policy.Effective == WidgetSurfaceAppearance.Transparent) return Microsoft.UI.Colors.Transparent;
        var color = Background(styles, "panel", Color.FromArgb(255, 0x1b, 0x1f, 0x29));
        var opacity = styles?.GetValueOrDefault("panel")?.Base.GetValueOrDefault("opacity")?.Number ?? 1;
        color.A = policy.Effective == WidgetSurfaceAppearance.Solid ? byte.MaxValue :
            (byte)Math.Clamp(Math.Round(color.A * Math.Clamp(opacity, 0, 1)), 0, 255);
        return color;
    }

    internal static Color Background(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, string role, Color fallback) =>
        styles?.GetValueOrDefault(role)?.Base.GetValueOrDefault("background") is { } value &&
        NativeComputedStyleAdapter.TryColor(value.Text, out var color) ? color : fallback;

    internal static void Apply(Border surface, OverlaySurfaceAppearance policy, IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles)
    {
        var color = Panel(policy, styles);
        if (surface.Background is SolidColorBrush brush) brush.Color = color;
        else surface.Background = new SolidColorBrush(color);
    }
}
