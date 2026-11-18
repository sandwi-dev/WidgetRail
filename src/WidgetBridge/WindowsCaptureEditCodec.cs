using System.Buffers.Binary;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

/// <summary>Trusted Windows-only edit encoder. Reads the retained original; writes only the store-owned bounded output.</summary>
internal static class WindowsCaptureEditCodec
{
    /// <summary>Admit format semantics before showing edit controls. Does not encode or save anything.</summary>
    internal static async Task ValidateSourceAsync(CaptureEditInput source, CancellationToken token)
    {
        if (!source.Media.IsWellFormed()) throw new BrokerException("capture_library_invalid_media", "Invalid editor source.");
        if (source.Media.ContentType == "video/mp4") { _ = await InspectVideoAsync(source, token).ConfigureAwait(false); return; }
        DemandImage(source.Stream); using var borrowed = new BorrowedInput(source.Stream); using var input = borrowed.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token).ConfigureAwait(false);
        if (decoder.FrameCount != 1 || decoder.PixelWidth != source.Media.Width || decoder.PixelHeight != source.Media.Height) throw Unsupported("image");
    }
    /// <summary>Host-owned editor preview, not the opaque thumbnail/context rendition. Original coordinates remain authoritative.</summary>
    internal static async Task<CaptureImageDimensions> CreateImagePreviewAsync(CaptureEditInput source, Stream output, CancellationToken token)
    {
        if (source.Media.ContentType != "image/png") throw Unsupported("image");
        DemandImage(source.Stream);
        using var borrowed = new BorrowedInput(source.Stream); using var input = borrowed.AsRandomAccessStream();
        using var bounded = new CaptureEditOutput(output, WidgetRail.WidgetProtocol.CaptureLimits.MaximumContextBytes);
        using var destination = bounded.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token).ConfigureAwait(false);
        if (decoder.FrameCount != 1 || decoder.PixelWidth != source.Media.Width || decoder.PixelHeight != source.Media.Height) throw Unsupported("image");
        var scale = Math.Min(1, Math.Min(1920d / decoder.PixelWidth, 1080d / decoder.PixelHeight));
        var width = Math.Max(1u, (uint)(decoder.PixelWidth * scale)); var height = Math.Max(1u, (uint)(decoder.PixelHeight * scale));
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(token).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        return new((int)width, (int)height);
    }
    internal static async Task EncodeAsync(CaptureEditInput source, CaptureEditRecipe recipe, Stream output,
        IProgress<double>? progress, CancellationToken token)
    {
        if (!recipe.IsValid(source.Media)) throw new BrokerException("capture_library_edit_invalid_recipe", "The edit is outside the source bounds.");
        using var borrowed = new BorrowedInput(source.Stream);
        using var input = borrowed.AsRandomAccessStream();
        using var destination = output.AsRandomAccessStream();
        if (recipe.Image is { } image)
        {
            DemandImage(source.Stream); input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token).ConfigureAwait(false);
            if (decoder.FrameCount != 1 || decoder.PixelWidth != source.Media.Width || decoder.PixelHeight != source.Media.Height) throw Unsupported("image");
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform { Rotation = (BitmapRotation)image.QuarterTurns, Bounds = new() { X = (uint)image.X, Y = (uint)image.Y, Width = (uint)image.Width, Height = (uint)image.Height } },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(token).ConfigureAwait(false);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(token).ConfigureAwait(false); progress?.Report(1); return;
        }
        var trim = recipe.Video!;
        var profile = await InspectVideoAsync(source, token).ConfigureAwait(false); input.Seek(0);
        var transcoder = new MediaTranscoder { AlwaysReencode = true, HardwareAccelerationEnabled = true,
            TrimStartTime = TimeSpan.FromTicks(trim.StartTicks), TrimStopTime = TimeSpan.FromTicks(trim.EndTicks) };
        var prepared = await transcoder.PrepareStreamTranscodeAsync(input, destination, profile).AsTask(token).ConfigureAwait(false);
        if (!prepared.CanTranscode) throw Unsupported("video");
        await prepared.TranscodeAsync().AsTask(token, progress is null ? null : new Progress<double>(value => progress.Report(Math.Clamp(value / 100, 0, 1)))).ConfigureAwait(false);
    }
    private static async Task<MediaEncodingProfile> InspectVideoAsync(CaptureEditInput source, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(source.Path).AsTask(token).ConfigureAwait(false);
        using (var media = MediaSource.CreateFromStorageFile(file))
        {
            var playback = new MediaPlaybackItem(media);
            await media.OpenAsync().AsTask(token).ConfigureAwait(false);
            if (playback.VideoTracks.Count != 1 || playback.AudioTracks.Count > 1 || playback.TimedMetadataTracks.Count != 0) throw Unsupported("video");
            WindowsCaptureEditVideoAdmission.Demand(playback.VideoTracks[0].GetEncodingProperties());
            if (playback.AudioTracks.Count == 1 && !string.Equals(playback.AudioTracks[0].GetEncodingProperties().Subtype, MediaEncodingSubtypes.Aac, StringComparison.OrdinalIgnoreCase)) throw Unsupported("video");
        }
        using var borrowed = new BorrowedInput(source.Stream); using var input = borrowed.AsRandomAccessStream(); input.Seek(0);
        var profile = await MediaEncodingProfile.CreateFromStreamAsync(input).AsTask(token).ConfigureAwait(false); input.Seek(0);
        if (profile.Video is not { Subtype: "H264", Width: > 0, Height: > 0 } || profile.Video.Width != source.Media.Width || profile.Video.Height != source.Media.Height ||
            profile.Video.FrameRate.Numerator == 0 || profile.Video.FrameRate.Denominator == 0 || profile.Video.Bitrate == 0 ||
            profile.Audio is { } audio && (!string.Equals(audio.Subtype, MediaEncodingSubtypes.Aac, StringComparison.OrdinalIgnoreCase) || audio.ChannelCount is < 1 or > 2)) throw Unsupported("video");
        return profile;
    }

    // Preserve 8-bit SDR pixels rather than silently dropping HDR, ICC, orientation or animation semantics.
    internal static void DemandImage(Stream stream)
    {
        stream.Position = 0; Span<byte> signature = stackalloc byte[8]; stream.ReadExactly(signature);
        if (!signature.SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) throw Unsupported("image");
        var headerSeen = false; var chunks = 0;
        Span<byte> header = stackalloc byte[8]; Span<byte> value = stackalloc byte[32];
        while (stream.Position < stream.Length && ++chunks <= 4096)
        {
            stream.ReadExactly(header); var length = BinaryPrimitives.ReadUInt32BigEndian(header); var type = System.Text.Encoding.ASCII.GetString(header[4..]);
            var next = checked(stream.Position + length + 4); if (next > stream.Length) throw Unsupported("image");
            if (type is "iCCP" or "eXIf" or "acTL" or "cICP" or "mDCV" or "cLLI") throw Unsupported("image");
            if (type == "IHDR")
            {
                if (headerSeen || length != 13) throw Unsupported("image"); stream.ReadExactly(value[..13]); headerSeen = true;
                if (value[8] > 8) throw Unsupported("image");
            }
            if (type == "gAMA") { if (length != 4) throw Unsupported("image"); stream.ReadExactly(value[..4]); if (BinaryPrimitives.ReadUInt32BigEndian(value) != 45455) throw Unsupported("image"); }
            if (type == "cHRM")
            {
                if (length != 32) throw Unsupported("image"); stream.ReadExactly(value);
                uint[] srgb = [31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000];
                for (var index = 0; index < srgb.Length; ++index) if (BinaryPrimitives.ReadUInt32BigEndian(value[(index * 4)..]) != srgb[index]) throw Unsupported("image");
            }
            stream.Position = next;
            if (type == "IEND") { if (!headerSeen || stream.Position != stream.Length) throw Unsupported("image"); stream.Position = 0; return; }
        }
        throw Unsupported("image");
    }
    internal static BrokerException Unsupported(string kind) => new("capture_library_edit_unsupported_" + kind,
        "This format cannot be edited without changing unsupported color, track or orientation information.");
    private sealed class BorrowedInput(Stream inner) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
