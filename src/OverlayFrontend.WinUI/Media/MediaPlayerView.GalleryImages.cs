using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private WidgetMotionOptions? galleryMotionOptions;
    private WidgetMotionHost? galleryImageHost, previousImageHost;
    private Image? previousGalleryImage;
    private WidgetPageMotion? galleryImageMotion;
    private MediaPlayerGalleryItem? displayedGalleryImage;
    private long galleryImageExpiry;

    internal void SetMediaMotion(WidgetMotionOptions options)
    {
        if (galleryMotionOptions != options) StopGalleryImageTransition();
        galleryMotionOptions = options;
        SetAnimationsAllowed(!options.Reduced);
    }

    private void InitializeGalleryImageLayers()
    {
        imageCanvas.Children.Remove(image);
        previousGalleryImage = new Image { Stretch = Stretch.Uniform };
        previousImageHost = new(previousGalleryImage) { IsHitTestVisible = false };
        galleryImageHost = new(image) { IsHitTestVisible = false };
        imageCanvas.Children.Add(previousImageHost);
        imageCanvas.Children.Add(galleryImageHost);
    }

    private bool CanBridgeGallerySelection(object? owner) =>
        HasGallery && galleryDeclaration?.InlineCarousel == false && Equals(resourceOwner, owner) &&
        displayedGalleryImage is { } shown && image.Source is BitmapImage { PixelWidth: > 0 } &&
        galleryImageExpiry > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() &&
        galleryDeclaration.Gallery!.Any(item => item == shown);

    private void PresentGalleryImage(BitmapImage bitmap)
    {
        var incoming = galleryDeclaration?.Gallery?.FirstOrDefault(item => item.Id == galleryItemId);
        var animate = incoming is not null && displayedGalleryImage?.Id != incoming.Id && CanBridgeGallerySelection(resourceOwner);
        var previous = image.Source;
        displayedGalleryImage = incoming;
        galleryImageExpiry = resolved!.ExpiresAtUnixMilliseconds ?? long.MaxValue;
        image.Source = bitmap;
        if (!animate || !animationsAllowed || galleryMotionOptions is not { } options ||
            galleryImageHost is null || previousImageHost is null || previousGalleryImage is null ||
            imageCanvas.ActualWidth <= 0 || imageCanvas.ActualHeight <= 0) return;
        var recipe = WidgetMotionPolicy.Section(options, new((float)imageCanvas.ActualWidth, (float)imageCanvas.ActualHeight), galleryDirection);
        if (recipe.Incoming.Duration == TimeSpan.Zero) return;
        previousGalleryImage.Source = previous;
        var motion = new WidgetPageMotion(new[] { (previousImageHost, recipe.Outgoing), (galleryImageHost, recipe.Incoming) });
        galleryImageMotion = motion;
        _ = CompleteGalleryImageTransitionAsync(motion);
    }

    private async Task CompleteGalleryImageTransitionAsync(WidgetPageMotion motion)
    {
        await motion.PlayAsync();
        if (ReferenceEquals(galleryImageMotion, motion)) StopGalleryImageTransition();
    }

    private void StopGalleryImageTransition()
    {
        var motion = galleryImageMotion;
        galleryImageMotion = null;
        motion?.Dispose();
        if (previousGalleryImage is not null) previousGalleryImage.Source = null;
    }
}
