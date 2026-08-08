using System.Globalization;
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
    /// <summary>
    /// Safe shell presentation metadata. Icons are semantic host glyphs, never
    /// package paths, font names, SVG, or executable drawing content.
    /// </summary>
    public WidgetPresentation Presentation { get; init; } = new();
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<string> OptionalPermissions { get; init; } = [];
    /// <summary>
    /// Legacy manifest-v1 spelling. <c>none</c> migrates to keep-alive and
    /// <c>suspend</c> migrates to suspend-when-hidden. New packages should use
    /// <see cref="ResidencyPolicy"/>; declaring both is invalid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetResidencyPolicy? ResidencyPolicy { get; init; }
    public WidgetResourceRequest ResourceRequest { get; init; } = new();
    public IReadOnlyList<string> Architectures { get; init; } = ["x64"];
}

public sealed record HostApiRange(string Minimum, int MaximumMajor);
public sealed record WidgetEntrypoint(string Runtime, string Assembly, string Type);
public sealed record WidgetPresentation(WidgetGlyph Icon = WidgetGlyph.Connection);
public sealed record WidgetResourceRequest(int MemoryMb = 48, int UpdateHz = 1);
public sealed record ManifestValidationError(string Path, string Code, string Message);

/// <summary>
/// Versioned, declarative worker-process residency request. Lifecycle remains
/// host authoritative and is independent from whether the process is resident.
/// </summary>
public sealed record WidgetResidencyPolicy
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Mode { get; init; } = WidgetResidencyPolicies.KeepAlive;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IdleSeconds { get; init; }
}

public enum WidgetResidencyMode
{
    KeepAlive,
    SuspendWhenHidden,
    UnloadAfterIdle,
}

public sealed record ResolvedWidgetResidencyPolicy(
    WidgetResidencyMode Mode,
    TimeSpan? IdleDuration = null)
{
    public static readonly ResolvedWidgetResidencyPolicy KeepAlive =
        new(WidgetResidencyMode.KeepAlive);
}

public static class WidgetResidencyPolicies
{
    public const string KeepAlive = "keep-alive";
    public const string SuspendWhenHidden = "suspend-when-hidden";
    public const string UnloadAfterIdle = "unload-after-idle";
    public const int MinimumIdleSeconds = 5;
    public const int MaximumIdleSeconds = 86_400;

