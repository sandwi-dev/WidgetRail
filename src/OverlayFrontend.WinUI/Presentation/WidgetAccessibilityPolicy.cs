using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Saved accessibility preferences apply after authored styles.</summary>
internal readonly record struct WidgetAccessibilityPolicy(ContrastPreference Contrast, bool BoldText, bool ReducedTransparency)
{
    internal static WidgetAccessibilityPolicy From(AppearanceSettings settings) =>
        new(settings.Contrast, settings.BoldText, settings.Transparency == TransparencyPreference.Reduced);

    internal bool HighContrast(bool system) => Contrast switch
    {
        ContrastPreference.High => true,
        ContrastPreference.Standard => false,
        _ => system,
    };

    internal ushort? FontWeight(ushort? authored) => BoldText ? (ushort)Math.Max(600, (int)(authored ?? 400)) : authored;
    internal double? Opacity(double? authored, bool highContrast) => authored is null ? null :
        ReducedTransparency || highContrast ? 1 : Math.Clamp(authored.Value, 0, 1);

    // A zero-alpha background is an intentionally absent fill, not a translucent
    // painted surface. Making it opaque would introduce unwanted boxes.
    internal byte BackgroundAlpha(byte authored) => ReducedTransparency && authored > 0 ? byte.MaxValue : authored;
}
