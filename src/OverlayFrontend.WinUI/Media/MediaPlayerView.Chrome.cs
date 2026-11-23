using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Grid inlineChrome = new();
    private readonly Grid videoControls = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new(8), Padding = new(0, 12, 0, 0) };
    private bool HasImagePresentation => image.Source is not null && imageViewport.Visibility == Visibility.Visible;
    private bool IsCarouselImagePreview => !IsExpanded && HasThumbnails && galleryDeclaration?.InlineCarousel == true;

    private void LayoutImageChrome()
    {
        // Fitted galleries use a stable footer outside the image. Ordinary and
        // fullscreen viewers keep their transient overlay controls.
        var footer = galleryDeclaration is not null && !HasThumbnails && !IsExpanded;
        Grid.SetRowSpan(stage, 2);
        Grid.SetRow(imageTools, footer ? 2 : 1);
        Grid.SetRowSpan(imageTools, 1);
        imageTools.VerticalAlignment = VerticalAlignment.Bottom;
        imageTools.HorizontalAlignment = HorizontalAlignment.Center;
        imageTools.Margin = footer ? new(0, 4, 0, 0) : IsExpanded ? new(24) : new(8);
        imageTools.Padding = footer ? new(0) : new(8);
        imageTools.Background = footer && !highContrastChrome ? null : galleryChromeBackground;
        imageTools.CornerRadius = footer ? new(0) : inlineRadius;
    }

    private bool imageChromeAllowed;
    private void RefreshImageChrome()
    {
        LayoutImageChrome();
        imageViewport.IsTabStop = imageViewport.AllowFocusOnInteraction = !IsExpanded && !IsCarouselImagePreview;
        imageViewport.IsHitTestVisible = input && IsImage && !IsCarouselImagePreview;
        var allowed = HasImagePresentation && active && input && !IsCarouselImagePreview;
        if (!allowed)
        {
            imageChrome.Stop();
            imageTools.Visibility = Visibility.Collapsed;
            imageTools.IsHitTestVisible = false;
        }
        else if (!imageChromeAllowed) imageChrome.Reveal();
        imageChromeAllowed = allowed;
        var shown = allowed && imageChrome.Visible;
        foreach (var control in imageControls)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(control, shown
                ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control
                : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(imageZoom, shown
            ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content
            : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    private void StopMediaChrome()
    {
        videoChrome.Stop(); idleVideoControl = null; embeddedChrome?.Stop(); imageChrome.Stop(); imageChromeAllowed = false;
    }

    private void ResetMediaEntry()
    {
        videoChrome.ResetEntry();
        imageChrome.ResetEntry();
        embeddedChrome?.ResetEntry();
        idleVideoControl = null;
    }

    private void EnterCurrentChrome()
    {
        if (!input || !active) return;
        if (embeddedReady) RevealEmbeddedControls(enter: true);
        else if (IsVideo) videoChrome.Enter();
        else if (HasImagePresentation && !IsCarouselImagePreview)
        {
            imageChrome.Enter();
            RefreshImageChrome();
        }
    }

    private void RevealCurrentChrome()
    {
        if (!input || !active) return;
        if (embeddedReady) RevealEmbeddedControls();
        else if (IsVideo) videoChrome.Reveal();
        else if (HasImagePresentation) { RefreshImageChrome(); RevealImageControls(); }
    }

    private void RevealImageControls() { if (imageChromeAllowed) imageChrome.Reveal(); }
}
