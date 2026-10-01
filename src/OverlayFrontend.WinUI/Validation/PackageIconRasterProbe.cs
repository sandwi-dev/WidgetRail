using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Hosting;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Bounded native rasterization experiment; never a production fallback.</summary>
internal static class PackageIconRasterProbe
{
    internal static async Task RunAsync(Panel owner, SvgImageSource source)
    {
        var results = new List<object>();
        foreach (var mode in new[] { "detached", "clipped-parent", "transparent-parent", "offscreen-parent" })
        {
            Canvas? stage = null;
            try
            {
                var image = new Image { Source = source, Width = 64, Height = 64, Stretch = Stretch.Uniform };
                if (mode == "detached") image.XamlRoot = owner.XamlRoot;
                else
                {
                    stage = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
                    if (mode == "clipped-parent") stage.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) };
                    else if (mode == "transparent-parent") stage.Opacity = 0;
                    else stage.RenderTransform = new TranslateTransform { X = -4096, Y = -4096 };
                    stage.Children.Add(image); owner.Children.Add(stage);
                    await Task.Delay(80);
                }
                image.Measure(new Size(64, 64)); image.Arrange(new Rect(0, 0, 64, 64));
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(image, 64, 64).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                var bytes = (await bitmap.GetPixelsAsync()).ToArray();
                if (mode == "offscreen-parent" && bytes.Length != 0)
                    await ShowMaskAsync(owner, bytes, bitmap.PixelWidth, bitmap.PixelHeight);
                results.Add(new { mode, width = bitmap.PixelWidth, height = bitmap.PixelHeight,
                    nontransparent = Enumerable.Range(0, bytes.Length / 4).Count(i => bytes[i * 4 + 3] != 0), result = "completed" });
            }
            catch (Exception error) { results.Add(new { mode, result = "failed", error = error.Message }); }
            finally { if (stage is not null) owner.Children.Remove(stage); }
        }
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "package-icon-raster-probe.json"), JsonSerializer.Serialize(results));
    }
    private static async Task ShowMaskAsync(Panel owner, byte[] bytes, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, bytes);
        await encoder.FlushAsync(); stream.Seek(0);
        var loaded = LoadedImageSurface.StartLoadFromStream(stream, new Size(width, height));
        var ready = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        loaded.LoadCompleted += (_, args) => ready.TrySetResult(args.Status);
        if (await ready.Task.WaitAsync(TimeSpan.FromSeconds(3)) != LoadedImageSourceLoadStatus.Success) throw new InvalidDataException("Native PNG surface decode failed");
        var icon = new ImageIcon { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        owner.Children.Add(icon);
        var compositor = ElementCompositionPreview.GetElementVisual(icon).Compositor;
        var mask = compositor.CreateMaskBrush(); mask.Mask = compositor.CreateSurfaceBrush(loaded);
        mask.Source = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 40, 240, 80));
        var sprite = compositor.CreateSpriteVisual(); sprite.Size = new(64, 64); sprite.Brush = mask;
        ElementCompositionPreview.SetElementChildVisual(icon, sprite);
        await Task.Delay(200);
    }
}
