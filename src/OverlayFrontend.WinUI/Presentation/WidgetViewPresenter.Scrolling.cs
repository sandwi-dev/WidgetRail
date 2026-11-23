using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool hasReadingPane;
    private PassiveScrollFocus? passiveScrollFocus;
    private bool CollectionBoundaryReady(WidgetIndexedCollectionView collection) => !applying &&
        (!ReferenceEquals(pendingIndexedScrollReveal?.Collection.Element,collection) ||
            pendingIndexedScrollReveal is {Started:true} && !collection.IsScrollRevealPending);
    private bool CollectionScrollPending(WidgetIndexedCollectionView collection) =>
        CollectionMemoryPending(collection) ||
        scrollGesture is { } gesture && ReferenceEquals(gesture.Owner.Element, collection) &&
            (gesture.Intermediate || gesture.Horizontal.IsPending || gesture.Vertical.IsPending) ||
        directionalScroll is { Pending: true } directional && ReferenceEquals(directional.Owner.Element, collection);
    private bool CollectionMemoryPending(WidgetIndexedCollectionView collection)
    {
        return pendingIndexedViewports.Any(pending => Owns(pending.Memory.Element)) ||
            pendingPageMemory.Any(memory => memory.Indexed.Any(viewport => Owns(viewport.Element)));
        bool Owns(WidgetElementIdentity identity) => bindings.TryGetValue(identity.Id,out var binding) &&
            binding.Identity==identity && ReferenceEquals(binding.Element,collection);
    }
    private bool OwnsCollectionScrollGesture(WidgetIndexedCollectionView collection) =>
        scrollGesture is { } gesture && gesture.Scope == activeScope && ReferenceEquals(gesture.Owner.Element, collection);
    private bool NeedsCollectionEntry(WidgetIndexedCollectionView collection) =>
        collection.IsFocusParked && !collection.IsEntryPending && !OwnsCollectionScrollGesture(collection);

    private void DeferredIndexedQueryCommitted(WidgetIndexedCollectionView collection)
    {
        // Match Apply's parked-focus reconciliation after an asynchronous data
        // commit. A newer explicit focus choice or scroll owner still wins.
        if (disposed || applying || !presentationActive || !presentationInputEnabled || !automaticFocusEnabled ||
            presentationOnly || pendingGroupEntry is not null ||
            FocusedBinding() is not { } focused || !ReferenceEquals(focused.Element,collection) ||
            focused.Identity.Scope != activeScope || !NeedsCollectionEntry(collection)) return;
        needsEntry=true;
        CaptureEntryOwnership(replace:true);
        QueueEntryFocus();
    }

    private void ChangeCollectionScrolling(string id, bool retainedViewport)
    {
        // An ordered window rebase retains the same native viewport and input
        // owner. Discard old coordinates/motion, not the user's scroll ownership;
        // otherwise parked focus can replay an old offscreen entry mid-gesture.
        if (scrollGesture is { } gesture && gesture.Owner.Identity.Id == id)
        {
            if (retainedViewport) gesture.Stop();
            else scrollGesture = null;
        }
        if (directionalScroll?.Owner.Identity.Id == id) directionalScroll = null;
        if (!retainedViewport && passiveScrollFocus is { } focus && FindBinding(focus.Viewport)?.Identity.Id == id)
            CancelPassiveScrollFocus();
    }
    private void CancelPassiveScrollFocus()
    {
        var previous = passiveScrollFocus;
        passiveScrollFocus = null;
        previous?.Dispose();
    }
    private void RetirePassiveScrollFocus(PassiveScrollFocus expected)
    {
        if (ReferenceEquals(passiveScrollFocus, expected)) CancelPassiveScrollFocus();
    }

    // WinUI can queue focus's BringIntoView until after Focus() returns. Its
    // native focus-reveal policy must remain disabled for this passive focus
    // ownership, not an arbitrary dispatcher delay. Explicit input/reveal,
    // focus departure and unloading all restore the original native policy.
    private sealed class PassiveScrollFocus : IDisposable
    {
        private readonly ScrollViewer scroll;
        private readonly Control target;
        private readonly bool previous;
        private readonly Microsoft.UI.Xaml.Data.Binding? previousBinding;
        private readonly Action<PassiveScrollFocus> ended;
        private bool disposed;
        internal ScrollViewer Viewport => scroll;
        internal PassiveScrollFocus(ScrollViewer scroll, Control target, Action<PassiveScrollFocus> ended)
        {
            this.scroll = scroll; this.target = target; this.ended = ended;
            previous = scroll.BringIntoViewOnFocusChange;
            previousBinding = scroll.GetBindingExpression(ScrollViewer.BringIntoViewOnFocusChangeProperty)?.ParentBinding;
            scroll.BringIntoViewOnFocusChange = false;
            scroll.Unloaded += End;
            target.LostFocus += End;
            target.Unloaded += End;
        }
        private void End(object sender, RoutedEventArgs args) => ended(this);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scroll.Unloaded -= End;
            target.LostFocus -= End;
            target.Unloaded -= End;
            // Native templates may expose an expression from ReadLocalValue,
            // which cannot be passed back through SetValue. Preserve ordinary
            // bindings explicitly; otherwise restore the effective boolean.
            if (previousBinding is not null)
                scroll.SetBinding(ScrollViewer.BringIntoViewOnFocusChangeProperty, previousBinding);
            else scroll.BringIntoViewOnFocusChange = previous;
        }
    }

    private sealed record ScrollGesture(ScrollViewer Viewport, Binding Owner, string Scope)
    {
        internal ScrollOffsetAccumulator Horizontal { get; } = new();
        internal ScrollOffsetAccumulator Vertical { get; } = new();
        internal int HorizontalDirection;
        internal int VerticalDirection;
        internal bool Animated;
        internal bool Intermediate;
        internal bool NativeWheel;
        internal long TraceId;
        internal double Tolerance => 1 / (Viewport.XamlRoot?.RasterizationScale ?? 1);
        internal void Observe()
        {
            Horizontal.Observe(Viewport.HorizontalOffset, Viewport.ScrollableWidth, Tolerance);
            Vertical.Observe(Viewport.VerticalOffset, Viewport.ScrollableHeight, Tolerance);
        }
        internal void ResetOffsets() { Horizontal.Reset(); Vertical.Reset(); }
        internal void Stop()
        {
            var stop = Animated;
            Animated = Intermediate = false;
            ResetOffsets();
            if (stop && Viewport.IsLoaded)
            {
                // Native cancellation reports its final compositor position
                // asynchronously. Do not send a stale ChangeView alongside it.
                (Viewport.Content as UIElement)?.CancelDirectManipulations();
            }
        }
    }
    private ScrollGesture? currentScrollGesture;
    private ScrollGesture? scrollGesture
    {
        get=>currentScrollGesture;
        set
        {
            if (!ReferenceEquals(currentScrollGesture, value)) currentScrollGesture?.Stop();
            if(!ReferenceEquals(currentScrollGesture?.Viewport,value?.Viewport))
            {
                if(currentScrollGesture is {} old)old.Viewport.ViewChanged-=ScrollFocusViewChanged;
                if(value is {} next)next.Viewport.ViewChanged+=ScrollFocusViewChanged;
            }
            currentScrollGesture=value;RefreshScrollFocusOutline();
        }
    }
    private void ScrollFocusViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        scrollGesture?.Observe();
        if (scrollGesture is { } gesture)
        {
            gesture.Intermediate = args.IsIntermediate;
            if (!args.IsIntermediate && !gesture.Horizontal.IsPending && !gesture.Vertical.IsPending) gesture.Animated = false;
            TraceScrollGesture(gesture, ScrollTracePhase.NativeViewChanged,
                gesture.Viewport.HorizontalOffset, gesture.Viewport.VerticalOffset);
        }
        RefreshScrollFocusOutline();
    }

    /// <summary>
    /// Applies one normalized DIP movement to the active native scroll owner.
    /// The input owner supplies cadence and sensitivity; WinUI owns extent,
    /// virtualization, compositor animation and painting. No custom animation
    /// queue or focus move is added.
    /// </summary>
    internal bool ScrollBy(double horizontalDelta, double verticalDelta)
    {
        if (previewEntered && previewSurface?.Fragment is { } preview) return preview.ScrollBy(horizontalDelta, verticalDelta);
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Scroll on the widget dispatcher.");
        if (!presentationActive || !presentationInputEnabled || disposed || applying || !IsLoaded || frame is null || !double.IsFinite(horizontalDelta) || !double.IsFinite(verticalDelta)) return false;
        if (HasTransientControl) return true;
        for (var binding = hasReadingPane && verticalDelta != 0 ? FocusedBinding() : null; binding is not null;
             binding = declarations[binding.Identity.Id].ParentId is { } parent ? bindings.GetValueOrDefault(parent) : null)
            if (binding.Element is WidgetPresentationSurface { ReadingPane: { } reader })
            {
                reader.ChangeView(null, Math.Clamp(reader.VerticalOffset + verticalDelta, 0, reader.ScrollableHeight), null, true);
                return true;
            }
        CancelPassiveScrollFocus();
        CancelScrollReveal();
        if (FocusedBrowser?.Scroll(horizontalDelta, verticalDelta) == true) return true;
        directionalScroll = null;
        CancelGroupEntry();
        ScrollViewer? target = null;
        for (var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; current is not null && !ReferenceEquals(current, this);
             current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer viewer && CanScroll(viewer)) { target = viewer; break; }
        // A panel can keep its actions fixed above a scrolling document. Prefer
        // that panel's viewport before falling back to another column's list.
        // Indexed focus can be parked on the wrapper while a page retires; its
        // native viewport is a descendant, not an ancestor of that focus target.
        for (var current = FocusedBinding(); target is null && current is not null;
             current = declarations[current.Identity.Id].ParentId is { } parent ? bindings.GetValueOrDefault(parent) : null)
            target = bindings.Values.Where(binding => binding.Identity.Scope == activeScope &&
                    IsDescendant(binding.Identity.Id, current.Identity.Id))
                .Select(binding => binding.Element is ScrollViewer viewer ? viewer :
                    binding.Element is WidgetIndexedCollectionView collection ? FindScroll(collection.NativeView) : null)
                .FirstOrDefault(viewer => viewer is not null && CanScroll(viewer));
        target ??= bindings.Values.Where(binding => binding.Identity.Scope == activeScope &&
                binding.Element.Visibility == Visibility.Visible)
            .Select(binding => binding.Element is ScrollViewer viewer ? viewer :
                binding.Element is WidgetIndexedCollectionView collection ? FindScroll(collection.NativeView) : null)
            .FirstOrDefault(viewer => viewer is not null && CanScroll(viewer));
        if (target is null) return false;
        var owner = FindBinding(target);
        if (owner is null) return false;
        var gesture = scrollGesture is { } prior && ReferenceEquals(prior.Viewport, target) &&
            ReferenceEquals(prior.Owner, owner) && prior.Scope == activeScope
                ? prior : new ScrollGesture(target, owner, activeScope);
        scrollGesture = gesture;
        gesture.NativeWheel = false;
        if (owner.Element is WidgetIndexedCollectionView indexed)
        {
            indexed.CancelHostNavigation(preserveReader: true);
            indexed.ObserveBoundaryScrollInput(horizontalDelta,verticalDelta);
        }
        var horizontalDirection = Math.Sign(horizontalDelta);
        var verticalDirection = Math.Sign(verticalDelta);
        var reversing = horizontalDirection != 0 && gesture.HorizontalDirection != 0 && horizontalDirection != gesture.HorizontalDirection ||
            verticalDirection != 0 && gesture.VerticalDirection != 0 && verticalDirection != gesture.VerticalDirection;
        // Retarget native animation from the current position when reversing,
        // rather than accumulating from the old forward destination.
        if (reversing) gesture.ResetOffsets();
        if (horizontalDirection != 0) gesture.HorizontalDirection = horizontalDirection;
        if (verticalDirection != 0) gesture.VerticalDirection = verticalDirection;
        var horizontal = gesture.Horizontal.Propose(target.HorizontalOffset, horizontalDelta, target.ScrollableWidth, gesture.Tolerance);
        var vertical = gesture.Vertical.Propose(target.VerticalOffset, verticalDelta, target.ScrollableHeight, gesture.Tolerance);
        var animate = !Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced;
        TraceScrollRequest(horizontal, vertical);
        var accepted = target.ChangeView(horizontal, vertical, null, disableAnimation: !animate);
        if (accepted)
        {
            gesture.Horizontal.Accepted(horizontal);
            gesture.Vertical.Accepted(vertical);
            gesture.Animated = animate;
        }
        TraceScrollGesture(gesture, ScrollTracePhase.Request, horizontal, vertical, accepted);
        return true;

        bool CanScroll(ScrollViewer viewer) => viewer.IsLoaded && viewer.Visibility == Visibility.Visible &&
            (horizontalDelta != 0 && viewer.HorizontalScrollMode != ScrollMode.Disabled && viewer.ScrollableWidth > 0 ||
             verticalDelta != 0 && viewer.VerticalScrollMode != ScrollMode.Disabled && viewer.ScrollableHeight > 0);
    }
    partial void TraceScrollRequest(double horizontal, double vertical);

    private static void TraceScrollGesture(ScrollGesture gesture, ScrollTracePhase phase,
        double requestedX, double requestedY, bool accepted = false)
    {
        var log = PresentationPipelineTrace.Log;
        if (!log.ScrollEnabled) return;
        if (gesture.TraceId == 0) gesture.TraceId = log.Begin().Id;
        var viewport = gesture.Viewport;
        var flags = (accepted ? 1 : 0) | (gesture.Animated ? 2 : 0) |
            (gesture.Intermediate ? 4 : 0) | (gesture.NativeWheel ? 8 : 0);
        log.Scroll(gesture.TraceId, (int)phase, requestedX, requestedY,
            viewport.HorizontalOffset, viewport.VerticalOffset,
            viewport.ScrollableWidth, viewport.ScrollableHeight, flags);
    }

    /// <summary>
    /// Rejoin directional navigation after free scrolling. Only native realized
    /// controls in the same declaration owner and active input scope participate;
    /// an unloaded page never causes a jump back to the old logical index.
    /// Returns false while a visible page has no ready focus target yet.
    /// </summary>
    internal bool SettleScrollFocus()
    {
        if (scrollGesture is not { } gesture) return true;
        if (!automaticFocusEnabled || !presentationActive || !presentationInputEnabled || disposed || applying || HasTransientControl ||
            activeScope != gesture.Scope || !gesture.Viewport.IsLoaded ||
            !bindings.TryGetValue(gesture.Owner.Identity.Id, out var owner) || !ReferenceEquals(owner, gesture.Owner))
        { scrollGesture = null; return true; }
        gesture.Observe();
        if (gesture.Animated && (gesture.Intermediate || gesture.Horizontal.IsPending || gesture.Vertical.IsPending)) return false;
        var scroll = gesture.Viewport;
        var visible = WidgetScrollViewport.ContentBounds(scroll);
        if (visible.Width <= 0 || visible.Height <= 0) return false;
        // Composite controls own native children that have no separate widget declaration.
        var controls = owner.Element switch
        {
            WidgetIndexedCollectionView collection => collection.ScrollFocusCandidates(),
            Media.MediaPlayerView media when media.ThumbnailFocusCandidates(scroll) is { } tiles => tiles,
            _ => bindings.Values.Where(binding => !ReferenceEquals(binding, owner) && Navigable(binding) && binding.Element is Control &&
                IsDescendant(binding.Identity.Id, owner.Identity.Id))
                .Select(binding => (Control)binding.Element),
        };
        var candidates = controls.Where(control => control.IsLoaded && control.IsEnabled &&
                control.Visibility == Visibility.Visible && NearestScroll(control) == scroll)
            .Select(control => (Control: control, Bounds: control.TransformToVisual(scroll).TransformBounds(
                new Rect(0, 0, control.ActualWidth, control.ActualHeight))))
            .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0 &&
                item.Bounds.Right > visible.Left + .5 && item.Bounds.Bottom > visible.Top + .5 && item.Bounds.X < visible.Right - .5 && item.Bounds.Y < visible.Bottom - .5)
            .ToArray();
        // Preserve focus if any of its pixels are still in the viewport, matching
        // the native host. Otherwise prefer a fully visible row at the leading edge.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (candidates.Any(item => IsWithin(focused, item.Control))) { scrollGesture = null; return true; }
        var horizontal = scroll.HorizontalScrollMode != ScrollMode.Disabled && scroll.ScrollableWidth > 0 &&
            (scroll.VerticalScrollMode == ScrollMode.Disabled || scroll.ScrollableHeight <= 0);
        var target = candidates.OrderByDescending(item => item.Bounds.X >= visible.Left - .5 && item.Bounds.Y >= visible.Top - .5 &&
                item.Bounds.Right <= visible.Right + .5 && item.Bounds.Bottom <= visible.Bottom + .5)
            .ThenBy(item => horizontal ? item.Bounds.X : item.Bounds.Y)
            .ThenBy(item => horizontal ? item.Bounds.Y : item.Bounds.X).FirstOrDefault().Control;
        if (target is null) return owner.Element is not WidgetIndexedCollectionView && FocusReadingRegion();
        // An oversized row may only partially fit. Focus must not undo the user's
        // scroll offset through WinUI's automatic BringIntoView request.
        return FocusWithoutReveal(target);

        bool FocusReadingRegion()
        {
            if(!scroll.IsTabStop)return false;
            return FocusWithoutReveal(scroll);
        }
        bool FocusWithoutReveal(Control control)
        {
            CancelPassiveScrollFocus();
            passiveScrollFocus = new(scroll, control, RetirePassiveScrollFocus);
            if (!control.Focus(FocusState.Keyboard)) { CancelPassiveScrollFocus(); return false; }
            scrollGesture = null;
            return true;
        }
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject ancestor)
    {
        for (; child is not null; child = VisualTreeHelper.GetParent(child))
            if (ReferenceEquals(child, ancestor)) return true;
        return false;
    }

    private static ScrollViewer? NearestScroll(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer scroll) return scroll;
        return null;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            if (FindScroll(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }
}
