using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.PackageDistribution;

internal sealed class ThemePackageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed record ThemePackageInspection(
    ThemeManifestDocument Manifest,
    IReadOnlyList<ThemeSourceFile> Files,
    int EntryCount,
    long TotalBytes,
    WrssPackageResult VerifiedStyles);

internal sealed record ThemeSourceFile(string RelativePath, byte[] Content);
internal sealed record ThemePackResult(string PackagePath, string Sha256, ThemePackageInspection Inspection);

internal static class ThemePackage
{
    public const string ManifestName = "theme.json";
    public const int MaximumEntries = 65;
    public const int MaximumPathLength = 240;
    public const long MaximumEntryBytes = WrssLimits.MaximumSourceBytes;
    public const long MaximumTotalBytes = 4L * 1024 * 1024;
    public const long MaximumArchiveBytes = 4L * 1024 * 1024;
    private static readonly DateTimeOffset ReproducibleTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<ThemePackageInspection> InspectSourceAsync(string source, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(source);
        if (Directory.Exists(path))
            return await ValidateAsync(await CaptureDirectoryAsync(path, excludedOutput: null, cancellationToken), cancellationToken);
        if (!File.Exists(path)) throw new DistributionUsageException($"Theme source does not exist: {path}");
        return await InspectArchiveAsync(path, cancellationToken);
    }

