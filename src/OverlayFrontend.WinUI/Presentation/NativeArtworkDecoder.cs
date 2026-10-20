using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.WidgetProtocol;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal readonly record struct ArtworkPixelSize(int Width, int Height);
internal sealed record NativeArtworkPayload(ReadOnlyMemory<byte> Bytes, Uri? RemoteUri = null,
    ArtworkFileReference? File = null, Func<CancellationToken, Task<NativeArtworkPayload?>>? Fallback = null);
internal sealed record NativeArtworkDecode(BitmapImage Image, ArtworkPixelSize Source, ArtworkPixelSize Decoded);

/// <summary>Native metadata validation and target-sized asynchronous decode. No pixel or encoded-data cache.</summary>
internal static class NativeArtworkDecoder
{
    internal static ArtworkPixelSize SizeFor(ArtworkPixelSize source, ArtworkPixelSize target, ImageFit? fit, bool naturalSize = false)
    {
        if (naturalSize) return source; // object-fit:none keeps intrinsic DIPs; never shrink the decoded source.
        if (target.Width == 0 && target.Height == 0) return source; // Unconstrained intrinsic image.
        var x = target.Width > 0 ? (double)target.Width / source.Width : (double?)null;
        var y = target.Height > 0 ? (double)target.Height / source.Height : (double?)null;
        if (fit == ImageFit.Fill && x is not null && y is not null)
            return new(Math.Min(source.Width, target.Width), Math.Min(source.Height, target.Height));
        var ratio = Math.Min(1, x is null ? y!.Value : y is null ? x.Value :
            fit == ImageFit.Contain ? Math.Min(x.Value, y.Value) : Math.Max(x.Value, y.Value));
        return new(Math.Max(1, (int)Math.Ceiling(source.Width * ratio)), Math.Max(1, (int)Math.Ceiling(source.Height * ratio)));
    }

    internal static async Task<NativeArtworkDecode?> DecodeAsync(NativeArtworkPayload? payload,
        ArtworkPixelSize target, ImageFit? fit, CancellationToken token, bool naturalSize = false)
    {
        if (payload is null || payload.RemoteUri is null && payload.File is null && payload.Bytes.IsEmpty) return null;
        if (payload.File is not null && payload.Fallback is not null)
        {
            try { return await DecodeAsync(payload with { Fallback = null }, target, fit, token, naturalSize); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException ||
                error is COMException && (unchecked((uint)error.HResult) & 0xffffff00u) == 0x88982f00u)
            {
                token.ThrowIfCancellationRequested();
                return await DecodeAsync(await payload.Fallback(token), target, fit, token, naturalSize);
            }
        }
        token.ThrowIfCancellationRequested();
        if (payload.RemoteUri is null && payload.Bytes.Length > ProtocolConstants.MaximumEncodedArtworkBytes)
            throw new InvalidDataException("Artwork exceeds its encoded resource bound.");
        (IRandomAccessStream Stream, long? Length) opened;
        if (payload.File is { } file) opened = await NativeArtworkFile.OpenAsync(file, token);
        else if (payload.RemoteUri is { } uri) opened = (await NativeArtworkStream.OpenAsync(uri, token), null);
        else opened = (OpenMemory(payload.Bytes), payload.Bytes.Length);
        using var stream = opened.Stream;
        var encodedLength = opened.Length ?? checked((long)stream.Size);
        if (encodedLength is <= 0 or > ProtocolConstants.MaximumEncodedArtworkBytes)
            throw new InvalidDataException("Artwork exceeds its encoded resource bound.");
        NativeArtworkCounters.Encoded(encodedLength);
        var metadata = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        if (metadata.PixelWidth is 0 or > ProtocolConstants.MaximumEncodedArtworkDimension ||
            metadata.PixelHeight is 0 or > ProtocolConstants.MaximumEncodedArtworkDimension ||
            (ulong)metadata.PixelWidth * metadata.PixelHeight > ProtocolConstants.MaximumEncodedArtworkPixels)
            throw new InvalidDataException("Artwork exceeds its source pixel resource bound.");
        var source = new ArtworkPixelSize((int)metadata.OrientedPixelWidth, (int)metadata.OrientedPixelHeight);
        var decoded = SizeFor(source, target, fit, naturalSize);
        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = decoded.Width, DecodePixelHeight = decoded.Height };
        NativeArtworkCounters.Decoding(source, decoded);
        await bitmap.SetSourceAsync(stream).AsTask(token);
        token.ThrowIfCancellationRequested();
        return new(bitmap, source, decoded);
    }

    private static IRandomAccessStream OpenMemory(ReadOnlyMemory<byte> bytes)
    {
        // The value-owned payload stays alive through SetSourceAsync. The adapter
        // is read-only and never exposes a writable stream to the native codec.
        if (!MemoryMarshal.TryGetArray(bytes, out var segment))
        { segment = new(bytes.ToArray()); NativeArtworkCounters.Copied(bytes.Length); }
        return new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false, publiclyVisible: true).AsRandomAccessStream();
    }
}

internal sealed record NativeArtworkStatistics(long Requests, long Decodes, long Completed, long Cancelled,
    long Failed, long EncodedBytes, long CopiedBytes, long NaturalPixelBytes, long ConfiguredPixelBytes);

/// <summary>Cumulative diagnostic counters; pixel totals describe decode requests, not process/GPU memory.</summary>
internal static class NativeArtworkCounters
{
    private static long requests, decodes, completed, cancelled, failed, encodedBytes, copiedBytes, naturalPixelBytes, configuredPixelBytes;
    internal static NativeArtworkStatistics Snapshot() => new(Interlocked.Read(ref requests), Interlocked.Read(ref decodes),
        Interlocked.Read(ref completed), Interlocked.Read(ref cancelled), Interlocked.Read(ref failed), Interlocked.Read(ref encodedBytes),
        Interlocked.Read(ref copiedBytes), Interlocked.Read(ref naturalPixelBytes), Interlocked.Read(ref configuredPixelBytes));
    internal static System.Text.Json.JsonElement JsonSnapshot() => System.Text.Json.JsonSerializer.SerializeToElement(
        Snapshot(), NativeArtworkJsonContext.Default.NativeArtworkStatistics);
    internal static void Requested() => Interlocked.Increment(ref requests);
    internal static void Completed() => Interlocked.Increment(ref completed);
    internal static void Cancelled() => Interlocked.Increment(ref cancelled);
    internal static void Failed() => Interlocked.Increment(ref failed);
    internal static void Encoded(long bytes) => Interlocked.Add(ref encodedBytes, bytes);
    internal static void Copied(long bytes) => Interlocked.Add(ref copiedBytes, bytes);
    internal static void Decoding(ArtworkPixelSize source, ArtworkPixelSize target)
    {
        Interlocked.Increment(ref decodes);
        Interlocked.Add(ref naturalPixelBytes, (long)source.Width * source.Height * 4);
        Interlocked.Add(ref configuredPixelBytes, (long)target.Width * target.Height * 4);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(NativeArtworkStatistics))]
internal sealed partial class NativeArtworkJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
