using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Shared passive banner surface. Its transparent gutter is part of window placement.</summary>
internal sealed partial class NotificationBannerChrome : Grid, IDisposable
{
    // WidgetOuterShadowMask reserves ceil(offset + 2 * blur + 1) = 29 DIPs.
    internal const double ShadowGutter = 30;
    private readonly DesktopAcrylicSurface material = new();
    private Border surface => material.Paint;
    private readonly WidgetNativeDepth depth;
    internal FrameworkElement CardSurface => surface;
    internal WidgetNativeDepth Depth => depth;

    internal NotificationBannerChrome(NotificationCard card)
    {
        Padding = new(ShadowGutter);
        IsHitTestVisible = false;
        var layers = new Grid();
        var shadow = new Grid { IsHitTestVisible = false };
        var edges = new Grid { IsHitTestVisible = false };
        surface.CornerRadius = new(12);
        surface.Padding = new(16, 8, 16, 8);
        surface.Child = card;
        layers.Children.Add(shadow); layers.Children.Add(material); layers.Children.Add(edges);
        Children.Add(layers);
        depth = new(surface, (shadow, edges), outerShadowOnly: true);
    }

    internal void Apply(ShellChromePalette palette, int opacity, bool reducedTransparency = false, bool acrylicEffects = true)
    {
        var alpha = (byte)Math.Clamp(255 * opacity / 100, 0, 255);
        if (palette.HighContrast)
        {
            material.ApplyMaterial(false,palette.Surface);
            surface.Background = new SolidColorBrush(palette.Surface);
            depth.Apply(new(Microsoft.UI.Colors.Transparent, 0, Vector2.Zero, 12,
                new(2), palette.Text, palette.Text, palette.Text, palette.Text, 1));
            return;
        }
        // Only the material fades with category opacity; text remains fully opaque.
        var acrylic = material.ApplyMaterial(acrylicEffects && !reducedTransparency,palette.Surface);
        material.MaterialOpacity = Math.Clamp(opacity / 100d,0,1);
        if (reducedTransparency) alpha = 255;
        surface.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1),
            GradientStops = {
                new() { Offset = 0, Color = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.White, .045) with { A = alpha } },
                new() { Offset = 1, Color = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.Black, .025) with { A = alpha } }
            }
        };
        if (acrylic) surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var top = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.White, .24);
        var side = ShellChromePalette.Blend(palette.Surface, palette.Text, .14);
        var bottom = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.Black, .28);
        depth.Apply(new(Color.FromArgb(105, 0, 0, 0), 12, new(0, 4), 12,
            new(1), top, side, bottom, side, opacity / 100f));
    }
    public void Dispose() { material.Retire(); depth.Dispose(); }
}
