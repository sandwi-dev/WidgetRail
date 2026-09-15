using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace WidgetRail.WindowsDisplayProvider;

/// <summary>Shared physical identity for display profiles and overlay sizing.</summary>
internal static class MonitorHardwareIdentity
{
    internal static bool IsValid(string? key) => key is null ||
        key.Length == 64 && key.All(char.IsAsciiHexDigit);

    internal static string? FromEdid(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> header = [0, 255, 255, 255, 255, 255, 255, 0];
        if (bytes.Length < 128 || !bytes[..8].SequenceEqual(header)) return null;
        var checksum = 0;
        foreach (var value in bytes[..128]) checksum += value;
        if ((checksum & 255) != 0) return null;
        var manufacturer = (bytes[8] << 8) | bytes[9];
        var letters = new[] { (manufacturer >> 10) & 31, (manufacturer >> 5) & 31, manufacturer & 31 };
        if (letters.Any(letter => letter is < 1 or > 26)) return null;
        var vendor = new string(letters.Select(letter => (char)('A' + letter - 1)).ToArray());
        string? serial = null;
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (bytes[offset] != 0 || bytes[offset + 1] != 0 || bytes[offset + 2] != 0 || bytes[offset + 3] != 255)
                continue;
            var text = Encoding.ASCII.GetString(bytes.Slice(offset + 5, 13)).Trim('\0', '\n', '\r', ' ').ToUpperInvariant();
            if (Meaningful(text)) serial = "text:" + text;
        }
        if (serial is null)
        {
            var numeric = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4));
            if (numeric is 0 or 1 or uint.MaxValue or 0x01010101 or 123456789) return null;
            serial = "number:" + numeric.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
        // Product ID is deliberately absent: VRR/HDR/firmware can change it.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vendor + ":" + serial)));
    }

    private static bool Meaningful(string value) => value.Length >= 3 &&
        value.All(character => character is >= ' ' and <= '~') && value.Distinct().Count() > 1 &&
        value is not ("UNKNOWN" or "DEFAULT" or "SERIAL" or "SERIALNUMBER" or "123456789" or "1234567890" or "NONE" or "N/A");

    internal static string? ReadCached(string devicePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var parts = devicePath.Split('#');
        if (parts.Length != 4 || !parts[0].Equals(@"\\?\DISPLAY", StringComparison.OrdinalIgnoreCase) ||
            !parts[3].Equals("{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", StringComparison.OrdinalIgnoreCase) ||
            parts.Skip(1).Take(2).Any(part => part.Length is 0 or > 128 ||
                !part.All(c => char.IsAsciiLetterOrDigit(c) || c is '&' or '_' or '-'))) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + "\\" + parts[2] + @"\Device Parameters");
            return key?.GetValue("EDID") is byte[] { Length: >= 128 and <= 32768 } edid ? FromEdid(edid) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { return null; }
    }
}