    public static async Task<ThemePackResult> PackAsync(
        string source, string? output, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(root)) throw new DistributionUsageException($"Theme directory does not exist: {root}");
        EnsureExistingPathHasNoReparsePoints(root);
        var requestedOutput = string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
        if (requestedOutput is not null &&
            !Path.GetExtension(requestedOutput).Equals(".wrtheme", StringComparison.OrdinalIgnoreCase))
            throw new DistributionUsageException("Theme package output must use the .wrtheme extension.");
        var files = await CaptureDirectoryAsync(root, requestedOutput, cancellationToken);
        var inspection = await ValidateAsync(files, cancellationToken);
        var destination = requestedOutput ?? Path.Combine(
            Directory.GetParent(root)?.FullName ?? root,
            $"{inspection.Manifest.Id}-{inspection.Manifest.Version}.wrtheme");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(destination)) throw new DistributionUsageException($"Theme package output is a directory: {destination}");
        RejectReparsePointIfPresent(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.wrtheme");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = ReproducibleTimestamp;
                    entry.ExternalAttributes = unchecked((int)(0x81A4u << 16));
                    await using var stream = entry.Open();
                    await stream.WriteAsync(file.Content, cancellationToken);
                }
            }
            var archiveInspection = await InspectArchiveAsync(temporary, cancellationToken);
            var sha256 = Convert.ToHexString(await PackageIntegrity.HashFileAsync(temporary, cancellationToken))
                .ToLowerInvariant();
            File.Move(temporary, destination, overwrite: true);
            return new ThemePackResult(destination, sha256, archiveInspection);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<ThemePackageInspection> InspectArchiveAsync(
        string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".wrtheme", StringComparison.OrdinalIgnoreCase))
            throw new DistributionUsageException("Theme packages must use the .wrtheme extension.");
        var info = new FileInfo(full);
        if (!info.Exists) throw new DistributionUsageException($"Theme package does not exist: {full}");
        if (info.Length > MaximumArchiveBytes)
            throw new ThemePackageException("archive_too_large", $"Theme archive exceeds {MaximumArchiveBytes} bytes.");
        RejectReparsePointIfPresent(full);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await InspectArchiveAsync(stream, cancellationToken);
    }

    public static async Task<ThemePackageInspection> InspectArchiveAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ThemePackageException("invalid_archive", "Theme package stream must be readable and seekable.");
        if (stream.Length > MaximumArchiveBytes)
            throw new ThemePackageException("archive_too_large", $"Theme archive exceeds {MaximumArchiveBytes} bytes.");
        stream.Position = 0;
        var files = new List<ThemeSourceFile>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaximumEntries)
                throw new ThemePackageException("too_many_entries", $"Theme archive exceeds {MaximumEntries} entries.");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveEntry(entry, seen);
                if (entry.Length > MaximumEntryBytes)
                    throw new ThemePackageException("entry_too_large", $"'{entry.FullName}' exceeds {MaximumEntryBytes} bytes.");
                var content = await ReadBoundedAsync(entry.Open(), entry.FullName, MaximumEntryBytes, cancellationToken);
                total = checked(total + content.LongLength);
                if (total > MaximumTotalBytes)
                    throw new ThemePackageException("package_too_large", $"Theme exceeds {MaximumTotalBytes} expanded bytes.");
                files.Add(new ThemeSourceFile(entry.FullName, content));
            }
        }
        catch (InvalidDataException exception)
        {
            throw new ThemePackageException("invalid_archive", "Theme package is not a valid ZIP archive.", exception);
        }
        stream.Position = 0;
        return await ValidateAsync(files, cancellationToken);
    }

    public static async Task<string> InstallAsync(
        ThemePackageInspection inspection, string settingsRoot, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settingsRoot));
        EnsureExistingPathHasNoReparsePoints(root);
        Directory.CreateDirectory(root);
        EnsureExistingPathHasNoReparsePoints(root);
        var lockPath = Path.Combine(root, ThemeCatalogMutationPolicy.LockFileName);
        RejectReparsePointIfPresent(lockPath);
        await using var installLock = await AcquireLockAsync(lockPath, cancellationToken);
        var themes = Path.Combine(root, "themes");
        Directory.CreateDirectory(themes);
        RejectReparsePointIfPresent(themes);
        var destinationId = Path.Combine(themes, inspection.Manifest.Id);
        var destination = Path.Combine(destinationId, inspection.Manifest.Version);
        if (!IsWithin(themes, destination))
            throw new ThemePackageException("path_escape", "Theme install path escapes the theme catalog.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new ThemePackageException(
                "version_exists", $"Theme {inspection.Manifest.Id} {inspection.Manifest.Version} is already installed.");
        var existing = new ThemeCatalog(new PlatformSettingsPaths(root)).Discover().Themes
            .Count(theme => !theme.Descriptor.IsBuiltIn);
        if (existing >= ThemeCatalog.MaximumThemes)
            throw new ThemePackageException(
                "too_many_themes", $"Theme catalog already contains the {ThemeCatalog.MaximumThemes}-theme limit.");

        var stageRoot = Path.Combine(root, $".theme-stage-{Guid.NewGuid():N}");
        try
        {
            var loaded = await MaterializeAndLoadAsync(stageRoot, inspection.Manifest, inspection.Files, cancellationToken);
            if (!loaded.IsValid)
                throw new ThemePackageException("theme_validation_failed", FirstError(loaded.Diagnostics));
            var stageId = Path.Combine(stageRoot, "themes", inspection.Manifest.Id);
            var stageVersion = Path.Combine(stageRoot, "themes", inspection.Manifest.Id, inspection.Manifest.Version);
            if (Directory.Exists(destinationId))
            {
                RejectReparsePointIfPresent(destinationId);
                Directory.Move(stageVersion, destination);
            }
            else
            {
                Directory.Move(stageId, destinationId);
            }
            return destination;
        }
        finally { TryDeleteTree(stageRoot); }
    }

    internal static async Task VerifyInstalledAsync(string directory, ThemePackageInspection reviewed, CancellationToken token)
    {
        EnsureExistingPathHasNoReparsePoints(directory);
        var installed = await CaptureDirectoryAsync(directory, null, token).ConfigureAwait(false);
        var files = installed.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        if (files.Count != reviewed.Files.Count || reviewed.Files.Any(file => !files.TryGetValue(file.RelativePath, out var current) ||
            !current.Content.AsSpan().SequenceEqual(file.Content)))
            throw new ThemePackageException("version_conflict", "The installed version differs from the catalogue package.");
    }

    private static async Task<ThemeLoadResult> MaterializeAndLoadAsync(
        string root, ThemeManifestDocument manifest, IReadOnlyList<ThemeSourceFile> files, CancellationToken cancellationToken)
    {
        var versionDirectory = Path.Combine(root, "themes", manifest.Id, manifest.Version);
        Directory.CreateDirectory(versionDirectory);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(
                versionDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(versionDirectory, destination))
                throw new ThemePackageException("path_escape", $"Theme path escapes staging: {file.RelativePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, file.Content, cancellationToken);
        }
        var paths = new PlatformSettingsPaths(root);
        var loaded = new ThemeCatalog(paths).Load(manifest.Id, manifest.Version);
        if (!loaded.IsValid)
            throw new ThemePackageException("theme_validation_failed", FirstError(loaded.Diagnostics));
        var compiled = WrssThemeCompiler.Compile(loaded.Package);
        if (!compiled.IsValid)
            throw new ThemePackageException("theme_compile_failed", FirstError(compiled.Diagnostics));
        return loaded;
    }

    public static byte[] SerializeManifest(ThemeManifestDocument manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

    public static void TryDeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<ThemePackageInspection> ValidateAsync(
        IReadOnlyList<ThemeSourceFile> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0) throw new ThemePackageException("empty_package", "Theme package is empty.");
        var manifestFile = files.SingleOrDefault(file => file.RelativePath == ManifestName)
            ?? throw new ThemePackageException("missing_theme_manifest", "Theme package requires exact-case root theme.json.");
        if (manifestFile.Content.Length > ThemeCatalog.MaximumManifestBytes)
            throw new ThemePackageException("theme_manifest_too_large", "Theme manifest exceeds 65536 bytes.");
        foreach (var file in files)
        {
            if (file.RelativePath != ManifestName &&
                !Path.GetExtension(file.RelativePath).Equals(".wrss", StringComparison.OrdinalIgnoreCase))
                throw new ThemePackageException(
                    "unsupported_theme_file", $"Theme packages may contain only theme.json and .wrss files: {file.RelativePath}");
            EnsureStrictUtf8(file);
        }
        var manifest = ParseManifest(manifestFile.Content);
        ValidateDistributionManifest(manifest);
        if (!files.Any(file => file.RelativePath == manifest.EntryFile))
            throw new ThemePackageException(
                "missing_theme_entry", $"Theme entry is missing or has different casing: {manifest.EntryFile}");
        var validationRoot = Path.Combine(Path.GetTempPath(), "WidgetRail", "theme-validation", Guid.NewGuid().ToString("N"));
        try
        {
            var loaded = await MaterializeAndLoadAsync(validationRoot, manifest, files, cancellationToken);
            var loadedSources = loaded.Package.Documents.Select(document => document.Source).ToHashSet(StringComparer.Ordinal);
            var unreferenced = files.Where(file => file.RelativePath.EndsWith(".wrss", StringComparison.OrdinalIgnoreCase) &&
                                                   !loadedSources.Contains(file.RelativePath)).FirstOrDefault();
            if (unreferenced is not null)
                throw new ThemePackageException(
                    "unreferenced_style", $"WRSS file is not reachable from {manifest.EntryFile}: {unreferenced.RelativePath}");
            return new(manifest, files, files.Count, files.Sum(file => (long)file.Content.Length), loaded.Package);
        }
        finally { TryDeleteTree(validationRoot); }
    }

    private static ThemeManifestDocument ParseManifest(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement, "$", 0);
            return JsonSerializer.Deserialize<ThemeManifestDocument>(content, JsonOptions)
                ?? throw new JsonException("Theme manifest was null.");
        }
        catch (JsonException exception)
        {
            throw new ThemePackageException("invalid_theme_manifest", OneLine(exception.Message), exception);
        }
    }

    private static void ValidateDistributionManifest(ThemeManifestDocument manifest)
    {
        if (manifest.SchemaVersion != ThemeManifestDocument.CurrentSchemaVersion)
            throw new ThemePackageException(
                "unsupported_theme_version", $"Distribution packages require schema version {ThemeManifestDocument.CurrentSchemaVersion}.");
        if (!ThemeIdentity.IsValid(manifest.Id) || manifest.Id == ThemeIdentity.BuiltInDefault)
            throw new ThemePackageException("invalid_theme_id", "Theme ID is invalid or reserved.");
        if (manifest.Publisher is not { } publisher ||
            !ThemeIdentity.IsValidPublisher(publisher))
            throw new ThemePackageException("invalid_theme_publisher", "Theme publisher must be a lowercase reverse-DNS identifier.");
        if (!manifest.Id.Equals(publisher, StringComparison.Ordinal) &&
            !manifest.Id.StartsWith(publisher + ".", StringComparison.Ordinal))
            throw new ThemePackageException("theme_identity_mismatch", "Theme ID must be owned by its publisher namespace.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80 || manifest.Name.Any(char.IsControl))
            throw new ThemePackageException("invalid_theme_name", "Theme name must contain 1 to 80 printable characters.");
        if (!ThemeIdentity.TryParseCanonicalVersion(manifest.Version, out _))
            throw new ThemePackageException("invalid_theme_version", "Theme version must use canonical dotted numeric notation.");
        if (!WrssPackageLoader.IsSafePackagePath(manifest.EntryFile))
            throw new ThemePackageException("invalid_theme_entry", "Theme entry must be a normalized package-relative .wrss path.");
    }

    private static async Task<IReadOnlyList<ThemeSourceFile>> CaptureDirectoryAsync(
        string root, string? excludedOutput, CancellationToken cancellationToken)
    {
        var files = new List<ThemeSourceFile>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        long total = 0;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            RejectReparsePointIfPresent(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var full = Path.GetFullPath(entry);
                if (!IsWithin(root, full)) throw new ThemePackageException("path_escape", $"Path escapes theme source: {entry}");
                RejectReparsePointIfPresent(full);
                if (Directory.Exists(full)) { pending.Push(full); continue; }
                if (excludedOutput is not null && full.Equals(excludedOutput, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
                ValidateRelativePath(relative);
                if (!seen.TryAdd(relative, relative))
                    throw new ThemePackageException("path_collision", $"Case-colliding paths: {relative} and {seen[relative]}");
                if (files.Count == MaximumEntries)
                    throw new ThemePackageException("too_many_entries", $"Theme source exceeds {MaximumEntries} files.");
                var length = new FileInfo(full).Length;
                if (length > MaximumEntryBytes)
                    throw new ThemePackageException("entry_too_large", $"'{relative}' exceeds {MaximumEntryBytes} bytes.");
                var content = await File.ReadAllBytesAsync(full, cancellationToken);
                RejectReparsePointIfPresent(full);
                total = checked(total + content.LongLength);
                if (total > MaximumTotalBytes)
                    throw new ThemePackageException("package_too_large", $"Theme exceeds {MaximumTotalBytes} bytes.");
                files.Add(new ThemeSourceFile(relative, content));
            }
        }
        return files;
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry, Dictionary<string, string> seen)
    {
        if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
            throw new ThemePackageException("directory_entry", "Theme archives must not contain explicit directory entries.");
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType == 0xA000)
            throw new ThemePackageException("symlink_entry", $"Theme archive contains a symbolic link: {entry.FullName}");
        ValidateRelativePath(entry.FullName);
        if (!seen.TryAdd(entry.FullName, entry.FullName))
            throw new ThemePackageException(
                "path_collision", $"Theme archive has duplicate or case-colliding paths: {entry.FullName} and {seen[entry.FullName]}");
    }

    private static void ValidateRelativePath(string path)
    {
        if (path.Length is < 1 or > MaximumPathLength || path != path.Normalize(NormalizationForm.FormC) ||
            path.Contains('\\') || path.Contains('\0') || path.StartsWith("/", StringComparison.Ordinal))
            throw new ThemePackageException("invalid_path", $"Theme path is invalid or ambiguous: {path}");
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(character => character < 32 || "<>:\"|?*".Contains(character)) || IsReservedName(segment))
                throw new ThemePackageException("invalid_path", $"Theme path contains an unsafe Windows segment: {path}");
        }
    }

    private static bool IsReservedName(string segment)
    {
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                    stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }

    private static void EnsureStrictUtf8(ThemeSourceFile file)
    {
        try { _ = StrictUtf8.GetString(file.Content); }
        catch (DecoderFallbackException exception)
        {
            throw new ThemePackageException("invalid_utf8", $"Theme file is not valid UTF-8: {file.RelativePath}", exception);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source, string name, long maximum, CancellationToken cancellationToken)
    {
        await using (source)
        {
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximum)
                    throw new ThemePackageException("entry_too_large", $"'{name}' exceeds {maximum} bytes.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new ThemePackageException("install_busy", "Another theme installation is in progress.", exception);
            }
        }
    }

    internal static void EnsureExistingPathHasNoReparsePoints(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            RejectReparsePointIfPresent(current.FullName);
    }

    private static void RejectReparsePointIfPresent(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ThemePackageException("reparse_point", $"Reparse points are not allowed in theme paths: {path}");
    }

    private static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = Path.GetFullPath(candidate);
        return normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectDuplicateProperties(JsonElement value, string path, int depth)
    {
        if (depth > 16) throw new JsonException("JSON nesting exceeds 16 levels.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}' at {path}.");
                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                RejectDuplicateProperties(item, $"{path}[{index++}]", depth + 1);
        }
    }

    private static string FirstError(IReadOnlyList<WrssDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(item => item.Severity == WrssDiagnosticSeverity.Error)?.ToString() ??
        "Theme validation failed.";

    private static string OneLine(string value)
    {
        var result = value.Replace('\r', ' ').Replace('\n', ' ');
        return result.Length <= 512 ? result : result[..512];
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };
}
