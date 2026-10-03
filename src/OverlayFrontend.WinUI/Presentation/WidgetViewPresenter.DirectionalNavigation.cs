using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal event Action<FocusNavigationDirection>? DirectionalBoundaryReached;

    private void DirectionalKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || presentationOnly || XamlRoot is null || Input.GamepadKeyBoundary.Owns(this, args) || HasTransientControl ||
            FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or RichEditBox ||
            FocusedBrowser?.Surface?.HasNativeKeyboardFocus == true) return;
        var direction = args.Key switch
        {
            Windows.System.VirtualKey.Up => FocusNavigationDirection.Up, Windows.System.VirtualKey.Down => FocusNavigationDirection.Down,
            Windows.System.VirtualKey.Left => FocusNavigationDirection.Left, Windows.System.VirtualKey.Right => FocusNavigationDirection.Right,
            _ => FocusNavigationDirection.None,
        };
        if (direction == FocusNavigationDirection.None) return;
        // Tunnel before a native control can perform a second spatial move.
        args.Handled = true;
        if (!MoveFocus(direction, args.KeyStatus.WasKeyDown)) DirectionalBoundaryReached?.Invoke(direction);
    }

    private bool MoveDirectionalFocus(FocusNavigationDirection direction, bool isRepeat)
    {
        CancelScrollReveal();
        if (FocusedMediaPlayer?.MoveFocus(direction) == true) return true;
        if (FocusedBinding() is not { } origin || !NavigationTarget(origin) ||
            presentation?.IsCurrent != true) return true; // No current geometry is not a root exit.
        if (ReverseDirectionalScroll(origin, direction, isRepeat)) return true;
        var scrollOwners = DirectionalScrollOwners(origin, direction).ToArray();
        var indexed = FindIndexedCollection();
        var neighbors = indexed?.FocusedRowNavigation ?? declarations[origin.Identity.Id].Node.Focus;
        var explicitId = direction switch
        {
            FocusNavigationDirection.Up => neighbors?.Up, FocusNavigationDirection.Down => neighbors?.Down,
            FocusNavigationDirection.Left => neighbors?.Left, FocusNavigationDirection.Right => neighbors?.Right, _ => null,
        };
        Binding? deferredExit = null;
        if (explicitId is not null && bindings.TryGetValue(explicitId, out var authored) && !ReferenceEquals(authored, origin))
        {
            var target = NavigationTarget(authored) ? authored : GroupTarget(authored);
            if (target is not null && NavigationTarget(target))
            {
                // Cross-container links are exits. Reveal remaining content first;
                // links within the current container keep their authored priority.
                if (scrollOwners.Any(pair => !IsDescendant(target.Identity.Id, pair.Owner.Identity.Id) &&
                    (HasScrollRoom(pair.Viewport, direction) || directionalScroll is { } gesture &&
                        ReferenceEquals(gesture.Viewport, pair.Viewport) && gesture.Direction == direction))) deferredExit = target;
                else { Commit(target); return true; }
            }
        }
        if (indexed?.MoveFocus(direction) == true) return true;
        FrameworkElement? exitedViewport = null;
        if (indexed is not null)
            foreach (var (owner, viewport) in scrollOwners.Where(pair => ReferenceEquals(pair.Owner, origin)))
            {
                var wasScrolling = ReferenceEquals(directionalScroll?.Viewport, viewport);
                if (TryDirectionalScroll(owner, viewport, origin, direction, isRepeat)) return true;
                if (wasScrolling) exitedViewport = viewport;
            }

        var nativeOrigin = indexed?.FocusedNavigationElement ?? origin.Element;
        if (NavigationBounds(exitedViewport ?? nativeOrigin) is not { } originBounds) return true;
        var policyDirection = direction switch
        {
            FocusNavigationDirection.Up => FocusDirection.Up, FocusNavigationDirection.Down => FocusDirection.Down,
            FocusNavigationDirection.Left => FocusDirection.Left, FocusNavigationDirection.Right => FocusDirection.Right,
            _ => (FocusDirection?)null,
        };
        if (policyDirection is null) return false;
        var candidates = new Dictionary<string, (Binding Target, FocusRectangle Bounds)>(StringComparer.Ordinal);
        foreach (var candidate in bindings.Values)
        {
            if (ReferenceEquals(candidate, origin) || !NavigationTarget(candidate)) continue;
            var owner = candidate;
            // Enter an external remembered group as one region, then resolve its
            // current child. Internal moves still compare individual controls.
            for (var ancestor = declarations[candidate.Identity.Id]; ancestor is not null;
                 ancestor = ancestor.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            {
                if (ancestor.Identity.Scope != activeScope) break;
                if (ancestor.Node.InitialChildFocusId is not null && !IsDescendant(origin.Identity.Id, ancestor.Node.Id))
                    owner = bindings[ancestor.Node.Id];
            }
            var target = ReferenceEquals(owner, candidate) ? candidate : GroupTarget(owner);
            if (target is not null && NavigationTarget(target) && NavigationBounds(owner.Element) is { } bounds)
                candidates.TryAdd(owner.Identity.Id, (target, bounds));
        }
        // Match the original ownership order without retaining a parallel layout
        // tree: nearest native responsive Grid, then matching-axis Scroll, then scope.
        for (var ancestor = declarations[origin.Identity.Id]; ancestor is not null;
             ancestor = ancestor.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (ancestor.Identity.Scope != activeScope) break;
            var node = ancestor.Node;
            var horizontal = direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right;
            if (!(node.Kind == ViewNodeKind.Grid && node.GridMinimumColumnWidth is not null) &&
                !(node.Kind == ViewNodeKind.Scroll && (node.ScrollAxis == ScrollAxis.Horizontal) == horizontal)) continue;
            if (Choose(ancestor.Node.Id) is { } target) { Commit(target); return true; }
            foreach (var (owner, viewport) in scrollOwners.Where(pair => pair.Owner.Identity.Id == node.Id))
            {
                var wasScrolling = ReferenceEquals(directionalScroll?.Viewport, viewport);
                if (TryDirectionalScroll(owner, viewport, origin, direction, isRepeat)) return true;
                // The retained control may now be far outside the viewport. At
                // this boundary rank outer targets from the container, otherwise
                // a fixed header can incorrectly appear below an offscreen anchor.
                if (wasScrolling && NavigationBounds(viewport) is { } boundary) originBounds = boundary;
            }
        }
        if (deferredExit is not null) { Commit(deferredExit); return true; }
        if (Choose(null) is { } outside) { Commit(outside); return true; }
        return false;

        Binding? Choose(string? owner)
        {
            var selected = DirectionalFocusPolicy.Choose(originBounds, policyDirection.Value, candidates
                .Where(pair => owner is null || IsDescendant(pair.Key, owner))
                .Select(pair => new FocusCandidate(pair.Key, pair.Value.Bounds)));
            return selected is null ? null : candidates[selected].Target;
        }
        void Commit(Binding target)
        {
            if (!bindings.TryGetValue(target.Identity.Id, out var current) || !ReferenceEquals(current, target) ||
                !NavigationTarget(target)) return;
            indexed?.CancelHostNavigation();
            directionalScroll = null;
            if (FocusBinding(target)) target.Element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
    }

    private bool NavigationTarget(Binding binding) => Navigable(binding) && binding.Element.IsLoaded &&
        binding.Element.XamlRoot == XamlRoot && (binding.Element is WidgetIndexedCollectionView ||
            binding.Element is Control { IsEnabled: true, IsTabStop: true });

    private FocusRectangle? NavigationBounds(FrameworkElement element)
    {
        if (element.XamlRoot != XamlRoot || Bounds(element) is not { } bounds) return null;
        var rect = new FocusRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return rect.IsUsable ? rect : null;
    }
}
