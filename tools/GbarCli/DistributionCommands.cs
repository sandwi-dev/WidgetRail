using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.GbarCli;

internal static class PackCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--output");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar pack <widget-directory> [--output <file.gbarwidget>]");

        var result = await WidgetPackagePacker.PackAsync(parsed.Positionals[0], parsed.Option("--output"));
        await output.WriteLineAsync(
            $"Packed {result.Inspection.Id} {result.Inspection.Version} to {result.PackagePath} " +
            $"({result.Inspection.EntryCount} files, {result.Inspection.TotalUncompressedBytes} bytes).");
        return 0;
    }
}

internal static class InstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        HttpMessageHandler? remoteHttpHandler,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog", "--sha256");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar install <file.gbarwidget|https-url|github:owner/repository@tag/asset.gbarwidget> " +
                "[--sha256 <64-hex>] [--catalog <root>]");

        var source = parsed.Positionals[0];
        var expectedSha256 = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        var remoteUri = RemotePackageSource.Resolve(source);
        if (remoteUri is null)
        {
            var localPath = Path.GetFullPath(source);
            if (!File.Exists(localPath))
                throw new WidgetPackageException("package_not_found", $"Package does not exist: {localPath}");
            if (!Path.GetExtension(localPath).Equals(".gbarwidget", StringComparison.OrdinalIgnoreCase))
                throw new WidgetPackageException("invalid_extension", "Widget packages must use the .gbarwidget extension.");
            if ((File.GetAttributes(localPath) & FileAttributes.ReparsePoint) != 0)
                throw new WidgetPackageException("reparse_point", "Local widget package files cannot be reparse points.");
            await using var localPackage = new FileStream(
                localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (expectedSha256 is not null)
            {
                var actualSha256 = await PackageIntegrity.HashStreamAsync(localPackage, cancellationToken);
                PackageIntegrity.Verify(expectedSha256, actualSha256);
            }
            var installed = await catalog.InstallAsync(localPackage, cancellationToken);
            await output.WriteLineAsync($"Installed {installed.Id} {installed.Version} to {installed.InstallPath}.");
            await WriteSelectionHintAsync(catalog, installed, output, cancellationToken);
            return 0;
        }

        if (expectedSha256 is null)
            throw new CliUsageException("Remote widget installation requires --sha256 <64-hex>.");
        using var downloader = new RemotePackageDownloader(remoteHttpHandler);
        await using var downloaded = await downloader.DownloadAsync(remoteUri, expectedSha256, cancellationToken);
        var remoteInstalled = await catalog.InstallAsync(downloaded.PackageStream, cancellationToken);
        await output.WriteLineAsync(
            $"Installed {remoteInstalled.Id} {remoteInstalled.Version} to {remoteInstalled.InstallPath} (disabled)." +
            " Review it, then run gbar enable when ready.");
        await WriteSelectionHintAsync(catalog, remoteInstalled, output, cancellationToken);
        await output.WriteLineAsync($"Downloaded SHA-256: {Convert.ToHexString(downloaded.Sha256).ToLowerInvariant()}");
        return 0;
    }

    private static async Task WriteSelectionHintAsync(
        CatalogService catalog,
        InstalledWidgetVersion installed,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var widget = (await catalog.DiscoverAsync(cancellationToken)).Widgets
            .Single(candidate => candidate.Id == installed.Id);
        if (widget.ActiveVersion.Version == installed.Version) return;
        await output.WriteLineAsync(
            $"Version {widget.ActiveVersion.Version} remains selected. To review this version, run " +
            $"gbar version select {installed.Id} {installed.Version} before enabling it.");
    }

}

internal static class ListCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 0)
            throw new CliUsageException("Usage: gbar list [--catalog <root>]");

        var snapshot = await new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog"))).DiscoverAsync();
        if (snapshot.Widgets.Count == 0)
        {
            await output.WriteLineAsync("No widgets installed.");
            return 0;
        }

        foreach (var widget in snapshot.Widgets)
        {
            var versions = widget.Versions.Count == 1 ? "1 version" : $"{widget.Versions.Count} versions";
            await output.WriteLineAsync(
                $"{(widget.Enabled ? "enabled " : "disabled")}  {widget.Id}  " +
                $"{widget.ActiveVersion.Version}  {widget.Name}  ({versions})");
        }
        return 0;
    }
}

internal static class UninstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar uninstall <widget-id> [--catalog <root>]");

        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        try
        {
            var removed = await catalog.UninstallAsync(parsed.Positionals[0], cancellationToken);
            var versions = string.Join(", ", removed.RemovedVersions.Select(version => version.ToString()));
            await output.WriteLineAsync(
                $"Uninstalled {removed.Id} ({removed.RemovedVersions.Count} version" +
                $"{(removed.RemovedVersions.Count == 1 ? string.Empty : "s")}: {versions}).");
            if (removed.CleanupPending)
                await output.WriteLineAsync(
                    "Package retirement is complete; locked staging files will be retried " +
                    "during the next install or uninstall.");
            return 0;
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
    }
}

