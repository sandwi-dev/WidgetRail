using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.OverlayFrontend.WinUI.Media;
namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class NativePopupTheme
{
    internal static IDisposable? FullscreenMedia(MediaFullscreenLayer layer)
    {
        var owner = Find(layer);
        return owner?.Track(() => layer.Background = owner.surface);
    }
    internal static IDisposable? MediaPlayer(MediaPlayerView media)
    {
        var owner = Find(media);
        return owner?.Track(() => media.ApplyTheme(owner.surface, owner.ink, owner.muted, owner.selected,
            owner.border, owner.focus, owner.fontSize, owner.fontWeight, owner.font, owner.cornerRadius));
    }
}
