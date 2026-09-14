using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsAppLibraryProvider;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.WrailCli;

internal static class PackCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(
            args, "--output", "--configuration", "--build-timeout-seconds");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: wrail pack <widget-directory|widget.csproj> " +
                "[--output <file.wrwidget>] [--configuration <name>] " +
                "[--build-timeout-seconds <10-600>]");

        var source = DevWidgetSource.Discover(parsed.Positionals[0]);
        if (source.Kind == DevWidgetSourceKind.PackageArchive)
            throw new CliUsageException(
                "wrail pack expects source or a package directory, not an existing .wrwidget.");
        if (source.Kind == DevWidgetSourceKind.PackageDirectory)
        {
            if (parsed.Option("--configuration") is not null ||
                parsed.Option("--build-timeout-seconds") is not null)
                throw new CliUsageException(
                    "--configuration and --build-timeout-seconds apply only to a source project; " +
                    "a staged package directory is packed as-is.");
            var raw = await WidgetPackagePacker.PackAsync(
                source.Root, parsed.Option("--output")).ConfigureAwait(false);
            await WriteResultAsync(raw.PackagePath, raw.Inspection, output).ConfigureAwait(false);
            return 0;
        }

        var configuration = parsed.Option("--configuration") ?? "Release";
        if (configuration.Length is < 1 or > 64 ||
            configuration.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new CliUsageException(
                "--configuration must be a simple 1-64 character name.");
        var timeout = ParseBuildTimeout(parsed.Option("--build-timeout-seconds"));
        var generation = Path.Combine(
            Path.GetTempPath(), "WidgetRail", "wrail-pack",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(generation);
        try
        {
            var prepared = await DevGenerationBuilder.PreparePackageAsync(
                source, generation, configuration, timeout, output, error,
                cancellationToken).ConfigureAwait(false);
            var inspection = await new CatalogService(
                    Path.Combine(generation, "published-validation"))
                .CreateInstaller().ValidateAsync(
                    prepared.PackagePath, cancellationToken)
                .ConfigureAwait(false);
            var destination = ResolveOutput(
                source.Root, prepared.Manifest, parsed.Option("--output"));
            await PublishAsync(prepared.PackagePath, destination, cancellationToken)
                .ConfigureAwait(false);
            await WriteResultAsync(destination, inspection, output).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            if (!await DevSession.DeleteTreeWithRetriesAsync(generation).ConfigureAwait(false))
                await error.WriteLineAsync(
                    "warning: temporary package build output could not be removed; " +
                    "close processes using the system temporary directory and retry.");
        }
    }

    private static TimeSpan ParseBuildTimeout(string? value)
    {
        if (value is null) return TimeSpan.FromSeconds(120);
        if (!int.TryParse(value, out var seconds) || seconds is < 10 or > 600)
            throw new CliUsageException(
                "--build-timeout-seconds must be between 10 and 600.");
        return TimeSpan.FromSeconds(seconds);
    }

    private static string ResolveOutput(
        string sourceRoot,
        WidgetManifest manifest,
        string? requested)
    {
        var destination = requested is null
            ? Path.Combine(
                Directory.GetParent(sourceRoot)?.FullName ?? sourceRoot,
                $"{manifest.Id}-{manifest.Version}.wrwidget")
            : Path.GetFullPath(requested);
        if (!Path.GetExtension(destination).Equals(
                ".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException(
                "Package output must use the .wrwidget extension.");
        if (Directory.Exists(destination))
            throw new CliUsageException($"Package output is a directory: {destination}");
        if (File.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException(
                "reparse_point", "Package output cannot be a reparse point.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        return destination;
    }

    private static async Task PublishAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Task WriteResultAsync(
        string packagePath,
        WidgetPackageInspection inspection,
        TextWriter output) =>
        output.WriteLineAsync(
            $"Packed {inspection.Id} {inspection.Version} to {packagePath} " +
            $"({inspection.EntryCount} files, {inspection.TotalUncompressedBytes} bytes).");
}

internal static class InstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        HttpMessageHandler? remoteHttpHandler,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(
            args,
            ["--catalog", "--sha256"],
            ["--accept-full-trust"]);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: wrail install <file.wrwidget|https-url|github:owner/repository@tag/asset.wrwidget> " +
                "[--sha256 <64-hex>] [--catalog <root>] [--accept-full-trust]");

        var source = parsed.Positionals[0];
        var expectedSha256 = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        var remoteUri = RemotePackageSource.Resolve(source);
        if (remoteUri is null)
        {
            var localPath = Path.GetFullPath(source);
            if (!File.Exists(localPath))
                throw new WidgetPackageException("package_not_found", $"Package does not exist: {localPath}");
            if (!Path.GetExtension(localPath).Equals(".wrwidget", StringComparison.OrdinalIgnoreCase))
                throw new WidgetPackageException("invalid_extension", "Widget packages must use the .wrwidget extension.");
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
            var approval = await InspectTrustAsync(
                catalog, localPackage, parsed, output, cancellationToken);
            var installed = await catalog.InstallAsync(
                localPackage, approval, cancellationToken);
            await output.WriteLineAsync($"Installed {installed.Id} {installed.Version} to {installed.InstallPath}.");
            await WriteSelectionHintAsync(catalog, installed, output, cancellationToken);
            return 0;
        }

        var github = source.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            ? GitHubPackageSource.Parse(source, ".wrwidget") : null;
        if (expectedSha256 is null && github is not null)
        {
            using var releases = new GitHubReleaseClient(remoteHttpHandler);
            expectedSha256 = await releases.ResolveDigestAsync(github, cancellationToken);
            await output.WriteLineAsync("Using the published SHA-256 for this GitHub release asset.");
        }
        if (expectedSha256 is null) throw new CliUsageException("Remote widget installation requires --sha256 <64-hex>.");
        using var downloader = new RemotePackageDownloader(remoteHttpHandler);
        await using var downloaded = await downloader.DownloadAsync(remoteUri, expectedSha256, cancellationToken);
        var remoteApproval = await InspectTrustAsync(
            catalog, downloaded.PackageStream, parsed, output, cancellationToken);
        var remoteInstalled = await catalog.InstallAsync(
            downloaded.PackageStream, remoteApproval, cancellationToken);
        if (github is not null)
            await PackageSources.SaveAsync(CatalogPath.Resolve(parsed.Option("--catalog")), "widget",
                new(remoteInstalled.Id, remoteInstalled.Version.ToString(), remoteInstalled.Manifest.Publisher,
                    github.ToString(), Convert.ToHexString(downloaded.Sha256).ToLowerInvariant(), remoteInstalled.ContentDigest), output, cancellationToken);
        await output.WriteLineAsync(
            $"Installed {remoteInstalled.Id} {remoteInstalled.Version} to {remoteInstalled.InstallPath} (disabled)." +
            " Review it, then run wrail enable when ready.");
        await WriteSelectionHintAsync(catalog, remoteInstalled, output, cancellationToken);
        await output.WriteLineAsync($"Downloaded SHA-256: {Convert.ToHexString(downloaded.Sha256).ToLowerInvariant()}");
        return 0;
    }

    private static async Task<WidgetPackageTrustApproval> InspectTrustAsync(
        CatalogService catalog,
        Stream package,
        CommandArguments arguments,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var inspection = await catalog.CreateInstaller()
            .ValidateAsync(package, cancellationToken).ConfigureAwait(false);
        if (WidgetManifestTrust.Resolve(inspection.Manifest) !=
            WidgetExecutionTrust.FullTrustCurrentUser)
            return WidgetPackageTrustApproval.None;
        if (!arguments.HasFlag("--accept-full-trust"))
            throw new WidgetPackageException(
                "full_trust_approval_required",
                "This package runs as an ordinary current-user process and is not AppContainer sandboxed. Review it, then repeat with --accept-full-trust.");
        await WriteFullTrustDisclosureAsync(
            output, inspection.Id, inspection.Version.ToString());
        return WidgetPackageTrustApproval.FullTrustCurrentUser;
    }

    internal static Task WriteFullTrustDisclosureAsync(
        TextWriter output,
        string widgetId,
        string version) => output.WriteLineAsync(
            $"FULL TRUST APPROVED: {widgetId} {version} runs as an ordinary current-user process, not in AppContainer. It can use the current user's files, network, registry, databases, and child processes.");

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
            $"wrail version select {installed.Id} {installed.Version} before enabling it.");
    }

}

