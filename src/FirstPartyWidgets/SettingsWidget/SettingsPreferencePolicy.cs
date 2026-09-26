using WidgetRail.PlatformSettings;

namespace WidgetRail.FirstPartyWidgets.Settings;

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
    AnimateWidgetSwitching,
    FocusAnimation,
    SectionAnimation,
    AnimateWidgetModals,
    ModalAnimation,
    AnimationSpeedDecrease,
    AnimationSpeedIncrease,
    WidgetSwitcher,
    OverlayPosition,
    Theme,
}

internal readonly record struct SettingsPreferenceMutation(
    SettingsPreferenceKind Kind,
    string SuccessStatus,
    string? ThemeId = null,
    string? ThemeVersion = null,
    string? DisplayId = null,
    WidgetFocusAnimation? FocusAnimation = null,
    WidgetSectionAnimation? SectionAnimation = null,
    WidgetModalAnimation? ModalAnimation = null)
{
    public bool IsScale => Kind is SettingsPreferenceKind.TextDecrease or SettingsPreferenceKind.TextIncrease
        or SettingsPreferenceKind.InterfaceDecrease or SettingsPreferenceKind.InterfaceIncrease;

    public PlatformSettingsDocument Apply(PlatformSettingsDocument current)
    {
        if (!IsScale || DisplayId is null)
            return current with { Appearance = Apply(current.Appearance) };
        var scale = DisplayScalePolicy.Resolve(current.Appearance, DisplayId);
        var changed = Apply(current.Appearance with { InterfaceScale = scale.InterfaceScale, TextScale = scale.TextScale });
        return current with { Appearance = DisplayScalePolicy.Set(current.Appearance, DisplayId,
            new(changed.InterfaceScale, changed.TextScale)) };
    }

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
        SettingsPreferenceKind.AnimateWidgetSwitching => appearance with
        {
            AnimateWidgetSwitching = !appearance.AnimateWidgetSwitching,
        },
        SettingsPreferenceKind.FocusAnimation when FocusAnimation is { } focus => appearance with
        {
            FocusAnimation = focus,
        },
        SettingsPreferenceKind.SectionAnimation when SectionAnimation is { } selected => appearance with
        {
            SectionAnimation = selected,
        },
        SettingsPreferenceKind.ModalAnimation when ModalAnimation is { } modal => appearance with
        {
            ModalAnimation = modal,
        },
        SettingsPreferenceKind.AnimateWidgetModals => appearance with
        {
            AnimateWidgetModals = !appearance.AnimateWidgetModals,
        },
        SettingsPreferenceKind.AnimationSpeedDecrease => appearance with
        {
            WidgetAnimationSpeed = Step(appearance.WidgetAnimationSpeed, -0.25,
                AppearanceSettings.MinimumWidgetAnimationSpeed, AppearanceSettings.MaximumWidgetAnimationSpeed),
        },
        SettingsPreferenceKind.AnimationSpeedIncrease => appearance with
        {
            WidgetAnimationSpeed = Step(appearance.WidgetAnimationSpeed, 0.25,
                AppearanceSettings.MinimumWidgetAnimationSpeed, AppearanceSettings.MaximumWidgetAnimationSpeed),
        },
        SettingsPreferenceKind.WidgetSwitcher => appearance with
        {
            WidgetSwitcher = appearance.WidgetSwitcher == WidgetSwitcherLayout.Rail
                ? WidgetSwitcherLayout.Radial : WidgetSwitcherLayout.Rail,
        },
        SettingsPreferenceKind.Theme when ThemeId is not null && ThemeVersion is not null =>
            appearance with { ThemeId = ThemeId, ThemeVersion = ThemeVersion },
        SettingsPreferenceKind.OverlayPosition => appearance with
        {
            OverlayPosition = appearance.OverlayPosition switch
            {
                OverlayPosition.Center => OverlayPosition.BottomLeft,
                OverlayPosition.BottomLeft => OverlayPosition.BottomRight,
                _ => OverlayPosition.Center,
            },
        },
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
        string? displayId = null;
        var separator = actionId.IndexOf('@');
        if (separator >= 0)
        {
            displayId = actionId[(separator + 1)..];
            actionId = actionId[..separator];
            if (!DisplayScalePolicy.IsValidId(displayId)) { mutation = default; return false; }
        }
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
            "widget-switcher.toggle" => new(SettingsPreferenceKind.WidgetSwitcher, "Widget switcher layout saved"),
            "focus-animation.fade" => new(SettingsPreferenceKind.FocusAnimation, "Focus animation saved",
                FocusAnimation: WidgetFocusAnimation.Fade),
            "focus-animation.slide" => new(SettingsPreferenceKind.FocusAnimation, "Focus animation saved",
                FocusAnimation: WidgetFocusAnimation.Slide),
            "focus-animation.none" => new(SettingsPreferenceKind.FocusAnimation, "Focus animation saved",
                FocusAnimation: WidgetFocusAnimation.None),
            "section-animation.paging" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.Paging),
            "section-animation.slide" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.Slide),
            "section-animation.verticalslide" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.VerticalSlide),
            "section-animation.reveal" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.Reveal),
            "section-animation.coverslide" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.CoverSlide),
            "modal-animation.lift" => new(SettingsPreferenceKind.ModalAnimation, "Dialog animation saved",
                ModalAnimation: WidgetModalAnimation.Lift),
            "modal-animation.zoom" => new(SettingsPreferenceKind.ModalAnimation, "Dialog animation saved",
                ModalAnimation: WidgetModalAnimation.Zoom),
            "section-animation.none" => new(SettingsPreferenceKind.SectionAnimation, "Section animation saved",
                SectionAnimation: WidgetSectionAnimation.None),
            "widget-modal-animation.toggle" => new(SettingsPreferenceKind.AnimateWidgetModals, "Dialog animation preference saved"),
            "widget-animation-speed.decrease" => new(SettingsPreferenceKind.AnimationSpeedDecrease, "Animation speed saved"),
            "widget-animation-speed.increase" => new(SettingsPreferenceKind.AnimationSpeedIncrease, "Animation speed saved"),
            "overlay-position.cycle" => new(SettingsPreferenceKind.OverlayPosition, "Overlay position saved"),
            "widget-switch-animation.toggle" => new(
                SettingsPreferenceKind.AnimateWidgetSwitching,
                "Widget-switch animation preference saved"),
            _ => default,
        };
        if (displayId is not null)
        {
            if (!mutation.IsScale || mutation.SuccessStatus is null) { mutation = default; return false; }
            mutation = mutation with { DisplayId = displayId };
        }
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
