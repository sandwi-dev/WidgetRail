using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private NativeAnimatedArtwork? imageAnimation;
    private readonly Button imagePlay = new() { Content = new SymbolIcon(Symbol.Play), Visibility = Visibility.Collapsed };
    private bool imageWantsAnimation, imageAnimationManual, animationsAllowed = true;
    private bool? displayedImagePlaying;
    internal bool HasImageAnimation => imageAnimation is not null;
    internal void SetAnimationsAllowed(bool allowed)
    {
        if (!allowed && animationsAllowed) carousel?.StopSlide();
        animationsAllowed = allowed; imageAnimation?.Refresh(); animationVisibility?.Refresh(); RefreshImageAnimationControl();
    }
    private void StartImageAnimation(BitmapImage bitmap)
    {
        StopImageAnimation();
        if (!bitmap.IsAnimatedBitmap) return;
        imageWantsAnimation = definition?.Options.AutoPlay == true;
        imageAnimation = new(image, bitmap, () => !disposed && active && !retainedForPageExit && !lifetime.IsCancellationRequested &&
            imageWantsAnimation && (animationsAllowed || imageAnimationManual) && currentAuthority?.Invoke() != false);
        imagePlay.Visibility = Visibility.Visible;
        RefreshImageAnimationControl(); LayoutImageTools();
    }
    private void ToggleImageAnimation()
    {
        if (!input || !IsImage || imageAnimation is null || image.Source is not BitmapImage bitmap) return;
        imageWantsAnimation = !bitmap.IsPlaying;
        imageAnimationManual = imageWantsAnimation;
        imageAnimation.Refresh(); RefreshImageAnimationControl(); InteractionChanged?.Invoke();
    }
    private void RefreshImageAnimationControl()
    {
        if (imageAnimation is null) return;
        var playing = image.Source is BitmapImage { IsPlaying: true };
        if (displayedImagePlaying == playing) return;
        displayedImagePlaying = playing;
        imagePlay.Content = new SymbolIcon(playing ? Symbol.Pause : Symbol.Play);
        Label(imagePlay, playing ? "Pause animation" : "Play animation", "MediaPlayer.Image.Play");
    }
    private void StopImageAnimation()
    {
        var hadAnimation = imageAnimation is not null;
        imageAnimation?.Dispose(); imageAnimation = null;
        imageWantsAnimation = imageAnimationManual = false;
        displayedImagePlaying = null;
        imagePlay.Visibility = Visibility.Collapsed;
        if (hadAnimation) LayoutImageTools();
    }
}
