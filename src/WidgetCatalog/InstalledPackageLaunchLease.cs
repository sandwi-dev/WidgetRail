using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WidgetCatalog;

internal readonly record struct InstalledPackageObjectIdentity(
    ulong VolumeSerialNumber,
    string FileId);

internal enum InstalledPackageContentTargetKind
{
    AuthorityRootDirectory,
    ReadOnlyDirectory,
    ReadOnlyFile,
}

internal readonly record struct InstalledPackageContentTarget(
    string Path,
    InstalledPackageContentTargetKind Kind,
    InstalledPackageObjectIdentity? ObjectIdentity);

/// <summary>
/// Pins the exact verified package files used by one worker session. Existing
/// bytes cannot be replaced or deleted while the lease is alive. On Windows,
/// each target carries identity evidence from its retained handle so callers can
/// grant authority only to those objects; later namespace additions receive no
/// runtime authority.
/// </summary>
internal sealed class InstalledPackageLaunchLease : IDisposable
{
    internal const int MaximumReadOnlyDirectories = 1_024;
    internal const int MaximumReadOnlyFiles = 1_024;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IReadOnlyList<FileStream> _files;
    private readonly IReadOnlyList<SafeFileHandle> _directories;
    private bool _disposed;

    private InstalledPackageLaunchLease(
        string packageRoot,
        string contentDigest,
        IReadOnlyList<InstalledPackageContentTarget> targets,
        IReadOnlyList<FileStream> files,
        IReadOnlyList<SafeFileHandle> directories)
    {
        PackageRoot = packageRoot;
        ContentDigest = contentDigest;
        Targets = targets;
        _files = files;
        _directories = directories;
    }

    internal string PackageRoot { get; }
    internal string ContentDigest { get; }
    internal IReadOnlyList<InstalledPackageContentTarget> Targets { get; }

    internal static InstalledPackageLaunchLease Acquire(
        string catalogRoot,
        InstalledWidgetVersion version,
        Action? checkpoint = null,
        Action? beforeFinalInventoryCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        ArgumentNullException.ThrowIfNull(version);
        version.VerificationOptions.Validate();
        if (version.VerifiedFiles.Count is < 2 ||
            version.VerifiedFiles.Count > version.VerificationOptions.MaximumArchiveEntries ||
            version.VerifiedFiles.Count > MaximumReadOnlyFiles)
            throw AdmissionFailure();

        var fullCatalogRoot = Path.GetFullPath(catalogRoot);
        var packageRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(version.InstallPath));
        if (!FileSystemSafety.IsWithin(fullCatalogRoot, packageRoot))
            throw AdmissionFailure();

        var expected = version.VerifiedFiles.Values
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        ValidateExpectedInventory(expected, version.VerificationOptions);
        ValidateDirectoryBudget(expected.Select(file => file.RelativePath));
        FileSystemSafety.EnsureTreeContainsNoReparsePoints(
            fullCatalogRoot,
            packageRoot,
            version.VerificationOptions.MaximumInstalledEntries,
            checkpoint);
        RequireExactInventory(packageRoot, expected, checkpoint);

