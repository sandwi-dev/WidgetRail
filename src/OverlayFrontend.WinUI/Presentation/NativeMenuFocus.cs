using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One bounded controller traversal for selection and command popups.</summary>
internal static class NativeMenuFocus
{
    internal static void Move<T>(IReadOnlyList<T> items, FocusNavigationDirection direction, ref int rememberedIndex) where T : Control
    {
        if (items.Count == 0 || direction is not (FocusNavigationDirection.Up or FocusNavigationDirection.Down)) return;
        var step = direction == FocusNavigationDirection.Up ? -1 : 1;
        var focused = items[0].XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
        var current = rememberedIndex >= 0 && rememberedIndex < items.Count ? rememberedIndex : step > 0 ? -1 : 0;
        for (var index = 0; index < items.Count; ++index)
            if (ReferenceEquals(items[index], focused)) { current = index; break; }
        for (var visited = 0; visited < items.Count; ++visited)
        {
            current = (current + step + items.Count) % items.Count;
            if (items[current] is { IsEnabled: true, Visibility: Visibility.Visible } item && item.Focus(FocusState.Keyboard))
            { rememberedIndex = current; return; }
        }
    }
}
