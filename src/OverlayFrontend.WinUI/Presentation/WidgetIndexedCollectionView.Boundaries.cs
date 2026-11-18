using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private (string Source,long? Epoch)? boundaryDomain;
    private bool nearBefore,nearAfter;
    private IndexedCollectionBoundary? boundaryInFlight;
    partial void TraceBoundaryDemand(IndexedCollectionBoundary direction);
    private IReadOnlyList<string> BoundaryProtectedKeys()
    {
        var keys=observedScroll is {} scroll?VisibleOccurrences(scroll).Select(item=>item.Slot.Key!).ToHashSet(StringComparer.Ordinal):new HashSet<string>(StringComparer.Ordinal);
        if(CaptureFocusedItem()?.ItemKey is {} focused)keys.Add(focused);
        return keys.Take(IndexedCollectionLimits.MaximumContinuityItems).ToArray();
    }

    private IndexedCollectionBoundary? CaptureBoundaryCrossing(IndexedCollectionDescriptor descriptor,ViewNode declaration)
    {
        if(!descriptor.ObservesBoundaries || declaration.CollectionBoundaries is null || observedScroll is not {} scroll ||
            heldViewport is not null || IsViewportScrollPending || pendingIndex is not null || pendingContinuity is not null || descriptor.Count==0 ||
            BoundaryObservationReady?.Invoke(this)==false)return null;
        var visible=VisibleOccurrences(scroll);
        if(visible.Length==0 || visible.Any(item=>item.Slot.Value is not {Lease.IsCurrent:true} row ||
            view?.ContainerFromIndex(item.Slot.Index) is not Microsoft.UI.Xaml.FrameworkElement container || !HasCurrentRowLayout(container,row)))return null;
        var domain=(descriptor.SourceId,descriptor.EndFollowingEpoch);
        if(boundaryDomain!=domain){boundaryDomain=domain;nearBefore=nearAfter=false;}
        var horizontal=axis==ScrollAxis.Horizontal;
        var viewport=horizontal?scroll.ViewportWidth:scroll.ViewportHeight;
        var maximum=horizontal?scroll.ScrollableWidth:scroll.ScrollableHeight;
        var offset=horizontal?scroll.HorizontalOffset:scroll.VerticalOffset;
        if(viewport<=0)return null;
        var threshold=viewport*.5;
        var before=offset<=threshold;var after=maximum-offset<=threshold;
        var result=before&&!nearBefore&&declaration.CollectionBoundaries.Before?IndexedCollectionBoundary.Before:
            after&&!nearAfter&&declaration.CollectionBoundaries.After?IndexedCollectionBoundary.After:(IndexedCollectionBoundary?)null;
        nearBefore=before;nearAfter=after;
        return result==boundaryInFlight?null:result;
    }

    private bool RequestAdjacentBoundary(IndexedCollectionBoundary direction)
    {
        if(!CanReceiveInput || BoundaryObservationReady?.Invoke(this)==false || source?.Declaration is not {IndexedCollection.ObservesBoundaries:true,CollectionBoundaries:{} available} ||
            !(direction==IndexedCollectionBoundary.Before?available.Before:available.After))return false;
        // Like discovered-list continuation, retain current focus and consume
        // only the loadable edge. No future index/movement is replayed.
        if(heldViewport is not null || pendingIndex is not null)return true;
        if(boundaryInFlight!=direction && queuedViewportObservation?.Observation.Boundary!=direction)
            QueueViewportObservation(demandedBoundary:direction);
        return true;
    }

    internal void ObserveBoundaryScrollInput(double horizontalDelta,double verticalDelta)
    {
        if(observedScroll is not {} scroll)return;
        var horizontal=axis==ScrollAxis.Horizontal;var delta=horizontal?horizontalDelta:verticalDelta;
        var offset=horizontal?scroll.HorizontalOffset:scroll.VerticalOffset;
        var maximum=horizontal?scroll.ScrollableWidth:scroll.ScrollableHeight;
        if(delta<0&&offset<=1)RequestAdjacentBoundary(IndexedCollectionBoundary.Before);
        else if(delta>0&&maximum-offset<=1)RequestAdjacentBoundary(IndexedCollectionBoundary.After);
    }
}
