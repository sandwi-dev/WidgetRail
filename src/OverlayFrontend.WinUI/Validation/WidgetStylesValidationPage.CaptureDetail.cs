using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // Geometry uses the production saved/unsaved snapshots. It does not resolve
    // the synthetic capture token or claim to validate media playback.
    private async Task CaptureDetailGeometryAsync(string directory, Grid root, WidgetViewPresenter page,
        ThemeCatalog catalog, WrssDocument package)
    {
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
            new((WrssDocument[])[package], []), catalog.Load(ThemeIdentity.BuiltInMonoInk, "1.0.0").Package).Theme!;
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.ResizeClient(new((int)(1240 * scale), (int)(800 * scale)));
        foreach (var state in new[] { "unsaved", "saved" })
        foreach (var narrow in new[] { false, true })
        {
            root.Width = page.Width = narrow ? 560 : 1180;
            root.Height = page.Height = 700;
            page.ApplyAppearance(AppearanceSettings.Default with { TextScale = narrow ? 1.5 : 1, Motion = MotionPreference.Reduced }, false);
            var snapshot = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, "detail-" + state + ".json"))) with { Sequence = ++sequence };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child); }
            Visit(snapshot.Root);
            foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
            var descriptor = new BridgeWidgetDescriptor { Id = "capture-detail", Name = "Capture detail fixture", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "capture-detail", PresentationGeneration = "capture-detail", Icon = WidgetGlyph.Fullscreen, PackageContentDigest = "" };
            page.DispatchActionAsync = _ => Task.CompletedTask;
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId ?? snapshot.Root.Id), descriptor, snapshot, styles));
            await Wait(() => Find<FrameworkElement>("Widget.capture-library.detail.info", root) is { IsLoaded: true, ActualWidth: > 0 });
            await Task.Delay(100);
            page.UpdateLayout();
            var layout = Find<FrameworkElement>("Widget.capture-library.detail.layout", root)!;
            var media = Find<FrameworkElement>("Widget.capture-library.preview", root)!;
            var info = Find<FrameworkElement>("Widget.capture-library.detail.info", root)!;
            var scroll = Find<ScrollViewer>("Widget.capture-library.detail.scroll", root)!;
            var mediaBounds = media.TransformToVisual(layout).TransformBounds(new(0, 0, media.ActualWidth, media.ActualHeight));
            var infoBounds = info.TransformToVisual(layout).TransformBounds(new(0, 0, info.ActualWidth, info.ActualHeight));
            Check(media.ActualHeight >= 400, $"{state}: detail media retains a substantial viewer height");
            Check(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"{state}: {(narrow ? "narrow large-text" : "wide")} detail has no horizontal overflow");
            if (narrow)
                Check(infoBounds.Top >= mediaBounds.Bottom + 8 && mediaBounds.Width >= layout.ActualWidth * .9,
                    $"{state}: narrow detail stacks information below full-width media");
            else
                Check(mediaBounds.Width >= layout.ActualWidth * .45 && mediaBounds.Width <= layout.ActualWidth * .55 && infoBounds.Left >= mediaBounds.Right + 8,
                    $"{state}: wide detail gives media half the page beside information");
            var actionId = state == "saved" ? "capture-library.back" : "capture-library.save";
            var action = Find<Button>("Widget." + actionId, root)!;
            var contentViewport = ScrollParts(scroll).OfType<ScrollContentPresenter>().First();
            Check(action.Focus(FocusState.Keyboard), $"{state}: detail action remains keyboard reachable");
            action.StartBringIntoView();
            try { await Wait(() =>
            {
                var bounds = action.TransformToVisual(contentViewport).TransformBounds(new(0, 0, action.ActualWidth, action.ActualHeight));
                return bounds.Top >= -1 && bounds.Bottom <= contentViewport.ActualHeight + 1;
            }); }
            catch (TimeoutException error)
            {
                var bounds = action.TransformToVisual(contentViewport).TransformBounds(new(0, 0, action.ActualWidth, action.ActualHeight));
                throw new InvalidOperationException($"Detail reveal did not settle: {state}, narrow={narrow}, bounds={bounds}, viewport={contentViewport.ActualHeight}, extent={scroll.ExtentHeight}, offset={scroll.VerticalOffset}", error);
            }
            var actionBounds = action.TransformToVisual(contentViewport).TransformBounds(new(0, 0, action.ActualWidth, action.ActualHeight));
            Check(actionBounds.Top >= -1 && actionBounds.Bottom <= contentViewport.ActualHeight + 1,
                $"{state}: detail scroll reveals the focused action at {(narrow ? "large" : "normal")} text");
            scroll.ChangeView(null, 0, null, true); await Task.Delay(100);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var output = File.Create(Path.Combine(directory, $"native-detail-{state}-{(narrow ? "narrow" : "wide")}.png"));
            await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
