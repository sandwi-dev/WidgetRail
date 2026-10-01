using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal Action? ContextChanged { get; set; }
    internal (WidgetIndexedRow Row, FrameworkElement Anchor)? CaptureContextRow()
    {
        if (disposed || !CanReceiveInput || source is null || FocusedIndex() is not { } index ||
            source.Items[index] is not IndexedItem<WidgetIndexedRow> { Value: { } row } || !row.Lease.IsCurrent ||
            view?.ContainerFromIndex(index) is not FrameworkElement { IsLoaded: true } anchor) return null;
        return (row, anchor);
    }
    internal bool IsContextRowCurrent(WidgetIndexedRow row, FrameworkElement anchor) =>
        !disposed && CanReceiveInput && ReferenceEquals(source, row.Owner) && row.Lease.IsCurrent &&
        anchor is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem { IsLoaded: true } container && containers.TryGetValue(container, out var slot) &&
        ReferenceEquals(slot.Slot.Value, row);
}
