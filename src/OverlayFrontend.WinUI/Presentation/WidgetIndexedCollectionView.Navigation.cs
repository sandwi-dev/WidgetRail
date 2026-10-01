using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using System.ComponentModel;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private int? pendingIndex;
    private bool navigationQueued;
    private bool navigationNeedsScroll;
    private FocusNavigationDirection pendingDirection;
    private IndexedCollectionFocusTarget? pendingEntry;
    private bool entering;
    private bool allowEntryFallback;
    private IndexedItemsSource<WidgetIndexedRow>.Retention? navigationRetention;
    private sealed record EntryIntent(int Index, IndexedCollectionFocusTarget? Target, bool AllowFallback);
    private EntryIntent? CaptureEntry() => entering && pendingIndex is { } index ? new(index, pendingEntry, allowEntryFallback) : null;
    private void RestoreEntry(EntryIntent? entry)
    {
        if (entry is null || entering || view is null || source is null || !CanReceiveInput || entry.Index >= source.Items.Count) return;
        if (entry.Target is not null && !MatchesQuery(entry.Target)) return;
        entering = true;
        pendingEntry = entry.Target;
        pendingIndex = entry.Index;
        allowEntryFallback = entry.AllowFallback;
        QueueNavigation();
    }
    internal bool IsEntryPending => entering;
    internal bool IsFocusParked => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is { } focused &&
        (ReferenceEquals(focused, this) || ReferenceEquals(focused, view));
    private void ParkFocusForQueryReplacement()
    {
        if (view?.XamlRoot is null || FocusedIndex() is null && !IsFocusParked) return;
        // Keep native focus in the logical collection, but outside ListView while
        // its ItemsSource is reset. Otherwise ListView schedules its own first-item
        // focus after realization, overriding a later authored deep-row entry.
        IsTabStop = true;
        try { Focus(FocusState.Programmatic); }
        finally { IsTabStop = false; }
    }
    internal event Action? NavigationSettled;
    internal FrameworkElement? FocusedNavigationElement => FocusedIndex() is { } index
        ? view?.ContainerFromIndex(index) as FrameworkElement : null;
    internal FocusNeighbors? FocusedRowNavigation => FocusedIndex() is { } index && source is not null &&
        index >= 0 && index < source.Items.Count &&
        source.Items[index] is IndexedItem<WidgetIndexedRow> { Value: { Lease.IsCurrent: true } row }
            ? row.Item.Root.Focus : null;
    internal Action<IndexedCollectionFocusTarget>? FocusRemembered { get; set; }

    internal Control? RetainedFocusTarget(IndexedCollectionFocusTarget target) =>
        MatchesQuery(target) && source!.Items[target.Index] is IndexedItem<WidgetIndexedRow> { Key: { } key } &&
        key == target.ItemKey && view?.ContainerFromIndex(target.Index) is Control { IsLoaded: true, IsEnabled: true } control
            ? control : null;

    internal bool Enter(IndexedCollectionFocusTarget? target = null, bool allowFallback = false)
    {
        if (disposed || source is null || view is null || !CanReceiveInput) return false;
        if (source.Items.Count == 0 && source.Declaration.IndexedCollection?.Discovery is not
            { HasMore: true, Status: DiscoveredCollectionStatus.Ready or DiscoveredCollectionStatus.Loading }) return false;
        if (target is not null && !MatchesQuery(target)) return false;
        CancelNavigation();
        entering = true;
        allowEntryFallback = allowFallback || target is null;
        pendingEntry = target;
        pendingIndex = target?.Index ?? 0;
        QueueNavigation();
        return true;
    }

    private bool MatchesQuery(IndexedCollectionFocusTarget target) => source?.Declaration is { IndexedCollection: { } query } node &&
        target.CollectionId == node.Id && target.SourceId == query.SourceId && target.QueryGeneration == query.QueryGeneration &&
        target.Index >= 0 && target.Index < query.Count;

    internal void CancelEntry()
    {
        if (entering) CancelNavigation();
    }
    internal void CancelHostNavigation() => CancelNavigation();

    internal IEnumerable<Control> ScrollFocusCandidates() => containers
        .Where(pair => pair.Value.Slot.Value?.Lease.IsCurrent == true && !pair.Value.Slot.Failed)
        .Select(pair => (Control)pair.Key);

    internal IndexedCollectionFocusTarget? CaptureFocusedItem()
    {
        if (source is null || FocusedIndex() is not { } index ||
            source.Items[index] is not IndexedItem<WidgetIndexedRow> { Value: not null, Key: { } key }) return null;
        var query = source.Declaration.IndexedCollection!;
        return new(source.Declaration.Id, query.SourceId, query.QueryGeneration, key, index);
    }

    private void RememberItemFocus()
    {
        if (CaptureFocusedItem() is { } item) FocusRemembered?.Invoke(item);
    }

    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        CancelPendingActivation();
        if (view is null || source is null || !CanReceiveInput || view.Items.Count == 0) return false;
        CancelEntry();
        var current = pendingIndex ?? FocusedIndex();
        if (current is null) return false;
        var grid = view.ItemsPanelRoot as ItemsWrapGrid;
        var columns = grid is null ? 1 : Math.Max(1, grid.MaximumRowsOrColumns);
        int target;
        if (groups is not null)
        {
            if (GroupedTarget(current.Value, direction, columns) is not { } groupedTarget)
                return pendingIndex is not null && direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down;
            target = groupedTarget;
        }
        else
        {
            var delta = direction switch
            {
                FocusNavigationDirection.Up when axis != ScrollAxis.Horizontal => -columns,
                FocusNavigationDirection.Down when axis != ScrollAxis.Horizontal => columns,
                FocusNavigationDirection.Left when axis == ScrollAxis.Horizontal || grid is not null && current % columns > 0 => -1,
                FocusNavigationDirection.Right when axis == ScrollAxis.Horizontal || grid is not null && current % columns < columns - 1 => 1,
                _ => 0,
            };
            if (delta == 0) return false;
            target = current.Value + delta;
            if (target >= view.Items.Count && delta > 1 && current.Value / columns < (view.Items.Count - 1) / columns) target = view.Items.Count - 1;
            if (target >= view.Items.Count && !source.ContinuationPaused && !source.ContinuationFailed && source.Declaration.IndexedCollection?.Discovery is
                { HasMore: true, Status: DiscoveredCollectionStatus.Ready or DiscoveredCollectionStatus.Loading })
            {
                // The discovered tail is not a navigation exit. Consume this
                // direction without accumulating a target beyond available data;
                // reversal uses the current row immediately and page arrival
                // never replays old movement. Failure/limit expose their footer.
                if (source.Items.HasMoreItems) _ = source.ContinueAsync();
                return true;
            }
            if (target < 0 || target >= view.Items.Count) return pendingIndex is not null;
        }
        pendingIndex = target;
        pendingDirection = direction;
        QueueNavigation();
        return true;
    }

    private void QueueNavigation(bool scrollIntoView = true)
    {
        if (view is null || pendingIndex is null || disposed) return;
        navigationNeedsScroll |= scrollIntoView;
        if (source is not null && pendingIndex is { } target && target >= 0 && target < source.Items.Count &&
            navigationRetention?.Slot.Index != target)
        {
            ReleaseNavigationRetention();
            navigationRetention = source.Items.Retain(target);
            navigationRetention.Slot.PropertyChanged += NavigationDataChanged;
        }
        view.LayoutUpdated -= FinishNavigation;
        view.LayoutUpdated += FinishNavigation;
        if (!navigationQueued)
        {
            navigationQueued = true;
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                navigationQueued = false;
                if (pendingIndex is not { } index || view is null || index >= view.Items.Count) return;
                // One native request for the newest logical target. A run of
                // controller frames never creates a backlog of layout work.
                if (navigationNeedsScroll)
                {
                    navigationNeedsScroll = false;
                    view.ScrollIntoView(view.Items[index], ScrollIntoViewAlignment.Default);
                    QueueNavigation(scrollIntoView: false);
                    return;
                }
                CompleteNavigation();
            })) { navigationQueued = false; CancelNavigation(); }
        }
    }

    private void NavigationDataChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Placeholder geometry may have put the target outside realization after
        // its actual row extent arrives. Reissue the current logical target once
        // on payload readiness, never on a paint/frame timer or unrelated rows.
        if (args.PropertyName is nameof(IndexedItem<WidgetIndexedRow>.Content) or nameof(IndexedItem<WidgetIndexedRow>.Failed)) QueueNavigation();
    }

    private void ReleaseNavigationRetention()
    {
        if (navigationRetention is not { } retention) return;
        navigationRetention = null;
        retention.Slot.PropertyChanged -= NavigationDataChanged;
        retention.Dispose();
    }

    private int? FocusedIndex()
    {
        for (var focused = view?.XamlRoot is null ? null : FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
             focused is not null && !ReferenceEquals(focused, view); focused = VisualTreeHelper.GetParent(focused))
            if (focused is SelectorItem item && view!.IndexFromContainer(item) is var index && index >= 0) return index;
        return null;
    }

    // Realization/layout notifications report readiness. Focus is committed only
    // after those native callbacks and ScrollIntoView have unwound: WinUI may
    // still choose its own reset target inside the realization transaction.
    private void FinishNavigation(object? sender, object args) => QueueNavigation(scrollIntoView: false);

    private void CompleteNavigation()
    {
        if (pendingIndex is not { } index || view is null || disposed || !CanReceiveInput) return;
        if (index >= view.Items.Count)
        {
            if (source?.Declaration.IndexedCollection?.Discovery is not
                { HasMore: true, Status: DiscoveredCollectionStatus.Ready or DiscoveredCollectionStatus.Loading }) CancelNavigation();
            return;
        }
        if (source?.Items[index] is not Collections.IndexedItem<Collections.WidgetIndexedRow> slot || slot.Failed)
        { CancelNavigation(); return; }
        // Retained pixels can describe the prior content revision (for example
        // disabled Visible-state rows while Interactive-state rows are loading).
        // They are not current navigation availability. Wait for the requested
        // row lease rather than skipping every old disabled item to the far end.
        if (slot.Value is not { } row || !row.Lease.IsCurrent) return;
        if (entering)
        {
            if (pendingEntry is { } queryTarget && !MatchesQuery(queryTarget)) { CancelNavigation(); return; }
            // An index is a location, not identity. Never silently focus another
            // occurrence when a stale/incorrect key was supplied by an author.
            if (pendingEntry is { } entry && slot.Key != entry.ItemKey) { CancelNavigation(); return; }
        }
        if (view.ContainerFromIndex(index) is not Control { IsLoaded: true } target) return;
        if (!target.IsEnabled)
        {
            TraceNavigation("disabled-target", index, row, target);
            if (entering && allowEntryFallback)
            {
                var next = pendingEntry is not null ? 0 : index + 1;
                pendingEntry = null;
                if (next >= view.Items.Count) { CancelNavigation(); return; }
                pendingIndex = next;
                QueueNavigation();
            }
            else if (entering || !MoveFocus(pendingDirection) || pendingIndex == index) CancelNavigation();
        }
        else if (target.Focus(FocusState.Keyboard))
        {
            TraceNavigation("focused-target", index, row, target);
            CancelNavigation();
        }
    }

    partial void TraceNavigation(string phase, int index, WidgetIndexedRow row, Control target);

    private void CancelNavigation()
    {
        var wasPending = pendingIndex is not null;
        ReleaseNavigationRetention();
        CancelPendingActivation();
        pendingIndex = null;
        pendingEntry = null;
        entering = false;
        navigationNeedsScroll = false;
        if (view is not null) view.LayoutUpdated -= FinishNavigation;
        if (wasPending) NavigationSettled?.Invoke();
    }

    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        TraceFocusDeparture(args);
        for (var target = args.NewFocusedElement as DependencyObject; target is not null; target = VisualTreeHelper.GetParent(target))
            if (ReferenceEquals(target, view)) return;
        // WinUI can temporarily focus a placeholder before its unavailable state
        // is applied. Its automatic fallback is not a user cancellation of an
        // authored entry. Preserve only that unavailable-control, same-root case;
        // actual navigation, pointer input, hide and explicit departure still cancel.
        if (entering && pendingIndex is not null && CanReceiveInput && args.Direction == FocusNavigationDirection.None &&
            args.InputDevice == FocusInputDeviceKind.Keyboard && args.OldFocusedElement is Control { IsEnabled: false } &&
            args.NewFocusedElement is FrameworkElement replacement && ReferenceEquals(replacement.XamlRoot, view?.XamlRoot)) return;
        CancelNavigation();
    }
    partial void TraceFocusDeparture(LosingFocusEventArgs args);
}
