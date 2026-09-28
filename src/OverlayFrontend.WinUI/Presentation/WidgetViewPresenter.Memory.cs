using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private const int MaximumMemoryEntries = 64;
    private sealed class PendingViewport(IndexedViewportMemento memory)
    { internal IndexedViewportMemento Memory { get; } = memory; internal bool Requested; internal double? Offset; internal IDisposable? Retention; internal WidgetIndexedCollectionView? Collection; }
    private readonly List<PendingViewport> pendingIndexedViewports = [];
    private sealed class PendingScroll(ScrollViewportMemento memory)
    { internal ScrollViewportMemento Memory { get; } = memory; internal ScrollViewer? Scroll; internal double? Offset; }
    private readonly List<PendingScroll> pendingScrollViewports = [];
    private long memoryIntent;
    private PresentationMemoryOwner? memoryOwner;
    private bool memoryRestoreQueued;
    internal bool HasPendingMemoryRestore => pendingIndexedViewports.Count != 0 || pendingScrollViewports.Count != 0;

    internal WidgetPresentationMemento? CapturePresentationState()
    {
        if (disposed || frame is null) return null;
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Capture presentation memory on its dispatcher.");
        RememberFocus();
        var indexed = bindings.Values.Where(binding => binding.Element is WidgetIndexedCollectionView && IsMemoryVisible(binding.Element))
            .Select(binding => ((WidgetIndexedCollectionView)binding.Element).CaptureViewport(binding.Identity))
            .OfType<IndexedViewportMemento>().Take(MaximumMemoryEntries).ToArray();
        var scrolls = bindings.Values.Where(binding => binding.Element is ScrollViewer && IsMemoryVisible(binding.Element))
            .Select(CaptureScrollViewport).OfType<ScrollViewportMemento>().Take(MaximumMemoryEntries).ToArray();
        return new(PresentationMemoryOwner.From(frame.Authority), pendingGroupEntry is null ? lastGroupRequest : 0,
            remembered.Values.TakeLast(MaximumMemoryEntries).ToArray(),
            groupMemory.Values.TakeLast(MaximumMemoryEntries).Select(memory => new GroupFocusMemento(memory.Group, memory.Child)).ToArray(),
            collectionMemory.TakeLast(MaximumMemoryEntries).Select(pair => new IndexedFocusMemento(pair.Key.Identity, pair.Value)).ToArray(), indexed, scrolls);
    }

    /// <summary>Apply after the first frame, while inactive. The shell controls later entry focus.</summary>
    internal bool RestorePresentationState(WidgetPresentationMemento state)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Restore presentation memory on its dispatcher.");
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelMemoryRestoration();
        if (frame is null || state.Owner != PresentationMemoryOwner.From(frame.Authority)) return false;
        lastGroupRequest = Math.Max(lastGroupRequest, state.ConsumedFocusRequest);
        if (pendingGroupEntry is { } request && request.RequestId <= state.ConsumedFocusRequest) pendingGroupEntry = null;
        foreach (var identity in state.Focus.Take(MaximumMemoryEntries))
            if (Matches(identity)) remembered[identity.Scope] = identity;
        foreach (var group in state.Groups.Take(MaximumMemoryEntries))
            if (Matches(group.Group) && Matches(group.Child) && IsDescendant(group.Child.Id, group.Group.Id))
                groupMemory[group.Group.Id] = new(group.Group, group.Child);
        foreach (var item in state.IndexedFocus.Take(MaximumMemoryEntries))
            if (Matches(item.Element) && declarations[item.Element.Id].Node.IndexedCollection is { } query &&
                query.SourceId == item.Target.SourceId && query.QueryGeneration == item.Target.QueryGeneration &&
                item.Target.Index >= 0 && item.Target.Index < query.Count)
                RememberCollectionFocus(item.Element, item.Target);
        foreach (var viewport in state.IndexedViewports.Take(MaximumMemoryEntries))
            if (Matches(viewport.Element)) pendingIndexedViewports.Add(new(viewport));
        foreach (var viewport in state.ScrollViewports.Take(MaximumMemoryEntries))
            if (Matches(viewport.Element) && Matches(viewport.Anchor)) pendingScrollViewports.Add(new(viewport));
        memoryIntent = entryIntentVersion;
        memoryOwner = state.Owner;
        if (HasPendingMemoryRestore) LayoutUpdated += RestoreMemoryLayout;
        return true;
    }

    private bool Matches(WidgetElementIdentity identity) =>
        bindings.TryGetValue(identity.Id, out var binding) && binding.Identity == identity;

    private static bool IsMemoryVisible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private ScrollViewportMemento? CaptureScrollViewport(Binding binding)
    {
        var scroll = (ScrollViewer)binding.Element;
        if (!scroll.IsLoaded || scroll.ViewportHeight <= 0 || scroll.ViewportWidth <= 0) return null;
        var axis = declarations[binding.Identity.Id].Node.ScrollAxis ?? ScrollAxis.Vertical;
        var horizontal = axis == ScrollAxis.Horizontal;
        var extent = horizontal ? scroll.ViewportWidth : scroll.ViewportHeight;
        var candidates = bindings.Values.Where(child => child.Identity != binding.Identity && child.Element.IsLoaded && IsMemoryVisible(child.Element) &&
                IsDescendant(child.Identity.Id, binding.Identity.Id) &&
                NearestScroll(child.Identity.Id) == binding.Identity.Id &&
                (declarations[child.Identity.Id].Node.IsFocusable || declarations[child.Identity.Id].Node.Children.Count == 0))
            .Select(child => (child.Identity, Bounds: child.LayoutElement.TransformToVisual(scroll).TransformBounds(
                new Rect(0, 0, child.LayoutElement.ActualWidth, child.LayoutElement.ActualHeight))))
            .Where(pair => (horizontal ? pair.Bounds.Right : pair.Bounds.Bottom) > 0 && (horizontal ? pair.Bounds.X : pair.Bounds.Y) < extent)
            .OrderBy(pair => horizontal ? pair.Bounds.X : pair.Bounds.Y).FirstOrDefault();
        if (candidates.Identity is null) return null;
        var size = horizontal ? candidates.Bounds.Width : candidates.Bounds.Height;
        var position = horizontal ? candidates.Bounds.X : candidates.Bounds.Y;
        return new(binding.Identity, candidates.Identity, axis, size > 0 ? Math.Clamp(-position / size, -1, 1) : 0);

        string? NearestScroll(string id)
        {
            for (var parent = declarations[id].ParentId; parent is not null; parent = declarations[parent].ParentId)
                if (bindings[parent].Element is ScrollViewer) return parent;
            return null;
        }
    }

    private void RestoreMemoryLayout(object? sender, object args)
    {
        if (disposed || frame is null || memoryOwner != PresentationMemoryOwner.From(frame.Authority) || memoryIntent != entryIntentVersion)
        { CancelMemoryRestoration(); return; }
        if (!presentationActive || applying || !IsLoaded) return;
        foreach (var pending in pendingIndexedViewports.ToArray())
        {
            if (Matches(pending.Memory.Element) && bindings[pending.Memory.Element.Id].Element is WidgetIndexedCollectionView collection && IsMemoryVisible(collection))
            {
                if (pending.Collection is null) { pending.Collection = collection; collection.NavigationSettled += QueueMemoryRestoration; }
                pending.Retention ??= collection.RetainViewport(pending.Memory, QueueMemoryRestoration);
                if (!collection.RestoreViewport(pending.Memory, ref pending.Requested, ref pending.Offset)) continue;
            }
            ReleaseViewport(pending); pendingIndexedViewports.Remove(pending);
        }
        foreach (var pending in pendingScrollViewports.ToArray())
        {
            var memory = pending.Memory;
            if (!Matches(memory.Element) || !Matches(memory.Anchor) || !IsDescendant(memory.Anchor.Id, memory.Element.Id) ||
                bindings[memory.Element.Id].Element is not ScrollViewer scroll || !IsMemoryVisible(scroll) ||
                (declarations[memory.Element.Id].Node.ScrollAxis ?? ScrollAxis.Vertical) != memory.Axis)
            { ReleaseScroll(pending); pendingScrollViewports.Remove(pending); continue; }
            var anchor = bindings[memory.Anchor.Id].LayoutElement;
            if (!anchor.IsLoaded || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0 || scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0) continue;
            if (pending.Scroll is null) { pending.Scroll = scroll; scroll.ViewChanged += MemoryScrollChanged; }
            var bounds = anchor.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            var horizontal = memory.Axis == ScrollAxis.Horizontal;
            var delta = (horizontal ? bounds.X : bounds.Y) + Math.Clamp(memory.ClippedFraction, -1, 1) * (horizontal ? bounds.Width : bounds.Height);
            var current = horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;
            var desired = Math.Clamp(current + delta, 0, horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight);
            if (Math.Abs(desired - current) <= 1 / (scroll.XamlRoot?.RasterizationScale ?? 1))
            { ReleaseScroll(pending); pendingScrollViewports.Remove(pending); }
            else if (pending.Offset != desired)
            {
                pending.Offset = desired;
                if (horizontal) scroll.ChangeView(desired, null, null, true);
                else scroll.ChangeView(null, desired, null, true);
            }
        }
        if (!HasPendingMemoryRestore) CancelMemoryRestoration();
    }

    private void CancelMemoryRestoration()
    {
        LayoutUpdated -= RestoreMemoryLayout;
        foreach (var pending in pendingIndexedViewports) ReleaseViewport(pending);
        foreach (var pending in pendingScrollViewports) ReleaseScroll(pending);
        pendingIndexedViewports.Clear(); pendingScrollViewports.Clear(); memoryOwner = null;
    }

    private void MemoryScrollChanged(object? sender, ScrollViewerViewChangedEventArgs args) => QueueMemoryRestoration();
    private void ReleaseScroll(PendingScroll pending)
    { if (pending.Scroll is { } scroll) scroll.ViewChanged -= MemoryScrollChanged; }

    private void ReleaseViewport(PendingViewport pending)
    {
        pending.Retention?.Dispose();
        if (pending.Collection is { } collection) collection.NavigationSettled -= QueueMemoryRestoration;
    }

    private void QueueMemoryRestoration()
    {
        if (memoryRestoreQueued || !HasPendingMemoryRestore) return;
        memoryRestoreQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => { memoryRestoreQueued = false; RestoreMemoryLayout(null, EventArgs.Empty); }))
            memoryRestoreQueued = false;
    }
}
