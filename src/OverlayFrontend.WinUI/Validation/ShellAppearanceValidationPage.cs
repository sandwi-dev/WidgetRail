using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ShellAppearanceValidationPage : Page, IAsyncDisposable
{
    private readonly OverlayDesktopBackdrop backdrop = new();
    private readonly Window owner;
    private readonly TextBlock status = new() { Text = "Checking shell appearance…", TextWrapping = TextWrapping.Wrap };
    private readonly Button focus = new() { Content = "Native focus remains here" };
    private readonly List<string> checks = [];
    private readonly StackPanel specimens = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private bool started;
    internal ShellAppearanceValidationPage(Window owner)
    {
        this.owner = owner;
        AutomationProperties.SetAutomationId(status, "ShellAppearance.Result");
        AutomationProperties.SetAutomationId(focus, "ShellAppearance.Focus");
        var hide = new Button { Content = "Hide test backdrop" };
        hide.Click += (_, _) => backdrop.Apply(0, default, default, 0, false);
        AutomationProperties.SetAutomationId(hide, "ShellAppearance.HideBackdrop");
        Content = new StackPanel { Spacing = 16, Children = { status, specimens, focus, hide } };
        Loaded += async (_, _) => { if (!started) { started = true; await RunAsync(); } };
    }

    private static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Palette(string color)
    {
        var style = new Dictionary<string, BridgeComputedStyleValue>
        {
            ["background"] = new() { Kind = WrssValueKind.Color, Text = color },
            ["opacity"] = new() { Kind = WrssValueKind.Number, Text = "0.8", Number = .8 },
        };
        return new Dictionary<string, BridgeNodeRenderStyles> { ["panel"] = new() { Base = style, Focused = style, Pressed = style } };
    }

    private async Task RunAsync()
    {
        try
        {
            var palette = Palette("rgba(240,40,30,0.5)");
            var settings = AppearanceSettings.Default;
            var panels = new List<Border>();
            foreach (var mode in new[] { WidgetSurfaceAppearance.Theme, WidgetSurfaceAppearance.Solid, WidgetSurfaceAppearance.Transparent })
            {
                var panel = new Border { Width = 160, Height = 120, Child = new TextBlock { Text = mode.ToString(),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
                var blue = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 20, 80, 220)) };
                blue.Children.Add(panel); specimens.Children.Add(blue); panels.Add(panel);
                OverlaySurfacePaint.Apply(panel, OverlayAppearancePolicy.Resolve(settings, "fixture", mode, false), palette);
            }
            await Task.Delay(100);
            var theme = ((SolidColorBrush)panels[0].Background).Color;
            Check(theme.A == 102 && theme.R == 240 && panels[0].Opacity == 1, "Theme multiplies palette brush alpha and opacity without fading content");
            Check(((SolidColorBrush)panels[1].Background).Color.A == 255, "Solid preserves palette RGB while forcing opaque paint");
            Check(((SolidColorBrush)panels[2].Background).Color.A == 0, "Transparent removes only host panel fill");
            async Task<Color> Pixel(UIElement specimen)
            {
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(specimen);
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                var offset = ((bitmap.PixelHeight / 8) * bitmap.PixelWidth + bitmap.PixelWidth / 2) * 4;
                return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            }
            var themed = await Pixel(specimens.Children[0]);
            var solid = await Pixel(specimens.Children[1]);
            var clear = await Pixel(specimens.Children[2]);
            Check(Math.Abs(themed.R - 108) < 4 && Math.Abs(themed.B - 144) < 4, "native Theme pixels blend authored fill with content behind it");
            Check(solid.R > 235 && solid.B < 35 && clear.B > 215 && clear.R < 25,
                $"native Solid and Transparent pixels distinguish the declared modes ({solid}; {clear})");
            var original = panels[0].Background;
            OverlaySurfacePaint.Apply(panels[0], OverlayAppearancePolicy.Resolve(settings, "fixture", WidgetSurfaceAppearance.Theme, false), Palette("rgba(240,40,30,0.5)"));
            Check(ReferenceEquals(original, panels[0].Background), "palette updates reuse the native brush rather than replacing content");
            var reduced = OverlayAppearancePolicy.Resolve(settings with { Transparency = TransparencyPreference.Reduced }, "fixture", WidgetSurfaceAppearance.Transparent, false);
            Check(OverlaySurfacePaint.Panel(reduced, palette).A == 255, "reduced transparency forces solid fill");
            var contrast = OverlayAppearancePolicy.Resolve(settings with { Contrast = ContrastPreference.High }, "fixture", WidgetSurfaceAppearance.Transparent, false);
            Check(OverlaySurfacePaint.Panel(contrast, palette) == new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background),
                "high contrast uses the Windows background palette");

            focus.Focus(FocusState.Keyboard);
            var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
            var foreground = GetForegroundWindow();
            backdrop.Apply(ownerHandle, area, Microsoft.UI.Colors.Black, .64, true);
            await Task.Delay(100);
            Check(backdrop.IsVisible && backdrop.PaintedColor.A == 163, "native backdrop shows bounded configured opacity across the selected monitor");
            Check(GetForegroundWindow() == foreground && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), focus),
                "showing the backdrop does not steal foreground or native control focus");
            const uint gwHwndPrevious = 3;
            var predecessor = GetWindow(backdrop.Handle, gwHwndPrevious);
            var above = false;
            for (var count = 0; predecessor != 0 && count < 512; ++count, predecessor = GetWindow(predecessor, gwHwndPrevious))
                if (predecessor == ownerHandle) { above = true; break; }
            Check(above, "overlay remains above its backdrop in native z order, including hidden IME windows");
            backdrop.Apply(ownerHandle, area, Microsoft.UI.Colors.Black, .2, true);
            Check(backdrop.PaintedColor.A == 51, "appearance update changes only backdrop paint opacity");
            backdrop.Apply(ownerHandle, area, default, .2, false);
            Check(!backdrop.IsVisible, "overlay hide retires visible backdrop immediately");
            backdrop.Apply(ownerHandle, area, default, 0, true);
            Check(!backdrop.IsVisible, "zero-opacity backdrop stays hidden");
            backdrop.Apply(ownerHandle, area, Microsoft.UI.Colors.Black, .64, true);
            Check(backdrop.IsVisible && GetForegroundWindow() == foreground, "backdrop reopens without another activation path");
            status.Text = $"Passed {checks.Count} shell appearance checks";
            Write(new { result = "passed", checks, pixels = new { themed, solid, clear } });
        }
        catch (Exception error) { status.Text = "Failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString() }); }
    }

    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    private static void Write<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "shell-appearance-result.json"), JsonSerializer.Serialize(value));
    }
    public ValueTask DisposeAsync() { backdrop.Dispose(); return ValueTask.CompletedTask; }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, uint command);
}
