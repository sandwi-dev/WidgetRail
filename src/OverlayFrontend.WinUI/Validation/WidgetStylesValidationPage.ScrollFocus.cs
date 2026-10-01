using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeScrollFocusAsync()
    {
        var priorWidth = presenter.Width;
        var priorHeight = presenter.Height;
        presenter.Width = 500; presenter.Height = 300;
        var rows = Enumerable.Range(0, 24).Select(index => new ViewNode
        {
            Id = "scroll-row-" + index, Kind = ViewNodeKind.Button, Text = "Track " + index, ActionId = "play",
        }).ToArray();
        var scrollNode = new ViewNode { Id = "scroll-focus.viewport", Kind = ViewNodeKind.Scroll,
            ScrollAxis = ScrollAxis.Vertical, Children = rows };
        var root = new ViewNode { Id = "scroll-focus.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[scrollNode] };
        var styles = rows.ToDictionary(row => row.Id, row => Compute("button { height: 44px; min-height: 44px; }", row.Id, "button"));
        styles[scrollNode.Id] = Compute("scroll { height: 240px; }", scrollNode.Id, "scroll");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<ScrollViewer>("Widget.scroll-focus.viewport")?.ScrollableHeight > 400);
        var scroll = Find<ScrollViewer>("Widget.scroll-focus.viewport")!;
        var first = Find<Button>("Widget.scroll-row-0")!;
        first.Focus(FocusState.Keyboard);
        // Finish initial entry/BringIntoView before simulating a later stick gesture.
        await Task.Delay(100);
        Check(presenter.ScrollBy(0, 480), "right-stick scrolling selects the focused declaration's native viewport");
        await Wait(() => scroll.VerticalOffset >= 470);
        var settledOffset = scroll.VerticalOffset;
        Check(presenter.SettleScrollFocus(), "right-stick release finds a ready visible focus target");
        var target = FocusManager.GetFocusedElement(XamlRoot) as Button;
        var bounds = target!.TransformToVisual(scroll).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        Check(target != first && bounds.Y >= -.5 && bounds.Bottom <= scroll.ViewportHeight + .5,
            "right-stick release focuses a fully visible row instead of its offscreen origin");
        await Task.Delay(50);
        Check(Math.Abs(scroll.VerticalOffset - settledOffset) < 1, "focus settlement preserves the user's viewport offset");
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), "directional navigation resumes from the visible settled row");
        target = FocusManager.GetFocusedElement(XamlRoot) as Button;
        presenter.ScrollBy(0, 1);
        await Task.Delay(30);
        Check(presenter.SettleScrollFocus() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target),
            "free scrolling preserves an already-visible focused control");
        presenter.ScrollBy(0, 300);
        presenter.SetAutomaticFocusEnabled(false);
        await Task.Delay(30);
        Check(presenter.SettleScrollFocus() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target),
            "revoking widget focus cancels pending scroll settlement");
        presenter.SetAutomaticFocusEnabled(true);
        presenter.Width = priorWidth; presenter.Height = priorHeight;
    }
}
