using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private bool systemAnimationsEnabled = true;
    internal void ApplyAppearance(AppearanceSettings value, bool animationsEnabled)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Apply appearance on the widget dispatcher.");
        if (appearance != value || systemAnimationsEnabled != animationsEnabled) SettleTransitions();
        appearance = value ?? throw new ArgumentNullException(nameof(value));
        systemAnimationsEnabled = animationsEnabled;
        NativeComputedStyleAdapter.SetMotionPolicy(value, animationsEnabled);
        NativeComputedStyleAdapter.SetTextScale(value.TextScale);
        NativeComputedStyleAdapter.SetAccessibilityPolicy(value);
        if (!presentationOnly) FontSize = nativeBaseFontSize * (double.IsFinite(value.TextScale)
            ? Math.Clamp(value.TextScale, AppearanceSettings.MinimumTextScale, AppearanceSettings.MaximumTextScale) : 1);
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetModalLayer layer) layer.ApplyAppearance(value, animationsEnabled);
    }
}
