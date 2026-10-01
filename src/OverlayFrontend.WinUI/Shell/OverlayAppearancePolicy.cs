using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal readonly record struct OverlaySurfaceAppearance(WidgetSurfaceAppearance Declared,
    WidgetSurfaceAppearance Requested, WidgetSurfaceAppearance Effective, string? FallbackReason, bool HighContrast);

/// <summary>Same override/accessibility precedence as native WidgetSurfaceAppearance.h.</summary>
internal static class OverlayAppearancePolicy
{
    internal static OverlaySurfaceAppearance Resolve(AppearanceSettings settings, string? widgetId,
        WidgetSurfaceAppearance declared, bool systemHighContrast, bool transparentComposition = true)
    {
        var selected = widgetId is not null && settings.WidgetSurfaceAppearanceOverrides.TryGetValue(widgetId, out var exact)
            ? exact : settings.WidgetSurfaceAppearance;
        var requested = selected switch
        {
            WidgetSurfaceAppearanceOverride.Widget => declared,
            WidgetSurfaceAppearanceOverride.Theme => WidgetSurfaceAppearance.Theme,
            WidgetSurfaceAppearanceOverride.Transparent => WidgetSurfaceAppearance.Transparent,
            _ => WidgetSurfaceAppearance.Solid,
        };
        var contrast = settings.Contrast switch
        {
            ContrastPreference.High => true, ContrastPreference.Standard => false, _ => systemHighContrast,
        };
        var fallback = contrast ? "high-contrast" : settings.Transparency == TransparencyPreference.Reduced ? "reduced-transparency" :
            requested == WidgetSurfaceAppearance.Transparent && !transparentComposition ? "unsupported-composition" :
            requested == WidgetSurfaceAppearance.Transparent && settings.BackdropOpacity <= 0 ? "zero-backdrop-contrast" : null;
        return new(declared, requested, fallback is null ? requested : WidgetSurfaceAppearance.Solid, fallback, contrast);
    }
}
