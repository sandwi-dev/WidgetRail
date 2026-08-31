namespace WidgetRail.WidgetSdk;

/// <summary>The encoded formats admitted by the trusted artwork resource contract.</summary>
public enum WidgetArtworkContentType
{
    Png,
    Jpeg,
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
            _ => false,
        };
    }

    internal static string ContentTypeValue(WidgetArtworkContentType contentType) => contentType switch
    {
        WidgetArtworkContentType.Png => PngContentType,
        WidgetArtworkContentType.Jpeg => JpegContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(contentType)),
    };

    internal static WidgetArtworkContentType? ParseContentType(string value) => value switch
    {
        PngContentType => WidgetArtworkContentType.Png,
        JpegContentType => WidgetArtworkContentType.Jpeg,
        _ => null,
    };

    private static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[..8].SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 &&
        bytes[^2] == 0xff && bytes[^1] == 0xd9;
}
