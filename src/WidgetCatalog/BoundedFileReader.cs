using System.Security.Cryptography;

namespace GameBarAlternative.WidgetCatalog;

internal static class BoundedFileReader
{
    internal static byte[] ReadAll(Stream input, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream is not readable.", nameof(input));
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var initialLength = input.CanSeek ? input.Length : (long?)null;
        return ReadCore(input, maximumBytes, initialLength);
    }

    internal static byte[] ReadExact(Stream input, long expectedLength, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream is not readable.", nameof(input));
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        return ReadCore(input, maximumBytes, expectedLength);
    }

    private static byte[] ReadCore(Stream input, int maximumBytes, long? initialLength)
    {
        if (initialLength is < 0 or > int.MaxValue)
            throw new InvalidDataException("Input length is invalid.");
        if (initialLength > maximumBytes)
            throw new InvalidDataException("Input exceeds its byte limit.");

        using var output = new MemoryStream(
            initialLength is > 0 and <= int.MaxValue ? (int)initialLength.Value : 0);
        var buffer = new byte[(int)Math.Min(64 * 1024L, (long)maximumBytes + 1)];
        try
        {
            var total = 0;
            while (true)
            {
                var remainingWithSentinel = (long)maximumBytes - total + 1;
                var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingWithSentinel));
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximumBytes)
                    throw new InvalidDataException("Input exceeds its byte limit.");
                output.Write(buffer, 0, read);
            }
            if (initialLength is { } expected &&
                (total != expected || (input.CanSeek && input.Length != expected)))
                throw new InvalidDataException("Input length changed while it was read.");
            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    internal static void AppendExact(
        IncrementalHash hash,
        Stream input,
        long expectedLength,
        byte[] buffer,
        IncrementalHash? contentHash = null)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(buffer);
        if (!input.CanRead) throw new ArgumentException("Input stream is not readable.", nameof(input));
        if (expectedLength < 0) throw new ArgumentOutOfRangeException(nameof(expectedLength));
        if (buffer.Length == 0) throw new ArgumentException("Buffer cannot be empty.", nameof(buffer));

        long consumed = 0;
        while (consumed < expectedLength)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, expectedLength - consumed));
            if (read == 0)
                throw new InvalidDataException("Input ended before its encoded length.");
            hash.AppendData(buffer, 0, read);
            contentHash?.AppendData(buffer, 0, read);
            consumed += read;
        }
        if (input.Read(buffer, 0, 1) != 0)
            throw new InvalidDataException("Input exceeded its encoded length.");
        if (input.CanSeek && input.Length != expectedLength)
            throw new InvalidDataException("Input length changed while it was hashed.");
    }
}
