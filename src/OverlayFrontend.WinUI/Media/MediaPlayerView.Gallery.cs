using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Grid galleryControls = new() { ColumnSpacing = 8, Padding = new(0, 0, 0, 4),
        VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly Button galleryPrevious = new() { Content = new SymbolIcon(Symbol.Back), MinWidth = 44, MinHeight = 44 };
    private readonly Button galleryNext = new() { Content = new SymbolIcon(Symbol.Forward), MinWidth = 44, MinHeight = 44 };
    private readonly TextBlock galleryLabel = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private Control[] galleryButtons = [];
    private Brush? galleryChromeBackground;
    private Brush? galleryArrowBackground;
    private MediaPlayerDefinition? galleryDeclaration;
    // A playback failure removes an occurrence only from this owner's current gallery.
    // Keep the widget declaration intact for source resolution and authority checks.
    private IReadOnlyList<MediaPlayerGalleryItem>? galleryItems;
    private int galleryDirection = 1;
    private bool exhaustedGallery;
    private string? galleryItemId;
    private object? galleryOwner;
    private Viewbox? galleryVideoFit;
    private Func<string?, string?, CancellationToken, Task<HostMediaPlayerSource>>? galleryResolver;
    private Func<bool>? galleryCurrent;
    private Func<string, Task>? galleryRelease;
    internal bool HasGallery => galleryDeclaration?.Gallery is { Count: > 1 };
    internal string? SelectedGalleryItemId => galleryItemId;
    internal bool HasFocusedGalleryAction => GalleryActionLabel is not null;
    private int GalleryIndex => galleryItems is { } items
        ? Enumerable.Range(0, items.Count).FirstOrDefault(index => items[index].Id == galleryItemId) : 0;
    private string? GalleryActionLabel => XamlRoot is null ? null :
        FocusedThumbnail is not null ? "Expand" : ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), galleryPrevious) ? "Previous attachment" :
        ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), galleryNext) ? "Next attachment" : null;

    private void InitializeGallery()
    {
        galleryButtons = [galleryPrevious, galleryNext];
        Label(galleryPrevious, "Previous attachment", "MediaPlayer.Gallery.Previous");
        Label(galleryNext, "Next attachment", "MediaPlayer.Gallery.Next");
        AutomationProperties.SetAutomationId(galleryLabel, "MediaPlayer.Gallery.Position");
        AutomationProperties.SetLiveSetting(galleryLabel, AutomationLiveSetting.Polite);
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            galleryControls.ColumnDefinitions.Add(new() { Width = width });
        galleryControls.Children.Add(galleryPrevious); galleryControls.Children.Add(galleryLabel); galleryControls.Children.Add(galleryNext);
        Grid.SetColumn(galleryLabel, 1); Grid.SetColumn(galleryNext, 2);
        mediaFrame.Children.Add(galleryControls);
        galleryPrevious.Click += (_, _) => SelectGalleryOffset(-1);
        galleryNext.Click += (_, _) => SelectGalleryOffset(1);
        foreach (var button in galleryButtons) button.GotFocus += (_, _) => InteractionChanged?.Invoke();
    }

    internal void Apply(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, object? owner = null,
        Func<string, CancellationToken, Task<HostMediaPlayerSource>>? resolveOwned = null, Func<string, Task>? releaseOwned = null,
        ImageSource? initialPoster = null, Func<string?, string?, CancellationToken, Task<HostMediaPlayerSource>>? resolveGalleryItem = null)
    {
        if (disposed || previewPlayback && !active) return;
        var oldIndex = GalleryIndex;
        var sameOwner = Equals(galleryOwner, owner);
        var oldItems = galleryItems;
        if (!sameOwner || galleryDeclaration != next)
        {
            galleryItems = next.Gallery;
            if (!sameOwner || galleryDeclaration is null) galleryDirection = 1;
            if (exhaustedGallery)
            {
                exhaustedGallery = false;
                Visibility = Visibility.Visible;
            }
        }
        else if (exhaustedGallery)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        if (next.Gallery is null)
        {
            if (galleryVideoFit is not null)
            {
                var index = stage.Children.IndexOf(galleryVideoFit);
                galleryVideoFit.Child = null;
                stage.Children.RemoveAt(index);
                galleryVideoFit = null;
                video.ClearValue(WidthProperty); video.ClearValue(HeightProperty);
                stage.Children.Insert(index, video);
            }
            rememberedThumbnail = null;
            galleryDeclaration = null; galleryItemId = null; galleryOwner = null; galleryResolver = null; galleryCurrent = null; galleryRelease = null;
            RefreshGalleryControls();
            ApplySource(resolve, next, current, owner, resolveOwned, releaseOwned, initialPoster);
            return;
        }
        if (!next.IsWellFormed() || resolveGalleryItem is null) throw new InvalidOperationException("A gallery requires a valid declaration and member resolver.");
        if (galleryVideoFit is null && next.Gallery.Any(item => item.Source.Kind is not (MediaPlayerSourceKind.WebImage or MediaPlayerSourceKind.EmbeddedSession)))
        {
            var index = stage.Children.IndexOf(video);
            stage.Children.RemoveAt(index);
            galleryVideoFit = new Viewbox { Stretch = Stretch.Uniform, Child = video };
            SizeGalleryVideo();
            stage.Children.Insert(index, galleryVideoFit);
        }
        var key = sameOwner && next.DeclaresGalleryItem(galleryItemId) && galleryItemId is not null
            ? galleryItemId : next.Gallery[sameOwner ? Math.Min(oldIndex, next.Gallery.Count - 1) : 0].Id;
        // An embedded provider can also withdraw a trailer in a new declaration.
        if (sameOwner && galleryItemId is not null && !next.DeclaresGalleryItem(galleryItemId) &&
            next.Gallery[0].PublicThumbnail is not null && oldItems is not null)
        {
            for (var i = oldIndex + galleryDirection; i >= 0 && i < oldItems.Count; i += galleryDirection)
                if (next.DeclaresGalleryItem(oldItems[i].Id)) { key = oldItems[i].Id; break; }
        }
        if (!sameOwner || galleryDeclaration is null || !HasThumbnails ||
            rememberedThumbnail is not null && !next.DeclaresGalleryItem(rememberedThumbnail))
            rememberedThumbnail = next.Gallery.Count > 1 && next.Gallery[0].PublicThumbnail is not null ? key : null;
        // A pending resolver carries the old complete declaration. Renew that
        // demand when metadata changes; decoded unchanged content can be retained.
        if (galleryDeclaration != next && !CanControl || galleryItemId != key) { definition = null; playbackMemory = null; }
        galleryDeclaration = next; galleryItemId = key; galleryOwner = owner;
        galleryResolver = resolveGalleryItem; galleryCurrent = current; galleryRelease = releaseOwned;
        ApplyGallerySelection();
    }

    private void SizeGalleryVideo()
    {
        // Keep the native picture at its decoded pixel size. Resizing its
        // output while also animating a scale briefly applies both changes.
        // The in-tree Viewbox fits the same element at either destination.
        if (galleryVideoFit is null) return;
        var size = player?.PlaybackSession;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        video.Width = (size is { NaturalVideoWidth: > 0 } ? size.NaturalVideoWidth : 640) / scale;
        video.Height = (size is { NaturalVideoHeight: > 0 } ? size.NaturalVideoHeight : 360) / scale;
    }

    private void ApplyGallerySelection()
    {
        if (galleryDeclaration is not { } declared || galleryResolver is not { } resolver || galleryItemId is not { } key) return;
        var selected = declared.SelectGalleryItem(key); var owner = galleryOwner;
        bool Current() => !disposed && Equals(galleryOwner, owner) && galleryItemId == key &&
            galleryDeclaration?.DeclaresGalleryItem(key) == true && galleryDeclaration.SelectGalleryItem(key) == selected && galleryCurrent?.Invoke() == true;
        ApplySource(token => resolver(key, null, token), selected, Current, owner,
            (lease, token) => resolver(key, lease, token), galleryRelease,
            initialPoster: declared.InlineCarousel && thumbnailDeclaration == declared && Equals(thumbnailOwner, owner)
                ? carousel?.PosterFor(key) : null, preserveExpanded: IsExpanded);
        RefreshGalleryControls(); InteractionChanged?.Invoke();
    }

    private void SelectGalleryOffset(int offset)
    {
        if (!input || disposed || changingPlacement || expandedHost?.IsTransitioning == true ||
            galleryItems is not { Count: > 0 } items || galleryCurrent?.Invoke() != true) return;
        var index = Math.Clamp(GalleryIndex + offset, 0, items.Count - 1);
        if (items[index].Id == galleryItemId) return;
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        galleryDirection = Math.Sign(offset);
        galleryItemId = items[index].Id; definition = null; playbackMemory = null;
        ApplyGallerySelection();
        if (focused is Control control && galleryButtons.Contains(control))
            (control.IsEnabled ? control : galleryPrevious.IsEnabled ? galleryPrevious : galleryNext).Focus(FocusState.Keyboard);
    }

    private bool AdvancePastUnavailableItem()
    {
        if (galleryItems is not { Count: > 0 } items || galleryCurrent?.Invoke() != true ||
            items[0].PublicThumbnail is null) return false;
        var index = GalleryIndex;
        var selected = items[index];
        var autoplayTrailer = (selected.Options ?? galleryDeclaration!.Options).AutoPlay &&
            selected.Source.Kind is MediaPlayerSourceKind.WebUrl or MediaPlayerSourceKind.EmbeddedSession;
        var carouselImage = galleryDeclaration!.InlineCarousel && selected.Source is { IsPublicMedia: true, Kind: MediaPlayerSourceKind.WebImage };
        if (!autoplayTrailer && !carouselImage)
            return false;
        var remaining = items.Where(item => item.Id != galleryItemId).ToArray();
        galleryItems = remaining;
        if (remaining.Length == 0)
        {
            exhaustedGallery = true;
            return false;
        }
        var nextIndex = Math.Clamp(galleryDirection < 0 ? index - 1 : index, 0, remaining.Length - 1);
        var failedId = galleryItemId;
        galleryItemId = remaining[nextIndex].Id;
        if (rememberedThumbnail == failedId) rememberedThumbnail = galleryItemId;
        RefreshThumbnails();
        definition = null;
        playbackMemory = null;
        ApplyGallerySelection();
        return true;
    }

    private void RefreshGalleryControls()
    {
        // Reserve the same toolbar space before and after preview entry. The
        // count is useful while passive; input authority still gates the buttons.
        RefreshThumbnails();
        RefreshCarousel();
        galleryControls.Visibility = HasGallery && (!HasThumbnails || IsExpanded) && !retainedForPageExit ? Visibility.Visible : Visibility.Collapsed;
        galleryLabel.Visibility = IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        // In fullscreen only the two compact controls remain; an empty full-width
        // ribbon would obscure content after the filename has been removed.
        galleryControls.Background = highContrastChrome && !IsExpanded ? galleryChromeBackground : null;
        galleryControls.BorderThickness = new(!IsExpanded && highContrastChrome ? 1 : 0);
        foreach (var button in galleryButtons)
        {
            button.Background = IsExpanded ? galleryArrowBackground : normalButtonBrush;
            button.CornerRadius = IsExpanded ? new(26) : inlineRadius;
            button.Width = button.Height = IsExpanded ? 52 : double.NaN;
        }
        var index = GalleryIndex; var items = galleryItems;
        galleryPrevious.IsEnabled = input && index > 0;
        galleryNext.IsEnabled = input && items is not null && index + 1 < items.Count;
        galleryPrevious.Visibility = IsExpanded && index == 0 ? Visibility.Collapsed : Visibility.Visible;
        galleryNext.Visibility = IsExpanded && items is not null && index == items.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        var item = items is { Count: > 0 } ? items[index] : null;
        galleryLabel.Text = item is null ? "" : $"{index + 1} / {items!.Count}";
        AutomationProperties.SetName(galleryLabel, item is null ? "" : $"{index + 1} of {items!.Count}, {item.Label}");
        NativePopupTheme.SetToolTip(galleryLabel, item?.Label);
    }

    private bool ActivateGalleryControl()
    {
        if (!input || !HasGallery || XamlRoot is null) return false;
        var focus = FocusManager.GetFocusedElement(XamlRoot);
        if (FocusedThumbnail is not null) { if (CanControl) _ = ToggleExpandedAsync(); return true; }
        if (ReferenceEquals(focus, galleryPrevious)) { SelectGalleryOffset(-1); return true; }
        if (ReferenceEquals(focus, galleryNext)) { SelectGalleryOffset(1); return true; }
        return false;
    }

    private bool MoveGalleryFocus(FocusNavigationDirection direction)
    {
        if (!input || !HasGallery || XamlRoot is null) return false;
        var focus = FocusManager.GetFocusedElement(XamlRoot);
        if (!IsExpanded && thumbnailPanel is { } strip)
        {
            if (FocusedThumbnail is { } tile)
            {
                var offset = direction == FocusNavigationDirection.Left ? -1 : direction == FocusNavigationDirection.Right ? 1 : 0;
                if (offset == 0)
                {
                    if (direction != FocusNavigationDirection.Up || (IsCarouselImagePreview && IsImage)) return false;
                    EnterCurrentChrome();
                    return PreferredFocus.Focus(FocusState.Keyboard);
                }
                var target = Math.Clamp(strip.Children.IndexOf(tile) + offset, 0, strip.Children.Count - 1);
                return FocusAdjacentThumbnail((Control)strip.Children[target]);
            }
            if (direction == FocusNavigationDirection.Down && !ReferenceEquals(focus, position))
                return ((Control)strip.Children[GalleryIndex]).Focus(FocusState.Keyboard);
            return false;
        }
        if (ReferenceEquals(focus, galleryPrevious) || ReferenceEquals(focus, galleryNext))
        {
            if (direction == FocusNavigationDirection.Down)
            {
                EnterCurrentChrome();
                return (IsImage ? (Control)(IsExpanded ? imageFit : imageViewport) : embeddedReady ? PreferredEmbeddedControl : CanControl ? play : this).Focus(FocusState.Keyboard);
            }
            if (direction == FocusNavigationDirection.Left) { if (galleryPrevious.IsEnabled) galleryPrevious.Focus(FocusState.Keyboard); return true; }
            if (direction == FocusNavigationDirection.Right) { if (galleryNext.IsEnabled) galleryNext.Focus(FocusState.Keyboard); return true; }
            return IsExpanded;
        }
        if (direction == FocusNavigationDirection.Up && (IsExpanded && IsImage && focus is Button tool && imageControls.Contains(tool) ||
            ReferenceEquals(focus, imageViewport) || ReferenceEquals(focus, video) || ReferenceEquals(focus, position) || ReferenceEquals(focus, this)))
            return (galleryPrevious.IsEnabled ? galleryPrevious : galleryNext).Focus(FocusState.Keyboard);
        return false;
    }

    private void RefreshGalleryFullscreen()
    {
        if (galleryDeclaration is null || expandedHost is not { IsTransitioning: false } host) return;
        RefreshGalleryControls();
        ApplyFrameLayout(true);
        host.UpdateMedia(this, IsVideo, FullscreenGalleryBounds, GalleryMotionContent, FullscreenControls());
    }
}
