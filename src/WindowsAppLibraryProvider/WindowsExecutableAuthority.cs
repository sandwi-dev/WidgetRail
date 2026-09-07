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

internal interface IWindowsExecutableAuthorityLease : IDisposable
{
    WindowsExecutableAuthority Authority { get; }
}

internal interface IWindowsExecutableAuthorityReader
{
    IWindowsExecutableAuthorityLease? AcquireExact(string path);
}

internal interface IWindowsPortableAppLauncher
{
    void Launch(WindowsExecutableAuthority authority, CancellationToken cancellationToken);
}

internal sealed class WindowsExecutableAuthorityLease(
    WindowsExecutableAuthority authority,
    IReadOnlyList<SafeFileHandle> handles) : IWindowsExecutableAuthorityLease
{
    private IReadOnlyList<SafeFileHandle>? _handles = handles;

    public WindowsExecutableAuthority Authority { get; } = authority;

    public void Dispose()
    {
        var handles = Interlocked.Exchange(ref _handles, null);
        if (handles is null) return;
        for (var index = handles.Count - 1; index >= 0; --index)
            handles[index].Dispose();
    }
}

internal sealed class WindowsExecutableAuthorityReader : IWindowsExecutableAuthorityReader
{
    internal const int MaximumExecutablePathCharacters = 32_762;
    private const int NativeDosPathPrefixCharacters = 4;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileNameNormalized = 0;
    private const uint DriveRemovable = 2;
    private const uint DriveFixed = 3;
    private const uint DriveRamDisk = 6;

    public IWindowsExecutableAuthorityLease? AcquireExact(string path)
    {
        if (!OperatingSystem.IsWindows() ||
            !TryNormalizeLocalExecutablePath(path, out var candidate))
            return null;

        var handles = new List<SafeFileHandle>();
        var transferred = false;
        try
        {
            var root = Path.GetPathRoot(candidate)!;
            if (!IsLocalDrive(root)) return null;
            foreach (var directory in EnumerateDirectories(root, candidate))
            {
                var handle = Open(
                    directory, FileReadAttributes,
                    FileShareRead | FileShareWrite,
                    FileFlagBackupSemantics | FileFlagOpenReparsePoint);
                if (handle is null || IsReparsePoint(handle) ||
                    !FinalPathEquals(handle, directory))
                {
                    handle?.Dispose();
                    return null;
                }
                handles.Add(handle);
            }

            var executable = Open(
                candidate, GenericRead, FileShareRead, FileFlagOpenReparsePoint);
            if (executable is null || IsReparsePoint(executable) ||
                !FinalPathEquals(executable, candidate) ||
                !GetFileInformationByHandleEx(
                    executable, FileIdInfoClass, out FileIdInfo information,
                    (uint)Marshal.SizeOf<FileIdInfo>()))
            {
                executable?.Dispose();
                return null;
            }
            handles.Add(executable);
            var lease = new WindowsExecutableAuthorityLease(
                new WindowsExecutableAuthority(
                    candidate,
                    new WindowsExecutableFileIdentity(
                        information.VolumeSerialNumber,
                        information.FileId.Low,
                        information.FileId.High)),
                handles.ToArray());
            transferred = true;
            return lease;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (!transferred)
            {
                for (var index = handles.Count - 1; index >= 0; --index)
                    handles[index].Dispose();
            }
        }
    }

    internal static bool TryNormalizeLocalExecutablePath(
        string? value,
        out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumExecutablePathCharacters ||
            value.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(value) ||
            !IsNormalDosRoot(Path.GetPathRoot(value)))
            return false;
        try
        {
            var full = Path.GetFullPath(value);
            var root = Path.GetPathRoot(full);
            if (full.Length > MaximumExecutablePathCharacters ||
                !IsNormalDosRoot(root) ||
                !Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return false;
            path = full;
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDirectories(
        string root,
        string executable)
    {
        yield return root;
        var directory = Path.GetDirectoryName(executable);
        if (directory is null || string.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
            yield break;
        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        foreach (var component in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            yield return current;
        }
    }

    private static bool IsLocalDrive(string root)
    {
        var kind = GetDriveType(root);
        return kind is DriveRemovable or DriveFixed or DriveRamDisk;
    }

    private static SafeFileHandle? Open(
        string path,
        uint access,
        uint sharing,
        uint flags)
    {
        var handle = CreateFile(
            ToNativeDosPath(path), access, sharing, IntPtr.Zero,
            OpenExisting, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        return null;
    }

    private static bool IsReparsePoint(SafeFileHandle handle) =>
        !GetFileInformationByHandleEx(
            handle, FileAttributeTagInfoClass, out FileAttributeTagInfo information,
            (uint)Marshal.SizeOf<FileAttributeTagInfo>()) ||
        (information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;

    private static bool FinalPathEquals(SafeFileHandle handle, string candidate)
    {
        var buffer = new char[
            MaximumExecutablePathCharacters + NativeDosPathPrefixCharacters + 1];
        var length = GetFinalPathNameByHandle(
            handle, buffer, (uint)buffer.Length, FileNameNormalized);
        if (length == 0 || length >= buffer.Length) return false;
        var final = NormalizeFinalPath(new string(buffer, 0, (int)length));
        return final is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(final),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFinalPath(string value)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (value.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        if (value.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
            value = value[devicePrefix.Length..];
        if (!TryNormalizeLocalPath(value, out var result)) return null;
        return result;
    }

    private static bool TryNormalizeLocalPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumExecutablePathCharacters ||
            value.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        try
        {
            var full = Path.GetFullPath(value);
            var root = Path.GetPathRoot(full);
            if (full.Length > MaximumExecutablePathCharacters ||
                !IsNormalDosRoot(root))
                return false;
            path = full;
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsNormalDosRoot(string? root) =>
        root is { Length: 3 } &&
        char.IsAsciiLetter(root[0]) &&
        root[1] == ':' &&
        root[2] == Path.DirectorySeparatorChar;

    private static string ToNativeDosPath(string path) => @"\\?\" + path;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo information,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInfo information,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] char[] path,
        uint pathLength,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string rootPathName);
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
