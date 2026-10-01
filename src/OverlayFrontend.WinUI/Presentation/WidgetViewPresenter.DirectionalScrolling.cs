using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // Unlike right-stick scrolling, this gesture may legitimately have no visible
    // focus target. Keep its anchor without running free-scroll focus settling.
    private sealed record DirectionalScroll(ScrollViewer Viewport, Binding Owner, Binding Anchor,
        DependencyObject NativeAnchor, string Scope, FocusNavigationDirection Direction, double Destination, bool Pending = true);
    private DirectionalScroll? currentDirectionalScroll;
    private DirectionalScroll? directionalScroll
    {
        get => currentDirectionalScroll;
        set
        {
            if (!ReferenceEquals(currentDirectionalScroll?.Viewport, value?.Viewport))
            {
                if (currentDirectionalScroll is { } old) old.Viewport.ViewChanged -= DirectionalScrollViewChanged;
                if (value is { } next) next.Viewport.ViewChanged += DirectionalScrollViewChanged;
            }
            currentDirectionalScroll = value;
        }
    }

    private void DirectionalScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        // Once native movement settles, subsequent input starts from its actual
        // position (including wheel/touch changes), not our old requested offset.
        if (!args.IsIntermediate && directionalScroll is { } gesture && ReferenceEquals(sender, gesture.Viewport))
            currentDirectionalScroll = gesture with { Pending = false };
    }

    private static bool Horizontal(FocusNavigationDirection direction) =>
        direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right;
    private static int ScrollSign(FocusNavigationDirection direction) =>
        direction is FocusNavigationDirection.Up or FocusNavigationDirection.Left ? -1 : 1;

    private IEnumerable<(Binding Owner, ScrollViewer Viewport)> DirectionalScrollOwners(Binding origin, FocusNavigationDirection direction)
    {
        for (var declaration = declarations[origin.Identity.Id]; declaration is not null;
             declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (declaration.Identity.Scope != activeScope) yield break;
            var owner = bindings[declaration.Node.Id];
            var viewport = owner.Element is ScrollViewer scroll ? scroll :
                owner.Element is WidgetIndexedCollectionView indexed ? FindScroll(indexed.NativeView) : null;
            if (viewport is null || !viewport.IsLoaded || viewport.Visibility != Visibility.Visible) continue;
            if (Horizontal(direction) ? viewport.HorizontalScrollMode == ScrollMode.Disabled :
                viewport.VerticalScrollMode == ScrollMode.Disabled) continue;
            yield return (owner, viewport);
        }
    }

    private static double ScrollOffset(ScrollViewer viewport, FocusNavigationDirection direction) =>
        Horizontal(direction) ? viewport.HorizontalOffset : viewport.VerticalOffset;
    private static double ScrollLimit(ScrollViewer viewport, FocusNavigationDirection direction) =>
        Horizontal(direction) ? viewport.ScrollableWidth : viewport.ScrollableHeight;
    private static bool HasScrollRoom(ScrollViewer viewport, FocusNavigationDirection direction) =>
        ScrollSign(direction) < 0 ? ScrollOffset(viewport, direction) > .5 :
        ScrollLimit(viewport, direction) - ScrollOffset(viewport, direction) > .5;

    private void ValidateDirectionalScroll(Binding origin)
    {
        if (directionalScroll is not { } gesture) return;
        if (gesture.Scope != activeScope || !ReferenceEquals(gesture.Anchor, origin) ||
            !gesture.Viewport.IsLoaded || !bindings.TryGetValue(gesture.Owner.Identity.Id, out var owner) ||
            !ReferenceEquals(owner, gesture.Owner) ||
            !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), gesture.NativeAnchor)) directionalScroll = null;
    }

    private bool ReverseDirectionalScroll(Binding origin, FocusNavigationDirection direction, bool isRepeat)
    {
        ValidateDirectionalScroll(origin);
        if (directionalScroll is not { } gesture || Horizontal(direction) != Horizontal(gesture.Direction) ||
            gesture.NativeAnchor is not FrameworkElement anchor) return false;
        var rect = anchor.TransformToVisual(gesture.Viewport).TransformBounds(new(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        var towardAnchor = direction switch
        {
            FocusNavigationDirection.Up => rect.Bottom <= .5,
            FocusNavigationDirection.Down => rect.Y >= gesture.Viewport.ViewportHeight - .5,
            FocusNavigationDirection.Left => rect.Right <= .5,
            FocusNavigationDirection.Right => rect.X >= gesture.Viewport.ViewportWidth - .5,
            _ => false,
        };
        if (!towardAnchor) return false;
        // Bring the remembered control back through ordinary scrolling before
        // resuming spatial navigation; do not jump to a different offscreen item.
        return TryDirectionalScroll(gesture.Owner, gesture.Viewport, origin, direction, isRepeat);
    }

    private bool TryDirectionalScroll(Binding owner, ScrollViewer viewport, Binding origin,
        FocusNavigationDirection direction, bool isRepeat)
    {
        var previous = directionalScroll;
        var continuing = previous is not null && ReferenceEquals(previous.Viewport, viewport) && previous.Direction == direction;
        var offset = ScrollOffset(viewport, direction);
        var limit = ScrollLimit(viewport, direction);
        var sign = ScrollSign(direction);
        // ChangeView is asynchronous. Accumulate from the latest requested offset,
        // not a stale native offset, and never exit while the final step is pending.
        var requested = continuing && previous!.Pending ? Math.Clamp(previous.Destination, 0, limit) : offset;
        var pending = continuing && Math.Abs(requested - offset) > .5;
        if (!HasScrollRoom(viewport, direction) && !pending)
        {
            if (continuing && isRepeat) return true;
            // Keep the boundary anchor until focus actually leaves. A pinned or
            // modal owner may decline the exit; held repeats must still stop here.
            return false;
        }
        var extent = Horizontal(direction) ? viewport.ViewportWidth : viewport.ViewportHeight;
        var basis = continuing ? (sign < 0 ? Math.Min(offset, requested) : Math.Max(offset, requested)) : offset;
        var destination = Math.Clamp(basis + sign * Math.Min(64, extent * .25), 0, limit);
        if (Math.Abs(destination - requested) <= .5 && pending) return true;
        if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject anchor) return true;
        scrollGesture = null;
        if (owner.Element is WidgetIndexedCollectionView indexed) indexed.CancelHostNavigation();
        // WinUI retargets its own scroll animation; reduced motion uses immediate
        // steps. There is no application animation timer or queued input backlog.
        directionalScroll = new(viewport, owner, origin, anchor, activeScope, direction, destination);
        if (!viewport.ChangeView(Horizontal(direction) ? destination : null,
            Horizontal(direction) ? null : destination, null,
            disableAnimation: Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced))
            directionalScroll = previous;
        return true;
    }
}
