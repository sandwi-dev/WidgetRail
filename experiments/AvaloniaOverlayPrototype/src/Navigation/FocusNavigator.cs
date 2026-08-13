using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace GameBarAlternative.AvaloniaPrototype.Navigation;

internal static class FocusNavigator
{
    public static bool HandleDirectionalKey(Control root, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return false;
        }

        if (!Move(root, e.Key))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    internal static bool Move(Control root, Key key)
    {
        var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is Slider && key is Key.Left or Key.Right)
        {
            return false;
        }

        if (focused is ListBoxItem && key is Key.Up or Key.Down)
        {
            return false;
        }

        var candidates = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
            .ToArray();

        if (candidates.Length == 0)
        {
            return false;
        }

        var currentIndex = focused is null ? -1 : Array.IndexOf(candidates, focused);
        var delta = key is Key.Left or Key.Up ? -1 : 1;
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + delta + candidates.Length) % candidates.Length;

        candidates[nextIndex].Focus(NavigationMethod.Directional);
        return true;
    }
}
