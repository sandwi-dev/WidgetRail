using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage
{
    private async Task OriginalColorSizingAsync()
    {
        var host = (StackPanel)Content;
        using var view = new WidgetPackageIconView { Width = 117, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(view);
        var demands = 0;
        Task<WidgetPresentationPackageIcon> Resolve(string id, CancellationToken token)
        {
            demands++;
            var bounds = id == "wide" ? "120 30" : "30 120";
            var dimensions = id == "wide" ? "width=\"120\" height=\"30\"" : "width=\"30\" height=\"120\"";
            var svg = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {bounds}\"><rect {dimensions} fill=\"#e73932\"/></svg>");
            return Task.FromResult(new WidgetPresentationPackageIcon(id, id, svg));
        }
        void Update(string id) => view.Update(new() { Id = "rectangular-icon", Kind = ViewNodeKind.Icon,
            Glyph = WidgetGlyph.Music, PackageIcon = new(id, WidgetPackageIconColorMode.OriginalColor) }, Resolve, "rectangular", diagnostics.Add);
        try
        {
            Update("wide");
            try
            {
                await Until(() => view.Content is Image { Source: SvgImageSource } && Math.Abs(view.ActualWidth - 117) <= 1);
            }
            catch (TimeoutException error)
            {
                throw new TimeoutException($"Wide icon did not settle: content={view.Content?.GetType().Name ?? "null"}, " +
                    $"actual={view.ActualWidth}x{view.ActualHeight}, dpi={view.XamlRoot?.RasterizationScale}, " +
                    $"loaded={view.IsLoaded}, demands={demands}, diagnostics={string.Join(",", diagnostics.TakeLast(4))}", error);
            }
            view.UpdateLayout();
            var image = (Image)view.Content;
            var source = (SvgImageSource)image.Source;
            Check(Math.Abs(source.RasterizePixelWidth / source.RasterizePixelHeight - 4) < .01,
                "wide original-color SVG uses a rectangular raster preserving the admitted aspect ratio");
            await CheckRedBoundsAsync(view, .95, .85, "wide logo visibly fills its authored rectangular slot instead of a small square");
            view.Width = 234; view.Height = 64;
            await Until(() => Math.Abs(view.ActualWidth - 234) <= 1);
            await CheckRedBoundsAsync(view, .95, .85, "resized wide logo preserves aspect and fills the larger slot");
            Check(ReferenceEquals(image, view.Content) && demands == 1 &&
                source.RasterizePixelWidth <= ProtocolConstants.MaximumPackageIconRasterDimension &&
                source.RasterizePixelHeight <= ProtocolConstants.MaximumPackageIconRasterDimension,
                "rectangular logo resizing retains admitted demand and decoded source within raster bounds");
            view.ClearValue(FrameworkElement.WidthProperty); view.ClearValue(FrameworkElement.HeightProperty);
            await Until(() => view.ActualWidth <= view.FontSize + 1 && view.ActualHeight <= view.FontSize + 1);
            Check(Math.Abs(view.ActualHeight - view.ActualWidth / 4) <= 1,
                "removing wide logo dimensions restores intrinsic size without retaining the arranged slot");
            view.Width = 32; view.Height = 128; Update("tall");
            await Until(() => view.Content is Image next && !ReferenceEquals(next, image) && Math.Abs(view.ActualHeight - 128) <= 1);
            await CheckRedBoundsAsync(view, .95, .95, "portrait original-color SVG fills a tall slot without distortion");
            Check(demands == 2 && view.Content is Image { Source: SvgImageSource },
                "rectangular original-color icons keep the native SVG path separate from square tint masks");
        }
        finally { host.Children.Remove(view); }
        await OriginalColorRasterDensityAsync();
    }

    private async Task OriginalColorRasterDensityAsync()
    {
        var host = (StackPanel)Content;
        using var view = new WidgetPackageIconView { Width = 160, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        var zoom = new Shell.OverlayScaleRoot { Width = 220, Height = 60, HorizontalAlignment = HorizontalAlignment.Left };
        zoom.Children.Add(view); host.Children.Add(zoom);
        var demands = 0;
        var stripes = string.Concat(Enumerable.Range(0, 80).Select(index => $"<rect x=\"{index * 2}\" width=\"1\" height=\"40\" fill=\"#ffffff\"/>"));
        var bytes = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 160 40\"><rect width=\"160\" height=\"40\" fill=\"#000000\"/>{stripes}</svg>");
        try
        {
            view.Update(new() { Id = "fine-logo", Kind = ViewNodeKind.Icon, Glyph = WidgetGlyph.Music,
                PackageIcon = new("fine-logo", WidgetPackageIconColorMode.OriginalColor) }, (_, _) =>
                { demands++; return Task.FromResult(new WidgetPresentationPackageIcon("fine-logo", "fine-logo", bytes)); }, "density", diagnostics.Add);
            await Until(() => view.Content is Image { Source: SvgImageSource } && Math.Abs(view.ActualWidth - 160) < 1);
            var image = (Image)view.Content;
            SvgImageSource Source() => (SvgImageSource)image.Source;
            await Until(() => Source().RasterizePixelWidth >= view.ActualWidth * view.XamlRoot.RasterizationScale);
            await Until(() => image.IsLoaded && image.ActualWidth > 0);
            view.UpdateLayout();
            var unit = new Windows.Foundation.Rect(0, 0, 1, 1);
            var transformed = image.RenderTransform?.TransformBounds(unit) ?? unit;
            // WinUI may expose its default identity transform as an object even
            // after clearing RenderTransform; inspect its effect, not nullness.
            Check(Math.Abs(transformed.Width - 1) < .001 && Math.Abs(transformed.Height - 1) < .001 &&
                Math.Abs(image.ActualWidth - view.ActualWidth) <= 1 && Math.Abs(image.ActualHeight - view.ActualHeight) <= 1,
                $"original-color SVG is arranged at paint size rather than magnifying a font-sized icon surface " +
                $"(image={image.ActualWidth:F2}x{image.ActualHeight:F2}, owner={view.ActualWidth:F2}x{view.ActualHeight:F2}, " +
                $"transform={image.RenderTransform?.GetType().Name ?? "null"}:{transformed.Width:F3}x{transformed.Height:F3}, " +
                $"raster={Source().RasterizePixelWidth:F2}x{Source().RasterizePixelHeight:F2}, dpi={view.XamlRoot.RasterizationScale:F2})");
            zoom.InterfaceScale = WidgetRail.PlatformSettings.AppearanceSettings.MaximumInterfaceScale;
            var physicalWidth = view.ActualWidth * view.XamlRoot.RasterizationScale * zoom.InterfaceScale;
            await Until(() => Source().RasterizePixelWidth >= Math.Min(512, physicalWidth));
            Check(true, "unchanged layout size upgrades SVG raster density for interface zoom and current monitor DPI");
            var expectedWidth = (int)Math.Ceiling(physicalWidth);
            var expectedHeight = (int)Math.Ceiling(view.ActualHeight * view.XamlRoot.RasterizationScale * zoom.InterfaceScale);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer()); stream.Seek(0);
            var referenceSource = new SvgImageSource { RasterizePixelWidth = expectedWidth, RasterizePixelHeight = expectedHeight };
            Check(await referenceSource.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success, "fine-detail SVG reference decodes at target physical resolution");
            var reference = new Image { Source = referenceSource, Width = view.Width, Height = view.Height, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(reference);
            try
            {
                await Until(() => reference.ActualWidth > 0);
                await Task.Delay(150);
                var actualContrast = await EdgeContrastAsync(view, expectedWidth, expectedHeight);
                var referenceContrast = await EdgeContrastAsync(reference, expectedWidth, expectedHeight);
                Check(referenceContrast > 40 && actualContrast >= referenceContrast * .75,
                    $"SVG fine-detail edge contrast tracks direct native rendering at physical resolution ({actualContrast:F1}/{referenceContrast:F1})");
            }
            finally { host.Children.Remove(reference); }
            view.Width = 320; view.Height = 80; zoom.Width = 400; zoom.Height = 100;
            await Until(() => Math.Abs(view.ActualWidth - 320) < 1 && Source().RasterizePixelWidth >=
                Math.Min(512, view.ActualWidth * view.XamlRoot.RasterizationScale * zoom.InterfaceScale));
            var retainedSource = Source();
            var retained = retainedSource.RasterizePixelWidth;
            zoom.InterfaceScale = 1; view.Width = 117; view.Height = 32;
            await Until(() => Math.Abs(view.ActualWidth - 117) < 1);
            await Task.Delay(50);
            Check(ReferenceEquals(view.Content, image) && ReferenceEquals(image.Source, retainedSource) && demands == 1 &&
                Source().RasterizePixelWidth == retained && retained <= ProtocolConstants.MaximumPackageIconRasterDimension,
                "width and zoom changes reuse one admitted SVG payload and retain its bounded high-water raster");
        }
        finally { host.Children.Remove(zoom); }
    }

    private static async Task<double> EdgeContrastAsync(FrameworkElement target, int width, int height)
    {
        var raster = new RenderTargetBitmap(); await raster.RenderAsync(target, width, height);
        var pixels = (await raster.GetPixelsAsync()).ToArray();
        var row = raster.PixelHeight / 2;
        var sum = 0d;
        for (var x = 2; x < raster.PixelWidth - 2; x++)
            sum += Math.Abs(pixels[(row * raster.PixelWidth + x) * 4 + 2] - pixels[(row * raster.PixelWidth + x - 1) * 4 + 2]);
        return sum / Math.Max(1, raster.PixelWidth - 4);
    }

    private async Task CheckRedBoundsAsync(FrameworkElement target, double minimumWidth, double minimumHeight, string name)
    {
        // Assert painted pixels, not only layout bounds: a square ImageIcon can
        // have a wide owner while still painting a tiny wordmark in its center.
        await Task.Delay(100);
        var raster = new RenderTargetBitmap();
        await raster.RenderAsync(target);
        var pixels = (await raster.GetPixelsAsync()).ToArray();
        var left = raster.PixelWidth; var top = raster.PixelHeight; var right = -1; var bottom = -1;
        for (var y = 0; y < raster.PixelHeight; y++)
            for (var x = 0; x < raster.PixelWidth; x++)
            {
                var offset = (y * raster.PixelWidth + x) * 4;
                if (pixels[offset + 2] < 180 || pixels[offset + 1] > 90 || pixels[offset] > 90) continue;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        Check(right - left + 1 >= raster.PixelWidth * minimumWidth && bottom - top + 1 >= raster.PixelHeight * minimumHeight, name);
    }
}
