namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    internal bool HasPendingPreviewPublicationForValidation => previewSource?.Row is { Lease.IsCurrent: false } row && row.Owner.HasPendingPresentation;
    internal void RefreshPreviewForValidation() => UpdateFocusPreview();
}
