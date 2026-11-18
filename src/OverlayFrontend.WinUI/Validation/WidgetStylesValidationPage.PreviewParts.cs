using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
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
    private async Task PreviewPartsAsync(string directory, string clip)
    {
        Directory.CreateDirectory(directory); clip = Path.GetFullPath(clip);
        var imagePath = FrontendArguments.Value(Environment.GetCommandLineArgs(), "--preview-parts-image");
        var imageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uint imageWidth = 0, imageHeight = 0;
        if (imagePath is not null)
        {
            imagePath = Path.GetFullPath(imagePath);
            using var stream = await (await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath)).OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            imageWidth = decoder.PixelWidth; imageHeight = decoder.PixelHeight;
        }
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.ResizeClient(new((int)(1100 * scale), (int)(900 * scale)));
        var root = new Grid { Width = 820, Height = 640 };
        var page = new WidgetViewPresenter { Width = 800, Height = 620 };
        root.Children.Add(page); PresentationLayers.Attach(root); new MediaFullscreenLayer().Attach(root);
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popup = new NativePopupTheme(); popup.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        page.ApplyAppearance(appearance, false);
        page.ValidationMediaResolver = async (definition, token) =>
        {
            if (imagePath is not null) await imageReady.Task.WaitAsync(token);
            return new HostMediaPlayerSource(definition, new Uri(imagePath ?? clip).AbsoluteUri,
                IsImage: imagePath is not null, Width: imagePath is null ? 1280 : (int)imageWidth,
                Height: imagePath is null ? 720 : (int)imageHeight, DurationSeconds: imagePath is null ? 1 : 0,
                ExpiresAtUnixMilliseconds: long.MaxValue);
        };
        Exception? failure = null; var measurements = new List<object>(); long generation = 0;
        try
        {
            ValidateAspectGridDefaults();
            Publish(scoped: false);
            await Wait(() => Find<Button>("Widget.parts.source", root) is { IsLoaded: true });
            page.Enter(restoreNativeFocus: false); Find<Button>("Widget.parts.source", root)!.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen && page.FocusPreviewFrame?.Surface.ActualWidth > 0 &&
                Find<MediaPlayerView>("Widget.parts.media", root) is { IsLoaded: true });
            var frame = page.FocusPreviewFrame!;
            Check(frame.Surface.Padding == new Thickness(12) && frame.Footer.Padding == new Thickness(0, 8, 0, 0),
                "Ordinary preview keeps12px frame and8px footer gap despite generic owner class padding");
            Publish(scoped: true);
            await Wait(() => frame.Surface.Padding == new Thickness(0) && frame.Footer.Padding == new Thickness(12, 8, 12, 12));
            Check(ReferenceEquals(frame, page.FocusPreviewFrame), "Scoped part stylesheet updates the retained preview owner");
            var footerCopy = page.CurrentBinding!.RenderStyles["parts.owner"].PreviewFooter!.ToDictionary(pair => pair.Key, pair => pair.Value with { });
            Check(!frame.ApplyFooterStyle(footerCopy), "Equal immutable footer maps do not trigger sizing invalidation");
            if (imagePath is not null)
            {
                var media = Find<MediaPlayerView>("Widget.parts.media", root)!;
                await Wait(() => media.ActualWidth > 100 && media.ActualHeight > 100);
                Sample("before-decode");
                imageReady.TrySetResult();
                await Wait(() => media.ImageDecodedForValidation);
                for (var i = 0; i < 8; ++i) { await Task.Delay(100); Sample("decoded-" + i); }
                Check(Math.Abs(media.ActualWidth / media.ActualHeight - 16d / 9) < .01,
                    "Decoded intrinsic image dimensions cannot expand the authored16:9 media viewport");
                var open = Find<Button>("Widget.parts.open", root)!;
                var actionBounds = open.TransformToVisual(frame.Body).TransformBounds(new(0, 0, open.ActualWidth, open.ActualHeight));
                Check(actionBounds.Bottom <= frame.Body.ActualHeight + 1, "Decoded image keeps following action inside the preview body");
                page.Width = 440; root.Width = 460; page.UpdateLayout();
                await Wait(() => frame.ActualWidth < 440 && frame.ActualWidth > 300);
                await Task.Delay(100);
                Sample("decoded-narrow");
                Check(Math.Abs(media.ActualWidth / media.ActualHeight - 16d / 9) < .01,
                    "Decoded image remains bounded16:9 after narrow viewport remeasurement");
                void Sample(string phase)
                {
                    var hero = Find<Grid>("Widget.parts.hero", root)!;
                    measurements.Add(new { phase, imageWidth, imageHeight, frameWidth = frame.ActualWidth, frameHeight = frame.ActualHeight,
                        mediaWidth = media.ActualWidth, mediaHeight = media.ActualHeight,
                        mediaBounds = media.TransformToVisual(frame).TransformBounds(new(0, 0, media.ActualWidth, media.ActualHeight)),
                        heroWidth = hero.ActualWidth, heroHeight = hero.ActualHeight,
                        rows = hero.RowDefinitions.Select(row => new { row.ActualHeight, unit = row.Height.GridUnitType.ToString() }).ToArray(),
                        image = media.ImageGeometryForValidation });
                }
            }
            else
            {
            await Measure("wide", 560);
            page.Width = 440; root.Width = 460; page.UpdateLayout();
            await Wait(() => frame.ActualWidth < 440 && frame.ActualWidth > 300);
            await Measure("narrow", frame.ActualWidth);
            var bounds = frame.Surface.TransformToVisual(page).TransformBounds(new(0, 0, frame.Surface.ActualWidth, frame.Surface.ActualHeight));
            Check(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= page.ActualWidth + .5,
                "Edge-to-edge preview remains clamped inside narrow widget viewport");
            Publish(scoped: true, contrast: true);
            await Wait(() => frame.Surface.BorderThickness == new Thickness(2) && frame.Footer.Background is SolidColorBrush footer &&
                footer.Color == ShellChromePalette.WindowColors().Background);
            Check(frame.Surface.Padding == new Thickness(0) && frame.Footer.Padding == new Thickness(12, 8, 12, 12),
                "High contrast retains scoped spacing and replaces authored footer paint with system colors");
            Publish(scoped: false, globalPadding: "4px 8px 12px 16px");
            await Wait(() => frame.Surface.Padding == new Thickness(16, 4, 8, 12));
            Check(frame.Footer.Padding == new Thickness(0, 8, 0, 0), "Removing scoped rules restores global four-value padding and footer default");
            Publish(scoped: false, omitParts: true);
            await Wait(() => frame.Surface.Padding == new Thickness(12));
            Check(frame.Footer.Padding == new Thickness(0, 8, 0, 0), "Missing part payload uses existing global theme fallback");
            }
        }
        catch (Exception error) { failure = error; }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
            { passed = failure is null, checks, measurements, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        async Task Measure(string name, double width)
        {
            var media = Find<MediaPlayerView>("Widget.parts.media", root)!;
            await Wait(() => Math.Abs(media.ActualWidth - (page.FocusPreviewFrame!.Surface.ActualWidth - 2)) < 1);
            await Task.Delay(50); // Observe a settled native arrange, not metadata dimensions.
            var video = media.PreviewVideoBoundsForValidation;
            var card = media.TransformToVisual(XamlRoot.Content).TransformBounds(new(0, 0, media.ActualWidth, media.ActualHeight));
            var onePhysicalPixel = 1 / XamlRoot.RasterizationScale;
            var maximumEdgeDifference = new[] { Math.Abs(video.Left - card.Left), Math.Abs(video.Right - card.Right),
                Math.Abs(video.Top - card.Top), Math.Abs(video.Bottom - card.Bottom) }.Max();
            measurements.Add(new { name, width, frameWidth = page.FocusPreviewFrame!.ActualWidth,
                mediaWidth = media.ActualWidth, mediaHeight = media.ActualHeight, video, card, onePhysicalPixel,
                maximumEdgeDifferencePhysicalPixels = maximumEdgeDifference * XamlRoot.RasterizationScale });
            Check(Math.Abs(media.ActualWidth / media.ActualHeight - 16d / 9) < .01,
                name + " aspect-ratio Star-cell Grid measures real media at16:9");
            Check(maximumEdgeDifference <= onePhysicalPixel + .001,
                name + " native video edges fill the media rectangle within one physical pixel of raster rounding");
        }
        void Publish(bool scoped, bool contrast = false, string globalPadding = "12px", bool omitParts = false)
        {
            var css = ".owner {padding:31px;background:#912455;} .body {gap:0px;width:100%;} " +
                ".hero {width:100%;min-width:0px;aspect-ratio:16/9;} " +
                ".media {width:100%;height:100%;min-width:0px;min-height:0px;padding:0px;border-width:0px;corner-radius:0px;background:transparent;} " +
                "preview {padding:" + globalPadding + ";} " +
                (scoped ? "preview.owner {padding:0px;} preview-footer.owner {padding:8px 12px 12px;background:#412c59;}" : "");
            var parsed = WrssParser.Parse(css, "preview-parts.wrss");
            var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new((WrssDocument[])[parsed.Document], []), new([], [])).Theme
                ?? throw new InvalidOperationException("Preview part stylesheet failed.");
            var body = UI.Stack("parts.body", UI.Grid("parts.hero", (GridTrackDefinition[])[GridTrack.Star()], (GridTrackDefinition[])[GridTrack.Star()],
                UI.MediaPlayer(MediaPlayerSource.LocalFile(imagePath ?? clip), "parts.media", options: new(Muted: true)).Classes("media").InGrid()).Classes("hero"),
                UI.Button("Open", "open", "parts.open")).Classes("body");
            var owner = (UI.FocusPresentationSurface(UI.Button("Capture", "source", "parts.source").PresentOnFocus(body),
                UI.Text("", "parts.empty"), "parts.owner") with
                { Preview = new(560, 520, AnchorPlacement.CenterOnAnchor, DelayMilliseconds: 0) { AllowMedia = true, EnterButton = ControllerButton.RightStick, SizeToContentHeight = true } }).Classes("owner");
            var snapshot = new WidgetView(UI.Stack("parts.page", owner).InputScope("parts.scope"), "parts.source").CreateSnapshot("preview-parts", ++generation);
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node)
            {
                var value = Compute(theme, node);
                if (!omitParts && node.Kind == ViewNodeKind.FocusPresentationSurface && node.FocusPreview is not null)
                    value = value with { PreviewFrame = Part("preview", node), PreviewFooter = Part("preview-footer", node) };
                styles[node.Id] = value;
                foreach (var child in node.Children) Visit(child);
                if (node.FocusPresentation is { } focused) Visit(focused);
                if (node.DefaultFocusPresentation is { } empty) Visit(empty);
            }
            IReadOnlyDictionary<string, BridgeComputedStyleValue> Part(string role, ViewNode node) => theme.ResolvePart(new(role, node.Id, node.StyleClasses.ToHashSet())).Properties
                .ToDictionary(pair => pair.Key, pair => new BridgeComputedStyleValue { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
            Visit(snapshot.Root);
            foreach (var role in new[] { "body", "canvas", "panel", "preview", "preview-footer", "hint" }) styles[role] = Compute(theme, role, role, []);
            var nextAppearance = appearance with { Contrast = contrast ? ContrastPreference.High : ContrastPreference.System };
            popup.Update(styles, nextAppearance); page.ApplyAppearance(nextAppearance, false);
            var descriptor = new BridgeWidgetDescriptor { Id = "parts", Name = "Preview parts", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "parts", PresentationGeneration = "parts", Icon = WidgetGlyph.Play, PackageContentDigest = "" };
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            page.UpdateLayout();
        }

        void ValidateAspectGridDefaults()
        {
            var ordinary = new Grid(); var styled = new WidgetExplicitGrid();
            foreach (var grid in new Grid[] { ordinary, styled })
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                grid.Children.Add(new Border { Width = 100, Height = 73 });
                grid.Measure(new(640, 360));
            }
            Check(ordinary.DesiredSize == styled.DesiredSize, "Unstyled explicit Grid retains native content measurement");
            styled.SetAspectRatio(16d / 9); styled.Measure(new(640, 360));
            Check(styled.DesiredSize == new Windows.Foundation.Size(640, 360), "Authored aspect ratio measures a finite width-derived viewport");
            styled.SetAspectRatio(null); styled.Measure(new(640, 360));
            Check(ordinary.DesiredSize == styled.DesiredSize, "Removing aspect ratio restores native Grid measurement");
            styled.Height = 100; styled.SetAspectRatio(16d / 9); styled.Measure(new(640, 360));
            Check(styled.DesiredSize.Height == 100, "Explicit Grid height takes precedence over aspect ratio");
        }
    }
}
