using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Resolves a Windows Shell icon entirely inside the trusted provider, renders
/// it to a fixed-size pixel buffer, and returns only bounded PNG pixels.
/// </summary>
internal sealed class WindowsAppIconSource : IWindowsAppIconSource
{
    internal const int IconPixels = 48;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiPidl = 0x000000008;
    private const uint DiNormal = 0x0003;
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    public string? TryRasterizePngBase64(
        string shortcutPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(shortcutPath))
            return null;

        var shellFileInfo = new ShellFileInfo();
        nint icon = 0;
        try
        {
            if (SHGetFileInfo(
                    shortcutPath,
                    0,
                    ref shellFileInfo,
                    (uint)Marshal.SizeOf<ShellFileInfo>(),
                    ShgfiIcon | ShgfiLargeIcon) == 0 ||
                shellFileInfo.Icon == 0)
                return null;
            icon = shellFileInfo.Icon;
            cancellationToken.ThrowIfCancellationRequested();
            var bgra = RenderIcon(icon, cancellationToken);
            if (bgra is null) return null;
            var png = EncodePng(bgra, IconPixels, IconPixels);
            if (png.Length > AppLibraryImageLimits.MaximumPngBytes) return null;
            return Convert.ToBase64String(png);
        }
        catch (Exception exception) when (exception is ArgumentException or
            ExternalException or IOException or UnauthorizedAccessException or
            OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (icon != 0) DestroyIcon(icon);
        }
    }

    public string? TryRasterizeAppsFolderPngBase64(
        string aumid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() ||
            WindowsAppsFolderApplicationSource.NormalizeAumid(aumid) is null)
            return null;

        nint itemIdList = 0;
        nint icon = 0;
        try
        {
            var result = SHParseDisplayName(
                "shell:AppsFolder\\" + aumid, 0, out itemIdList, 0, out _);
            if (result < 0 || itemIdList == 0) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var shellFileInfo = new ShellFileInfo();
            if (SHGetFileInfoFromPidl(
                    itemIdList,
                    0,
                    ref shellFileInfo,
                    (uint)Marshal.SizeOf<ShellFileInfo>(),
                    ShgfiIcon | ShgfiLargeIcon | ShgfiPidl) == 0 ||
                shellFileInfo.Icon == 0)
                return null;
            icon = shellFileInfo.Icon;
            cancellationToken.ThrowIfCancellationRequested();
            var bgra = RenderIcon(icon, cancellationToken);
            if (bgra is null) return null;
            var png = EncodePng(bgra, IconPixels, IconPixels);
            if (png.Length > AppLibraryImageLimits.MaximumPngBytes) return null;
            return Convert.ToBase64String(png);
        }
        catch (Exception exception) when (exception is ArgumentException or
            ExternalException or IOException or UnauthorizedAccessException or
            OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (icon != 0) DestroyIcon(icon);
            if (itemIdList != 0) CoTaskMemFree(itemIdList);
        }
    }

    private static byte[]? RenderIcon(nint icon, CancellationToken cancellationToken)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = IconPixels,
            Height = -IconPixels,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
            SizeImage = IconPixels * IconPixels * 4,
        };
        var bitmapInfo = new BitmapInfo { Header = header };
        nint pixels = 0;
        nint bitmap = 0;
        nint deviceContext = 0;
        nint previous = 0;
        try
        {
            deviceContext = CreateCompatibleDC(0);
            if (deviceContext == 0) return null;
            bitmap = CreateDIBSection(
                deviceContext, ref bitmapInfo, 0, out pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) return null;
            previous = SelectObject(deviceContext, bitmap);
            if (previous == 0 || previous == new nint(-1)) return null;

            var result = new byte[IconPixels * IconPixels * 4];
            Marshal.Copy(result, 0, pixels, result.Length);
            if (!DrawIconEx(
                    deviceContext, 0, 0, icon, IconPixels, IconPixels,
                    0, 0, DiNormal))
                return null;
            cancellationToken.ThrowIfCancellationRequested();
            Marshal.Copy(pixels, result, 0, result.Length);
            NormalizeAlpha(result);
            return result;
        }
        finally
        {
            if (previous != 0 && previous != new nint(-1) && deviceContext != 0)
                SelectObject(deviceContext, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (deviceContext != 0) DeleteDC(deviceContext);
        }
    }

    private static void NormalizeAlpha(byte[] bgra)
    {
        var hasAlpha = false;
        for (var index = 3; index < bgra.Length; index += 4)
            hasAlpha |= bgra[index] != 0;

        for (var index = 0; index < bgra.Length; index += 4)
        {
            var alpha = bgra[index + 3];
            if (!hasAlpha)
            {
                bgra[index + 3] =
                    bgra[index] == 0 && bgra[index + 1] == 0 && bgra[index + 2] == 0
                        ? (byte)0
                        : (byte)255;
                continue;
            }
            if (alpha == 0)
            {
                bgra[index] = bgra[index + 1] = bgra[index + 2] = 0;
                continue;
            }
            if (alpha == 255) continue;
            bgra[index] = Unpremultiply(bgra[index], alpha);
            bgra[index + 1] = Unpremultiply(bgra[index + 1], alpha);
            bgra[index + 2] = Unpremultiply(bgra[index + 2], alpha);
        }
    }

    private static byte Unpremultiply(byte value, byte alpha) =>
        (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);

    internal static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || width > AppLibraryImageLimits.MaximumPixelDimension ||
            height > AppLibraryImageLimits.MaximumPixelDimension ||
            bgra.Length != checked(width * height * 4))
            throw new ArgumentOutOfRangeException(nameof(bgra));

        using var raw = new MemoryStream(checked((width * 4 + 1) * height));
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                raw.WriteByte(bgra[offset + 2]);
                raw.WriteByte(bgra[offset + 1]);
                raw.WriteByte(bgra[offset]);
                raw.WriteByte(bgra[offset + 3]);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw.GetBuffer().AsSpan(0, checked((int)raw.Length)));

        using var png = new MemoryStream();
        png.Write(PngSignature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, data.Length);
        output.Write(value);
        output.Write(type);
        output.Write(data);
        var crc = Crc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(value, crc);
        output.Write(value);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type) crc = UpdateCrc32(crc, value);
        foreach (var value in data) crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xedb88320U ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public int SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SHGetFileInfo(
        string path, uint fileAttributes, ref ShellFileInfo fileInfo,
        uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", SetLastError = true)]
    private static extern nint SHGetFileInfoFromPidl(
        nint itemIdList, uint fileAttributes, ref ShellFileInfo fileInfo,
        uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name, nint bindContext, out nint itemIdList,
        uint requestedAttributes, out uint attributes);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint deviceContext, ref BitmapInfo bitmapInfo, uint usage,
        out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        nint deviceContext, int x, int y, nint icon, int width, int height,
        uint animationStep, nint flickerFreeBrush, uint flags);
}
