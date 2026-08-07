using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.PlatformSettings;

public sealed record ThemeDescriptor(
    string Id,
    string Name,
    Version Version,
    bool IsBuiltIn);

public sealed record ThemeCatalogEntry(
    ThemeDescriptor Descriptor,
    bool IsValid,
    IReadOnlyList<GbssDiagnostic> Diagnostics);

public sealed record ThemeCatalogSnapshot(IReadOnlyList<ThemeCatalogEntry> Themes);

public sealed record ThemeLoadResult(
    ThemeDescriptor Descriptor,
    GbssPackageResult Package)
{
    public bool IsValid => Package.IsValid;
    public IReadOnlyList<GbssDiagnostic> Diagnostics => Package.Diagnostics;
}

internal sealed record ThemeManifestDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonRequired]
    public required string Id { get; init; }

    [JsonRequired]
    public required string Name { get; init; }

    [JsonRequired]
    public required string Version { get; init; }

    [JsonRequired]
    public required string EntryFile { get; init; }
}

public sealed class ThemeCatalog
{
    public const int MaximumThemes = 128;
    public const int MaximumManifestBytes = 64 * 1024;
    private const string ManifestFileName = "theme.json";
    private const string BuiltInResource =
        "GameBarAlternative.PlatformSettings.Themes.builtin-default.gbss";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly PlatformSettingsPaths _paths;
    private readonly ThemeLoadResult _builtIn;

    public ThemeCatalog(PlatformSettingsPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _builtIn = LoadBuiltIn();
    }

    public ThemeLoadResult BuiltInDefault => _builtIn;

