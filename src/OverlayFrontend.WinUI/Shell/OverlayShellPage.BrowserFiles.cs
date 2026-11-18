using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task<string[]> ShowBrowserFilePickerAsync(BrowserSurface browser, BrowserFileRequest request, CancellationToken token)
    {
        if (retired || !visible || SystemFilePickerOpen || HostChoiceActive || LocalInstallActive || !browser.CanCompleteSiteDecision || browser.XamlRoot is not { } root) return [];
        var pin = pinned is { } current && ReferenceEquals(root, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (pin is null && !ReferenceEquals(root, XamlRoot)) return [];
        return await WithNativeFilePickerAsync(root, pin,
            cancellation => BrowserFilePicker.PickAsync(root.ContentIslandEnvironment.AppWindowId, request, cancellation), token);
    }

    private bool ReceiveSystemFilePicker(ControllerFrame frame)
    {
        if (!SystemFilePickerOpen) return false;
        heldAction.Reset(); rightStick.Reset();
        if (frame.Connected == 0 || frame.Primed != 0) { shellGestures.Reset(); return true; }
        shellGestures.CaptureFrame(frame);
        shellGestures.ReconcileReleasedState(frame);
        return true;
    }
}
