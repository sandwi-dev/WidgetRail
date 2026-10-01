using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Opt-in probe for the controller semantic route, never keyboard/mouse injection.
/// Call on the UI dispatcher with the real collection and the host's frame router.
/// It does not open a native controller session. No app startup hooks are installed.
/// </summary>
internal static class NativeIndexedFocusProbe
{
    internal sealed record Sample(int Step, int? FocusedIndex, int RealizedCount, bool RouteHandled);

    internal static async Task<IReadOnlyList<Sample>> RunAsync(ListViewBase view,
        Func<ControllerFrame, bool> route, int startIndex = 0, int steps = 80,
        int settleMilliseconds = 35, CancellationToken cancellationToken = default,
        NavigationDirection direction = NavigationDirection.Down)
    {
        if (!view.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Probe requires the UI dispatcher.");
        if (steps is < 1 or > 512 || settleMilliseconds is < 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(steps));
        if (startIndex < 0 || startIndex >= view.Items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        view.ScrollIntoView(view.Items[startIndex], ScrollIntoViewAlignment.Leading);
        var deadline = Environment.TickCount64 + 5000;
        while (view.ContainerFromIndex(startIndex) is not Control)
        {
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("Native initial container did not realize.");
            await Task.Delay(15, cancellationToken);
        }
        if (!((Control)view.ContainerFromIndex(startIndex)).Focus(FocusState.Keyboard))
            throw new InvalidOperationException("Native initial item rejected focus.");
        var samples = new List<Sample> { Capture(view, 0, true) };
        for (var step = 1; step <= steps; ++step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = ControllerFrame.Create();
            frame.Connected = 1;
            frame.DpadNavigation = new() { Direction = direction, Phase = NavigationPhase.Pressed };
            var handled = route(frame);
            if (settleMilliseconds > 0) await Task.Delay(settleMilliseconds, cancellationToken);
            samples.Add(Capture(view, step, handled));
        }
        if (settleMilliseconds == 0)
        {
            await Task.Delay(150, cancellationToken);
            samples[^1] = Capture(view, steps, samples[^1].RouteHandled);
        }
        return samples.AsReadOnly();
    }

    private static Sample Capture(ListViewBase view, int step, bool handled)
    {
        var focused = FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
        int? index = null;
        while (focused is not null && !ReferenceEquals(focused, view))
        {
            if (focused is Control control)
            {
                var candidate = view.IndexFromContainer(control);
                if (candidate >= 0) { index = candidate; break; }
            }
            focused = VisualTreeHelper.GetParent(focused);
        }
        var realized = 0;
        var pending = new Stack<DependencyObject>(); pending.Push(view);
        while (pending.TryPop(out var node))
        {
            if (node is ListViewItem or GridViewItem) ++realized;
            for (var child = 0; child < VisualTreeHelper.GetChildrenCount(node); ++child)
                pending.Push(VisualTreeHelper.GetChild(node, child));
        }
        return new(step, index, realized, handled);
    }
}