internal static class RepairCommand
{
    private const string Usage =
        "Usage: gbar repair <list|remove> [<widget-id> <version>] [--catalog <root>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count == 1 && parsed.Positionals[0] == "list")
            return await ListAsync(parsed, output, cancellationToken);
        if (parsed.Positionals.Count == 3 && parsed.Positionals[0] == "remove")
            return await RemoveAsync(parsed, output, cancellationToken);
        throw new CliUsageException(Usage);
    }

    private static async Task<int> ListAsync(
        CommandArguments parsed,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        var health = await catalog.InspectHealthAsync(cancellationToken);
        await output.WriteLineAsync(health.IsWithinDirectoryLimits
            ? "Catalog directory quotas: within limits (package contents not validated)."
            : $"Catalog directory quotas: exceeded ({health.FailureCode}).");
        if (health.Candidates.Count == 0)
        {
            await output.WriteLineAsync("No installed version directories found.");
            return 0;
        }
        foreach (var candidate in health.Candidates)
        {
            var status = candidate.Selected
                ? candidate.WidgetEnabled ? "enabled-selected-protected" : "selected-protected"
                : "removable";
            await output.WriteLineAsync($"{status}  {candidate.Id}  {candidate.Version}");
        }
        return 0;
    }

    private static async Task<int> RemoveAsync(
        CommandArguments parsed,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var versionText = parsed.Positionals[2];
        if (!Version.TryParse(versionText, out var version) ||
            !string.Equals(version.ToString(), versionText, StringComparison.Ordinal))
            throw new CliUsageException("Repair version must use canonical dotted numeric notation. " + Usage);
        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        try
        {
            var removed = await catalog.RemoveInactiveVersionAsync(
                parsed.Positionals[1], version, cancellationToken);
            await output.WriteLineAsync(
                $"Removed inactive version {removed.Id} {removed.Version} from the catalog.");
            if (removed.CleanupPending)
                await output.WriteLineAsync(
                    "Version retirement is complete; locked staging files will be retried " +
                    "during the next install, uninstall, or repair.");
            return 0;
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
    }
}

internal static class EnabledCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, bool enabled)
    {
        var command = enabled ? "enable" : "disable";
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException($"Usage: gbar {command} <widget-id> [--catalog <root>]");

        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        try
        {
            await catalog.SetEnabledAsync(parsed.Positionals[0], enabled);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
        await output.WriteLineAsync($"{(enabled ? "Enabled" : "Disabled")} {parsed.Positionals[0]}.");
        return 0;
    }
}

internal static class CatalogPath
{
    public static string Resolve(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new CliOperationException("The current user's Local Application Data directory is unavailable; pass --catalog explicitly.");
        return Path.Combine(local, "GameBarAlternative", "widgets");
    }
}

internal sealed record WidgetPackResult(string PackagePath, WidgetPackageInspection Inspection);

