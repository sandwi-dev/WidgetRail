namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal bool ControllerPointerHeldForValidation=>pointerPressed||nativePointerDown;
    internal long ControllerPointerEpochForValidation=>pointerEpoch;
}
