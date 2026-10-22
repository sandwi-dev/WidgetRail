using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ResizeNavigationAsync()
    {
        var stage = new Grid { Width = 640, Height = 440 };
        var backdrop = new Border { Width = 600, Height = 340 };
        var page = new WidgetViewPresenter { Width = 600, Height = 340 };
        stage.Children.Add(backdrop); stage.Children.Add(page); host.Children.Add(stage);
        Exception? failure = null;
        page.Failed = error => failure = error;
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 }, true);
        try
        {
            Render("home", 340);
            await Wait(() => page.IsLoaded && page.ActualHeight == 340);
            await Task.Delay(80);
            var prior = page.TransitionPlayback;
            Render("detail", 280);
            Check(page.TransitionPlayback == prior && page.OutgoingTransitionCount == 0,
                "size-changing navigation suppresses page playback before it starts");
            await Wait(() => page.WidgetResizeStarts == 1 || failure is not null);
            if (failure is not null) throw failure;
            Check(await page.WidgetResizeCompletion! == WidgetMotionOutcome.Completed,
                "size-changing navigation completes the existing resize animation");
            Render("nested", 280);
            await Wait(() => page.TransitionPlayback != prior || failure is not null);
            if (failure is not null) throw failure;
            Check(await page.TransitionPlayback! == WidgetMotionOutcome.Completed && page.WidgetResizeStarts == 1,
                "same-size navigation completes page motion without another resize");
            prior = page.TransitionPlayback;
            Render("home", 340, WidgetNavigationOperation.Back);
            Check(page.TransitionPlayback == prior && page.OutgoingTransitionCount == 0,
                "back navigation to a different size also uses resize only");
            Render("another", 340);
            Check(page.TransitionPlayback == prior && page.OutgoingTransitionCount == 0,
                "rapid navigation during an active resize does not add page motion");
            await Wait(() => page.WidgetResizeStarts == 2);
            Check(await page.WidgetResizeCompletion! == WidgetMotionOutcome.Completed,
                "rapid navigation leaves the resize completing normally");
            page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            Render("reduced", 280);
            Check(page.OutgoingTransitionCount == 0 && page.WidgetResizeCompletion!.IsCompleted,
                "reduced motion applies the new size without either animation");
        }
        finally { host.Children.Remove(stage); await page.DisposeAsync(); }

        void Render(string key, double height, WidgetNavigationOperation operation = WidgetNavigationOperation.Forward)
        {
            var node = new ViewNode { Id = "resize.route", Kind = ViewNodeKind.Stack,
                Transition = new("resize.pages", key, 0, WidgetTransitionKind.Page, operation),
                Children = (ViewNode[])[new() { Id = "resize.entry", Kind = ViewNodeKind.Button, Text = key, ActionId = "open" }] };
            page.Apply(CreateFrame(node, new Dictionary<string, BridgeNodeRenderStyles>()), () =>
            {
                var previous = new Vector2((float)page.Width, (float)page.Height);
                if (page.Height != height)
                {
                    page.Height = backdrop.Height = height;
                    page.ResizeWidgetContent(previous, backdrop, WidgetResizeReason.ContentSizeChanged);
                    return false;
                }
                return page.WidgetResizeCompletion is not { IsCompleted: false };
            });
        }
    }
}
