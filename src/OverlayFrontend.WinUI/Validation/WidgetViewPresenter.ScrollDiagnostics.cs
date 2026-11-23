namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal Action<double, double>? ScrollRequestTrace { get; set; }
    internal string? ScrollOwnerForValidation => scrollGesture?.Owner.Identity.Id;
    partial void TraceScrollRequest(double horizontal, double vertical) => ScrollRequestTrace?.Invoke(horizontal, vertical);
}
