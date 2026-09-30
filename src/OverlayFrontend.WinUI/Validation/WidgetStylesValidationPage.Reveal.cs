using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeWidgetRevealAsync()
    {
        var background = new Microsoft.UI.Xaml.Controls.Border { Width = 240, Height = 80 };
        host.Children.Add(background);
        var page = new WidgetViewPresenter { Width = 240, Height = 80, Opacity = 0 };
        host.Children.Add(page);
        try
        {
            page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full,
                AnimateWidgetSwitching = true, WidgetAnimationSpeed = .5 }, true);
            page.Apply(CreateFrame(new() { Id = "reveal", Kind = ViewNodeKind.Button, Text = "Ready content", ActionId = "play" },
                new Dictionary<string, BridgeNodeRenderStyles>()));
            var visual = ElementCompositionPreview.GetElementVisual(page);
            await Wait(() => page.IsLoaded && page.ActualWidth > 0 && visual.Opacity == 0);
            var fill = ElementCompositionPreview.GetElementVisual(background);
            foreach (var position in Enum.GetValues<OverlayPosition>())
            {
                page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full,
                    AnimateWidgetSwitching = true, WidgetAnimationSpeed = .5, OverlayPosition = position }, true);
                page.Opacity = 1;
                var prior = new System.Numerics.Vector2(480, 160);
                page.ResizeWidgetContent(prior, background);
                Check(visual.Scale == new System.Numerics.Vector3(2, 2, 1) && fill.Scale == visual.Scale,
                    "widget and fill start at the previous extent together");
                var anchor = position == OverlayPosition.BottomLeft ? 0 : position == OverlayPosition.BottomRight ? 240 : 120;
                Check(visual.CenterPoint == new System.Numerics.Vector3(anchor, 80, 0),
                    "resize keeps the selected horizontal anchor and bottom edge fixed");
                page.UpdateLayout();
                Check(await page.WidgetResizeCompletion! == WidgetRail.OverlayFrontend.WinUI.Motion.WidgetMotionOutcome.Completed,
                    "widget resize completes after native layout");
                await Task.Delay(40);
                Check(visual.Opacity == 1 && visual.Scale == System.Numerics.Vector3.One && fill.Scale == visual.Scale,
                    "widget remains opaque and settles at its actual extent");
                page.Opacity = 0;
                await Wait(() => visual.Opacity == 0);
            }
            page.Opacity = 1; page.ResizeWidgetContent(new(120, 40), background);
            page.UpdateLayout();
            await Task.Delay(50);
            var intermediate = page.PresentedWidgetSize;
            Check(intermediate.X > 120 && intermediate.X < 240 && intermediate.Y > 40 && intermediate.Y < 80,
                "superseding resize samples the in-flight extent instead of its endpoint");
            var interrupted = page.WidgetResizeCompletion!;
            page.SettleWidgetResize();
            page.Width = background.Width = 360;
            page.Height = background.Height = 120;
            page.ResizeWidgetContent(intermediate, background, Motion.WidgetResizeReason.ContentSizeChanged);
            Check(await interrupted == Motion.WidgetMotionOutcome.Canceled &&
                visual.Scale == new System.Numerics.Vector3(intermediate.X / 360, intermediate.Y / 120, 1) && fill.Scale == visual.Scale,
                "retargeted content resize starts both layers from the interrupted extent");
            page.UpdateLayout();
            Check(await page.WidgetResizeCompletion! == Motion.WidgetMotionOutcome.Completed,
                "retargeted content resize completes at its new native extent");
            page.ResizeWidgetContent(new(120, 40), background);
            page.Opacity = 0; page.SettleWidgetResize();
            await Task.Delay(100);
            Check(visual.Opacity == 0 && visual.Scale == System.Numerics.Vector3.One && fill.Scale == visual.Scale,
                "canceling resize settles both layers without overriding newer opacity");
        }
        finally { host.Children.Remove(page); host.Children.Remove(background); await page.DisposeAsync(); }
    }
}
