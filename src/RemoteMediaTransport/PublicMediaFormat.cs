namespace WidgetRail.RemoteMediaTransport;

/// <summary>Container identification from owned bytes, never from a filename or HTTP media type.</summary>
public static class PublicMediaFormat
{
    public static string? Identify(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff) return "image/jpeg";
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (header.Length >= 16 && header.Slice(4, 4).SequenceEqual("ftyp"u8) &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header) is >= 16 and <= 1024 * 1024) return "video/mp4";
        if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return "video/webm";
        if (header.Length >= 4 && header[..4].SequenceEqual("OggS"u8)) return "audio/ogg";
        if (header.Length >= 3 && header[..3].SequenceEqual("ID3"u8) ||
            header.Length >= 4 && header[0] == 0xff && (header[1] & 0xe0) == 0xe0 && (header[1] & 0x18) != 0x08 &&
            (header[1] & 6) != 0 && (header[2] & 0xf0) is not (0 or 0xf0) && (header[2] & 0x0c) != 0x0c) return "audio/mpeg";
        return null;
    }
}
