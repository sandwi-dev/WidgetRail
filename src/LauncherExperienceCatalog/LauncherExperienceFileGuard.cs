using System.Buffers.Binary;

namespace GameBarAlternative.LauncherExperienceCatalog;

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
            if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return false;
            if (bytes.Slice(12, 4).SequenceEqual("VP8X"u8))
            {
                width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
                height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
                return true;
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
}

public sealed class LauncherExperiencePackageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
