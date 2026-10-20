using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AnimatedScrollLifetimeAsync()
    {
        var rows = Enumerable.Range(0, 60).Select(index => new ViewNode
        {
            Id = "motion.row-" + index, Kind = ViewNodeKind.Button, Text = "Row " + index, ActionId = "play",
        }).ToArray();
        var viewport = new ViewNode { Id = "motion.scroll", Kind = ViewNodeKind.Scroll,
            ScrollAxis = ScrollAxis.Vertical, Children = rows };
        var root = new ViewNode { Id = "motion.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[viewport] };
        var styles = rows.ToDictionary(row => row.Id,
            row => Compute("button { height: 44px; min-height: 44px; }", row.Id, "button"));
        styles[viewport.Id] = Compute("scroll { height: 240px; }", viewport.Id, "scroll");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<ScrollViewer>("Widget.motion.scroll")?.ScrollableHeight > 2000);
        var scroll = Find<ScrollViewer>("Widget.motion.scroll")!;
        Find<Button>("Widget.motion.row-0")!.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        try
        {
            foreach (var policy in new[] { (MotionPreference.Full, true), (MotionPreference.Reduced, true), (MotionPreference.System, false) })
            {
                presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = policy.Item1 }, policy.Item2);
                await Reset();
                presenter.ScrollBy(0, 500);
                await Wait(() => scroll.VerticalOffset > 1);
                if (policy.Item1 == MotionPreference.Full)
                    Check(scroll.VerticalOffset < 499 && !presenter.SettleScrollFocus(),
                        "full motion interpolates natively and defers passive focus until completion");
                await Wait(presenter.SettleScrollFocus);
                Check(Math.Abs(scroll.VerticalOffset - 500) < 1,
                    "native scroll reaches the requested destination under " + policy);
                await Task.Delay(120);
                Check(Math.Abs(scroll.VerticalOffset - 500) < 1,
                    "completed scroll does not rewind when focus settles under " + policy);
            }
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
            await Reset();
            presenter.ScrollBy(0, 900);
            await Wait(() => scroll.VerticalOffset > 60 && scroll.VerticalOffset < 850);
            var reversedAt = scroll.VerticalOffset;
            var reversedDestination = double.NaN;
            presenter.ScrollRequestTrace = (_, y) => reversedDestination = y;
            presenter.ScrollBy(0, -40);
            presenter.ScrollRequestTrace = null;
            await Wait(() => Math.Abs(scroll.VerticalOffset - reversedDestination) < 1);
            await Task.Delay(250);
            Check(Math.Abs(scroll.VerticalOffset - reversedDestination) < 1 && reversedDestination < reversedAt + 100,
                $"reversing active native motion discards the forward destination (from {reversedAt}, now {scroll.VerticalOffset})");
            presenter.ScrollBy(0, -10000);
            await Wait(presenter.SettleScrollFocus);
            Check(scroll.VerticalOffset < 1, "reversal clamps at the start without residual forward motion");

            foreach (var interruption in new[] { "hide", "input", "focus", "motion", "replace" })
            {
                await Reset();
                presenter.ScrollBy(0, 900);
                await Wait(() => scroll.VerticalOffset > 10 && scroll.VerticalOffset < 850);
                var beforeStop = scroll.VerticalOffset;
                switch (interruption)
                {
                    case "hide": await presenter.SetPresentationActiveAsync(false); break;
                    case "input": presenter.SetPresentationInputEnabled(false); break;
                    case "focus": presenter.SetAutomaticFocusEnabled(false); break;
                    case "motion": presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, true); break;
                    case "replace":
                        presenter.Apply(CreateFrame(new() { Id = "replacement", Kind = ViewNodeKind.Button,
                            Text = "Replacement", ActionId = "replacement" }, new Dictionary<string, BridgeNodeRenderStyles>()));
                        break;
                }
                if (interruption is "hide" or "input")
                    Check(!presenter.ScrollBy(0, 50), interruption + " refuses further scroll input");
                await Task.Delay(350);
                // Cancellation reports the final compositor pose on a later
                // ViewChanged. It must stop nearby, not replay the 900-DIP target.
                var stoppedAt = scroll.VerticalOffset;
                Check(stoppedAt >= beforeStop - 1 && stoppedAt < beforeStop + 100,
                    interruption + " stops near the current displayed position without rewinding");
                await Task.Delay(200);
                Check(Math.Abs(scroll.VerticalOffset - stoppedAt) < 1,
                    $"{interruption} cancels owned motion without delayed replay (from {stoppedAt}, now {scroll.VerticalOffset})");
                Check(presenter.SettleScrollFocus(), interruption + " retires pending focus settlement");
                await presenter.SetPresentationActiveAsync(true);
                presenter.SetPresentationInputEnabled(true);
                presenter.SetAutomaticFocusEnabled(true);
                presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
                if (interruption == "replace")
                {
                    presenter.Apply(CreateFrame(root, styles));
                    await Wait(() => Find<ScrollViewer>("Widget.motion.scroll")?.ScrollableHeight > 2000);
                    scroll = Find<ScrollViewer>("Widget.motion.scroll")!;
                }
            }
            await Reset();
            presenter.ScrollBy(0, 900);
            await Wait(() => scroll.VerticalOffset > 100 && scroll.VerticalOffset < 850);
            presenter.MoveFocus(FocusNavigationDirection.Down);
            var focused = FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
            Check(focused is Button && focused != Find<Button>("Widget.motion.row-0"),
                "directional input interrupts native motion and immediately navigates from visible content");
            var stopped = scroll.VerticalOffset;
            await Task.Delay(350);
            Check(Math.Abs(scroll.VerticalOffset - stopped) < 100 && scroll.VerticalOffset < 500,
                "directional interruption cannot resume the old scroll destination");
            var bounds = focused!.TransformToVisual(scroll).TransformBounds(new(0, 0, focused.ActualWidth, focused.ActualHeight));
            Check(bounds.Bottom > 0 && bounds.Top < scroll.ViewportHeight && scroll.BringIntoViewOnFocusChange,
                "directional interruption restores native reveal and leaves its selected control visible");
            await Reset();
            presenter.ScrollBy(0, 900);
            await Wait(() => scroll.VerticalOffset > 120 && scroll.VerticalOffset < 850);
            var oldDispatch = presenter.DispatchActionAsync;
            string? activated = null;
            presenter.DispatchActionAsync = request => { activated = request.Action.SourceElementId; return Task.CompletedTask; };
            try
            {
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await Wait(() => activated is not null);
                Check(activated != "motion.row-0", "Select during native scrolling cannot activate the old offscreen row");
            }
            finally { presenter.DispatchActionAsync = oldDispatch; }
        }
        finally
        {
            presenter.ApplyAppearance(AppearanceSettings.Default, true);
        }

        async Task Reset()
        {
            // Revoke the previous controller intent before a separate programmatic reset.
            presenter.SetAutomaticFocusEnabled(false);
            scroll.ChangeView(null, 0, null, true);
            await Wait(() => scroll.VerticalOffset < 1);
            Find<Button>("Widget.motion.row-0")!.Focus(FocusState.Keyboard);
            presenter.SetAutomaticFocusEnabled(true);
            await Task.Delay(80);
        }
    }
}
