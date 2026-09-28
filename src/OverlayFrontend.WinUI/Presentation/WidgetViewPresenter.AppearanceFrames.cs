namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private void ApplyAppearanceFrame(WidgetPresentationBinding next)
    {
        // This path admits only a new trusted style revision of the same exact
        // declaration snapshot. Preserve structural focus restoration, pending
        // group entry, media state and collection/provider demand.
        applying = true;
        try
        {
            frame = next.Frame;
            presentation = next;
            UpdateResponsiveVisibility();
            foreach (var declaration in declarations.Values)
            {
                var binding = bindings[declaration.Node.Id];
                if (binding.Element is WidgetIndexedCollectionView collection) collection.RefreshAppearanceBinding(next);
                UpdateContainerLayout(binding, declaration.Node);
                if (binding.Children is WidgetPosterPanel poster) UpdatePoster(poster, declaration.Node);
                ApplySizeAndTypography(binding.Element, declaration.Node);
                ApplyComputedStyles(binding, declaration.Node);
                ApplyMotionGeometry(binding);
            }
            UpdateModalGeometry();
            UpdateNativeNeighbors();
        }
        finally { applying = false; }
        // Geometry-changing themes still invalidate native measure/arrange via
        // their property setters; this is not a paint-only optimization.
        if (needsEntry || pendingRestore is not null || pendingGroupEntry is not null) QueueEntryFocus();
        QueueSurfaceUpdate();
    }
}
