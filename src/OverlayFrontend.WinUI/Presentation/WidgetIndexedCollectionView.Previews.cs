using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal ImageSource? BorrowPreviewArtwork(WidgetIndexedRow row, FrameworkElement anchor, string handle) =>
        !disposed && row.Lease.IsCurrent && anchor is SelectorItem container &&
        containers.TryGetValue(container, out var entry) && ReferenceEquals(entry.Slot.Value, row) &&
        IsContextRowCurrent(row, container) ? entry.LayoutOwner?.BorrowDecodedArtwork(row, container, handle) : null;

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
    // A partial page can be replenished without changing this admitted prefix.
    // The preview owns its original semantic lease; native row actions continue
    // using the replacement page's current binding.
    internal bool CanRetainPrefixPreview(WidgetIndexedRow row, FrameworkElement anchor)
    {
        if (disposed || !CanReceiveInput || !ReferenceEquals(source, row.Owner) || !row.Lease.IsCurrent ||
            source!.Declaration.IndexedCollection is not { RetainsAppendedPrefix: true } query ||
            !IndexedCollectionContract.RetainsPrefix(query, row.Lease.Range.Source) ||
            anchor is not SelectorItem { IsLoaded: true } container || !containers.TryGetValue(container, out var entry) ||
            entry.Slot.Failed || entry.Slot.Key != row.Item.Key || entry.Slot.Value?.Lease.IsCurrent != true) return false;
        var relative = entry.Slot.Index - row.Lease.Range.StartIndex;
        return relative >= 0 && relative < row.Lease.Range.Items.Count && row.Lease.Range.Items[relative].Key == row.Item.Key;
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
