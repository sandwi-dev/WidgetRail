using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PendingScrollReveal(ScrollRevealRequest Request, Binding Scroll, Binding Target);
    private PendingScrollReveal? pendingScrollReveal;
    private sealed class PendingIndexedScrollReveal(ScrollRevealRequest request,Binding collection)
    {
        internal ScrollRevealRequest Request {get;}=request;
        internal Binding Collection {get;}=collection;
        internal bool Started;
        internal Binding? FocusTarget;
        internal WeakReference<DependencyObject>? FocusFallback;
        internal DependencyObject? NativeFallback=>FocusFallback is {} weak && weak.TryGetTarget(out var target)?target:null;
    }
    private PendingIndexedScrollReveal? pendingIndexedScrollReveal;
    private long lastScrollRevealRequest;

    private void UpdateScrollReveal(ViewSnapshot snapshot)
    {
        if (snapshot.ScrollRevealRequest is {} indexedRequest && (indexedRequest.IndexedItem is not null || indexedRequest.DeferredSource is not null))
        {
            UpdateIndexedScrollReveal(indexedRequest);
            return;
        }
        if (snapshot.ScrollRevealRequest is not { } request || presentationOnly || !presentationActive ||
            !bindings.TryGetValue(request.ScrollId, out var scroll) || scroll.Element is not ScrollViewer ||
            request.TargetId is null || !bindings.TryGetValue(request.TargetId, out var target) || scroll.Identity.Scope != activeScope ||
            target.Identity.Scope != activeScope || !IsDescendant(request.TargetId, request.ScrollId))
        { CancelScrollReveal(); return; }
        if (pendingScrollReveal is { } old && (old.Request != request || !ReferenceEquals(old.Scroll, scroll) || !ReferenceEquals(old.Target, target)))
            CancelScrollReveal();
        if (request.RequestId <= lastScrollRevealRequest) return;
        CancelPassiveScrollFocus();
        scrollGesture = null;
        CancelScrollReveal();
        lastScrollRevealRequest = request.RequestId;
        var pending = new PendingScrollReveal(request, scroll, target);
        pendingScrollReveal = pending;
        LayoutUpdated += ScrollRevealLayoutUpdated;
        // Focus restoration runs first. Reveal scrolls only its own viewport and never moves focus.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ReferenceEquals(pendingScrollReveal, pending)) ApplyScrollReveal();
        });
    }

    private void UpdateIndexedScrollReveal(ScrollRevealRequest request)
    {
        if (presentationOnly || !presentationActive || HasTransientControl ||
            !bindings.TryGetValue(request.ScrollId, out var binding) || binding.Element is not WidgetIndexedCollectionView ||
            binding.Identity.Scope != activeScope)
        { CancelScrollReveal(); return; }
        var completing=false;
        if(pendingIndexedScrollReveal is {Request.DeferredSource:{} deferred} admitted && admitted.Request.RequestId==request.RequestId &&
            ReferenceEquals(admitted.Collection,binding) && IndexedCollectionContract.SameViewportDomain(declarations[binding.Identity.Id].Node.IndexedCollection,deferred))
        {
            if(request.DeferredSource is {} refreshed && IndexedCollectionContract.SameViewportDomain(refreshed,deferred) &&
                IndexedCollectionContract.SameViewportDomain(declarations[binding.Identity.Id].Node.IndexedCollection,refreshed))return;
            completing=request.IndexedItem is not null && NativeRestoreStillOwnsFocus(admitted.FocusTarget,admitted.NativeFallback);
        }
        if (pendingIndexedScrollReveal is { } old && (old.Request != request || !ReferenceEquals(old.Collection, binding)))
            CancelScrollReveal();
        if (request.RequestId <= lastScrollRevealRequest && !completing) return;
        CancelPassiveScrollFocus();
        scrollGesture = null;
        CancelScrollReveal();
        lastScrollRevealRequest = request.RequestId;
        var pending = new PendingIndexedScrollReveal(request, binding);
        pendingIndexedScrollReveal = pending;
        if(request.DeferredSource is {} starting)
        {
            var expected=effectiveView?.FocusGroupEntryRequest is {TargetId:{} focusId} focus && focus.RequestId==request.RequestId
                ?bindings.GetValueOrDefault(focusId):FocusedBinding();
            pending.FocusTarget=expected?.Element is WidgetIndexedCollectionView?null:expected;
            if(XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject currentFocus)
                pending.FocusFallback=new(currentFocus);
            if(!IndexedCollectionContract.SameViewportDomain(declarations[binding.Identity.Id].Node.IndexedCollection,starting))CancelScrollReveal();
            return;
        }
        // Focus restoration is queued first. The collection rejects stale query
        // identities and waits for the exact row payload before scrolling.
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(pendingIndexedScrollReveal, pending)) return;
            pending.Started=true;
            if (disposed || !presentationActive || presentationOnly || HasTransientControl ||
                !bindings.TryGetValue(request.ScrollId, out var current) || !ReferenceEquals(current, binding) ||
                current.Identity.Scope != activeScope ||
                !((WidgetIndexedCollectionView)binding.Element).Reveal(request.IndexedItem!)) CancelScrollReveal();
        })) CancelScrollReveal();
    }

    private void ScrollRevealLayoutUpdated(object? sender, object args) => ApplyScrollReveal();
    private void ObserveDeferredRevealFocus(DependencyObject? next)
    {
        if(!applying && pendingIndexedScrollReveal is {Request.DeferredSource:not null} pending &&
            !NativeRestoreStillOwnsFocus(pending.FocusTarget,pending.NativeFallback,next))CancelScrollReveal();
    }

    private void ApplyScrollReveal()
    {
        if (pendingScrollReveal is not { } pending || applying) return;
        if (disposed || !presentationActive || presentationOnly || HasTransientControl ||
            !bindings.TryGetValue(pending.Request.ScrollId, out var scroll) || !ReferenceEquals(scroll, pending.Scroll) ||
            pending.Request.TargetId is null || !bindings.TryGetValue(pending.Request.TargetId, out var target) || !ReferenceEquals(target, pending.Target) ||
            scroll.Identity.Scope != activeScope || target.Identity.Scope != activeScope)
        { CancelScrollReveal(); return; }
        var viewport = (ScrollViewer)scroll.Element;
        var element = target.LayoutElement;
        if (element.Visibility != Visibility.Visible) { CancelScrollReveal(); return; }
        // Lightweight TextBlocks can have valid native layout while IsLoaded
        // remains false. Require attachment to this viewport's live XamlRoot
        // instead; binding/scope checks above retain snapshot ownership.
        if (!IsLoaded || !viewport.IsLoaded || element.XamlRoot is null ||
            !ReferenceEquals(element.XamlRoot, viewport.XamlRoot) || viewport.ViewportHeight <= 0 ||
            element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        if (NearestScroll(element) != viewport)
        { CancelScrollReveal(); return; }
        var bounds = element.TransformToVisual(viewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var horizontal = declarations[scroll.Identity.Id].Node.ScrollAxis == ScrollAxis.Horizontal;
        var visible = WidgetScrollViewport.ContentBounds(viewport);
        var offset = horizontal ? viewport.HorizontalOffset + bounds.X - visible.X : viewport.VerticalOffset + bounds.Y - visible.Y;
        var limit = horizontal ? viewport.ScrollableWidth : viewport.ScrollableHeight;
        CancelScrollReveal(); // Consume before ChangeView can trigger layout again.
        viewport.ChangeView(horizontal ? Math.Clamp(offset, 0, limit) : null,
            horizontal ? null : Math.Clamp(offset, 0, limit), null,
            disableAnimation: Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced);
    }

    private void CancelScrollReveal()
    {
        if (pendingIndexedScrollReveal?.Collection.Element is WidgetIndexedCollectionView collection) collection.CancelReveal();
        pendingIndexedScrollReveal = null;
        pendingScrollReveal = null;
        LayoutUpdated -= ScrollRevealLayoutUpdated;
    }
}
