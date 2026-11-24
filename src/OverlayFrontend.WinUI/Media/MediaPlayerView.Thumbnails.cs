using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private const double ThumbnailWidth = 168;
    private const double ThumbnailHeight = ThumbnailWidth * 9 / 16;
    private ScrollViewer? thumbnailScroll;
    private StackPanel? thumbnailPanel;
    private readonly Dictionary<string, Image> thumbnailImages = new(StringComparer.Ordinal);
    private MediaGalleryCarousel? carousel;
    private Grid InlineGalleryParent => carousel?.Center ?? mediaFrame;
    private CancellationTokenSource? thumbnailLifetime;
    private MediaPlayerDefinition? thumbnailDeclaration;
    private object? thumbnailOwner;
    private string? rememberedThumbnail;
    internal DirectionalScrollAnchor? ThumbnailAnchor { get; set; }
    private Control? directionalThumbnail;

    private bool FocusAdjacentThumbnail(Control target)
    {
        if (ThumbnailAnchor is not { } anchor || thumbnailScroll is not { ViewportWidth: > 0 } scroll)
            return target.Focus(FocusState.Keyboard);
        directionalThumbnail = target;
        try
        {
            if (!target.Focus(FocusState.Keyboard)) return false;
            if (!ReferenceEquals(target.Parent, thumbnailPanel)) return true;
            var bounds = target.TransformToVisual(scroll).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
            var viewport = WidgetScrollViewport.ContentBounds(scroll);
            var leading = WidgetScrollViewport.AnchoredLeading(anchor, viewport.Width, bounds.Width);
            var offset = Math.Clamp(scroll.HorizontalOffset + bounds.X - viewport.X - leading, 0, scroll.ScrollableWidth);
            if (Math.Abs(offset - scroll.HorizontalOffset) > .5) scroll.ChangeView(offset, null, null, true);
            return true;
        }
        finally { directionalThumbnail = null; }
    }
    private bool HasThumbnails => galleryDeclaration?.Gallery is { Count: > 1 } items && items[0].PublicThumbnail is not null;
    internal bool UsesInlineThumbnailNavigation => !IsExpanded && galleryDeclaration?.Gallery?[0].PublicThumbnail is not null;
    internal bool HasInlineThumbnailFocus => UsesInlineThumbnailNavigation && !IsMovingPresentation && !HasOptionsMenu && FocusedThumbnail is not null;
    private Button? FocusedThumbnail => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button button &&
        thumbnailPanel is not null && ReferenceEquals(button.Parent, thumbnailPanel) ? button : null;
    private Button? FindThumbnail(string? id) => thumbnailPanel?.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, id));

    internal IEnumerable<Control>? ThumbnailFocusCandidates(ScrollViewer viewport) =>
        ReferenceEquals(viewport, thumbnailScroll) && !IsExpanded ? thumbnailPanel?.Children.OfType<Button>() : null;

    private void FocusMediaEntry()
    {
        if (!IsExpanded && (IsCarouselImagePreview || rememberedThumbnail is not null && rememberedThumbnail == galleryItemId) && thumbnailPanel is { } strip)
        {
            var tile = strip.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, galleryItemId));
            if (tile is { IsEnabled: true } && tile.Focus(FocusState.Keyboard)) return;
        }
        if (CanControl)
        {
            EnterCurrentChrome();
            PreferredFocus.Focus(FocusState.Keyboard);
        }
    }

    private void RefreshThumbnails()
    {
        if (!HasThumbnails || !active || disposed || exhaustedGallery || retainedForPageExit)
        {
            ClearThumbnails();
            return;
        }
        // A provider may withdraw the selected embedded trailer. Keep the surviving
        // native buttons mounted so focus can move before the retired tile leaves.
        if (thumbnailDeclaration is { Gallery: { } previous } && Equals(thumbnailOwner, galleryOwner) &&
            galleryItems is { } remaining && thumbnailPanel is not null &&
            (thumbnailDeclaration != galleryDeclaration || thumbnailPanel.Children.Count != remaining.Count) &&
            previous.Where(item => remaining.Contains(item)).SequenceEqual(remaining) &&
            remaining.All(item => FindThumbnail(item.Id) is not null))
        {
            if (input && !IsExpanded && FocusedThumbnail is { } focused && !remaining.Any(item => Equals(item.Id, focused.Tag)) &&
                FindThumbnail(galleryItemId) is { } replacement)
                FocusAdjacentThumbnail(replacement);
            foreach (var tile in thumbnailPanel.Children.OfType<Button>().Where(button =>
                !remaining.Any(item => Equals(item.Id, button.Tag))).ToArray())
            {
                thumbnailPanel.Children.Remove(tile);
                thumbnailImages.Remove((string)tile.Tag);
            }
            thumbnailDeclaration = galleryDeclaration;
        }
        if (thumbnailDeclaration != galleryDeclaration || !Equals(thumbnailOwner, galleryOwner))
        {
            ClearThumbnails();
            thumbnailDeclaration = galleryDeclaration;
            thumbnailOwner = galleryOwner;
            thumbnailLifetime = new();
            thumbnailPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(4) };
            thumbnailScroll = new()
            {
                Content = thumbnailPanel, IsTabStop = false,
                HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            AutomationProperties.SetName(thumbnailScroll, "Media thumbnails");
            Grid.SetRow(thumbnailScroll, 2);
            mediaFrame.Children.Add(thumbnailScroll);
            foreach (var item in galleryItems!)
                AddThumbnail(item, thumbnailLifetime.Token);
        }
        thumbnailScroll!.IsHitTestVisible = !IsExpanded;
        foreach (var tile in thumbnailPanel!.Children.OfType<Button>())
        {
            tile.IsTabStop = !IsExpanded;
            var selected = Equals(tile.Tag, galleryItemId);
            tile.BorderBrush = selected ? selectedBrush : normalBrush;
            tile.IsEnabled = input;
            AutomationProperties.SetItemStatus(tile, selected ? "Selected" : "");
        }
    }

    private void AddThumbnail(MediaPlayerGalleryItem item, CancellationToken token)
    {
        var picture = new Image { Stretch = Stretch.UniformToFill };
        thumbnailImages.Add(item.Id, picture);
        var layers = new Grid();
        layers.Children.Add(picture);
        if (item.Source.Kind != MediaPlayerSourceKind.WebImage)
        {
            var badge = new Border
            {
                Width = 26, Height = 26, CornerRadius = new(13),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new(1),
                Child = new FontIcon { Glyph = "\uE768", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(4),
            };
            layers.Children.Add(badge);
        }
        var tile = new Button
        {
            Width = ThumbnailWidth, Height = ThumbnailHeight, Padding = new(0), BorderThickness = new(2), Tag = item.Id,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            UseSystemFocusVisuals = true, Content = layers,
        };
        Label(tile, item.Label, "MediaPlayer.Thumbnail." + item.Id, showToolTip: false);
        tile.GotFocus += (_, _) =>
        {
            if (tile.FocusState == FocusState.Keyboard && !changingPlacement && ReferenceEquals(FocusedThumbnail, tile)) SelectThumbnail(item.Id);
            if (!ReferenceEquals(tile, directionalThumbnail))
                tile.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        };
        tile.Click += (_, _) =>
        {
            if (galleryItemId != item.Id) SelectThumbnail(item.Id);
            else if (CanControl) _ = ToggleExpandedAsync();
        };
        thumbnailPanel!.Children.Add(tile);
        _ = LoadThumbnailAsync(layers, picture, item, token);
    }

    private async Task LoadThumbnailAsync(Grid layers, Image target, MediaPlayerGalleryItem item, CancellationToken token)
    {
        try
        {
            if (galleryCurrent?.Invoke() != true) return;
            var payload = new NativeArtworkPayload(ReadOnlyMemory<byte>.Empty, new(item.PublicThumbnail!), IsPublicMedia: true);
#if ENABLE_WIDGET_VALIDATION
            payload = ValidationImagePayload?.Invoke(new(item.PublicThumbnail!)) ?? payload;
#endif
            var bitmap = await NativeArtworkDecoder.DecodeAsync(payload, new((int)(2 * ThumbnailWidth), (int)(2 * ThumbnailHeight)), ImageFit.Cover, token, autoPlay: false);
            if (!token.IsCancellationRequested && galleryCurrent?.Invoke() == true && bitmap is not null)
            {
                target.Source = bitmap.Image;
                carousel?.ThumbnailReady(item.Id, bitmap.Image);
                return;
            }
        }
        // Thumbnail failures leave the named tile available for its independent original.
        catch (Exception error) when (error is not OutOfMemoryException) { }
        if (token.IsCancellationRequested || galleryCurrent?.Invoke() != true || FindThumbnail(item.Id) is null) return;
        var caption = new TextBlock
        {
            Text = item.Label, MaxLines = 2, Margin = new(8), TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(caption, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAutomationId(caption, "MediaPlayer.ThumbnailFallback." + item.Id);
        layers.Children.Insert(0, caption);
    }

    private void SelectThumbnail(string id)
    {
        if (!input || changingPlacement || galleryCurrent?.Invoke() != true || galleryItemId == id || galleryItems is null) return;
        var index = Enumerable.Range(0, galleryItems.Count).FirstOrDefault(i => galleryItems[i].Id == id, -1);
        if (index < 0) return;
        galleryDirection = index < GalleryIndex ? -1 : 1;
        galleryItemId = id;
        definition = null;
        playbackMemory = null;
        ApplyGallerySelection();
    }

    private void ClearThumbnails()
    {
        thumbnailImages.Clear();
        carousel?.Clear();
        thumbnailLifetime?.Cancel();
        thumbnailLifetime?.Dispose();
        if (thumbnailScroll is not null) mediaFrame.Children.Remove(thumbnailScroll);
        thumbnailScroll = null;
        thumbnailPanel = null;
        thumbnailLifetime = null;
        thumbnailDeclaration = null;
        thumbnailOwner = null;
    }

    private void RefreshCarousel()
    {
        if (galleryDeclaration is { InlineCarousel: true, Gallery.Count: > 1 } declaration && !disposed)
        {
            if (carousel is null)
            {
                carousel = new(id =>
                {
                    if (!input || changingPlacement || IsExpanded || galleryCurrent?.Invoke() != true) return;
                    SelectThumbnail(id);
                    if (FindThumbnail(galleryItemId) is { } tile)
                        tile.Focus(FocusState.Programmatic);
                }, id => thumbnailImages.GetValueOrDefault(id)?.Source, () => !disposed && active && galleryCurrent?.Invoke() == true);
                Grid.SetRow(carousel, 1);
                mediaFrame.Children.Insert(0, carousel);
                MoveInlineChildren(mediaFrame, carousel.Center);
                carousel.Center.SizeChanged += CarouselCenterSizeChanged;
            }
            carousel.Update(galleryItems!, GalleryIndex, active && !retainedForPageExit && !exhaustedGallery,
                input && !IsExpanded, !IsExpanded, animationsAllowed && !changingPlacement && !IsExpanded);
        }
        else if (carousel is not null)
        {
            carousel.Clear();
            carousel.Center.SizeChanged -= CarouselCenterSizeChanged;
            MoveInlineChildren(carousel.Center, mediaFrame);
            mediaFrame.Children.Remove(carousel);
            carousel = null;
        }
    }

    private void CarouselCenterSizeChanged(object sender, SizeChangedEventArgs args) => ReflowTransport();

    private void MoveInlineChildren(Grid from, Grid to)
    {
        foreach (var child in new FrameworkElement?[] { body, embeddedStage, imageTools, inlineGallerySlot })
        {
            if (child is null || !from.Children.Contains(child)) continue;
            from.Children.Remove(child);
            to.Children.Add(child);
        }
    }
}
