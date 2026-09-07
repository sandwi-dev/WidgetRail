using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WindowsAppLibraryProvider;

internal sealed record WindowsExecutableFileIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh);

internal sealed record WindowsExecutableAuthority(
    string CanonicalPath,
    WindowsExecutableFileIdentity FileIdentity)
{
    internal string WorkingDirectory =>
        Path.GetDirectoryName(CanonicalPath)
        ?? throw new InvalidOperationException("Executable working directory is unavailable.");
}

internal interface IWindowsExecutableAuthorityReader
{
    WindowsExecutableAuthority? ReadExact(string path);
}

internal interface IWindowsPortableAppLauncher
{
    void Launch(WindowsExecutableAuthority authority, CancellationToken cancellationToken);
}

internal sealed class WindowsExecutableAuthorityReader : IWindowsExecutableAuthorityReader
{
    internal const int MaximumExecutablePathCharacters = 4096;
    private const int FileIdInfoClass = 18;
    private const uint FileNameNormalized = 0;

    public WindowsExecutableAuthority? ReadExact(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumExecutablePathCharacters ||
            !Path.IsPathFullyQualified(path) || !IsExecutable(path))
            return null;
        try
        {
            using var handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandleEx(
                    handle, FileIdInfoClass, out var information,
                    (uint)Marshal.SizeOf<FileIdInfo>()))
                return null;
            var buffer = new char[MaximumExecutablePathCharacters + 1];
            var length = GetFinalPathNameByHandle(
                handle, buffer, (uint)buffer.Length, FileNameNormalized);
            if (length == 0 || length >= buffer.Length) return null;
            var canonical = NormalizeFinalPath(new string(buffer, 0, (int)length));
            if (canonical is null || !IsExecutable(canonical)) return null;
            return new WindowsExecutableAuthority(
                canonical,
                new WindowsExecutableFileIdentity(
                    information.VolumeSerialNumber,
                    information.FileId.Low,
                    information.FileId.High));
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? NormalizeFinalPath(string value)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (value.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            value = @"\\" + value[uncPrefix.Length..];
        else if (value.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
            value = value[devicePrefix.Length..];
        if (!Path.IsPathFullyQualified(value) ||
            value.Length > MaximumExecutablePathCharacters)
            return null;
        return Path.GetFullPath(value);
    }

    private static bool IsExecutable(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        internal ulong Low;
        internal ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo information,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] char[] path,
        uint pathLength,
        uint flags);
}

internal sealed class WindowsPortableAppLauncher : IWindowsPortableAppLauncher
{
    public void Launch(
        WindowsExecutableAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = Process.Start(CreateStartInfo(authority));
        if (process is null)
            throw new Win32Exception("Windows did not accept the executable launch request.");
    }

    internal static ProcessStartInfo CreateStartInfo(WindowsExecutableAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return new ProcessStartInfo
        {
            FileName = authority.CanonicalPath,
            WorkingDirectory = authority.WorkingDirectory,
            UseShellExecute = false,
            ErrorDialog = false,
        };
    }
}
