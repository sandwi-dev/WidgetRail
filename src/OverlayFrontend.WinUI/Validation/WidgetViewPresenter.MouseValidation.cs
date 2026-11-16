namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal async Task QueueHoverExitForValidation()
    {
        SchedulePreviewPointerExit();
        await Task.Delay(250);
    }
}
