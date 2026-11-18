namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    internal object NavigationMemoryForValidation() => new { entryIntentVersion, memoryIntent, memoryOwner, pageMemoryVersion,
        focusQueued, needsEntry, pendingRestore = pendingRestore?.Target.Identity.Id, pendingGroup = pendingGroupEntry?.RequestId,
        focused = FocusedBinding()?.Identity.Id,
        pages = pageMemory.Select(pair => new { pair.Key, pair.Value }).ToArray(),
        scrolls = pendingScrollViewports.Select(pending => new { pending.Memory, pending.Offset, current = pending.Scroll?.VerticalOffset }).ToArray() };
    internal bool HasPendingPreviewPublicationForValidation => previewSource?.Row is { Lease.IsCurrent: false } row && row.Owner.HasPendingPresentation;
    internal bool HasPendingPrimaryPreviewForValidation => pendingPrimaryPreview is not null;
    internal bool HasPendingDeferredPreviewReadForValidation => deferredPreviewRead is not null;
    internal System.Collections.Generic.IReadOnlyList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase> PreviewMenuItemsForValidation =>
        previewSurface?.Fragment?.selectPopup?.Items ?? Array.Empty<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase>();
    internal bool IsPreviewEnteredForValidation => previewEntered;
    internal object PreviewSelectReadinessForValidation(string id)
    {
        var fragment = previewSurface?.Fragment;
        var binding = fragment?.bindings.GetValueOrDefault(id);
        return new { childExists = fragment is not null, childApplying = fragment?.applying,
            inputEnabled = fragment?.presentationInputEnabled, active = fragment?.presentationActive,
            current = fragment is not null && PreviewInputCurrent(fragment), popup = fragment?.selectPopup is not null,
            loaded = binding?.Element.IsLoaded, width = binding?.Element.ActualWidth, height = binding?.Element.ActualHeight,
            eligible = binding is not null && fragment!.Eligible(binding),
            sameRoot = ReferenceEquals(XamlRoot, binding?.Element.XamlRoot) };
    }
    internal void RefreshPreviewForValidation() => UpdateFocusPreview();
}
