using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeTextScaleScopesAsync()
    {
        var main = new Grid { Width = 300, Height = 50 }; var pin = new Grid { Width = 300, Height = 50 };
        NativeTextScaleScope.Set(main, 1);
        NativeTextScaleScope.Set(pin, 1.5);
        var mainText = new TextBlock { Text = "Main display" };
        var pinText = new TextBlock { Text = "Pinned display" };
        var nested = new Grid(); nested.Children.Add(pinText);
        main.Children.Add(mainText); pin.Children.Add(nested);
        host.Children.Insert(0, main); host.Children.Insert(0, pin);
        using var mainStyle = new NativeComputedStyleAdapter(mainText, false);
        using var pinStyle = new NativeComputedStyleAdapter(pinText, false);
        using var mainChrome = new ShellChromeStyles();
        using var pinChrome = new ShellChromeStyles();
        var first = new TextBlock(); var second = new TextBlock();
        var style = Compute("#scale { font-size: 20px; letter-spacing: 2px; }", "scale", "text");
        try
        {
            mainStyle.Update(style); pinStyle.Update(style);
            Check(mainText.FontSize == 20 && pinText.FontSize == 30, "logical presenter scope applies before first native layout");
            main.UpdateLayout(); pin.UpdateLayout();
            await Task.Delay(100);
            Check(mainText.FontSize == 20 && pinText.FontSize == 30,
                $"separate presenter scopes retain different native text sizes simultaneously (main={mainText.FontSize}, pin={pinText.FontSize}, scopes={NativeTextScaleScope.Find(mainText)?.Value}/{NativeTextScaleScope.Find(pinText)?.Value})");
            NativeTextScaleScope.Set(pin, 1.25);
            await Wait(() => pinText.FontSize == 25);
            Check(mainText.FontSize == 20 && pinText.CharacterSpacing == 100,
                "nested indexed-style text inherits its own scope without changing other text or letter-spacing ratio");
            mainChrome.Register(first, "body"); pinChrome.Register(second, "body");
            var palette = new Dictionary<string, BridgeNodeRenderStyles> { ["body"] = style };
            mainChrome.Update(palette, AppearanceSettings.Default with { TextScale = 1 }, true);
            pinChrome.Update(palette, AppearanceSettings.Default with { TextScale = 1.5 }, true);
            Check(first.FontSize == 20 && second.FontSize == 30 && mainText.FontSize == 20 && pinText.FontSize == 25,
                "independent main and compact chrome cannot overwrite widget or peer typography");
            pinChrome.Update(palette, AppearanceSettings.Default with { TextScale = 1.25 }, true);
            Check(second.FontSize == 25 && first.FontSize == 20,
                "retained shell palette still applies a changed local text scale");
            nested.Children.Remove(pinText); main.Children.Add(pinText);
            await Task.Delay(50);
            pinStyle.RefreshTextScale();
            await Wait(() => pinText.FontSize == 20);
            NativeTextScaleScope.Set(pin, 1.5);
            await Task.Delay(30);
            Check(pinText.FontSize == 20, "reparented native text releases its former scale subscription");
            NativeTextScaleScope.Set(main, 1.25);
            await Wait(() => mainText.FontSize == 25 && pinText.FontSize == 25);
            Check(true, "reparented native text follows its new presenter scope");
        }
        finally { host.Children.Remove(main); host.Children.Remove(pin); }
    }
}
