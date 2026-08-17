using System.Buffers.Binary;

namespace WidgetRail.PlatformBroker;

internal static class BrokerInlinePngPolicy
{
    internal static (string Base64, int Bytes)? Validate(
        string? pngBase64,
        int maximumPngBytes,
        int maximumPixelDimension)
    {
        if (string.IsNullOrEmpty(pngBase64)) return null;
        byte[] png;
        try
        {
            png = Convert.FromBase64String(pngBase64);
        }
        catch (FormatException)
        {
            return null;
        }
        if (png.Length is < 45 || png.Length > maximumPngBytes ||
            !pngBase64.Equals(Convert.ToBase64String(png), StringComparison.Ordinal) ||
            !png.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4)) != 13 ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return null;

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width is < 1 || width > maximumPixelDimension ||
            height is < 1 || height > maximumPixelDimension ||
            png[24] != 8 || png[25] != 6 || png[26] != 0 || png[27] != 0 ||
            png[28] != 0 || !HasWellFormedChunks(png))
            return null;
        return (pngBase64, png.Length);
    }

    private static bool HasWellFormedChunks(ReadOnlySpan<byte> png)
    {
        var offset = 8;
        var sawHeader = false;
        var sawImageData = false;
        while (offset <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
            if (length < 0 || length > png.Length - offset - 12) return false;
            var type = png.Slice(offset + 4, 4);
            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) return false;
                sawHeader = true;
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                return false;
            }
            if (type.SequenceEqual("IDAT"u8)) sawImageData = true;
            offset += 12 + length;
            if (!type.SequenceEqual("IEND"u8)) continue;
            return length == 0 && sawImageData && offset == png.Length;
        }
        return false;
    }
}
