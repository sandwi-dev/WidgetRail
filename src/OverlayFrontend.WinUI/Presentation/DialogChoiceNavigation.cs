using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Shared list navigation only; dialog actions and lifetimes remain with their owners.</summary>
internal static class DialogChoiceNavigation
{
    internal static bool ContainsFocus(FrameworkElement element)
    {
        if (element.XamlRoot is null) return false;
        for (var node = FocusManager.GetFocusedElement(element.XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, element)) return true;
        return false;
    }

    internal static bool Move(ListView list, FocusNavigationDirection direction)
    {
        if (!ContainsFocus(list) || direction is not (FocusNavigationDirection.Up or FocusNavigationDirection.Down)) return false;
        var next = list.SelectedIndex + (direction == FocusNavigationDirection.Up ? -1 : 1);
        if (next < 0 || next >= list.Items.Count) return false;
        list.SelectedIndex = next;
        list.ScrollIntoView(list.Items[next]);
        (list.Items[next] as Control)?.Focus(FocusState.Keyboard);
        return true;
    }
}
