using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private string scrollFocus = "not-run";

    private async Task ProbeScrollFocusAsync()
    {
        scrollFocus = "pending"; Observe();
        try
        {
            var view = View() ?? throw new InvalidOperationException("Missing native indexed view.");
            view.ScrollIntoView(view.Items[0], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(0) is Control { IsLoaded: true, IsEnabled: true });
            ((Control)view.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            await Until(() => scroll.VerticalOffset < 1);
            await Task.Delay(100, lifetime.Token); // Finish initial ScrollIntoView/entry before the independent stick gesture.
            if (!presenter.ScrollBy(0, scroll.ViewportHeight * 3)) throw new InvalidOperationException("Scroll gesture was not admitted.");
            await Until(() => scroll.VerticalOffset > scroll.ViewportHeight);
            await Until(presenter.SettleScrollFocus);
            var focused = FocusManager.GetFocusedElement(XamlRoot) as Control;
            if (focused is null || view.IndexFromContainer(focused) <= 0)
                throw new InvalidOperationException("Scroll release did not select a realized visible row.");
            var bounds = focused.TransformToVisual(scroll).TransformBounds(new(0, 0, focused.ActualWidth, focused.ActualHeight));
            if (bounds.Y < -.5 || bounds.Bottom > scroll.ViewportHeight + .5)
                throw new InvalidOperationException("Settled row is not fully visible.");
            var before = AutomationProperties.GetAutomationId(focused);
            var offset = scroll.VerticalOffset;
            await Task.Delay(80, lifetime.Token);
            if (Math.Abs(scroll.VerticalOffset - offset) > 1) throw new InvalidOperationException("Focus settlement moved the viewport.");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => FocusManager.GetFocusedElement(XamlRoot) is Control next &&
                AutomationProperties.GetAutomationId(next) != before && view.IndexFromContainer(next) > 0);
            scrollFocus = "passed:3";
        }
        catch (Exception error) { scrollFocus = "failed:" + error.Message; }
        Observe();

        async Task Until(Func<bool> ready, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(ready))] string? condition = null)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!ready())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native scroll focus did not settle: " + condition);
                await Task.Delay(16, lifetime.Token);
            }
        }
    }
}
