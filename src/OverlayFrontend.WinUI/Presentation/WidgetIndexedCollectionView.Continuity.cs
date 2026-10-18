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
        internal bool Requested;
        internal double? Offset;
        internal IDisposable? ViewportRetention;
        internal IndexedItemsSource<WidgetIndexedRow>.Retention? FocusRetention;
    }
    private Continuity? pendingContinuity;
    private bool continuityQueued;

    private Continuity? CaptureContinuity(WidgetPresentationBinding binding, ViewNode next)
    {
        if (source is null || !source.Presentation.SameSurface(binding) || source.ActiveScope != binding.Scope ||
            source.Declaration.Id != next.Id || source.Declaration.IndexedCollection is not { } previous ||
            next.IndexedCollection is not { } query || previous.SourceId != query.SourceId || query.QueryGeneration <= previous.QueryGeneration ||
            source.Declaration.IndexedOccurrenceKeys is not { } oldKeys || next.IndexedOccurrenceKeys is not { } keys) return null;
        var oldViewport = pendingContinuity?.Viewport ?? CaptureViewport(new(binding.Scope, next.Id, ViewNodeKind.IndexedCollection, ""), preferFocused: true);
        var oldFocus = pendingContinuity?.Focus ?? CaptureFocusedItem();
        IndexedViewportMemento? viewport = null;
        IndexedCollectionFocusTarget? focus = null;
        if (oldViewport is not null && IndexedCollectionContract.RebaseOccurrence(oldKeys, keys, oldViewport.Key) is var anchor && anchor >= 0)
            viewport = oldViewport with { Query = query.QueryGeneration, Index = anchor, Key = keys[anchor] };
        if (oldFocus is not null && IndexedCollectionContract.RebaseOccurrence(oldKeys, keys, oldFocus.ItemKey) is var index && index >= 0)
            focus = oldFocus with { QueryGeneration = query.QueryGeneration, Index = index, ItemKey = keys[index] };
        TraceContinuity("capture", viewport);
        return new(viewport, focus);
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
        if (source is null || view is null || pending.Viewport is null && pending.Focus is null) return;
        pendingContinuity = pending;
        if (pending.Viewport is { } viewport) pending.ViewportRetention = RetainViewport(viewport, QueueContinuity);
        if (pending.Focus is { } focus)
        {
            pending.FocusRetention = source.Items.Retain(focus.Index);
            FocusRemembered?.Invoke(focus);
        }
        source.Items.StateChanged += ContinuityDataChanged;
        view.LayoutUpdated += ContinuityLayoutChanged;
        QueueContinuity();
    }

    private void ContinuityDataChanged(object? sender, EventArgs args) => QueueContinuity();
    private void ContinuityLayoutChanged(object? sender, object args) => QueueContinuity();
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
        if (pending.Focus is { } focus)
        {
            var slot = pending.FocusRetention?.Slot;
            if (slot is null || slot.Failed || slot.Key is { } key && key != focus.ItemKey)
            { pending.Focus = null; }
            else if (slot.Value is { Lease.IsCurrent: true } && view.ContainerFromIndex(focus.Index) is Control { IsLoaded: true, IsEnabled: true } control)
            {
                // Only the previous focused collection may restore focus. A later
                // pointer/key/controller departure cancels this intent entirely.
                if (IsFocusParked || FocusedIndex() is not null) control.Focus(FocusState.Keyboard);
                pending.Focus = null;
            }
            else if (pending.Viewport is null)
                view.ScrollIntoView(source.Items[focus.Index], ScrollIntoViewAlignment.Default);
        }
        var viewportReady = pending.Viewport is null || RestoreViewport(pending.Viewport, ref pending.Requested, ref pending.Offset, allowRetainedPixels: true);
        // A new query can have no surviving anchor. Finish deterministically rather
        // than keeping a restoration subscription after failure or empty data.
        if (viewportReady && pending.Focus is null) CancelContinuity();
    }

    partial void TraceContinuity(string phase, IndexedViewportMemento? viewport);

    private void CancelContinuity()
    {
        if (pendingContinuity is not { } pending) return;
        TraceContinuity("cancel", pending.Viewport);
        pendingContinuity = null;
        if (source is not null) source.Items.StateChanged -= ContinuityDataChanged;
        if (view is not null) view.LayoutUpdated -= ContinuityLayoutChanged;
        pending.ViewportRetention?.Dispose();
        pending.FocusRetention?.Dispose();
    }
}
