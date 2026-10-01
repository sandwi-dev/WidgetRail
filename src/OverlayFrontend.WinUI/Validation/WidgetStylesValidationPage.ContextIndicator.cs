using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeContextIndicatorAsync()
    {
        var page = new WidgetViewPresenter { Width = 240, Height = 240 };
        var scroll = new ScrollViewer { Content = page, Width = 250, Height = 115,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        host.Children.Add(scroll);
        var trigger = ControllerButton.Menu;
        var disabled = false; var busy = false;
        var color = "#00ff00";
        void Apply()
        {
            ViewNode Tile(string id) => new() { Id = id, Kind = ViewNodeKind.ActionSurface, ActionId = "open", AccessibilityLabel = id,
                ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal, ContextMenuButton = trigger,
                ContextActions = (WidgetContextAction[])[new("options", "Options", IsDisabled: disabled, IsBusy: busy)],
                Children = (ViewNode[])[new() { Id = id + ".text", Kind = ViewNodeKind.Text, Text = id }] };
            var root = new ViewNode { Id = "badges", Kind = ViewNodeKind.Stack,
                Children = (ViewNode[])[Tile("first"), Tile("second")] };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            foreach (var id in new[] { "first", "second" })
                styles[id] = Compute($"actionSurface {{ width: 220px; height: 100px; padding: 12px; border-width: 2px; corner-radius: 12px; background: #202030; color: {color}; }} actionSurface:focused {{ scale: 1.05; }}", id, "actionSurface");
            page.Apply(CreateFrame(root, styles));
        }
        try
        {
            WidgetControllerPrompts.Set(ControllerFamily.Xbox);
            Apply();
            await Wait(() => Find<Button>("Widget.first", page)?.IsLoaded == true);
            var first = Find<Button>("Widget.first", page)!;
            var second = Find<Button>("Widget.second", page)!;
            outside.Focus(FocusState.Keyboard);
            await Task.Delay(40);
            Check(NativeComputedStyleAdapter.For(first)!.ContextIndicator?.IsShown != true, "unfocused row has no context badge");
            var width = first.ActualWidth; var height = first.ActualHeight;
            first.Focus(FocusState.Keyboard);
            await Wait(() => NativeComputedStyleAdapter.For(first)!.ContextIndicator?.IsShown == true);
            var hint = NativeComputedStyleAdapter.For(first)!.ContextIndicator!;
            Check(first.ActualWidth == width && first.ActualHeight == height && hint.DesiredSize.Width == 0 && hint.DesiredSize.Height == 0,
                "context badge contributes no row measurement");
            Check(Math.Abs(hint.BadgeBounds.Right - (first.ActualWidth - 5)) < 1 && hint.BadgeBounds.Top == 5 && hint.BadgeBounds.Width == 26,
                "context badge matches native 26-DIP size and five-DIP top-right inset");
            Check(!hint.IsHitTestVisible && AutomationProperties.GetAccessibilityView(hint) == AccessibilityView.Raw,
                "context badge introduces no pointer or accessibility target");
            Check(hint.Glyph.Glyph == char.ConvertFromUtf32(WidgetGlyphs.Character(ControllerPrompt.Menu, false)) &&
                ColorOf(hint.Glyph.Foreground) == Microsoft.UI.Colors.Lime, "context badge uses the shared themed controller glyph");
            var xbox = hint.Glyph.Glyph;
            WidgetControllerPrompts.Set(ControllerFamily.PlayStation);
            await Wait(() => hint.Glyph.Glyph != xbox);
            Check(true, "context badge follows controller-family changes without widget publication");
            foreach (var button in new[] { ControllerButton.X, ControllerButton.Y })
            {
                trigger = button; Apply();
                var prompt = button == ControllerButton.X ? ControllerPrompt.X : ControllerPrompt.Y;
                await Wait(() => hint.Glyph.Glyph == char.ConvertFromUtf32(WidgetGlyphs.Character(prompt, true)));
                Check(hint.IsShown, "context badge follows authored " + button + " menu shortcut");
            }
            color = "#ff00ff"; Apply();
            await Wait(() => ColorOf(hint.Glyph.Foreground) == Microsoft.UI.Colors.Magenta);
            Check(true, "context badge updates with resolved theme ink");
            disabled = true; Apply(); await Wait(() => !hint.IsShown);
            Check(true, "all-disabled context actions hide the badge");
            disabled = false; busy = true; Apply(); await Task.Delay(30);
            Check(!hint.IsShown, "all-busy context actions hide the badge");
            busy = false; Apply(); await Wait(() => hint.IsShown);
            page.SetPresentationInputEnabled(false); Check(!hint.IsShown, "withdrawing input hides context affordance while native focus is retained");
            page.SetPresentationInputEnabled(true); await Wait(() => hint.IsShown);
            NativeComputedStyleAdapter.SetRootFocusPresentation(page.XamlRoot, false);
            Check(!hint.IsShown, "passive pinned-root policy hides the context badge");
            NativeComputedStyleAdapter.SetRootFocusPresentation(page.XamlRoot, true); await Wait(() => hint.IsShown);
            scroll.ChangeView(null, 45, null, true);
            await Wait(() => scroll.VerticalOffset > 30 && !hint.IsShown);
            Check(true, "partly clipped top-right badge is suppressed while scrolling");
            scroll.ChangeView(null, 0, null, true); await Wait(() => hint.IsShown);
            scroll.IsEnabled = false; await Wait(() => !hint.IsShown);
            Check(true, "disabled native ancestor suppresses the badge");
            scroll.IsEnabled = true; second.Focus(FocusState.Keyboard);
            await Wait(() => !hint.IsShown && NativeComputedStyleAdapter.For(second)!.ContextIndicator?.IsShown == true);
            Check(true, "context badge follows focus to the next row and clears the old row");
        }
        finally
        {
            if (page.XamlRoot is { } root) NativeComputedStyleAdapter.SetRootFocusPresentation(root, true);
            WidgetControllerPrompts.Set(ControllerFamily.Xbox);
            await page.DisposeAsync(); host.Children.Remove(scroll);
        }
    }
}
