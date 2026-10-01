using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record ScrollGesture(ScrollViewer Viewport, Binding Owner, string Scope);
    private ScrollGesture? scrollGesture;

    /// <summary>
    /// Applies one normalized DIP movement to the active native scroll owner.
    /// The input owner supplies cadence and sensitivity; WinUI owns extent,
    /// virtualization and painting. No queued animation or focus move is added.
    /// </summary>
    internal bool ScrollBy(double horizontalDelta, double verticalDelta)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Scroll on the widget dispatcher.");
        if (!presentationActive || disposed || applying || !IsLoaded || frame is null || !double.IsFinite(horizontalDelta) || !double.IsFinite(verticalDelta)) return false;
        if (HasTransientControl) return true;
        directionalScroll = null;
        CancelGroupEntry();
        ScrollViewer? target = null;
        for (var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; current is not null && !ReferenceEquals(current, this);
             current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer viewer && CanScroll(viewer)) { target = viewer; break; }
        target ??= bindings.Values.Where(binding => binding.Identity.Scope == activeScope &&
                binding.Element.Visibility == Visibility.Visible)
            .Select(binding => binding.Element is ScrollViewer viewer ? viewer :
                binding.Element is WidgetIndexedCollectionView collection ? FindScroll(collection.NativeView) : null)
            .FirstOrDefault(viewer => viewer is not null && CanScroll(viewer));
        if (target is null) return false;
        var owner = FindBinding(target);
        if (owner is null) return false;
        scrollGesture = new(target, owner, activeScope);
        if (owner.Element is WidgetIndexedCollectionView indexed) indexed.CancelHostNavigation();
        target.ChangeView(Math.Clamp(target.HorizontalOffset + horizontalDelta, 0, target.ScrollableWidth),
            Math.Clamp(target.VerticalOffset + verticalDelta, 0, target.ScrollableHeight), null, disableAnimation: true);
        return true;

        bool CanScroll(ScrollViewer viewer) => viewer.IsLoaded && viewer.Visibility == Visibility.Visible &&
            (horizontalDelta != 0 && viewer.HorizontalScrollMode != ScrollMode.Disabled && viewer.ScrollableWidth > 0 ||
             verticalDelta != 0 && viewer.VerticalScrollMode != ScrollMode.Disabled && viewer.ScrollableHeight > 0);
    }

    /// <summary>
    /// Rejoin directional navigation after free scrolling. Only native realized
    /// controls in the same declaration owner and active input scope participate;
    /// an unloaded page never causes a jump back to the old logical index.
    /// Returns false while a visible page has no ready focus target yet.
    /// </summary>
    internal bool SettleScrollFocus()
    {
        if (scrollGesture is not { } gesture) return true;
        if (!automaticFocusEnabled || !presentationActive || !presentationInputEnabled || disposed || applying || HasTransientControl ||
            activeScope != gesture.Scope || !gesture.Viewport.IsLoaded ||
            !bindings.TryGetValue(gesture.Owner.Identity.Id, out var owner) || !ReferenceEquals(owner, gesture.Owner))
        { scrollGesture = null; return true; }
        var scroll = gesture.Viewport;
        var width = scroll.ViewportWidth;
        var height = scroll.ViewportHeight;
        if (width <= 0 || height <= 0) return false;
        var controls = owner.Element is WidgetIndexedCollectionView collection
            ? collection.ScrollFocusCandidates()
            : bindings.Values.Where(binding => Navigable(binding) && binding.Element is Control &&
                IsDescendant(binding.Identity.Id, owner.Identity.Id))
                .Select(binding => (Control)binding.Element);
        var candidates = controls.Where(control => control.IsLoaded && control.IsEnabled &&
                control.Visibility == Visibility.Visible && NearestScroll(control) == scroll)
            .Select(control => (Control: control, Bounds: control.TransformToVisual(scroll).TransformBounds(
                new Rect(0, 0, control.ActualWidth, control.ActualHeight))))
            .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0 &&
                item.Bounds.Right > .5 && item.Bounds.Bottom > .5 && item.Bounds.X < width - .5 && item.Bounds.Y < height - .5)
            .ToArray();
        // Preserve focus if any of its pixels are still in the viewport, matching
        // the native host. Otherwise prefer a fully visible row at the leading edge.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (candidates.Any(item => IsWithin(focused, item.Control))) { scrollGesture = null; return true; }
        var horizontal = scroll.HorizontalScrollMode != ScrollMode.Disabled && scroll.ScrollableWidth > 0 &&
            (scroll.VerticalScrollMode == ScrollMode.Disabled || scroll.ScrollableHeight <= 0);
        var target = candidates.OrderByDescending(item => item.Bounds.X >= -.5 && item.Bounds.Y >= -.5 &&
                item.Bounds.Right <= width + .5 && item.Bounds.Bottom <= height + .5)
            .ThenBy(item => horizontal ? item.Bounds.X : item.Bounds.Y)
            .ThenBy(item => horizontal ? item.Bounds.Y : item.Bounds.X).FirstOrDefault().Control;
        if (target is null) return false;
        // An oversized row may only partially fit. Focus must not undo the user's
        // scroll offset through WinUI's automatic BringIntoView request.
        scroll.BringIntoViewRequested += SuppressBringIntoView;
        try
        {
            if (!target.Focus(FocusState.Keyboard)) return false;
            scrollGesture = null;
            return true;
        }
        finally { scroll.BringIntoViewRequested -= SuppressBringIntoView; }

        static void SuppressBringIntoView(UIElement sender, BringIntoViewRequestedEventArgs args) => args.Handled = true;
        static bool IsWithin(DependencyObject? child, DependencyObject ancestor)
        {
            for (; child is not null; child = VisualTreeHelper.GetParent(child))
                if (ReferenceEquals(child, ancestor)) return true;
            return false;
        }
    }

    private static ScrollViewer? NearestScroll(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer scroll) return scroll;
        return null;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            if (FindScroll(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }
}
