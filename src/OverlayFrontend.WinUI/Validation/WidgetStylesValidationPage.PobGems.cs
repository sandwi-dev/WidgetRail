using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PobGemInspectionAsync(string directory)
    {
        Exception? failure = null;
        var root = new Grid();
        var page = new WidgetViewPresenter();
        root.Children.Add(page); PresentationLayers.Attach(root);
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popupTheme = new NativePopupTheme(); popupTheme.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var cssPath = Path.Combine(directory, "pob.wrss");
        var package = WrssParser.Parse(File.ReadAllText(cssPath), "pob.wrss");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1480 * scale), (int)(1100 * scale)));
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            foreach (var game in new[] { 1, 2 })
            foreach (var size in new[] { (Width: 1320d, Height: 900d), (Width: 820d, Height: 600d) })
            {
                root.Width = page.Width = size.Width; root.Height = page.Height = size.Height;
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
                var picker = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, $"poe{game}-gem-picker.json")));
                var inspection = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, $"poe{game}-gem-inspection.json")));
                var row = JsonSerializer.Deserialize<ViewNode>(File.ReadAllBytes(Path.Combine(directory, $"poe{game}-gem-row.json")))!;
                // Use the real demanded row/content and styles. Worker/lease routing
                // is qualified separately; this fixture measures native presentation.
                ViewNode Materialize(ViewNode node) => node.Kind == ViewNodeKind.IndexedCollection
                    ? new ViewNode { Id = node.Id, Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                        StyleClasses = node.StyleClasses, Children = (ViewNode[])[row] }
                    : node with { Children = node.Children.Select(Materialize).ToArray() };
                picker = picker with { Root = Materialize(picker.Root), InitialFocusId = row.Id, FocusGroupEntryRequest = null };
                inspection = inspection with { Root = Materialize(inspection.Root) with
                    { AnchoredPresentation = inspection.Root.AnchoredPresentation! with { Anchor = new(ElementId: row.Id) } } };
                var open = false;
                var chosen = 0;
                page.DispatchActionAsync = request =>
                {
                    if (request.Action.ActionId.StartsWith("gem-inspection-close.", StringComparison.Ordinal)) { open = false; Publish(); }
                    else if (request.Action.ActionId == "choose") ++chosen;
                    return Task.CompletedTask;
                };
                Publish();
                await Wait(() => Find<Button>("Widget." + row.Id, root) is { IsLoaded: true });
                var opener = Find<Button>("Widget." + row.Id, root)!;
                opener.Focus(FocusState.Keyboard);
                await Wait(() => page.IsFocusPreviewOpen);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener), "Gem passive preview retains the native row focus");
                Check(chosen == 0, "Gem preview does not choose a candidate");
                open = true; Publish();
                await Wait(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused) == "Widget." + inspection.InitialFocusId);
                var panel = Find<FrameworkElement>("Widget.pob.gem-inspection", root)!;
                await Wait(() => panel.ActualWidth > 0 && panel.ActualHeight > 0);
                var bounds = panel.TransformToVisual(page).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight));
                Check(bounds.Left >= 15 && bounds.Top >= 15 && bounds.Right <= page.ActualWidth - 15 && bounds.Bottom <= page.ActualHeight - 15,
                    $"PoE{game} inspection fits {size.Width}x{size.Height} in {themeId}");
                var scrolls = Descendants(panel).OfType<ScrollViewer>().ToArray();
                Check(scrolls.All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1), "Inspection has no horizontal content overflow");
                var apply = Descendants(panel).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button).Contains("gem-inspection-apply.", StringComparison.Ordinal));
                apply.StartBringIntoView();
                await Wait(() => scrolls.All(scroll => scroll.ScrollableHeight <= 0 || WidgetReadingScrollFocus.IsVisible(apply, scroll)));
                Check(apply.IsEnabled && apply.Focus(FocusState.Keyboard), "Explicit Add/Replace remains reachable after scrolling");
                if (size.Width == 1320)
                    await Capture(Path.Combine(directory, $"native-poe{game}-{themeId}.png"));
                Find<Button>("Widget." + inspection.InitialFocusId, root)!.Focus(FocusState.Keyboard);
                page.ActivateFocused();
                await Wait(() => !open && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener));
                Check(chosen == 0, "Closing returns to the same native candidate without choosing it");
                page.ActivateFocused();
                Check(chosen == 1, "A still chooses the focused gem after returning from inspection");
                void Publish()
                {
                    // The exporter uses its SDK test host identity for the picker
                    // and direct widget identity for inspection. They are one live
                    // widget in this native presentation fixture.
                    var snapshot = (open ? inspection : picker) with { Sequence = ++sequence, WidgetInstanceId = "pob-gem-fixture" };
                    var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                    void Visit(ViewNode node)
                    {
                        styles[node.Id] = Compute(theme, node);
                        foreach (var child in node.Children) Visit(child);
                        if (node.FocusPresentation is { } fragment) Visit(fragment);
                        if (node.DefaultFocusPresentation is { } fallback) Visit(fallback);
                    }
                    Visit(snapshot.Root);
                    foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
                    popupTheme.Update(styles, AppearanceSettings.Default);
                    var descriptor = new BridgeWidgetDescriptor { Id = "pob", Name = "PoB fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = "pob-gem", PresentationGeneration = "pob-gem", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
                    page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    page.UpdateLayout();
                }
            }
        }
        catch (Exception error) { failure = error; await Capture(Path.Combine(directory, "native-failure.png")); }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
        async Task Capture(string path)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var output = File.Create(path); await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
