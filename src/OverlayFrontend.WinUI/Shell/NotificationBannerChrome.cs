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
    private readonly Border surface = new() { CornerRadius = new(12), Padding = new(16, 8, 16, 8) };
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
        surface.Child = card;
        layers.Children.Add(shadow); layers.Children.Add(surface); layers.Children.Add(edges);
        Children.Add(layers);
        depth = new(surface, (shadow, edges), outerShadowOnly: true);
    }

    internal void Apply(ShellChromePalette palette, int opacity)
    {
        var alpha = (byte)Math.Clamp(255 * opacity / 100, 0, 255);
        if (palette.HighContrast)
        {
            surface.Background = new SolidColorBrush(palette.Surface);
            depth.Apply(new(Microsoft.UI.Colors.Transparent, 0, Vector2.Zero, 12,
                new(2), palette.Text, palette.Text, palette.Text, palette.Text, 1));
            return;
        }
        // Derive depth from the active theme instead of introducing a fixed banner color.
        surface.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1),
            GradientStops = {
                new() { Offset = 0, Color = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.White, .045) with { A = alpha } },
                new() { Offset = 1, Color = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.Black, .025) with { A = alpha } }
            }
        };
        var top = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.White, .24);
        var side = ShellChromePalette.Blend(palette.Surface, palette.Text, .14);
        var bottom = ShellChromePalette.Blend(palette.Surface, Microsoft.UI.Colors.Black, .28);
        depth.Apply(new(Color.FromArgb(105, 0, 0, 0), 12, new(0, 4), 12,
            new(1), top, side, bottom, side, opacity / 100f));
    }
    public void Dispose() => depth.Dispose();
}
