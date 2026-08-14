using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace GameBarAlternative.AvaloniaPrototype.Navigation;

public static class FocusNavigator
{
    internal static bool Move(
        Control focused,
        NavigationDirection direction,
        IEnumerable<Control> searchRoots)
    {
        var focusManager = TopLevel.GetTopLevel(focused)?.FocusManager;
        var target = FindTarget(focusManager, focused, direction, searchRoots);
        if (target is null || !focusManager!.Focus(target, NavigationMethod.Directional)) return false;
        target.BringIntoView();
        return true;
    }

    internal static Control? FindTarget(
        IFocusManager? focusManager,
        Control focused,
        NavigationDirection direction,
        IEnumerable<Control> searchRoots)
    {
        if (focusManager is null || !direction.IsDirectional())
        {
            return null;
        }

        foreach (var searchRoot in searchRoots.Distinct())
        {
            var target = focusManager.FindNextElement(
                direction,
                new FindNextElementOptions
                {
                    FocusedElement = focused,
                    SearchRoot = searchRoot,
                    NavigationStrategyOverride = XYFocusNavigationStrategy.RectilinearDistance,
                }) as Control;
            if (target is not null &&
                !ReferenceEquals(target, focused) &&
                (IsExplicitTarget(focused, target, direction) || IsStrictlyInDirection(focused, target, direction)))
            {
                return target;
            }
        }

        return null;
    }

    private static bool IsExplicitTarget(Control focused, Control target, NavigationDirection direction)
    {
        var explicitTarget = direction switch
        {
            NavigationDirection.Up => focused.GetValue(XYFocus.UpProperty),
            NavigationDirection.Down => focused.GetValue(XYFocus.DownProperty),
            NavigationDirection.Left => focused.GetValue(XYFocus.LeftProperty),
            NavigationDirection.Right => focused.GetValue(XYFocus.RightProperty),
            _ => null,
        };
        return ReferenceEquals(explicitTarget, target);
    }

    private static bool IsStrictlyInDirection(Control focused, Control target, NavigationDirection direction)
    {
        var root = TopLevel.GetTopLevel(focused);
        if (root is null || !ReferenceEquals(root, TopLevel.GetTopLevel(target)))
        {
            return false;
        }

        var fromOrigin = focused.TranslatePoint(default, root) ?? default;
        var toOrigin = target.TranslatePoint(default, root) ?? default;
        var fromX = fromOrigin.X + focused.Bounds.Width / 2;
        var fromY = fromOrigin.Y + focused.Bounds.Height / 2;
        var toX = toOrigin.X + target.Bounds.Width / 2;
        var toY = toOrigin.Y + target.Bounds.Height / 2;
        const double tolerance = 0.5;
        return direction switch
        {
            NavigationDirection.Up => toY < fromY - tolerance,
            NavigationDirection.Down => toY > fromY + tolerance,
            NavigationDirection.Left => toX < fromX - tolerance,
            NavigationDirection.Right => toX > fromX + tolerance,
            _ => false,
        };
    }
}
