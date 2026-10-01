using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeToolTipsAsync()
    {
        var origin = new Button { Content = "Tooltip origin", Width = 180, Height = 40 };
        host.Children.Add(origin);
        var theme = new NativePopupTheme(); theme.Attach(origin);
        var appearance = AppearanceSettings.Default with { Contrast = ContrastPreference.Standard, TextScale = 1, InterfaceScale = 1 };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["panel"] = Compute("#panel { background: #123456; corner-radius: 4px; }", "panel", "stack"),
            ["body"] = Compute("#body { color: #fff0cc; font-size: 16px; }", "body", "text"),
            ["hint"] = Compute("#hint { color: #aabbcc; font-size: 13px; font-family: Consolas; }", "hint", "text"),
        };
        theme.Update(styles, appearance);
        NativePopupTheme.SetToolTip(origin, "Seek backward");
        var tip = (ToolTip)ToolTipService.GetToolTip(origin);
        try
        {
            await Wait(() => origin.IsLoaded);
            origin.Focus(FocusState.Keyboard);
            tip.PlacementTarget = origin; tip.XamlRoot = origin.XamlRoot; tip.IsOpen = true;
            await Wait(() => tip.IsLoaded && tip.ActualWidth > 0);
            Check(ColorOf(tip.Background) == Color.FromArgb(255, 18, 52, 86) && ColorOf(tip.Foreground) == Color.FromArgb(255, 255, 240, 204),
                "native tooltip takes surface and text ink from the active theme");
            Check(tip.FontSize == 13 && tip.FontFamily.Source == "Consolas" && tip.CornerRadius.TopLeft == 3,
                "tooltip uses theme hint typography and compact theme-derived corners");
            Check(tip.Padding == new Thickness(8, 4, 8, 4) && tip.ActualHeight < 40 && tip.ActualWidth < 280,
                "short tooltip remains compact without a fixed minimum width or height");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), origin), "opening tooltip never steals controller focus");
            var surfaceBrush = tip.Background;
            styles["panel"] = Compute("#panel { background: #654321; corner-radius: 0px; }", "panel", "stack");
            theme.Update(new Dictionary<string, BridgeNodeRenderStyles>(styles), appearance with { BoldText = true, TextScale = 1.25, InterfaceScale = 1.25 });
            Check(ReferenceEquals(surfaceBrush, tip.Background) && ColorOf(tip.Background) == Color.FromArgb(255, 101, 67, 33) && tip.CornerRadius.TopLeft == 0,
                "open tooltip rethemes in place including square-corner themes");
            Check(tip.FontWeight.Weight >= 600 && Near(tip.FontSize, 13 * 1.25 * 1.25) && tip.Padding.Left == 10,
                "tooltip applies Bold Text, text scale and interface scale exactly once");
            var contrast = appearance with { Contrast = ContrastPreference.High };
            theme.Update(styles, contrast);
            var palette = ShellChromePalette.Resolve(styles, contrast);
            Check(ColorOf(tip.Background) == palette.Surface && ColorOf(tip.Foreground) == palette.Text && ColorOf(tip.BorderBrush) == palette.Focus,
                "tooltip respects the system high-contrast surface text and border palette");
            // WinUI can unload the popup before delivering Closed. The theme
            // registration releases its lease on that native lifetime event.
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler onClosed = (_, _) => closed.TrySetResult();
            tip.Closed += onClosed;
            try { tip.IsOpen = false; await closed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { tip.Closed -= onClosed; }
            theme.Update(styles, appearance with { TextScale = 1.5 });
            Check(tip.FontSize == 13, "closed tooltip releases its live appearance lease");
            tip.IsOpen = true; await Wait(() => tip.IsLoaded);
            Check(Near(tip.FontSize, 19.5), "reopened tooltip acquires current accessibility settings");
            NativePopupTheme.SetToolTip(origin, "Seek forward");
            Check(ReferenceEquals(tip, ToolTipService.GetToolTip(origin)) && (string)tip.Content == "Seek forward",
                "recycled control updates tooltip text without replacing the popup");
            host.Children.Remove(origin); await Wait(() => !tip.IsOpen);
            theme.Update(styles, appearance);
            Check(Near(tip.FontSize, 19.5), "unloaded tooltip origin releases its theme lease");
            NativePopupTheme.SetToolTip(origin, null);
            Check(ToolTipService.GetToolTip(origin) is null, "removed tooltip detaches its registration");
        }
        finally { NativePopupTheme.SetToolTip(origin, null); host.Children.Remove(origin); }
    }
}
