namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal Action<double, double>? ScrollRequestTrace { get; set; }
    partial void TraceScrollRequest(double horizontal, double vertical) => ScrollRequestTrace?.Invoke(horizontal, vertical);
}
