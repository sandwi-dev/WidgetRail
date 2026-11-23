using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private sealed record ViewportWork(WidgetPresentationBinding Binding, ViewNode Declaration, IndexedViewportObservation Observation);
    private sealed class HeldViewport(WidgetPresentationBinding binding, ViewNode declaration, string scope,
        IndexedViewportMemento anchor, IndexedCollectionFocusTarget? focus)
    {
        internal WidgetPresentationBinding Binding = binding;
        internal ViewNode Declaration = declaration;
        internal string Scope = scope;
        internal IndexedViewportMemento Anchor = anchor;
        internal IndexedCollectionFocusTarget? Focus = focus;
        internal readonly string RequestedKey = anchor.Key;
        internal bool Scrolling;
        internal bool RequestCompleted;
        internal bool HasRecoveryPublication;
        internal string? ResolvedKey;
        internal readonly CancellationTokenSource Lifetime = new(TimeSpan.FromSeconds(35));
        internal readonly TaskCompletionSource Published = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly string viewportId = Guid.NewGuid().ToString("N");
    private long viewportSequence;
    private ScrollViewer? observedScroll;
    private ViewportWork? queuedViewportObservation;
    private ViewportWork? lastViewportObservation;
    private Task? viewportObservationPump;
    private HeldViewport? heldViewport;
    private bool applyingRecoveryFallback;
    private bool nativeViewportChanging;
    private bool IsViewportScrollPending => nativeViewportChanging || HasPendingViewportScroll?.Invoke(this) == true;

    // Parking retains the reader's focus ownership while an old row retires.
    // It is not a transition to passive end-following: doing that at a page
    // boundary can pull the viewport toward the tail during continued input.
    private bool HasReadingOwner => FocusedIndex() is not null || IsFocusParked || HasReadingPreview?.Invoke(this) == true;
    internal void RefreshReaderOwnership()
    {
        if (HasReadingOwner && pendingContinuity?.FollowEnd == true) CancelContinuity();
        QueueViewportObservation();
    }

    private bool ViewportAtEnd() => view?.IsLoaded == true && FindNativeScroll(view) is { } scroll &&
        (axis == ScrollAxis.Horizontal ? scroll.ViewportWidth > 0 && scroll.ScrollableWidth - scroll.HorizontalOffset <= 1 :
            scroll.ViewportHeight > 0 && scroll.ScrollableHeight - scroll.VerticalOffset <= 1);

    private void AttachViewportObservation()
    {
        var next = view is null ? null : FindNativeScroll(view);
        if (ReferenceEquals(next, observedScroll)) return;
        DetachViewportObservation(); observedScroll = next;
        if (next is not null) { next.ViewChanging += ObservedViewportChanging; next.ViewChanged += ObservedViewportChanged; }
        QueueViewportObservation();
    }
    private void DetachViewportObservation()
    {
        if (observedScroll is not null) { observedScroll.ViewChanging -= ObservedViewportChanging; observedScroll.ViewChanged -= ObservedViewportChanged; }
        observedScroll = null;
    }
    private void ObservedViewportChanging(object? sender, ScrollViewerViewChangingEventArgs args)
    { nativeViewportChanging = true; if (heldViewport is { } recovery) recovery.Scrolling = true; }
    private void ObservedViewportChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (scrollOnly && pendingIndex is not null) QueueNavigation(scrollIntoView: false);
        if (!args.IsIntermediate) { nativeViewportChanging = false; RefreshHeldViewportAfterScroll(); }
        QueueViewportObservation();
    }

    private void RefreshHeldViewportAfterScroll()
    {
        if (heldViewport is not { } recovery || source is null) return;
        recovery.Scrolling = false;
        if (CaptureViewport(new(recovery.Scope, recovery.Declaration.Id, ViewNodeKind.IndexedCollection, "")) is { } anchor)
            recovery.Anchor = anchor;
        AdoptRecoveredNeighbor(recovery);
        if (recovery.Declaration.IndexedOccurrenceKeys?.Contains(recovery.Anchor.Key) == true)
        {
            heldViewport = null;
            pendingContinuity = new(recovery.Anchor, recovery.Focus);
            recovery.Published.TrySetResult(); recovery.Lifetime.Cancel();
            Apply(recovery.Binding, recovery.Declaration, recovery.Scope);
        }
        else if (recovery.RequestCompleted && recovery.HasRecoveryPublication && recovery.Anchor.Key != recovery.RequestedKey)
            RestartHeldViewportRecovery(recovery);
    }

    private void RestartHeldViewportRecovery(HeldViewport previous)
    {
        if (!ReferenceEquals(heldViewport, previous) || previous.Scrolling) return;
        var next = new HeldViewport(previous.Binding, previous.Declaration, previous.Scope, previous.Anchor, previous.Focus);
        heldViewport = next;
        previous.Published.TrySetCanceled(); previous.Lifetime.Cancel();
        TrackRetirement(RecoverViewportAsync(next));
    }

    private static void AdoptRecoveredNeighbor(HeldViewport recovery)
    {
        if (recovery.Anchor.Key != recovery.RequestedKey || recovery.ResolvedKey is not { } resolved || resolved == recovery.RequestedKey ||
            recovery.Declaration.IndexedOccurrenceKeys?.Contains(resolved) != true) return;
        recovery.Anchor = recovery.Anchor with { Key = resolved, ClippedFraction = 0, LeadingPosition = Math.Max(0, recovery.Anchor.LeadingPosition ?? 0) };
        if (recovery.Focus?.ItemKey == recovery.RequestedKey) recovery.Focus = recovery.Focus with { ItemKey = resolved };
    }

    private void RetainHeldNavigationTarget(int index)
    {
        if (heldViewport is { } recovery && source?.Declaration is { IndexedCollection: { } query, IndexedOccurrenceKeys: { } keys } && index >= 0 && index < keys.Count)
            recovery.Focus = new(source.Declaration.Id, query.SourceId, query.QueryGeneration, keys[index], index);
    }

    private void QueueViewportObservation(bool inactive = false,IndexedCollectionBoundary? demandedBoundary=null)
    {
        var binding = heldViewport?.Binding ?? source?.Presentation;
        var declaration = heldViewport?.Declaration ?? source?.Declaration;
        if (binding is null || declaration?.IndexedCollection is not {} descriptor || !(descriptor.CanRecoverViewport || descriptor.ObservesBoundaries)) return;
        var active = !inactive && !disposed && presentationActive && inputActive && binding.IsCurrent && IsLoaded;
        var atEnd = active && heldViewport is null && !HasReadingOwner && (pendingContinuity?.FollowEnd == true || !IsViewportScrollPending && ViewportAtEnd());
        var boundary=active?demandedBoundary??CaptureBoundaryCrossing(descriptor,declaration):null;
        var physicalEnd=descriptor.ObservesBoundaries?(bool?)(active && heldViewport is null && !IsViewportScrollPending && ViewportAtEnd()):null;
        var physicalStart=descriptor.ObservesBoundaries?(bool?)(active && heldViewport is null && !IsViewportScrollPending && observedScroll is {} scroller &&
            (axis==ScrollAxis.Horizontal?scroller.ViewportWidth>0&&scroller.HorizontalOffset<=1:scroller.ViewportHeight>0&&scroller.VerticalOffset<=1)):null;
        if (lastViewportObservation is { } prior && prior.Binding.SameInput(binding) && prior.Declaration.IndexedCollection == declaration.IndexedCollection &&
            prior.Observation.Active == active && prior.Observation.AtEnd == atEnd && prior.Observation.PhysicalAtEnd==physicalEnd &&
            prior.Observation.PhysicalAtStart==physicalStart && boundary is null) return;
        if(active && boundary is null && queuedViewportObservation is {Observation.Boundary:{} queued} old && old.Declaration.IndexedCollection==descriptor)
            boundary=queued;
        var work = new ViewportWork(binding, declaration, new(viewportId, checked(++viewportSequence), active, atEnd)
            {Boundary=boundary,PhysicalAtEnd=physicalEnd,PhysicalAtStart=physicalStart,ProtectedKeys=boundary is null?null:BoundaryProtectedKeys()});
        if(boundary is {} direction)TraceBoundaryDemand(direction);
        lastViewportObservation = work;
        queuedViewportObservation = work;
        if (viewportObservationPump is null or { IsCompleted: true }) viewportObservationPump = PublishViewportObservationsAsync();
    }

    private async Task PublishViewportObservationsAsync()
    {
        while (queuedViewportObservation is { } work)
        {
            queuedViewportObservation = null;
            boundaryInFlight=work.Observation.Boundary;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var admission = WidgetIndexedRows.ProviderAdmission(session);
            var entered = false;
            try
            {
                await admission.WaitAsync(deadline.Token); entered = true;
                if (work.Observation.Active && !work.Binding.IsCurrent) continue;
                var authority = session.GetState(work.Binding.Frame.Authority.WidgetId)?.LastGood?.Authority ?? work.Binding.Frame.Authority;
                await session.ObserveIndexedViewportAsync(authority, work.Declaration.Id, work.Declaration.IndexedCollection!,
                    work.Observation, work.Binding.PinnedLayoutId, deadline.Token);
            }
            catch (Exception error) when (error is WidgetPresentationSessionException or OperationCanceledException or ArgumentException)
            {
                // A query/surface may retire while this coalesced observation is
                // in transit. Feedback grants no row authority and is never retried blindly.
                if (ReferenceEquals(lastViewportObservation, work)) lastViewportObservation = null;
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { Diagnostics.FrontendFailureLog.Current.Write("indexed-viewport-observation", error); }
            finally { boundaryInFlight=null;if (entered) admission.Release(); }
        }
    }

    private bool TryHoldViewportRecovery(WidgetPresentationBinding binding, ViewNode declaration, string scope)
    {
        if (applyingRecoveryFallback) return false;
        if (heldViewport is { } pending)
        {
            if (!pending.Binding.SameSurface(binding) || pending.Scope != scope ||
                !IndexedCollectionContract.SameViewportDomain(declaration.IndexedCollection, pending.Declaration.IndexedCollection!))
            { CancelViewportRecovery(false); return false; }
            pending.Binding = binding; pending.Declaration = declaration;
            pending.HasRecoveryPublication |= declaration.IndexedOccurrenceKeys?.Contains(pending.RequestedKey) == true;
            if (pending.Scrolling) return true;
            AdoptRecoveredNeighbor(pending);
            if (declaration.IndexedOccurrenceKeys?.Contains(pending.Anchor.Key) == true)
            {
                heldViewport = null;
                pendingContinuity = new(pending.Anchor, pending.Focus);
                pending.Published.TrySetResult();
                return false;
            }
            if (pending.RequestCompleted && pending.HasRecoveryPublication && pending.Anchor.Key != pending.RequestedKey)
                RestartHeldViewportRecovery(pending);
            return true;
        }
        if (source is null || view is null || !presentationActive || !inputActive || !source.Presentation.SameSurface(binding) ||
            source.ActiveScope != scope || source.Declaration.Id != declaration.Id || declaration.IndexedCollection is not { CanRecoverViewport: true } next ||
            source.Declaration.IndexedCollection is not { } previous || !IndexedCollectionContract.SameViewportDomain(next, previous) ||
            next.QueryGeneration <= previous.QueryGeneration || declaration.IndexedOccurrenceKeys is not { } keys ||
            ViewportAtEnd() && !HasReadingOwner && !IsViewportScrollPending && next.EndFollowingPreviousQuery is { } forwardFrom && forwardFrom <= previous.QueryGeneration) return false;
        var anchor = pendingContinuity?.Viewport ?? CaptureViewport(new(scope, declaration.Id, ViewNodeKind.IndexedCollection, ""), preferFocused: true);
        if (anchor is null || keys.Contains(anchor.Key)) return false;
        var recovery = new HeldViewport(binding, declaration, scope, anchor, pendingContinuity?.Focus ?? CaptureFocusedItem());
        CancelContinuity(); CancelNavigation(); CancelPendingActivation();
        heldViewport = recovery;
        // The existing owner retains its bounded visible paint while leases are
        // stale. No old row action, artwork or additional range gains authority.
        QueueViewportObservation();
        TrackRetirement(RecoverViewportAsync(recovery));
        return true;
    }

    private async Task RecoverViewportAsync(HeldViewport recovery)
    {
        var admission = WidgetIndexedRows.ProviderAdmission(session);
        var entered = false;
        try
        {
            await admission.WaitAsync(recovery.Lifetime.Token); entered = true;
            if (!recovery.Binding.IsCurrent) { CancelViewportRecovery(false); return; }
            // Like indexed reads/lookup, provider admission may wait past an
            // unrelated parent publication. Renew only the frame authority;
            // the session still validates this exact captured collection query.
            var frame=session.GetState(recovery.Binding.Frame.Authority.WidgetId)?.LastGood ?? recovery.Binding.Frame;
            if(!WidgetIndexedRows.SameOwner(recovery.Binding.Frame.Authority,frame.Authority))
            { CancelViewportRecovery(false); return; }
            var oldKeys = source?.Declaration.IndexedOccurrenceKeys ?? [];
            var oldIndex = oldKeys.ToList().IndexOf(recovery.Anchor.Key);
            string[] neighbors = oldIndex < 0 ? [] : oldKeys.Skip(oldIndex + 1).Concat(oldKeys.Take(oldIndex).Reverse()).ToArray();
            var result = await session.RecoverIndexedViewportResultAsync(frame.Authority, recovery.Declaration.Id,
                recovery.Declaration.IndexedCollection!, new(viewportId, checked(++viewportSequence), recovery.Anchor.Key) { NeighborKeys = neighbors },
                recovery.Binding.PinnedLayoutId, recovery.Lifetime.Token);
            var recovered = result.Recovered;
            recovery.ResolvedKey = result.AnchorKey;
            admission.Release(); entered = false;
            recovery.RequestCompleted = true;
            if (recovered && ReferenceEquals(heldViewport, recovery) && !recovery.Scrolling)
            {
                AdoptRecoveredNeighbor(recovery);
                if (recovery.Declaration.IndexedOccurrenceKeys?.Contains(recovery.Anchor.Key) == true)
                {
                    heldViewport = null;
                    pendingContinuity = new(recovery.Anchor, recovery.Focus);
                    recovery.Published.TrySetResult();
                    Apply(recovery.Binding, recovery.Declaration, recovery.Scope);
                }
            }
            if (recovered && recovery.Scrolling && recovery.Declaration.IndexedOccurrenceKeys?.Contains(recovery.RequestedKey) == true)
                recovery.Lifetime.CancelAfter(Timeout.InfiniteTimeSpan);
            if (recovered && ReferenceEquals(heldViewport, recovery) && !recovery.Scrolling &&
                recovery.HasRecoveryPublication && recovery.Anchor.Key != recovery.RequestedKey && recovery.Declaration.IndexedOccurrenceKeys?.Contains(recovery.Anchor.Key) != true)
                RestartHeldViewportRecovery(recovery);
            if (recovered) await recovery.Published.Task.WaitAsync(recovery.Lifetime.Token);
        }
        catch (OperationCanceledException) when (recovery.Lifetime.IsCancellationRequested)
        {
            if (ReferenceEquals(heldViewport, recovery) && !disposed) failed(new TimeoutException("Viewport recovery timed out."));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (ReferenceEquals(heldViewport, recovery) && !disposed) failed(error);
        }
        finally
        {
            if (entered) admission.Release();
            if (ReferenceEquals(heldViewport, recovery)) CancelViewportRecovery(true);
            recovery.Lifetime.Dispose();
        }
    }

    private void CancelViewportRecovery(bool applyLatest)
    {
        if (heldViewport is not { } recovery) return;
        heldViewport = null;
        recovery.Lifetime.Cancel(); recovery.Published.TrySetCanceled();
        if (!applyLatest || disposed) return;
        applyingRecoveryFallback = true;
        try { Apply(recovery.Binding, recovery.Declaration, recovery.Scope); }
        finally { applyingRecoveryFallback = false; }
    }
    private void CancelInputContinuity(bool preserveReader = false)
    {
        lastDirectionalAnchor = null;
        cancelKeyboardEnd?.Invoke();
        // Leaving the list changes focus ownership, not the need to repair its
        // still-visible reader. Presentation/domain retirement cancels the read.
        if (!preserveReader && heldViewport is { } recovery) recovery.Focus = null;
        if (continuityPanel is not null && pendingContinuity is { } pending)
        {
            // Input cancels the old reading/focus intent, but native insertion
            // anchoring belongs to the layout transaction. Changing its mode
            // between collection notifications and arrange changes which edge
            // WinUI uses to interpret the anchor it already captured.
            pending.Viewport = null; pending.Focus = null; pending.FollowEnd = false;
            pending.AwaitingFocusLayout = false;
            pending.ViewportKeyPending = pending.FocusKeyPending = false;
            pending.ViewportRetention?.Dispose(); pending.ViewportRetention = null;
            pending.FocusRetention?.Dispose(); pending.FocusRetention = null;
            QueueContinuity(); // Existing demand/layout completion retires the mode.
        }
        else CancelContinuity();
        QueueViewportObservation();
    }
}
