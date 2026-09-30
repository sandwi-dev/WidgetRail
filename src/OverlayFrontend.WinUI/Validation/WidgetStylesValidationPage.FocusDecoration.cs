using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeFocusDecorationAsync()
    {
        await RetiredFocusLoadedAsync();
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.Full, FocusAnimation = WidgetFocusAnimation.Fade };
        NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
        var button = new Button { Content = "Focus decoration", Width = 220, Height = 60, Margin = new Thickness(12) };
        host.Children.Add(button);
        using var adapter = new NativeComputedStyleAdapter(button);
        var styles = Compute("""
            #outline { color: #ffeedd; background: #123456; corner-radius: 8px; transition-duration: 120ms; }
            #outline:focused { outline-color: #aabbcc; outline-width: 2px; outline-offset: -2px; scale: 1.05; }
            #outline:pressed { scale: 0.95; }
            """, "outline", "button");
        try
        {
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => button.IsLoaded && adapter.FocusDecoration is { IsVisible: false });
            var decoration = adapter.FocusDecoration!;
            var path = (Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)
                ((Microsoft.UI.Composition.CompositionSpriteShape)((Microsoft.UI.Composition.ShapeVisual)decoration.DecorationVisual).Shapes[0]).Geometry;
            Check(path.Offset == new Vector2(1) && path.Size == new Vector2(218, 58),
                "negative outline offset matches the native centered stroke geometry");
            Check(!button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is not null,
                "authored focus outline receives one dedicated native child visual");
            button.Focus(FocusState.Keyboard);
            await Wait(() => decoration.Playback is not null);
            Check(await decoration.Playback! == WidgetMotionOutcome.Completed,
                "global Fade animates the authored focus decoration on the compositor");
            await Wait(() => adapter.ScaleMotion is { IsAnimating: false });
            Check(decoration.IsVisible && Math.Abs(button.Scale.X - 1.05f) < .0001 && button.Opacity == 1 &&
                ColorOf(button.Background) == Windows.UI.Color.FromArgb(255, 18, 52, 86),
                "authored scale and focus decoration share the control without tinting its content");
            var playback = decoration.Playback;
            adapter.Update(styles);
            Check(ReferenceEquals(playback, decoration.Playback), "same-state snapshots do not replay focus decoration motion");
            NativeComputedStyleAdapter.SetMotionPolicy(settings with { FocusAnimation = WidgetFocusAnimation.Settle }, true);
            await Task.Delay(20);
            outside.Focus(FocusState.Keyboard);
            await Wait(() => decoration.Playback != playback);
            await decoration.Playback!;
            button.Focus(FocusState.Keyboard);
            await Wait(() => decoration.IsVisible);
            await Wait(() => decoration.Playback?.IsCompleted == true);
            Check(decoration.DecorationVisual.Scale == Vector3.One && button.ActualWidth == 220,
                "Settle transforms only the decoration and preserves native control layout");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => adapter.FocusDecoration is null);
            Check(button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is null,
                "high contrast restores the platform focus visual and removes the authored layer");
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            await Wait(() => adapter.FocusDecoration is not null);
            adapter.Update(null);
            Check(button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is null,
                "removing outline styles retires compositor resources and restores native focus behavior");
            adapter.Update(Compute("#outer { outline-color: #ffffff; outline-width: 2px; outline-offset: 3px; }", "outer", "button"));
            var expanded = adapter.FocusDecoration!.DecorationVisual;
            Check(expanded.Size == new Vector2(230, 70) && expanded.Parent.Offset == new Vector3(-5, -5, 0),
                "positive outline offset reserves the complete outside stroke without moving control content");
            var depth = Compute("#depth { background: rgba(40,60,80,.8); surface-shading: .1; }", "depth", "button");
            adapter.Update(depth);
            var gradient = (LinearGradientBrush)button.Background;
            Check(gradient.GradientStops.Count == 2 && gradient.GradientStops[0].Color.R > 40 &&
                gradient.GradientStops[1].Color.R < 40 && gradient.GradientStops.All(stop => stop.Color.A == 204),
                "surface depth maps theme color to a native gradient while preserving authored alpha");
            adapter.Update(depth);
            Check(ReferenceEquals(gradient, button.Background), "unchanged surface shading reuses its native gradient brush");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => button.Background is SolidColorBrush);
            Check(button.Background is SolidColorBrush, "high contrast removes decorative surface shading");
        }
        finally
        {
            adapter.Dispose(); host.Children.Remove(button);
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default, true);
        }
    }

    private async Task RetiredFocusLoadedAsync()
    {
        var button = new Button { Content = "Retired focus", Width = 120, Height = 44, UseSystemFocusVisuals = true };
        WidgetFocusDecoration? decoration = null;
        var retiredOnLoad = false;
        // Match passive pin entry: a preceding Loaded callback withdraws the
        // decoration while XAML is delivering that same event to its subscribers.
        button.Loaded += (_, _) => { retiredOnLoad = true; decoration?.Dispose(); };
        decoration = WidgetFocusDecoration.Create(button) ?? throw new InvalidOperationException("Missing focus adornment");
        try
        {
            host.Children.Add(button);
            await Wait(() => retiredOnLoad && button.IsLoaded);
            button.Width = 180;
            await Task.Delay(40);
            Check(!decoration.IsVisible && button.UseSystemFocusVisuals,
                "focus decoration retired during Loaded ignores captured native callbacks and later resize without using closed visuals");
        }
        finally { decoration.Dispose(); host.Children.Remove(button); }
    }
}
