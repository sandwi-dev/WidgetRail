using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private bool systemAnimationsEnabled = true;
    private Motion.WidgetSurfaceResizeMotion? surfaceResize;
    internal Task<Motion.WidgetMotionOutcome>? WidgetResizeCompletion => surfaceResize?.Playback;
    internal int WidgetResizeStarts => surfaceResize?.Starts ?? 0;
    internal System.Numerics.Vector2 PresentedWidgetSize => surfaceResize?.PresentedSize(new((float)Width, (float)Height)) ?? new((float)Width, (float)Height);
    internal void ResizeWidgetContent(System.Numerics.Vector2 previousSize, Microsoft.UI.Xaml.FrameworkElement background,
        Motion.WidgetResizeReason reason = Motion.WidgetResizeReason.WidgetSwitch)
    {
        // Own only scale/center on the whole clipped presenter, leaving shell
        // staging opacity and internal section/dialog motion independent.
        (surfaceResize ??= new(this)).Play(appearance, systemAnimationsEnabled, previousSize, background, reason);
    }
    internal void SettleWidgetResize() => surfaceResize?.Cancel();
    internal void ApplyAppearance(AppearanceSettings value, bool animationsEnabled)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Apply appearance on the widget dispatcher.");
        if (appearance != value || systemAnimationsEnabled != animationsEnabled)
        {
            SettleTransitions(); SettleModalExit(); SettleWidgetResize();
            foreach (var reveal in artworkReveals.Values) reveal.Cancel();
        }
        appearance = value ?? throw new ArgumentNullException(nameof(value));
        systemAnimationsEnabled = animationsEnabled;
        WidgetPresentationSurface.SetMotionAppearance(value, animationsEnabled);
        NativeComputedStyleAdapter.SetMotionPolicy(value, animationsEnabled);
        NativeTextScaleScope.Set(this, value.TextScale);
        foreach (var adapter in nativeStyles.Values) adapter.RefreshTextScale();
        NativeComputedStyleAdapter.SetAccessibilityPolicy(value);
        if (!presentationOnly) FontSize = nativeBaseFontSize * (double.IsFinite(value.TextScale)
            ? Math.Clamp(value.TextScale, AppearanceSettings.MinimumTextScale, AppearanceSettings.MaximumTextScale) : 1);
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetModalLayer layer) layer.ApplyAppearance(value, animationsEnabled);
    }
}
