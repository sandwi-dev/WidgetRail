using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task DirectionalScrollingAsync()
    {
        presenter.Width = 600; presenter.Height = 320;
        foreach (var animated in new[] { false, true })
        foreach (var horizontal in new[] { false, true })
        {
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = animated ? MotionPreference.System : MotionPreference.Reduced }, animated);
            var backward = horizontal ? FocusNavigationDirection.Left : FocusNavigationDirection.Up;
            var forward = horizontal ? FocusNavigationDirection.Right : FocusNavigationDirection.Down;
            ViewNode Button(string id) => new() { Id = id, Kind = ViewNodeKind.Button, Text = id, ActionId = id };
            var anchor = Button("anchor") with { Focus = horizontal ? new(Left: "exit", Right: "exit") : new(Up: "exit", Down: "exit") };
            var scroll = new ViewNode { Id = "reading", Kind = ViewNodeKind.Scroll,
                ScrollAxis = horizontal ? ScrollAxis.Horizontal : ScrollAxis.Vertical, GridCell = new() { Row = 1 },
                Children = (ViewNode[])[new() { Id = "leading", Kind = ViewNodeKind.Spacer }, anchor,
                    new() { Id = "trailing", Kind = ViewNodeKind.Spacer }] };
            var root = new ViewNode { Id = "scroll-test", Kind = ViewNodeKind.Grid,
                GridLayout = new() { Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 50 }, new() { Sizing = GridTrackSizing.Star }] },
                Children = (ViewNode[])[Button("exit"), scroll] };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["scroll-test"] = Compute("#x { padding: 0px; gap: 0px; }", "x", "grid"),
                ["reading"] = Compute("#x { padding: 0px; gap: 0px; }", "x", "scroll"),
                ["anchor"] = Compute("#x { width: 100px; height: 44px; min-height: 0px; padding: 0px; margin: 0px; }", "x", "button"),
                ["leading"] = Compute(horizontal ? "#x { width: 700px; }" : "#x { height: 400px; }", "x", "spacer"),
                ["trailing"] = Compute(horizontal ? "#x { width: 900px; }" : "#x { height: 600px; }", "x", "spacer"),
            };
            presenter.Apply(CreateFrame(root, styles));
            await Wait(() => Find<Button>("Widget.anchor") is { IsLoaded: true });
            var viewport = Find<ScrollViewer>("Widget.reading")!;
            var control = Find<Button>("Widget.anchor")!;
            double Offset() => horizontal ? viewport.HorizontalOffset : viewport.VerticalOffset;
            double Limit() => horizontal ? viewport.ScrollableWidth : viewport.ScrollableHeight;
            void Position(double value) => viewport.ChangeView(horizontal ? value : null, horizontal ? null : value, null, true);
            bool Focused() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control);
            async Task PlaceAnchor()
            {
                viewport.UpdateLayout();
                control.Focus(FocusState.Keyboard);
                // Let focus's native bring-into-view complete before establishing
                // the test offset; otherwise two independent requests race.
                await Task.Delay(100);
                Position(horizontal ? 660 : 370);
                try { await Wait(() => Math.Abs(Offset() - (horizontal ? 660 : 370)) < .5); }
                catch (Exception error) { throw new InvalidOperationException($"{backward} setup offset={Offset()} limit={Limit()} checks={checks.Count}", error); }
            }
            async Task Drain(FocusNavigationDirection direction, double destination)
            {
                for (var i = 0; i < 50 && Math.Abs(Offset() - destination) > .5; ++i)
                {
                    if (!presenter.MoveFocus(direction, isRepeat: true)) throw new InvalidOperationException("Scroll escaped its owner before the edge.");
                    await Task.Delay(20);
                }
                await Wait(() => Math.Abs(Offset() - destination) < .5);
            }
            await PlaceAnchor();
            var before = Offset();
            Check(presenter.MoveFocus(backward), $"{backward}: noninteractive leading content consumes navigation");
            await Wait(() => Offset() < before - 1);
            Check(Focused(), $"{backward}: external explicit link waits for the scroll boundary");
            await Drain(backward, 0);
            Check(Focused(), $"{backward}: scrolling through text retains its logical focus anchor");
            Check(presenter.MoveFocus(backward, isRepeat: true) && Focused(), $"{backward}: held input cannot escape at the boundary");
            Check(presenter.MoveFocus(backward) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.exit")),
                $"{backward}: fresh press follows the authored exit");

            await PlaceAnchor();
            // Two requests before WinUI publishes the first offset must accumulate.
            before = Offset();
            presenter.MoveFocus(forward); presenter.MoveFocus(forward, isRepeat: true);
            await Wait(() => Offset() >= before + 127);
            Check(Focused(), $"{forward}: pending native scroll updates accumulate without losing focus");
            await Drain(forward, Limit());
            presenter.Apply(CreateFrame(root, styles));
            Check(presenter.MoveFocus(forward, isRepeat: true) && Focused(), $"{forward}: equivalent snapshots preserve the held-edge guard");
            before = Offset();
            presenter.MoveFocus(backward);
            await Wait(() => Offset() < before - 1);
            Check(Focused(), $"{backward}: reversal scrolls toward the anchor instead of taking its external link");
            for (var i = 0; i < 5; ++i) { presenter.MoveFocus(backward, isRepeat: true); await Task.Delay(20); }
            Check(Focused(), $"{backward}: repeated reversal does not invoke free-scroll focus settling");

            // Input withdrawal retires the gesture even if the visual tree survives.
            Position(Limit()); await Wait(() => Math.Abs(Offset() - Limit()) < .5);
            presenter.SetPresentationInputEnabled(false); presenter.SetPresentationInputEnabled(true);
            Check(presenter.MoveFocus(forward, isRepeat: true) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.exit")),
                $"{forward}: withdrawn input does not leave a stale edge guard");

            // Without an authored exit, the exact same policy reports the root
            // boundary only after the scroll completes and a new press arrives.
            var unlinked = root with { Children = (ViewNode[])[root.Children[0], scroll with
                { Children = (ViewNode[])[scroll.Children[0], anchor with { Focus = null }, scroll.Children[2]] }] };
            presenter.Apply(CreateFrame(unlinked, styles));
            await PlaceAnchor();
            await Drain(forward, Limit());
            Check(presenter.MoveFocus(forward, isRepeat: true) && Focused(), $"{forward}: implicit root exit is also held at the edge");
            Check(!presenter.MoveFocus(forward) && Focused(), $"{forward}: fresh press reports the true root boundary");
            Check(presenter.MoveFocus(forward, isRepeat: true) && Focused(), $"{forward}: a declined root exit keeps held repeats inside the owner");
        }
    }
}
