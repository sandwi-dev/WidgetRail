using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

/// <summary>Generic image rendition adapter; original library files are never rewritten.</summary>
internal static class WindowsCaptureImageCodec
{
    internal static async Task<CaptureImageDimensions> CreateContextAsync(string inputPath, string outputPath, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(inputPath).AsTask(token).ConfigureAwait(false);
        using var input = await file.OpenReadAsync().AsTask(token).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token).ConfigureAwait(false);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (long)decoder.PixelWidth * decoder.PixelHeight > WidgetProtocol.CaptureLimits.MaximumPixels)
            throw new BrokerException("capture_library_invalid_media", "Image dimensions exceed the supported limit.");
        var scale = Math.Min(1, Math.Min(1920d / decoder.PixelWidth, 1080d / decoder.PixelHeight));
        var width = Math.Max(1u, (uint)(decoder.PixelWidth * scale));
        var height = Math.Max(1u, (uint)(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
        using var encoded = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encoded).AsTask(token).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        if (encoded.Size > WidgetProtocol.CaptureLimits.MaximumContextBytes) throw new BrokerException("capture_library_invalid_media", "Context image is too large.");
        encoded.Seek(0);
        using var stream = encoded.AsStreamForRead();
        await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await stream.CopyToAsync(output, token).ConfigureAwait(false);
        return new((int)width, (int)height);
    }
}
