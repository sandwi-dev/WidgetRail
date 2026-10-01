using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task HighContrastPaletteAsync()
    {
        var resources = Application.Current.Resources;
        var keys = new[] { "SystemColorWindowColor", "SystemColorWindowTextColor" };
        var saved = keys.ToDictionary(key => key, key =>
            resources.TryGetValue(key, out var value) ? (Exists: true, Value: (object?)value) : (Exists: false, Value: (object?)null));
        var panel = new StackPanel { RequestedTheme = ElementTheme.Dark };
        var label = new TextBlock { Text = "Retained shell label" };
        var button = new Button { Content = "Retained widget button" };
        panel.Children.Add(label); panel.Children.Add(button); host.Children.Add(panel);
        using var shellStyle = new Shell.ShellChromeStyles();
        using var buttonStyle = new NativeComputedStyleAdapter(button);
        var palette = new Dictionary<string, BridgeNodeRenderStyles>
        { ["label"] = Compute("text { color: #123456; }", "label", "text") };
        shellStyle.Register(label, "label");
        shellStyle.Update(palette, PlatformSettings.AppearanceSettings.Default, true);
        buttonStyle.Update(Compute("button { color: #123456; background: #345678; }", "button", "button"));
        try
        {
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            resources[keys[0]] = Windows.UI.Color.FromArgb(255, 16, 32, 48);
            resources[keys[1]] = Windows.UI.Color.FromArgb(255, 240, 232, 180);
            WidgetViewPresenter.SetSystemHighContrast(true);
            await Wait(() => ColorOf(label.Foreground) == (Windows.UI.Color)resources[keys[1]]);
            button.Focus(FocusState.Keyboard);
            await Wait(() => button.FocusState != FocusState.Unfocused);
            // No ActualTheme change and no replacement WRSS palette. Only the
            // production system-theme notification carries this invalidation.
            resources[keys[0]] = Windows.UI.Color.FromArgb(255, 48, 16, 40);
            resources[keys[1]] = Windows.UI.Color.FromArgb(255, 180, 240, 232);
            WidgetViewPresenter.SetSystemHighContrast(true);
            shellStyle.Update(palette, PlatformSettings.AppearanceSettings.Default, true);
            await Wait(() => ColorOf(label.Foreground) == (Windows.UI.Color)resources[keys[1]] &&
                ColorOf(button.Foreground) == (Windows.UI.Color)resources[keys[1]] &&
                ColorOf(button.Background) == (Windows.UI.Color)resources[keys[0]]);
            Check(ReferenceEquals(panel.Children[0], label) && ReferenceEquals(panel.Children[1], button) &&
                ReferenceEquals(NativeComputedStyleAdapter.For(button), buttonStyle) && button.FocusState != FocusState.Unfocused,
                "system high-contrast palette changes refresh retained shell and widget colors without moving focus");
        }
        finally
        {
            foreach (var (key, value) in saved)
                if (value.Exists) resources[key] = value.Value!; else resources.Remove(key);
            WidgetViewPresenter.SetSystemHighContrast(new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast);
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            host.Children.Remove(panel);
        }
    }

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