internal static class WidgetPackagePacker
{
    private static readonly DateTimeOffset ReproducibleTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<WidgetPackResult> PackAsync(string sourcePath, string? outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        if (!Directory.Exists(root))
            throw new CliUsageException($"Widget directory does not exist: {root}");
        EnsurePathHasNoReparsePoints(root);

        var requestedOutput = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
        if (requestedOutput is not null &&
            !Path.GetExtension(requestedOutput).Equals(".gbarwidget", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Package output must use the .gbarwidget extension.");

        var options = new WidgetCatalogOptions();
        var files = await CaptureFilesAsync(root, requestedOutput, options);
        var manifestEntry = files.SingleOrDefault(file => file.RelativePath == "manifest.json")
            ?? throw new WidgetPackageException(
                "missing_manifest", "Widget directory must contain a root-level manifest.json using exact casing.");
        var manifest = ParseManifest(manifestEntry.Content);
        ValidatePackageIdentity(manifest);
        if (!files.Any(file => file.RelativePath == manifest.Entrypoint.Assembly))
            throw new WidgetPackageException(
                "missing_entrypoint", $"Entrypoint assembly is missing or has different casing: {manifest.Entrypoint.Assembly}");

        var destination = requestedOutput ?? Path.Combine(
            Directory.GetParent(root)?.FullName ?? root,
            $"{manifest.Id}-{manifest.Version}.gbarwidget");
        var outputDirectory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(outputDirectory);
        if (Directory.Exists(destination))
            throw new CliUsageException($"Package output is a directory: {destination}");
        if (File.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException("reparse_point", $"Package output cannot be a reparse point: {destination}");

        var temporary = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(destination)}.{Guid.NewGuid():N}.gbarwidget");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = ReproducibleTimestamp;
                    entry.ExternalAttributes = unchecked((int)(0x81A4u << 16));
                    await using var stream = entry.Open();
                    await stream.WriteAsync(file.Content);
                }
            }

            var validationRoot = Path.Combine(Path.GetTempPath(), "GameBarAlternative", "pack-validation");
            var inspection = await new CatalogService(validationRoot, options)
                .CreateInstaller().ValidateAsync(temporary);
            File.Move(temporary, destination, overwrite: true);
            return new WidgetPackResult(destination, inspection);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<IReadOnlyList<SourceFile>> CaptureFilesAsync(
        string root,
        string? excludedOutput,
        WidgetCatalogOptions options)
    {
        var files = new List<SourceFile>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        long totalBytes = 0;

        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            EnsureWithin(root, directory);
            RejectReparsePoint(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var fullEntry = Path.GetFullPath(entry);
                EnsureWithin(root, fullEntry);
                var attributes = File.GetAttributes(fullEntry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new WidgetPackageException("reparse_point", $"Reparse points are not allowed in widget packages: {fullEntry}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(fullEntry);
                    continue;
                }
                if (excludedOutput is not null && fullEntry.Equals(excludedOutput, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = Path.GetRelativePath(root, fullEntry).Replace(Path.DirectorySeparatorChar, '/');
                ValidateRelativePath(relative, options.MaximumPathLength);
                if (!paths.TryAdd(relative, relative))
                    throw new WidgetPackageException(
                        "path_collision", $"Case-colliding package paths: '{relative}' and '{paths[relative]}'.");
                if (files.Count == options.MaximumArchiveEntries)
                    throw new WidgetPackageException(
                        "too_many_entries", $"Widget directory exceeds {options.MaximumArchiveEntries} files.");

                var info = new FileInfo(fullEntry);
                if (info.Length > options.MaximumEntryBytes)
                    throw new WidgetPackageException("entry_too_large", $"'{relative}' exceeds the per-entry size limit.");
                var content = await ReadBoundedAsync(fullEntry, options.MaximumEntryBytes);
                RejectReparsePoint(fullEntry);
                try { totalBytes = checked(totalBytes + content.LongLength); }
                catch (OverflowException exception)
                {
                    throw new WidgetPackageException("package_too_large", "Widget package size overflowed.", exception);
                }
                if (totalBytes > options.MaximumTotalBytes)
                    throw new WidgetPackageException(
                        "package_too_large", $"Widget directory exceeds {options.MaximumTotalBytes} expanded bytes.");
                files.Add(new SourceFile(relative, content));
            }
        }
        return files;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, long maximumBytes)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream((int)Math.Min(stream.Length, maximumBytes));
        var buffer = new byte[64 * 1024];
        long written = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            written += read;
            if (written > maximumBytes)
                throw new WidgetPackageException("entry_too_large", $"'{path}' exceeds the per-entry size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static WidgetManifest ParseManifest(byte[] payload)
    {
        WidgetManifest manifest;
        try { manifest = ManifestJson.Deserialize(payload); }
        catch (JsonException exception)
        {
            throw new WidgetPackageException("invalid_manifest", $"Manifest JSON is invalid: {exception.Message}", exception);
        }
        var errors = WidgetManifestValidator.Validate(manifest);
        if (errors.Count != 0)
            throw new WidgetPackageException("invalid_manifest", $"{errors[0].Path}: {errors[0].Message}");
        return manifest;
    }

    private static void ValidatePackageIdentity(WidgetManifest manifest)
    {
        if (!manifest.Id.Equals(manifest.Publisher, StringComparison.Ordinal) &&
            !manifest.Id.StartsWith(manifest.Publisher + ".", StringComparison.Ordinal))
            throw new WidgetPackageException("identity_mismatch", "Manifest ID must be owned by its publisher namespace.");
        if (!Version.TryParse(manifest.Version, out var version) ||
            !string.Equals(version.ToString(), manifest.Version, StringComparison.Ordinal))
            throw new WidgetPackageException(
                "invalid_manifest", "Manifest version must use a deterministic dotted numeric representation.");
    }

    private static void EnsurePathHasNoReparsePoints(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            RejectReparsePoint(current.FullName);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException("reparse_point", $"Reparse points are not allowed in widget package paths: {path}");
    }

    private static void EnsureWithin(string root, string candidate)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new WidgetPackageException("path_escape", $"Path escapes the widget directory: {candidate}");
    }

    private static void ValidateRelativePath(string path, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains('\0') ||
            path.Length > maximumLength || path != path.Normalize(NormalizationForm.FormC) ||
            path.StartsWith("/", StringComparison.Ordinal))
            throw new WidgetPackageException("invalid_path", $"Package path is invalid or ambiguous: '{path}'.");
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(ch => ch < 32 || "<>:\"|?*".Contains(ch)) || IsReservedWindowsName(segment))
                throw new WidgetPackageException(
                    "invalid_path", $"Package path contains an unsafe Windows segment: '{path}'.");
        }
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                     stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                stem[3] is >= '1' and <= '9');
    }

    private sealed record SourceFile(string RelativePath, byte[] Content);
}
