namespace WidgetRail.PlatformSettings;

/// <summary>WidgetRail sizing only; never changes Windows display scaling.</summary>
public sealed record DisplayScaleSettings(double InterfaceScale, double TextScale);

public static class DisplayScalePolicy
{
    public const int MaximumDisplays = 32;

    public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) &&
        id.Length <= 256 && !id.Any(char.IsControl);

    public static DisplayScaleSettings Resolve(AppearanceSettings appearance, string? displayId) =>
        displayId is not null && appearance.DisplayScales.TryGetValue(displayId, out var scale)
            ? scale : new(appearance.InterfaceScale, appearance.TextScale);

    public static AppearanceSettings Set(AppearanceSettings appearance, string displayId,
        DisplayScaleSettings scale)
    {
        if (!IsValidId(displayId))
            throw new PlatformSettingsException("display_unavailable", "The current display is unavailable.");
        var values = new Dictionary<string, DisplayScaleSettings>(appearance.DisplayScales,
            StringComparer.Ordinal);
        if (!values.ContainsKey(displayId) && values.Count >= MaximumDisplays)
            throw new PlatformSettingsException("display_limit", "Too many saved display sizes.");
        values[displayId] = scale;
        return appearance with { DisplayScales = values };
    }
}
