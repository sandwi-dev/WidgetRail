using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // Informational display readiness only. This deliberately reads local row
    // identity, not the cross-thread session authority used for input admission.
    internal bool HasCurrentGuideRows(FrameworkElement guideRoot, Rect clip)
    {
        if (view is null || source is null || !view.IsLoaded) return false;
        if (source.Items.Count == 0) return true;
        if (FindNativeScroll(view) is not { ViewportHeight: > 0, ViewportWidth: > 0 } scroll) return false;
        clip = GuideIntersection(clip, scroll.TransformToVisual(guideRoot)
            .TransformBounds(new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight)));
        if (clip.Width == 0 || clip.Height == 0) return true;
        var any = false;
        var expected = source.Declaration.IndexedCollection!;
        foreach (var (container, item) in containers)
        {
            if (!container.IsLoaded || container.ActualWidth <= 0 || container.ActualHeight <= 0) continue;
            var bounds = container.TransformToVisual(guideRoot).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (GuideIntersection(bounds, clip) is { Width: 0 } or { Height: 0 }) continue;
            any = true;
            if (item.Slot.Failed) continue; // A terminal unavailable row is a settled state.
            if (item.Slot.Value is not { } row || !ReferenceEquals(row.Owner, source)) return false;
            var actual = row.Lease.Range.Source;
            if (actual.SourceId != expected.SourceId || actual.QueryGeneration != expected.QueryGeneration ||
                actual.ContentRevision != expected.ContentRevision) return false;
            // Busy rows remain focusable; command availability is carried by
            // their current snapshot, not by disabling the native container.
            var enabled = view.IsEnabled && row.Item.Root is not { IsDisabled: true };
            if (container.IsEnabled != enabled) return false;
        }
        return any;
    }

    internal static Rect GuideIntersection(Rect first, Rect second)
    {
        var x = Math.Max(first.X, second.X); var y = Math.Max(first.Y, second.Y);
        return new(x, y, Math.Max(0, Math.Min(first.Right, second.Right) - x), Math.Max(0, Math.Min(first.Bottom, second.Bottom) - y));
    }
}
