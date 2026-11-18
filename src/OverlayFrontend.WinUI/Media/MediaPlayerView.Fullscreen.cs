using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MediaControlsVisibility videoChrome = null!;
    private MediaControlsVisibility imageChrome = null!;
    private bool changingPlacement;
    private void InitializeMediaChrome()
    {
        videoChrome = new([videoControls], () => input && IsVideo && !changingPlacement && !adjustingVolume && !HasOptionsMenu,
            () =>
            {
                if (XamlRoot is null) return;
                for (var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; focused is not null;
                    focused = VisualTreeHelper.GetParent(focused))
                    if (ReferenceEquals(focused, toolbar)) { video.Focus(FocusState.Programmatic); return; }
            }, () => HasOptionsMenu || Input.PointerCaptureLifetime.HasCaptureWithin(toolbar) ||
                KeepsChromeFocus(toolbar),
            () => highContrastChrome ? TimeSpan.Zero : MediaFullscreenLayer.Find(this)?.ControlsFadeDuration ?? TimeSpan.Zero);
        videoChrome.Initialize();
        imageChrome = new([imageTools], () => input && IsImage && !changingPlacement,
            () => { if (Presentation.DialogChoiceNavigation.ContainsFocus(imageTools)) imageViewport.Focus(FocusState.Programmatic); },
            () => Input.PointerCaptureLifetime.HasCaptureWithin(imageViewport) ||
                Input.PointerCaptureLifetime.HasCaptureWithin(imageTools) || KeepsChromeFocus(imageTools),
            () => highContrastChrome ? TimeSpan.Zero : MediaFullscreenLayer.Find(this)?.ControlsFadeDuration ?? TimeSpan.Zero);
        imageChrome.Initialize();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(video, "Video. Activate to show playback controls");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(video, "MediaPlayer.Video.Viewport");
        video.UseSystemFocusVisuals = false;
        card.PointerMoved += (_, _) => { if (!input) return; if (IsVideo) videoChrome.Reveal(); else if (IsImage) imageChrome.Reveal(); };
        imageViewport.PointerMoved += (_, _) => { if (IsImage && input) imageChrome.Reveal(); };
        video.PointerMoved += (_, _) => { if (IsVideo && input) videoChrome.Reveal(); };
        video.PointerPressed += (_, _) => { if (input) { video.Focus(FocusState.Pointer); videoChrome.Reveal(); } };
        stage.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        stage.PointerPressed += (_, _) =>
        {
            if (!input && previewPlayback && active) RequestPreviewInteraction?.Invoke();
            if (input && IsVideo && !IsExpanded) { videoChrome.Reveal(); PreferredFocus.Focus(FocusState.Pointer); }
        };
        video.GotFocus += (_, _) => InteractionChanged?.Invoke();
        card.KeyDown += OnMediaKeyDown;
        inlineChrome.KeyDown += OnMediaKeyDown;
        // Resident video is a sibling of the chrome, so its keys do not bubble through the card.
        video.KeyDown += OnMediaKeyDown;
        void OnMediaKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Handled) return;
            if (Input.GamepadKeyBoundary.Owns(this, args) || !input) return;
            if (args.Key == Windows.System.VirtualKey.Enter && IsVideo &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), video))
            { if (!RevealVideoControls()) Toggle(); args.Handled = true; return; }
            if (args.Key == Windows.System.VirtualKey.Space && IsVideo) { Toggle(); args.Handled = true; }
            if (IsVideo) videoChrome.Reveal(); else if (IsImage) imageChrome.Reveal();
        }
    }
    private bool KeepsChromeFocus(FrameworkElement chrome) =>
        Presentation.DialogChoiceNavigation.ContainsFocus(chrome) &&
        (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not Control { FocusState: FocusState.Pointer });

    private bool RevealVideoControls()
    {
        if (!IsVideo || !input || videoChrome.Visible) return false;
        videoChrome.Reveal(); PreferredFocus.Focus(FocusState.Keyboard); return true;
    }
    private Task ToggleExpandedAsync()
    {
        if (expandedHost is { } existing) { existing.Close(); return Task.CompletedTask; }
        if (!input || disposed || changingPlacement || MediaFullscreenLayer.Find(this) is not { IsOpen: false } host) return Task.CompletedTask;
        var previous = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Control;
        var origin = host.BoundsOf(IsVideo ? video : card);
        if (IsVideo && origin is { } viewport) origin = MediaTransitionGeometry.Fit(viewport, VideoAspectRatio);
        var imageState = CaptureImageViewportForPlacement();
        ClearPendingImageViewportRestore();
        changingPlacement = true;
        expandedHost = host;
        return host.ShowAsync(new(this, card, Handle, direction => MoveFocus(direction),
            () =>
            {
                // ScrollViewer may finalize its native viewport after the
                // first arrange. Reconcile against the settled dimensions.
                RestoreImageViewport(imageState);
                changingPlacement = false;
                RevealCurrentChrome();
                if (videoStage is not null) videoStage.IsHitTestVisible = input;
                if (input && !disposed) (previous is { IsEnabled: true, Visibility: Visibility.Visible } ? previous : PreferredFocus).Focus(FocusState.Keyboard);
            },
            () =>
            {
                if (!changingPlacement) imageState = CaptureImageViewportForPlacement();
                ClearPendingImageViewportRestore();
                changingPlacement = true;
                expandedHost = null;
                SetExpandedLayout(false);
                if (!disposed) Content = card;
                videoStage?.SetSlot(stage);
                InteractionChanged?.Invoke();
            },
            () =>
            {
                if (disposed || !input || !IsLoaded) return;
                UpdateLayout();
                RestoreImageViewport(imageState);
                changingPlacement = false;
                EnsureInlineChrome();
                RevealCurrentChrome();
                (previous is { IsEnabled: true, Visibility: Visibility.Visible } ? previous : PreferredFocus).Focus(FocusState.Keyboard);
            }, PanImage, () => changingPlacement = false, Origin: origin,
            Prepare: () => RestoreImageViewport(imageState), Chrome: [inlineChrome, imageTools], UseXamlTransform: IsVideo,
            MediaBounds: IsVideo ? () => videoStage?.VideoBounds ?? MediaTransitionGeometry.Fit(video.TransformToVisual(card)
                .TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight)), VideoAspectRatio) : null,
            MotionContent: videoStage, Detach: () =>
            {
                RestoreInlineChrome();
                videoStage?.EnterFullscreen();
                Content = null;
                SetExpandedLayout(true);
            }, RepeatedNavigate: (direction, repeated) => MoveFocus(direction, repeated)));
    }

    private double VideoAspectRatio => player?.PlaybackSession is { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } playback
        ? (double)playback.NaturalVideoWidth / playback.NaturalVideoHeight : 16d / 9;

    private void SetExpandedLayout(bool expanded)
    {
        card.ClearValue(WidthProperty); card.ClearValue(HeightProperty);
        ApplyFrameLayout(expanded);
        if (videoStage is not null) videoStage.IsHitTestVisible = false;
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
        RevealCurrentChrome();
        RefreshImageChrome();
        LayoutImageTools();
        videoControls.MaxWidth = expanded ? 960 : double.PositiveInfinity;
        InteractionChanged?.Invoke();
    }

    private CornerRadius inlineRadius;
}
