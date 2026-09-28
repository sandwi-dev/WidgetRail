using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeScaleAsync()
    {
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = 1 };
        NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
        var button = new Button { Content = "Native scale", Width = 180, Height = 60, Margin = new Thickness(12) };
        var originalCenter = button.CenterPoint;
        host.Children.Add(button);
        using var adapter = new NativeComputedStyleAdapter(button);
        var styles = Compute("""
            #scale { transition-duration: 180ms; transition-easing: ease-in-out; }
            #scale:focused { scale: 1.08; }
            #scale:pressed { scale: 0.94; }
            """, "scale", "button");
        try
        {
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => button.IsLoaded && button.ActualWidth > 0);
            Check(button.Scale == Vector3.One && adapter.ScaleMotion is not null,
                "focus-only scale declarations prepare one native owner before the first interaction");
            button.Focus(FocusState.Keyboard);
            await Wait(() => adapter.ScaleMotion!.Starts == 1);
            await Wait(() => !adapter.ScaleMotion!.IsAnimating);
            Check(Math.Abs(button.Scale.X - 1.08f) < .0001 && button.ActualWidth == 180 && button.ActualHeight == 60,
                "focused scale finishes on the compositor without changing native layout dimensions");
            var starts = adapter.ScaleMotion!.Starts;
            adapter.Update(styles);
            Check(adapter.ScaleMotion.Starts == starts, "ordinary style snapshots do not replay the current scale");
            adapter.SetControllerPressed(true);
            await Wait(() => adapter.ScaleMotion!.IsAnimating);
            adapter.SetControllerPressed(false);
            await Wait(() => !adapter.ScaleMotion!.IsAnimating);
            Check(Math.Abs(button.Scale.X - 1.08f) < .0001 && adapter.ScaleMotion!.Starts == starts + 2,
                "a release during compression retargets the same native channel to focused scale");
            adapter.SetControllerPressed(true);
            await Wait(() => !adapter.ScaleMotion!.IsAnimating);
            Check(Math.Abs(button.Scale.X - .94f) < .0001, "held controller press reaches the authored compressed scale");
            NativeComputedStyleAdapter.SetMotionPolicy(settings with { Motion = MotionPreference.Reduced }, true);
            adapter.SetControllerPressed(false);
            Check(Math.Abs(button.Scale.X - 1.08f) < .0001 && !adapter.ScaleMotion!.IsAnimating,
                "reduced motion preserves the state scale and settles it without animation");
            NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
            await Task.Delay(30);
            var spring = Compute("#scale { scale: 1; transition-duration: 120ms; transition-easing: spring; } #scale:focused { scale: 1.06; }",
                "scale", "button");
            adapter.Update(spring);
            await Wait(() => !adapter.ScaleMotion!.IsAnimating);
            Check(Math.Abs(button.Scale.X - 1.06f) < .0001, "bounded WRSS spring completes through native keyframes");
            adapter.Update(null);
            Check(button.Scale == Vector3.One && button.CenterPoint == originalCenter && adapter.ScaleMotion is null,
                "removing scale styles restores the original native scale and pivot");
            adapter.Update(Compute("#scale { font-size: 20px; }", "scale", "button"));
            NativeComputedStyleAdapter.SetTextScale(1.25);
            await Wait(() => button.FontSize == 25);
            Check(button.FontSize == 25 && button.Scale == Vector3.One,
                "global text scaling reflows authored typography without using a visual transform");
            NativeComputedStyleAdapter.SetTextScale(1);
            await Wait(() => button.FontSize == 20);
            Check(button.FontSize == 20, "text-scale changes derive from authored font size without accumulating");
        }
        finally
        {
            adapter.Dispose(); host.Children.Remove(button);
            NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default, true);
            NativeComputedStyleAdapter.SetTextScale(1);
        }
    }
}