internal static class ListCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 0)
            throw new CliUsageException("Usage: wrail list [--catalog <root>]");

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
            throw new CliUsageException("Usage: wrail uninstall <widget-id> [--catalog <root>]");

        var catalogRoot = CatalogPath.Resolve(parsed.Option("--catalog"));
        var catalog = new CatalogService(
            catalogRoot,
            new CliWidgetUninstallAuthorityParticipant(catalogRoot));
        try
        {
            var removed = await catalog.UninstallAsync(parsed.Positionals[0], cancellationToken);
            await PackageSources.RemoveAsync(catalogRoot, "widget", removed.Id, null, output, cancellationToken);
            var versions = string.Join(", ", removed.RemovedVersions.Select(version => version.ToString()));
            await output.WriteLineAsync(
                $"Uninstalled {removed.Id} ({removed.RemovedVersions.Count} version" +
                $"{(removed.RemovedVersions.Count == 1 ? string.Empty : "s")}: {versions}).");
            if (removed.CleanupPending)
                await output.WriteLineAsync(
                    "Package retirement is complete; pending cleanup will be retried " +
                    "by a later uninstall operation on this catalog.");
            return 0;
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
    }
}

internal sealed class CliWidgetUninstallAuthorityParticipant(
    string catalogRoot) : IWidgetUninstallAuthorityParticipant
{
    private readonly WindowsPortableAppStore _store = new(
        WindowsPortableAppRegistrationPaths.ForCatalogRoot(catalogRoot));

    public async Task<WidgetUninstallAuthorityCommit> RetirePackageAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _store.RetirePackageAsync(
                packageId, cancellationToken).ConfigureAwait(false);
            return new(result.Committed, result.CleanupPending);
        }
        catch (Exception exception) when (exception is BrokerException or
            IOException or UnauthorizedAccessException)
        {
            throw new WidgetPackageException(
                "registration_cleanup_failed",
                "Portable app registration cleanup failed.",
                exception);
        }
    }
}

