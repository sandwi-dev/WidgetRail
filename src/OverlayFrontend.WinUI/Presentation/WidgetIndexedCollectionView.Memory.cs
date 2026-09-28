using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private sealed class ViewportRetention(IndexedItemsSource<WidgetIndexedRow>.Retention retention,
        IndexedItem<WidgetIndexedRow>? measurement, ScrollViewer? scroll, Action changed) : IDisposable
    {
        internal void Subscribe()
        {
            retention.Slot.PropertyChanged += Changed;
            if (measurement is not null && !ReferenceEquals(measurement, retention.Slot)) measurement.PropertyChanged += Changed;
            if (scroll is not null) scroll.ViewChanged += ViewChanged;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => changed();
        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => changed();
        public void Dispose()
        {
            retention.Slot.PropertyChanged -= Changed;
            if (measurement is not null && !ReferenceEquals(measurement, retention.Slot)) measurement.PropertyChanged -= Changed;
            if (scroll is not null) scroll.ViewChanged -= ViewChanged;
            retention.Dispose();
        }
    }
    internal IDisposable? RetainViewport(IndexedViewportMemento memory, Action changed)
    {
        if (source is null || source.Declaration.IndexedCollection is not { } query || query.SourceId != memory.Source ||
            query.QueryGeneration != memory.Query || memory.Index < 0 || memory.Index >= query.Count) return null;
        var retention = new ViewportRetention(source.Items.Retain(memory.Index), measurementRetention?.Slot,
            view is null ? null : FindNativeScroll(view), changed);
        retention.Subscribe(); return retention;
    }
    internal IndexedViewportMemento? CaptureViewport(WidgetElementIdentity identity)
    {
        if (source is null || view is null || !view.IsLoaded || FindNativeScroll(view) is not { } scroll) return null;
        var horizontal = axis == ScrollAxis.Horizontal;
        var extent = horizontal ? scroll.ViewportWidth : scroll.ViewportHeight;
        var candidates = containers.Select(pair => (Container: pair.Key, Slot: pair.Value.Slot))
            .Where(pair => pair.Container.IsLoaded && pair.Slot.Key is not null)
            .Select(pair => (pair.Slot, Bounds: pair.Container.TransformToVisual(scroll).TransformBounds(
                new Rect(0, 0, pair.Container.ActualWidth, pair.Container.ActualHeight))))
            .Where(pair => (horizontal ? pair.Bounds.Right : pair.Bounds.Bottom) > 0 &&
                (horizontal ? pair.Bounds.X : pair.Bounds.Y) < extent)
            .OrderBy(pair => horizontal ? pair.Bounds.X : pair.Bounds.Y).ThenBy(pair => pair.Slot.Index).ToArray();
        if (candidates.Length == 0) return null;
        var first = candidates[0];
        var size = horizontal ? first.Bounds.Width : first.Bounds.Height;
        var position = horizontal ? first.Bounds.X : first.Bounds.Y;
        var query = source.Declaration.IndexedCollection!;
        return new(identity, query.SourceId, query.QueryGeneration, layoutKind!.Value, axis!.Value,
            first.Slot.Index, first.Slot.Key!, size > 0 ? Math.Clamp(-position / size, -1, 1) : 0);
    }

    // true means completed or no longer applicable; false waits for native readiness.
    internal bool RestoreViewport(IndexedViewportMemento memory, ref bool requested, ref double? pendingOffset)
    {
        if (source is null || view is null || source.Declaration.IndexedCollection is not { } query ||
            query.SourceId != memory.Source || query.QueryGeneration != memory.Query ||
            layoutKind != memory.Layout || axis != memory.Axis || memory.Index < 0 || memory.Index >= query.Count) return true;
        if (!presentationActive || !view.IsLoaded || IsEntryPending || FindNativeScroll(view) is not { } scroll ||
            scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0) return false;
        if (measurementRetention is { Slot.Failed: false } measure && measure.Slot.Value?.Lease.IsCurrent != true) return false;
        var slot = (IndexedItem<WidgetIndexedRow>)source.Items[memory.Index];
        if (slot.Failed) return true;
        if (slot.Value is not { Lease.IsCurrent: true }) return false;
        if (slot.Key != memory.Key) return true;
        if (view.ContainerFromIndex(memory.Index) is not FrameworkElement { IsLoaded: true } container)
        {
            if (!requested) { requested = true; view.ScrollIntoView(source.Items[memory.Index], ScrollIntoViewAlignment.Leading); }
            return false;
        }
        var bounds = container.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        var horizontal = axis == ScrollAxis.Horizontal;
        var size = horizontal ? bounds.Width : bounds.Height;
        var position = horizontal ? bounds.X : bounds.Y;
        var delta = position + Math.Clamp(memory.ClippedFraction, -1, 1) * size;
        var current = horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;
        var desired = Math.Clamp(current + delta, 0, horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight);
        if (Math.Abs(desired - current) <= 1 / (view.XamlRoot?.RasterizationScale ?? 1)) return true;
        if (pendingOffset != desired)
        {
            pendingOffset = desired;
            if (horizontal) scroll.ChangeView(desired, null, null, true);
            else scroll.ChangeView(null, desired, null, true);
        }
        return false; // Confirm on native ViewChanged/layout; never assume ChangeView is synchronous.
    }
}
