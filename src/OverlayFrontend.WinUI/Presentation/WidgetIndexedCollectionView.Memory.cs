using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private sealed class ViewportRetention(IndexedItemsSource<WidgetIndexedRow>.Retention retention,
        IndexedItem<WidgetIndexedRow>? measurement, ScrollViewer? scroll, Action changed, Action? scrollSettled) : IDisposable
    {
        internal void Subscribe()
        {
            retention.Slot.PropertyChanged += Changed;
            if (measurement is not null && !ReferenceEquals(measurement, retention.Slot)) measurement.PropertyChanged += Changed;
            if (scroll is not null) scroll.ViewChanged += ViewChanged;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => changed();
        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            if (!args.IsIntermediate) scrollSettled?.Invoke();
            changed();
        }
        public void Dispose()
        {
            retention.Slot.PropertyChanged -= Changed;
            if (measurement is not null && !ReferenceEquals(measurement, retention.Slot)) measurement.PropertyChanged -= Changed;
            if (scroll is not null) scroll.ViewChanged -= ViewChanged;
            retention.Dispose();
        }
    }
    internal IDisposable? RetainViewport(IndexedViewportMemento memory, Action changed, Action? scrollSettled = null)
    {
        if (source is null || source.Declaration.IndexedCollection is not { } query || query.SourceId != memory.Source ||
            query.QueryGeneration != memory.Query || memory.Index < 0 || memory.Index >= query.Count) return null;
        var retention = new ViewportRetention(source.Items.Retain(memory.Index), measurementRetention?.Slot,
            view is null ? null : FindNativeScroll(view), changed, scrollSettled);
        retention.Subscribe(); return retention;
    }
    internal IndexedViewportMemento? CaptureViewport(WidgetElementIdentity identity, bool preferFocused = false)
    {
        if (source is null || view is null || !view.IsLoaded || FindNativeScroll(view) is not { } scroll) return null;
        var horizontal = axis == ScrollAxis.Horizontal;
        var visible = WidgetScrollViewport.ContentBounds(scroll);
        var start = horizontal ? visible.Left : visible.Top;
        var end = horizontal ? visible.Right : visible.Bottom;
        var candidates = containers.Select(pair => (Container: pair.Key, Slot: pair.Value.Slot))
            .Where(pair => pair.Container.IsLoaded && pair.Slot.Key is not null)
            .Select(pair => (pair.Slot, Bounds: LayoutBoundsInScroll(pair.Container, scroll)))
            .Where(pair => (horizontal ? pair.Bounds.Right : pair.Bounds.Bottom) > start &&
                (horizontal ? pair.Bounds.X : pair.Bounds.Y) < end)
            .OrderBy(pair => horizontal ? pair.Bounds.X : pair.Bounds.Y).ThenBy(pair => pair.Slot.Index).ToArray();
        if (candidates.Length == 0) return null;
        var first = preferFocused && FocusedIndex() is { } focus && candidates.Any(pair => pair.Slot.Index == focus)
            ? candidates.First(pair => pair.Slot.Index == focus) : candidates[0];
        var size = horizontal ? first.Bounds.Width : first.Bounds.Height;
        var position = (horizontal ? first.Bounds.X : first.Bounds.Y) - start;
        var query = source.Declaration.IndexedCollection!;
        return new(identity, query.SourceId, query.QueryGeneration, layoutKind!.Value, axis!.Value,
            first.Slot.Index, first.Slot.Key!, size > 0 ? Math.Clamp(-position / size, -1, 1) : 0, preferFocused ? position : null);
    }

    // true means completed or no longer applicable; false waits for native readiness.
    internal bool RestoreViewport(IndexedViewportMemento memory, ref bool requested, ref double? pendingOffset, bool allowRetainedPixels = false, bool keepAnchorVisible = false)
    {
        if (source is null || view is null || source.Declaration.IndexedCollection is not { } query ||
            query.SourceId != memory.Source || query.QueryGeneration != memory.Query ||
            layoutKind != memory.Layout || axis != memory.Axis || memory.Index < 0 || memory.Index >= query.Count) return true;
        if (!presentationActive || !view.IsLoaded || entering || FindNativeScroll(view) is not { } scroll ||
            scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0) return false;
        if (measurementRetention is { Slot.Failed: false } measure && measure.Slot.Value?.Lease.IsCurrent != true) return false;
        var slot = (IndexedItem<WidgetIndexedRow>)source.Items[memory.Index];
        if (slot.Failed) return true;
        if (slot.Value is null || !allowRetainedPixels && !slot.Value.Lease.IsCurrent) return false;
        if (slot.Key != memory.Key) return true;
        if (view.ContainerFromIndex(memory.Index) is not FrameworkElement { IsLoaded: true } container)
        {
            if (!requested) { requested = true; view.ScrollIntoView(source.Items[memory.Index], ScrollIntoViewAlignment.Leading); }
            return false;
        }
        var bounds = LayoutBoundsInScroll(container, scroll);
        var horizontal = axis == ScrollAxis.Horizontal;
        var size = horizontal ? bounds.Width : bounds.Height;
        var visible = WidgetScrollViewport.ContentBounds(scroll);
        var position = horizontal ? bounds.X - visible.X : bounds.Y - visible.Y;
        var desiredPosition = memory.LeadingPosition ?? -Math.Clamp(memory.ClippedFraction, -1, 1) * size;
        if (keepAnchorVisible) desiredPosition = Math.Clamp(desiredPosition, 0, Math.Max(0, (horizontal ? visible.Width : visible.Height) - size));
        var delta = position - desiredPosition;
        var current = horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;
        var desired = Math.Clamp(current + delta, 0, horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight);
        if (Math.Abs(desired - current) <= 1 / (view.XamlRoot?.RasterizationScale ?? 1))
        {
            // An applied request is no longer pending. Native BringIntoView may
            // subsequently move the viewport while a replacement lease loads;
            // the same target must then be eligible for reconciliation again.
            pendingOffset = null;
            return true;
        }
        TraceViewportRestore(memory.Index, current, position, size, desiredPosition, desired, pendingOffset, null);
        if (pendingOffset != desired)
        {
            pendingOffset = desired;
            var accepted = horizontal ? scroll.ChangeView(desired, null, null, true) : scroll.ChangeView(null, desired, null, true);
            TraceViewportRestore(memory.Index, current, position, size, desiredPosition, desired, pendingOffset, accepted);
            if (!accepted) pendingOffset = null;
        }
        return false; // Confirm on native ViewChanged/layout; never assume ChangeView is synchronous.
    }
    partial void TraceViewportRestore(int index, double current, double position, double size, double desiredPosition, double desired, double? pendingOffset, bool? accepted);

    private static Rect LayoutBoundsInScroll(FrameworkElement container, ScrollViewer scroll)
    {
        // Focus/press scaling is paint, not scroll position. Measuring the animated
        // container itself captures an intermediate scale and accumulates drift on
        // each Back. Map its arranged box through its parent instead.
        var offset = container.ActualOffset;
        return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(container) is UIElement parent
            ? parent.TransformToVisual(scroll).TransformBounds(new Rect(offset.X, offset.Y, container.ActualWidth, container.ActualHeight))
            : container.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
    }
}
