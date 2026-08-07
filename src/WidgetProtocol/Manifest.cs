using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameBarAlternative.WidgetProtocol;

public sealed record WidgetManifest
{
    public int ManifestVersion { get; init; } = ProtocolConstants.CurrentManifestVersion;
    public required string Id { get; init; }
    public required string Publisher { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required HostApiRange HostApi { get; init; }
    public required WidgetEntrypoint Entrypoint { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<string> OptionalPermissions { get; init; } = [];
    public string BackgroundPolicy { get; init; } = "none";
    public WidgetResourceRequest ResourceRequest { get; init; } = new();
    public IReadOnlyList<string> Architectures { get; init; } = ["x64"];
}

public sealed record HostApiRange(string Minimum, int MaximumMajor);
public sealed record WidgetEntrypoint(string Runtime, string Assembly, string Type);
public sealed record WidgetResourceRequest(int MemoryMb = 48, int UpdateHz = 1);
public sealed record ManifestValidationError(string Path, string Code, string Message);

public static partial class WidgetManifestValidator
{
    private static readonly HashSet<string> SupportedRuntimes = new(StringComparer.Ordinal) { "dotnet-worker" };
    private static readonly HashSet<string> SupportedArchitectures = new(StringComparer.Ordinal) { "x64", "arm64" };
    private static readonly HashSet<string> SupportedBackgroundPolicies = new(StringComparer.Ordinal) { "none", "suspend" };

    public static IReadOnlyList<ManifestValidationError> Validate(WidgetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<ManifestValidationError>();

        if (manifest.ManifestVersion != ProtocolConstants.CurrentManifestVersion)
            Add("$.manifestVersion", "unsupported_version", $"Expected manifest version {ProtocolConstants.CurrentManifestVersion}.");
        if (!PackageIdRegex().IsMatch(manifest.Id ?? string.Empty))
            Add("$.id", "invalid_id", "ID must be a reverse-DNS identifier using lowercase letters, digits, underscores and hyphens.");
        if (!PackageIdRegex().IsMatch(manifest.Publisher ?? string.Empty))
            Add("$.publisher", "invalid_publisher", "Publisher must be a reverse-DNS identifier.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80)
            Add("$.name", "invalid_name", "Name must contain 1 to 80 characters.");
        if (!System.Version.TryParse(manifest.Version, out _))
            Add("$.version", "invalid_version", "Version must be a valid dotted numeric version.");
        System.Version? minimum = null;
        if (manifest.HostApi is null || !System.Version.TryParse(manifest.HostApi.Minimum, out minimum) || minimum.Major < 1)
            Add("$.hostApi.minimum", "invalid_version", "Minimum host API must be a valid version with major >= 1.");
        if (manifest.HostApi is null || manifest.HostApi.MaximumMajor < 1 ||
            (minimum is not null && manifest.HostApi.MaximumMajor < minimum.Major))
            Add("$.hostApi.maximumMajor", "invalid_range", "Maximum major must include the minimum host API.");

        if (manifest.Entrypoint is null)
            Add("$.entrypoint", "required", "Entrypoint is required.");
        else
        {
            if (!SupportedRuntimes.Contains(manifest.Entrypoint.Runtime ?? string.Empty))
                Add("$.entrypoint.runtime", "unsupported_runtime", "Only 'dotnet-worker' is currently supported.");
            if (!IsRelativePackagePath(manifest.Entrypoint.Assembly))
                Add("$.entrypoint.assembly", "invalid_path", "Assembly must be a normalized package-relative path without traversal segments.");
            if (string.IsNullOrWhiteSpace(manifest.Entrypoint.Type) || !manifest.Entrypoint.Type.Contains('.', StringComparison.Ordinal))
                Add("$.entrypoint.type", "invalid_type", "Entrypoint type must be namespace-qualified.");
        }

        if (!SupportedBackgroundPolicies.Contains(manifest.BackgroundPolicy ?? string.Empty))
            Add("$.backgroundPolicy", "unsupported_policy", "Supported policies are 'none' and 'suspend'.");
        if (manifest.ResourceRequest is null)
            Add("$.resourceRequest", "required", "Resource request is required.");
        else
        {
            if (manifest.ResourceRequest.MemoryMb is < 16 or > 256)
                Add("$.resourceRequest.memoryMb", "out_of_range", "Memory request must be between 16 and 256 MB.");
            if (manifest.ResourceRequest.UpdateHz is < 1 or > 60)
                Add("$.resourceRequest.updateHz", "out_of_range", "Update rate must be between 1 and 60 Hz.");
        }

        var permissions = manifest.Permissions ?? [];
        var optionalPermissions = manifest.OptionalPermissions ?? [];
        if (manifest.Permissions is null) Add("$.permissions", "required", "Permissions cannot be null.");
        if (manifest.OptionalPermissions is null) Add("$.optionalPermissions", "required", "Optional permissions cannot be null.");
        CheckPermissions(permissions, "$.permissions");
        CheckPermissions(optionalPermissions, "$.optionalPermissions");
        var required = permissions.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < optionalPermissions.Count; i++)
            if (required.Contains(optionalPermissions[i]))
                Add($"$.optionalPermissions[{i}]", "duplicate_permission", "A permission cannot be both required and optional.");

        var architectures = manifest.Architectures ?? [];
        if (architectures.Count == 0)
            Add("$.architectures", "required", "At least one architecture is required.");
        for (var i = 0; i < architectures.Count; i++)
            if (!SupportedArchitectures.Contains(architectures[i]))
                Add($"$.architectures[{i}]", "unsupported_architecture", "Supported architectures are x64 and arm64.");

        return errors;

        void CheckPermissions(IReadOnlyList<string> permissions, string path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < permissions.Count; i++)
            {
                var permission = permissions[i];
                if (!PermissionRegex().IsMatch(permission))
                    Add($"{path}[{i}]", "invalid_permission", "Permission syntax is invalid.");
                if (!seen.Add(permission))
                    Add($"{path}[{i}]", "duplicate_permission", "Permission is declared more than once.");
            }
        }

        void Add(string path, string code, string message) => errors.Add(new(path, code, message));
    }

    private static bool IsRelativePackagePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        !value.Contains('\\') &&
        !value.Split('/').Any(segment => segment is ".." or "." or "");

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9]*)*(?::[A-Za-z0-9._-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionRegex();
}

public static class ManifestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static WidgetManifest Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<WidgetManifest>(payload, Options)
        ?? throw new JsonException("The manifest payload was null.");

    public static byte[] Serialize(WidgetManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
}
