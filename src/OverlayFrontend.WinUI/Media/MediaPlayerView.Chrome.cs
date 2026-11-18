using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Grid inlineChrome = new();
    private readonly Grid videoControls = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new(8), Padding = new(0, 12, 0, 0) };
    private PresentationPortal? inlineChromePortal;

    private void EnsureInlineChrome()
    {
        if (disposed || !active || !IsVideo || IsExpanded || changingPlacement ||
            !IsLoaded || !card.IsLoaded || inlineChromePortal is not null ||
            PresentationLayers.Find(this) is not { } layers) return;
        // Video retains its full-size native plane. The same toolbar uses the
        // existing logical-owner portal so text and hit targets stay UI-sized.
        body.Children.Remove(inlineChrome);
        Grid.SetRow(inlineChrome, 0); Grid.SetRowSpan(inlineChrome, 1);
        toolbar.VerticalAlignment = VerticalAlignment.Bottom;
        videoControls.Margin = new(8);
        inlineChromePortal = new(layers, stage, inlineChrome, PresentationLayers.MediaChromeLayer(stage));
    }

    private void RestoreInlineChrome()
    {
        if (inlineChromePortal is null) return;
        Input.PointerCaptureLifetime.ReleaseWithin(inlineChrome);
        inlineChromePortal.Dispose(); inlineChromePortal = null;
        Grid.SetRow(inlineChrome, 0); Grid.SetRowSpan(inlineChrome, 2);
        toolbar.VerticalAlignment = VerticalAlignment.Bottom;
        videoControls.Margin = new(8);
        body.Children.Add(inlineChrome);
    }

    private bool HasImagePresentation => image.Source is not null && imageViewport.Visibility == Visibility.Visible;

    private void LayoutImageChrome()
    {
        // Chrome overlays the canvas; revealing it never changes image fit/zoom.
        Grid.SetRowSpan(stage, 2);
        Grid.SetRow(imageTools, 0);
        Grid.SetRowSpan(imageTools, 2);
        imageTools.VerticalAlignment = VerticalAlignment.Bottom;
        imageTools.HorizontalAlignment = HorizontalAlignment.Center;
        imageTools.Margin = IsExpanded ? new(24) : new(8);
    }

    private bool imageChromeAllowed;
    private void RefreshImageChrome()
    {
        LayoutImageChrome();
        var allowed = HasImagePresentation && active && input;
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
        videoChrome.Stop(); imageChrome.Stop(); imageChromeAllowed = false;
    }

    private void RevealCurrentChrome()
    {
        if (!input || !active) return;
        if (IsVideo) videoChrome.Reveal();
        else if (HasImagePresentation) { RefreshImageChrome(); imageChrome.Reveal(); }
    }
}
