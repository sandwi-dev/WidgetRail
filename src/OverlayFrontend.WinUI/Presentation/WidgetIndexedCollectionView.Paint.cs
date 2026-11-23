using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // Scope changes recreate controls and revoke their input. They need not
    // erase resident presentation of the same ordered query. Seed before the
    // previous native view retires; every authoritative range is reacquired by
    // the new source through normal demand. The old row owner is never relabeled.
    internal void SeedSameQueryPaint(WidgetIndexedCollectionView previous,
        WidgetPresentationBinding binding, ViewNode declaration)
    {
        if (disposed || source is not null || previous.disposed || previous.source is not { } prior ||
            !ReferenceEquals(session, previous.session) || !prior.Presentation.SameSurface(binding) ||
            prior.ActiveScope == binding.Scope || prior.Frame.Authority.WorkerRun != binding.Frame.Authority.WorkerRun ||
            prior.Frame.Descriptor.PackageContentDigest != binding.Frame.Descriptor.PackageContentDigest ||
            prior.Declaration.Id != declaration.Id || prior.Declaration.IndexedGroups is not null || declaration.IndexedGroups is not null ||
            prior.Declaration.IndexedCollection is not { Discovery: null } old ||
            declaration.IndexedCollection is not { Discovery: null } next || next.SourceId != old.SourceId ||
            next.QueryGeneration != old.QueryGeneration || next.Count != old.Count || next.ContentRevision < old.ContentRevision ||
            prior.Declaration.CollectionLayout?.Kind != declaration.CollectionLayout?.Kind || prior.Declaration.ScrollAxis != declaration.ScrollAxis)
            return;
        source = new WidgetIndexedRows(session, binding, declaration, DispatcherQueue) { Failed = failed };
        var artwork = new Dictionary<WidgetIndexedRow, IReadOnlyList<WidgetStaticArtworkPaint>>(ReferenceEqualityComparer.Instance);
        foreach (var item in previous.containers.Values)
            if (item.Slot.Value is { } row && item.LayoutOwner?.CaptureStaticArtworkPaint(row) is { Count: > 0 } paint)
                artwork[row] = paint;
        source.Items.SeedSameQueryPresentation(prior.Items, row => artwork.TryGetValue(row, out var paint)
            ? row with { RetainedArtwork = paint } : row);
        source.DiscoveryChanged += UpdateDiscoveryFooter;
        source.StylesChanged += IndexedStylesChanged;
        if (!presentationActive) TrackRetirement(source.SetPresentationActiveAsync(false));
    }
}
