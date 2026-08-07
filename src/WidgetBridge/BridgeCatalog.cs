using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetStyling;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.WidgetBridge;

public sealed record BridgeQuickActionDescriptor
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string ActionId { get; init; }
    public required string SourceElementId { get; init; }
    public ControllerButton? ControllerButton { get; init; }
}

public sealed record BridgeWidgetDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required WidgetGlyph Icon { get; init; }
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
}

internal sealed record ConfiguredWidget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public WidgetGlyph Icon { get; init; } = WidgetGlyph.Connection;
    public required string WorkerExecutable { get; init; }
    public string? StyleFile { get; init; }
    public IReadOnlyList<string> WorkerArguments { get; init; } = [];
    /// <summary>Trusted host policy; worker manifests and IPC cannot override it.</summary>
    public int MemoryLimitMb { get; init; } = 64;
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
    [JsonIgnore]
    public GbssTheme? CompiledTheme { get; init; }
    [JsonIgnore]
    public GbssPackageResult StylePackage { get; init; } = new([], []);

    public BridgeWidgetDescriptor PublicDescriptor() => new()
    {
        Id = Id,
        Name = Name,
        InstanceId = InstanceId,
        Icon = Icon,
        QuickActions = QuickActions,
    };
}

internal sealed record BridgeCatalogDocument
{
    public int CatalogVersion { get; init; } = 1;
    public IReadOnlyList<ConfiguredWidget> Widgets { get; init; } = [];
}

public sealed class BridgeCatalog
{
    private readonly IReadOnlyDictionary<string, ConfiguredWidget> _configured;
    private readonly IReadOnlyList<ConfiguredWidget> _ordered;

