using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>The existing shell roles, shared by the wheel, rail and controller chips.</summary>
internal sealed record ShellChromePalette(Color Surface, Color Item, Color Text, Color Selected,
    Color SelectedText, Color Muted, Color Focus, double FocusWidth, bool HighContrast)
{
    private static readonly UISettings SystemUi = new();
    private static readonly AccessibilitySettings Accessibility = new();
    internal static ShellChromePalette Resolve(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance)
    {
        var ui = SystemUi;
        var contrast = appearance.Contrast == ContrastPreference.High ||
            appearance.Contrast == ContrastPreference.System && Accessibility.HighContrast;
        Color Value(string role, string property, Color fallback) =>
            styles?.GetValueOrDefault(role)?.Base.GetValueOrDefault(property) is { } value &&
            NativeComputedStyleAdapter.TryColor(value.Text, out var color) ? color : fallback;
        Color Background(string role, Color fallback)
        {
            var color = Value(role, "background", fallback);
            color.A = (byte)Math.Clamp(Math.Round(color.A * (styles?.GetValueOrDefault(role)?.Base.GetValueOrDefault("opacity")?.Number ?? 1)), 0, 255);
            return color;
        }
        var background = ui.GetColorValue(UIColorType.Background);
        var foreground = ui.GetColorValue(UIColorType.Foreground);
        var canvas = Composite(background, Background("canvas", background));
        var surface = Composite(canvas, Background("panel", canvas));
        var text = Value("tray-item", "color", Value("body", "color", foreground));
        var selectedText = Value("tray-item:selected", "color", Value("body", "color", foreground));
        var selected = Background("tray-item:selected", Blend(surface, foreground, .16));
        var focus = Value("tray-item:selected:focused", "outline-color", Value("tray-item:focused", "outline-color", selectedText));
        var width = styles?.GetValueOrDefault("tray-item:selected:focused")?.Base.GetValueOrDefault("outline-width")?.Number
            ?? styles?.GetValueOrDefault("tray-item:focused")?.Base.GetValueOrDefault("outline-width")?.Number ?? 2;
        return contrast
            ? new(background, background, foreground, SystemColor("SystemColorHighlightColor", ui.GetColorValue(UIColorType.Accent)),
                SystemColor("SystemColorHighlightTextColor", foreground), foreground, foreground, Math.Max(2, width), true)
            : new(surface, Background("tray-item", Microsoft.UI.Colors.Transparent), text, selected, selectedText,
                Value("hint", "color", text), focus, width, false);
    }
    private static Color SystemColor(string key, Color fallback) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value) && value is Color color ? color : fallback;

    internal static Color Composite(Color background, Color foreground) => Blend(background, foreground, foreground.A / 255d);
    internal static Color Blend(Color background, Color foreground, double amount) => Color.FromArgb(255,
        (byte)Math.Clamp(Math.Round(background.R + (foreground.R - background.R) * amount), 0, 255),
        (byte)Math.Clamp(Math.Round(background.G + (foreground.G - background.G) * amount), 0, 255),
        (byte)Math.Clamp(Math.Round(background.B + (foreground.B - background.B) * amount), 0, 255));
    internal static SolidColorBrush Brush(Color color) => new(color);
}
