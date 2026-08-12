using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.GbarCli;

internal static class ThemeCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        HttpMessageHandler? remoteHttpHandler,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }
        return await (args[0] switch
        {
            "new" => ThemeNewCommand.RunAsync(args[1..], output),
            "validate" => ThemeValidateCommand.RunAsync(args[1..], output, cancellationToken),
            "pack" => ThemePackCommand.RunAsync(args[1..], output, cancellationToken),
            "inspect" => ThemeInspectCommand.RunAsync(args[1..], output, cancellationToken),
            "preview" => ThemePreviewCommand.RunAsync(args[1..], output, cancellationToken),
            "install" => ThemeInstallCommand.RunAsync(
                args[1..], output, remoteHttpHandler, cancellationToken),
            "list" => ThemeListCommand.RunAsync(args[1..], output),
            "remove" => ThemeRemoveCommand.RunAsync(args[1..], output, cancellationToken),
            _ => throw Usage(),
        });
    }

    private static CliUsageException Usage() => new(
        "Usage: gbar theme <new|validate|pack|inspect|preview|install|list|remove> ...");

    private const string HelpText = """
        gbar theme - safe data-only global theme tools

        Usage:
          gbar theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]
          gbar theme validate <theme-directory|file.gbartheme>
          gbar theme preview <theme-directory|file.gbartheme>
          gbar theme pack <theme-directory> [--output <file.gbartheme>]
          gbar theme inspect <file.gbartheme>
          gbar theme install <file.gbartheme|https-url|github:owner/repository@tag/asset.gbartheme> [--sha256 <64-hex>] [--settings-root <root>]
          gbar theme list [--settings-root <root>]
          gbar theme remove <exact-id> <exact-version> [--settings-root <root>]

        Remote installs require a SHA-256 pin. Packages contain only strict JSON and GBSS;
        validation proves structure and integrity, not publisher identity.
        """;
}

internal static partial class ThemeNewCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--output", "--id", "--publisher", "--version");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]");

        var name = parsed.Positionals[0].Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new CliUsageException("Theme name must contain 1 to 80 printable characters.");
        var publisher = parsed.Option("--publisher") ?? "dev.example";
        var id = parsed.Option("--id") ?? $"{publisher}.{Slug(name)}";
        var version = parsed.Option("--version") ?? "1.0.0";
        ValidateIdentity(id, publisher, version);

        var targetName = string.Concat(name.Where(char.IsLetterOrDigit));
        if (targetName.Length == 0) targetName = "Theme";
        var target = Path.GetFullPath(parsed.Option("--output") ?? Path.Combine(Environment.CurrentDirectory, targetName));
        if (File.Exists(target) || Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new CliUsageException($"Output directory is not empty: {target}");

        ThemePackage.EnsureExistingPathHasNoReparsePoints(target);
        Directory.CreateDirectory(target);
        var manifest = new ThemeManifestDocument
        {
            SchemaVersion = ThemeManifestDocument.CurrentSchemaVersion,
            Id = id,
            Publisher = publisher,
            Name = name,
            Version = version,
            EntryFile = "theme.gbss",
        };
        await File.WriteAllBytesAsync(Path.Combine(target, ThemePackage.ManifestName),
            ThemePackage.SerializeManifest(manifest));
        await File.WriteAllTextAsync(Path.Combine(target, "theme.gbss"), StarterGbss, new UTF8Encoding(false));

        await output.WriteLineAsync($"Created theme {name} in {target}");
        await output.WriteLineAsync($"  Identity: {id} {version}");
        await output.WriteLineAsync($"  Publisher: {publisher}");
        await output.WriteLineAsync($"Next: gbar theme preview \"{target}\"");
        return 0;
    }

    private static void ValidateIdentity(string id, string publisher, string version)
    {
        if (!ThemeIdentity.IsValidPublisher(publisher))
            throw new CliUsageException("Publisher must be a lowercase reverse-DNS identifier.");
        if (!ThemeIdentity.IsValid(id) || id == ThemeIdentity.BuiltInDefault)
            throw new CliUsageException("Theme ID must be a lowercase portable identifier and cannot be builtin.default.");
        if (!id.Equals(publisher, StringComparison.Ordinal) &&
            !id.StartsWith(publisher + ".", StringComparison.Ordinal))
            throw new CliUsageException("Theme ID must be owned by its publisher namespace.");
        if (!ThemeIdentity.TryParseCanonicalVersion(version, out _))
            throw new CliUsageException("Theme version must use canonical dotted numeric notation.");
    }

    private static string Slug(string name)
    {
        var value = NonPortable().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(value) ? "theme" : value;
    }

    private const string StarterGbss = """
        :root {
          --accent: #7c6cff;
          --surface: rgba(20, 23, 31, 0.98);
          --surface-raised: rgba(31, 35, 47, 0.98);
          --text: #f7f7fa;
          --text-muted: #adb4c2;
          --focus: #ff7898;
        }

        panel, tray { background: var(--surface); color: var(--text); corner-radius: 18px; }
        tray-item:selected { background: var(--surface-raised); color: var(--text); }
        tray-item:focused { outline-color: var(--focus); outline-width: 2px; }
        button { background: var(--surface-raised); color: var(--text); corner-radius: 12px; }
        button:focused { outline-color: var(--focus); outline-width: 2px; scale: 1.03; }
        """;

    [GeneratedRegex("[^a-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonPortable();
}

internal static class ThemeValidateCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar theme validate <theme-directory|file.gbartheme>");
        var inspection = await ThemePackage.InspectSourceAsync(parsed.Positionals[0], cancellationToken);
        await output.WriteLineAsync(
            $"Valid theme: {inspection.Manifest.Id} {inspection.Manifest.Version} by {inspection.Manifest.Publisher} " +
            $"({inspection.EntryCount} files, {inspection.TotalBytes} bytes).");
        return 0;
    }
}