    private BridgeCatalog(IEnumerable<ConfiguredWidget> configured)
    {
        _ordered = configured.ToArray();
        _configured = _ordered.ToDictionary(widget => widget.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<BridgeWidgetDescriptor> Widgets =>
        _ordered.Select(widget => widget.PublicDescriptor()).ToArray();

    internal ConfiguredWidget GetConfigured(string widgetId)
    {
        if (!_configured.TryGetValue(widgetId, out var widget))
            throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
        return widget;
    }

    public static BridgeCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        BridgeCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BridgeCatalogDocument>(File.ReadAllBytes(fullPath), options)
                ?? throw new BridgeCatalogException("Catalog document was null.");
        }
        catch (JsonException exception)
        {
            throw new BridgeCatalogException("Catalog JSON is invalid or contains unknown properties.", exception);
        }
        if (document.CatalogVersion != 1)
            throw new BridgeCatalogException("Only catalog version 1 is supported.");
        if (document.Widgets is null || document.Widgets.Count > 256)
            throw new BridgeCatalogException("Catalog must contain at most 256 widgets.");

        var widgets = new Dictionary<string, ConfiguredWidget>(StringComparer.Ordinal);
        foreach (var source in document.Widgets)
        {
            ValidateIdentifier(source.Id, "widget ID");
            ValidateIdentifier(source.InstanceId, "instance ID");
            ValidateLabel(source.Name, "widget name");
            if (source.WorkerArguments is null || source.WorkerArguments.Count > 64 ||
                source.WorkerArguments.Any(argument => argument is null || argument.Length > 4096))
                throw new BridgeCatalogException($"Widget '{source.Id}' has invalid worker arguments.");
            if (source.MemoryLimitMb is < 16 or > 256)
                throw new BridgeCatalogException(
                    $"Widget '{source.Id}' memoryLimitMb must be between 16 and 256.");
            if (source.QuickActions is null || source.QuickActions.Count > 16)
                throw new BridgeCatalogException($"Widget '{source.Id}' has too many quick actions.");

            var executable = Path.GetFullPath(source.WorkerExecutable, directory);
            if (!File.Exists(executable))
                throw new BridgeCatalogException($"Worker executable for '{source.Id}' does not exist.");
            var style = CompileTheme(source, directory);
            var quickActionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in source.QuickActions)
            {
                ValidateIdentifier(action.Id, "quick action ID");
                ValidateIdentifier(action.ActionId, "action ID");
                ValidateIdentifier(action.SourceElementId, "source element ID");
                ValidateLabel(action.Label, "quick action label");
                if (!quickActionIds.Add(action.Id))
                    throw new BridgeCatalogException($"Widget '{source.Id}' repeats quick action '{action.Id}'.");
            }
            if (!widgets.TryAdd(source.Id, source with
                {
                    WorkerExecutable = executable,
                    CompiledTheme = style.Theme,
                    StylePackage = style.Package,
                }))
                throw new BridgeCatalogException($"Widget ID '{source.Id}' is duplicated.");
        }
        return new BridgeCatalog(widgets.Values);
    }

    public static async Task<BridgeCatalogLoadResult> LoadWithInstalledAsync(
        string trustedCatalogPath,
        string installedCatalogRoot,
        string workerHostExecutable,
        CancellationToken cancellationToken = default)
    {
        var trusted = Load(trustedCatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedCatalogRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerHostExecutable);
        var workerHost = Path.GetFullPath(workerHostExecutable);
        var warnings = new List<string>();
        WidgetCatalogSnapshot installed;
        try
        {
            installed = await new CatalogService(
                installedCatalogRoot).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WidgetPackageException or IOException or UnauthorizedAccessException)
        {
            var code = exception is WidgetPackageException package ? package.Code : "catalog_unavailable";
            warnings.Add($"Installed widget catalog was ignored ({SafeDiagnostic(code)}).");
            return new BridgeCatalogLoadResult(trusted, warnings);
        }

        var combined = trusted._ordered.ToList();
        var known = combined.Select(widget => widget.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var widget in installed.Widgets.Where(item => item.Enabled))
        {
            if (combined.Count == 256)
            {
                warnings.Add("Enabled installed widgets exceeded the 256-widget host limit; remaining entries were ignored.");
                break;
            }
            var manifest = widget.ActiveVersion.Manifest;
            if (!IsBridgeIdentifier(manifest.Id) || !IsBridgeLabel(manifest.Name))
            {
                warnings.Add("An enabled installed widget had an ID or name outside bridge bounds and was ignored.");
                continue;
            }
            if (!known.Add(manifest.Id))
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' conflicts with a trusted widget and was ignored.");
                continue;
            }
            if (!SupportsCurrentHost(manifest))
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' is incompatible with this host and was ignored.");
                continue;
            }
            if (manifest.Permissions.Count != 0)
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' requires capabilities that are not yet broker-connected and was ignored.");
                continue;
            }
            if (!File.Exists(workerHost))
            {
                warnings.Add("Installed widgets are enabled, but the generic worker host is not packaged.");
                break;
            }
            var packageRoot = widget.ActiveVersion.InstallPath;
            var assembly = Path.GetFullPath(
                manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar),
                packageRoot);
            var styleFile = File.Exists(Path.Combine(packageRoot, "styles", "default.gbss"))
                ? "styles/default.gbss"
                : null;
            CompiledWidgetStyle style;
            try
            {
                style = CompileTheme(new ConfiguredWidget
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    InstanceId = InstalledInstanceId(manifest.Id, manifest.Version),
                    WorkerExecutable = workerHost,
                    WorkerArguments = [],
                    StyleFile = styleFile,
                }, packageRoot);
            }
            catch (Exception exception) when (exception is BridgeCatalogException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' has invalid styles and was ignored.");
                continue;
            }
            combined.Add(new ConfiguredWidget
            {
                Id = manifest.Id,
                Name = manifest.Name,
                InstanceId = InstalledInstanceId(manifest.Id, manifest.Version),
                Icon = WidgetGlyph.Connection,
                WorkerExecutable = workerHost,
                WorkerArguments =
                [
                    "--package-root", packageRoot,
                    "--widget-assembly", assembly,
                    "--widget-type", manifest.Entrypoint.Type,
                ],
                StyleFile = styleFile,
                // Community manifests describe expected usage but do not set
                // enforcement policy. The trusted host owns this fixed cap.
                MemoryLimitMb = 64,
                QuickActions = [],
                CompiledTheme = style.Theme,
                StylePackage = style.Package,
            });
        }
        return new BridgeCatalogLoadResult(new BridgeCatalog(combined), warnings);
    }

    private static CompiledWidgetStyle CompileTheme(ConfiguredWidget source, string packageRoot)
    {
        if (source.StyleFile is null) return new(new GbssPackageResult([], []), null);
        if (!GbssPackageLoader.IsSafePackagePath(source.StyleFile))
            throw new BridgeCatalogException(
                $"Widget '{source.Id}' styleFile must be a normalized package-relative .gbss path.");
        var package = GbssPackageLoader.Load(
            source.StyleFile,
            new GbssFileSourceProvider(packageRoot));
        var compiled = GbssThemeCompiler.Compile(package);
        if (compiled.IsValid) return new(package, compiled.Theme!);

        var diagnostics = compiled.Diagnostics
            .Where(item => item.Severity == GbssDiagnosticSeverity.Error)
            .Take(8)
            .Select(item =>
                $"{SafeDiagnostic(item.Source)}({item.Line},{item.Column}) {item.Code}: {SafeDiagnostic(item.Message)}")
            .ToArray();
        var summary = diagnostics.Length == 0
            ? "unknown GBSS compilation error"
            : string.Join("; ", diagnostics);
        throw new BridgeCatalogException($"Widget '{source.Id}' style is invalid: {summary}");
    }

    private sealed record CompiledWidgetStyle(GbssPackageResult Package, GbssTheme? Theme);

    private static string SafeDiagnostic(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 256 ? singleLine : singleLine[..256];
    }

    private static void ValidateIdentifier(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new BridgeCatalogException($"The {label} is invalid.");
    }

    private static void ValidateLabel(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new BridgeCatalogException($"The {label} is invalid.");
    }

    private static bool IsBridgeIdentifier(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static bool IsBridgeLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    private static bool SupportsCurrentHost(WidgetManifest manifest)
    {
        if (!Version.TryParse(manifest.HostApi.Minimum, out var minimum) ||
            minimum.Major > BridgeProtocol.CurrentVersion ||
            manifest.HostApi.MaximumMajor < BridgeProtocol.CurrentVersion)
            return false;
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };
        return architecture.Length != 0 && manifest.Architectures.Contains(architecture, StringComparer.Ordinal);
    }

    private static string InstalledInstanceId(string id, string version)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{id}@{version}"));
        return $"installed.{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}

public sealed record BridgeCatalogLoadResult(
    BridgeCatalog Catalog,
    IReadOnlyList<string> Warnings);

public sealed class BridgeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
