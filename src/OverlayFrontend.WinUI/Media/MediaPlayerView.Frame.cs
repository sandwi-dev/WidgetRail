using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private Brush? authoredFrameBackground, authoredFrameBorder, themeFrameBorder;
    private Thickness? authoredFramePadding, authoredFrameThickness;
    private CornerRadius? authoredFrameRadius;

    // Existing WRSS surface properties belong to the actual media frame, not
    // a second ContentControl frame around it. Transport chrome keeps its theme.
    internal void ApplyFrameStyle(Brush? background, Brush? border, Thickness? padding,
        Thickness? thickness, CornerRadius? radius)
    {
        authoredFrameBackground = background; authoredFrameBorder = border;
        authoredFramePadding = padding; authoredFrameThickness = thickness; authoredFrameRadius = radius;
        if (!disposed) ApplyFrameLayout(IsExpanded);
    }

    private void ApplyFrameLayout(bool expanded)
    {
        expanded &= fullscreenGallery is null;
        card.Padding = expanded ? new(0) : authoredFramePadding ?? new(PassiveAnimation ? 0 : 8);
        card.BorderThickness = expanded ? new(0) : authoredFrameThickness ?? new(PassiveAnimation ? 0 : 1);
        card.CornerRadius = expanded ? new(0) : authoredFrameRadius ?? inlineRadius;
        card.BorderBrush = authoredFrameBorder ?? themeFrameBorder;
        card.Background = expanded ? normalBrush : authoredFrameBackground ?? (PassiveAnimation ? null : normalBrush);
    }
}
