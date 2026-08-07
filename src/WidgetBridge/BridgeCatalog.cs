using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetStyling;

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
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
}

internal sealed record ConfiguredWidget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required string WorkerExecutable { get; init; }
    public string? StyleFile { get; init; }
    public IReadOnlyList<string> WorkerArguments { get; init; } = [];
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
    [JsonIgnore]
    public GbssTheme? CompiledTheme { get; init; }

    public BridgeWidgetDescriptor PublicDescriptor() => new()
    {
        Id = Id,
        Name = Name,
        InstanceId = InstanceId,
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

    private BridgeCatalog(IReadOnlyDictionary<string, ConfiguredWidget> configured) => _configured = configured;

    public IReadOnlyList<BridgeWidgetDescriptor> Widgets =>
        _configured.Values.Select(widget => widget.PublicDescriptor()).ToArray();

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
            if (source.QuickActions is null || source.QuickActions.Count > 16)
                throw new BridgeCatalogException($"Widget '{source.Id}' has too many quick actions.");

            var executable = Path.GetFullPath(source.WorkerExecutable, directory);
            if (!File.Exists(executable))
                throw new BridgeCatalogException($"Worker executable for '{source.Id}' does not exist.");
            var theme = CompileTheme(source, directory);
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
                    CompiledTheme = theme,
                }))
                throw new BridgeCatalogException($"Widget ID '{source.Id}' is duplicated.");
        }
        return new BridgeCatalog(widgets);
    }

    private static GbssTheme? CompileTheme(ConfiguredWidget source, string packageRoot)
    {
        if (source.StyleFile is null) return null;
        if (!GbssPackageLoader.IsSafePackagePath(source.StyleFile))
            throw new BridgeCatalogException(
                $"Widget '{source.Id}' styleFile must be a normalized package-relative .gbss path.");
        var package = GbssPackageLoader.Load(
            source.StyleFile,
            new GbssFileSourceProvider(packageRoot));
        var compiled = GbssThemeCompiler.Compile(package);
        if (compiled.IsValid) return compiled.Theme!;

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
}

public sealed class BridgeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
