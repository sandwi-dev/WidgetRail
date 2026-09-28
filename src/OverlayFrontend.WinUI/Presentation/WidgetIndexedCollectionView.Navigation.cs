using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private int? pendingIndex;
    private bool navigationQueued;
    private FocusNavigationDirection pendingDirection;
    private IndexedCollectionFocusTarget? pendingEntry;
    private bool entering;
    private bool allowEntryFallback;
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
    internal Action<IndexedCollectionFocusTarget>? FocusRemembered { get; set; }

    internal bool Enter(IndexedCollectionFocusTarget? target = null, bool allowFallback = false)
    {
        if (disposed || source is null || view is null || !CanReceiveInput || source.Items.Count == 0) return false;
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

    private void RememberItemFocus()
    {
        if (source is null || FocusedIndex() is not { } index ||
            source.Items[index] is not Collections.IndexedItem<Collections.WidgetIndexedRow> { Key: { } key }) return;
        var query = source.Declaration.IndexedCollection!;
        FocusRemembered?.Invoke(new(source.Declaration.Id, query.SourceId, query.QueryGeneration, key, index));
    }

    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        CancelPendingActivation();
        if (view is null || source is null || !CanReceiveInput || view.Items.Count == 0) return false;
        CancelEntry();
        var current = pendingIndex ?? FocusedIndex();
        if (current is null) return false;
        var grid = view.ItemsPanelRoot as ItemsWrapGrid;
        var columns = grid is null || grid.ItemWidth <= 0 || double.IsNaN(grid.ItemWidth) ? 1 :
            Math.Max(1, (int)Math.Round(view.ActualWidth / grid.ItemWidth));
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
            if (target < 0 || target >= view.Items.Count) return pendingIndex is not null;
        }
        pendingIndex = target;
        pendingDirection = direction;
        QueueNavigation();
        return true;
    }

    private void QueueNavigation()
    {
        if (view is null) return;
        view.LayoutUpdated -= FinishNavigation;
        view.LayoutUpdated += FinishNavigation;
        if (!navigationQueued)
        {
            navigationQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                navigationQueued = false;
                if (pendingIndex is not { } index || view is null) return;
                // One native request for the newest logical target. A run of
                // controller frames never creates a backlog of layout work.
                view.ScrollIntoView(view.Items[index], ScrollIntoViewAlignment.Default);
                FinishNavigation(null, null!);
            });
        }
    }

    private int? FocusedIndex()
    {
        for (var focused = view?.XamlRoot is null ? null : FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
             focused is not null && !ReferenceEquals(focused, view); focused = VisualTreeHelper.GetParent(focused))
            if (focused is SelectorItem item && view!.IndexFromContainer(item) is var index && index >= 0) return index;
        return null;
    }

    private void FinishNavigation(object? sender, object args)
    {
        if (pendingIndex is not { } index || view is null || disposed) return;
        if (entering)
        {
            if (pendingEntry is { } queryTarget && !MatchesQuery(queryTarget)) { CancelNavigation(); return; }
            if (source!.Items[index] is not Collections.IndexedItem<Collections.WidgetIndexedRow> slot || slot.Key is null) return;
            // An index is a location, not identity. Never silently focus another
            // occurrence when a stale/incorrect key was supplied by an author.
            if (pendingEntry is { } entry && slot.Key != entry.ItemKey) { CancelNavigation(); return; }
        }
        if (view.ContainerFromIndex(index) is not Control { IsLoaded: true } target) return;
        if (!target.IsEnabled)
        {
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
        else if (target.Focus(FocusState.Keyboard)) CancelNavigation();
    }

    private void CancelNavigation()
    {
        CancelPendingActivation();
        pendingIndex = null;
        pendingEntry = null;
        entering = false;
        if (view is not null) view.LayoutUpdated -= FinishNavigation;
    }

    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        for (var target = args.NewFocusedElement as DependencyObject; target is not null; target = VisualTreeHelper.GetParent(target))
            if (ReferenceEquals(target, view)) return;
        CancelNavigation();
    }
}