internal static class ThemePackCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--output");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar theme pack <theme-directory> [--output <file.gbartheme>]");
        var result = await ThemePackage.PackAsync(parsed.Positionals[0], parsed.Option("--output"), cancellationToken);
        await output.WriteLineAsync(
            $"Packed {result.Inspection.Manifest.Id} {result.Inspection.Manifest.Version} to {result.PackagePath} " +
            $"({result.Inspection.EntryCount} files, {result.Inspection.TotalBytes} bytes).");
        await output.WriteLineAsync($"SHA-256: {result.Sha256}");
        return 0;
    }
}

internal static class ThemeInspectCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar theme inspect <file.gbartheme>");
        var path = Path.GetFullPath(parsed.Positionals[0]);
        if (!File.Exists(path)) throw new CliUsageException($"Theme package does not exist: {path}");
        var inspection = await ThemePackage.InspectArchiveAsync(path, cancellationToken);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        await output.WriteLineAsync($"Theme: {inspection.Manifest.Name}");
        await output.WriteLineAsync($"ID: {inspection.Manifest.Id}");
        await output.WriteLineAsync($"Publisher: {inspection.Manifest.Publisher}");
        await output.WriteLineAsync($"Version: {inspection.Manifest.Version}");
        await output.WriteLineAsync($"Entry: {inspection.Manifest.EntryFile}");
        await output.WriteLineAsync($"Files: {inspection.EntryCount}; expanded bytes: {inspection.TotalBytes}");
        await output.WriteLineAsync($"SHA-256: {sha256}");
        await output.WriteLineAsync("Trust: data-only validation passed; publisher identity is not authenticated.");
        return 0;
    }
}

internal static class ThemePreviewCommand
{
    private static readonly (string Label, string Role, GbssPseudoState[] States)[] Roles =
    [
        ("canvas", "canvas", []), ("backdrop", "backdrop", []), ("panel", "panel", []),
        ("tray", "tray", []), ("tray-item", "tray-item", []),
        ("tray-item:selected", "tray-item", [GbssPseudoState.Selected]),
        ("tray-item:focused", "tray-item", [GbssPseudoState.Focused]),
        ("title", "title", []), ("body", "body", []), ("hint", "hint", []),
        ("status", "status", []), ("button", "button", []),
        ("button:focused", "button", [GbssPseudoState.Focused]), ("progress", "progress", []),
    ];

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar theme preview <theme-directory|file.gbartheme>");
        var inspection = await ThemePackage.InspectSourceAsync(parsed.Positionals[0], cancellationToken);
        var root = Path.Combine(Path.GetTempPath(), "GameBarAlternative", "theme-preview", Guid.NewGuid().ToString("N"));
        try
        {
            var load = await ThemePackage.MaterializeAndLoadAsync(root, inspection, cancellationToken);
            var platform = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(root, "platform"))).BuiltInDefault.Package;
            var compiled = ThemeLayerCompiler.Compile(platform, new GbssPackageResult([], []), load.Package);
            if (!compiled.IsValid)
                throw new ThemePackageException("theme_compile_failed", FirstDiagnostic(compiled.Diagnostics));
            await output.WriteLineAsync(
                $"Computed preview: {inspection.Manifest.Name} ({inspection.Manifest.Id} {inspection.Manifest.Version})");
            await output.WriteLineAsync("The built-in platform layer is included; accessibility overrides are not simulated.");
            foreach (var role in Roles)
            {
                var style = compiled.Theme!.Resolve(new GbssElement(
                    role.Role, $"preview.{role.Label.Replace(':', '.')}",
                    new HashSet<string>(StringComparer.Ordinal), role.States.ToHashSet()));
                var values = style.Properties.Count == 0
                    ? "(no computed properties)"
                    : string.Join("; ", style.Properties.Select(item => $"{item.Key}={item.Value.Text}"));
                await output.WriteLineAsync($"{role.Label}: {values}");
            }
            return 0;
        }
        finally { ThemePackage.TryDeleteTree(root); }
    }

    private static string FirstDiagnostic(IReadOnlyList<GbssDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(item => item.Severity == GbssDiagnosticSeverity.Error)?.ToString() ??
        "Theme compilation failed.";
}

