using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MediaControlsVisibility videoChrome = null!;
    private MediaControlsVisibility imageChrome = null!;
    private bool changingPlacement;
    private Control? idleVideoControl;
    private void InitializeMediaChrome()
    {
        videoChrome = new([videoControls], () => input && IsPlaying && !changingPlacement,
            () =>
            {
                if (XamlRoot is null) return;
                if (Presentation.DialogChoiceNavigation.ContainsFocus(toolbar))
                {
                    idleVideoControl = FocusManager.GetFocusedElement(XamlRoot) as Control;
                    video.Focus(FocusState.Programmatic);
                }
            }, () => adjustingVolume || HasOptionsMenu || Input.PointerCaptureLifetime.HasCaptureWithin(toolbar),
            () => highContrastChrome ? TimeSpan.Zero : MediaFullscreenLayer.Find(this)?.ControlsFadeDuration ?? TimeSpan.Zero,
            hideWhileHovered: true);
        videoChrome.Initialize();
        imageChrome = new([imageTools], () => input && IsImage && !changingPlacement && !IsExpanded,
            () => { if (Presentation.DialogChoiceNavigation.ContainsFocus(imageTools)) imageViewport.Focus(FocusState.Programmatic); },
            () => galleryDeclaration is not null && !IsExpanded || Input.PointerCaptureLifetime.HasCaptureWithin(imageViewport) ||
                Input.PointerCaptureLifetime.HasCaptureWithin(imageTools) || KeepsChromeFocus(imageTools),
            () => highContrastChrome ? TimeSpan.Zero : MediaFullscreenLayer.Find(this)?.ControlsFadeDuration ?? TimeSpan.Zero);
        imageChrome.Initialize();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(video, "Video. Activate to show playback controls");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(video, "MediaPlayer.Video.Viewport");
        video.UseSystemFocusVisuals = false;
        card.PointerMoved += GalleryPointerMoved;
        imageViewport.PointerMoved += (_, _) => { if (IsImage && input) RevealImageControls(); };
        toolbar.GotFocus += (_, _) => { idleVideoControl = null; if (input && IsVideo && !changingPlacement) videoChrome.Reveal(); };
        video.PointerMoved += GalleryPointerMoved;
        video.PointerPressed += (_, _) => { if (input) { videoChrome.Enter(); PreferredFocus.Focus(FocusState.Pointer); } };
        stage.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        stage.PointerPressed += (_, _) =>
        {
            if (!input && previewPlayback && active) RequestPreviewInteraction?.Invoke();
            if (input && IsVideo && !IsExpanded) { videoChrome.Enter(); PreferredFocus.Focus(FocusState.Pointer); }
        };
        video.GotFocus += (_, _) =>
        {
            if (input && IsVideo && !changingPlacement && !videoChrome.Entered) RevealVideoControls();
            InteractionChanged?.Invoke();
        };
        imageViewport.GotFocus += (_, _) =>
        {
            if (input && IsImage && !changingPlacement && !imageChrome.Entered) EnterCurrentChrome();
        };
        video.PreviewKeyDown += (_, args) =>
        {
            if (args.Handled || !input || Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space or
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
                args.Handled = RevealVideoControls();
        };
        card.KeyDown += OnMediaKeyDown;
        inlineChrome.KeyDown += OnMediaKeyDown;
        // Resident video is a sibling of the chrome, so its keys do not bubble through the card.
        video.KeyDown += OnMediaKeyDown;
    }
    private void OnMediaKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || Input.GamepadKeyBoundary.Owns(this, args) || !input) return;
        if (args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space && IsVideo &&
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), video))
        { if (!RevealVideoControls()) Toggle(); args.Handled = true; return; }
        if (args.Key == Windows.System.VirtualKey.Space && IsVideo) { Toggle(); args.Handled = true; }
        if (args.Key == Windows.System.VirtualKey.Space && IsImage && HasImageAnimation) { ToggleImageAnimation(); args.Handled = true; }
        if (IsVideo) videoChrome.Reveal(); else if (IsImage) RevealImageControls();
    }
    private bool KeepsChromeFocus(FrameworkElement chrome) =>
        Presentation.DialogChoiceNavigation.ContainsFocus(chrome) &&
        (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not Control { FocusState: FocusState.Pointer });

    private bool RevealVideoControls()
    {
        if (!IsVideo || !input || (videoChrome.Visible && !(idleVideoControl is not null && XamlRoot is not null &&
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), video)))) return false;
        var restore = idleVideoControl;
        videoChrome.Enter();
        (restore is { IsEnabled: true, IsLoaded: true, Visibility: Visibility.Visible } &&
            (ReferenceEquals(restore, position) || controls.Contains(restore)) ? restore : PreferredFocus).Focus(FocusState.Keyboard);
        return true;
    }
    private Task ToggleExpandedAsync()
    {
        if (expandedHost is { } existing) { existing.Close(); return Task.CompletedTask; }
        if (!input || disposed || changingPlacement || MediaFullscreenLayer.Find(this) is not { IsOpen: false } host) return Task.CompletedTask;
        var previous = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Control;
        var fromThumbnail = FocusedThumbnail is not null;
        var gallery = galleryDeclaration is not null ? new Grid() : null;
        var content = (FrameworkElement?)gallery ?? card;
        carousel?.StopSlide();
        var origin = gallery is not null ? FittedGalleryBounds(host, (FrameworkElement?)embeddedViewport ?? body) : host.BoundsOf(IsVideo ? video : card);
        if (gallery is null && IsVideo && origin is { } viewport) origin = MediaTransitionGeometry.Fit(viewport, VideoAspectRatio);
        var imageState = CaptureImageViewportForPlacement();
        ClearPendingImageViewportRestore();
        changingPlacement = true;
        expandedHost = host;
        // The shell collapses the original reading row after entry. The native
        // fullscreen layer owns visibility until its card returns to that row.
        if (IsInlineVideo) ObserveMediaPlacement(host);
        return host.ShowAsync(new(this, content, Handle, direction => MoveFocus(direction),
            () =>
            {
                // ScrollViewer may finalize its native viewport after the
                // first arrange. Reconcile against the settled dimensions.
                RestoreImageViewport(imageState);
                changingPlacement = false;
                EnterCurrentChrome();
                if (input && !disposed) (previous is { IsEnabled: true, Visibility: Visibility.Visible } &&
                    !ReferenceEquals(previous, imageViewport) && !(thumbnailPanel is not null && ReferenceEquals(previous.Parent, thumbnailPanel)) ? previous : PreferredFocus).Focus(FocusState.Keyboard);
            },
            () =>
            {
                if (!changingPlacement) imageState = CaptureImageViewportForPlacement();
                ClearPendingImageViewportRestore();
                changingPlacement = true;
                expandedHost = null;
                if (gallery is not null) RestoreInlineGallery(gallery);
                SetExpandedLayout(false);
                if (!disposed && gallery is null) Content = card;
                if (!disposed && IsInlineVideo) ObserveMediaPlacement(this);
                InteractionChanged?.Invoke();
            },
            () =>
            {
                if (disposed || !input || !IsLoaded) return;
                UpdateLayout();
                if (IsCarouselImagePreview && IsImage) FitImage();
                else RestoreImageViewport(imageState);
                changingPlacement = false;
                RevealCurrentChrome();
                var returnControl = previous;
                if (fromThumbnail && thumbnailPanel is { } strip)
                    returnControl = (Control)strip.Children[GalleryIndex];
                (returnControl is { IsEnabled: true, Visibility: Visibility.Visible } ? returnControl : PreferredFocus).Focus(FocusState.Keyboard);
            }, PanImage, () =>
            {
                changingPlacement = false;
                animationVisibility?.Refresh();
            }, Origin: origin,
            Prepare: () => RestoreImageViewport(imageState), Chrome: FullscreenControls(), UseXamlTransform: IsVideo,
            MotionContent: gallery is null ? null : GalleryMotionContent,
            MediaBounds: gallery is not null ? FullscreenGalleryBounds : IsVideo ? () => MediaTransitionGeometry.Fit(video.TransformToVisual(card)
                .TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight)), VideoAspectRatio) : null,
            ReturnOrigin: gallery is not null ? () => FittedGalleryBounds(host, inlineGallerySlot) : null,
            Detach: () =>
            {
                if (gallery is null) Content = null;
                else ExpandGalleryViewport(gallery);
                SetExpandedLayout(true);
            }, RepeatedNavigate: (direction, repeated) => MoveFocus(direction, repeated)));
    }

    private double VideoAspectRatio => player?.PlaybackSession is { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } playback
        ? (double)playback.NaturalVideoWidth / playback.NaturalVideoHeight : 16d / 9;

    private Grid? fullscreenGallery;
    private Border? inlineGallerySlot;
    private FrameworkElement GalleryMotionContent => (FrameworkElement?)embeddedViewport ?? stage;
    private FrameworkElement[] FullscreenControls() => embeddedToolbar is { } buttons
        ? [inlineChrome, imageTools, galleryControls, buttons] : [inlineChrome, imageTools, galleryControls];

    private double GalleryAspectRatio => IsImage && resolved is { Width: > 0, Height: > 0 } original
        ? (double)original.Width / original.Height : embeddedViewport?.Declaration?.AspectRatio ?? VideoAspectRatio;

    private Rect? FittedGalleryBounds(MediaFullscreenLayer host, FrameworkElement? slot) =>
        host.BoundsOf(slot) is { } rect ? MediaTransitionGeometry.Fit(rect, GalleryAspectRatio) : null;

    private Rect? FullscreenGalleryBounds() => fullscreenGallery is { } view
        ? MediaTransitionGeometry.Fit(new(0, 0, view.ActualWidth, view.ActualHeight), GalleryAspectRatio) : null;

    private void ExpandGalleryViewport(Grid destination)
    {
        // Keep the inline frame and strip mounted. Only the live XAML playback
        // subtree and its interactive controls travel to the same-root host.
        var viewport = (FrameworkElement?)embeddedStage ?? body;
        inlineGallerySlot = new Border { MinHeight = viewport.DesiredSize.Height };
        Grid.SetRow(inlineGallerySlot, 1);
        mediaFrame.RowDefinitions[0].MinHeight = mediaFrame.RowDefinitions[0].ActualHeight;
        mediaFrame.RowDefinitions[2].MinHeight = mediaFrame.RowDefinitions[2].ActualHeight;
        InlineGalleryParent.Children.Insert(0, inlineGallerySlot);
        fullscreenGallery = destination;
        Move(body);
        if (embeddedStage is not null) Move(embeddedStage);
        Move(imageTools);
        Move(galleryControls);
        destination.SizeChanged += ExpandedGallerySizeChanged;
        destination.KeyDown += OnMediaKeyDown;
        destination.PointerMoved += GalleryPointerMoved;

        void Move(FrameworkElement child)
        {
            (ReferenceEquals(child, galleryControls) ? mediaFrame : InlineGalleryParent).Children.Remove(child);
            destination.Children.Add(child);
        }
    }

    private void RestoreInlineGallery(Grid expanded)
    {
        expanded.SizeChanged -= ExpandedGallerySizeChanged;
        expanded.KeyDown -= OnMediaKeyDown;
        expanded.PointerMoved -= GalleryPointerMoved;
        expanded.Children.Clear();
        fullscreenGallery = null;
        InlineGalleryParent.Children.Remove(inlineGallerySlot!);
        inlineGallerySlot = null;
        mediaFrame.RowDefinitions[0].MinHeight = mediaFrame.RowDefinitions[2].MinHeight = 0;
        if (disposed) return;
        InlineGalleryParent.Children.Insert(0, body);
        if (embeddedStage is not null) InlineGalleryParent.Children.Add(embeddedStage);
        InlineGalleryParent.Children.Add(imageTools);
        mediaFrame.Children.Add(galleryControls);
    }

    private void ExpandedGallerySizeChanged(object sender, SizeChangedEventArgs args) => ReflowTransport();
    private void GalleryPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!input) return;
        if (embeddedReady) return; // The embedded stage owns its pointer events.
        if (IsVideo && videoChrome.PointerMoved(args)) videoChrome.Reveal();
        else if (IsImage) RevealImageControls();
    }

    private void SetExpandedLayout(bool expanded)
    {
        card.ClearValue(WidthProperty); card.ClearValue(HeightProperty);
        // Float the gallery arrows over the full media viewport. Reserving a
        // header row here shrinks a 16:9 original and leaves a three-sided rim.
        Grid.SetRow(galleryControls, expanded ? 1 : 0);
        galleryControls.VerticalAlignment = expanded ? VerticalAlignment.Center : VerticalAlignment.Top;
        galleryControls.Margin = expanded ? new(24, 0, 24, 0) : new(0);
        galleryControls.Padding = expanded ? new(0) : new(0, 0, 0, 4);
        if (HasGallery)
        {
            galleryPrevious.Content = expanded ? new FontIcon { Glyph = "\uE76B", FontSize = 22 } : new SymbolIcon(Symbol.Back);
            galleryNext.Content = expanded ? new FontIcon { Glyph = "\uE76C", FontSize = 22 } : new SymbolIcon(Symbol.Forward);
        }
        ApplyFrameLayout(expanded);
        Grid.SetRowSpan(stage, 2);
        foreach (var chrome in new FrameworkElement[] { videoControls, imageTools })
        {
            Grid.SetRow(chrome, 0);
            Grid.SetRowSpan(chrome, 2);
            chrome.VerticalAlignment = VerticalAlignment.Bottom;
            chrome.Margin = expanded ? new(24) : new(8);
        }
        Label(expand, expanded ? "Collapse player" : "Expand player", "MediaPlayer.Expand");
        Label(imageExpand, expanded ? "Collapse image" : "Expand image", "MediaPlayer.Image.Expand");
        expand.Content = new SymbolIcon(expanded ? Symbol.BackToWindow : Symbol.FullScreen);
        imageExpand.Content = new SymbolIcon(expanded ? Symbol.BackToWindow : Symbol.FullScreen);
        video.IsTabStop = input && IsVideo;
        imageViewport.UseSystemFocusVisuals = !expanded;
        StopMediaChrome();
        RefreshGalleryControls();
        RevealCurrentChrome();
        RefreshImageChrome();
        LayoutImageTools();
        videoControls.MaxWidth = expanded ? 960 : double.PositiveInfinity;
        ReflowTransport();
        InteractionChanged?.Invoke();
    }

    private CornerRadius inlineRadius;
}
