using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameBarAlternative.PlatformSettings;

[JsonConverter(typeof(JsonStringEnumConverter<MotionPreference>))]
public enum MotionPreference
{
    System,
    Full,
    Reduced,
}

public sealed record AppearanceSettings
{
    public const double MinimumInterfaceScale = 0.8;
    public const double MaximumInterfaceScale = 1.25;
    public const double MinimumTextScale = 0.85;
    public const double MaximumTextScale = 1.5;
    public const double MinimumBackdropOpacity = 0.35;
    public const double MaximumBackdropOpacity = 0.8;

    [JsonRequired]
    public required string ThemeId { get; init; }

    [JsonRequired]
    public required string ThemeVersion { get; init; }

    [JsonRequired]
    public required double InterfaceScale { get; init; }

    [JsonRequired]
    public required double TextScale { get; init; }

    [JsonRequired]
    public required double BackdropOpacity { get; init; }

    [JsonRequired]
    public required MotionPreference Motion { get; init; }

    public static AppearanceSettings Default { get; } = new()
    {
        ThemeId = ThemeIdentity.BuiltInDefault,
        ThemeVersion = ThemeIdentity.BuiltInDefaultVersion,
        InterfaceScale = 1,
        TextScale = 1,
        BackdropOpacity = 0.64,
        Motion = MotionPreference.System,
    };
}

public sealed record PlatformSettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonRequired]
    public required AppearanceSettings Appearance { get; init; }

    public static PlatformSettingsDocument Default { get; } = new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Appearance = AppearanceSettings.Default,
    };
}

public static partial class ThemeIdentity
{
    public const string BuiltInDefault = "builtin.default";
    public const string BuiltInDefaultVersion = "1.0.0";
    public const int MaximumLength = 128;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        value is not "." and not ".." &&
        ThemeIdRegex().IsMatch(value);

    public static bool TryParseCanonicalVersion(string? value, out Version? version)
    {
        version = null;
        return value is { Length: > 0 and <= 64 } &&
               Version.TryParse(value, out version) &&
               string.Equals(version.ToString(), value, StringComparison.Ordinal);
    }

    [GeneratedRegex("\\A[a-z0-9](?:[a-z0-9._-]{0,127})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdRegex();
}

public static class PlatformSettingsValidator
{
    public static IReadOnlyList<PlatformSettingsValidationError> Validate(PlatformSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<PlatformSettingsValidationError>();
        if (document.SchemaVersion != PlatformSettingsDocument.CurrentSchemaVersion)
            Add("$.schemaVersion", "unsupported_version",
                $"Expected settings schema version {PlatformSettingsDocument.CurrentSchemaVersion}.");
        if (document.Appearance is null)
        {
            Add("$.appearance", "required", "Appearance settings are required.");
            return errors;
        }

        var appearance = document.Appearance;
        if (!ThemeIdentity.IsValid(appearance.ThemeId))
            Add("$.appearance.themeId", "invalid_theme_id",
                "Theme ID must be a lowercase portable identifier of at most 128 characters.");
        if (!ThemeIdentity.TryParseCanonicalVersion(appearance.ThemeVersion, out _))
            Add("$.appearance.themeVersion", "invalid_theme_version",
                "Theme version must use canonical dotted numeric notation.");
        Range(appearance.InterfaceScale, AppearanceSettings.MinimumInterfaceScale,
            AppearanceSettings.MaximumInterfaceScale, "$.appearance.interfaceScale");
        Range(appearance.TextScale, AppearanceSettings.MinimumTextScale,
            AppearanceSettings.MaximumTextScale, "$.appearance.textScale");
        Range(appearance.BackdropOpacity, AppearanceSettings.MinimumBackdropOpacity,
            AppearanceSettings.MaximumBackdropOpacity, "$.appearance.backdropOpacity");
        if (!Enum.IsDefined(appearance.Motion))
            Add("$.appearance.motion", "invalid_enum", "Motion preference is invalid.");
        return errors;

        void Range(double value, double minimum, double maximum, string path)
        {
            if (!double.IsFinite(value) || value < minimum || value > maximum)
                Add(path, "out_of_range", $"Value must be between {minimum} and {maximum}.");
        }

        void Add(string path, string code, string message) =>
            errors.Add(new PlatformSettingsValidationError(path, code, message));
    }
}

public sealed record PlatformSettingsValidationError(string Path, string Code, string Message);

public sealed class PlatformSettingsException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
