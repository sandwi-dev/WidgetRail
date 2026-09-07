using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WidgetRail.PlatformSettings;

[JsonConverter(typeof(JsonStringEnumConverter<MotionPreference>))]
public enum MotionPreference
{
    System,
    Full,
    Reduced,
}

[JsonConverter(typeof(JsonStringEnumConverter<ContrastPreference>))]
public enum ContrastPreference
{
    System,
    Standard,
    High,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransparencyPreference>))]
public enum TransparencyPreference
{
    Full,
    Reduced,
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetSurfaceAppearanceOverride>))]
public enum WidgetSurfaceAppearanceOverride
{
    Widget,
    Theme,
    Transparent,
    Solid,
}

public sealed record AppearanceSettings
{
    public const double MinimumInterfaceScale = 0.8;
    public const double MaximumInterfaceScale = 1.25;
    public const double MinimumTextScale = 0.85;
    public const double MaximumTextScale = 1.5;
    public const double MinimumBackdropOpacity = 0;
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

    /// <summary>
    /// Host-owned contrast policy. This is intentionally additive to schema
    /// version 1 so settings written by older builds remain readable.
    /// </summary>
    public ContrastPreference Contrast { get; init; } = ContrastPreference.System;

    /// <summary>Raises rendered text to at least semibold after WRSS resolution.</summary>
    public bool BoldText { get; init; }

    /// <summary>Removes blur, translucent surfaces, and partial node opacity.</summary>
    public TransparencyPreference Transparency { get; init; } = TransparencyPreference.Full;

    /// <summary>
    /// Controls only host-owned transitions between tray-selected widgets.
    /// Older schema-v1 settings omit this value and retain the product default.
    /// </summary>
    public bool AnimateWidgetSwitching { get; init; }

    /// <summary>Global host-owned override; Widget preserves each declaration.</summary>
    public WidgetSurfaceAppearanceOverride WidgetSurfaceAppearance { get; init; } =
        WidgetSurfaceAppearanceOverride.Widget;

    /// <summary>Bounded exact widget-ID overrides applied ahead of the global value.</summary>
    public IReadOnlyDictionary<string, WidgetSurfaceAppearanceOverride>
        WidgetSurfaceAppearanceOverrides { get; init; } =
            new Dictionary<string, WidgetSurfaceAppearanceOverride>(StringComparer.Ordinal);

    public static AppearanceSettings Default { get; } = new()
    {
        ThemeId = ThemeIdentity.BuiltInDefault,
        ThemeVersion = ThemeIdentity.BuiltInDefaultVersion,
        InterfaceScale = 1,
        TextScale = 1,
        BackdropOpacity = 0.64,
        Motion = MotionPreference.System,
        Contrast = ContrastPreference.System,
        BoldText = false,
        Transparency = TransparencyPreference.Full,
        AnimateWidgetSwitching = false,
        WidgetSurfaceAppearance = WidgetSurfaceAppearanceOverride.Widget,
    };
}

public sealed record PlatformSettingsDocument
{
    public const int CurrentSchemaVersion = 3;

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
    public const string BuiltInCoolSlate = "widgetrail.builtin.cool-slate";
    public const string BuiltInCoolSlateVersion = "1.0.0";
    public const string BuiltInNeonCircuit = "widgetrail.builtin.neon-circuit";
    public const string BuiltInNeonCircuitVersion = "1.0.0";
    public const string BuiltInArcadeRush = "widgetrail.builtin.arcade-rush";
    public const string BuiltInArcadeRushVersion = "1.0.0";
    public const string BuiltInRedline = "widgetrail.builtin.redline";
    public const string BuiltInRedlineVersion = "1.0.0";
    public const int MaximumLength = 128;

    public static bool IsBuiltIn(string? value) =>
        value is BuiltInDefault or BuiltInCoolSlate or BuiltInNeonCircuit or BuiltInArcadeRush
             or BuiltInRedline;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        value is not "." and not ".." &&
        ThemeIdRegex().IsMatch(value);

    public static bool IsValidPublisher(string? value) =>
        value is { Length: > 0 and <= MaximumLength } && PublisherIdRegex().IsMatch(value);

    public static bool TryParseCanonicalVersion(string? value, out Version? version)
    {
        version = null;
        return value is { Length: > 0 and <= 64 } &&
               Version.TryParse(value, out version) &&
               string.Equals(version.ToString(), value, StringComparison.Ordinal);
    }

    [GeneratedRegex("\\A[a-z0-9](?:[a-z0-9._-]{0,127})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdRegex();

    [GeneratedRegex("\\A[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PublisherIdRegex();
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
        if (!Enum.IsDefined(appearance.Contrast))
            Add("$.appearance.contrast", "invalid_enum", "Contrast preference is invalid.");
        if (!Enum.IsDefined(appearance.Transparency))
            Add("$.appearance.transparency", "invalid_enum", "Transparency preference is invalid.");
        if (!Enum.IsDefined(appearance.WidgetSurfaceAppearance))
            Add("$.appearance.widgetSurfaceAppearance", "invalid_enum",
                "Widget surface appearance preference is invalid.");
        if (appearance.WidgetSurfaceAppearanceOverrides is null)
            Add("$.appearance.widgetSurfaceAppearanceOverrides", "required",
                "Widget surface appearance overrides are required.");
        else
        {
            if (appearance.WidgetSurfaceAppearanceOverrides.Count > 256)
                Add("$.appearance.widgetSurfaceAppearanceOverrides", "too_many",
                    "At most 256 widget surface appearance overrides are allowed.");
            foreach (var (widgetId, value) in appearance.WidgetSurfaceAppearanceOverrides)
            {
                if (!ThemeIdentity.IsValid(widgetId))
                    Add("$.appearance.widgetSurfaceAppearanceOverrides", "invalid_widget_id",
                        "Widget surface override IDs must be lowercase portable identifiers.");
                if (!Enum.IsDefined(value))
                    Add($"$.appearance.widgetSurfaceAppearanceOverrides.{widgetId}", "invalid_enum",
                        "Widget surface appearance override is invalid.");
            }
        }
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