    public static ResolvedWidgetResidencyPolicy Resolve(WidgetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ResidencyPolicy is { } policy) return Resolve(policy);
        return manifest.BackgroundPolicy switch
        {
            null or "none" => ResolvedWidgetResidencyPolicy.KeepAlive,
            "suspend" => new(WidgetResidencyMode.SuspendWhenHidden),
            _ => throw new ArgumentException("The legacy background policy is invalid.", nameof(manifest)),
        };
    }

    public static ResolvedWidgetResidencyPolicy Resolve(WidgetResidencyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Mode switch
        {
            KeepAlive => ResolvedWidgetResidencyPolicy.KeepAlive,
            SuspendWhenHidden => new(WidgetResidencyMode.SuspendWhenHidden),
            UnloadAfterIdle when policy.IdleSeconds is { } seconds =>
                new(WidgetResidencyMode.UnloadAfterIdle, TimeSpan.FromSeconds(seconds)),
            _ => throw new ArgumentException("The residency policy is invalid.", nameof(policy)),
        };
    }

    public static void Validate(
        WidgetResidencyPolicy? policy,
        string path,
        Action<string, string, string> add)
    {
        if (policy is null) return;
        if (policy.SchemaVersion != WidgetResidencyPolicy.CurrentSchemaVersion)
            add($"{path}.schemaVersion", "unsupported_version",
                $"Expected residency policy schema {WidgetResidencyPolicy.CurrentSchemaVersion}.");
        if (policy.Mode is not (KeepAlive or SuspendWhenHidden or UnloadAfterIdle))
            add($"{path}.mode", "unsupported_policy",
                $"Supported residency policies are '{KeepAlive}', '{SuspendWhenHidden}', and '{UnloadAfterIdle}'.");
        if (policy.Mode == UnloadAfterIdle)
        {
            if (policy.IdleSeconds is not (>= MinimumIdleSeconds and <= MaximumIdleSeconds))
                add($"{path}.idleSeconds", "out_of_range",
                    $"Idle unload must be between {MinimumIdleSeconds} and {MaximumIdleSeconds} seconds.");
        }
        else if (policy.IdleSeconds is not null)
        {
            add($"{path}.idleSeconds", "not_applicable",
                "idleSeconds is valid only for unload-after-idle.");
        }
    }
}

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
        if (!IsPackageIdentity(manifest.Id))
            Add("$.id", "invalid_id", "ID must be a reverse-DNS identifier using lowercase letters, digits, underscores and hyphens.");
        if (!IsPackageIdentity(manifest.Publisher))
            Add("$.publisher", "invalid_publisher", "Publisher must be a reverse-DNS identifier.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80 ||
            ContainsUnsafeText(manifest.Name))
            Add("$.name", "invalid_name",
                "Name must contain 1 to 80 characters without control, formatting, or bidi-override characters.");
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

        if (manifest.Presentation is null)
            Add("$.presentation", "required", "Presentation cannot be null.");
        else if (!Enum.IsDefined(manifest.Presentation.Icon))
            Add("$.presentation.icon", "unsupported_icon",
                "Presentation icon must use a host-defined semantic glyph.");

        if (manifest.BackgroundPolicy is not null &&
            !SupportedBackgroundPolicies.Contains(manifest.BackgroundPolicy))
            Add("$.backgroundPolicy", "unsupported_policy",
                "Legacy backgroundPolicy supports only 'none' and 'suspend'. Use residencyPolicy for new packages.");
        if (manifest.BackgroundPolicy is not null && manifest.ResidencyPolicy is not null)
            Add("$.residencyPolicy", "conflicting_policy",
                "Declare residencyPolicy or legacy backgroundPolicy, not both.");
        WidgetResidencyPolicies.Validate(manifest.ResidencyPolicy, "$.residencyPolicy", Add);
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
        if ((long)permissions.Count + optionalPermissions.Count >
            ProtocolConstants.MaximumManifestPermissionCount)
            Add("$.permissions", "too_many_permissions",
                $"A manifest may declare at most {ProtocolConstants.MaximumManifestPermissionCount} required and optional permissions in total.");
        CheckPermissions(permissions, "$.permissions");
        CheckPermissions(optionalPermissions, "$.optionalPermissions");
        var validationLimit = ProtocolConstants.MaximumManifestPermissionCount + 1;
        var required = permissions.Take(validationLimit)
            .Where(permission => permission is not null)
            .ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(optionalPermissions.Count, validationLimit); i++)
            if (optionalPermissions[i] is { } permission && required.Contains(permission))
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
            var count = Math.Min(
                permissions.Count,
                ProtocolConstants.MaximumManifestPermissionCount + 1);
            for (var i = 0; i < count; i++)
            {
                var permission = permissions[i];
                if (string.IsNullOrWhiteSpace(permission) ||
                    ContainsUnsafeText(permission) ||
                    (permission.Length <= ProtocolConstants.MaximumCapabilityIdLength &&
                     !PermissionRegex().IsMatch(permission)))
                    Add($"{path}[{i}]", "invalid_permission", "Permission syntax is invalid.");
                if (permission?.Length > ProtocolConstants.MaximumCapabilityIdLength)
                    Add($"{path}[{i}]", "too_long",
                        $"Permission IDs may not exceed {ProtocolConstants.MaximumCapabilityIdLength} characters.");
                if (permission is not null && !seen.Add(permission))
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

    private static bool IsPackageIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= ProtocolConstants.MaximumCapabilityIdLength &&
        !ContainsUnsafeText(value) &&
        PackageIdRegex().IsMatch(value);

    private static bool ContainsUnsafeText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var category = char.GetUnicodeCategory(character);
            if (char.IsControl(character) || category is UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return true;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return true;
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return true;
            }
        }
        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex("^[a-z](?:[a-z0-9-]*[a-z0-9])?(?:\\.[a-z](?:[a-z0-9-]*[a-z0-9])?)*(?::[A-Za-z0-9._-]+)?$", RegexOptions.CultureInvariant)]
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
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static WidgetManifest Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<WidgetManifest>(payload, Options)
        ?? throw new JsonException("The manifest payload was null.");

    public static byte[] Serialize(WidgetManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
}