internal static class RepairCommand
{
    private const string Usage =
        "Usage: wrail repair <list|remove> [<widget-id> <version>] [--catalog <root>]";

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
        var parsed = new CommandArguments(
            args,
            ["--catalog"],
            enabled ? ["--accept-full-trust"] : []);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                $"Usage: wrail {command} <widget-id> [--catalog <root>]" +
                (enabled ? " [--accept-full-trust]" : string.Empty));

        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        try
        {
            var approval = WidgetPackageTrustApproval.None;
            if (enabled)
            {
                var widget = (await catalog.DiscoverAsync()).Widgets.SingleOrDefault(
                    candidate => candidate.Id == parsed.Positionals[0])
                    ?? throw new KeyNotFoundException(
                        $"Widget '{parsed.Positionals[0]}' is not installed.");
                if (WidgetManifestTrust.Resolve(widget.ActiveVersion.Manifest) ==
                    WidgetExecutionTrust.FullTrustCurrentUser)
                {
                    if (!parsed.HasFlag("--accept-full-trust"))
                        throw new WidgetPackageException(
                            "full_trust_approval_required",
                            "This package runs as an ordinary current-user process and is not AppContainer sandboxed. Review it, then repeat with --accept-full-trust.");
                    await InstallCommand.WriteFullTrustDisclosureAsync(
                        output, widget.Id, widget.ActiveVersion.Version.ToString());
                    approval = WidgetPackageTrustApproval.FullTrustCurrentUser;
                }
            }
            await catalog.SetEnabledAsync(parsed.Positionals[0], enabled, approval);
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
        return Path.Combine(local, "WidgetRail", "widgets");
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
            !Path.GetExtension(requestedOutput).Equals(".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Package output must use the .wrwidget extension.");

        var options = new WidgetCatalogOptions();
        var files = await CaptureFilesAsync(root, requestedOutput, options);
        var manifestEntry = files.SingleOrDefault(file => file.RelativePath == "manifest.json")
            ?? throw new WidgetPackageException(
                "missing_manifest", "Widget directory must contain a root-level manifest.json using exact casing.");
        var manifest = ParseManifest(manifestEntry.Content);
        ValidatePackageIdentity(manifest);
        var entrypointPath = WidgetEntrypointRuntimes.ResolvePackagePath(
            manifest.Entrypoint);
        if (!files.Any(file => file.RelativePath == entrypointPath))
            throw new WidgetPackageException(
                "missing_entrypoint", $"Entrypoint is missing or has different casing: {entrypointPath}");

        var destination = requestedOutput ?? Path.Combine(
            Directory.GetParent(root)?.FullName ?? root,
            $"{manifest.Id}-{manifest.Version}.wrwidget");
        var outputDirectory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(outputDirectory);
        if (Directory.Exists(destination))
            throw new CliUsageException($"Package output is a directory: {destination}");
        if (File.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException("reparse_point", $"Package output cannot be a reparse point: {destination}");

        var temporary = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(destination)}.{Guid.NewGuid():N}.wrwidget");
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

            var validationRoot = Path.Combine(Path.GetTempPath(), "WidgetRail", "pack-validation");
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
