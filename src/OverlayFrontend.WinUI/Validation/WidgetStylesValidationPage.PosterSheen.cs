using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PosterSheenAsync()
    {
        var panel = new WidgetPosterPanel { Width = 100, Height = 150 };
        NativeMotionVisibility.Prepare(panel);
        var button = new Button { Content = panel };
        host.Children.Add(button);
        using var adapter = new NativeComputedStyleAdapter(button) { RoundedContent = panel };
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.System, FocusAnimation = WidgetFocusAnimation.None };
        var styles = Compute("#poster:focused { focus-sheen: sweep; }", "poster", "button");
        try
        {
            NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => panel.IsLoaded && panel.ActualWidth > 0);
            button.Focus(FocusState.Keyboard);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            var sweep = ElementCompositionPreview.GetElementChildVisual(panel);
            adapter.Update(styles);
            Check(ReferenceEquals(sweep, ElementCompositionPreview.GetElementChildVisual(panel)), "Style refresh retains the in-flight sweep with border motion off");
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is null);
            adapter.Update(styles);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Settled focus allocates no idle sheen and does not replay");
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            button.Focus(FocusState.Keyboard);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Focus departure retires the previous sweep");
            styles = Compute("#poster:focused { focus-sheen: reflection; focus-sheen-duration: 1.6s; focus-sheen-delay: 180ms; focus-sheen-opacity: .42; }",
                "poster", "button");
            adapter.Update(styles);
            button.Focus(FocusState.Keyboard);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            var light = ElementCompositionPreview.GetElementChildVisual(panel);
            Check(light is Microsoft.UI.Composition.SpriteVisual { Brush: Microsoft.UI.Composition.CompositionLinearGradientBrush } &&
                Math.Abs(light.Opacity - .42) < .001, "Broad reflection uses the authored peak opacity");
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Task.Delay(220);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Leaving during the reflection delay discards the highlight");
            button.Focus(FocusState.Keyboard);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            light = ElementCompositionPreview.GetElementChildVisual(panel);
            await Task.Delay(850);
            adapter.Update(styles);
            Check(ReferenceEquals(light, ElementCompositionPreview.GetElementChildVisual(panel)), "Slow reflection survives a snapshot after the short sweep would have finished");
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is null);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            styles = Compute("#poster:focused { focus-sheen: reflection; focus-sheen-duration: 180ms; focus-sheen-repeat-delay: 1s; }",
                "poster", "button");
            button.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is null);
            await Task.Delay(250);
            adapter.Update(styles);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Rest holds no highlight and a same-focus snapshot does not bypass it");
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is not null);
            await Wait(() => ElementCompositionPreview.GetElementChildVisual(panel) is null);
            var staleTick = panel.CaptureRepeatTick();
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Task.Delay(1200);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null && !panel.HasRepeatTimer,
                "Focus loss during rest retires the timer and queued work");
            button.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => panel.HasRepeatTimer);
            staleTick();
            Check(panel.HasRepeatTimer && ElementCompositionPreview.GetElementChildVisual(panel) is null,
                "A queued tick from the previous focus cannot paint or cancel the new owner's rest");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => !panel.HasRepeatTimer);
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            button.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => panel.HasRepeatTimer);
            var recycledTick = panel.CaptureRepeatTick();
            host.Children.Remove(button);
            await Wait(() => !panel.IsLoaded && !panel.HasRepeatTimer);
            recycledTick();
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Unloading during rest rejects a retired card's queued tick");
            host.Children.Add(button);
            await Wait(() => panel.IsLoaded);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            NativeComputedStyleAdapter.SetMotionPolicy(settings, false);
            button.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "System reduced motion suppresses the sweep");
            NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            button.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            Check(ElementCompositionPreview.GetElementChildVisual(panel) is null, "Contrast mode suppresses the sweep");
        }
        finally
        {
            host.Children.Remove(button);
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default, true);
        }
    }
}
