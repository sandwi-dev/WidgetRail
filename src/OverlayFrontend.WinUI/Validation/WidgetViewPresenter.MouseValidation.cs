namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal Task QueueHoverExitForValidation() => DismissPreviewAfterPointerExitAsync(++previewExitRevision);
}
