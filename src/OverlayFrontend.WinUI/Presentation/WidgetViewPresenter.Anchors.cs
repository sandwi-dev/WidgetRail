using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private AnchorRectangle? ResolvePresentationAnchor(PresentationAnchor? anchor, FrameworkElement relativeTo)
    {
        if (anchor is null) return null;
        FrameworkElement? target = null;
        if (anchor.ElementId is { } id && bindings.TryGetValue(id, out var ordinary)) target = ordinary.LayoutElement;
        else if (anchor.IndexedItem is { } indexed && bindings.TryGetValue(indexed.CollectionId, out var collection) &&
            collection.Element is WidgetIndexedCollectionView view) target = view.ResolveAnchor(indexed);
        return VisibleAnchorBounds(target, relativeTo);
    }

    internal static AnchorRectangle? VisibleAnchorBounds(FrameworkElement? target, FrameworkElement relativeTo)
    {
        if (target?.XamlRoot is null || !ReferenceEquals(target.XamlRoot, relativeTo.XamlRoot) ||
            target.ActualWidth <= 0 || target.ActualHeight <= 0) return null;
        var bounds = target.TransformToVisual(relativeTo).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        for (DependencyObject? parent = target; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement { Visibility: Visibility.Collapsed }) return null;
            if (parent is UIElement element && PresentationLayers.LocalClip(element) is { } clip)
            {
                var viewport = element.TransformToVisual(relativeTo).TransformBounds(clip);
                bounds.Intersect(viewport);
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
            }
        }
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
