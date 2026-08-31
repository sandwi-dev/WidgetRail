using System.Buffers.Binary;

namespace WidgetRail.WidgetSdk;

/// <summary>The encoded formats admitted by the trusted artwork resource contract.</summary>
public enum WidgetArtworkContentType
{
    Png,
    Jpeg,
    WebP,
}

/// <summary>
/// One value-owned encoded artwork resource. The runtime preserves these bytes
/// exactly; presentation sizing occurs only after trusted native decode.
/// </summary>
public sealed class WidgetEncodedArtwork
{
    private readonly byte[] _bytes;

    public WidgetEncodedArtwork(
        WidgetArtworkContentType contentType,
        ReadOnlyMemory<byte> bytes)
    {
        if (!Enum.IsDefined(contentType))
            throw new ArgumentOutOfRangeException(nameof(contentType));
        ContentType = contentType;
        _bytes = bytes.ToArray();
    }

    public WidgetArtworkContentType ContentType { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes;
}

internal static class WidgetEncodedArtworkContract
{
    internal const string PngContentType = "image/png";
    internal const string JpegContentType = "image/jpeg";
    internal const string WebPContentType = "image/webp";

    internal static bool IsValid(WidgetEncodedArtwork? artwork)
    {
        if (artwork is null || artwork.Bytes.Length is <= 0 or
            > WidgetRail.WidgetProtocol.ProtocolConstants.MaximumEncodedArtworkBytes)
            return false;
        var bytes = artwork.Bytes.Span;
        return artwork.ContentType switch
        {
            WidgetArtworkContentType.Png => IsPng(bytes),
            WidgetArtworkContentType.Jpeg => IsJpeg(bytes),
            WidgetArtworkContentType.WebP => IsWebP(bytes),
            _ => false,
        };
    }

    internal static string ContentTypeValue(WidgetArtworkContentType contentType) => contentType switch
    {
        WidgetArtworkContentType.Png => PngContentType,
        WidgetArtworkContentType.Jpeg => JpegContentType,
        WidgetArtworkContentType.WebP => WebPContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(contentType)),
    };

    internal static WidgetArtworkContentType? ParseContentType(string value) => value switch
    {
        PngContentType => WidgetArtworkContentType.Png,
        JpegContentType => WidgetArtworkContentType.Jpeg,
        WebPContentType => WidgetArtworkContentType.WebP,
        _ => null,
    };

    private static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[..8].SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 &&
        bytes[^2] == 0xff && bytes[^1] == 0xd9;

    private static bool IsWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8)
            return false;

        var extended = false;
        var animated = false;
        var animationHeader = false;
        var animationFrame = false;
        var imagePayload = false;
        var offset = 12;
        while (bytes.Length - offset >= 8)
        {
            var encodedLength = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset + 4, 4));
            if (encodedLength > int.MaxValue) return false;
            var length = (int)encodedLength;
            var data = offset + 8;
            if (length > bytes.Length - data) return false;
            var end = data + length;
            var chunk = bytes.Slice(offset, 4);
            if (chunk.SequenceEqual("VP8 "u8))
            {
                if (length < 10 || !bytes.Slice(data + 3, 3)
                        .SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                    return false;
                imagePayload = true;
            }
            else if (chunk.SequenceEqual("VP8L"u8))
            {
                if (length < 5 || bytes[data] != 0x2f) return false;
                imagePayload = true;
            }
            else if (chunk.SequenceEqual("VP8X"u8))
            {
                if (extended || length != 10 || (bytes[data] & 0xc1) != 0) return false;
                extended = true;
                animated = (bytes[data] & 0x02) != 0;
            }
            else if (chunk.SequenceEqual("ANIM"u8))
            {
                if (!extended || !animated || length != 6) return false;
                animationHeader = true;
            }
            else if (chunk.SequenceEqual("ANMF"u8))
            {
                if (!extended || !animated || length < 24 ||
                    !ContainsWebPImageChunk(bytes, data + 16, end))
                    return false;
                animationFrame = true;
            }
            var padded = length + (length & 1);
            if (padded > bytes.Length - data) return false;
            offset = data + padded;
        }
        return offset == bytes.Length &&
            (animated ? animationHeader && animationFrame : imagePayload);
    }

    private static bool ContainsWebPImageChunk(
        ReadOnlySpan<byte> bytes,
        int offset,
        int end)
    {
        var imagePayload = false;
        while (end - offset >= 8)
        {
            var encodedLength = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset + 4, 4));
            if (encodedLength > int.MaxValue) return false;
            var length = (int)encodedLength;
            var data = offset + 8;
            if (length > end - data) return false;
            var chunk = bytes.Slice(offset, 4);
            if (chunk.SequenceEqual("VP8 "u8))
            {
                if (length < 10 || !bytes.Slice(data + 3, 3)
                        .SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                    return false;
                imagePayload = true;
            }
            else if (chunk.SequenceEqual("VP8L"u8))
            {
                if (length < 5 || bytes[data] != 0x2f) return false;
                imagePayload = true;
            }
            var padded = length + (length & 1);
            if (padded > end - data) return false;
            offset = data + padded;
        }
        return offset == end && imagePayload;
    }
}
