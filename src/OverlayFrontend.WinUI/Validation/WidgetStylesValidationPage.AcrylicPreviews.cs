using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AcrylicPreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1080 * scale), (int)(900 * scale)));
        Exception? failure = null;
        presenter.Visibility = Visibility.Collapsed;
        var root = new Grid { Width = 860, Height = 480, Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray) };
        var pattern = new Grid();
        for (var i = 0; i < 20; ++i)
        {
            pattern.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var stripe = new Border { Background = new SolidColorBrush(i % 2 == 0 ? Microsoft.UI.Colors.SlateGray : Microsoft.UI.Colors.DarkSlateBlue) };
            Grid.SetColumn(stripe, i); pattern.Children.Add(stripe);
        }
        root.Children.Add(pattern);
        root.Children.Add(new TextBlock { Text = "CONTENT BEHIND THE PREVIEW", FontSize = 44, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var page = new WidgetViewPresenter { Width = 840, Height = 460 };
        root.Children.Add(page); PresentationLayers.Attach(root); host.Children.Add(root);
        var popup = new NativePopupTheme(); popup.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced, Transparency = TransparencyPreference.Full };
        page.ApplyAppearance(appearance, false);
        page.Failed = error => failure ??= error;
        var dispatches = 0; page.DispatchActionAsync = _ => { dispatches++; return Task.CompletedTask; };
        var themeId = ThemeIdentity.BuiltInCanopy;
        var material = "standard";
        var tint = .65;
        try
        {
            foreach (var id in new[] { ThemeIdentity.BuiltInCanopy, ThemeIdentity.BuiltInMineralLight })
            {
                themeId = id;
                Publish();
                await Wait(() => Find<Button>("Widget.acrylic.source", root) is { IsLoaded: true });
                var source = Find<Button>("Widget.acrylic.source", root)!; source.Focus(FocusState.Keyboard);
                await Wait(() => page.IsFocusPreviewOpen && page.FocusPreviewFrame?.Depth.IsRealized == true &&
                    page.FocusPreviewChrome is { ActualWidth: > 0, ActualHeight: > 0 });
                var frame = page.FocusPreviewFrame!;
                var chrome = frame.Surface;
                var bounds = (chrome.ActualWidth, chrome.ActualHeight);
                Check(chrome.Background is LinearGradientBrush, id + ": standard remains the default material");
                material = "acrylic"; Publish();
                await Wait(() => chrome.Background is AcrylicBrush);
                var brush = (AcrylicBrush)chrome.Background;
                Check(brush.TintOpacity == .65 && brush.Opacity == 1 && brush.FallbackColor.A == 255 && brush.TintColor.A == 255,
                    id + ": acrylic has independent tint strength, opaque fallback and no subtree opacity");
                Check(ReferenceEquals(page.FocusPreviewFrame, frame) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source) &&
                    bounds == (chrome.ActualWidth, chrome.ActualHeight) && dispatches == 0, id + $": material changes preserve source focus and geometry (frame={ReferenceEquals(page.FocusPreviewFrame, frame)}, focus={ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source)}, before={bounds}, after={(chrome.ActualWidth, chrome.ActualHeight)}, actions={dispatches})");
                tint = .42; Publish();
                Check(ReferenceEquals(chrome.Background, brush) && brush.TintOpacity == .42, id + ": tint changes reuse the connected native brush");
                brush.AlwaysUseFallback = true;
                Check(brush.FallbackColor.A == 255, id + ": native forced fallback is opaque");
                brush.AlwaysUseFallback = false;
                appearance = appearance with { Transparency = TransparencyPreference.Reduced }; Publish();
                Check(chrome.Background is LinearGradientBrush solid && solid.GradientStops.All(stop => stop.Color.A == 255),
                    id + ": reduced transparency detaches acrylic and uses an opaque fill");
                appearance = appearance with { Transparency = TransparencyPreference.Full, Contrast = ContrastPreference.High }; Publish();
                Check(chrome.Background is SolidColorBrush { Color.A: 255 } && chrome.BorderThickness == new Thickness(2),
                    id + ": high contrast detaches acrylic and restores the Windows outline");
                appearance = appearance with { Contrast = AppearanceSettings.Default.Contrast }; material = "standard"; tint = .65; Publish();
                Check(chrome.Background is LinearGradientBrush, id + ": switching back releases the acrylic background");
            }
            themeId = ThemeIdentity.BuiltInCanopy; material = "acrylic"; Publish();
            var visibleFrame = page.FocusPreviewFrame!;
            await page.SetPresentationActiveAsync(false);
            Check(!page.IsFocusPreviewOpen && visibleFrame.Surface.Background is null, "Hiding preview disconnects its material brush");
            await page.SetPresentationActiveAsync(true); Publish();
            Find<Button>("Widget.acrylic.source", root)!.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen && page.FocusPreviewChrome is Border { Background: AcrylicBrush });
            Check(dispatches == 0, "Reopening acrylic never invokes a widget action");
            // Leave one owned specimen available for compositor-level screenshot comparison.
            page.DispatchActionAsync = _ =>
            {
                if (page.FocusPreviewChrome is Border { Background: AcrylicBrush current })
                {
                    current.AlwaysUseFallback = !current.AlwaysUseFallback;
                    File.WriteAllText(Path.Combine(directory, "fallback.json"), JsonSerializer.Serialize(new { fallback = current.AlwaysUseFallback }));
                }
                return Task.CompletedTask;
            };
        }
        catch (Exception error) { failure ??= error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new {
            passed = failure is null, checks, error = failure?.ToString(), advancedEffectsEnabled = new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled,
            scope = "Native XAML backdrop specimen; no desktop/game capture and no external network content." }));
        if (failure is not null) { await page.DisposeAsync(); throw failure; }
        Unloaded += async (_, _) => await page.DisposeAsync();

        void Publish()
        {
            page.ApplyAppearance(appearance, false);
            var package = WrssParser.Parse("preview { background-material: " + material + "; background-tint-opacity: " +
                tint.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; } .acrylic-content { gap: 12px; } text { color: var(--text); }", "acrylic.wrss");
            var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
            var source = UI.Button("Preview source", "source", "acrylic.source").PresentOnFocus(
                UI.Stack("acrylic.content", UI.Text("Rich preview", "acrylic.title"),
                    UI.Text("Text stays sharp while content behind the panel is softened.", "acrylic.description"),
                    UI.Text("Native in-app acrylic", "acrylic.caption")).Classes("acrylic-content"));
            var content = UI.Grid("acrylic.page", (GridTrackDefinition[])[GridTrack.Pixel(80), GridTrack.Pixel(60), GridTrack.Star()],
                (GridTrackDefinition[])[GridTrack.Pixel(170), GridTrack.Star()], source.InGrid(row: 1));
            var view = new WidgetView(UI.FocusPresentationSurface(content, UI.Text("", "acrylic.empty"), "acrylic.preview")
                with { Preview = new(400, 220, DelayMilliseconds: 0) }, "acrylic.source");
            var snapshot = view.CreateSnapshot("acrylic.instance", ++sequence);
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node)
            {
                styles[node.Id] = Compute(theme, node);
                foreach (var child in node.Children) Visit(child);
                if (node.FocusPresentation is { } fragment) Visit(fragment);
                if (node.DefaultFocusPresentation is { } fallback) Visit(fallback);
            }
            Visit(snapshot.Root);
            foreach (var role in new[] { "body", "canvas", "panel", "preview", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
            popup.Update(styles, appearance);
            var descriptor = new BridgeWidgetDescriptor { Id = "acrylic", Name = "Acrylic fixture", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "acrylic", PresentationGeneration = "acrylic", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            page.UpdateLayout();
        }
    }
}