        var fileStreams = new List<FileStream>(expected.Length);
        var directoryHandles = new List<SafeFileHandle>();
        var objectIdentities = new Dictionary<string, InstalledPackageObjectIdentity>(
            StringComparer.OrdinalIgnoreCase);
        var directoryPaths = RequiredDirectories(packageRoot, expected);
        var filePaths = expected.Select(file => Resolve(packageRoot, file.RelativePath)).ToArray();
        try
        {
            foreach (var directory in directoryPaths)
            {
                checkpoint?.Invoke();
                FileSystemSafety.EnsureNoReparsePoints(fullCatalogRoot, directory);
                if (!Directory.Exists(directory)) throw AdmissionFailure();
                if (OperatingSystem.IsWindows())
                {
                    var handle = OpenPinnedHandle(directory, expectDirectory: true);
                    directoryHandles.Add(handle);
                    objectIdentities.Add(directory, GetObjectIdentity(handle));
                }
            }

            using var treeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            treeHash.AppendData(StrictUtf8.GetBytes(InstalledPackageIntegrity.Algorithm));
            Span<byte> integer = stackalloc byte[sizeof(long)];
            var buffer = new byte[64 * 1024];
            try
            {
                foreach (var file in expected)
                {
                    checkpoint?.Invoke();
                    var fullPath = Resolve(packageRoot, file.RelativePath);
                    FileSystemSafety.EnsureNoReparsePoints(fullCatalogRoot, fullPath);
                    FileStream stream;
                    if (OperatingSystem.IsWindows())
                    {
                        var handle = OpenPinnedHandle(fullPath, expectDirectory: false);
                        try
                        {
                            objectIdentities.Add(fullPath, GetObjectIdentity(handle));
                            stream = new FileStream(
                                handle, FileAccess.Read, buffer.Length, isAsync: false);
                        }
                        catch
                        {
                            handle.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        stream = new FileStream(
                            fullPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            buffer.Length,
                            FileOptions.SequentialScan);
                    }
                    fileStreams.Add(stream);
                    if (stream.Length != file.Length ||
                        file.Length < 0 ||
                        file.Length > version.VerificationOptions.MaximumEntryBytes)
                        throw AdmissionFailure();

                    var pathBytes = StrictUtf8.GetBytes(file.RelativePath);
                    BinaryPrimitives.WriteInt64BigEndian(integer, pathBytes.Length);
                    treeHash.AppendData(integer);
                    treeHash.AppendData(pathBytes);
                    BinaryPrimitives.WriteInt64BigEndian(integer, file.Length);
                    treeHash.AppendData(integer);
                    using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    try
                    {
                        BoundedFileReader.AppendExact(
                            treeHash, stream, file.Length, buffer, fileHash, checkpoint);
                    }
                    catch (InvalidDataException)
                    {
                        throw AdmissionFailure();
                    }
                    if (!FixedTimeDigestEquals(file.Sha256, fileHash.GetHashAndReset()))
                        throw AdmissionFailure();
                }
                if (!FixedTimeDigestEquals(
                        version.ContentDigest,
                        treeHash.GetHashAndReset()))
                    throw AdmissionFailure();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }

            checkpoint?.Invoke();
            beforeFinalInventoryCheck?.Invoke();
            RequireExactInventory(packageRoot, expected, checkpoint);
            return new InstalledPackageLaunchLease(
                packageRoot,
                version.ContentDigest,
                CreateTargets(packageRoot, directoryPaths, filePaths, objectIdentities),
                fileStreams,
                directoryHandles);
        }
        catch
        {
            foreach (var stream in fileStreams) stream.Dispose();
            foreach (var handle in directoryHandles) handle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var stream in _files) stream.Dispose();
        foreach (var handle in _directories) handle.Dispose();
    }

    private static void ValidateExpectedInventory(
        IReadOnlyList<VerifiedPackageFile> expected,
        WidgetCatalogOptions options)
    {
        long totalBytes = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in expected)
        {
            if (!paths.Add(file.RelativePath) ||
                file.RelativePath.Length is < 1 ||
                file.RelativePath.Length > options.MaximumPathLength ||
                file.RelativePath != file.RelativePath.Normalize(NormalizationForm.FormC) ||
                Path.IsPathRooted(file.RelativePath) ||
                file.RelativePath.Contains('\\') ||
                file.RelativePath.Split('/').Any(segment => segment is "" or "." or "..") ||
                file.Length < 0 || file.Length > options.MaximumEntryBytes ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw AdmissionFailure();
            totalBytes = checked(totalBytes + file.Length);
            if (totalBytes > options.MaximumTotalBytes) throw AdmissionFailure();
        }
    }

