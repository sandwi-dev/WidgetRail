using GameBarAlternative.PlatformSettings;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

internal enum SettingsPreferenceKind
{
    TextDecrease,
    TextIncrease,
    InterfaceDecrease,
    InterfaceIncrease,
    OpacityDecrease,
    OpacityIncrease,
    MotionSystem,
    MotionReduced,
    ContrastSystem,
    ContrastHigh,
    BoldText,
    ReducedTransparency,
    Theme,
}

internal readonly record struct SettingsPreferenceMutation(
    SettingsPreferenceKind Kind,
    string SuccessStatus,
    string? ThemeId = null,
    string? ThemeVersion = null)
{
    public PlatformSettingsDocument Apply(PlatformSettingsDocument current) =>
        current with { Appearance = Apply(current.Appearance) };

    private AppearanceSettings Apply(AppearanceSettings appearance) => Kind switch
    {
        SettingsPreferenceKind.TextDecrease => appearance with
        {
            TextScale = Step(
                appearance.TextScale,
                -SettingsPreferencePolicy.ScaleStep,
                AppearanceSettings.MinimumTextScale,
                AppearanceSettings.MaximumTextScale),
        },
        SettingsPreferenceKind.TextIncrease => appearance with
        {
            TextScale = Step(
                appearance.TextScale,
                SettingsPreferencePolicy.ScaleStep,
                AppearanceSettings.MinimumTextScale,
                AppearanceSettings.MaximumTextScale),
        },
        SettingsPreferenceKind.InterfaceDecrease => appearance with
        {
            InterfaceScale = Step(
                appearance.InterfaceScale,
                -SettingsPreferencePolicy.ScaleStep,
                AppearanceSettings.MinimumInterfaceScale,
                AppearanceSettings.MaximumInterfaceScale),
        },
        SettingsPreferenceKind.InterfaceIncrease => appearance with
        {
            InterfaceScale = Step(
                appearance.InterfaceScale,
                SettingsPreferencePolicy.ScaleStep,
                AppearanceSettings.MinimumInterfaceScale,
                AppearanceSettings.MaximumInterfaceScale),
        },
        SettingsPreferenceKind.OpacityDecrease => appearance with
        {
            BackdropOpacity = Step(
                appearance.BackdropOpacity,
                -SettingsPreferencePolicy.OpacityStep,
                AppearanceSettings.MinimumBackdropOpacity,
                AppearanceSettings.MaximumBackdropOpacity),
        },
        SettingsPreferenceKind.OpacityIncrease => appearance with
        {
            BackdropOpacity = Step(
                appearance.BackdropOpacity,
                SettingsPreferencePolicy.OpacityStep,
                AppearanceSettings.MinimumBackdropOpacity,
                AppearanceSettings.MaximumBackdropOpacity),
        },
        SettingsPreferenceKind.MotionSystem => appearance with
        {
            Motion = appearance.Motion == MotionPreference.System
                ? MotionPreference.Full
                : MotionPreference.System,
        },
        SettingsPreferenceKind.MotionReduced => appearance with
        {
            Motion = appearance.Motion == MotionPreference.Reduced
                ? MotionPreference.Full
                : MotionPreference.Reduced,
        },
        SettingsPreferenceKind.ContrastSystem => appearance with
        {
            Contrast = appearance.Contrast == ContrastPreference.System
                ? ContrastPreference.Standard
                : ContrastPreference.System,
        },
        SettingsPreferenceKind.ContrastHigh => appearance with
        {
            Contrast = appearance.Contrast == ContrastPreference.High
                ? ContrastPreference.Standard
                : ContrastPreference.High,
        },
        SettingsPreferenceKind.BoldText => appearance with
        {
            BoldText = !appearance.BoldText,
        },
        SettingsPreferenceKind.ReducedTransparency => appearance with
        {
            Transparency = appearance.Transparency == TransparencyPreference.Reduced
                ? TransparencyPreference.Full
                : TransparencyPreference.Reduced,
        },
        SettingsPreferenceKind.Theme when ThemeId is not null && ThemeVersion is not null =>
            appearance with { ThemeId = ThemeId, ThemeVersion = ThemeVersion },
        _ => appearance,
    };

    private static double Step(double current, double delta, double minimum, double maximum) =>
        Math.Clamp(
            Math.Round(current + delta, 2, MidpointRounding.AwayFromZero),
            minimum,
            maximum);
}

/// <summary>
/// Closed ordinary appearance/overlay preference vocabulary and persistence rule.
/// It cannot route diagnostics or privileged authority-recovery actions.
/// </summary>
internal static class SettingsPreferencePolicy
{
    internal const double ScaleStep = 0.05;
    internal const double OpacityStep = 0.05;

    public static bool TryCreate(string actionId, out SettingsPreferenceMutation mutation)
    {
        mutation = actionId switch
        {
            "text.decrease" => new(SettingsPreferenceKind.TextDecrease, "Text size saved"),
            "text.increase" => new(SettingsPreferenceKind.TextIncrease, "Text size saved"),
            "interface.decrease" => new(
                SettingsPreferenceKind.InterfaceDecrease, "Interface size saved"),
            "interface.increase" => new(
                SettingsPreferenceKind.InterfaceIncrease, "Interface size saved"),
            "opacity.decrease" => new(
                SettingsPreferenceKind.OpacityDecrease, "Backdrop saved"),
            "opacity.increase" => new(
                SettingsPreferenceKind.OpacityIncrease, "Backdrop saved"),
            "motion.system" => new(
                SettingsPreferenceKind.MotionSystem, "Motion preference saved"),
            "motion.reduced" => new(
                SettingsPreferenceKind.MotionReduced, "Motion preference saved"),
            "contrast.system" => new(
                SettingsPreferenceKind.ContrastSystem, "Contrast preference saved"),
            "contrast.high" => new(
                SettingsPreferenceKind.ContrastHigh, "Contrast preference saved"),
            "bold-text.toggle" => new(
                SettingsPreferenceKind.BoldText, "Bold text preference saved"),
            "transparency.reduced" => new(
                SettingsPreferenceKind.ReducedTransparency,
                "Transparency preference saved"),
            _ => default,
        };
        return mutation.SuccessStatus is not null;
    }

    public static SettingsPreferenceMutation Theme(
        string themeId,
        string themeVersion,
        string themeName) => new(
            SettingsPreferenceKind.Theme,
            $"Theme set to {themeName}",
            themeId,
            themeVersion);

    public static Task<PlatformSettingsDocument> PersistAsync(
        PlatformSettingsStore store,
        PlatformSettingsDocument current,
        bool currentIsValid,
        SettingsPreferenceMutation mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(current);
        return currentIsValid
            ? store.UpdateAsync(mutation.Apply, cancellationToken)
            : store.ReplaceAsync(mutation.Apply(current), cancellationToken);
    }
}
