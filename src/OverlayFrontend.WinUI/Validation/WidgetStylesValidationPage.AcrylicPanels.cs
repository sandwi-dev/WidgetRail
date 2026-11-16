using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AcrylicPanelsAsync()
    {
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        presenter.ApplyAppearance(appearance, false);
        presenter.Width = 600;
        presenter.Height = 420;
        foreach (var kind in new[] { "stack", "row", "grid", "scroll", "focus-presentation" })
        {
            var child = UI.Button("Retained focus", "action", "material.action");
            WidgetElement panel = kind switch
            {
                "row" => UI.Row("material.panel", child),
                "grid" => UI.Grid("material.panel", new[] { GridTrack.Auto() }, new[] { GridTrack.Star() }, child),
                "scroll" => UI.VerticalScroll("material.panel", child),
                "focus-presentation" => UI.FocusPresentationSurface(child, UI.Text("Summary", "material.summary"), "material.panel"),
                _ => UI.Stack("material.panel", child),
            };
            var snapshot = new WidgetView(UI.Stack("material.root", panel).InputScope("material.scope"))
                .CreateSnapshot("material.fixture", 1);
            var acrylicStyle = Compute("#material { background: #26352e; background-material: acrylic; background-tint-opacity: .35; background-luminosity-opacity: .25; }", "material", "stack");
            var styles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["material.panel"] = acrylicStyle,
                // Material is deliberately ignored on leaf controls.
                ["material.action"] = acrylicStyle,
            };
            presenter.Apply(CreateFrame(snapshot.Root, styles, "material.scope"));
            await Wait(() => Find<Button>("Widget.material.action")?.IsLoaded == true);
            var element = Find<FrameworkElement>("Widget.material.panel")!;
            var target = element is WidgetPresentationSurface surface ? surface.StylePanel : element;
            Brush? Background() => target switch
            {
                Panel value => value.Background,
                Control value => value.Background,
                Border value => value.Background,
                _ => null,
            };
            var button = Find<Button>("Widget.material.action")!;
            button.Focus(FocusState.Keyboard);
            Check(Background() is AcrylicBrush, $"{kind} explicitly opts into native acrylic");
            Check(button.Background is not AcrylicBrush && button.Opacity == 1,
                $"{kind} keeps inner control fills and text independent of its material");
            var brush = Background();
            var adapter = NativeComputedStyleAdapter.For(target)!;
            adapter.Update(acrylicStyle);
            Check(ReferenceEquals(brush, Background()), $"{kind} reuses its brush on repeated publication");
            presenter.ApplyAppearance(appearance with { AcrylicEffects = false }, false);
            await Wait(() => Background() is SolidColorBrush { Color.A: 255 });
            Check(button.Opacity == 1, $"{kind} acrylic toggle preserves control opacity");
            presenter.ApplyAppearance(appearance, false);
            await Wait(() => Background() is AcrylicBrush);
            presenter.ApplyAppearance(appearance with { Transparency = TransparencyPreference.Reduced }, false);
            await Wait(() => Background() is SolidColorBrush { Color.A: 255 });
            presenter.ApplyAppearance(appearance, false);
            await Wait(() => Background() is AcrylicBrush);
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => Background() is SolidColorBrush);
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            await Wait(() => Background() is AcrylicBrush);
            adapter.Update(Compute("#material { background: rgba(38, 53, 46, .84); background-material: standard; }", "material", "stack"));
            Check(Background() is SolidColorBrush, $"{kind} can return to its ordinary alpha fill");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button),
                $"{kind} material and accessibility changes preserve native focus");
        }
        var previewStyles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["preview"] = Compute("preview { background: rgba(38,53,46,.4); background-material: acrylic; }", "preview", "preview"),
        };
        var palette = Shell.ShellChromePalette.Resolve(null, appearance);
        using var frame = new WidgetPreviewFrame();
        frame.Apply(PreviewFrameStyle.Resolve(previewStyles, palette, appearance));
        Check(frame.Surface.Background is AcrylicBrush, "Rich preview frame admits its declared acrylic");
        frame.Apply(PreviewFrameStyle.Resolve(previewStyles, palette, appearance with { AcrylicEffects = false }));
        Check(frame.Surface.Background is LinearGradientBrush plain && plain.GradientStops.All(stop => stop.Color.A == 255),
            "Rich preview acrylic off uses solid paint without retaining the blur brush");
    }
}
