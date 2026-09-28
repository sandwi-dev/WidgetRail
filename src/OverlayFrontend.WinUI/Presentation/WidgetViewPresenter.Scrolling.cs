using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
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
        CancelGroupEntry();
        ScrollViewer? target = null;
        for (var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; current is not null && !ReferenceEquals(current, this);
             current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer viewer && CanScroll(viewer)) { target = viewer; break; }
        target ??= bindings.Values.Where(binding => binding.Identity.Scope == frame.Authority.ActiveInputScopeId &&
                binding.Element.Visibility == Visibility.Visible)
            .Select(binding => binding.Element is ScrollViewer viewer ? viewer :
                binding.Element is WidgetIndexedCollectionView collection ? FindScroll(collection.NativeView) : null)
            .FirstOrDefault(viewer => viewer is not null && CanScroll(viewer));
        if (target is null) return false;
        target.ChangeView(Math.Clamp(target.HorizontalOffset + horizontalDelta, 0, target.ScrollableWidth),
            Math.Clamp(target.VerticalOffset + verticalDelta, 0, target.ScrollableHeight), null, disableAnimation: true);
        return true;

        bool CanScroll(ScrollViewer viewer) => viewer.IsLoaded && viewer.Visibility == Visibility.Visible &&
            (horizontalDelta != 0 && viewer.HorizontalScrollMode != ScrollMode.Disabled && viewer.ScrollableWidth > 0 ||
             verticalDelta != 0 && viewer.VerticalScrollMode != ScrollMode.Disabled && viewer.ScrollableHeight > 0);
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            if (FindScroll(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }
}
