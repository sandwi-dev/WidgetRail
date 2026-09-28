using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private bool systemAnimationsEnabled = true;
    internal void ApplyAppearance(AppearanceSettings value, bool animationsEnabled)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Apply appearance on the widget dispatcher.");
        appearance = value ?? throw new ArgumentNullException(nameof(value));
        systemAnimationsEnabled = animationsEnabled;
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetModalLayer layer) layer.ApplyAppearance(value, animationsEnabled);
    }
}
