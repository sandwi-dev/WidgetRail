using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal sealed class WindowsStartMenuApplicationSource : IStartMenuApplicationSource
{
    internal const int MaximumShortcutCandidates = 4096;
    internal const int MaximumDirectoryDepth = 16;
    internal const int MaximumShortcutBytes = 1024 * 1024;

    public IReadOnlyList<StartMenuRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var registrations = new List<StartMenuRegistration>();
        EnumerateRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            StartMenuScope.CurrentUser,
            registrations,
            cancellationToken);
        EnumerateRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            StartMenuScope.AllUsers,
            registrations,
            cancellationToken);
        return registrations;
    }

    public StartMenuRegistration? ReadExact(
        string shortcutPath,
        StartMenuScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return null;
        var root = Environment.GetFolderPath(scope switch
        {
            StartMenuScope.CurrentUser => Environment.SpecialFolder.Programs,
            StartMenuScope.AllUsers => Environment.SpecialFolder.CommonPrograms,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        });
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(shortcutPath))
            return null;

        try
        {
            root = Path.GetFullPath(root);
            shortcutPath = Path.GetFullPath(shortcutPath);
            if (!IsSafeShortcutPath(root, shortcutPath, cancellationToken)) return null;
            return TryReadRegistration(root, shortcutPath, scope);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsSafeShortcutPath(
        string root,
        string shortcutPath,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(root, shortcutPath);
        if (relativePath.Length == 0 || relativePath == "." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath) ||
            !Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return false;

        var components = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    private static void EnumerateRoot(
        string root,
        StartMenuScope scope,
        List<StartMenuRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        root = Path.GetFullPath(root);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visitedCandidates = 0;

        while (pending.Count != 0 && visitedCandidates < MaximumShortcutCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(current.Path).EnumerateFileSystemInfos();
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try
                    {
                        attributes = entry.Attributes;
                    }
                    catch (Exception exception) when (exception is IOException or
                        UnauthorizedAccessException or FileNotFoundException)
                    {
                        continue;
                    }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (current.Depth < MaximumDirectoryDepth)
                            pending.Push((entry.FullName, current.Depth + 1));
                        continue;
                    }
                    if (!entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        continue;
                    visitedCandidates++;
                    if (TryReadRegistration(root, entry.FullName, scope) is { } registration)
                        registrations.Add(registration);
                    if (visitedCandidates >= MaximumShortcutCandidates) break;
                }
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A Start Menu folder can change during enumeration. The next
                // refresh will reconcile it; never fail the entire catalog.
            }
        }
    }

    private static StartMenuRegistration? TryReadRegistration(
        string root, string shortcutPath, StartMenuScope scope)
    {
        var relativePath = Path.GetRelativePath(root, shortcutPath);
        if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            return null;

        try
        {
            object shellLinkObject = new ShellLinkComObject();
            var shellLink = (IShellLinkW)shellLinkObject;
            try
            {
                ((IPersistFile)shellLink).Load(shortcutPath, 0);
                var target = new StringBuilder(32_768);
                shellLink.GetPath(target, target.Capacity, nint.Zero, 0x4);
                var targetPath = target.ToString().Trim();
                if (!IsExecutableTarget(targetPath)) return null;
                var arguments = new StringBuilder(8_192);
                shellLink.GetArguments(arguments, arguments.Capacity);
                var revalidationKey = HashShortcutFile(shortcutPath);
                if (revalidationKey is null) return null;
                var identity = HashIdentity(targetPath, arguments.ToString());
                var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                return new StartMenuRegistration(
                    identity, displayName, scope, Path.GetFullPath(shortcutPath),
                    revalidationKey);
            }
            finally
            {
                if (OperatingSystem.IsWindows() && Marshal.IsComObject(shellLinkObject))
                    Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
        catch (Exception exception) when (exception is COMException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsExecutableTarget(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || targetPath.Length > 32_767)
            return false;
        var extension = Path.GetExtension(targetPath);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    internal static string IdentityForExecutable(string targetPath) =>
        HashIdentity(targetPath, string.Empty);

    private static string HashIdentity(string targetPath, string arguments)
    {
        var normalized = targetPath.Trim().ToUpperInvariant() + "\0" + arguments.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "start-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? HashShortcutFile(string shortcutPath)
    {
        try
        {
            using var stream = new FileStream(
                shortcutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumShortcutBytes) return null;
            return "lnk-" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            NotSupportedException)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkComObject;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumCharacters,
            nint findData,
            uint flags);
        void GetIDList(out nint itemIdList);
        void SetIDList(nint itemIdList);
        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,
            int maximumCharacters);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int maximumCharacters);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int maximumCharacters);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int maximumCharacters,
            out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
