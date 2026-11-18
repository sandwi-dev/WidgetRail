using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal static class PointerCaptureLifetime
{
    /// <summary>Release native control/thumb captures before an owned surface is disabled or detached.</summary>
    internal static void ReleaseWithin(DependencyObject root)
    {
        // Capture-lost handlers can change the visual tree; enumerate before invoking them.
        foreach (var element in CapturedWithin(root).ToArray()) element.ReleasePointerCaptures();
    }
    internal static bool HasCaptureWithin(DependencyObject root) => CapturedWithin(root).Any();
    private static IEnumerable<UIElement> CapturedWithin(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.TryPop(out var item))
        {
            if (item is UIElement element && element.PointerCaptures is { Count: > 0 }) yield return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) pending.Push(VisualTreeHelper.GetChild(item, i));
        }
    }
}
