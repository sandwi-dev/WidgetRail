using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // The renderer already knows its native container. Keep that exact ownership
    // beside the existing slot subscription instead of searching the visual tree
    // or treating data arrival as proof that the row template has been arranged.
    internal bool ReportRowLayout(SelectorItem container, WidgetIndexedRowView renderer, WidgetIndexedRow row)
    {
        if (disposed || !containers.TryGetValue(container, out var entry) || !ReferenceEquals(entry.Slot.Value, row)) return false;
        containers[container] = (entry.Slot, entry.Changed, renderer);
        if (renderer.HasCurrentLayout(row, container)) NavigationSettled?.Invoke();
        return true;
    }

    internal void ForgetRowLayout(SelectorItem container, WidgetIndexedRowView renderer)
    {
        if (containers.TryGetValue(container, out var entry) && ReferenceEquals(entry.LayoutOwner, renderer))
            containers[container] = (entry.Slot, entry.Changed, null);
    }

    private bool HasCurrentRowLayout(FrameworkElement container, WidgetIndexedRow row) =>
        container is SelectorItem item && containers.TryGetValue(item, out var entry) &&
        ReferenceEquals(entry.Slot.Value, row) && entry.LayoutOwner?.HasCurrentLayout(row, item) == true;
}
