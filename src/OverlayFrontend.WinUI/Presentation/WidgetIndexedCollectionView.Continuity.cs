using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // At most one pending update and two demanded rows. Pixels can survive, but
    // leases/actions always belong to the exact new query and are reacquired.
    private sealed class Continuity(IndexedViewportMemento? viewport, IndexedCollectionFocusTarget? focus)
    {
        internal IndexedViewportMemento? Viewport = viewport;
        internal IndexedCollectionFocusTarget? Focus = focus;
        internal readonly string? OwnedFocusKey = focus?.ItemKey;
        internal bool Requested;
        internal bool AwaitingFocusLayout;
        internal bool NeedsLookup;
        internal CancellationTokenSource? Lookup;
        internal double? Offset;
        internal IDisposable? ViewportRetention;
        internal IndexedItemsSource<WidgetIndexedRow>.Retention? FocusRetention;
        internal IndexedItemsSource<WidgetIndexedRow>.Retention? EndRetention;
        internal bool FollowEnd;
        internal ScrollViewer? EndScroll;
        internal bool NearestOnMissing;
        internal bool FocusKeyPending;
        internal bool ViewportKeyPending;
    }
    private Continuity? pendingContinuity;
    private bool continuityQueued;
    private bool transferringContinuityFocus;

    private Continuity? CaptureContinuity(WidgetPresentationBinding binding, ViewNode next, bool allowEndFollowing)
    {
        if (source is null || !source.Presentation.SameSurface(binding) || source.ActiveScope != binding.Scope ||
            source.Declaration.Id != next.Id || source.Declaration.IndexedCollection is not { } previous ||
            next.IndexedCollection is not { } query || previous.SourceId != query.SourceId || query.QueryGeneration <= previous.QueryGeneration) return null;
        var followEnd = allowEndFollowing && !HasReadingOwner && previous.EndFollowingEpoch is { } epoch && query.EndFollowingEpoch == epoch &&
            query.EndFollowingPreviousQuery is { } forwardFrom && forwardFrom <= previous.QueryGeneration && previous.Count > 0 && next.IndexedOccurrenceKeys is not null &&
            presentationActive && CanReceiveInput && view?.IsLoaded == true &&
            (pendingContinuity?.FollowEnd == true || FindNativeScroll(view) is { } scroll && (axis == ScrollAxis.Horizontal
                ? scroll.ViewportWidth > 0 && scroll.ScrollableWidth - scroll.HorizontalOffset <= 1
                : scroll.ViewportHeight > 0 && scroll.ScrollableHeight - scroll.VerticalOffset <= 1));
        if (!followEnd && next.IndexedCollection.CanLocateItems != true &&
            (source.Declaration.IndexedOccurrenceKeys is null || next.IndexedOccurrenceKeys is null)) return null;
        var oldViewport = pendingContinuity?.Viewport ?? CaptureViewport(new(binding.Scope, next.Id, ViewNodeKind.IndexedCollection, ""), preferFocused: true);
        var oldFocus = pendingContinuity?.Focus ?? CaptureFocusedItem();
        if (followEnd)
        {
            var followingKeys = next.IndexedOccurrenceKeys!;
            var followingFocusIndex = oldFocus is null ? -1 : followingKeys.ToList().IndexOf(oldFocus.ItemKey);
            if (oldFocus is not null && followingFocusIndex < 0) followingFocusIndex = followingKeys.Count - 1;
            return new(null, followingFocusIndex < 0 ? null : oldFocus! with { QueryGeneration = query.QueryGeneration, Index = followingFocusIndex }) { FollowEnd = true };
        }
        if (source.Declaration.IndexedOccurrenceKeys is not { } oldKeys || next.IndexedOccurrenceKeys is not { } keys)
            return new(oldViewport, oldFocus) { NeedsLookup = true,NearestOnMissing=RelatedRefresh(binding,next) };
        IndexedViewportMemento? viewport = null;
        IndexedCollectionFocusTarget? focus = null;
        if (oldViewport is not null && IndexedCollectionContract.RebaseOccurrence(oldKeys, keys, oldViewport.Key) is var anchor && anchor >= 0)
            viewport = oldViewport with { Query = query.QueryGeneration, Index = anchor, Key = keys[anchor] };
        if (oldFocus is not null && IndexedCollectionContract.RebaseOccurrence(oldKeys, keys, oldFocus.ItemKey) is var index && index >= 0)
            focus = oldFocus with { QueryGeneration = query.QueryGeneration, Index = index, ItemKey = keys[index] };
        TraceContinuity("capture", viewport);
        return new(viewport, focus);
    }

    private bool RelatedRefresh(WidgetPresentationBinding binding,ViewNode next)=>source is not null &&
        source.Presentation.SameSurface(binding)&&source.Presentation.Frame.Authority.WorkerRun==binding.Frame.Authority.WorkerRun&&
        source.ActiveScope==binding.Scope&&source.Declaration.Id==next.Id&&source.Declaration.IndexedCollection is {} previous&&
        next.IndexedCollection is {RefreshFromQuery:{} from} query&&query.SourceId==previous.SourceId&&
        query.QueryGeneration>previous.QueryGeneration&&from<=previous.QueryGeneration;

    // Called only while the presenter proves that the entered preview still
    // owns native focus. This transfers focus provenance, never row/action
    // authority: the replacement must acquire its own current row lease.
    internal bool AdoptPreviewRefreshFocus(WidgetIndexedRow row,WidgetPresentationBinding binding,ViewNode next)
    {
        if(source is null||view is null||!RelatedRefresh(binding,next)||!CanReceiveInput||!Owns(row.Owner)||
            next.CollectionLayout?.Kind!=layoutKind||next.ScrollAxis!=axis||
            source!.Declaration.IndexedCollection is not {} previous||row.Lease.Range.Source.SourceId!=previous.SourceId||
            row.Lease.Range.Source.QueryGeneration!=previous.QueryGeneration||row.Lease.Range.Source.ContentRevision!=previous.ContentRevision||
            row.Lease.Range.ScopeId!=source.ActiveScope)return false;
        var offset=row.Lease.Range.Items.ToList().FindIndex(item=>ReferenceEquals(item,row.Item));
        var index=row.Lease.Range.StartIndex+offset;
        if(offset<0||index<0||index>=source.Items.Count||source.Items[index] is not IndexedItem<WidgetIndexedRow> slot||slot.Key!=row.Item.Key)return false;
        var viewport=CaptureViewport(new(binding.Scope,next.Id,ViewNodeKind.IndexedCollection,""));
        var focus=new IndexedCollectionFocusTarget(next.Id,previous.SourceId,previous.QueryGeneration,row.Item.Key,index);
        CancelContinuity();
        if(!ParkCollectionFocus())return false;
        pendingContinuity=new(viewport,focus);
        return true;
    }

    internal IndexedCollectionFocusTarget? RebaseRememberedFocus(IndexedCollectionFocusTarget target, ViewNode next)
    {
        if (source?.Declaration is not { IndexedCollection: { } prior, IndexedOccurrenceKeys: { } oldKeys } ||
            next.IndexedCollection is not { } query || next.IndexedOccurrenceKeys is not { } keys ||
            prior.SourceId != query.SourceId || prior.QueryGeneration != target.QueryGeneration || query.QueryGeneration <= prior.QueryGeneration)
            return null;
        var index = IndexedCollectionContract.RebaseOccurrence(oldKeys, keys, target.ItemKey);
        return index < 0 ? null : target with { QueryGeneration = query.QueryGeneration, Index = index, ItemKey = keys[index] };
    }

    private void BeginContinuity(Continuity pending)
    {
        if (source is null || view is null || pending.Viewport is null && pending.Focus is null && !pending.FollowEnd) return;
        pendingContinuity = pending;
        if (pending.NeedsLookup)
        {
            pending.Lookup = new();
            _ = LocateContinuityAsync(pending, source, pending.Lookup.Token);
            return;
        }
        if (pending.Viewport is { } viewport) pending.ViewportRetention = RetainViewport(viewport, QueueContinuity, () => pending.Offset = null);
        if (pending.Focus is { } focus)
        {
            pending.FocusRetention = source.Items.Retain(focus.Index);
            if(!pending.FocusKeyPending)FocusRemembered?.Invoke(focus);
        }
        if (pending.FollowEnd && source.Items.Count > 0)
        {
            pending.EndRetention = source.Items.Retain(source.Items.Count - 1);
            pending.EndScroll = FindNativeScroll(view);
            if (pending.EndScroll is not null) pending.EndScroll.ViewChanged += FollowingEndViewChanged;
        }
        source.Items.StateChanged += ContinuityDataChanged;
        view.LayoutUpdated += ContinuityLayoutChanged;
        QueueContinuity();
    }

    private async Task LocateContinuityAsync(Continuity pending, WidgetIndexedRows owner, CancellationToken token)
    {
        var viewport = pending.Viewport; var focus = pending.Focus;
        var query = owner.Declaration.IndexedCollection!;
        bool Current() => !disposed && presentationActive && ReferenceEquals(source, owner) && ReferenceEquals(pendingContinuity, pending) && !token.IsCancellationRequested;
        try
        {
            int? viewportIndex = viewport is null ? null : await owner.LocateAsync(viewport.Key, token);
            if (!Current()) return;
            int? focusIndex = focus is null ? null : focus.ItemKey == viewport?.Key ? viewportIndex : await owner.LocateAsync(focus.ItemKey, token);
            if (!Current()) return;
            pending.ViewportKeyPending=pending.NearestOnMissing&&viewport is not null&&viewportIndex is null&&query.Count>0;
            pending.FocusKeyPending=pending.NearestOnMissing&&focus is not null&&focusIndex is null&&query.Count>0;
            if(pending.ViewportKeyPending)viewportIndex=Math.Min(viewport!.Index,query.Count-1);
            if(pending.FocusKeyPending)focusIndex=Math.Min(focus!.Index,query.Count-1);
            pending.Viewport = viewport is not null && viewportIndex is { } anchor ? viewport with { Query = query.QueryGeneration, Index = anchor } : null;
            pending.Focus = focus is not null && focusIndex is { } target ? focus with { QueryGeneration = query.QueryGeneration, Index = target } : null;
            pending.NeedsLookup = false;
            pending.Lookup?.Dispose(); pending.Lookup = null;
            if (pending.Viewport is null && pending.Focus is null)
            {
                CancelContinuity();
                if (focus is not null && IsFocusParked && source!.Items.Count > 0) Enter();
                return;
            }
            BeginContinuity(pending);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !Current()) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (Current())
            {
                CancelContinuity();
                if (focus is not null && IsFocusParked && source!.Items.Count > 0) Enter();
                Diagnostics.FrontendFailureLog.Current.Write("indexed-anchor-lookup", error);
            }
        }
    }

    private void ContinuityDataChanged(object? sender, EventArgs args) => QueueContinuity();
    private void ContinuityLayoutChanged(object? sender, object args)
    {
        if (pendingContinuity is { AwaitingFocusLayout: true } pending)
        {
            pending.AwaitingFocusLayout = false;
            TraceContinuity("post-focus-layout", pending.Viewport);
        }
        QueueContinuity();
    }
    private void QueueContinuity()
    {
        if (pendingContinuity is null || continuityQueued || disposed) return;
        continuityQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RestoreContinuity)) continuityQueued = false;
    }
    private void RestoreContinuity()
    {
        continuityQueued = false;
        if (pendingContinuity is not { } pending || source is null || view is null) return;
        TraceContinuity("restore", pending.Viewport);
        if (disposed || !presentationActive || !CanReceiveInput) { CancelContinuity(); return; }
        if (pending.AwaitingFocusLayout) return;
        if(pending.ViewportKeyPending&&pending.Viewport is {} unresolvedViewport)
        {
            var slot=source.Items[unresolvedViewport.Index] as IndexedItem<WidgetIndexedRow>;
            if(slot is null||slot.Failed){pending.Viewport=null;pending.ViewportKeyPending=false;}
            else if(slot is {Key:{} key,Value.Lease.IsCurrent:true})
            {pending.Viewport=unresolvedViewport with{Key=key,ClippedFraction=0};pending.ViewportKeyPending=false;}
            else return;
        }
        if(pending.FocusKeyPending&&pending.Focus is {} unresolvedFocus)
        {
            var slot=pending.FocusRetention?.Slot;
            if(slot is null||slot.Failed){pending.Focus=null;pending.FocusKeyPending=false;}
            else if(slot is {Key:{} key,Value.Lease.IsCurrent:true})
            {var resolved=unresolvedFocus with{ItemKey=key};pending.Focus=resolved;pending.FocusKeyPending=false;FocusRemembered?.Invoke(resolved);}
            else return;
        }
        if (pending.Focus is { } focus)
        {
            var slot = pending.FocusRetention?.Slot;
            if (slot is null || slot.Failed || slot.Key is { } key && key != focus.ItemKey)
            { pending.Focus = null; }
            else if (slot.Value is { Lease.IsCurrent: true } && view.ContainerFromIndex(focus.Index) is Control { IsLoaded: true, IsEnabled: true } control)
            {
                // Only the previous focused collection may restore focus. A later
                // pointer/key/controller departure cancels this intent entirely.
                if (IsFocusParked || FocusedIndex() is not null)
                {
                    transferringContinuityFocus = true;
                    bool focused;
                    try { focused = control.Focus(FocusState.Keyboard); }
                    finally { transferringContinuityFocus = false; }
                    pending.Focus = null;
                    if (focused && pending.Viewport is not null)
                    {
                        // WinUI can queue its focus BringIntoView after Focus()
                        // returns. Keep this exact viewport checkpoint through
                        // native arrange before declaring its pixels settled.
                        pending.AwaitingFocusLayout = true;
                        view.InvalidateArrange();
                        return;
                    }
                }
                pending.Focus = null;
            }
            else if (pending.Viewport is null)
                view.ScrollIntoView(source.Items[focus.Index], ScrollIntoViewAlignment.Default);
        }
        var viewportReady = pending.FollowEnd ? RestoreFollowingEnd(pending) :
            pending.Viewport is null || RestoreViewport(pending.Viewport, ref pending.Requested, ref pending.Offset, allowRetainedPixels: true);
        // A new query can have no surviving anchor. Finish deterministically rather
        // than keeping a restoration subscription after failure or empty data.
        if (viewportReady && pending.Focus is null) { TraceContinuity("settled", pending.Viewport); CancelContinuity(); }
    }

    partial void TraceContinuity(string phase, IndexedViewportMemento? viewport);

    private bool RestoreFollowingEnd(Continuity pending) => pending.EndRetention?.Slot is not { } slot ||
        RestoreFollowingEnd(slot, ref pending.Requested, ref pending.Offset);

    private bool RestoreFollowingEnd(IndexedItem<WidgetIndexedRow> slot, ref bool requested, ref double? pendingOffset)
    {
        if (source is null || view is null || slot.Failed) return true;
        if (slot.Value is not { Lease.IsCurrent: true } row || !view.IsLoaded || FindNativeScroll(view) is not { } scroll) return false;
        if (view.ContainerFromIndex(slot.Index) is not FrameworkElement { IsLoaded: true } container)
        {
            if (!requested) { requested = true; view.ScrollIntoView(slot, ScrollIntoViewAlignment.Default); }
            return false;
        }
        if (!HasCurrentRowLayout(container, row)) return false;
        var horizontal = axis == ScrollAxis.Horizontal;
        var desired = horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight;
        var current = horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;
        if (Math.Abs(desired - current) <= 1 / (view.XamlRoot?.RasterizationScale ?? 1)) { pendingOffset = null; return true; }
        if (pendingOffset != desired)
        {
            pendingOffset = desired;
            if (!(horizontal ? scroll.ChangeView(desired, null, null, true) : scroll.ChangeView(null, desired, null, true))) pendingOffset = null;
        }
        return false;
    }
    private void FollowingEndViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => QueueContinuity();

    private void CancelContinuity()
    {
        if (pendingContinuity is not { } pending) return;
        TraceContinuity("cancel", pending.Viewport);
        pendingContinuity = null;
        pending.Lookup?.Cancel(); pending.Lookup?.Dispose(); pending.Lookup = null;
        if (source is not null) source.Items.StateChanged -= ContinuityDataChanged;
        if (view is not null) view.LayoutUpdated -= ContinuityLayoutChanged;
        pending.ViewportRetention?.Dispose();
        pending.FocusRetention?.Dispose();
        pending.EndRetention?.Dispose();
        if (pending.EndScroll is not null) pending.EndScroll.ViewChanged -= FollowingEndViewChanged;
    }
}
