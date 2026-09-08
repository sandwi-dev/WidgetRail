using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

public sealed class WidgetPackageInstaller
{
    private readonly string _root;
    private readonly string _packagesRoot;
    private readonly string _stagingRoot;
    private readonly WidgetCatalogOptions _options;

    internal WidgetPackageInstaller(string root, WidgetCatalogOptions options)
    {
        _root = root;
        _packagesRoot = Path.Combine(root, "packages");
        _stagingRoot = Path.Combine(root, "staging");
        _options = options;
    }

    public Task<WidgetPackageInspection> ValidateAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = OpenArchive(packagePath);
        var plan = InspectArchiveSafe(archive, cancellationToken);
        return Task.FromResult(plan.Inspection);
    }

    public Task<WidgetPackageInspection> ValidateAsync(
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = OpenArchive(packageStream);
        var plan = InspectArchiveSafe(archive, cancellationToken);
        return Task.FromResult(plan.Inspection);
    }

    internal async Task<InstalledWidgetVersion> InstallAsync(
        string packagePath,
        Func<WidgetPackageInspection, CancellationToken, Task> prePublish,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(prePublish);
        using var archive = OpenArchive(packagePath);
        return await InstallArchiveAsync(archive, prePublish, cancellationToken);
    }

    /// <summary>
    /// Validates one locked stream, runs a policy decision against that exact
    /// inspection, and publishes the same bytes only when the policy succeeds.
    /// </summary>
    internal async Task<InstalledWidgetVersion> InstallAsync(
        Stream packageStream,
        Func<WidgetPackageInspection, CancellationToken, Task> prePublish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(prePublish);
        using var archive = OpenArchive(packageStream);
        return await InstallArchiveAsync(archive, prePublish, cancellationToken);
    }

    private async Task<InstalledWidgetVersion> InstallArchiveAsync(
        ZipArchive archive,
        Func<WidgetPackageInspection, CancellationToken, Task>? prePublish,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_packagesRoot);
        Directory.CreateDirectory(_stagingRoot);
        FileSystemSafety.EnsureNoReparsePoints(_root, _root);
        FileSystemSafety.EnsureNoReparsePoints(_root, _packagesRoot);
        FileSystemSafety.EnsureNoReparsePoints(_root, _stagingRoot);

        var stage = Path.Combine(_stagingRoot, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            var plan = InspectArchiveSafe(archive, cancellationToken);
            try
            {
                await ExtractAsync(plan, stage, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                throw new WidgetPackageException(
                    "invalid_archive", "Package content is not a valid ZIP archive.", exception);
            }

            FileSystemSafety.EnsureTreeContainsNoReparsePoints(_root, stage);
            var stagedManifestPath = Path.Combine(stage, "manifest.json");
            var stagedManifest = ReadAndValidateManifest(await File.ReadAllBytesAsync(stagedManifestPath, cancellationToken));
            if (!ManifestJson.Serialize(stagedManifest).AsSpan().SequenceEqual(
                    ManifestJson.Serialize(plan.Inspection.Manifest)))
                throw new WidgetPackageException(
                    "package_changed", "Package manifest changed between archive inspection and staging.");

            var stagedInspection = plan.Inspection with { Manifest = stagedManifest };
            if (prePublish is not null)
                await prePublish(stagedInspection, cancellationToken);

            var contentDigest = InstalledPackageIntegrity.Seal(
                _root, stage, _options);

            var idParent = Path.Combine(_packagesRoot, stagedInspection.Id);
            Directory.CreateDirectory(idParent);
            FileSystemSafety.EnsureNoReparsePoints(_root, idParent);
            var destination = Path.Combine(idParent, stagedInspection.Manifest.Version);
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new WidgetPackageException(
                    "version_already_installed",
                    $"{stagedInspection.Id} {stagedInspection.Manifest.Version} is already installed; installed versions are immutable.");

            Directory.Move(stage, destination);
            return new InstalledWidgetVersion(
                stagedInspection.Id,
                stagedInspection.Version,
                destination,
                stagedManifest,
                contentDigest);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private ZipArchive OpenArchive(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
            throw new WidgetPackageException("package_not_found", $"Package does not exist: {fullPath}");
        if (!Path.GetExtension(fullPath).Equals(".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new WidgetPackageException("invalid_extension", "Widget packages must use the .wrwidget extension.");
        try
        {
            return ZipFile.OpenRead(fullPath);
        }
        catch (InvalidDataException exception)
        {
            throw new WidgetPackageException("invalid_archive", "Package is not a valid ZIP archive.", exception);
        }
    }

    private static ZipArchive OpenArchive(Stream packageStream)
    {
        if (!packageStream.CanRead || !packageStream.CanSeek)
            throw new WidgetPackageException(
                "invalid_archive_stream", "Widget package streams must be readable and seekable.");
        try
        {
            packageStream.Position = 0;
            return new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new WidgetPackageException("invalid_archive", "Package is not a valid ZIP archive.", exception);
        }
    }

    private PackagePlan InspectArchive(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > _options.MaximumArchiveEntries)
            throw new WidgetPackageException("too_many_entries", $"Package exceeds {_options.MaximumArchiveEntries} entries.");

        var knownPaths = new Dictionary<string, PlannedPath>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PlannedEntry>();
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ValidateEntryPath(entry.FullName, out var isDirectory);
            ValidateEntryType(entry, isDirectory);

            if (entry.Length < 0 || entry.Length > _options.MaximumEntryBytes)
                throw new WidgetPackageException("entry_too_large", $"'{path}' exceeds the per-entry size limit.");
            try
            {
                totalBytes = checked(totalBytes + entry.Length);
            }
            catch (OverflowException exception)
            {
                throw new WidgetPackageException("package_too_large", "Package size metadata overflowed.", exception);
            }
            if (totalBytes > _options.MaximumTotalBytes)
                throw new WidgetPackageException("package_too_large", $"Package exceeds the {_options.MaximumTotalBytes}-byte expanded-size limit.");

            RegisterPath(path, isDirectory, knownPaths);
            entries.Add(new PlannedEntry(entry, path, isDirectory));
        }
        InstalledPackageLaunchLease.ValidateDirectoryBudget(
            entries.Where(entry => !entry.IsDirectory)
                .Select(entry => entry.RelativePath));

        var manifestEntry = entries.SingleOrDefault(item =>
            string.Equals(item.RelativePath, "manifest.json", StringComparison.Ordinal) && !item.IsDirectory);
        if (manifestEntry is null)
            throw new WidgetPackageException("missing_manifest", "Package must contain one root-level manifest.json using exact casing.");

        WidgetManifest manifest;
        try
        {
            manifest = ReadAndValidateManifest(ReadEntryBounded(manifestEntry.Entry, _options.MaximumEntryBytes, cancellationToken));
        }
        catch (JsonException exception)
        {
            throw new WidgetPackageException("invalid_manifest", $"Manifest JSON is invalid: {exception.Message}", exception);
        }
        if (!manifest.Id.Equals(manifest.Publisher, StringComparison.Ordinal) &&
            !manifest.Id.StartsWith(manifest.Publisher + ".", StringComparison.Ordinal))
            throw new WidgetPackageException("identity_mismatch", "Manifest ID must be owned by its publisher namespace.");
        if (!Version.TryParse(manifest.Version, out var version) ||
            !string.Equals(version.ToString(), manifest.Version, StringComparison.Ordinal))
            throw new WidgetPackageException("invalid_manifest", "Manifest version must use a deterministic dotted numeric representation.");
        var entrypointPath = WidgetEntrypointRuntimes.ResolvePackagePath(
            manifest.Entrypoint);
        if (!knownPaths.TryGetValue(entrypointPath, out var packagedEntrypoint) ||
            packagedEntrypoint.IsDirectory ||
            !string.Equals(
                packagedEntrypoint.CanonicalPath, entrypointPath, StringComparison.Ordinal))
            throw new WidgetPackageException(
                "missing_entrypoint",
                $"Entrypoint is missing or has different casing: {entrypointPath}");

        long iconBytes = 0;
        foreach (var pair in (manifest.IconAssets ??
                     new Dictionary<string, WidgetPackageIconAsset>(StringComparer.Ordinal))
                 .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!knownPaths.TryGetValue(pair.Value.Path, out var packagedIcon) ||
                packagedIcon.IsDirectory ||
                !string.Equals(
                    packagedIcon.CanonicalPath, pair.Value.Path, StringComparison.Ordinal))
                throw new WidgetPackageException(
                    "missing_icon_asset",
                    $"Icon asset '{pair.Key}' is missing or has different casing: {pair.Value.Path}");
            var plannedIcon = entries.Single(entry =>
                string.Equals(entry.RelativePath, pair.Value.Path, StringComparison.Ordinal));
            if (plannedIcon.Entry.Length is < 1 or > ProtocolConstants.MaximumPackageIconBytes)
                throw new WidgetPackageException(
                    "icon_asset_too_large",
                    $"Icon asset '{pair.Key}' exceeds {ProtocolConstants.MaximumPackageIconBytes} bytes.");
            iconBytes = checked(iconBytes + plannedIcon.Entry.Length);
            if (iconBytes > ProtocolConstants.MaximumPackageIconAggregateBytes)
                throw new WidgetPackageException(
                    "icon_assets_too_large",
                    $"Declared icon assets exceed {ProtocolConstants.MaximumPackageIconAggregateBytes} bytes.");
            _ = SvgIconNormalizer.Normalize(ReadEntryBounded(
                plannedIcon.Entry, ProtocolConstants.MaximumPackageIconBytes, cancellationToken));
        }

        return new PackagePlan(
            entries,
            new WidgetPackageInspection(manifest.Id, version, manifest, entries.Count, totalBytes));
    }

    private PackagePlan InspectArchiveSafe(ZipArchive archive, CancellationToken cancellationToken)
    {
        try
        {
            return InspectArchive(archive, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new WidgetPackageException("invalid_archive", "Package is not a valid ZIP archive.", exception);
        }
    }

    private WidgetManifest ReadAndValidateManifest(byte[] payload)
    {
        var manifest = ManifestJson.Deserialize(payload);
        var errors = WidgetManifestValidator.Validate(manifest);
        if (errors.Count != 0)
            throw new WidgetPackageException("invalid_manifest", $"{errors[0].Path}: {errors[0].Message}");
        return manifest;
    }

    private async Task ExtractAsync(PackagePlan plan, string stage, CancellationToken cancellationToken)
    {
        long totalWritten = 0;
        foreach (var planned in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(stage, planned.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!FileSystemSafety.IsWithin(stage, destination))
                throw new WidgetPackageException("path_escape", $"Entry escaped staging: {planned.RelativePath}");

            if (planned.IsDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = planned.Entry.Open();
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            long entryWritten = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                entryWritten += read;
                totalWritten += read;
                if (entryWritten > _options.MaximumEntryBytes || totalWritten > _options.MaximumTotalBytes)
                    throw new WidgetPackageException("package_too_large", "Expanded package exceeded its declared safety limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (entryWritten != planned.Entry.Length)
                throw new WidgetPackageException("length_mismatch", $"Expanded length for '{planned.RelativePath}' did not match archive metadata.");
        }
    }

    private string ValidateEntryPath(string value, out bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains('\0') ||
            value.Length > _options.MaximumPathLength || value != value.Normalize(NormalizationForm.FormC))
            throw new WidgetPackageException("invalid_path", $"Archive entry path is invalid or ambiguous: '{value}'.");
        isDirectory = value.EndsWith("/", StringComparison.Ordinal);
        var path = isDirectory ? value[..^1] : value;
        if (string.Equals(
                path, InstalledPackageIntegrity.MetadataFileName,
                StringComparison.OrdinalIgnoreCase))
            throw new WidgetPackageException(
                "reserved_path", "Package contains a host-reserved integrity path.");
        if (path.Length == 0 || path.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw new WidgetPackageException("invalid_path", $"Archive entry path is not relative: '{value}'.");

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(ch => ch < 32 || "<>:\"|?*".Contains(ch)) || IsReservedWindowsName(segment))
                throw new WidgetPackageException("invalid_path", $"Archive entry contains an unsafe Windows path segment: '{value}'.");
        }
        return path;
    }

    private static void ValidateEntryType(ZipArchiveEntry entry, bool isDirectory)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        var expectedUnixType = isDirectory ? 0x4000 : 0x8000;
        if (unixType != 0 && unixType != expectedUnixType)
            throw new WidgetPackageException("unsupported_entry_type", $"Links and special archive entries are not allowed: {entry.FullName}");
        if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException("unsupported_entry_type", $"Reparse-point entries are not allowed: {entry.FullName}");
        if (isDirectory && entry.Length != 0)
            throw new WidgetPackageException("invalid_directory", $"Directory entry has content: {entry.FullName}");
    }

    private static void RegisterPath(string path, bool isDirectory, Dictionary<string, PlannedPath> knownPaths)
    {
        var segments = path.Split('/');
        for (var index = 1; index < segments.Length; index++)
        {
            var parent = string.Join('/', segments.Take(index));
            if (knownPaths.TryGetValue(parent, out var existingParent))
            {
                if (!string.Equals(existingParent.CanonicalPath, parent, StringComparison.Ordinal))
                    throw new WidgetPackageException("path_collision", $"Case-colliding archive paths: '{parent}' and '{existingParent.CanonicalPath}'.");
                if (!existingParent.IsDirectory)
                    throw new WidgetPackageException("path_collision", $"File '{existingParent.CanonicalPath}' is also used as a directory.");
            }
            else
            {
                knownPaths[parent] = new PlannedPath(parent, IsDirectory: true, IsExplicit: false);
            }
        }

        if (knownPaths.TryGetValue(path, out var existing))
        {
            if (!string.Equals(existing.CanonicalPath, path, StringComparison.Ordinal))
                throw new WidgetPackageException("path_collision", $"Case-colliding archive paths: '{path}' and '{existing.CanonicalPath}'.");
            if (existing.IsExplicit || !isDirectory || !existing.IsDirectory)
                throw new WidgetPackageException("path_collision", $"Duplicate or case-colliding archive path: '{path}' and '{existing.CanonicalPath}'.");
            knownPaths[path] = existing with { IsExplicit = true };
        }
        else
        {
            knownPaths[path] = new PlannedPath(path, isDirectory, IsExplicit: true);
        }
    }

    private static byte[] ReadEntryBounded(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, maximumBytes));
        var buffer = new byte[16 * 1024];
        long written = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer);
            if (read == 0) break;
            written += read;
            if (written > maximumBytes)
                throw new WidgetPackageException("entry_too_large", $"'{entry.FullName}' exceeds the entry size limit.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                     stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                stem[3] is >= '1' and <= '9');
    }

    private sealed record PlannedPath(string CanonicalPath, bool IsDirectory, bool IsExplicit);
    private sealed record PlannedEntry(ZipArchiveEntry Entry, string RelativePath, bool IsDirectory);
    private sealed record PackagePlan(IReadOnlyList<PlannedEntry> Entries, WidgetPackageInspection Inspection);
}
