using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.LauncherExperienceCatalog;

internal sealed record LauncherExperienceSourceFile(string RelativePath, byte[] Content);

internal sealed record LauncherExperienceArchiveInspection(
    LauncherExperiencePackage Package,
    IReadOnlyList<LauncherExperienceSourceFile> Files,
    long TotalBytes);

internal sealed record LauncherExperiencePackResult(
    string ArchivePath,
    string Sha256,
    LauncherExperienceArchiveInspection Inspection);

/// <summary>
/// Owns the immutable archive and installed-catalog boundary for data-only
/// Launcher Experience packages. Package semantics remain owned by
/// <see cref="LauncherExperienceValidator"/>.
/// </summary>
internal static class LauncherExperienceArchive
{
    public const string Extension = ".gbarlauncher";
    public const long MaximumArchiveBytes = 33L * 1024 * 1024;
    private const int MaximumArchiveEntries = LauncherExperienceValidator.MaximumFiles;
    private static readonly DateTimeOffset ReproducibleTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static LauncherExperienceArchiveInspection InspectDirectory(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        var validated = new LauncherExperienceValidator().ValidateDirectory(source);
        var package = RequireValid(validated);
        var files = package.Files.Select(relative =>
                new LauncherExperienceSourceFile(
                    relative,
                    LauncherExperienceFileGuard.ReadBounded(
                        Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar)),
                        LauncherExperienceValidator.MaximumAssetBytes)))
            .ToArray();
        var total = files.Sum(file => (long)file.Content.Length);
        return new(package, Array.AsReadOnly(files), total);
    }

    public static async Task<LauncherExperiencePackResult> PackAsync(
        string sourcePath,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = InspectDirectory(sourcePath);
        var output = Path.GetFullPath(outputPath ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(sourcePath))!,
            $"{inspection.Package.Manifest.Id}-{inspection.Package.Manifest.Version}{Extension}"));
        if (!Path.GetExtension(output).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            throw new LauncherExperiencePackageException(
                "invalid_extension", $"Launcher Experience archives must use {Extension}.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new LauncherExperiencePackageException(
                "immutable_archive", "The output archive already exists and will not be overwritten.");
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        LauncherExperienceFileGuard.RejectReparsePoint(parent);
        var temporary = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var source in inspection.Files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(source.RelativePath, CompressionLevel.NoCompression);
                    entry.LastWriteTime = ReproducibleTimestamp;
                    entry.ExternalAttributes = unchecked((int)(0x81A4u << 16));
                    await using var destination = entry.Open();
                    await destination.WriteAsync(source.Content, cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(temporary, output);
            await using var input = new FileStream(
                output, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
                .ToLowerInvariant();
            return new(output, hash, inspection);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<LauncherExperienceArchiveInspection> InspectArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(archivePath);
        if (!File.Exists(path))
            throw new LauncherExperiencePackageException(
                "archive_not_found", "Launcher Experience archive does not exist.");
        if (!Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            throw new LauncherExperiencePackageException(
                "invalid_extension", $"Launcher Experience archives must use {Extension}.");
        LauncherExperienceFileGuard.RejectReparsePoint(path);
        await using var input = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await InspectArchiveAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<LauncherExperienceArchiveInspection> InspectArchiveAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var captured = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long archiveBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            archiveBytes = checked(archiveBytes + read);
            if (archiveBytes > MaximumArchiveBytes)
                throw new LauncherExperiencePackageException(
                    "archive_too_large", $"Launcher Experience archive exceeds {MaximumArchiveBytes} bytes.");
            captured.Write(buffer, 0, read);
        }
        captured.Position = 0;

        IReadOnlyList<LauncherExperienceSourceFile> files;
        try
        {
            using var archive = new ZipArchive(captured, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaximumArchiveEntries)
                throw new LauncherExperiencePackageException(
                    "too_many_entries", $"Launcher Experience archive exceeds {MaximumArchiveEntries} entries.");
            var known = new Dictionary<string, (string Canonical, bool IsFile)>(StringComparer.OrdinalIgnoreCase);
            var capturedFiles = new List<LauncherExperienceSourceFile>(archive.Entries.Count);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveEntry(entry, known);
                if (known.Count(item => !item.Value.IsFile) >
                    LauncherExperienceValidator.MaximumDirectories)
                    throw new LauncherExperiencePackageException(
                        "too_many_directories",
                        $"Package may contain at most {LauncherExperienceValidator.MaximumDirectories} directories.");
                var content = await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false);
                expanded = checked(expanded + content.LongLength);
                if (expanded > LauncherExperienceValidator.MaximumExpandedBytes)
                    throw new LauncherExperiencePackageException(
                        "package_too_large",
                        $"Expanded package exceeds {LauncherExperienceValidator.MaximumExpandedBytes} bytes.");
                capturedFiles.Add(new(entry.FullName, content));
            }
            files = new ReadOnlyCollection<LauncherExperienceSourceFile>(capturedFiles);
        }
        catch (InvalidDataException exception)
        {
            throw new LauncherExperiencePackageException(
                "invalid_archive", "Launcher Experience archive is malformed.", exception);
        }

        var temporary = Path.Combine(
            Path.GetTempPath(), "gbar-launcher-inspect", Guid.NewGuid().ToString("N"));
        try
        {
            Materialize(temporary, files);
            var package = RequireValid(new LauncherExperienceValidator().ValidateDirectory(temporary));
            return new(package, files, files.Sum(file => (long)file.Content.Length));
        }
        finally
        {
            TryDeleteTree(temporary);
        }
    }

    public static async Task<string> InstallAsync(
        LauncherExperienceArchiveInspection inspection,
        string catalogRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var root = Path.GetFullPath(catalogRoot);
        Directory.CreateDirectory(root);
        LauncherExperienceFileGuard.RejectReparsePoint(root);
        await using var mutation = await AcquireMutationLockAsync(root, cancellationToken).ConfigureAwait(false);
        var manifest = inspection.Package.Manifest;
        var destination = Path.Combine(root, manifest.Id, manifest.Version.ToString());
        if (!LauncherExperienceFileGuard.IsWithin(root, destination))
            throw new LauncherExperiencePackageException("path_escape", "Installed path escapes the catalog root.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new LauncherExperiencePackageException(
                "immutable_version", "That Launcher Experience ID/version is already installed and cannot be overwritten.");
        var snapshot = new LauncherExperienceCatalog(root).Discover();
        if (snapshot.Experiences.Count(item => !item.Descriptor.IsBuiltIn) >=
            LauncherExperienceCatalog.MaximumInstalledVersions)
            throw new LauncherExperiencePackageException(
                "too_many_experiences",
                $"Launcher experience catalog may contain at most {LauncherExperienceCatalog.MaximumInstalledVersions} installed versions.");

        var staging = Path.Combine(
            Path.GetDirectoryName(root)!, $".{Path.GetFileName(root)}.launcher-staging.{Guid.NewGuid():N}");
        try
        {
            Materialize(staging, inspection.Files);
            RequireValid(new LauncherExperienceValidator().ValidateDirectory(
                staging, manifest.Id, manifest.Version.ToString()));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            TryDeleteTree(staging);
        }
    }

    public static async Task RemoveAsync(
        string catalogRoot,
        string id,
        string version,
        CancellationToken cancellationToken)
    {
        if (!LauncherExperienceIdentity.IsValidId(id) ||
            !LauncherExperienceIdentity.TryParseCanonicalVersion(version, out _))
            throw new LauncherExperiencePackageException(
                "invalid_identity", "Launcher Experience ID and version must be exact canonical values.");
        if (id.StartsWith(LauncherExperienceBuiltIns.Publisher + ".", StringComparison.Ordinal))
            throw new LauncherExperiencePackageException(
                "built_in_protected", "Built-in recovery experiences cannot be removed.");
        var root = Path.GetFullPath(catalogRoot);
        if (!Directory.Exists(root))
            throw new LauncherExperiencePackageException("experience_not_found", "Launcher Experience is not installed.");
        LauncherExperienceFileGuard.RejectReparsePoint(root);
        await using var mutation = await AcquireMutationLockAsync(root, cancellationToken).ConfigureAwait(false);
        var destination = Path.Combine(root, id, version);
        if (!LauncherExperienceFileGuard.IsWithin(root, destination) || !Directory.Exists(destination))
            throw new LauncherExperiencePackageException("experience_not_found", "Launcher Experience is not installed.");
        RequireValid(new LauncherExperienceValidator().ValidateDirectory(destination, id, version));
        Directory.Delete(destination, recursive: true);
        var idDirectory = Path.GetDirectoryName(destination)!;
        if (Directory.Exists(idDirectory) && !Directory.EnumerateFileSystemEntries(idDirectory).Any())
            Directory.Delete(idDirectory);
    }

    public static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static LauncherExperiencePackage RequireValid(LauncherExperienceValidationResult validation)
    {
        if (validation.Package is { } package && validation.Diagnostics.Count == 0) return package;
        var first = validation.Diagnostics.FirstOrDefault() ??
                    new LauncherExperienceDiagnostic("$", "invalid_package", "Launcher Experience package is invalid.");
        throw new LauncherExperiencePackageException(first.Code, first.Message, diagnosticPath: first.Path);
    }

    private static void ValidateArchiveEntry(
        ZipArchiveEntry entry,
        Dictionary<string, (string Canonical, bool IsFile)> known)
    {
        if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
            throw new LauncherExperiencePackageException(
                "directory_entry", "Launcher Experience archives must not contain explicit directory entries.");
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType != 0 && unixType != 0x8000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new LauncherExperiencePackageException(
                "unsupported_entry_type", "Links and special archive entries are not allowed.",
                diagnosticPath: entry.FullName);
        ValidateRelativePath(entry.FullName);
        foreach (var segmentEnd in ParentPaths(entry.FullName))
        {
            if (known.TryGetValue(segmentEnd, out var parent))
            {
                if (parent.IsFile || !string.Equals(parent.Canonical, segmentEnd, StringComparison.Ordinal))
                    throw new LauncherExperiencePackageException(
                        "path_collision", "Archive contains file/directory or case-colliding paths.",
                        diagnosticPath: entry.FullName);
            }
            else
            {
                known.Add(segmentEnd, (segmentEnd, false));
            }
        }
        if (!known.TryAdd(entry.FullName, (entry.FullName, true)))
            throw new LauncherExperiencePackageException(
                "path_collision", "Archive contains duplicate or case-colliding paths.",
                diagnosticPath: entry.FullName);
    }

    private static IEnumerable<string> ParentPaths(string path)
    {
        var segments = path.Split('/');
        for (var index = 1; index < segments.Length; index++)
            yield return string.Join('/', segments.Take(index));
    }

    private static void ValidateRelativePath(string path)
    {
        if (!LauncherExperienceFileGuard.IsSafePackagePath(path) ||
            path != path.Normalize(NormalizationForm.FormC))
            throw new LauncherExperiencePackageException(
                "invalid_path", "Archive path is invalid or ambiguous.", diagnosticPath: path);
        foreach (var segment in path.Split('/'))
        {
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(character => character < 32 || "<>\"|?*".Contains(character)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                stem[3] is >= '1' and <= '9')
                throw new LauncherExperiencePackageException(
                    "invalid_path", "Archive path contains an unsafe Windows segment.", diagnosticPath: path);
        }
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Length > LauncherExperienceValidator.MaximumAssetBytes)
            throw new LauncherExperiencePackageException(
                "file_too_large", "Archive entry exceeds the package file limit.",
                diagnosticPath: entry.FullName);
        await using var source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > LauncherExperienceValidator.MaximumAssetBytes)
                throw new LauncherExperiencePackageException(
                    "file_too_large", "Archive entry exceeds the package file limit.",
                    diagnosticPath: entry.FullName);
            destination.Write(buffer, 0, read);
        }
        if (total != entry.Length)
            throw new LauncherExperiencePackageException(
                "entry_changed", "Archive entry length changed while it was read.",
                diagnosticPath: entry.FullName);
        return destination.ToArray();
    }

    private static void Materialize(
        string root,
        IReadOnlyList<LauncherExperienceSourceFile> files)
    {
        Directory.CreateDirectory(root);
        foreach (var file in files)
        {
            var destination = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!LauncherExperienceFileGuard.IsWithin(root, destination))
                throw new LauncherExperiencePackageException("path_escape", "Archive path escapes its root.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, file.Content);
        }
    }

    private static async Task<FileStream> AcquireMutationLockAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(root, ".launcher-experience.lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
