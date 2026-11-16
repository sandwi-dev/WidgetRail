using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // Resident native occurrence only. Hover must never fetch, scroll, select or retain a stale row.
    internal (WidgetIndexedRow Row, FrameworkElement Anchor)? CapturePreviewRow(DependencyObject? target)
    {
        if (disposed || !CanReceiveInput || source is null) return null;
        for (var current = target; current is not null && !ReferenceEquals(current, this);
             current = VisualTreeHelper.GetParent(current))
            if (current is SelectorItem container && containers.TryGetValue(container, out var entry) &&
                !entry.Slot.Failed && entry.Slot.Value is { Lease.IsCurrent: true } row && IsContextRowCurrent(row, container))
                return (row, container);
        return null;
    }
    // Paint continuity only: this never authorizes an action from the old lease.
    internal bool IsPreviewContentRefreshing(WidgetIndexedRow previous, FrameworkElement anchor) =>
        !disposed && CanReceiveInput && ReferenceEquals(source, previous.Owner) &&
        source!.Declaration.IndexedCollection is { } next && next.SourceId == previous.Lease.Range.Source.SourceId &&
        next.QueryGeneration == previous.Lease.Range.Source.QueryGeneration &&
        (next.ContentRevision > previous.Lease.Range.Source.ContentRevision || source.HasPendingPresentation) &&
        anchor is SelectorItem { IsLoaded: true } container && containers.TryGetValue(container, out var entry) &&
        !entry.Slot.Failed && ReferenceEquals(entry.Slot.Value, previous) && entry.Slot.Key == previous.Item.Key;

}
