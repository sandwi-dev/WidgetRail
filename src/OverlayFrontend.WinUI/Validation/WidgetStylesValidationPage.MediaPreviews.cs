using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MediaPreviewsAsync(string directory, string clip)
    {
        Directory.CreateDirectory(directory);
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.ResizeClient(new((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(900 * scale)));
        var dialogPreview = Environment.GetCommandLineArgs().Contains("--media-preview-dialog");
        var shellHandoff = Environment.GetCommandLineArgs().Contains("--media-preview-shell-handoff");
        var root = new Grid { Width = 820, Height = 600 };
        var inlineHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var page = new WidgetViewPresenter { Width = 800, Height = 580 };
        inlineHost.Content = page; root.Children.Add(inlineHost); PresentationLayers.Attach(root);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        if (shellHandoff) fullscreen.Changed += () =>
        {
            // Match production RefreshFullscreenChrome: its ordinary widget
            // host is disabled during entry and hidden once fullscreen settles.
            inlineHost.IsEnabled = !fullscreen.IsOpen;
            inlineHost.Visibility = fullscreen.IsOpen && !fullscreen.IsTransitioning ? Visibility.Collapsed : Visibility.Visible;
        };
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popup = new NativePopupTheme(); popup.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var authored = WrssParser.Parse(".preview-media { width:100%; height:250px; } .preview-source { width:180px; }", "preview-media.wrss");
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new((WrssDocument[])[authored.Document], []),
            catalog.Load("widgetrail.builtin.velvet-dusk", "1.0.0").Package).Theme!;
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        page.ApplyAppearance(appearance, false);
        Exception? failure = null;
        object? failedState = null;
        MediaPlayerView? testedMedia = null;
        var expandClicks = 0;
        var trace = new List<string>();
        void Trace(string message) { if (trace.Count >= 100) trace.RemoveAt(0); trace.Add(message); }
        page.FocusTrace = Trace;
        fullscreen.TraceForValidation = message => Trace("fullscreen:" + message);
        page.Failed = error => failure ??= error;
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var residentBefore = MediaPlayerView.ResidentPlayers;
        var posterBytes = await ArtworkPngAsync(320, 180);
        var posterReads = 0;
        page.ResolveArtworkAsync = (handle, _) =>
        {
            ++posterReads;
            if (handle != "fixture.clip.poster") throw new InvalidOperationException("Unexpected native poster handle.");
            return Task.FromResult<WidgetArtworkSource?>(new WidgetEncodedArtwork(WidgetArtworkContentType.Png, posterBytes));
        };
        page.ValidationMediaResolver = async (definition, token) =>
        {
            await opening.Task.WaitAsync(token);
            return new HostMediaPlayerSource(definition, new Uri(clip).AbsoluteUri,
                Width: 1280, Height: 720, DurationSeconds: 5, ExpiresAtUnixMilliseconds: long.MaxValue);
        };
        try
        {
            var fragment = UI.Stack("preview.content", UI.MediaPlayer(MediaPlayerSource.LocalFile(clip), "preview.player",
                options: new(AutoPlay: true, Loop: true, Muted: false)).WithPoster(new("fixture.clip.poster")).Classes("preview-media"));
            var sourceContent = UI.Stack("preview.page",
                UI.FocusPresentationSurface(UI.Button("Clip", "open", "preview.source").Classes("preview-source").PresentOnFocus(fragment),
                    UI.Text("", "empty"), "preview.surface") with
                { Preview = new(560, 360, DelayMilliseconds: 0) { AllowMedia = true, EnterButton = ControllerButton.RightStick } });
            var view = dialogPreview
                ? new WidgetView(UI.Button("Background", "background", "preview.background"))
                    .WithModal(new("preview.modal", "Dialog clip preview", sourceContent, "preview.source", "close"))
                : new WidgetView(sourceContent.InputScope("preview.page"), "preview.source");
            var snapshot = view.CreateSnapshot("media-preview-input", 1);
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node)
            {
                styles[node.Id] = Compute(theme, node);
                foreach (var child in node.Children) Visit(child);
                if (node.FocusPresentation is { } content) Visit(content);
                if (node.DefaultFocusPresentation is { } empty) Visit(empty);
            }
            Visit(snapshot.Root);
            foreach (var role in new[] { "body", "canvas", "panel", "preview", "hint" }) styles[role] = Compute(theme, role, role, []);
            popup.Update(styles, appearance);
            var descriptor = new BridgeWidgetDescriptor { Id = "preview", Name = "Media preview", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "media-preview", PresentationGeneration = "media-preview", Icon = WidgetGlyph.Play, PackageContentDigest = "" };
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            page.UpdateLayout();
            await Wait(() => Find<Button>("Widget.preview.source", root) is { IsLoaded: true });
            page.Enter(restoreNativeFocus: false);
            Find<Button>("Widget.preview.source", root)!.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen && Find<MediaPlayerView>("Widget.preview.player", root)?.PosterLoadingForValidation == true);
            var media = Find<MediaPlayerView>("Widget.preview.player", root)!;
            testedMedia = media;
            ((Button)media.PreviewExpandControlForValidation).Click += (_, _) => ++expandClicks;
            Check(posterReads == 1 && !media.IsPlaying, "Existing scoped artwork demand shows the native poster while media resolution is delayed");
            await CapturePixels("poster", media);
            if (Environment.GetCommandLineArgs().Contains("--media-preview-continuity")) await CapturePixels("handoff", media);
            opening.TrySetResult();
            await Wait(() => media.IsPlaying);
            Check(media.HasPosterForValidation && posterReads == 1 && MediaPlayerView.ResidentPlayers == residentBefore + 1,
                "Playback uses the same native player poster with one artwork resolution and one media decoder");
            Check(media.PreviewMutedForValidation && media.PreviewSystemControlsDisabledForValidation && !page.IsPreviewEnteredForValidation,
                "Host presenter forces passive media mute and suppresses system media controls before preview entry");
            var originalPlayer = media.PlaybackPlayerForValidation;
            // Playing may precede the first native arrange following aspect
            // discovery. Establish the real fitted slot before the input action.
            await Wait(() => SameBounds(media.ExpectedPreviewVideoBoundsForValidation, media.PreviewVideoBoundsForValidation));
            var passiveBounds = media.PreviewVideoBoundsForValidation;
            var mediaPlane = root.Children.OfType<MediaPresentationStage>().Single();
            var previewLayer = root.Children.OfType<PresentationPortal>().Single(portal => portal.Layer is PresentationLayer.ContentPreview or PresentationLayer.WidgetDialogPreview);
            var mediaBand = dialogPreview ? PresentationLayer.WidgetDialogPreviewMedia : PresentationLayer.ContentPreviewMedia;
            Check(Canvas.GetZIndex(mediaPlane) == (int)mediaBand && Canvas.GetZIndex(mediaPlane) > Canvas.GetZIndex(previewLayer) &&
                Canvas.GetZIndex(mediaPlane) < (int)(dialogPreview ? PresentationLayer.FullscreenBackdrop : PresentationLayer.WidgetDialog),
                "Preview-owned native video paints above its frame and below the next modal/fullscreen boundary");
            await CapturePixels("passive-video", media);
            if (Environment.GetCommandLineArgs().Contains("--media-preview-pixel-ack"))
            {
                // Negative control uses the existing cover lifetime while the
                // same player advances; the resolver now repairs old bands on
                // layout, so the fixture must not fight that ownership policy.
                mediaPlane.SetCovered(true);
                try { await CapturePixels("covered-video", media); }
                finally { mediaPlane.SetCovered(false); }
            }
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick, ControllerEventPhase.Released);
            await Wait(() => page.IsPreviewEnteredForValidation);
            media.UpdateLayout();
            await Wait(() => SameBounds(passiveBounds, media.PreviewVideoBoundsForValidation));
            Check(ReferenceEquals(media.PlaybackPlayerForValidation, originalPlayer),
                "Entering preview controls preserves the video rectangle and native player");
            media.PreviewOptionsControlForValidation.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await Wait(() => media.PreviewOptionsLoadedForValidation);
            Check(page.IsPreviewEnteredForValidation && page.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Label == "Choose"),
                "Playback options retain preview ownership and publish the native menu guide");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            Check(!media.HasOptionsMenu && page.IsPreviewEnteredForValidation,
                "Back closes playback options before leaving the entered preview");
            await NativeContextProbeInput.LeftClickAsync(media.PreviewOptionsControlForValidation,
                new(media.PreviewOptionsControlForValidation.ActualWidth / 2, media.PreviewOptionsControlForValidation.ActualHeight / 2));
            await Wait(() => media.PreviewOptionsLoadedForValidation);
            var restart = media.PreviewOptionsFirstForValidation!;
            await NativeContextProbeInput.LeftClickAsync(restart, new(restart.ActualWidth / 2, restart.ActualHeight / 2));
            await Wait(() => !media.HasOptionsMenu);
            Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen && ReferenceEquals(media.PlaybackPlayerForValidation, originalPlayer),
                "Native playback menu pointer action preserves the preview and its existing player");
            double draggedVolume = -1;
            await NativeContextProbeInput.HoldSliderDragAsync(media.PreviewVolumeForValidation, async () =>
            {
                media.HidePreviewChromeForValidation();
                await Task.Delay(3200);
                Check(media.PreviewChromeVisibleForValidation && page.IsPreviewEnteredForValidation,
                    "Native slider capture keeps overlay controls and preview alive beyond the auto-hide delay");
                draggedVolume = media.PreviewVolumeForValidation.Value;
            }, async () =>
            {
                await Task.Delay(150);
                Check(Math.Abs(draggedVolume - media.PreviewVolumeForValidation.Value) < .01 &&
                    !Input.PointerCaptureLifetime.HasCaptureWithin(media.PreviewVolumeForValidation),
                    "Released native volume drag cannot keep adjusting on later mouse movement");
            });
            media.FocusPreviewVolumeForValidation();
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(media.IsAdjustingVolume, "Entered media preview reaches its native volume adjustment");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            Check(!media.IsAdjustingVolume && page.IsPreviewEnteredForValidation, "First B leaves native volume adjustment without leaving preview");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            Check(!page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen && media.PreviewMutedForValidation,
                "Second B returns to the source with the passive preview muted");
            media.UpdateLayout();
            Check(SameBounds(passiveBounds, media.PreviewVideoBoundsForValidation),
                "Leaving preview controls preserves the same video rectangle");
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick, ControllerEventPhase.Released);
            media.UpdateLayout();
            await Wait(() => media.PreviewExpandControlForValidation is { IsLoaded: true, IsEnabled: true, ActualWidth: > 0, ActualHeight: > 0 });
            await CapturePixels("inline-controls", media);
            await NativeContextProbeInput.LeftClickAsync(media.PreviewExpandControlForValidation,
                new(media.PreviewExpandControlForValidation.ActualWidth / 2, media.PreviewExpandControlForValidation.ActualHeight / 2),
                () => Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen && ((Button)media.PreviewExpandControlForValidation).IsPressed,
                    "Native inline Expand press reaches its button and belongs to the preview before release"));
            await Wait(() => media.IsExpanded && !media.IsMovingPresentation && !fullscreen.IsTransitioning);
            if (shellHandoff)
            {
                await Wait(() => inlineHost.Visibility == Visibility.Collapsed);
                await Task.Delay(120);
                Check(media.IsExpanded && page.IsFocusPreviewOpen && media.IsPlaying,
                    "Production fullscreen handoff may hide the inline owner without retiring its current preview player");
            }
            Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen, "Expanding native media preserves its logical preview owner");
            await CapturePixels("expanded-video", media);
            await NativeContextProbeInput.LeftClickAsync(media.PreviewPlayControlForValidation,
                new(media.PreviewPlayControlForValidation.ActualWidth / 2, media.PreviewPlayControlForValidation.ActualHeight / 2));
            await Wait(() => !media.IsPlaying);
            Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen && media.IsExpanded,
                "Fullscreen chrome click follows native playback handling without dismissing the preview");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            await Wait(() => !media.IsExpanded && !media.IsMovingPresentation && !fullscreen.IsOpen);
            Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen, "Fullscreen B collapses into the same entered preview");
            Check(ReferenceEquals(media.PlaybackPlayerForValidation, originalPlayer), "Fullscreen handoff returns the exact same native player instance");
            await CapturePixels("returned-video", media);
            Check(Canvas.GetZIndex(mediaPlane) == (int)mediaBand,
                $"Collapse restores the same preview-owned media band (actual={Canvas.GetZIndex(mediaPlane)}, attached={ReferenceEquals(mediaPlane.Parent, root)})");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            Check(!page.IsPreviewEnteredForValidation, "Following B exits preview interaction normally");
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick, ControllerEventPhase.Released);
            media.FocusPreviewVolumeForValidation();
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            NativeContextProbeInput.Escape();
            await Wait(() => !media.IsAdjustingVolume);
            Check(page.IsPreviewEnteredForValidation, "Keyboard Escape leaves native volume adjustment before the preview");
            NativeContextProbeInput.Escape();
            await Wait(() => !page.IsPreviewEnteredForValidation);
            Check(page.IsFocusPreviewOpen && media.PreviewMutedForValidation, "Next Escape returns to the muted passive preview");
            var outsideClicks = 0;
            void Click(object sender, RoutedEventArgs args) => ++outsideClicks;
            outside.Click += Click;
            try
            {
                await NativeContextProbeInput.LeftClickAsync(outside, new(outside.ActualWidth / 2, outside.ActualHeight / 2));
                await Wait(() => !page.IsFocusPreviewOpen && outsideClicks == 1);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), outside),
                    "Ordinary outside click still dismisses preview and activates its existing target once");
            }
            finally { outside.Click -= Click; }
            if (shellHandoff)
            {
                Find<Button>("Widget.preview.source", root)!.Focus(FocusState.Keyboard);
                await Wait(() => page.IsFocusPreviewOpen && media.IsPlaying);
                await page.HandleControllerButtonAsync(ControllerButton.RightStick);
                await page.HandleControllerButtonAsync(ControllerButton.RightStick, ControllerEventPhase.Released);
                media.PreviewExpandControlForValidation.Focus(FocusState.Keyboard);
                NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Enter);
                await Wait(() => media.IsExpanded && !media.IsMovingPresentation && !fullscreen.IsTransitioning);
                Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen, "Native keyboard Enter expands the current preview through the shared fullscreen host");
                NativeContextProbeInput.Escape();
                await Wait(() => !media.IsExpanded && !media.IsMovingPresentation && !fullscreen.IsOpen);
                Check(page.IsPreviewEnteredForValidation && page.IsFocusPreviewOpen, "Native Escape restores the same entered preview after shell handoff");
                media.PreviewExpandControlForValidation.Focus(FocusState.Keyboard);
                await page.HandleControllerButtonAsync(ControllerButton.A);
                await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Wait(() => media.IsExpanded && !media.IsMovingPresentation && !fullscreen.IsTransitioning);
                Check(page.IsFocusPreviewOpen, "Controller A expands the current preview without a replacement player");
                var replacement = new WidgetView(UI.Stack("preview.page", UI.Button("Current page", "continue", "preview.current"))
                    .InputScope("preview.page"), "preview.current").CreateSnapshot("media-preview-input", 2);
                Visit(replacement.Root);
                page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    replacement.WidgetInstanceId, replacement.Sequence, replacement.ActiveInputScopeId), descriptor, replacement, styles));
                await Wait(() => !fullscreen.IsOpen && !page.IsFocusPreviewOpen && MediaPlayerView.ResidentPlayers == residentBefore);
                Check(true, "Removing the preview source while expanded revokes fullscreen and releases its decoder");
            }
            if (failure is not null) throw failure;
        }
        catch (Exception error)
        {
            failure = error;
            failedState = new { preview = page.FocusPreviewDiagnostics(), expanded = testedMedia?.IsExpanded,
                moving = testedMedia?.IsMovingPresentation, playing = testedMedia?.IsPlaying, expandClicks,
                fullscreen.IsOpen, fullscreen.IsTransitioning, shellHandoff, inlineVisibility = inlineHost.Visibility.ToString(),
                planes = root.Children.OfType<MediaPresentationStage>().Select(plane => new { layer = Canvas.GetZIndex(plane), plane.Visibility, plane.HasSlot }).ToArray(), trace };
        }
        finally { opening.TrySetCanceled(); await page.DisposeAsync(); fullscreen.Close(restoreFocus: false); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks,
            error = failure?.ToString(), failedState, scope = "Real native media/preview controls with a controlled local resolver; file custody and lease authority are tested separately through both pipes." }));
        if (failure is not null) throw failure;

        async Task CapturePixels(string phase, MediaPlayerView media)
        {
            if (!Environment.GetCommandLineArgs().Contains("--media-preview-pixel-ack")) return;
            await Wait(() => media.PreviewVideoBoundsForValidation.Width > 0 && media.PreviewVideoBoundsForValidation.Height > 0);
            // Controls now overlay video instead of shrinking it. Sample the
            // remaining unobscured content with the same color threshold.
            var rect = media.UnobscuredPreviewVideoBoundsForValidation;
            var factor = XamlRoot.RasterizationScale;
            var phasePath = Path.Combine(directory, phase + ".phase.json");
            await File.WriteAllTextAsync(phasePath + ".pending", JsonSerializer.Serialize(new { phase, processId = Environment.ProcessId,
                window = WinRT.Interop.WindowNative.GetWindowHandle(App.Window).ToInt64(),
                x = rect.X * factor, y = rect.Y * factor, width = rect.Width * factor, height = rect.Height * factor }));
            File.Move(phasePath + ".pending", phasePath);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            if (phase == "handoff")
            {
                while (!File.Exists(phasePath + ".started")) await Task.Delay(10, deadline.Token);
                opening.TrySetResult();
            }
            while (!File.Exists(phasePath + ".captured")) await Task.Delay(25, deadline.Token);
            using var acknowledgement = JsonDocument.Parse(await File.ReadAllTextAsync(phasePath + ".captured"));
            Check(acknowledgement.RootElement.GetProperty("passed").GetBoolean(),
                phase == "handoff" ? "Owned-window pixel samples retain poster or video throughout source opening" :
                "Actual native window pixels contain the expected " + phase + " content");
        }
        static bool SameBounds(Windows.Foundation.Rect left, Windows.Foundation.Rect right) =>
            Math.Abs(left.X - right.X) < .5 && Math.Abs(left.Y - right.Y) < .5 &&
            Math.Abs(left.Width - right.Width) < .5 && Math.Abs(left.Height - right.Height) < .5;
    }
}
