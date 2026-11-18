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
    private double? navigationRequestExtent;
    private bool navigationQueued;
    private bool navigationNeedsScroll;
    private bool viewportRevealQueued;
    private bool scrollOnly;
    private double? revealPendingOffset;
    private bool revealRequested;

    private void NativeViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var horizontal = axis == ScrollAxis.Horizontal;
        var previous = horizontal ? args.PreviousSize.Width : args.PreviousSize.Height;
        var next = horizontal ? args.NewSize.Width : args.NewSize.Height;
        if (next >= previous - 0.5 || viewportRevealQueued || disposed || pendingIndex is not null || FocusedIndex() is not { } index) return;
        var capturedView = view;
        var capturedSource = source;
        viewportRevealQueued = true;
        // Status chrome and window resizing can shrink a native viewport while
        // its focused row keeps the same identity and offset. Reconcile once
        // after layout, without moving focus or starting another navigation.
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            viewportRevealQueued = false;
            if (disposed || !CanReceiveInput || pendingIndex is not null || !ReferenceEquals(view, capturedView) ||
                !ReferenceEquals(source, capturedSource) || FocusedIndex() != index ||
                view?.ContainerFromIndex(index) is not FrameworkElement item || FindNativeScroll(view) is not { } scroll) return;
            var bounds = item.TransformToVisual(scroll).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight));
            var visible = WidgetScrollViewport.ContentBounds(scroll);
            var extent = horizontal ? visible.Width : visible.Height;
            if (extent <= 0) return;
            var start = horizontal ? bounds.X - visible.X : bounds.Y - visible.Y;
            var end = horizontal ? bounds.Right - visible.X : bounds.Bottom - visible.Y;
            var size = horizontal ? bounds.Width : bounds.Height;
            var delta = size > extent || start < 0 ? start : end > extent ? end - extent : 0;
            if (Math.Abs(delta) <= 0.5) return;
            var offset = Math.Clamp((horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset) + delta, 0,
                horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight);
            ObserveFocusedViewportReveal();
            scroll.ChangeView(horizontal ? offset : null, horizontal ? null : offset, null, disableAnimation: true);
        })) viewportRevealQueued = false;
    }
    partial void ObserveFocusedViewportReveal();
    private FocusNavigationDirection pendingDirection;
    private IndexedCollectionFocusTarget? pendingEntry;
    private bool entering;
    private bool allowEntryFallback;
    private IndexedItemsSource<WidgetIndexedRow>.Retention? navigationRetention;
    private sealed record EntryIntent(int Index, IndexedCollectionFocusTarget? Target, bool AllowFallback);
    private EntryIntent? CaptureEntry() => entering && pendingIndex is { } index ? new(index, pendingEntry, allowEntryFallback) : null;
    private EntryIntent? RebaseEntry(EntryIntent? entry,WidgetPresentationBinding binding,ViewNode next)
    {
        // Only an active exact Enter may outrank older remembered focus. A
        // passive Reveal is not captured here and can never acquire focus.
        if(entry?.Target is not {} target || source is null || !source.Presentation.SameSurface(binding) ||
            source.ActiveScope!=binding.Scope || source.Declaration.Id!=next.Id || !MatchesQuery(target) ||
            entry.Index!=target.Index || source.Declaration is not {IndexedCollection:{} previous,IndexedOccurrenceKeys:{} oldKeys} ||
            next is not {IndexedCollection:{} query,IndexedOccurrenceKeys:{} keys} || query.QueryGeneration<=previous.QueryGeneration ||
            !IndexedCollectionContract.SameViewportDomain(query,previous) || target.Index>=oldKeys.Count || oldKeys[target.Index]!=target.ItemKey)return null;
        var index=keys.ToList().IndexOf(target.ItemKey);
        return index<0?null:entry with{Index=index,Target=target with{QueryGeneration=query.QueryGeneration,Index=index}};
    }
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
    internal bool IsEntryPending => entering || pendingContinuity?.Focus is not null;
    internal bool IsScrollRevealPending => scrollOnly && pendingIndex is not null;
    internal bool IsFocusParked => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is { } focused &&
        (ReferenceEquals(focused, this) || ReferenceEquals(focused, view));
    private void ParkFocusForQueryReplacement()
    {
        if (view?.XamlRoot is null || FocusedIndex() is null && !IsFocusParked) return;
        // Keep native focus in the logical collection, but outside ListView while
        // its ItemsSource is reset. Otherwise ListView schedules its own first-item
        // focus after realization, overriding a later authored deep-row entry.
        ParkCollectionFocus();
    }
    private bool ParkCollectionFocus()
    {
        IsTabStop = true;
        try { return Focus(FocusState.Programmatic); }
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

    internal Control? RetainedFocusTarget(IndexedCollectionFocusTarget target, bool requireVisible = false) =>
        MatchesQuery(target) && source!.Items[target.Index] is IndexedItem<WidgetIndexedRow> { Key: { } key } &&
        key == target.ItemKey && view?.ContainerFromIndex(target.Index) is Control { IsLoaded: true, IsEnabled: true } control &&
        (!requireVisible || ReadyTargetVisible(control))
            ? control : null;

    private bool ReadyTargetVisible(Control target)
    {
        if (view is null || FindNativeScroll(view) is not { ViewportWidth: > 0, ViewportHeight: > 0 } scroll || target.ActualWidth <= 0 || target.ActualHeight <= 0) return false;
        var visible = WidgetScrollViewport.ContentBounds(scroll);
        var bounds = LayoutBoundsInScroll(target, scroll);
        return bounds.Left >= visible.Left - .5 && bounds.Top >= visible.Top - .5 && bounds.Right <= visible.Right + .5 && bounds.Bottom <= visible.Bottom + .5;
    }

    internal bool Enter(IndexedCollectionFocusTarget? target = null, bool allowFallback = false)
    {
        CancelContinuity();
        if (disposed || source is null || view is null || !CanReceiveInput) return false;
        if (source.Items.Count == 0 && source.Declaration.IndexedCollection?.Discovery is not
            { HasMore: true, Status: DiscoveredCollectionStatus.Ready or DiscoveredCollectionStatus.Loading }) return false;
        if (target is not null && !MatchesQuery(target)) return false;
        return BeginEntry(target?.Index??0,target,allowFallback||target is null);
    }
    internal bool EnterVisibleEdge(FocusNavigationDirection direction,FrameworkElement? origin)
    {
        if(disposed || source is null || view?.IsLoaded!=true || origin is null || origin.XamlRoot!=XamlRoot || !CanReceiveInput || layoutKind!=CollectionLayoutKind.List ||
            FindNativeScroll(view) is not {ViewportWidth:>0,ViewportHeight:>0} scroll)return false;
        var candidates=VisibleOccurrences(scroll,includeLoading:true).Where(item=>!item.Slot.Failed &&
            item.Slot.Value is not {Lease.IsCurrent:true,Item.Root.IsDisabled:true}).ToArray();
        if(candidates.Length==0)return false;
        var policy=direction switch{FocusNavigationDirection.Up=>FocusDirection.Up,FocusNavigationDirection.Down=>FocusDirection.Down,
            FocusNavigationDirection.Left=>FocusDirection.Left,FocusNavigationDirection.Right=>FocusDirection.Right,_=>(FocusDirection?)null};
        if(policy is null)return false;
        // Rank only the displayed slices through the same directional geometry
        // policy as ordinary controls. Offscreen remembered rows are not input
        // candidates; their return/navigation memory remains available separately.
        var from=origin.TransformToVisual(scroll).TransformBounds(new(0,0,origin.ActualWidth,origin.ActualHeight));
        var visible=WidgetScrollViewport.ContentBounds(scroll);
        var ranked=candidates.Select(item=>
        {
            var clipped=item.Bounds;clipped.Intersect(visible);
            return new FocusCandidate(item.Slot.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),new(clipped.X,clipped.Y,clipped.Width,clipped.Height));
        });
        var chosen=DirectionalFocusPolicy.Choose(new(from.X,from.Y,from.Width,from.Height),policy.Value,ranked);
        if(chosen is null)return false;
        var slot=candidates.First(item=>item.Slot.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)==chosen).Slot;
        var query=source.Declaration.IndexedCollection!;
        IndexedCollectionFocusTarget? target=slot.Key is {} key?new(source.Declaration.Id,query.SourceId,query.QueryGeneration,key,slot.Index):null;
        CancelContinuity();
        return BeginEntry(slot.Index,target,false);
    }
    private bool BeginEntry(int index,IndexedCollectionFocusTarget? target,bool allowFallback)
    {
        if(disposed || source is null || view is null || !CanReceiveInput)return false;
        CancelNavigation();
        entering = true;
        allowEntryFallback = allowFallback;
        pendingEntry = target;
        pendingIndex = index;
        // Reuse the same admission/container checks as delayed navigation. A
        // current, already-visible row needs neither ScrollIntoView nor another
        // dispatcher/layout round trip. Retained stale pixels never take this path.
        if (target is not null && RetainedFocusTarget(target, requireVisible: true) is not null &&
            source.Items[target.Index] is IndexedItem<WidgetIndexedRow> { Value: { Lease.IsCurrent: true } })
        {
            CompleteNavigation();
            if (pendingIndex is null) return true;
        }
        QueueNavigation();
        return true;
    }

    internal bool Reveal(IndexedCollectionFocusTarget target)
    {
        CancelContinuity();
        // An explicit or restoring focus handoff wins over a passive scroll.
        if (disposed || source is null || view is null || !CanReceiveInput || pendingIndex is not null || !MatchesQuery(target)) return false;
        scrollOnly = true;
        pendingEntry = target;
        pendingIndex = target.Index;
        QueueNavigation();
        return true;
    }

    internal void CancelReveal()
    {
        if (scrollOnly) CancelNavigation();
    }

    internal bool MatchesQuery(IndexedCollectionFocusTarget target) => source?.Declaration is { IndexedCollection: { } query } node &&
        target.CollectionId == node.Id && target.SourceId == query.SourceId && target.QueryGeneration == query.QueryGeneration &&
        target.Index >= 0 && target.Index < query.Count;

    internal void CancelEntry()
    {
        if (entering) CancelNavigation();
    }
    internal void CancelHostNavigation(bool preserveReader = false) { CancelInputContinuity(preserveReader); CancelNavigation(); }

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
        if (CaptureFocusedItem() is not { } item) return;
        if (heldViewport is { } recovery) recovery.Focus = item;
        if (!transferringContinuityFocus && pendingContinuity is { } continuity && continuity.OwnedFocusKey != item.ItemKey)
        {
            TraceContinuity("focus-departure", continuity.Viewport);
            CancelContinuity();
        }
        FocusRemembered?.Invoke(item);
    }

    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        CancelInputContinuity(true);
        CancelReveal();
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
            if (target < 0 || target >= view.Items.Count)
                return RequestAdjacentBoundary(target<0?IndexedCollectionBoundary.Before:IndexedCollectionBoundary.After) || pendingIndex is not null;
        }
        pendingIndex = target;
        RetainHeldNavigationTarget(target);
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
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                navigationQueued = false;
                if (pendingIndex is not { } index || view is null || index >= view.Items.Count) return;
                if (scrollOnly)
                {
                    if (!CanReceiveInput || pendingEntry is not { } target || !MatchesQuery(target) ||
                        source?.Items[index] is not IndexedItem<WidgetIndexedRow> slot || slot.Failed)
                    { CancelNavigation(); return; }
                    // Resolve the key before changing scroll position. An index alone
                    // is not authority to reveal a replaced or unrelated occurrence.
                    if (slot.Value is not { Lease.IsCurrent: true }) return;
                    if (slot.Key != target.ItemKey) { CancelNavigation(); return; }
                }
                // One native request for the newest logical target. A run of
                // controller frames never creates a backlog of layout work.
                if (navigationNeedsScroll)
                {
                    // ScrollIntoView before the native panel's first arrange can
                    // be ignored (notably by horizontal lists). Keep the one
                    // logical intent pending until actual viewport readiness.
                    if (!view.IsLoaded || view.ItemsPanelRoot is null ||
                        FindNativeScroll(view) is not { ViewportWidth: > 0, ViewportHeight: > 0 }) return;
                    navigationNeedsScroll = false;
                    view.ScrollIntoView(view.Items[index], ScrollIntoViewAlignment.Default);
                    navigationRequestExtent=NavigationExtent();
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
    private double? NavigationExtent()=>!disposed && view is not null && FindNativeScroll(view) is {} scroll
        ?axis==ScrollAxis.Horizontal?scroll.ScrollableWidth:scroll.ScrollableHeight:null;
    private void FinishNavigation(object? sender, object args)
    {
        if(disposed || pendingIndex is null || view is null)return;
        // Real rows can displace a loading-estimate target out of realization.
        // Reissue that same retained target only when native geometry changed,
        // never on an unchanged frame or by accumulating future navigation.
        var extent=NavigationExtent();
        var retarget=pendingIndex is {} index && view?.ContainerFromIndex(index) is not FrameworkElement{IsLoaded:true} &&
            extent is {} current && navigationRequestExtent is {} previous &&
            Math.Abs(current-previous)>1/(view?.XamlRoot?.RasterizationScale??1);
        if(retarget)TraceNavigationRealization(pendingIndex!.Value,navigationRequestExtent!.Value,extent!.Value);
        QueueNavigation(scrollIntoView:retarget);
    }
    partial void TraceNavigationRealization(int index,double previousExtent,double currentExtent);

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
        // An exact disabled occurrence is already inadmissible. Do not wait for
        // WinUI to realize a disabled offscreen container before retiring entry.
        if (entering && !allowEntryFallback && row.Item.Root.IsDisabled == true)
        { CancelNavigation(); return; }
        if (entering || scrollOnly)
        {
            if (pendingEntry is { } queryTarget && !MatchesQuery(queryTarget)) { CancelNavigation(); return; }
            // An index is a location, not identity. Never silently focus another
            // occurrence when a stale/incorrect key was supplied by an author.
            if (pendingEntry is { } entry && slot.Key != entry.ItemKey) { CancelNavigation(); return; }
        }
        if (view.ContainerFromIndex(index) is not Control { IsLoaded: true } target) return;
        // Payload publication, native recycling, and container policy have distinct
        // callbacks. A current row lease alone does not mean that this container
        // has finished adopting that row's enabled state (especially on resume).
        if (target is not Microsoft.UI.Xaml.Controls.Primitives.SelectorItem selector ||
            !containers.TryGetValue(selector, out var bound) || !ReferenceEquals(bound.Slot, slot) ||
            target.IsEnabled != (row.Item.Root.IsDisabled != true)) return;
        if (scrollOnly || target.IsEnabled)
        {
            // Focus and passive reveal must use the same current row geometry.
            // Committing focus against a loading estimate can leave its real
            // container far offscreen once long rows finish arranging.
            if (!HasCurrentRowLayout(target, row) || FindNativeScroll(view) is not { } scroll) return;
            var bounds = LayoutBoundsInScroll(target, scroll);
            var visible = WidgetScrollViewport.ContentBounds(scroll);
            var horizontal = axis == ScrollAxis.Horizontal;
            var itemStart = horizontal ? bounds.Left : bounds.Top;
            var itemEnd = horizontal ? bounds.Right : bounds.Bottom;
            var viewportStart = horizontal ? visible.Left : visible.Top;
            var viewportEnd = horizontal ? visible.Right : visible.Bottom;
            var inset = horizontal ? view.Padding.Left + view.Padding.Right + target.Margin.Left + target.Margin.Right :
                view.Padding.Top + view.Padding.Bottom + target.Margin.Top + target.Margin.Bottom;
            var tolerance = 1 / (view.XamlRoot?.RasterizationScale ?? 1);
            var overlap = Math.Max(0, Math.Min(itemEnd, viewportEnd) - Math.Max(itemStart, viewportStart));
            var required = Math.Min(itemEnd - itemStart, Math.Max(1, viewportEnd - viewportStart - inset));
            if (scrollOnly && source.Declaration.IndexedCollection?.EndFollowingEpoch is not null && index == source.Items.Count - 1)
            {
                if (!RestoreFollowingEnd(slot, ref revealRequested, ref revealPendingOffset)) return;
            }
            else if (overlap + tolerance < required)
            {
                // Native ScrollIntoView may have used the loading estimate.
                // Reconcile the same occurrence through existing viewport memory
                // once its real row has arranged; native layout/ViewChanged owns
                // completion, rather than a dispatcher retry loop.
                var query = source.Declaration.IndexedCollection!;
                var desiredLeading = itemStart < viewportStart || itemEnd - itemStart >= viewportEnd - viewportStart
                    ? 0 : viewportEnd - viewportStart - (itemEnd - itemStart);
                var memory = new IndexedViewportMemento(new(source.ActiveScope, source.Declaration.Id, ViewNodeKind.IndexedCollection, ""),
                    query.SourceId, query.QueryGeneration, layoutKind!.Value, axis!.Value, index, slot.Key!, 0, desiredLeading);
                if (!RestoreViewportGeometry(memory,target,scroll,ref revealPendingOffset)) return;
            }
            if(scrollOnly){CancelNavigation();return;}
        }
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
        navigationRequestExtent=null;
        revealPendingOffset = null;
        revealRequested = false;
        var wasPending = pendingIndex is not null;
        ReleaseNavigationRetention();
        CancelPendingActivation();
        pendingIndex = null;
        pendingEntry = null;
        entering = false;
        scrollOnly = false;
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
        extendAfterNavigation = null;
        CancelInputContinuity();
        CancelNavigation();
    }
    partial void TraceFocusDeparture(LosingFocusEventArgs args);
}