internal static class ThemeInstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        HttpMessageHandler? remoteHttpHandler,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--settings-root", "--sha256");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar theme install <file.gbartheme|https-url|github:owner/repository@tag/asset.gbartheme> " +
                "[--sha256 <64-hex>] [--settings-root <root>]");
        var expected = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        var sourceText = parsed.Positionals[0];
        var remoteUri = RemotePackageSource.Resolve(sourceText, ".gbartheme");
        ThemePackageInspection inspection;
        string? downloadedHash = null;
        if (remoteUri is null)
        {
            var source = Path.GetFullPath(sourceText);
            if (!File.Exists(source)) throw new CliUsageException($"Theme package does not exist: {source}");
            if (!Path.GetExtension(source).Equals(".gbartheme", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Theme packages must use the .gbartheme extension.");
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new ThemePackageException("reparse_point", "Local theme package files cannot be reparse points.");
            await using var localPackage = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (expected is not null)
                PackageIntegrity.Verify(expected, await PackageIntegrity.HashStreamAsync(localPackage, cancellationToken));
            inspection = await ThemePackage.InspectArchiveAsync(localPackage, cancellationToken);
        }
        else
        {
            if (expected is null)
                throw new CliUsageException("Remote theme installation requires --sha256 <64-hex>.");
            using var downloader = new RemotePackageDownloader(remoteHttpHandler, new RemoteDownloadOptions
            {
                MaximumBytes = ThemePackage.MaximumArchiveBytes,
            });
            await using var downloaded = await downloader.DownloadAsync(
                remoteUri, expected, cancellationToken, ".gbartheme");
            inspection = await ThemePackage.InspectArchiveAsync(downloaded.PackageStream, cancellationToken);
            downloadedHash = Convert.ToHexString(downloaded.Sha256).ToLowerInvariant();
        }
        var installed = await ThemePackage.InstallAsync(
            inspection, ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")), cancellationToken);
        await output.WriteLineAsync(
            $"Installed {inspection.Manifest.Id} {inspection.Manifest.Version} to {installed}.");
        await output.WriteLineAsync("Select it from Settings > Appearance after reviewing the publisher and preview.");
        if (downloadedHash is not null)
            await output.WriteLineAsync($"Downloaded SHA-256: {downloadedHash}");
        return 0;
    }
}

internal static class ThemeListCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--settings-root");
        if (parsed.Positionals.Count != 0)
            throw new CliUsageException("Usage: gbar theme list [--settings-root <root>]");
        var paths = new PlatformSettingsPaths(ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")));
        var snapshot = new ThemeCatalog(paths).Discover();
        foreach (var theme in snapshot.Themes)
        {
            var validity = theme.IsValid ? "valid  " : "invalid";
            var publisher = theme.Descriptor.Publisher ?? (theme.Descriptor.IsBuiltIn ? "platform" : "legacy/unverified");
            await output.WriteLineAsync(
                $"{validity}  {theme.Descriptor.Id}  {theme.Descriptor.Version}  {theme.Descriptor.Name}  [{publisher}]");
            foreach (var diagnostic in theme.Diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Error).Take(1))
                await output.WriteLineAsync($"         {diagnostic.Code}: {diagnostic.Message}");
        }
        return 0;
    }
}

internal static class ThemeRemoveCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--settings-root");
        if (parsed.Positionals.Count != 2)
            throw new CliUsageException(
                "Usage: gbar theme remove <exact-id> <exact-version> [--settings-root <root>]");
        var paths = new PlatformSettingsPaths(
            ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")));
        var result = await new ThemeCatalogMutationPolicy(
                new PlatformSettingsStore(paths), new ThemeCatalog(paths))
            .RetireAsync(parsed.Positionals[0], parsed.Positionals[1], cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Removed {result.ThemeId} {result.Version}." +
            (result.CleanupPending ? " Retired files are pending cleanup." : string.Empty));
        return 0;
    }
}

internal static class ThemeSettingsRoot
{
    public static string Resolve(string? requested) => string.IsNullOrWhiteSpace(requested)
        ? PlatformSettingsPaths.CreateDefault().RootDirectory
        : Path.GetFullPath(requested);
}

