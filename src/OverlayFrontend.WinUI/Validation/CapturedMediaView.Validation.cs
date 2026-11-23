using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.Media.Playback;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private CaptureAttachment? attachment => definition?.Source.Attachment;
    internal CaptureAttachment? Attachment => attachment;
    internal void Apply(Func<CancellationToken, Task<HostCaptureAttachment>> resolve, CaptureAttachment next, Func<bool> current) =>
        Apply(async token => { var result = await resolve(token); return new(MediaPlayerDefinition.FromCapture(result.Attachment), new Uri(result.Path).AbsoluteUri,
            result.Attachment.ContentType == "image/png", result.Attachment.Width, result.Attachment.Height, result.Attachment.DurationSeconds, result.Attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true); },
            MediaPlayerDefinition.FromCapture(next), current);

    internal async Task ValidateCapturePlaybackAsync(string png, string mp4, List<string> checks)
    {
        var still = AttachmentFor(png, "image/png", 0);
        var clip = AttachmentFor(mp4, "video/mp4", 5);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        await Until(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0);
        Check(image.Visibility == Visibility.Visible && player is null, "Screenshot preview decodes generated PNG without a player");
        SetActive(true, true);
        Check(CanControl && imageTools.Visibility == Visibility.Collapsed && imageViewport.Focus(FocusState.Keyboard),
            "Image tools stay hidden until the keyboard enters the viewer");
        await Until(() => imageTools.Visibility == Visibility.Visible);
        await Task.Delay(100);
        var imageCenter = image.TransformToVisual(imageCanvas).TransformPoint(new(image.ActualWidth / 2, image.ActualHeight / 2));
        Check(Math.Abs(imageCenter.X - imageCanvas.ActualWidth / 2) < 1 && Math.Abs(imageCenter.Y - imageCanvas.ActualHeight / 2) < 1,
            "Aspect-fit image is centered horizontally and vertically in its viewport canvas");
        var toolsCenter = imageTools.TransformToVisual(body).TransformPoint(new(imageTools.ActualWidth / 2, 0));
        Check(Math.Abs(toolsCenter.X - body.ActualWidth / 2) < 1 && imageTools.ColumnDefinitions.All(column => column.Width.IsAuto),
            "Image controls form a centered content-sized group");
        Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        await Until(() => imageViewport.ZoomFactor > 1.1);
        var imageOffset = imageViewport.HorizontalOffset;
        Check(!MoveFocus(FocusNavigationDirection.Right) && imageViewport.HorizontalOffset == imageOffset,
            "Zoomed inline image leaves boundary navigation to the widget without panning");
        PanImage(short.MaxValue, 0);
        await Task.Delay(40);
        Check(PanImage(short.MaxValue, 0), "Focused image consumes analog pan input");
        await Until(() => imageViewport.HorizontalOffset > imageOffset);
        Check(MoveFocus(FocusNavigationDirection.Down) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), imageFit),
            "Directional navigation reaches image controls while zoomed");
        Check(!PanImage(short.MaxValue, 0), "Image does not consume right-stick input while its toolbar is focused");
        Check(MoveFocus(FocusNavigationDirection.Up) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), imageViewport),
            "Toolbar navigation returns to the image canvas");
        Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
        await Until(() => imageViewport.ZoomFactor == 1);
        Check(PanImage(short.MaxValue, 0), "Fit image consumes pan input to prevent parent page scrolling");
        var fullscreen = MediaFullscreenLayer.Find(this)!;
        fullscreen.TransitionDuration = TimeSpan.FromMilliseconds(600);
        var imageExpanded = ToggleExpandedAsync();
        await Until(() => expandedHost is { IsLoaded: true, IsOpen: true } && imageTools.ActualHeight > 0);
        await Task.Delay(80);
        Check(fullscreen.IsTransitioning && !card.IsHitTestVisible, "Configured fullscreen duration keeps controls inert during expansion");
        await Until(() => !changingPlacement);
        Check(!fullscreen.IsTransitioning && card.IsHitTestVisible, "Expansion settles motion before enabling controls");
        fullscreen.TransitionDuration = MediaFullscreenLayer.DefaultTransitionDuration;
        imageViewport.Focus(FocusState.Keyboard);
        Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        await Until(() => imageViewport.ZoomFactor > 1.1);
        imageOffset = imageViewport.HorizontalOffset;
        expandedHost!.MoveAnalog(short.MaxValue, 0);
        await Task.Delay(40);
        Check(expandedHost.MoveAnalog(short.MaxValue, 0), "Fullscreen host forwards analog pan to the image");
        await Until(() => imageViewport.HorizontalOffset > imageOffset);
        expandedHost.MoveFocus(FocusNavigationDirection.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), imageFit),
            "Zoomed fullscreen image navigation reaches the toolbar");
        FitImage(); await Until(() => imageViewport.ZoomFactor == 1);
        Check(imageTools.TransformToVisual(card).TransformPoint(new(0, imageTools.ActualHeight)).Y <= card.ActualHeight + 1,
            "Expanded image controls fit inside the visible media card");
        Check(card.ActualWidth <= expandedHost!.ActualWidth + 1 && card.ActualHeight <= expandedHost.ActualHeight + 1 &&
            imageTools.TransformToVisual(card).TransformPoint(new(imageTools.ActualWidth, 0)).X <= card.ActualWidth - 8,
            "Expanded image and its rightmost control fit the shared viewport with focus clearance");
        Handle(ControllerButton.B, ControllerEventPhase.Pressed); Handle(ControllerButton.B, ControllerEventPhase.Released);
        await imageExpanded;
        Check(imageViewport.ZoomFactor == 1 && !IsExpanded, "Fit and Back restore the inline image viewer");
        ZoomImage(1);
        await Until(() => imageViewport.ZoomFactor > 1.1);
        var originalScale = RememberImageViewport()!.Scale;
        var zoomedExpansion = ToggleExpandedAsync();
        await Until(() => !changingPlacement && expandedHost is { IsTransitioning: false });
        await Until(() => Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
        Check(Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01,
            "Animated expansion preserves inspected image scale after native scroll restoration");
        expandedHost!.Close(); await zoomedExpansion;
        await Until(() => Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
        Check(Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01,
            "Animated collapse restores the original image scale");
        var interruptedImage = ToggleExpandedAsync();
        fullscreen.Close(); await interruptedImage;
        await Until(() => Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
        Check(!IsExpanded, "Canceling image expansion before its first frame retains the original zoom");
        await AssertImageReversal(60, "original");
        Check(!IsExpanded, "Reversing image expansion mid-animation retains the original zoom");
        // The earlier ten-second scale-restoration failure was intermittent.
        // Probe a bounded set of native placement phases without resetting zoom
        // between attempts or weakening the existing absolute-scale assertion.
        for (var cycle = 0; cycle < 2; ++cycle)
            foreach (var delay in new[] { 0, 20, 60, 120 })
            {
                await AssertImageReversal(delay, $"sweep-{cycle}");
                Check(!IsExpanded, $"Image reversal retains original scale at {delay}ms, cycle {cycle + 1}");
            }

        var unloadedCheckpoint = RememberImageViewport()!;
        FitImage();
        await Until(() => imageViewport.ZoomFactor == 1);
        stage.Children.Remove(imageViewport);
        await Until(() => !imageViewport.IsLoaded);
        RestoreImageViewport(unloadedCheckpoint);
        Check(pendingImageViewportRestore is not null && imageViewport.ZoomFactor == 1,
            "Unloaded image restoration retains its checkpoint without issuing a lost native zoom");
        stage.Children.Insert(0, imageViewport);
        await Until(() => imageViewport.IsLoaded && Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
        Check(pendingImageViewportRestore is null,
            "Loaded image applies the exact retained zoom checkpoint once");

        await VerifyDeferredRestoreCancellation(() => ZoomImage(1), "Fresh zoom");
        await VerifyDeferredRestoreCancellation(FitImage, "Fit");
        await VerifyDeferredRestoreCancellation(() => SetActive(false, false), "Presentation retirement");

        async Task VerifyDeferredRestoreCancellation(Action supersede, string label)
        {
            var checkpoint = RememberImageViewport()!;
            stage.Children.Remove(imageViewport);
            await Until(() => !imageViewport.IsLoaded);
            RestoreImageViewport(checkpoint);
            Check(pendingImageViewportRestore is not null, label + " fixture retains an unloaded viewport checkpoint");
            supersede();
            Check(pendingImageViewportRestore is null, label + " revokes deferred image restoration");
            SetActive(true, true);
            stage.Children.Insert(0, imageViewport);
            await Until(() => imageViewport.IsLoaded);
            FitImage();
            await Until(() => imageViewport.ZoomFactor == 1);
            await Task.Delay(50);
            Check(imageViewport.ZoomFactor == 1 && pendingImageViewportRestore is null,
                label + " is not overwritten by a late Loaded/layout callback");
            RestoreImageViewport(checkpoint);
            await Until(() => Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
        }

        async Task AssertImageReversal(int closeDelay, string phase)
        {
            imageReversalTrace = [];
            imageReversalCase = $"{phase}:{closeDelay}ms";
            try
            {
                TraceImageViewportState("before-reversal-expansion", originalScale, null, null);
                if (Math.Abs(RememberImageViewport()!.Scale - originalScale) >= .01 || IsExpanded || changingPlacement)
                    throw new InvalidOperationException("Image reversal began from an unsettled baseline.");
                var reversing = ToggleExpandedAsync();
                if (closeDelay != 0) await Task.Delay(closeDelay);
                TraceImageViewportState("before-reversal-close", originalScale, null, null);
                fullscreen.Close(); await reversing;
                TraceImageViewportState("reversal-close-completed", originalScale, null, null);
                await Until(() => Math.Abs(RememberImageViewport()!.Scale - originalScale) < .01);
            }
            catch (Exception error) when (error is TimeoutException or InvalidOperationException)
            {
                TraceImageViewportState("reversal-failed", originalScale, null, null);
                throw new InvalidOperationException("Interrupted image reversal failed. Bounded viewport evidence: " +
                    string.Join(";", imageReversalTrace), error);
            }
            finally { imageReversalTrace = null; imageReversalCase = null; }
        }
        SetActive(true, true);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(clip, mp4)), clip, () => true);
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(!IsPlaying && player!.IsMuted, "Native video opens muted without autoplay");
        Check(play.IsTabStop && playbackOptions.IsTabStop && play.UseSystemFocusVisuals && playbackOptions.UseSystemFocusVisuals,
            "Native transport buttons participate in focus and use theme focus visuals");
        Check(play.Focus(FocusState.Keyboard), "Play accepts keyboard/controller focus");
        Check(MoveFocus(FocusNavigationDirection.Right) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), mute) && PrimaryActionLabel == "Unmute",
            "Right navigation focuses audio and updates the A action");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        Check(!player!.IsMuted && !IsPlaying, "Controller A changes mute without starting paused playback");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        Check(MoveFocus(FocusNavigationDirection.Left) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), play),
            "Left navigation returns to Play without leaving the media element");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying);
        Toggle(); await Until(() => !IsPlaying);
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying && player!.PlaybackSession.Position.TotalSeconds > .1);
        Handle(ControllerButton.A, ControllerEventPhase.Released);
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => !IsPlaying);
        Check(true, "Controller A plays and pauses native video");
        var before = player!.PlaybackSession.Position.TotalSeconds;
        Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        await Until(() => player.PlaybackSession.Position.TotalSeconds > before + .3);
        Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying && player.PlaybackSession.Position.TotalSeconds < 1);
        Check(true, "Native capture supports seeking and replay");
        Check(position.ActualWidth > 120 && toolbar.ActualHeight >= play.ActualHeight,
            "Timeline fills available toolbar width and transport controls fit within the media card");
        SetActive(false, false);
        await Until(() => !IsPlaying);
        Check(!Handle(ControllerButton.A, ControllerEventPhase.Pressed), "Inactive preview pauses and refuses controller actions");
        Check(!Handle(ControllerButton.B, ControllerEventPhase.Pressed), "Preview leaves B to normal host navigation");
        SetActive(true, true);
        var general = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(mp4), new(Volume:.6, PlaybackRate:1.25, Loop:true));
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(Attachment is null && !player!.IsMuted && player.IsLoopingEnabled && Math.Abs(player.Volume-.6)<.01,
            "General local video uses the same player without a capture attachment and honors options");
        await Until(() => Math.Abs(player!.PlaybackSession.PlaybackRate-1.25)<.01);
        Check(true, "Initial playback rate is applied after media opens");
        Check(playbackOptions.Focus(FocusState.Keyboard), "Playback options accepts native controller focus");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => optionsMenu?.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault() is { IsLoaded: true });
        optionsNavigation!.Move(FocusNavigationDirection.Down);
        await optionsNavigation.ActivateAsync();
        var rate = optionsMenu!.Items.OfType<MenuFlyoutSubItem>().Single().Items.OfType<MenuFlyoutItem>()
            .Single(item => AutomationProperties.GetAutomationId(item) == "MediaPlayer.Options.speed.1.5");
        await Until(() => rate.IsLoaded);
        Check(rate.Focus(FocusState.Keyboard), "Playback speed submenu accepts native focus");
        Handle(ControllerButton.B, ControllerEventPhase.Pressed);
        Handle(ControllerButton.B, ControllerEventPhase.Released);
        await Until(() => FocusManager.GetFocusedElement(XamlRoot) is MenuFlyoutSubItem);
        Check(HasOptionsMenu && Math.Abs(player.PlaybackSession.PlaybackRate - 1.25) < .01,
            "First Back closes only the playback speed submenu without changing playback");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => rate.IsLoaded);
        Check(rate.Focus(FocusState.Keyboard), "Playback speed submenu can reopen after Back");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => !HasOptionsMenu && Math.Abs(player.PlaybackSession.PlaybackRate - 1.5) < .01);
        Check(true, "Shared native menu applies the selected playback speed");
        playbackOptions.Focus(FocusState.Keyboard); Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => optionsMenu?.Items.OfType<MenuFlyoutItem>().Any(item => item.IsLoaded && AutomationProperties.GetAutomationId(item) == "MediaPlayer.Options.loop") == true);
        optionsMenu!.Items.OfType<MenuFlyoutItem>().Single(item => AutomationProperties.GetAutomationId(item) == "MediaPlayer.Options.loop").Focus(FocusState.Keyboard);
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => !HasOptionsMenu);
        Check(!player.IsLoopingEnabled,"Native options menu updates looping");
        ToggleMute(); Check(player.IsMuted,"Mute control updates native audio");
        volume.Value = 35; Check(!player.IsMuted && Math.Abs(player.Volume-.35)<.01,"Volume slider adjusts native audio and unmutes");
        Check(time.Text == "Volume 35%" && double.IsFinite(time.Width),
            "Volume feedback uses the existing fixed-size status cell");
        Check(Equals(position.ThumbToolTipValueConverter.Convert(72d, typeof(string), null!, "en"), "1:12"),
            "Native scrub tooltip formats the target as minutes and seconds");
        position.Focus(FocusState.Keyboard);
        MoveFocus(FocusNavigationDirection.Right);
        Check(player.PlaybackSession.Position.TotalSeconds>=.4,"Focusable timeline supports controller scrubbing");
        MoveFocus(FocusNavigationDirection.Down);
        Check(controls.Contains(FocusManager.GetFocusedElement(XamlRoot) as Microsoft.UI.Xaml.Controls.Control),"Timeline returns to transport controls");
        var waiting = new TaskCompletionSource<HostMediaPlayerSource>();
        var pendingOptions = general with { Revision = 2 };
        Apply(_ => waiting.Task, pendingOptions, () => true);
        general = pendingOptions with { Options = new(Muted:true) };
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        waiting.SetResult(new(pendingOptions,new Uri(mp4).AbsoluteUri));
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(player!.IsMuted,"Changing playback options during source resolution cannot strand or revive the old load");
        Toggle(); await Until(() => IsPlaying);
        var retained = player;
        Check(playbackOptions.Focus(FocusState.Keyboard), "Playback options control accepts focus before expansion");
        var inlineVideo = MediaTransitionGeometry.Fit(fullscreen.BoundsOf(video)!.Value, VideoAspectRatio);
        var expanded = ToggleExpandedAsync();
        await Until(() => expandedHost is { IsLoaded: true, IsOpen: true });
        await CheckStableVideoTransition("Playing video entrance");
        await Until(() => !changingPlacement && expandedHost is { IsTransitioning: false });
        Check(fullscreen.MediaEndpointMatches(inlineVideo), "Native video animation endpoint matches its fitted inline pixels rather than the card");
        Check(card.Parent is MediaFullscreenLayer, "Expanded viewer uses the shared shell layer without dialog framing");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playbackOptions), "Expansion preserves the focused transport control; actual=" +
            (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focus ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focus) : "none"));
        Check(card.ActualWidth <= expandedHost!.ActualWidth + 1 && card.ActualHeight <= expandedHost.ActualHeight + 1 &&
            toolbar.TransformToVisual(card).TransformPoint(new(toolbar.ActualWidth, 0)).X <= card.ActualWidth - 8,
            "Expanded video and rightmost controls fit the shared viewport with focus clearance");
        Check(toolbar.TransformToVisual(card).TransformPoint(new(0, toolbar.ActualHeight)).Y <= card.ActualHeight + 1,
            "Expanded video reserves visible height for timeline and transport controls");
        Check(IsPlaying && ReferenceEquals(player,retained) && card.Parent is Microsoft.UI.Xaml.Controls.Grid,"Expanded view retains one player and playback state");
        Handle(ControllerButton.B,ControllerEventPhase.Pressed);
        Handle(ControllerButton.B,ControllerEventPhase.Released);
        await CheckStableVideoTransition("Playing video exit");
        await expanded;
        Check(ReferenceEquals(card.ReadLocalValue(UIElement.RenderTransformProperty), DependencyProperty.UnsetValue),
            "Native video transition restores the original XAML transform after collapse");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playbackOptions), "Collapse restores the same focused transport control");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var transition = ToggleExpandedAsync();
            await Until(() => IsExpanded && !changingPlacement && expandedHost is { IsTransitioning: false } && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playbackOptions));
            var layer = expandedHost!;
            layer.Route(ControllerButton.RightStick, ControllerEventPhase.Pressed);
            Check(layer.Route(ControllerButton.RightStick, ControllerEventPhase.Released), "Collapse gesture release stays owned by fullscreen");
            await transition;
            Check(!IsExpanded && ReferenceEquals(player, retained) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playbackOptions),
                "Repeated expansion retains the player and focus without visiting another control");
        }
        Check(IsPlaying && !IsExpanded && ReferenceEquals(Content,card) && ReferenceEquals(player,retained),"B restores inline player without recreating playback");
        Toggle(); await Until(() => !IsPlaying);
        var pausedExpansion = ToggleExpandedAsync();
        await CheckStableVideoTransition("Paused video entrance");
        await Until(() => !changingPlacement && expandedHost is { IsTransitioning: false });
        expandedHost!.Close();
        await CheckStableVideoTransition("Paused video exit");
        await pausedExpansion;
        Toggle(); await Until(() => IsPlaying);
        var rapid = ToggleExpandedAsync();
        await Until(() => IsExpanded);
        expandedHost!.Close();
        var overlapping = ToggleExpandedAsync();
        Check((!IsExpanded || expandedHost is { IsClosing: true }) && overlapping.IsCompleted,
            "Repeated collapse is ignored while the previous animated placement is closing");
        await rapid;
        Check(!IsExpanded && !fullscreen.IsTransitioning, "Close during entrance finishes inline without a stale animation callback");
        var forced = ToggleExpandedAsync();
        fullscreen.Close(restoreFocus: false);
        Check(!IsExpanded && !fullscreen.IsOpen && !fullscreen.IsTransitioning, "Host teardown closes immediately even during an animation");
        await forced;
        fullscreen.ApplyMotion(WidgetRail.PlatformSettings.AppearanceSettings.Default with { Motion = WidgetRail.PlatformSettings.MotionPreference.Reduced }, true);
        var reduced = ToggleExpandedAsync();
        await Until(() => !changingPlacement);
        Check(!fullscreen.IsTransitioning && card.IsHitTestVisible, "Reduced motion settles expansion without transforms");
        fullscreen.Close(); await reduced;
        fullscreen.ApplyMotion(WidgetRail.PlatformSettings.AppearanceSettings.Default, true);
        player!.Pause(); await Until(() => !IsPlaying);
        Check(Handle(ControllerButton.X, ControllerEventPhase.Pressed), "Inline player consumes X for playback");
        await Until(() => IsPlaying);
        Check(Handle(ControllerButton.X, ControllerEventPhase.Repeated) && Handle(ControllerButton.X, ControllerEventPhase.Released),
            "Inline player owns the complete X gesture");
        await Task.Delay(100);
        Check(IsPlaying, "Holding or releasing X does not toggle playback again");
        Handle(ControllerButton.X, ControllerEventPhase.Pressed);
        await Until(() => !IsPlaying);
        Handle(ControllerButton.X, ControllerEventPhase.Released);
        Check(!IsPlaying, "A second inline X press pauses playback");
        var controlsCheck = ToggleExpandedAsync();
        await Until(() => IsExpanded && !changingPlacement && expandedHost is { IsTransitioning: false });
        Check(play.Focus(FocusState.Keyboard), "Visible transport owns keyboard focus");
        videoChrome.Hide();
        Check(videoChrome.Visible && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), play), "Focused transport remains visible without stealing keyboard/controller focus");
        Check(video.Focus(FocusState.Keyboard), "Media canvas can take focus before hiding inactive controls");
        player!.Pause();
        await Until(() => !IsPlaying);
        Check(play.Focus(FocusState.Pointer), "A mouse-activated control can retain pointer focus before becoming idle");
        videoChrome.Reveal();
        await Until(() => !videoChrome.Visible);
        Check(!IsPlaying && !videoControls.IsHitTestVisible && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),video),
            "Paused video hides idle pointer-focused controls and parks focus on its visible canvas without playing");
        videoChrome.Reveal();
        videoChrome.Hide();
        Check(!videoChrome.Visible && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), video), "Hidden controls leave focus on the visible media canvas");
        var wasPlaying = IsPlaying;
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        Check(videoChrome.Visible && IsPlaying == wasPlaying, "First A reveals controls without activating a hidden command");
        await Task.Delay(180);
        Check(videoChrome.Visible && videoControls.Visibility == Visibility.Visible,
            "Interrupted controls fade cannot collapse a newly revealed toolbar");
        Check(video.Focus(FocusState.Keyboard), "Leaving the toolbar permits its inactive fade");
        videoChrome.Hide();
        await Until(() => videoControls.Visibility == Visibility.Collapsed);
        Check(!videoControls.IsHitTestVisible, "Completed controls fade retires native hit targets");
        fullscreen.ApplyMotion(WidgetRail.PlatformSettings.AppearanceSettings.Default with { Motion = WidgetRail.PlatformSettings.MotionPreference.Reduced }, true);
        videoChrome.Reveal(); videoChrome.Hide();
        Check(videoControls.Visibility == Visibility.Collapsed, "Reduced motion settles media controls visibility immediately");
        fullscreen.ApplyMotion(WidgetRail.PlatformSettings.AppearanceSettings.Default, true);
        videoChrome.Reveal();
        expandedHost!.Width = 380; expandedHost.Height = 240; expandedHost.UpdateLayout();
        await Task.Delay(100);
        Check(card.ActualWidth <= 381 && toolbar.TransformToVisual(card).TransformPoint(new(toolbar.ActualWidth, toolbar.ActualHeight)).X <= card.ActualWidth + 1,
            "Narrow fullscreen layout keeps the transport inside the right edge");
        var oldFont = time.FontSize;
        ApplyTheme(normalBrush!, play.Foreground, time.Foreground, selectedBrush!, card.BorderBrush, position.Foreground,
            24, time.FontWeight, time.FontFamily, inlineRadius.TopLeft);
        Check(toolbar.Background is null && videoControls.Background is LinearGradientBrush scrim &&
            scrim.GradientStops.All(stop=>stop.Color.A<255) && imageTools.Background is SolidColorBrush imageBacking && imageBacking.Color.A<255,
            "Normal media chrome uses translucent backings with fully opaque controls");
        expandedHost.UpdateLayout();
        Check(toolbar.TransformToVisual(card).TransformPoint(new(toolbar.ActualWidth, toolbar.ActualHeight)).X <= card.ActualWidth + 1,
            "Large text retains safe transport bounds in a narrow fullscreen viewport");
        ApplyTheme(normalBrush!, play.Foreground, time.Foreground, selectedBrush!, card.BorderBrush, position.Foreground,
            oldFont, time.FontWeight, time.FontFamily, inlineRadius.TopLeft);
        ApplyTheme(normalBrush!, play.Foreground, time.Foreground, selectedBrush!, card.BorderBrush, position.Foreground,
            oldFont, time.FontWeight, time.FontFamily, inlineRadius.TopLeft, highContrast: true);
        Check(normalBrush is SolidColorBrush requested && toolbar.Background is SolidColorBrush painted &&
            painted.Color == (requested.Color with { A=255 }) && videoControls.Background is SolidColorBrush &&
            videoControls.Padding.Top == 0 && controls.OfType<Button>().All(button => button.BorderThickness.Top == 1),
            "High contrast uses solid media controls with visible button borders");
        ApplyTheme(normalBrush!, play.Foreground, time.Foreground, selectedBrush!, card.BorderBrush, position.Foreground,
            oldFont, time.FontWeight, time.FontFamily, inlineRadius.TopLeft);
        expandedHost.ClearValue(WidthProperty); expandedHost.ClearValue(HeightProperty); expandedHost.Close();
        await controlsCheck;
        await Task.Delay(150);
        Check(toolbar.TransformToVisual(this).TransformPoint(new(0, toolbar.ActualHeight)).Y <= ActualHeight + 1,
            "Returning from expansion keeps inline transport inside its original height");
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        Check(ReferenceEquals(player,retained),"Compatible snapshots do not reopen general media");
        var rememberedPosition = player!.PlaybackSession.Position.TotalSeconds;
        SetActive(false,false); Check(ResidentPlayers==0,"Hiding a player releases its native decoder slot");
        SetActive(true,true);
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)),general,()=>true);
        await Until(()=>player?.PlaybackSession.NaturalDuration.TotalSeconds>4.9 && playbackMemory is null);
        Check(!IsPlaying && Math.Abs(player!.PlaybackSession.Position.TotalSeconds-rememberedPosition)<.3,"Same-source reopen restores position without autoplay");
        var extraPlayers = Enumerable.Range(0,4).Select(_ => new MediaPlayerView()).ToArray();
        try
        {
            foreach (var extra in extraPlayers) { extra.SetActive(true,false); extra.Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)),general,()=>true); }
            Check(ResidentPlayers==4 && extraPlayers[3].player is null && extraPlayers[3].waitingForSlot,"Native decoder residency is bounded across media elements");
            extraPlayers[0].Dispose(); extraPlayers[3].Refresh();
            Check(extraPlayers[3].player is not null && ResidentPlayers==4,"Waiting player recovers when another decoder closes");
        }
        finally { foreach (var extra in extraPlayers) extra.Dispose(); }
        Check(ResidentPlayers==1,"Disposing extra players releases every decoder slot");
        var delayed = new TaskCompletionSource<HostCaptureAttachment>();
        var retired = clip with { Id = new string('c', 32) };
        Apply(_ => delayed.Task, retired, () => true);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        delayed.SetResult(new(retired, mp4));
        await Until(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0);
        await Task.Delay(100);
        Check(player is null && attachment == still, "Late load from retired media cannot replace the current preview");
        var denied = false;
        SetActive(true, true);
        Apply(_ => denied ? Task.FromException<HostCaptureAttachment>(new InvalidOperationException("revoked")) :
            Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        denied = true; nextAuthorityCheck = 0;
        await Until(() => image.Source is null && status.Visibility == Visibility.Visible);
        Check(true, "Revoked capture authority clears already-decoded preview pixels");
        Dispose(); Check(player is null && image.Source is null && ResidentPlayers == 0, "Disposal releases native media and image references");

        static CaptureAttachment AttachmentFor(string path, string type, double duration) =>
            new(Guid.NewGuid().ToString("N"), type, 640, 360, new FileInfo(path).Length, duration, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        async Task CheckStableVideoTransition(string label)
        {
            var wasPlaying = IsPlaying;
            var retainedPlayer = player;
            for (var frame = 0; frame < 4; frame++)
            {
                await Task.Delay(25);
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(card);
                visual.Properties.TryGetVector3("Translation", out var translation);
                if (visual.Scale != System.Numerics.Vector3.One || translation != System.Numerics.Vector3.Zero || visual.Opacity != 1)
                    throw new InvalidOperationException(label + " used a direct compositor transform for native video");
            }
            Check(ReferenceEquals(player, retainedPlayer) && IsPlaying == wasPlaying && !player!.IsVideoFrameServerEnabled,
                label + " uses the XAML transform path and retains ordinary playback");
        }
        static async Task Until(Func<bool> condition)
        {
            var end = Environment.TickCount64 + 10000;
            while (!condition()) { if (Environment.TickCount64 >= end) throw new TimeoutException("Media preview check timed out."); await Task.Delay(25); }
        }
    }

    private List<string>? imageReversalTrace;
    private string? imageReversalCase;
    partial void TraceImageViewportState(string phase, double? expectedScale, float? targetZoom, bool? changeAccepted)
    {
        if (imageReversalTrace is not { } trace) return;
        if (trace.Count == 32) trace.RemoveAt(0);
        trace.Add(System.Text.Json.JsonSerializer.Serialize(new
        {
            time = Environment.TickCount64, scenario = imageReversalCase, phase, expectedScale, targetZoom, changeAccepted,
            IsLoaded, viewportLoaded = imageViewport.IsLoaded, changingPlacement, IsExpanded,
            pendingRestore = pendingImageViewportRestore is not null,
            canvasWidth = imageCanvas.Width, canvasHeight = imageCanvas.Height,
            viewportWidth = imageViewport.ViewportWidth, viewportHeight = imageViewport.ViewportHeight,
            actualWidth = imageViewport.ActualWidth, actualHeight = imageViewport.ActualHeight,
            zoom = imageViewport.ZoomFactor, fitScale = ImageFitScale, scale = RememberImageViewport()?.Scale,
            horizontalOffset = imageViewport.HorizontalOffset, verticalOffset = imageViewport.VerticalOffset,
        }));
    }
}
