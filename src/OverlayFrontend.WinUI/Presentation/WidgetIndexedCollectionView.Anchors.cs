using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal FrameworkElement? ResolveAnchor(IndexedCollectionFocusTarget target)
    {
        if (disposed || !presentationActive || !MatchesQuery(target) || source is null || view is null ||
            view.ContainerFromIndex(target.Index) is not SelectorItem container || !containers.TryGetValue(container, out var data)) return null;
        var item = data.Slot;
        if (item.Index != target.Index || item.Value?.Lease.IsCurrent != true || item.Failed || item.Key != target.ItemKey) return null;
        // Inspect resident metadata only: an anchor never fetches, materializes or scrolls an occurrence.
        return container;
    }
}
