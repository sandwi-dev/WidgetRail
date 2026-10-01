using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SliderControlValidationPage
{
    private BridgeNodeRenderStyles? pixelStyles;

    // Opt-in screen-capture checkpoints: the external runner captures actual
    // compositor focus pixels, then acknowledges each phase with a .continue file.
    private async Task CaptureSliderPixelsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var stack = (StackPanel)Content;
        stack.Children.Remove(presenter);
        var scale = new OverlayScaleRoot { InterfaceScale = .9, Width = 400, HorizontalAlignment = HorizontalAlignment.Left };
        scale.Children.Add(presenter);
        stack.Children.Add(scale);
        stack.Margin = new Thickness(60);
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 32));
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-slider-pixels")));
        var arcade = catalog.BuiltInThemes.Single(entry => entry.Descriptor.Id == "widgetrail.builtin.arcade-rush");
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), arcade.Package).Theme!;
        Dictionary<string, BridgeComputedStyleValue> Resolve(params WrssPseudoState[] states)
        {
            var values = theme.Resolve(new("slider", "adjust", new HashSet<string>(), states.ToHashSet())).Properties.ToDictionary(pair => pair.Key,
                pair => new BridgeComputedStyleValue { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
            values["width"] = new() { Kind = WrssValueKind.Length, Text = "160px", Number = 160, Unit = "px" };
            values["height"] = new() { Kind = WrssValueKind.Length, Text = "44px", Number = 44, Unit = "px" };
            return values;
        }
        pixelStyles = new() { Base = Resolve(), Focused = Resolve(WrssPseudoState.Focused), Pressed = Resolve(WrssPseudoState.Focused, WrssPseudoState.Pressed) };
        minimum = 0; maximum = 1; step = .05;
        foreach (var zoom in new[] { .9, 1.25 })
        foreach (var endpoint in new[] { 0d, .5, 1d })
        foreach (var engaged in new[] { false, true })
        {
            scale.InterfaceScale = zoom;
            presenter.ResetPressedStyles(); value = endpoint; Apply(); await Task.Delay(150); Focus("adjust");
            if (engaged) await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await Task.Delay(300);
            var slider = (Slider)Find(presenter, "Widget.adjust")!;
            var phase = FormattableString.Invariant($"{zoom:0.00}-{endpoint:0.0}-{engaged}");
            var parts = new List<object>();
            void Visit(DependencyObject item)
            {
                if (item is FrameworkElement element)
                    parts.Add(new { type = element.GetType().Name, element.Name, element.ActualWidth, element.ActualHeight,
                        element.Margin, bounds = element.TransformToVisual(slider).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight)),
                        slot = LayoutInformation.GetLayoutSlot(element), clip = (element.Clip as RectangleGeometry)?.Rect });
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) Visit(VisualTreeHelper.GetChild(item, i));
            }
            Visit(slider);
            status.Text = phase;
            File.WriteAllText(Path.Combine(directory, "checkpoint.json"), JsonSerializer.Serialize(new { phase, dpi = XamlRoot.RasterizationScale, parts }));
            var resume = Path.Combine(directory, phase + ".continue");
            for (var attempt = 0; !File.Exists(resume); attempt++)
            {
                if (attempt == 400) throw new TimeoutException("Slider pixel capture was not acknowledged: " + phase);
                await Task.Delay(100);
            }
        }
        File.WriteAllText(Path.Combine(directory, "complete.json"), "{}");
    }
}
