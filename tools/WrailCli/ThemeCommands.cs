using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WrailCli;

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
            "update" => PackageUpdateCommand.RunAsync(args[1..], output, remoteHttpHandler, cancellationToken, theme: true),
            "list" => ThemeListCommand.RunAsync(args[1..], output),
            "remove" => ThemeRemoveCommand.RunAsync(args[1..], output, cancellationToken),
            _ => throw Usage(),
        });
    }

    private static CliUsageException Usage() => new(
        "Usage: wrail theme <new|validate|pack|inspect|preview|install|update|list|remove> ...");

    private const string HelpText = """
        wrail theme - safe data-only global theme tools

        Usage:
          wrail theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]
          wrail theme validate <theme-directory|file.wrtheme>
          wrail theme preview <theme-directory|file.wrtheme>
          wrail theme pack <theme-directory> [--output <file.wrtheme>]
          wrail theme inspect <file.wrtheme>
          wrail theme update <theme-id> [--version <installed-version>] [--repo <owner/repository>] [--tag <tag>] [--asset <filename>] [--sha256 <64-hex>] [--apply] [--settings-root <root>]
          wrail theme install <file.wrtheme|https-url|github:owner/repository@tag/asset.wrtheme> [--sha256 <64-hex>] [--settings-root <root>]
          wrail theme list [--settings-root <root>]
          wrail theme remove <exact-id> <exact-version> [--settings-root <root>]

        Remote installs require a SHA-256 pin. Packages contain only strict JSON and WRSS;
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
                "Usage: wrail theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]");

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
            EntryFile = "theme.wrss",
        };
        await File.WriteAllBytesAsync(Path.Combine(target, ThemePackage.ManifestName),
            ThemePackage.SerializeManifest(manifest));
        await File.WriteAllTextAsync(Path.Combine(target, "theme.wrss"), StarterWrss, new UTF8Encoding(false));

        await output.WriteLineAsync($"Created theme {name} in {target}");
        await output.WriteLineAsync($"  Identity: {id} {version}");
        await output.WriteLineAsync($"  Publisher: {publisher}");
        await output.WriteLineAsync($"Next: wrail theme preview \"{target}\"");
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

    private const string StarterWrss = """
        :root {
          --accent: #7c6cff;
          --surface: rgba(20, 23, 31, 0.98);
          --surface-raised: rgba(31, 35, 47, 0.98);
          --text: #f7f7fa;
          --text-muted: #adb4c2;
          --scrollbar-track: #303647;
          --scrollbar-thumb: #adb4c2;
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
            throw new CliUsageException("Usage: wrail theme validate <theme-directory|file.wrtheme>");
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
            throw new CliUsageException("Usage: wrail theme pack <theme-directory> [--output <file.wrtheme>]");
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
            throw new CliUsageException("Usage: wrail theme inspect <file.wrtheme>");
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
    private static readonly (string Label, string Role, WrssPseudoState[] States)[] Roles =
    [
        ("canvas", "canvas", []), ("backdrop", "backdrop", []), ("panel", "panel", []),
        ("tray", "tray", []), ("tray-item", "tray-item", []),
        ("tray-item:selected", "tray-item", [WrssPseudoState.Selected]),
        ("tray-item:focused", "tray-item", [WrssPseudoState.Focused]),
        ("title", "title", []), ("body", "body", []), ("hint", "hint", []),
        ("status", "status", []), ("button", "button", []),
        ("button:focused", "button", [WrssPseudoState.Focused]), ("progress", "progress", []),
    ];

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: wrail theme preview <theme-directory|file.wrtheme>");
        var inspection = await ThemePackage.InspectSourceAsync(parsed.Positionals[0], cancellationToken);
        var platform = new ThemeCatalog(new PlatformSettingsPaths(Path.GetTempPath())).BuiltInDefault.Package;
        var compiled = ThemeLayerCompiler.Compile(platform, new WrssPackageResult([], []), inspection.VerifiedStyles);
        if (!compiled.IsValid)
            throw new ThemePackageException("theme_compile_failed", FirstDiagnostic(compiled.Diagnostics));
        await output.WriteLineAsync(
            $"Computed preview: {inspection.Manifest.Name} ({inspection.Manifest.Id} {inspection.Manifest.Version})");
        await output.WriteLineAsync("The built-in platform layer is included; accessibility overrides are not simulated.");
        foreach (var role in Roles)
        {
            var style = compiled.Theme!.Resolve(new WrssElement(
                role.Role, $"preview.{role.Label.Replace(':', '.')}",
                new HashSet<string>(StringComparer.Ordinal), role.States.ToHashSet()));
            var values = style.Properties.Count == 0
                ? "(no computed properties)"
                : string.Join("; ", style.Properties.Select(item => $"{item.Key}={item.Value.Text}"));
            await output.WriteLineAsync($"{role.Label}: {values}");
        }
        return 0;
    }

    private static string FirstDiagnostic(IReadOnlyList<WrssDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(item => item.Severity == WrssDiagnosticSeverity.Error)?.ToString() ??
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
                "Usage: wrail theme install <file.wrtheme|https-url|github:owner/repository@tag/asset.wrtheme> " +
                "[--sha256 <64-hex>] [--settings-root <root>]");
        var expected = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        var sourceText = parsed.Positionals[0];
        var remoteUri = RemotePackageSource.Resolve(sourceText, ".wrtheme");
        var github = sourceText.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            ? GitHubPackageSource.Parse(sourceText, ".wrtheme") : null;
        ThemePackageInspection inspection;
        string? downloadedHash = null;
        if (remoteUri is null)
        {
            var source = Path.GetFullPath(sourceText);
            if (!File.Exists(source)) throw new CliUsageException($"Theme package does not exist: {source}");
            if (!Path.GetExtension(source).Equals(".wrtheme", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Theme packages must use the .wrtheme extension.");
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
            if (expected is null && github is not null)
            {
                using var releases = new GitHubReleaseClient(remoteHttpHandler);
                expected = await releases.ResolveDigestAsync(github, cancellationToken);
                await output.WriteLineAsync("Using the published SHA-256 for this GitHub release asset.");
            }
            if (expected is null)
                throw new CliUsageException("Remote theme installation requires --sha256 <64-hex>.");
            using var downloader = new RemotePackageDownloader(remoteHttpHandler, new RemoteDownloadOptions
            {
                MaximumBytes = ThemePackage.MaximumArchiveBytes,
            });
            await using var downloaded = await downloader.DownloadAsync(
                remoteUri, expected, cancellationToken, ".wrtheme");
            inspection = await ThemePackage.InspectArchiveAsync(downloaded.PackageStream, cancellationToken);
            downloadedHash = Convert.ToHexString(downloaded.Sha256).ToLowerInvariant();
        }
        var installed = await ThemePackage.InstallAsync(
            inspection, ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")), cancellationToken);
        if (github is not null && downloadedHash is not null)
            await PackageSources.SaveAsync(ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")), "theme",
                new(inspection.Manifest.Id, inspection.Manifest.Version, inspection.Manifest.Publisher!,
                    github.ToString(), downloadedHash, ThemePackageDigest.Compute(inspection)), output, cancellationToken);
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
            throw new CliUsageException("Usage: wrail theme list [--settings-root <root>]");
        var paths = new PlatformSettingsPaths(ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")));
        var snapshot = new ThemeCatalog(paths).Discover();
        foreach (var theme in snapshot.Themes)
        {
            var validity = theme.IsValid ? "valid  " : "invalid";
            var publisher = theme.Descriptor.Publisher ?? (theme.Descriptor.IsBuiltIn ? "platform" : "legacy/unverified");
            await output.WriteLineAsync(
                $"{validity}  {theme.Descriptor.Id}  {theme.Descriptor.Version}  {theme.Descriptor.Name}  [{publisher}]");
            foreach (var diagnostic in theme.Diagnostics.Where(item => item.Severity == WrssDiagnosticSeverity.Error).Take(1))
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
                "Usage: wrail theme remove <exact-id> <exact-version> [--settings-root <root>]");
        var paths = new PlatformSettingsPaths(
            ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")));
        var result = await new ThemeCatalogMutationPolicy(
                new PlatformSettingsStore(paths), new ThemeCatalog(paths))
            .RetireAsync(parsed.Positionals[0], parsed.Positionals[1], cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Removed {result.ThemeId} {result.Version}." +
            (result.CleanupPending ? " Retired files are pending cleanup." : string.Empty));
        await PackageSources.RemoveAsync(ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")), "theme",
            result.ThemeId, result.Version.ToString(), output, cancellationToken);
        return 0;
    }
}

internal static class ThemeSettingsRoot
{
    public static string Resolve(string? requested) => string.IsNullOrWhiteSpace(requested)
        ? PlatformSettingsPaths.CreateDefault().RootDirectory
        : Path.GetFullPath(requested);
}