    public ThemeCatalogSnapshot Discover()
    {
        var entries = new List<ThemeCatalogEntry>
        {
            new(_builtIn.Descriptor, _builtIn.IsValid, _builtIn.Diagnostics),
        };
        if (!Directory.Exists(_paths.ThemesDirectory)) return new ThemeCatalogSnapshot(entries);

        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.ThemesDirectory);
        FileSystemGuard.RejectReparsePoint(_paths.ThemesDirectory);
        var idDirectories = Directory.EnumerateDirectories(_paths.ThemesDirectory)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (idDirectories.Length > MaximumThemes)
            throw new PlatformSettingsException(
                "too_many_themes", $"Theme directory contains more than {MaximumThemes} theme IDs.");
        var packageCount = 0;
        foreach (var idDirectory in idDirectories)
        {
            FileSystemGuard.RejectReparsePoint(idDirectory);
            var directoryId = Path.GetFileName(idDirectory);
            var versions = Directory.EnumerateDirectories(idDirectory)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (versions.Length == 0)
            {
                entries.Add(new ThemeCatalogEntry(
                    FallbackDescriptor(directoryId),
                    false,
                    Invalid(directoryId, "missing_theme_version",
                        "Theme ID directory does not contain a version directory.").Diagnostics));
                continue;
            }
            packageCount = checked(packageCount + versions.Length);
            if (packageCount > MaximumThemes)
                throw new PlatformSettingsException(
                    "too_many_themes", $"Theme directory contains more than {MaximumThemes} versioned themes.");
            foreach (var versionDirectory in versions)
            {
                var versionText = Path.GetFileName(versionDirectory);
                var loaded = LoadDirectory(versionDirectory, directoryId, versionText);
                entries.Add(new ThemeCatalogEntry(
                    loaded.Descriptor,
                    loaded.IsValid,
                    loaded.Diagnostics));
            }
        }
        if (packageCount > MaximumThemes)
            throw new PlatformSettingsException(
                "too_many_themes", $"Theme directory contains more than {MaximumThemes} versioned themes.");
        return new ThemeCatalogSnapshot(entries);
    }

    public ThemeLoadResult Load(string themeId, string version)
    {
        if (!ThemeIdentity.IsValid(themeId))
            return Invalid(themeId ?? string.Empty, "invalid_theme_id", "Theme ID is invalid.");
        if (!ThemeIdentity.TryParseCanonicalVersion(version, out _))
            return Invalid(themeId, "invalid_theme_version", "Theme version is invalid.");
        if (themeId == ThemeIdentity.BuiltInDefault)
            return version == ThemeIdentity.BuiltInDefaultVersion
                ? _builtIn
                : Invalid(themeId, "theme_not_found", $"Theme '{themeId}' {version} is not installed.");
        if (!Directory.Exists(_paths.ThemesDirectory))
            return Invalid(themeId, "theme_not_found", $"Theme '{themeId}' {version} is not installed.");

        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.ThemesDirectory);
        FileSystemGuard.RejectReparsePoint(_paths.ThemesDirectory);
        var idDirectory = Directory.EnumerateDirectories(_paths.ThemesDirectory)
            .SingleOrDefault(path => string.Equals(Path.GetFileName(path), themeId, StringComparison.Ordinal));
        if (idDirectory is null)
            return Invalid(themeId, "theme_not_found", $"Theme '{themeId}' {version} is not installed.");
        FileSystemGuard.RejectReparsePoint(idDirectory);
        var match = Directory.EnumerateDirectories(idDirectory)
            .SingleOrDefault(path => string.Equals(Path.GetFileName(path), version, StringComparison.Ordinal));
        return match is null
            ? Invalid(themeId, "theme_not_found", $"Theme '{themeId}' {version} is not installed.")
            : LoadDirectory(match, themeId, version);
    }

    private ThemeLoadResult LoadDirectory(string directory, string directoryId, string directoryVersion)
    {
        var fallback = FallbackDescriptor(directoryId, directoryVersion);
        try
        {
            FileSystemGuard.RejectReparsePoint(directory);
            if (!FileSystemGuard.IsWithin(_paths.ThemesDirectory, directory))
                return Invalid(fallback, "theme_path_escape", "Theme directory escapes the theme root.");
            if (!ThemeIdentity.IsValid(directoryId) || directoryId == ThemeIdentity.BuiltInDefault)
                return Invalid(fallback, "invalid_theme_id", "Theme directory name is invalid or reserved.");
            if (!ThemeIdentity.TryParseCanonicalVersion(directoryVersion, out _))
                return Invalid(fallback, "invalid_theme_version", "Theme version directory is invalid.");

            var manifest = Directory.EnumerateFiles(directory)
                .SingleOrDefault(path =>
                    string.Equals(Path.GetFileName(path), ManifestFileName, StringComparison.Ordinal));
            if (manifest is null)
                return Invalid(fallback, "missing_theme_manifest",
                    "Theme directory must contain exact-case theme.json.");
            FileSystemGuard.RejectReparsePoint(manifest);
            var manifestBytes = ReadBounded(manifest, MaximumManifestBytes, "theme_manifest_too_large");
            ThemeManifestDocument document;
            try
            {
                StrictJson.RejectDuplicateProperties(manifestBytes);
                document = JsonSerializer.Deserialize<ThemeManifestDocument>(manifestBytes, JsonOptions)
                    ?? throw new JsonException("Theme manifest was null.");
            }
            catch (JsonException exception)
            {
                return Invalid(fallback, "invalid_theme_manifest",
                    $"Theme manifest JSON is invalid: {SafeMessage(exception.Message)}");
            }

            var manifestError = ValidateManifest(document, directoryId, directoryVersion, out var version);
            var descriptor = new ThemeDescriptor(
                document.Id ?? directoryId,
                document.Name ?? directoryId,
                version ?? new Version(0, 0, 0),
                IsBuiltIn: false);
            if (manifestError is not null)
                return Invalid(descriptor, manifestError.Value.Code, manifestError.Value.Message);

            var package = GbssPackageLoader.Load(
                document.EntryFile,
                new GbssFileSourceProvider(directory));
            return new ThemeLoadResult(descriptor, SanitizePackage(package));
        }
        catch (PlatformSettingsException exception)
        {
            return Invalid(fallback, exception.Code, SafeMessage(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(fallback, "theme_io_error", SafeMessage(exception.Message));
        }
    }

    private static (string Code, string Message)? ValidateManifest(
        ThemeManifestDocument document,
        string directoryId,
        string directoryVersion,
        out Version? version)
    {
        version = null;
        if (document.SchemaVersion != ThemeManifestDocument.CurrentSchemaVersion)
            return ("unsupported_theme_version",
                $"Expected theme manifest version {ThemeManifestDocument.CurrentSchemaVersion}.");
        if (!ThemeIdentity.IsValid(document.Id) || document.Id == ThemeIdentity.BuiltInDefault)
            return ("invalid_theme_id", "Theme manifest ID is invalid or reserved.");
        if (!string.Equals(document.Id, directoryId, StringComparison.Ordinal))
            return ("theme_identity_mismatch", "Theme manifest ID must exactly match its directory name.");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 80 ||
            document.Name.Any(character => char.IsControl(character)))
            return ("invalid_theme_name", "Theme name must contain 1 to 80 printable characters.");
        if (!ThemeIdentity.TryParseCanonicalVersion(document.Version, out version))
            return ("invalid_theme_version", "Theme version must use canonical dotted numeric notation.");
        if (!string.Equals(document.Version, directoryVersion, StringComparison.Ordinal))
            return ("theme_identity_mismatch", "Theme manifest version must exactly match its version directory.");
        if (!GbssPackageLoader.IsSafePackagePath(document.EntryFile))
            return ("invalid_theme_entry", "Theme entry must be a normalized package-relative .gbss path.");
        return null;
    }

    private static ThemeLoadResult LoadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResource)
            ?? throw new PlatformSettingsException(
                "missing_builtin_theme", "The built-in default theme resource is missing.");
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var parsed = GbssParser.Parse(source, "builtin/default.gbss");
        var package = new GbssPackageResult([parsed.Document], parsed.Diagnostics);
        var compiled = GbssThemeCompiler.Compile(package);
        if (!compiled.IsValid)
            throw new PlatformSettingsException(
                "invalid_builtin_theme",
                "The built-in default theme failed GBSS validation.");
        return new ThemeLoadResult(
            new ThemeDescriptor(
                ThemeIdentity.BuiltInDefault,
                "Default",
                Version.Parse(ThemeIdentity.BuiltInDefaultVersion),
                IsBuiltIn: true),
            package);
    }

    private static ThemeLoadResult Invalid(string id, string code, string message) =>
        Invalid(FallbackDescriptor(id), code, message);

    private static ThemeDescriptor FallbackDescriptor(string id, string? version = null) =>
        new(
            ThemeIdentity.IsValid(id) ? id : "invalid-theme",
            string.IsNullOrWhiteSpace(id) ? "Invalid theme" : id,
            ThemeIdentity.TryParseCanonicalVersion(version, out var parsed)
                ? parsed!
                : new Version(0, 0, 0),
            IsBuiltIn: false);

    private static ThemeLoadResult Invalid(
        ThemeDescriptor descriptor,
        string code,
        string message) => new(
            descriptor,
            new GbssPackageResult(
                [],
                [new GbssDiagnostic(
                    "theme.json", 1, 1, GbssDiagnosticSeverity.Error, code, message)]));

    private static GbssPackageResult SanitizePackage(GbssPackageResult package) => new(
        package.Documents,
        package.Diagnostics.Select(item => item with
        {
            Source = SafeSource(item.Source),
            Message = SafeMessage(item.Message),
        }).ToArray());

    private static byte[] ReadBounded(string path, int maximumBytes, string code)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = stream.Read(buffer);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new PlatformSettingsException(code, $"File exceeds {maximumBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string SafeSource(string value)
    {
        var normalized = value.Replace('\\', '/');
        return normalized.Length <= 256 && !Path.IsPathRooted(normalized)
            ? normalized
            : "theme.gbss";
    }

    private static string SafeMessage(string value)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 512 ? oneLine : oneLine[..512];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
