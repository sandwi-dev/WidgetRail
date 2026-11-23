namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetPackageIconView
{
    internal (bool Required, bool Pending, bool Ready, string? Label) CapturePreparation => icon.CapturePreparation;
}

internal sealed partial class WidgetNativePackageIcon
{
    // Validation-only observation: no retries, resource admission or rendering changes.
    internal (bool Required, bool Pending, bool Ready, string? Label) CapturePreparation => (retry is not null || succeeded, loading, succeeded, accessibleName);
}
