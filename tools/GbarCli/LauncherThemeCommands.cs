using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameBarAlternative.LauncherExperienceCatalog;

namespace GameBarAlternative.GbarCli;

internal static class LauncherThemeCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }
        return await (args[0] switch
        {
            "new" => LauncherThemeNewCommand.RunAsync(args[1..], output, cancellationToken),
            "validate" => LauncherThemeValidateCommand.RunAsync(args[1..], output, cancellationToken),
            "preview" => LauncherThemePreviewCommand.RunAsync(args[1..], output, cancellationToken),
            "pack" => LauncherThemePackCommand.RunAsync(args[1..], output, cancellationToken),
            "inspect" => LauncherThemeInspectCommand.RunAsync(args[1..], output, cancellationToken),
            "install" => LauncherThemeInstallCommand.RunAsync(args[1..], output, cancellationToken),
            "list" => LauncherThemeListCommand.RunAsync(args[1..], output),
            "remove" => LauncherThemeRemoveCommand.RunAsync(args[1..], output, cancellationToken),
            _ => throw Usage(),
        }).ConfigureAwait(false);
    }

    private static CliUsageException Usage() => new(
        "Usage: gbar launcher-theme <new|validate|preview|pack|inspect|install|list|remove> ...");

    private const string HelpText = """
        gbar launcher-theme - deterministic data-only Launcher Experience tools

        Usage:
          gbar launcher-theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>] [--preset <hero-rail|cover-wall|carousel|compact-grid>]
          gbar launcher-theme validate <directory|file.gbarlauncher>
          gbar launcher-theme preview <directory|file.gbarlauncher> [--output <preview.json>]
          gbar launcher-theme pack <directory> [--output <file.gbarlauncher>]
          gbar launcher-theme inspect <file.gbarlauncher>
          gbar launcher-theme install <file.gbarlauncher> [--catalog <root>]
          gbar launcher-theme list [--catalog <root>]
          gbar launcher-theme remove <exact-id> <exact-version> [--catalog <root>]

        Packages are strict local data: JSON recipes, launcher-scoped GBSS, and
        bounded static PNG/JPEG/WebP assets. They cannot add actions, providers,
        scripts, executables, URLs, or game/content authority. Preview emits the
        deterministic offscreen fixture contract; ordinary-overlay adoption is
        unavailable until the native Launcher Experience hook is installed.
        """;
}

