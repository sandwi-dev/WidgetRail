namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>Resolves only artwork declared by this displayed and still-selected pinned projection.</summary>
    public Task<WidgetPresentationArtwork> ResolvePinnedArtworkAsync(WidgetPinnedSelection selection,
        WidgetPinnedProjection projection, string artworkHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(projection);
        return ResolveArtworkCoreAsync(projection.Frame.Authority, artworkHandle, cancellationToken, selection, projection);
    }

    private void DemandPinnedArtworkLocked(WidgetPinnedSelection selection, WidgetPinnedProjection projection, string handle)
    {
        var current = DemandPinnedProjectionLocked(projection);
        if (!IsPinnedSelectionCurrent(selection) || selection.WidgetId != current.Authority.WidgetId ||
            selection.LayoutId != projection.LayoutId || !ReferenceEquals(selection.Epoch, projection.Epoch))
            throw PinnedStale("Pinned artwork requires the current selected layout.");
        var currentView = PinnedSnapshot(current, projection.LayoutId).Snapshot;
        if (projection.Snapshot.ActiveInputScopeId != currentView.ActiveInputScopeId ||
            !ContainsArtwork(projection.Snapshot.Root, handle) || !ContainsArtwork(currentView.Root, handle))
            throw new WidgetPresentationSessionException("unknown_artwork", "The pinned artwork scope or handle retired.");
    }

    private void RetirePinnedArtworkLocked(string? widget = null, string? layout = null)
    {
        foreach (var pair in _artwork.ToArray())
            if (pair.Value.PinnedSelection is { } selection && (widget is null || widget == selection.WidgetId) &&
                (layout is null || layout == selection.LayoutId))
            {
                _artwork.Remove(pair.Key);
                pair.Value.Completion.TrySetException(PinnedStale("The pinned artwork selection retired."));
            }
    }
    private void ReconcilePinnedArtworkLocked(string widget)
    {
        foreach (var pair in _artwork.ToArray())
        {
            if (pair.Value.PinnedSelection is not { } selection || selection.WidgetId != widget ||
                pair.Value.PinnedProjection is not { } projection) continue;
            try { DemandPinnedArtworkLocked(selection, projection, pair.Value.ArtworkHandle); }
            catch (WidgetPresentationSessionException error)
            { _artwork.Remove(pair.Key); pair.Value.Completion.TrySetException(error); }
        }
    }
}
