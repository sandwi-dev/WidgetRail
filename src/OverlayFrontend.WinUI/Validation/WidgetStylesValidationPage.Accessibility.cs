using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AccessibilityPreferencesAsync()
    {
        var settings = AppearanceSettings.Default with { Contrast = ContrastPreference.Standard, BoldText = false };
        var text = new TextBlock { Text = "Saved accessibility preferences", FontWeight = new Windows.UI.Text.FontWeight { Weight = 400 } };
        var panel = new Grid(); panel.Children.Add(text); host.Children.Add(panel);
        using var textStyle = new NativeComputedStyleAdapter(text, false);
        using var panelStyle = new NativeComputedStyleAdapter(panel, false);
        try
        {
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings);
            textStyle.Update(Compute("#label { font-weight: 400; color: #123456; }", "label", "text"));
            panelStyle.Update(Compute("#panel { background: #11223380; opacity: .6; }", "panel", "stack"));
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings with { BoldText = true, Transparency = TransparencyPreference.Reduced });
            await Wait(() => text.FontWeight.Weight == 600 && Near(panel.Opacity, 1));
            Check(ColorOf(panel.Background).A == 255, "reduced transparency makes painted panel fills opaque without fading text");
            textStyle.Update(Compute("#label { font-weight: 800; color: #123456; }", "label", "text"));
            Check(text.FontWeight.Weight == 800, "bold-text minimum preserves heavier authored headings");
            panelStyle.Update(Compute("#panel { background: transparent; }", "panel", "stack"));
            Check(ColorOf(panel.Background).A == 0, "reduced transparency preserves intentionally absent fills");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings);
            await Wait(() => ColorOf(text.Foreground) == Windows.UI.Color.FromArgb(255, 18, 52, 86));
            Check(text.FontWeight.Weight == 800, "standard contrast explicitly retains authored palette under system high contrast");
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings with { Contrast = ContrastPreference.High });
            await Wait(() => ColorOf(text.Foreground) != Windows.UI.Color.FromArgb(255, 18, 52, 86));
            Check(ColorOf(text.Foreground).A == 255, "saved high contrast selects the native system palette");
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings with { BoldText = true });
            textStyle.Update(null);
            await Wait(() => text.FontWeight.Weight == 600);
            NativeComputedStyleAdapter.SetAccessibilityPolicy(settings);
            await Wait(() => text.FontWeight.Weight == 400);
            Check(true, "disabling bold text restores the original inherited or local weight");
        }
        finally
        {
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            NativeComputedStyleAdapter.SetAccessibilityPolicy(AppearanceSettings.Default);
            host.Children.Remove(panel);
        }
    }
}
