using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MediaPlayerElement = Microsoft.UI.Xaml.Controls.MediaPlayerElement;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MediaGalleryAsync(string directory, string clip)
    {
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "generated-image.png");
        await File.WriteAllBytesAsync(imagePath, await ArtworkPngAsync(320, 180));
        var root = new Grid { Width = 800, Height = 600 };
        var themeOwner = new NativePopupTheme(); themeOwner.Attach(root);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
            new((WrssDocument[])[], []), catalog.Load(ThemeIdentity.BuiltInMonoInk, "1.0.0").Package).Theme!;
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        foreach (var role in new[] { "body", "canvas", "panel", "preview", "hint" }) styles[role] = Compute(theme, role, role, []);
        var accessibleGallery = Environment.GetCommandLineArgs().Contains("--media-gallery-enlarged-high-contrast");
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        if (accessibleGallery) appearance = appearance with { Contrast = ContrastPreference.High, TextScale = 1.5 };
        themeOwner.Update(styles, appearance);
        var media = new MediaPlayerView { Width = 640, Height = 420 };
        root.Children.Add(media); PresentationLayers.Attach(root);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        fullscreen.ApplyMotion(appearance with { Motion = accessibleGallery ? MotionPreference.Reduced : MotionPreference.Full }, true);
        PresentationPortal? overlayPortal = null;
        WidgetModalLayer? overlayLayer = null;
        var overlayMode = Environment.GetCommandLineArgs().Contains("--media-gallery-overlay");
        if (overlayMode)
        {
            root.Children.Remove(media);
            media.Width = media.Height = double.NaN;
            media.ApplyFrameStyle(new SolidColorBrush(Microsoft.UI.Colors.Transparent), null, new(0), new(0), new(0));
            var anchor = new Grid { Width = 320, Height = 240, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            root.Children.Add(anchor);
            var panel = new WidgetModalPanel { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Padding = new(0), Children = { media } };
            overlayLayer = new WidgetModalLayer { OverlayPresentation = new(720) };
            overlayLayer.Children.Add(panel);
            overlayLayer.Configure(anchor, panel, 720, 405, double.PositiveInfinity, double.PositiveInfinity);
            overlayPortal = new(PresentationLayers.Find(root)!, anchor, overlayLayer);
        }
        host.Children.Clear(); host.Children.Add(status); host.Children.Add(root);
        App.Window.AppWindow.ResizeClient(new(1100, 900));
        var resident = MediaPlayerView.ResidentPlayers;
        var capture = new CaptureAttachment(new string('a', 32), "image/png", 320, 180, new FileInfo(imagePath).Length, 0, long.MaxValue);
        var first = new MediaPlayerGalleryItem("first", "First image", MediaPlayerSource.FromCapture(capture));
        var duplicate = first with { Id = "duplicate", Label = "Second occurrence" };
        var videoItem = new MediaPlayerGalleryItem("clip", "Synthetic clip", MediaPlayerSource.LocalFile(clip), new(AutoPlay: true, Loop: true, Muted: true));
        var declared = UI.MediaGallery((MediaPlayerGalleryItem[])[first, duplicate, videoItem], "gallery").Media;
        var resolutions = new List<string>();
        TaskCompletionSource<HostMediaPlayerSource>? delayed = null;
        var failDuplicate = false;
        Exception? failure = null;
        object? pointerDiagnostics = null;
        var pressed = 0; var clicks = 0;
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--gallery-image-switch-only"))
            {
                declared = UI.MediaGallery((MediaPlayerGalleryItem[])[first, duplicate with {
                    Source = MediaPlayerSource.FromSavedCapture(new string('b',32),1) }], "gallery").Media;
                media.SetMediaMotion(Motion.WidgetMotionOptions.From(appearance with {
                    Motion = MotionPreference.Full, SectionAnimation = WidgetSectionAnimation.Slide }, true));
                media.SetActive(true, true);
                Publish();
                await Wait(() => media.IsImage && media.ActualWidth > 0);
                var original = media.GalleryPictureForValidation;
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
                await Task.Delay(320);
                Check(!media.IsImage && original is not null && ReferenceEquals(original, media.GalleryPictureForValidation),
                    "A delayed next capture holds the old picture without granting it input");
                await CaptureGallery("pending.png");
                var selected = declared.SelectGalleryItem("duplicate");
                delayed.SetResult(new(selected,new Uri(imagePath).AbsoluteUri,IsImage:true,Width:320,Height:180));
                await Wait(() => media.IsImage && media.GalleryImageTransitionForValidation);
                await CaptureGallery("switch.png");
                media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
                await Wait(() => media.SelectedGalleryItemId == "first" && media.IsImage && !media.GalleryImageTransitionForValidation);
                Check(media.CanControl && media.GalleryPictureForValidation is not null,"Reverse navigation settles on the first picture");
                return;
            }
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--store-gallery-fixture") is { } fixture)
            {
                var items = JsonSerializer.Deserialize<MediaPlayerGalleryItem[]>(await File.ReadAllTextAsync(fixture))
                    ?? throw new InvalidDataException("Missing store media fixture.");
                var carousel = Environment.GetCommandLineArgs().Contains("--inline-carousel");
                if (carousel) { media.Width = 790; media.Height = 345; }
                declared = UI.MediaGallery(items, "gallery", inlineCarousel: carousel).Media;
                media.SetActive(true, true); Publish();
                var firstTileId = "MediaPlayer.Thumbnail." + items[0].Id;
                await Wait(() => Find<Button>(firstTileId, root) is { IsLoaded: true, ActualWidth: > 0 });
                var firstTile = Find<Button>(firstTileId, root)!;
                Check(media.Focus(FocusState.Keyboard), "The gallery accepts initial focus");
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), firstTile));
                Check(true, "Gallery entry focuses the first trailer thumbnail");
                foreach (var item in items)
                {
                    var thumbnail = Find<Button>("MediaPlayer.Thumbnail." + item.Id, root)!;
                    Check(ToolTipService.GetToolTip(thumbnail) is null && AutomationProperties.GetName(thumbnail) == item.Label,
                        $"The {item.Id} thumbnail keeps its accessible name without obstructing the media");
                }
                var palette = Shell.ShellChromePalette.Resolve(styles, appearance);
                Check(ToolTipService.GetToolTip(Find<Button>("MediaPlayer.Expand", root)!) is ToolTip expandHint &&
                    ColorOf(expandHint.Background) == palette.Surface && ColorOf(expandHint.Foreground) == palette.Text,
                    "The fullscreen button uses the active tooltip palette");
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                while (!media.IsPlaying) await Task.Delay(50, startup.Token);
                Check(media.Definition?.Source.IsAdaptiveDash == true, "Live trailer plays through the checked DASH source");
                if (carousel)
                {
                    var side = Find<Button>("MediaPlayer.Carousel.Next", media)!;
                    await Wait(() => ((Image)side.Content).Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 336 });
                    var carouselStage = Find<MediaGalleryCarousel>("MediaPlayer.Carousel", media)!;
                    Check(Math.Abs(carouselStage.Center.TransformToVisual(carouselStage).TransformPoint(new()).X) < 1 &&
                        carouselStage.Center.ActualWidth < carouselStage.ActualWidth - 8 && !side.IsTabStop,
                        "First trailer sits at the leading edge beside a display-sized still with no extra focus stop");
                }
                await CaptureGallery("trailer.png");
                var playButton = Find<Button>("MediaPlayer.Play", root)!;
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), firstTile) &&
                    media.MoveFocus(FocusNavigationDirection.Up) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playButton),
                    "Autoplay retains thumbnail focus and Up reaches playback");
                var seek = Find<Slider>("MediaPlayer.Position", root)!;
                await Wait(() => seek.IsEnabled);
                var playingSurface = Find<MediaPlayerElement>("MediaPlayer.Video.Viewport", media)!;
                var transportPanel = (FrameworkElement)((FrameworkElement)playButton.Parent).Parent;
                await Wait(() => transportPanel.Visibility == Visibility.Collapsed);
                Check(media.IsPlaying && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playingSurface),
                    "An idle focused playback button gives focus to the video as the transport hides");
                media.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                media.Handle(ControllerButton.A, ControllerEventPhase.Released);
                Check(media.IsPlaying && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), playButton),
                    "The first activation wakes the focused control without pausing playback");
                var beforeSeek = seek.Value;
                media.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                media.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Released);
                await Wait(() => seek.Value >= beforeSeek + 4);
                Check(true, "Right trigger seeks forward in the live trailer");
                Check(media.MoveFocus(FocusNavigationDirection.Down), "Down enters the thumbnail strip");
                var inlineVideo = Find<MediaPlayerElement>("MediaPlayer.Video.Viewport", media)!;
                var decoder = inlineVideo.MediaPlayer;
                var originalVideoSize = inlineVideo.ActualSize;
                var inlineBounds = firstTile.TransformToVisual(root).TransformBounds(new(0, 0, firstTile.ActualWidth, firstTile.ActualHeight));
                bool StripStayedInline() => ReferenceEquals(Find<Button>(firstTileId, media), firstTile) && firstTile.IsLoaded &&
                    firstTile.TransformToVisual(root).TransformBounds(new(0, 0, firstTile.ActualWidth, firstTile.ActualHeight)).Equals(inlineBounds);
                media.ActivateFocusedControl();
                root.UpdateLayout();
                Check(StripStayedInline(), "Opening leaves the original strip mounted at its inline position");
                await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
                Check(StripStayedInline(), "Fullscreen keeps the strip in its original bounds");
                Check(ReferenceEquals(Find<MediaPlayerElement>("MediaPlayer.Video.Viewport", fullscreen), inlineVideo) &&
                    decoder is not null && ReferenceEquals(inlineVideo.MediaPlayer, decoder), "Fullscreen keeps the same in-tree video and decoder");
                Check(inlineVideo.ActualSize == originalVideoSize, $"Native video retains its rendered size ({originalVideoSize} to {inlineVideo.ActualSize})");
                Check(media.IsPlaying, $"The decoder keeps playing in fullscreen (state {decoder?.PlaybackSession.PlaybackState})");
                fullscreen.Route(ControllerButton.B, ControllerEventPhase.Pressed);
                fullscreen.Route(ControllerButton.B, ControllerEventPhase.Released);
                Check(StripStayedInline(), "Closing keeps the strip at the same bounds");
                await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
                Check(StripStayedInline() && ReferenceEquals(Find<MediaPlayerElement>("MediaPlayer.Video.Viewport", media), inlineVideo) &&
                    ReferenceEquals(decoder, inlineVideo.MediaPlayer) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), firstTile),
                    "Video returns to its inline tree and exact thumbnail");
                media.ActivateFocusedControl();
                await Task.Delay(45);
                fullscreen.Route(ControllerButton.B, ControllerEventPhase.Pressed);
                fullscreen.Route(ControllerButton.B, ControllerEventPhase.Released);
                await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
                Check(StripStayedInline() && media.IsPlaying && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), firstTile),
                    "Back during entry returns the live trailer and restores strip focus");
                Check(media.MoveFocus(FocusNavigationDirection.Right), "Right previews the next item");
                await Wait(() => media.IsImage && media.CanControl);
                var tile = Find<Button>("MediaPlayer.Thumbnail." + items[1].Id, root)!;
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), tile), "Screenshot load preserves thumbnail focus");
                var fitControl = Find<Button>("MediaPlayer.Image.Fit", media)!;
                if (carousel) Check(((FrameworkElement)fitControl.Parent).Visibility == Visibility.Collapsed,
                    "Inline carousel screenshots show no image toolbar");
                await CaptureGallery("screenshot.png");
                media.ActivateFocusedControl();
                await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
                CheckFullscreenImageBounds();
                var fullImage = Find<ScrollViewer>("MediaPlayer.Image.Viewport", fullscreen)!;
                Check(!fullImage.IsTabStop && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), fitControl) &&
                    ((FrameworkElement)fitControl.Parent).Visibility == Visibility.Visible, "Fullscreen opens on Fit, with the image outside the focus order");
                media.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await Wait(() => fullImage.ZoomFactor > 1);
                Check(media.PanImage(25000, 0) && !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), fullImage),
                    "Fullscreen image zoom and controller pan work with focus on the toolbar");
                Check(media.MoveFocus(FocusNavigationDirection.Up) && media.MoveFocus(FocusNavigationDirection.Down) &&
                    ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), fitControl), "Gallery arrow returns to Fit without selecting the image");
                media.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                media.Handle(ControllerButton.B, ControllerEventPhase.Released);
                await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), tile), "Back restores the exact thumbnail");
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    media.SetActive(false, false);
                    await media.PlaybackRetirement;
                    media.SetActive(true, true);
                    Publish();
                    await Wait(() => media.CanControl);
                    media.Focus(FocusState.Keyboard);
                    await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),
                        Find<Button>("MediaPlayer.Thumbnail." + items[1].Id, root)));
                }
                Check(true, "Two gallery suspensions restore the selected thumbnail when the host re-enters the player");
                if (items.Length > 2)
                {
                    media.ActivateFocusedControl();
                    await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
                    media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
                    media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Released);
                    await Wait(() => media.SelectedGalleryItemId == items[2].Id && media.CanControl);
                    media.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                    media.Handle(ControllerButton.B, ControllerEventPhase.Released);
                    await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),
                        Find<Button>("MediaPlayer.Thumbnail." + items[2].Id, root)), "Fullscreen browsing returns to the newly selected thumbnail");
                }
                var failed = items[0] with { Id = "unavailable-trailer" };
                declared = declared with { Gallery = new[] { failed, items[1] }, Source = failed.Source };
                media.Apply(_ => throw new InvalidOperationException(), declared, () => true, "another-game",
                    resolveGalleryItem: (id, _, _) => id == failed.Id
                        ? Task.FromException<HostMediaPlayerSource>(new IOException("Unavailable trailer fixture"))
                        : Task.FromResult(Resolve(declared.SelectGalleryItem(id))));
                await Wait(() => media.SelectedGalleryItemId == items[1].Id && media.IsImage && media.CanControl);
                Check(true, "An unavailable autoplay trailer advances to the first screenshot");
                var onlyTrailer = declared with { Gallery = new[] { failed } };
                var attempts = 0;
                void OpenUnavailableTrailer() => media.Apply(_ => throw new InvalidOperationException(), onlyTrailer,
                    () => true, "trailer-only", resolveGalleryItem: (_, _, _) =>
                    {
                        attempts++;
                        return Task.FromException<HostMediaPlayerSource>(new IOException("Unavailable trailer fixture"));
                    });
                OpenUnavailableTrailer();
                await Wait(() => media.Visibility == Visibility.Collapsed);
                OpenUnavailableTrailer();
                Check(attempts == 1 && !media.CanControl, "A sole unavailable autoplay trailer retires without an error panel or a render-time retry");
                var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
                var late = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
                media.Apply(_ => throw new InvalidOperationException(), declared, () => true, "late-game",
                    resolveGalleryItem: (_, _, token) => { started.TrySetResult(token); return late.Task; });
                var pending = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(media.Visibility == Visibility.Visible, "Opening another game restores its media stage");
                media.SetActive(false, false);
                Check(pending.IsCancellationRequested, "Hiding cancels an unresolved trailer before decoder creation");
                late.SetResult(Resolve(declared.SelectGalleryItem(failed.Id)));
                await media.PlaybackRetirement;
                Check(MediaPlayerView.ResidentPlayers == resident, "Suspension releases the store player");
                await NativeDashMediaSource.VerifySegmentCancellationAsync();
                Check(true, "Retiring a native DASH source cancels and drains an in-flight segment after the first byte");
                return;
            }
            media.SetActive(true, true);
            await VerifyFailedTrailerNavigation();
            Publish();
            await Wait(() => media.IsImage && media.CanControl);
            if (overlayMode)
            {
                await Wait(() => media.ActualWidth > 600 && Math.Abs(media.ActualWidth / media.ActualHeight - 16.0/9) < .01);
                Check(true, "Decoded gallery uses transparent overlay-sized 16:9 geometry beyond its compact anchor");
                await CaptureGallery("overlay-decoded.png");
            }
            Check(media.SelectedGalleryItemId == "first" && resolutions.SequenceEqual(new[] { "first" }), "Gallery resolves only its selected occurrence");
            var next = Find<Button>("MediaPlayer.Gallery.Next", root)!;
            await Wait(() => next.IsLoaded && next.ActualWidth > 0 && next.ActualHeight > 0 && next.IsEnabled);
            next.UpdateLayout();
            var imageViewport=Find<ScrollViewer>("MediaPlayer.Image.Viewport",root)!;
            var previous=Find<Button>("MediaPlayer.Gallery.Previous",root)!;
            Check(imageViewport.Focus(FocusState.Keyboard),"Gallery image accepts controller entry");
            Check(media.MoveFocus(FocusNavigationDirection.Up)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),next),
                "Up from the first image reaches the enabled Next action");
            if (accessibleGallery)
            {
                var counter = Find<TextBlock>("MediaPlayer.Gallery.Position", root)!;
                var toolbar = (Grid)next.Parent;
                var expectedSize = (styles["body"].Base.GetValueOrDefault("font-size")?.Number ?? 16) * appearance.TextScale;
                Check(Math.Abs(counter.FontSize - expectedSize) < .01 && !counter.IsTextTrimmed &&
                    next.ActualWidth >= 44 && next.ActualHeight >= 44 && next.IsEnabled,
                    "Enlarged attachment count fits between full-size controller targets");
                Check(toolbar.Background is SolidColorBrush backing && backing.Color.A == 255 &&
                    counter.Foreground is SolidColorBrush ink && ink.Color != backing.Color,
                    "High-contrast attachment navigation gives the count an opaque contrasting backing");
                await CaptureGallery("attachment-focus.png");
            }
            Check(media.MoveFocus(FocusNavigationDirection.Right)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),next),
                "Right at the gallery toolbar edge stays there instead of falling into image controls");
            Check(media.MoveFocus(FocusNavigationDirection.Down)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),imageViewport),
                "Down from gallery navigation returns to the image");
            var headerBottom=next.TransformToVisual(media).TransformBounds(new(0,0,next.ActualWidth,next.ActualHeight)).Bottom;
            Check(headerBottom<=imageViewport.TransformToVisual(media).TransformPoint(new(0,0)).Y,
                "Gallery navigation occupies its own row above the image instead of covering it");
            next.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => pressed++), true);
            next.Click += (_, _) => clicks++;
            var point = next.TransformToVisual(null).TransformPoint(new(next.ActualWidth / 2, next.ActualHeight / 2));
            pointerDiagnostics = new { next.ActualWidth, next.ActualHeight, next.IsEnabled, next.IsHitTestVisible,
                x = point.X, y = point.Y, hits = VisualTreeHelper.FindElementsInHostCoordinates(point, XamlRoot.Content)
                    .Take(12).Select(element => element.GetType().Name + ":" + AutomationProperties.GetAutomationId(element)).ToArray() };
            await NativeContextProbeInput.ClickWindowElementAsync(
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), next);
            await Wait(() => media.SelectedGalleryItemId == "duplicate" && media.CanControl);
            Check(resolutions.Count == 2, "Mouse Next reaches a duplicate source with its own occurrence identity");
            Check(previous.Focus(FocusState.Keyboard)&&media.MoveFocus(FocusNavigationDirection.Right)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),next),
                "Right moves directly between the two enabled gallery arrows");
            Check(media.MoveFocus(FocusNavigationDirection.Left)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),previous),
                "Left moves directly back to the previous gallery arrow");
            var reads = resolutions.Count;
            declared = declared with { Source = duplicate.Source, Gallery = (MediaPlayerGalleryItem[])[duplicate with { Label = "Renamed occurrence" }, first, videoItem] };
            Publish();
            Check(media.SelectedGalleryItemId == "duplicate" && resolutions.Count == reads, "Reordering and relabeling retain the selected occurrence without reopening unchanged content");
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Released);
            await Wait(() => media.SelectedGalleryItemId == "first" && media.CanControl);
            Check(imageViewport.Focus(FocusState.Keyboard),"Image focus owns the attachment change before loading");
            delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Released);
            await Wait(() => media.SelectedGalleryItemId == "clip" && !media.CanControl);
            var loading=Find<ProgressRing>("MediaPlayer.Loading",root)!;
            var openingStatus=Find<TextBlock>("MediaPlayer.Status",root)!;
            await Wait(()=>loading.IsActive&&loading.ActualWidth>0);
            Check(loading.Visibility==Visibility.Visible&&!loading.IsTabStop&&!loading.IsHitTestVisible&&
                AutomationProperties.GetName(loading)=="Loading media"&&openingStatus.Visibility==Visibility.Collapsed,
                "Delayed media uses an accessible, noninteractive loading indicator without opening text");
            var feedback = Find<Grid>("MediaPlayer.Feedback", root)!;
            var loadingStage=(FrameworkElement)feedback.Parent;
            if (accessibleGallery)
                Check(feedback is { Visibility: Visibility.Visible, Background: SolidColorBrush { Color.A: 255 } } &&
                    !feedback.IsHitTestVisible && feedback.ActualWidth < loadingStage.ActualWidth / 2,
                    "Loading indicator has a compact opaque backing over transparent media");
            await Wait(()=>
            {
                var bounds=loading.TransformToVisual(loadingStage).TransformBounds(new(0,0,loading.ActualWidth,loading.ActualHeight));
                return Math.Abs(bounds.X+bounds.Width/2-loadingStage.ActualWidth/2)<1&&
                    Math.Abs(bounds.Y+bounds.Height/2-loadingStage.ActualHeight/2)<1;
            });
            var loadingBounds=loading.TransformToVisual(loadingStage).TransformBounds(new(0,0,loading.ActualWidth,loading.ActualHeight));
            Check(Math.Abs(loadingBounds.X+loadingBounds.Width/2-loadingStage.ActualWidth/2)<1&&
                Math.Abs(loadingBounds.Y+loadingBounds.Height/2-loadingStage.ActualHeight/2)<1,
                "Loading indicator is centered without changing the reserved media stage");
            await CaptureGallery("loading.png");
            media.SetActive(false,false);Check(!loading.IsActive,"Suspending pending media stops the loading animation");
            media.SetActive(true,true);Check(loading.IsActive,"Returning to pending media resumes its loading indicator");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),media),
                "A delayed attachment replacement parks focus on the stable player instead of escaping its preview owner");
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Released);
            await Wait(() => media.SelectedGalleryItemId == "first" && media.IsImage && media.CanControl);
            Check(!loading.IsActive&&loading.Visibility==Visibility.Collapsed,"A decoded replacement retires the old loading indicator");
            Check(feedback.Visibility == Visibility.Collapsed, "Successful playback removes the entire feedback surface");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),imageViewport),
                "The replacement restores image input only while the player still owns focus");
            delayed.SetResult(Resolve(declared.SelectGalleryItem("clip")));
            delayed = null; await Task.Delay(100);
            Check(media.SelectedGalleryItemId == "first" && media.IsImage && MediaPlayerView.ResidentPlayers == resident,
                "Changing selection during a delayed video read retires it without starting a decoder");

            var viewport = Find<ScrollViewer>("MediaPlayer.Image.Viewport", root)!;
            Check(viewport.Focus(FocusState.Keyboard), "Gallery image viewport accepts focus");
            var zoomFooter=(FrameworkElement)Find<Button>("MediaPlayer.Image.ZoomOut",root)!.Parent;
            var imageBounds=viewport.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0,0,viewport.ActualWidth,viewport.ActualHeight));
            Check(zoomFooter.TransformToVisual(root).TransformPoint(new()).Y>=imageBounds.Bottom-1,
                "Fitted gallery zoom controls occupy a footer outside the image");
            media.Handle(ControllerButton.RightStick, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightStick, ControllerEventPhase.Released);
            await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
            CheckFullscreenImageBounds();
            await CaptureGallery("fullscreen-image.png");
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Released);
            await Wait(() => media.SelectedGalleryItemId == "clip" && media.IsPlaying);
            Check(media.PreviewSystemControlsDisabledForValidation, "Fullscreen gallery clips keep playback controls local");
            Check(media.IsExpanded && MediaPlayerView.ResidentPlayers == resident + 1, "Fullscreen image-to-video selection keeps fullscreen and owns exactly one video player");
            var galleryChrome = (FrameworkElement)previous.Parent;
            var galleryTitle = Find<TextBlock>("MediaPlayer.Gallery.Position", root)!;
            Check(galleryTitle.Visibility == Visibility.Collapsed && galleryChrome.Visibility == Visibility.Visible,
                "Fullscreen keeps attachment arrows visible without the filename/title overlay");
            await Task.Delay(3200);
            Check(galleryChrome.Visibility == Visibility.Visible && previous.IsEnabled,
                "Fullscreen attachment navigation remains available after playback chrome times out");
            var arrowBounds = previous.TransformToVisual(fullscreen).TransformBounds(new(0, 0, previous.ActualWidth, previous.ActualHeight));
            Check(previous.Content is FontIcon { Glyph: "\uE76B" } && previous.CornerRadius.TopLeft >= previous.ActualWidth / 2 &&
                Math.Abs(arrowBounds.Y + arrowBounds.Height / 2 - fullscreen.ActualHeight / 2) < 1 &&
                next.Visibility == Visibility.Collapsed, "Only the available circular edge chevron appears at the last attachment");
            if (accessibleGallery)
            {
                Check(galleryChrome is Grid { Background: null } && previous.Background is SolidColorBrush buttonBrush &&
                    buttonBrush.Color.A == 255 && previous.Focus(FocusState.Keyboard),
                    "Fullscreen keeps the enabled arrow focusable on its own opaque backing above playing video");
                await CaptureGallery("fullscreen-navigation.png");
            }
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Released);
            await Wait(() => media.IsImage && media.CanControl && MediaPlayerView.ResidentPlayers == resident);
            Check(media.IsExpanded && media.SelectedGalleryItemId == "first", "Fullscreen video-to-image selection releases the old decoder without leaving fullscreen");
            media.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.B, ControllerEventPhase.Released);
            await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
            Check(media.CanControl, "B returns the selected gallery image to its inline viewer");
            Check(galleryChrome.Visibility == Visibility.Visible && galleryTitle.Visibility == Visibility.Visible,
                "Returning inline restores the filename/title alongside attachment navigation");
            declared = declared with { Source = duplicate.Source, Gallery = (MediaPlayerGalleryItem[])[duplicate, videoItem] };
            Publish();
            await Wait(() => media.SelectedGalleryItemId == "clip" && media.CanControl);
            Check(true, "Removing a selected occurrence chooses a bounded surviving position");
            failDuplicate = true;
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Released);
            var status = Find<TextBlock>("MediaPlayer.Status", root)!;
            await Wait(() => media.SelectedGalleryItemId == "duplicate" && status.Visibility == Visibility.Visible &&
                status.Text.StartsWith("This media could not be opened"));
            Check(!loading.IsActive&&loading.Visibility==Visibility.Collapsed,"Media failure replaces the spinner with readable error text");
            root.UpdateLayout();
            var stage = (FrameworkElement)feedback.Parent;
            if (accessibleGallery)
            {
                Check(feedback is { Visibility: Visibility.Visible, Background: SolidColorBrush { Color.A: 255 } } &&
                    feedback.ActualHeight < stage.ActualHeight && !status.IsTextTrimmed,
                    "Enlarged unavailable-media text fits on its high-contrast backing");
                await CaptureGallery("unavailable.png");
            }
            var statusBounds = status.TransformToVisual(stage).TransformBounds(new(0, 0, status.ActualWidth, status.ActualHeight));
            Check(Math.Abs(statusBounds.X + statusBounds.Width / 2 - stage.ActualWidth / 2) < 1 &&
                Math.Abs(statusBounds.Y + statusBounds.Height / 2 - stage.ActualHeight / 2) < 1 &&
                statusBounds.Width <= 480 && statusBounds.Right <= stage.ActualWidth,
                "Unavailable media explains the failure centrally within a bounded readable stage");
            Check(next.IsEnabled && !media.CanControl && MediaPlayerView.ResidentPlayers == resident,
                "A failed gallery source retains Next without stale playback or a decoder");
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightBumper, ControllerEventPhase.Released);
            await Wait(() => media.SelectedGalleryItemId == "clip" && media.CanControl && status.Visibility == Visibility.Collapsed);
            Check(true, "Gallery navigation recovers from a failed image to the next video");
            media.SetActive(false, false);
            await Wait(() => MediaPlayerView.ResidentPlayers == resident);
            Check(Find<Button>("MediaPlayer.Gallery.Next", root)!.Visibility == Visibility.Visible &&
                !Find<Button>("MediaPlayer.Gallery.Next", root)!.IsEnabled, "Suspension revokes gallery input while retaining its selection");
            const string testThumbnail = "https://example.invalid/first-entry.png";
            var thumbnailBytes = await File.ReadAllBytesAsync(imagePath);
            media.ValidationImagePayload = _ => new(thumbnailBytes);
            declared = UI.MediaGallery(new[] { videoItem with { PublicThumbnail = testThumbnail }, first with { PublicThumbnail = testThumbnail } },
                "entry", inlineCarousel: true).Media;
            media.SetActive(true, true);
            Publish();
            await Wait(() => media.IsPlaying && Find<Button>("MediaPlayer.Thumbnail.clip", root) is { IsLoaded: true });
            var clipTile = Find<Button>("MediaPlayer.Thumbnail.clip", root)!;
            Check(clipTile.Focus(FocusState.Keyboard), "The playing trailer can be browsed from its thumbnail");
            Publish();
            await Task.Delay(150);
            Check(!media.PreviewChromeVisibleForValidation && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), clipTile),
                "A playing trailer and an ordinary republication leave focused thumbnails unobstructed");
            Check(media.MoveFocus(FocusNavigationDirection.Up) && media.PreviewChromeVisibleForValidation &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), media.PreviewPlayControlForValidation),
                "One Up from a thumbnail reveals and focuses the native transport");
            await Wait(() => !media.PreviewChromeVisibleForValidation);
            Check(media.IsPlaying, "Idle controls disappear after entry while the trailer continues");
            Check(clipTile.Focus(FocusState.Keyboard) && media.MoveFocus(FocusNavigationDirection.Right),
                "The thumbnail strip remains reachable after the controls hide");
            await Wait(() => media.SelectedGalleryItemId == first.Id && media.IsImage);
            Check(media.MoveFocus(FocusNavigationDirection.Left), "The trailer can be reselected from its neighboring image");
            await Wait(() => media.SelectedGalleryItemId == videoItem.Id && media.IsPlaying);
            var canvas = media.PreviewVideoForValidation;
            await NativeContextProbeInput.HoverAsync(canvas, () =>
            {
                Check(!media.PreviewChromeVisibleForValidation, "Moving the pointer over a new trailer leaves its controls hidden");
                return Task.CompletedTask;
            });
            var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(canvas.XamlRoot.ContentIslandEnvironment.AppWindowId);
            await NativeContextProbeInput.ClickWindowElementAsync(window, canvas);
            Check(media.PreviewChromeVisibleForValidation && media.IsPlaying,
                "The first pointer click reveals the trailer controls without pausing it");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            delayed?.TrySetCanceled();
            if (failure is null || !Environment.GetCommandLineArgs().Contains("--retain-media-gallery-failure"))
            { media.Dispose(); await media.PlaybackRetirement; fullscreen.Close(restoreFocus: false); overlayPortal?.Dispose(); overlayLayer?.Dispose(); host.Children.Remove(root); }
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
            { passed = failure is null, checks, resolutions, error = failure?.ToString(), selected = media.SelectedGalleryItemId, pointerDiagnostics,
                pointerTarget = NativeContextProbeInput.LastPointerTrace, pressed, clicks,
                scope = declared.Source.IsAdaptiveDash ? "Native thumbnail gallery with a public Steam trailer and screenshots." :
                    "Native gallery with generated PNG and bundled synthetic MP4. Exact declaration and custody admission are tested separately through both pipes." }));
        }
        if (failure is not null) throw failure;

        void CheckFullscreenImageBounds()
        {
            var viewport = Find<ScrollViewer>("MediaPlayer.Image.Viewport", root)!;
            var origin = viewport.TransformToVisual(fullscreen).TransformPoint(new());
            Check(Math.Abs(origin.X) < 1 && Math.Abs(origin.Y) < 1 &&
                Math.Abs(viewport.ActualWidth - fullscreen.ActualWidth) < 1 &&
                Math.Abs(viewport.ActualHeight - fullscreen.ActualHeight) < 1,
                "Fullscreen arrows overlay the image without reserving space on any edge");
        }

        async Task CaptureGallery(string name)
        {
            var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);
            using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync()));
            await encoder.FlushAsync();stream.Seek(0);
            using var reader=new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);
            var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);await File.WriteAllBytesAsync(Path.Combine(directory,name),bytes);
        }
        HostMediaPlayerSource Resolve(MediaPlayerDefinition selected) => selected.Source.IsPublicMedia
            ? new(selected, selected.Source.Location!, IsImage: selected.Source.Kind == MediaPlayerSourceKind.WebImage)
            : selected.Source.Kind == MediaPlayerSourceKind.Capture
            ? new(selected, new Uri(imagePath).AbsoluteUri, IsImage: true, Width: 320, Height: 180)
            : new(selected, new Uri(clip).AbsoluteUri);
        async Task VerifyFailedTrailerNavigation()
        {
            const string thumb = "https://example.invalid/thumb.png";
            var left = first with { PublicThumbnail = thumb };
            var right = duplicate with { PublicThumbnail = thumb };
            var broken = new MediaPlayerGalleryItem("broken", "Unavailable trailer",
                MediaPlayerSource.PublicFile("https://example.invalid/trailer.mp4"), new(AutoPlay: true)) { PublicThumbnail = thumb };
            var gallery = UI.MediaGallery(new[] { left, broken, right }, "regression", inlineCarousel: true).Media;
            media.ValidationImagePayload = _ => new(new byte[] { 0 });
            foreach (var backwards in new[] { true, false })
            {
                var attempts = 0;
                var delayedFailure = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Apply() => media.Apply(_ => throw new InvalidOperationException(), gallery, () => true, backwards,
                    resolveGalleryItem: (id, _, _) =>
                    {
                        if (id != broken.Id) return Task.FromResult(Resolve(gallery.SelectGalleryItem(id)));
                        attempts++;
                        return delayedFailure.Task;
                    });
                Apply();
                await Wait(() => media.CanControl);
                await Wait(() => Find<TextBlock>("MediaPlayer.ThumbnailFallback." + left.Id, root) is not null &&
                    Find<TextBlock>("MediaPlayer.ThumbnailFallback." + right.Id, root) is not null &&
                    Find<TextBlock>("MediaPlayer.ThumbnailFallback." + broken.Id, root) is not null);
                Check(true, "All thumbnail downloads can fail while the original remains usable");
                var from = backwards ? right : left;
                var to = backwards ? left : right;
                Button Tile(MediaPlayerGalleryItem item) => Find<Button>("MediaPlayer.Thumbnail." + item.Id, root)!;
                Check(Tile(from).Focus(FocusState.Keyboard), "Focus the thumbnail on the approach side");
                await Wait(() => media.SelectedGalleryItemId == from.Id && media.CanControl);
                var direction = backwards ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;
                Check(media.MoveFocus(direction), "Focus moves to the pending trailer");
                await Wait(() => media.SelectedGalleryItemId == broken.Id && attempts == 1);
                var ring = Find<ProgressRing>("MediaPlayer.Loading", media)!;
                var center = Find<MediaGalleryCarousel>("MediaPlayer.Carousel", media)!.Center;
                await Wait(() => ring.IsActive && center.ActualHeight > 0);
                root.UpdateLayout();
                var ringBounds = ring.TransformToVisual(center).TransformBounds(new(0, 0, ring.ActualWidth, ring.ActualHeight));
                Check(Math.Abs(ringBounds.X + ringBounds.Width / 2 - center.ActualWidth / 2) < 1 &&
                    Math.Abs(ringBounds.Y + ringBounds.Height / 2 - center.ActualHeight / 2) < 1,
                    "Pending carousel media centers the indicator inside the selected preview");
                delayedFailure.SetException(new IOException("Unavailable trailer"));
                await Wait(() => media.SelectedGalleryItemId == to.Id && media.CanControl &&
                    ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Tile(to)));
                Check(Tile(broken) is null, "Failure skips in the requested direction and removes the unavailable thumbnail");
                Apply();
                Check(media.MoveFocus(backwards ? FocusNavigationDirection.Right : FocusNavigationDirection.Left),
                    "Reverse navigation reaches the other surviving thumbnail");
                await Wait(() => media.SelectedGalleryItemId == from.Id && media.CanControl);
                Check(attempts == 1, "An unchanged declaration and reverse navigation do not retry the failed occurrence");
            }
            var pending = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HostMediaPlayerSource> ResolveWithdrawn(string? id, string? lease, CancellationToken token) => id == broken.Id
                ? pending.Task : Task.FromResult(Resolve(gallery.SelectGalleryItem(id)));
            media.Apply(_ => throw new InvalidOperationException(), gallery, () => true, "withdrawal", resolveGalleryItem: ResolveWithdrawn);
            await Wait(() => media.CanControl);
            Find<Button>("MediaPlayer.Thumbnail." + right.Id, root)!.Focus(FocusState.Keyboard);
            await Wait(() => media.SelectedGalleryItemId == right.Id);
            media.MoveFocus(FocusNavigationDirection.Left);
            await Wait(() => media.SelectedGalleryItemId == broken.Id);
            var revised = gallery with { Gallery = new[] { left, right } };
            media.Apply(_ => throw new InvalidOperationException(), revised, () => true, "withdrawal", resolveGalleryItem: ResolveWithdrawn);
            pending.SetException(new IOException("Retired trailer failed late"));
            await Wait(() => media.SelectedGalleryItemId == left.Id && media.CanControl &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("MediaPlayer.Thumbnail." + left.Id, root)));
            Check(true, "A withdrawn trailer keeps the browsing direction and late failure cannot reclaim focus");
            // Begin fullscreen with one working original, then lose every original
            // when the provider publishes a new source revision.
            media.ActivateFocusedControl();
            await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
            var missingImage = new MediaPlayerGalleryItem("missing-image", "Missing screenshot",
                MediaPlayerSource.PublicImage("https://example.invalid/image.png")) { PublicThumbnail = thumb };
            var unavailable = UI.MediaGallery(new[] { broken, missingImage }, "empty", inlineCarousel: true).Media;
            var requests = new List<string>();
            void FailAll() => media.Apply(_ => throw new InvalidOperationException(), unavailable, () => true, "all-fail",
                resolveGalleryItem: (id, _, _) =>
                {
                    requests.Add(id!);
                    return Task.FromException<HostMediaPlayerSource>(new IOException("Missing original"));
                });
            FailAll();
            await Wait(() => media.Visibility == Visibility.Collapsed && !media.IsExpanded);
            FailAll();
            Check(requests.Count == 2 && requests.Distinct().Count() == 2 && !media.CanControl,
                "All failed originals close fullscreen and collapse the gallery after one attempt each");
            media.ValidationImagePayload = null;
        }
        void Publish() => media.Apply(_ => Task.FromResult(Resolve(declared.SelectGalleryItem(null))), declared, () => true, "gallery-owner",
            resolveGalleryItem: (key, _, _) =>
            {
                resolutions.Add(key!);
                var selected = declared.SelectGalleryItem(key);
                if (failDuplicate && key == "duplicate") return Task.FromException<HostMediaPlayerSource>(new IOException("Synthetic unavailable image"));
                return (key == "clip" || selected.Source.Kind == MediaPlayerSourceKind.SavedCapture) && delayed is not null ? delayed.Task : Task.FromResult(Resolve(selected));
            });
    }
}
