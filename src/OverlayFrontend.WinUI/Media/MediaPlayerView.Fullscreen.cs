using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MediaControlsVisibility videoChrome = null!;
    private bool changingPlacement;
    private void InitializeMediaChrome()
    {
        videoChrome = new([toolbar], () => IsExpanded && IsPlaying && !adjustingVolume,
            () => { if (IsExpanded && XamlRoot is not null) video.Focus(FocusState.Programmatic); });
        videoChrome.Initialize();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(video, "Video. Activate to show playback controls");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(video, "MediaPlayer.Video.Viewport");
        video.UseSystemFocusVisuals = false;
        card.PointerMoved += (_, _) => { if (IsVideo && IsExpanded) videoChrome.Reveal(); };
        video.PointerMoved += (_, _) => { if (IsVideo && IsExpanded) videoChrome.Reveal(); };
        video.PointerPressed += (_, _) => { if (input) { video.Focus(FocusState.Pointer); videoChrome.Reveal(); } };
        stage.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        stage.PointerPressed += (_, _) => { if (input && IsVideo && !IsExpanded) PreferredFocus.Focus(FocusState.Pointer); };
        video.GotFocus += (_, _) => InteractionChanged?.Invoke();
        card.KeyDown += OnMediaKeyDown;
        // Resident video is a sibling of the chrome, so its keys do not bubble through the card.
        video.KeyDown += OnMediaKeyDown;
        void OnMediaKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Handled) return;
            if (Input.GamepadKeyBoundary.Owns(this, args) || !input) return;
            if (args.Key == Windows.System.VirtualKey.Space && IsVideo) { Toggle(); args.Handled = true; }
            if (IsVideo && IsExpanded) videoChrome.Reveal();
        }
    }
    private bool RevealVideoControls()
    {
        if (!IsVideo || !IsExpanded || videoChrome.Visible) return false;
        videoChrome.Reveal(); PreferredFocus.Focus(FocusState.Keyboard); return true;
    }
    private Task ToggleExpandedAsync()
    {
        if (expandedHost is { } existing) { existing.Close(); return Task.CompletedTask; }
        if (!input || disposed || changingPlacement || MediaFullscreenLayer.Find(this) is not { IsOpen: false } host) return Task.CompletedTask;
        var previous = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Control;
        var origin = host.BoundsOf(IsVideo ? video : card);
        if (IsVideo && origin is { } viewport) origin = MediaTransitionGeometry.Fit(viewport, VideoAspectRatio);
        var imageState = RememberImageViewport();
        changingPlacement = true;
        expandedHost = host;
        videoStage?.EnterFullscreen();
        Content = null;
        SetExpandedLayout(true);
        return host.ShowAsync(new(this, card, Handle, direction => MoveFocus(direction),
            () =>
            {
                // ScrollViewer may finalize its native viewport after the
                // first arrange. Reconcile against the settled dimensions.
                RestoreImageViewport(imageState);
                changingPlacement = false;
                if (videoStage is not null) videoStage.IsHitTestVisible = input;
                if (input && !disposed) (previous is { IsEnabled: true, Visibility: Visibility.Visible } ? previous : PreferredFocus).Focus(FocusState.Keyboard);
            },
            () =>
            {
                if (!changingPlacement) imageState = RememberImageViewport();
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
                (previous is { IsEnabled: true, Visibility: Visibility.Visible } ? previous : PreferredFocus).Focus(FocusState.Keyboard);
            }, PanImage, () => changingPlacement = false, Origin: origin,
            Prepare: () => RestoreImageViewport(imageState), Chrome: [toolbar, imageTools], UseXamlTransform: IsVideo,
            MediaBounds: IsVideo ? () => videoStage?.VideoBounds ?? MediaTransitionGeometry.Fit(video.TransformToVisual(card)
                .TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight)), VideoAspectRatio) : null,
            MotionContent: videoStage));
    }

    private double VideoAspectRatio => player?.PlaybackSession is { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } playback
        ? (double)playback.NaturalVideoWidth / playback.NaturalVideoHeight : 16d / 9;

    private void SetExpandedLayout(bool expanded)
    {
        card.ClearValue(WidthProperty); card.ClearValue(HeightProperty);
        card.Padding = new(expanded ? 0 : 8);
        card.BorderThickness = new(expanded ? 0 : 1);
        card.CornerRadius = expanded ? new(0) : inlineRadius;
        card.Background = expanded && videoStage is not null ? null : normalBrush;
        if (videoStage is not null) videoStage.IsHitTestVisible = false;
        Grid.SetRowSpan(stage, expanded ? 2 : 1);
        foreach (var chrome in new FrameworkElement[] { toolbar, imageTools })
        {
            Grid.SetRow(chrome, expanded ? 0 : 1);
            Grid.SetRowSpan(chrome, expanded ? 2 : 1);
            chrome.VerticalAlignment = expanded ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            chrome.Margin = expanded ? new(24) : new(0);
        }
        Label(expand, expanded ? "Collapse player" : "Expand player", "MediaPlayer.Expand");
        Label(imageExpand, expanded ? "Collapse image" : "Expand image", "MediaPlayer.Image.Expand");
        expand.Content = new SymbolIcon(expanded ? Symbol.BackToWindow : Symbol.FullScreen);
        imageExpand.Content = new SymbolIcon(expanded ? Symbol.BackToWindow : Symbol.FullScreen);
        video.IsTabStop = expanded;
        imageViewport.UseSystemFocusVisuals = !expanded;
        if (IsVideo) videoChrome.Reveal();
        if (!expanded) videoChrome.Stop();
        imageTools.HorizontalAlignment = HorizontalAlignment.Center;
        LayoutImageTools();
        toolbar.MaxWidth = expanded ? 960 : double.PositiveInfinity;
        InteractionChanged?.Invoke();
    }

    private CornerRadius inlineRadius;
}
