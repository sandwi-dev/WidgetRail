using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Pins one bounded, non-reparse directory tree for an exact AppContainer
/// worker session. The lease is internal host plumbing, not widget authority.
/// </summary>
internal sealed class PinnedDirectoryContentLease : IWidgetProcessContentLease
{
    internal const int MaximumEntries = 1_024;
    internal const long MaximumAggregateFileBytes = 64L * 1024 * 1024;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private readonly IReadOnlyList<SafeFileHandle> _handles;
    private bool _disposed;

    private PinnedDirectoryContentLease(
        IReadOnlyList<AppContainerAuthorityExpectedTarget> targets,
        IReadOnlyList<SafeFileHandle> handles)
    {
        Targets = targets;
        _handles = handles;
    }

    public IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets { get; }

    internal static PinnedDirectoryContentLease Acquire(string directory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Exact preview content leases require Windows object identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(root) || IsReparse(root))
            throw new WidgetProcessAdmissionException(
                "Preview content root is unavailable or traverses a reparse point.");

        var first = CaptureTree(root);
        var handles = new List<SafeFileHandle>(first.Count);
        var targets = new List<AppContainerAuthorityExpectedTarget>(first.Count);
        try
        {
            foreach (var entry in first)
            {
                var handle = OpenPinned(entry.Path, entry.IsDirectory);
                handles.Add(handle);
                targets.Add(new AppContainerAuthorityExpectedTarget(
                    new AppContainerAuthorityTarget(
                        entry.Path,
                        entry.Path.Equals(root, StringComparison.OrdinalIgnoreCase)
                            ? AppContainerAuthorityTargetKind.AuthorityRootDirectory
                            : entry.IsDirectory
                                ? AppContainerAuthorityTargetKind.VerifiedDirectory
                                : AppContainerAuthorityTargetKind.VerifiedFile),
                    Identity(handle)));
            }
            var second = CaptureTree(root);
            if (!first.SequenceEqual(second))
                throw new WidgetProcessAdmissionException(
                    "Preview content changed during exact admission.");
            return new PinnedDirectoryContentLease(targets.AsReadOnly(), handles.AsReadOnly());
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var handle in _handles) handle.Dispose();
    }

    private static IReadOnlyList<Entry> CaptureTree(string root)
    {
        var entries = new List<Entry> { new(root, true, 0) };
        long aggregateBytes = 0;
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count != 0)
        {
            var directory = pending.Dequeue();
            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new WidgetProcessAdmissionException(
                    "Preview content could not be enumerated exactly.", exception);
            }
            foreach (var path in children)
            {
                if (entries.Count >= MaximumEntries)
                    throw new WidgetProcessAdmissionException(
                        $"Preview content exceeds the {MaximumEntries}-entry bound.");
                if (IsReparse(path))
                    throw new WidgetProcessAdmissionException(
                        "Preview content cannot contain reparse points.");
                var attributes = File.GetAttributes(path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                long length = 0;
                if (isDirectory)
                {
                    pending.Enqueue(path);
                }
                else
                {
                    length = new FileInfo(path).Length;
                    aggregateBytes = checked(aggregateBytes + length);
                    if (aggregateBytes > MaximumAggregateFileBytes)
                        throw new WidgetProcessAdmissionException(
                            $"Preview content exceeds the {MaximumAggregateFileBytes}-byte bound.");
                }
                entries.Add(new(Path.GetFullPath(path), isDirectory, length));
            }
        }
        return entries
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SafeFileHandle OpenPinned(string path, bool expectDirectory)
    {
        var handle = NativeMethods.CreateFile(
            path,
            GenericRead,
            FileShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | (expectDirectory ? FileFlagBackupSemantics : 0),
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new WidgetProcessAdmissionException(
                "Preview content could not be pinned.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if (!NativeMethods.GetFileAttributeTagInfo(
                handle, FileAttributeTagInfoClass, out var information,
                Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()) ||
            (information.FileAttributes & FileAttributes.ReparsePoint) != 0 ||
            ((information.FileAttributes & FileAttributes.Directory) != 0) != expectDirectory)
        {
            handle.Dispose();
            throw new WidgetProcessAdmissionException(
                "Preview content changed shape during exact admission.");
        }
        return handle;
    }

    private static AppContainerAuthorityObjectIdentity Identity(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileIdInfo(
                handle, FileIdInfoClass, out var information,
                Marshal.SizeOf<NativeMethods.FileIdInfo>()))
            throw new WidgetProcessAdmissionException(
                "Preview content object identity could not be captured.");
        return new AppContainerAuthorityObjectIdentity(
            information.VolumeSerialNumber,
            $"{information.FileId.Low:X16}{information.FileId.High:X16}");
    }

    private static bool IsReparse(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private readonly record struct Entry(string Path, bool IsDirectory, long Length);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAttributeTagInfo
        {
            internal FileAttributes FileAttributes;
            internal uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileId128
        {
            internal ulong Low;
            internal ulong High;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdInfo
        {
            internal ulong VolumeSerialNumber;
            internal FileId128 FileId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileAttributeTagInfo(
            SafeFileHandle file,
            int informationClass,
            out FileAttributeTagInfo information,
            int bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileIdInfo(
            SafeFileHandle file,
            int informationClass,
            out FileIdInfo information,
            int bufferSize);
    }
}