internal static partial class LauncherThemeNewCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(
            args, "--output", "--id", "--publisher", "--version", "--preset");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar launcher-theme new <Name> [--output <directory>] [--id <id>] " +
                "[--publisher <id>] [--version <version>] " +
                "[--preset <hero-rail|cover-wall|carousel|compact-grid>]");
        var name = parsed.Positionals[0].Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new CliUsageException("Launcher Experience name must contain 1 to 80 printable characters.");
        var publisher = parsed.Option("--publisher") ?? "dev.example";
        var id = parsed.Option("--id") ?? $"{publisher}.{Slug(name)}";
        var version = parsed.Option("--version") ?? "1.0.0";
        var preset = parsed.Option("--preset") ?? "hero-rail";
        ValidateIdentity(id, publisher, version);
        if (!LauncherThemeTemplate.IsPreset(preset))
            throw new CliUsageException("Preset must be hero-rail, cover-wall, carousel, or compact-grid.");

        var directoryName = string.Concat(name.Where(char.IsLetterOrDigit));
        if (directoryName.Length == 0) directoryName = "LauncherExperience";
        var target = Path.GetFullPath(parsed.Option("--output") ??
                                      Path.Combine(Environment.CurrentDirectory, directoryName));
        if (File.Exists(target) || Directory.Exists(target))
            throw new CliUsageException($"Output path already exists: {target}");
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        LauncherThemePathGuard.EnsureNoReparsePoints(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            LauncherThemeTemplate.Write(staging, id, publisher, name, version, preset);
            var inspection = LauncherExperienceArchive.InspectDirectory(staging);
            Directory.Move(staging, target);
            await output.WriteLineAsync($"Created Launcher Experience {name} in {target}");
            await output.WriteLineAsync($"  Identity: {id} {version}");
            await output.WriteLineAsync($"  Preset: {preset}");
            await output.WriteLineAsync($"  Content SHA-256: {inspection.Package.Descriptor.ContentDigest}");
            await output.WriteLineAsync($"Next: gbar launcher-theme preview \"{target}\"");
            return 0;
        }
        finally
        {
            LauncherExperienceArchive.TryDeleteTree(staging);
        }
    }

    private static void ValidateIdentity(string id, string publisher, string version)
    {
        if (!LauncherExperienceIdentity.IsValidPublisher(publisher))
            throw new CliUsageException("Publisher must be a lowercase reverse-DNS identifier.");
        if (!LauncherExperienceIdentity.IsValidId(id) ||
            !LauncherExperienceIdentity.IsOwnedByPublisher(id, publisher))
            throw new CliUsageException("Launcher Experience ID must be owned by its publisher namespace.");
        if (!LauncherExperienceIdentity.TryParseCanonicalVersion(version, out _))
            throw new CliUsageException("Version must use canonical dotted numeric notation.");
    }

    private static string Slug(string name)
    {
        var value = NonPortable().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(value) ? "launcher-experience" : value;
    }

    [GeneratedRegex("[^a-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonPortable();
}

internal static class LauncherThemeValidateCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar launcher-theme validate <directory|file.gbarlauncher>");
        var inspection = await LauncherThemeSource.InspectAsync(parsed.Positionals[0], cancellationToken);
        await output.WriteLineAsync(
            $"Valid Launcher Experience: {inspection.Package.Manifest.Id} " +
            $"{inspection.Package.Manifest.Version} ({inspection.Files.Count} files, " +
            $"{inspection.TotalBytes} bytes, SHA-256 {inspection.Package.Descriptor.ContentDigest}).");
        return 0;
    }
}

internal static class LauncherThemePackCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--output");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar launcher-theme pack <directory> [--output <file.gbarlauncher>]");
        var result = await LauncherExperienceArchive.PackAsync(
            parsed.Positionals[0], parsed.Option("--output"), cancellationToken);
        await output.WriteLineAsync(
            $"Packed {result.Inspection.Package.Manifest.Id} " +
            $"{result.Inspection.Package.Manifest.Version} to {result.ArchivePath}.");
        await output.WriteLineAsync($"SHA-256: {result.Sha256}");
        return 0;
    }
}

internal static class LauncherThemeInspectCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar launcher-theme inspect <file.gbarlauncher>");
        var path = Path.GetFullPath(parsed.Positionals[0]);
        var inspection = await LauncherExperienceArchive.InspectArchiveAsync(path, cancellationToken);
        await using var input = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var archiveDigest = Convert.ToHexString(
            await System.Security.Cryptography.SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
        var package = inspection.Package;
        await output.WriteLineAsync($"Launcher Experience: {package.Manifest.Name}");
        await output.WriteLineAsync($"ID: {package.Manifest.Id}");
        await output.WriteLineAsync($"Publisher claim: {package.Manifest.Publisher}");
        await output.WriteLineAsync($"Version: {package.Manifest.Version}");
        await output.WriteLineAsync($"Preset: {LauncherThemeTemplate.PresetName(package.Manifest.LayoutPreset)}");
        await output.WriteLineAsync($"Files: {inspection.Files.Count}; expanded bytes: {inspection.TotalBytes}");
        await output.WriteLineAsync($"Content SHA-256: {package.Descriptor.ContentDigest}");
        await output.WriteLineAsync($"Archive SHA-256: {archiveDigest}");
        await output.WriteLineAsync("Trust: strict data-only validation passed; publisher identity is not authenticated.");
        return 0;
    }
}

