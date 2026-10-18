using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Live content viewport geometry, including native padding and reserved
/// host decoration. Reads the current template without caching or changing layout.</summary>
internal static class WidgetScrollViewport
{
    internal static Rect ContentBounds(ScrollViewer scroll)
    {
        if (FindPresenter(scroll) is { ActualWidth: > 0, ActualHeight: > 0 } content)
        {
            var local = PresentationLayers.LocalClip(content) ?? new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            return content.TransformToVisual(scroll).TransformBounds(local);
        }
        // Before template realization, retain the existing native extent fallback.
        return new(0, 0, Math.Max(0, scroll.ViewportWidth), Math.Max(0, scroll.ViewportHeight));
    }

    internal static ScrollContentPresenter? FindPresenter(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollContentPresenter presenter) return presenter;
            if (child is ScrollViewer) continue;
            if (FindPresenter(child) is { } found) return found;
        }
        return null;
    }
}