    private static void RequireExactInventory(
        string packageRoot,
        IReadOnlyList<VerifiedPackageFile> expected,
        Action? checkpoint)
    {
        var actual = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                checkpoint?.Invoke();
                return Path.GetRelativePath(packageRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
            })
            .Where(path => !string.Equals(
                path, InstalledPackageIntegrity.MetadataFileName, StringComparison.OrdinalIgnoreCase))
            .Take(expected.Count + 1)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (actual.Length != expected.Count ||
            !actual.SequenceEqual(expected.Select(file => file.RelativePath), StringComparer.Ordinal))
            throw AdmissionFailure();
    }

    private static string[] RequiredDirectories(
        string packageRoot,
        IReadOnlyList<VerifiedPackageFile> expected)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packageRoot };
        foreach (var file in expected)
        {
            var current = Path.GetDirectoryName(Resolve(packageRoot, file.RelativePath));
            while (current is not null && FileSystemSafety.IsWithin(packageRoot, current))
            {
                directories.Add(current);
                if (string.Equals(current, packageRoot, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }
        return directories
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static void ValidateDirectoryBudget(IEnumerable<string> relativePaths)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            string.Empty,
        };
        foreach (var relativePath in relativePaths)
        {
            var segments = relativePath.Split('/');
            for (var index = 1; index < segments.Length; index++)
            {
                directories.Add(string.Join('/', segments.Take(index)));
                if (directories.Count > MaximumReadOnlyDirectories)
                    throw new WidgetPackageException(
                        "too_many_launch_directories",
                        $"Package requires more than {MaximumReadOnlyDirectories} verified-content directories.");
            }
        }
    }

    private static string Resolve(string packageRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar), packageRoot);
        if (!FileSystemSafety.IsWithin(packageRoot, fullPath) ||
            string.Equals(packageRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            throw AdmissionFailure();
        return fullPath;
    }

    private static bool FixedTimeDigestEquals(string expectedText, byte[] actual)
    {
        byte[] expected;
        try { expected = Convert.FromHexString(expectedText); }
        catch (FormatException) { return false; }
        try
        {
            return expected.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static WidgetPackageException AdmissionFailure() => new(
        "package_launch_integrity",
        "Installed widget content changed before its worker could start.");

    private static IReadOnlyList<InstalledPackageContentTarget> CreateTargets(
        string packageRoot,
        IReadOnlyList<string> directoryPaths,
        IReadOnlyList<string> filePaths,
        IReadOnlyDictionary<string, InstalledPackageObjectIdentity> objectIdentities)
    {
        InstalledPackageObjectIdentity? Identity(string path) =>
            objectIdentities.TryGetValue(path, out var identity) ? identity : null;
        return new[]
            {
                new InstalledPackageContentTarget(
                    packageRoot,
                    InstalledPackageContentTargetKind.AuthorityRootDirectory,
                    Identity(packageRoot)),
            }
            .Concat(directoryPaths
                .Where(path => !string.Equals(
                    path, packageRoot, StringComparison.OrdinalIgnoreCase))
                .Select(path => new InstalledPackageContentTarget(
                    path,
                    InstalledPackageContentTargetKind.ReadOnlyDirectory,
                    Identity(path))))
            .Concat(filePaths.Select(path => new InstalledPackageContentTarget(
                path,
                InstalledPackageContentTargetKind.ReadOnlyFile,
                Identity(path))))
            .ToArray();
    }

    private static SafeFileHandle OpenPinnedHandle(string path, bool expectDirectory)
    {
        var handle = CreateFile(
            path,
            GenericRead,
            (uint)FileShare.Read,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint |
                (expectDirectory ? FileFlagBackupSemantics : 0),
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw AdmissionFailure();
        }
        try
        {
            if (!GetFileAttributeTagInfo(
                    handle,
                    FileAttributeTagInfoClass,
                    out var information,
                    Marshal.SizeOf<FileAttributeTagInfo>()))
                throw AdmissionFailure();
            var isDirectory = (information.FileAttributes & FileAttributes.Directory) != 0;
            if ((information.FileAttributes & FileAttributes.ReparsePoint) != 0 ||
                isDirectory != expectDirectory)
                throw AdmissionFailure();
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static InstalledPackageObjectIdentity GetObjectIdentity(SafeFileHandle handle)
    {
        if (!GetFileIdInfo(
                handle,
                FileIdInfoClass,
                out var information,
                Marshal.SizeOf<FileIdInfo>()))
            throw AdmissionFailure();
        return new InstalledPackageObjectIdentity(
            information.VolumeSerialNumber,
            $"{information.FileId.Low:X16}{information.FileId.High:X16}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal FileAttributes FileAttributes;
        internal uint ReparseTag;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
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
    private static extern bool GetFileAttributeTagInfo(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInfo information,
        int bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileIdInfo(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo information,
        int bufferSize);
}
