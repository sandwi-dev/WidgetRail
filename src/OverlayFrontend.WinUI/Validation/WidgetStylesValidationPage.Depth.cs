using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeDepthAsync()
    {
        await NativeDepthStatesAsync();
        var root = new ViewNode { Id = "depth-root", Kind = ViewNodeKind.Stack, Children = [
            new() { Id = "depth-button", Kind = ViewNodeKind.Button, Text = "Native depth", ActionId = "depth" }] };
        var style = Compute("""
            button { width: 280px; height: 70px; margin: 30px; background: #f0e8d0; color: #102030;
              corner-radius: 12px; shadow-color: rgba(0,0,0,.5); shadow-blur: 12px; shadow-offset-x: -3px;
              shadow-offset-y: 8px; border-color: #445566; border-width: 2px; border-top-color: #ff3322;
              border-bottom-color: #2255ff; transition-duration: 120ms; }
            button:focused { scale: 1.05; outline-color: #22eecc; outline-width: 2px; }
            button:pressed { shadow-color: transparent; }
            """, "depth-button", "button");
        var styles = new Dictionary<string, BridgeNodeRenderStyles> { ["depth-button"] = style,
            ["depth-root"] = Compute("stack { background: #dddddd; shadow-color: rgba(0,0,0,.25); shadow-blur: 5px; }", "depth-root", "stack") };
        NativeComputedStyleAdapter.SetAccessibilityPolicy(AppearanceSettings.Default with { Contrast = ContrastPreference.Standard });
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<Button>("Widget.depth-button") is { IsLoaded: true });
        var button = Find<Button>("Widget.depth-button")!;
        var adapter = NativeComputedStyleAdapter.For(button)!;
        await Wait(() => adapter.Depth?.IsRealized == true);
        var panel = Find<Grid>("Widget.depth-root")!;
        Check(NativeComputedStyleAdapter.For(panel)?.Depth?.IsRealized == true,
            "ordinary panel uses dedicated depth slots without replacing its authored background");
        var depth = adapter.Depth!;
        Check(depth.NativeShadow is { BlurRadius: 12, Offset.X: -3, Offset.Y: 8 } && Math.Abs(depth.NativeShadow.Opacity - .5) < .01,
            "authored shadow color alpha blur and signed offsets map to native DropShadow");
        Check(depth.EdgeCount == 4 && ColorOf(button.BorderBrush).A == 0 && button.BorderThickness == new Thickness(2),
            "native per-edge strokes replace uniform border pixels while preserving border layout width");
        Check(ColorOf(button.Background).R == 240 && button.Opacity == 1,
            "shadow layers remain behind authored content without tinting or fading the control");
        var creates = depth.NativeResourceCreates;
        presenter.Apply(CreateFrame(root, styles));
        Check(ReferenceEquals(depth, adapter.Depth) && depth.NativeResourceCreates == creates,
            "ordinary style updates reuse native shadow mask and edge resources");
        button.Focus(FocusState.Keyboard);
        await Wait(() => adapter.ScaleMotion is { IsAnimating: false } && button.Scale.X > 1);
        Check(adapter.FocusDecoration is not null && depth.IsRealized && ElementCompositionPreview.GetElementChildVisual(button) is not null,
            "depth template slots coexist with native focus outline and authored control scale");
        var size = button.ActualWidth;
        button.Width = size + 40;
        await Wait(() => button.ActualWidth > size);
        Check(ReferenceEquals(depth, adapter.Depth) && depth.NativeResourceCreates == creates,
            "resizing updates native shadow geometry without reallocating visual owners");
        host.Children.Remove(presenter);
        await Wait(() => !button.IsLoaded && !depth.IsRealized);
        Check(!depth.IsRealized, "unloading a styled control releases native depth masks and brushes");
        host.Children.Add(presenter);
        await Wait(() => depth.IsRealized);
        Check(depth.NativeResourceCreates == creates + 1,
            "reloading realizes a fresh depth owner without duplicating native visuals");
        NativeComputedStyleAdapter.SetAccessibilityPolicy(AppearanceSettings.Default with { Contrast = ContrastPreference.High });
        await Wait(() => adapter.Depth is null);
        Check(button.UseSystemFocusVisuals && button.BorderBrush is SolidColorBrush { Color.A: > 0 },
            $"high contrast removes decorative shadows and edge colors and restores native contrast border [systemFocus={button.UseSystemFocusVisuals}; border={ColorOf(button.BorderBrush)}]");
        NativeComputedStyleAdapter.SetAccessibilityPolicy(AppearanceSettings.Default with { Contrast = ContrastPreference.Standard });
        await Wait(() => adapter.Depth?.IsRealized == true);
        adapter.Update(null);
        Check(adapter.Depth is null, "removing depth declarations retires native masks and shadows");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => adapter.Depth?.IsRealized == true);
        var retiring = adapter.Depth!;
        presenter.Apply(CreateFrame(new() { Id = "retired-depth", Kind = ViewNodeKind.Text, Text = "Depth retired" }, new Dictionary<string, BridgeNodeRenderStyles>()));
        Check(!retiring.IsRealized, "removing native depth control releases compositor resources immediately");
        // Leave a themed visual specimen for an explicit screenshot after checks.
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<Button>("Widget.depth-button") is { IsLoaded: true });
        NativeComputedStyleAdapter.SetAccessibilityPolicy(AppearanceSettings.Default);
        await NativeCollectionDepthAsync();
    }
}