internal sealed class ThemePackageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed record ThemePackageInspection(
    ThemeManifestDocument Manifest,
    IReadOnlyList<ThemeSourceFile> Files,
    int EntryCount,
    long TotalBytes);

internal sealed record ThemeSourceFile(string RelativePath, byte[] Content);
internal sealed record ThemePackResult(string PackagePath, string Sha256, ThemePackageInspection Inspection);

internal static class ThemePackage
{
    public const string ManifestName = "theme.json";
    public const int MaximumEntries = 65;
    public const int MaximumPathLength = 240;
    public const long MaximumEntryBytes = GbssLimits.MaximumSourceBytes;
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
        if (!File.Exists(path)) throw new CliUsageException($"Theme source does not exist: {path}");
        return await InspectArchiveAsync(path, cancellationToken);
    }

    public static async Task<ThemePackResult> PackAsync(
        string source, string? output, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(root)) throw new CliUsageException($"Theme directory does not exist: {root}");
        EnsureExistingPathHasNoReparsePoints(root);
        var requestedOutput = string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
        if (requestedOutput is not null &&
            !Path.GetExtension(requestedOutput).Equals(".gbartheme", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Theme package output must use the .gbartheme extension.");
        var files = await CaptureDirectoryAsync(root, requestedOutput, cancellationToken);
        var inspection = await ValidateAsync(files, cancellationToken);
        var destination = requestedOutput ?? Path.Combine(
            Directory.GetParent(root)?.FullName ?? root,
            $"{inspection.Manifest.Id}-{inspection.Manifest.Version}.gbartheme");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(destination)) throw new CliUsageException($"Theme package output is a directory: {destination}");
        RejectReparsePointIfPresent(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.gbartheme");
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
        if (!Path.GetExtension(full).Equals(".gbartheme", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Theme packages must use the .gbartheme extension.");
        var info = new FileInfo(full);
        if (!info.Exists) throw new CliUsageException($"Theme package does not exist: {full}");
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
            var loaded = await MaterializeAndLoadAsync(stageRoot, inspection, cancellationToken);
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

    public static async Task<ThemeLoadResult> MaterializeAndLoadAsync(
        string root, ThemePackageInspection inspection, CancellationToken cancellationToken)
    {
        var versionDirectory = Path.Combine(root, "themes", inspection.Manifest.Id, inspection.Manifest.Version);
        Directory.CreateDirectory(versionDirectory);
        foreach (var file in inspection.Files)
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
        var loaded = new ThemeCatalog(paths).Load(inspection.Manifest.Id, inspection.Manifest.Version);
        if (!loaded.IsValid)
            throw new ThemePackageException("theme_validation_failed", FirstError(loaded.Diagnostics));
        var compiled = GbssThemeCompiler.Compile(loaded.Package);
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
                !Path.GetExtension(file.RelativePath).Equals(".gbss", StringComparison.OrdinalIgnoreCase))
                throw new ThemePackageException(
                    "unsupported_theme_file", $"Theme packages may contain only theme.json and .gbss files: {file.RelativePath}");
            EnsureStrictUtf8(file);
        }
        var manifest = ParseManifest(manifestFile.Content);
        ValidateDistributionManifest(manifest);
        if (!files.Any(file => file.RelativePath == manifest.EntryFile))
            throw new ThemePackageException(
                "missing_theme_entry", $"Theme entry is missing or has different casing: {manifest.EntryFile}");
        var inspection = new ThemePackageInspection(manifest, files, files.Count, files.Sum(file => (long)file.Content.Length));
        var validationRoot = Path.Combine(Path.GetTempPath(), "GameBarAlternative", "theme-validation", Guid.NewGuid().ToString("N"));
        try
        {
            var loaded = await MaterializeAndLoadAsync(validationRoot, inspection, cancellationToken);
            var loadedSources = loaded.Package.Documents.Select(document => document.Source).ToHashSet(StringComparer.Ordinal);
            var unreferenced = files.Where(file => file.RelativePath.EndsWith(".gbss", StringComparison.OrdinalIgnoreCase) &&
                                                   !loadedSources.Contains(file.RelativePath)).FirstOrDefault();
            if (unreferenced is not null)
                throw new ThemePackageException(
                    "unreferenced_style", $"GBSS file is not reachable from {manifest.EntryFile}: {unreferenced.RelativePath}");
            return inspection;
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
        if (!GbssPackageLoader.IsSafePackagePath(manifest.EntryFile))
            throw new ThemePackageException("invalid_theme_entry", "Theme entry must be a normalized package-relative .gbss path.");
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

    private static string FirstError(IReadOnlyList<GbssDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(item => item.Severity == GbssDiagnosticSeverity.Error)?.ToString() ??
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
