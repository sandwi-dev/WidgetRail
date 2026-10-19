using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Paint-only preview frame. The surface owns input; shadow and edge slots never do.</summary>
internal sealed partial class WidgetPreviewFrame : Grid, IDisposable
{
    internal Border Surface { get; } = new();
    internal WidgetNativeDepth Depth { get; }
    internal double ShadowInset { get; private set; } = 30;
    private PreviewFrameStyle? current;
    private readonly LinearGradientBrush paint = new() { StartPoint = new(0, 0), EndPoint = new(0, 1) };

    internal WidgetPreviewFrame()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        var shadow = new Grid { IsHitTestVisible = false };
        var edges = new Grid { IsHitTestVisible = false };
        Children.Add(shadow); Children.Add(Surface); Children.Add(edges);
        Depth = new(Surface, (shadow, edges), outerShadowOnly: true);
    }

    internal bool Apply(PreviewFrameStyle style)
    {
        if (current == style) return false;
        current = style;
        Surface.CornerRadius = new(style.Radius);
        Surface.BorderThickness = new(style.BorderWidth);
        Surface.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Surface.Padding = new(12);
        if (style.HighContrast) Surface.Background = new SolidColorBrush(style.Surface);
        else
        {
            paint.GradientStops.Clear();
            paint.GradientStops.Add(new() { Offset = 0, Color = Shade(style.Surface, style.Shading) });
            paint.GradientStops.Add(new() { Offset = 1, Color = Shade(style.Surface, -style.Shading) });
            Surface.Background = paint;
        }
        Depth.Apply(new(style.Shadow, style.Blur, style.Offset, style.Radius, new(style.BorderWidth),
            style.Top, style.Side, style.Bottom, style.Side, 1));
        ShadowInset = style.Shadow.A == 0 ? 16 : Math.Max(16, Math.Ceiling(2 * style.Blur + Math.Max(Math.Abs(style.Offset.X), Math.Abs(style.Offset.Y)) + 1));
        return true;
    }

    private static Color Shade(Color color, double amount)
    {
        var shaded = ShellChromePalette.Blend(color, amount >= 0 ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black, Math.Abs(amount));
        return shaded with { A = color.A };
    }

    public void Dispose() => Depth.Dispose();
}

/// <summary>Bounded theme metrics in widget-local DIPs; portal scaling is applied only once.</summary>
internal sealed record PreviewFrameStyle(Color Surface, double Shading, float Radius, double BorderWidth,
    Color Top, Color Side, Color Bottom, Color Shadow, float Blur, Vector2 Offset, bool HighContrast)
{
    internal static PreviewFrameStyle Resolve(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles,
        ShellChromePalette palette, AppearanceSettings appearance)
    {
        var role = styles?.GetValueOrDefault("preview")?.Base;
        Color ColorValue(string property, Color fallback) => role?.GetValueOrDefault(property) is { } value &&
            NativeComputedStyleAdapter.TryColor(value.Text, out var color) ? color : fallback;
        double Number(string property, double fallback, double minimum, double maximum) =>
            role?.GetValueOrDefault(property) is { Number: { } value, Unit: null or "px" } && double.IsFinite(value)
                ? Math.Clamp(value, minimum, maximum) : fallback;
        var radius = (float)Number("corner-radius", 10, 0, 24);
        if (palette.HighContrast)
            return new(palette.Surface, 0, radius, 2, palette.Text, palette.Text, palette.Text,
                Microsoft.UI.Colors.Transparent, 0, Vector2.Zero, true);
        var fill = ColorValue("background", palette.Surface);
        fill.A = appearance.Transparency == TransparencyPreference.Reduced ? byte.MaxValue :
            (byte)Math.Round(fill.A * Number("opacity", 1, 0, 1));
        var border = ColorValue("border-color", ShellChromePalette.Blend(palette.Surface, palette.Text, .16));
        return new(fill, Number("surface-shading", .035, -.25, .25), radius, Number("border-width", 1, 0, 4),
            ColorValue("border-top-color", border), border, ColorValue("border-bottom-color", border),
            ColorValue("shadow-color", Color.FromArgb(90, 0, 0, 0)), (float)Number("shadow-blur", 12, 0, 24),
            new((float)Number("shadow-offset-x", 0, -12, 12), (float)Number("shadow-offset-y", 4, -12, 12)), false);
    }
}