internal static class LauncherThemePreviewCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--output");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar launcher-theme preview <directory|file.gbarlauncher> [--output <preview.json>]");
        var inspection = await LauncherThemeSource.InspectAsync(parsed.Positionals[0], cancellationToken);
        var document = LauncherThemePreview.Create(inspection.Package);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, LauncherThemePreview.JsonOptions);
        if (parsed.Option("--output") is { } requested)
        {
            var destination = Path.GetFullPath(requested);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new LauncherExperiencePackageException(
                    "immutable_preview", "Preview output already exists and will not be overwritten.");
            var parent = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parent);
            LauncherThemePathGuard.EnsureNoReparsePoints(parent);
            var temporary = destination + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            await output.WriteLineAsync(
                $"Wrote {document.Frames.Count} deterministic offscreen preview frames to {destination}.");
        }
        else
        {
            await output.WriteLineAsync(Encoding.UTF8.GetString(bytes));
        }
        return 0;
    }
}

internal static class LauncherThemeInstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar launcher-theme install <file.gbarlauncher> [--catalog <root>]");
        var inspection = await LauncherExperienceArchive.InspectArchiveAsync(
            parsed.Positionals[0], cancellationToken);
        var installed = await LauncherExperienceArchive.InstallAsync(
            inspection, LauncherThemeCatalogRoot.Resolve(parsed.Option("--catalog")), cancellationToken);
        await output.WriteLineAsync(
            $"Installed {inspection.Package.Manifest.Id} {inspection.Package.Manifest.Version} to {installed}.");
        await output.WriteLineAsync("Installation grants presentation data only; it grants no game or content authority.");
        return 0;
    }
}

internal static class LauncherThemeListCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 0)
            throw new CliUsageException("Usage: gbar launcher-theme list [--catalog <root>]");
        var snapshot = new GameBarAlternative.LauncherExperienceCatalog.LauncherExperienceCatalog(
            LauncherThemeCatalogRoot.Resolve(parsed.Option("--catalog"))).Discover();
        foreach (var entry in snapshot.Experiences)
        {
            await output.WriteLineAsync(
                $"{(entry.IsValid ? "valid  " : "invalid")}  {entry.Descriptor.Id}  " +
                $"{entry.Descriptor.Version}  {entry.Descriptor.Name}  " +
                $"[{entry.Descriptor.Publisher}]  {entry.Descriptor.ContentDigest}");
            foreach (var diagnostic in entry.Diagnostics.Take(1))
                await output.WriteLineAsync($"         {diagnostic.Code}: {diagnostic.Message}");
        }
        return 0;
    }
}

internal static class LauncherThemeRemoveCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 2)
            throw new CliUsageException(
                "Usage: gbar launcher-theme remove <exact-id> <exact-version> [--catalog <root>]");
        await LauncherExperienceArchive.RemoveAsync(
            LauncherThemeCatalogRoot.Resolve(parsed.Option("--catalog")),
            parsed.Positionals[0], parsed.Positionals[1], cancellationToken);
        await output.WriteLineAsync($"Removed {parsed.Positionals[0]} {parsed.Positionals[1]}.");
        return 0;
    }
}

internal static class LauncherThemeSource
{
    public static Task<LauncherExperienceArchiveInspection> InspectAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(source);
        if (Directory.Exists(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LauncherExperienceArchive.InspectDirectory(path));
        }
        return LauncherExperienceArchive.InspectArchiveAsync(path, cancellationToken);
    }
}

internal static class LauncherThemeCatalogRoot
{
    public static string Resolve(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new LauncherExperiencePackageException(
                "local_app_data_unavailable", "Local Application Data is unavailable.");
        return Path.Combine(local, "GameBarAlternative", "launcher-experiences");
    }
}

internal static class LauncherThemePathGuard
{
    public static void EnsureNoReparsePoints(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new LauncherExperiencePackageException(
                    "reparse_point", "Authoring output paths cannot contain reparse points.");
            current = current.Parent;
        }
    }
}

