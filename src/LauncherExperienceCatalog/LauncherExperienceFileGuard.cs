using System.Buffers.Binary;

namespace WidgetRail.LauncherExperienceCatalog;

internal static class LauncherExperienceFileGuard
{
    public static bool IsSafePackagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > LauncherExperienceValidator.MaximumPathLength ||
            Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':')) return false;
        return path.Split('/').All(segment =>
            segment.Length is > 0 and <= 100 && segment is not "." and not ".." &&
            segment.All(character => !char.IsControl(character)));
    }

    public static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new LauncherExperiencePackageException("reparse_point", "Package paths cannot contain reparse points.");
    }

    public static byte[] ReadBounded(string path, long maximumBytes)
    {
        RejectReparsePoint(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (input.Length > maximumBytes)
            throw new LauncherExperiencePackageException("file_too_large", "Package file exceeds its byte limit.");
        using var output = new MemoryStream(checked((int)input.Length));
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new LauncherExperiencePackageException("file_too_large", "Package file exceeds its byte limit.");
            output.Write(buffer, 0, read);
        }
        if (input.Length != total)
            throw new LauncherExperiencePackageException("file_changed", "Package file changed while it was read.");
        return output.ToArray();
    }

    public static bool TryReadImageDimensions(ReadOnlySpan<byte> bytes, string extension, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 24 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
            width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            return width > 0 && height > 0;
        }
        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 20 || !bytes[..4].SequenceEqual("RIFF"u8) ||
                !bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return false;
            var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
            var riffEnd = (long)riffSize + 8;
            if (riffEnd < 20 || riffEnd > bytes.Length) return false;
            var webpOffset = 12;
            while (webpOffset + 8 <= riffEnd)
            {
                var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(webpOffset + 4, 4));
                var dataStart = webpOffset + 8L;
                var dataEnd = dataStart + chunkSize;
                if (dataEnd > riffEnd) return false;
                var chunk = bytes.Slice(webpOffset, 4);
                if (chunk.SequenceEqual("VP8 "u8))
                {
                    if (chunkSize < 10 || !bytes.Slice((int)dataStart + 3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                        return false;
                    width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice((int)dataStart + 6, 2)) & 0x3fff;
                    height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice((int)dataStart + 8, 2)) & 0x3fff;
                    return width > 0 && height > 0;
                }
                if (chunk.SequenceEqual("VP8L"u8))
                {
                    if (chunkSize < 5 || bytes[(int)dataStart] != 0x2f) return false;
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice((int)dataStart + 1, 4));
                    width = (int)(bits & 0x3fff) + 1;
                    height = (int)((bits >> 14) & 0x3fff) + 1;
                    return true;
                }
                if (chunk.SequenceEqual("VP8X"u8))
                {
                    if (chunkSize < 10) return false;
                    width = 1 + bytes[(int)dataStart + 4] + (bytes[(int)dataStart + 5] << 8) +
                            (bytes[(int)dataStart + 6] << 16);
                    height = 1 + bytes[(int)dataStart + 7] + (bytes[(int)dataStart + 8] << 8) +
                             (bytes[(int)dataStart + 9] << 16);
                    return true;
                }
                var paddedSize = (long)chunkSize + (chunkSize & 1);
                var next = dataStart + paddedSize;
                if (next > riffEnd || next > int.MaxValue) return false;
                webpOffset = (int)next;
            }
            return false;
        }
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return false;
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        var offset = 2;
        while (offset + 8 < bytes.Length)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0xd8 or 0xd9) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length) return false;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (length < 7) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }
            offset += length;
        }
        return false;
    }

    public static bool IsSingleFrameImage(ReadOnlySpan<byte> bytes, string extension)
    {
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return bytes.IndexOf("acTL"u8) < 0;
        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 20) return false;
            var riffEnd = Math.Min(bytes.Length, checked((int)Math.Min(int.MaxValue,
                (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8)));
            var offset = 12;
            while (offset + 8 <= riffEnd)
            {
                var chunk = bytes.Slice(offset, 4);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
                var dataStart = offset + 8L;
                var dataEnd = dataStart + length;
                if (dataEnd > riffEnd) return false;
                if (chunk.SequenceEqual("ANIM"u8) || chunk.SequenceEqual("ANMF"u8)) return false;
                if (chunk.SequenceEqual("VP8X"u8) && length >= 1 && (bytes[(int)dataStart] & 0x02) != 0)
                    return false;
                var next = dataEnd + (length & 1);
                if (next > int.MaxValue) return false;
                offset = (int)next;
            }
            return true;
        }
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return false;
        if (bytes.IndexOf("MPF\0"u8) >= 0) return false;
        for (var index = 2; index + 1 < bytes.Length; index++)
            if (bytes[index] == 0xff && bytes[index + 1] == 0xd8) return false;
        return true;
    }
}

public sealed class LauncherExperiencePackageException(
    string code,
    string message,
    Exception? innerException = null,
    string diagnosticPath = "$")
    : Exception(message, innerException)
{
    public string Code { get; } = code;
    public string DiagnosticPath { get; } = diagnosticPath;
}
