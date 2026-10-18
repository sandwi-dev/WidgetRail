using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task FocusPreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1080 * scale), (int)(850 * scale)));
        Exception? failure = null;
        object? failureState = null;
        var root = new Grid { Width = 920, Height = 580 };
        var page = new WidgetViewPresenter { Width = 900, Height = 560 };
        var background = new Border(); root.Children.Add(background);
        root.Children.Add(page); PresentationLayers.Attach(root);
        presenter.Visibility = Visibility.Collapsed;
        host.Children.Add(root);
        var popupTheme = new NativePopupTheme(); popupTheme.Attach(page);
        var dispatched = 0;
        page.DispatchActionAsync = _ => { ++dispatched; return Task.CompletedTask; };
        page.Failed = error => failure ??= error;
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            {
                var package = WrssParser.Parse(".preview-page { padding: 16px; background: var(--surface); } text { color: var(--text); } .preview-details { gap: 8px; }", "preview-fixture.wrss");
                var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package);
                var theme = compiled.Theme ?? throw new InvalidOperationException("Invalid preview fixture theme");
                var options = new FocusPreviewOptions(360, 220, DelayMilliseconds: 180);
                var caption = "Adjust this setting without leaving your current page.";
                var busy = false;
                var showFirst = true;
                var secondPreview = true;
                Publish();
                await Wait(() => Find<Button>("Widget.preview.first", root) is { IsLoaded: true });
                var first = Find<Button>("Widget.preview.first", root)!;
                var second = Find<Button>("Widget.preview.second", root)!;
                first.Focus(FocusState.Keyboard);
                await Task.Delay(45);
                Check(!page.IsFocusPreviewOpen, "Preview waits for selection to settle");
                for (var index = 0; index < 8; index++)
                {
                    caption = "Progress update " + index; Publish(); await Task.Delay(30);
                }
                await Wait(() => page.IsFocusPreviewOpen);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), "Passive preview never steals focus");
                Check(dispatched == 0, "Settling does not dispatch widget actions");
                Check(page.FocusPreviewChrome is { IsHitTestVisible: false }, "Passive preview cannot intercept clicks");
                Check(Inside(), "Preview stays within widget bounds");
                var portal = root.Children.OfType<PresentationPortal>().Single();
                Check(Canvas.GetZIndex(portal) == (int)PresentationLayer.ContentPreview, "Preview uses a band below dialogs");
                busy = true; Publish(); await Task.Delay(30);
                Check(page.IsFocusPreviewOpen && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), "Busy state preserves preview and focus");
                busy = false;
                caption = "Widget-authored details reuse ordinary text, layout and progress controls."; Publish();
                await Wait(() => Find<TextBlock>("Widget.preview.first.details", root)?.Text == caption);
                var textInk = ColorOf(Find<TextBlock>("Widget.preview.first.details", root)!.Foreground);
                var panelInk = ColorOf(((Border)page.FocusPreviewChrome!).Background);
                var contrast = (Math.Max(Luminance(textInk), Luminance(panelInk)) + .05) /
                    (Math.Min(Luminance(textInk), Luminance(panelInk)) + .05);
                Check(contrast >= 4.5, "Preview text contrasts with native chrome in " + themeId);
                await Capture(Path.Combine(directory, themeId + ".png"));
                page.SetPreviewPointerTarget(second);
                // Lightweight WinUI TextBlocks can be laid out without IsLoaded;
                // verify their current pixels and presentation ownership instead.
                await Wait(() => page.IsFocusPreviewOpen && Find<TextBlock>("Widget.preview.second.details", root) is
                    { Text: "Video preview", ActualWidth: > 0, ActualHeight: > 0, XamlRoot: not null });
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first) && dispatched == 0, "Hover shows another source without selecting or focusing it");
                page.SetPreviewPointerTarget(null); await Task.Delay(30);
                Check(!page.IsFocusPreviewOpen, "Leaving all preview sources dismisses immediately");
                second.Focus(FocusState.Keyboard); await Wait(() => page.IsFocusPreviewOpen);
                page.Width = 420; root.Width = 440; page.UpdateLayout();
                await Wait(Inside);
                Check(Inside(), "Same preview repositions after a narrow resize");
                page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
                page.ResizeWidgetContent(new(380, 520), background);
                Check(!page.IsFocusPreviewOpen, "Compositor resize dismisses preview without a render polling loop");
                if (page.WidgetResizeCompletion is { } motion) await motion;
                await Wait(() => page.IsFocusPreviewOpen && Inside());
                Check(true, "Settled resize restores the current preview within its final bounds");
                page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
                page.SetPresentationInputEnabled(false);
                Check(!page.IsFocusPreviewOpen, "Input suspension synchronously revokes preview");
                page.SetPresentationInputEnabled(true); second.Focus(FocusState.Keyboard); Publish();
                await Wait(() => page.IsFocusPreviewOpen);
                await page.SetPresentationActiveAsync(false);
                Check(!page.IsFocusPreviewOpen, "Widget suspension synchronously revokes preview");
                await page.SetPresentationActiveAsync(true); Publish();
                first.Focus(FocusState.Keyboard); await Task.Delay(30);
                showFirst = false; secondPreview = false; Publish();
                await Task.Delay(240);
                Check(!page.IsFocusPreviewOpen, "Removed source cannot reopen from an old timer");
                page.Width = 900; root.Width = 920;

                void Publish()
                {
                    var firstButton = UI.Button("Audio setting", "preview.inspect.first", "preview.first") with { IsBusy = busy };
                    var secondButton = UI.Button("Video setting", "preview.inspect.second", "preview.second");
                    WidgetElement secondElement = secondPreview ? secondButton.PresentOnFocus(UI.Text("Video preview", "preview.second.details")) : secondButton;
                    var content = UI.Grid("preview.page", (GridTrackDefinition[])[GridTrack.Pixel(50), GridTrack.Pixel(60), GridTrack.Star()],
                        (GridTrackDefinition[])[GridTrack.Pixel(170), GridTrack.Star(), GridTrack.Pixel(170)],
                        UI.Text("Settings preview", "preview.title").InGrid(columnSpan: 3),
                        showFirst ? firstButton.PresentOnFocus(UI.Stack("preview.first.content", UI.Text(caption, "preview.first.details"),
                            UI.Progress(.7, 1, "preview.first.progress")).Classes("preview-details")).InGrid(row: 1) : UI.Spacer("preview.placeholder").InGrid(row: 1),
                        secondElement.InGrid(row: 1, column: 2)).Classes("preview-page");
                    var view = new WidgetView(UI.FocusPresentationSurface(content, UI.Text("Fallback", "preview.default"), "preview.surface")
                        with { Preview = options }, showFirst ? "preview.first" : "preview.second");
                    var snapshot = view.CreateSnapshot("preview.instance", ++sequence);
                    var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                    void Visit(ViewNode node)
                    {
                        styles[node.Id] = Compute(theme, node);
                        foreach (var child in node.Children) Visit(child);
                        if (node.FocusPresentation is { } fragment) Visit(fragment);
                        if (node.DefaultFocusPresentation is { } fallback) Visit(fallback);
                    }
                    Visit(snapshot.Root);
                    // Native chrome consumes the same resolved semantic palette as the fragments.
                    foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" })
                        styles[role] = Compute(theme, role, role, []);
                    popupTheme.Update(styles, AppearanceSettings.Default);
                    var descriptor = new BridgeWidgetDescriptor { Id = "preview", Name = "Preview fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = "preview", PresentationGeneration = "preview", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
                    page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    page.UpdateLayout();
                }
            }
        }
        catch (Exception error) { failure ??= error; failureState = page.FocusPreviewDiagnostics(); await Capture(Path.Combine(directory, "failure.png")); }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, failureState, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        static double Luminance(Windows.UI.Color color)
        {
            static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }

        bool Inside()
        {
            if (page.FocusPreviewChrome is not { ActualWidth: > 0 } chrome) return false;
            var point = chrome.TransformToVisual(page).TransformPoint(new());
            return point.X >= 15 && point.Y >= 15 && point.X + chrome.ActualWidth <= page.ActualWidth - 15 && point.Y + chrome.ActualHeight <= page.ActualHeight - 15;
        }
        async Task Capture(string path)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
        }
    }
}