internal static class LauncherThemeTemplate
{
    private static readonly byte[] PreviewPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static void Write(
        string root,
        string id,
        string publisher,
        string name,
        string version,
        string preset)
    {
        Directory.CreateDirectory(Path.Combine(root, "layouts"));
        Directory.CreateDirectory(Path.Combine(root, "styles"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        var manifest = new
        {
            schemaVersion = 1,
            id,
            publisher,
            name,
            version,
            layoutPreset = preset,
            compositionFile = "layouts/launcher-layout.json",
            styleFile = "styles/launcher.gbss",
            previewFile = "assets/preview.png",
            parameters = new
            {
                backgroundMode = "selected-game-artwork",
                accent = "#8f80ff",
                tileSize = "large",
                metadataDensity = "standard",
                motionIntensity = "reduced",
                showSystemStatus = true,
                focusEffect = "lift",
            },
        };
        File.WriteAllBytes(Path.Combine(root, "launcher.json"),
            JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        File.WriteAllText(Path.Combine(root, "layouts", "launcher-layout.json"), Recipe,
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "styles", "launcher.gbss"), Style,
            new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(root, "assets", "preview.png"), PreviewPng);
    }

    public static bool IsPreset(string value) => value is
        "hero-rail" or "cover-wall" or "carousel" or "compact-grid";

    public static string PresetName(LauncherLayoutPreset preset) => preset switch
    {
        LauncherLayoutPreset.HeroRail => "hero-rail",
        LauncherLayoutPreset.CoverWall => "cover-wall",
        LauncherLayoutPreset.Carousel => "carousel",
        LauncherLayoutPreset.CompactGrid => "compact-grid",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private const string Style = """
        launcher-game-rail { gap: 12px; }
        launcher-details-panel { background: rgba(18, 20, 30, 0.78); corner-radius: 18px; }
        launcher-controller-hints { color: #f5f5f8; }
        """;

    private const string Recipe = """
        {
          "schemaVersion": 1,
          "branches": {
            "compact": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "source-status", "region": { "x": 0.05, "y": 0.03, "width": 0.35, "height": 0.14 } },
              { "type": "region", "slot": "details-panel", "region": { "x": 0.05, "y": 0.19, "width": 0.9, "height": 0.32 }, "surface": "glass" },
              { "type": "region", "slot": "game-rail", "region": { "x": 0.05, "y": 0.54, "width": 0.9, "height": 0.27 }, "orientation": "horizontal" },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.5, "y": 0.83, "width": 0.45, "height": 0.15 } }
            ] } },
            "standard": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "details-panel", "region": { "x": 0.08, "y": 0.08, "width": 0.5, "height": 0.36 }, "surface": "glass" },
              { "type": "region", "slot": "source-status", "region": { "x": 0.68, "y": 0.08, "width": 0.24, "height": 0.1 } },
              { "type": "region", "slot": "game-rail", "region": { "x": 0.08, "y": 0.55, "width": 0.84, "height": 0.27 }, "orientation": "horizontal" },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.52, "y": 0.88, "width": 0.4, "height": 0.08 } }
            ] } },
            "wide": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "details-panel", "region": { "x": 0.08, "y": 0.08, "width": 0.5, "height": 0.36 }, "surface": "glass" },
              { "type": "region", "slot": "source-status", "region": { "x": 0.68, "y": 0.08, "width": 0.24, "height": 0.1 } },
              { "type": "region", "slot": "game-rail", "region": { "x": 0.08, "y": 0.55, "width": 0.84, "height": 0.27 }, "orientation": "horizontal" },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.52, "y": 0.88, "width": 0.4, "height": 0.08 } }
            ] } }
          }
        }
        """;
}

internal sealed record LauncherThemePreviewDocument(
    int SchemaVersion,
    string PackageId,
    string Version,
    string ContentDigest,
    string SelectedPreset,
    IReadOnlyList<LauncherThemePreviewFrame> Frames);

internal sealed record LauncherThemePreviewFrame(
    string Scenario,
    string Preset,
    string Branch,
    int Width,
    int Height,
    double InterfaceScale,
    bool ReducedMotion,
    bool ReducedTransparency,
    bool HighContrast,
    int TotalItems,
    int RetainedItems,
    string? FirstFixtureItem,
    string? LastFixtureItem,
    string? SelectedTitle,
    string SourceState,
    string ArtworkState,
    string OperationState,
    IReadOnlyList<string> Slots);

internal static class LauncherThemePreview
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static LauncherThemePreviewDocument Create(LauncherExperiencePackage package)
    {
        var frames = new List<LauncherThemePreviewFrame>(132);
        foreach (var scenario in Scenarios)
        foreach (var preset in Enum.GetValues<LauncherLayoutPreset>())
        foreach (var surface in Surfaces)
        {
            var recipe = preset == package.Manifest.LayoutPreset && package.Recipe is not null
                ? package.Recipe
                : LauncherExperienceBuiltIns.RecoveryFor(preset).Recipe!;
            var root = recipe.Branches.TryGetValue(surface.Branch, out var exact)
                ? exact
                : LauncherExperienceBuiltIns.RecoveryFor(preset).Recipe!.Branches[surface.Branch];
            frames.Add(new(
                scenario.Name,
                LauncherThemeTemplate.PresetName(preset),
                surface.Branch.ToString().ToLowerInvariant(),
                surface.Width,
                surface.Height,
                scenario.Scale,
                scenario.ReducedMotion,
                scenario.ReducedTransparency,
                scenario.HighContrast,
                scenario.Items,
                Math.Min(scenario.Items, 64),
                scenario.Items == 0 ? null : "fixture-game-0001",
                scenario.Items == 0 ? null : $"fixture-game-{Math.Min(scenario.Items, 64):D4}",
                scenario.Items == 0 ? null : scenario.LongTitle
                    ? "A deliberately long localized game title that proves wrapping and focus reveal without clipping the primary action"
                    : "Fixture Game 0001",
                scenario.SourceState,
                scenario.Items == 0 ? "none" : scenario.HasArtwork ? "sealed" : "missing",
                scenario.ActiveOperation ? "download-active" : "idle",
                CollectSlots(root)));
        }
        return new(
            1,
            package.Manifest.Id,
            package.Manifest.Version.ToString(),
            package.Descriptor.ContentDigest,
            LauncherThemeTemplate.PresetName(package.Manifest.LayoutPreset),
            frames.AsReadOnly());
    }

    private static IReadOnlyList<string> CollectSlots(LauncherLayoutNode root)
    {
        var slots = new List<string>();
        Visit(root);
        return slots.AsReadOnly();

        void Visit(LauncherLayoutNode node)
        {
            if (node.Slot is { } slot) slots.Add(SlotName(slot));
            foreach (var child in node.Children) Visit(child);
        }
    }

    private static string SlotName(LauncherSlot slot) => slot switch
    {
        LauncherSlot.HeroBackground => "hero-background",
        LauncherSlot.GameRail => "game-rail",
        LauncherSlot.DetailsPanel => "details-panel",
        LauncherSlot.CollectionTabs => "collection-tabs",
        LauncherSlot.SourceStatus => "source-status",
        LauncherSlot.OperationStatus => "operation-status",
        LauncherSlot.SystemStatus => "system-status",
        LauncherSlot.ControllerHints => "controller-hints",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static readonly (LauncherResponsiveBranch Branch, int Width, int Height)[] Surfaces =
    [
        (LauncherResponsiveBranch.Compact, 854, 480),
        (LauncherResponsiveBranch.Standard, 1280, 720),
        (LauncherResponsiveBranch.Wide, 1920, 1040),
    ];

    private static readonly PreviewScenario[] Scenarios =
    [
        new("empty", 0, "online", false, false, false, 1, false, false, false),
        new("twenty-games", 20, "online", true, false, false, 1, false, false, false),
        new("two-thousand-games", 2000, "online", true, false, false, 1, false, false, false),
        new("offline", 20, "offline", true, false, false, 1, false, false, false),
        new("long-title", 20, "online", true, false, true, 1, false, false, false),
        new("missing-art", 20, "online", false, false, false, 1, false, false, false),
        new("active-operation", 20, "online", true, true, false, 1, false, false, false),
        new("interface-scale-150", 20, "online", true, false, false, 1.5, false, false, false),
        new("reduced-motion", 20, "online", true, false, false, 1, true, false, false),
        new("reduced-transparency", 20, "online", true, false, false, 1, false, true, false),
        new("high-contrast", 20, "online", true, false, false, 1, false, false, true),
    ];

    private sealed record PreviewScenario(
        string Name,
        int Items,
        string SourceState,
        bool HasArtwork,
        bool ActiveOperation,
        bool LongTitle,
        double Scale,
        bool ReducedMotion,
        bool ReducedTransparency,
        bool HighContrast);
}
