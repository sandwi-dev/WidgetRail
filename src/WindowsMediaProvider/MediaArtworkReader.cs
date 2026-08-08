using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameBarAlternative.WindowsMediaProvider;

/// <summary>
/// Converts an untrusted GSMTC thumbnail into the bounded inline-PNG profile
/// understood by the declarative renderer. Source bytes, source dimensions,
/// decoded pixels, output bytes, and elapsed work are all bounded.
/// </summary>
internal static class MediaArtworkReader
{
    internal const int MaximumSourceBytes = 1024 * 1024;
    internal const uint MaximumSourceDimension = 4_096;
    internal const uint MaximumOutputDimension = 48;
    internal const int MaximumOutputBytes = 12 * 1024;
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(180);

    internal static async Task<string?> TryReadAsync(
        IRandomAccessStreamReference? reference,
        CancellationToken cancellationToken)
    {
        if (reference is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        try
        {
            using var source = await reference.OpenReadAsync().AsTask(timeout.Token)
                .ConfigureAwait(false);
            if (source.Size is 0 or > MaximumSourceBytes) return null;

            var decoder = await BitmapDecoder.CreateAsync(source).AsTask(timeout.Token)
                .ConfigureAwait(false);
            if (decoder.PixelWidth is 0 or > MaximumSourceDimension ||
                decoder.PixelHeight is 0 or > MaximumSourceDimension)
                return null;

            var scale = Math.Min(
                (double)MaximumOutputDimension / decoder.PixelWidth,
                (double)MaximumOutputDimension / decoder.PixelHeight);
            scale = Math.Min(1, scale);
            var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
            var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
            var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height };
            var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(timeout.Token).ConfigureAwait(false);
            var bytes = pixels.DetachPixelData();
            if (bytes.Length != checked((int)(width * height * 4))) return null;

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output)
                .AsTask(timeout.Token).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
                width, height, 96, 96, bytes);
            await encoder.FlushAsync().AsTask(timeout.Token).ConfigureAwait(false);
            if (output.Size is 0 or > MaximumOutputBytes) return null;

            output.Seek(0);
            using var reader = new DataReader(output.GetInputStreamAt(0));
            var loaded = await reader.LoadAsync((uint)output.Size).AsTask(timeout.Token)
                .ConfigureAwait(false);
            if (loaded != output.Size) return null;
            var png = new byte[loaded];
            reader.ReadBytes(png);
            return Convert.ToBase64String(png);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Missing, malformed, disappearing, or unsupported artwork must not
            // discard the otherwise valid media session.
            return null;
        }
    }
}
