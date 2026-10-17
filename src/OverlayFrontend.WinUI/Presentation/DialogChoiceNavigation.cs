using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Shared native list input; dialog actions and lifetimes remain with their owners.</summary>
internal static class DialogChoiceNavigation
{
    // Explicit ListViewItems can produce their content in ItemClick. Resolve
    // against the current list so pointer input never uses an old selection.
    internal static ListViewItem? ClickedItem(ListView list, object clicked) =>
        list.Items.OfType<ListViewItem>().FirstOrDefault(item =>
            ReferenceEquals(item, clicked) || ReferenceEquals(item.Content, clicked));

    internal static ListViewItem? FocusedItem(ListView list)
    {
        ListViewItem? item = null;
        var node = list.XamlRoot is null ? null : FocusManager.GetFocusedElement(list.XamlRoot) as DependencyObject;
        while (node is not null && !ReferenceEquals(node, list))
        {
            if (node is ListViewItem row) item = row;
            node = VisualTreeHelper.GetParent(node);
        }
        return ReferenceEquals(node, list) && item is not null && list.Items.Contains(item) ? item : null;
    }

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
        var current = FocusedItem(list) is { } item ? list.Items.IndexOf(item) : list.SelectedIndex;
        var next = current + (direction == FocusNavigationDirection.Up ? -1 : 1);
        if (next < 0 || next >= list.Items.Count) return false;
        list.SelectedIndex = next;
        list.ScrollIntoView(list.Items[next]);
        (list.Items[next] as Control)?.Focus(FocusState.Keyboard);
        return true;
    }
}
