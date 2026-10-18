using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        var presenter = pin is null ? surface : pin.Presenter;
        var focus = FocusManager.GetFocusedElement(root) as Control;
        SystemFilePickerOpen = true;
        presenter?.SetAutomaticFocusEnabled(false);
        presenter?.SetPresentationInputEnabled(false);
        pin?.SetInput(false);
        shellGestures.Claim(ControllerButton.A);
        ResetInputPresentation(); PublishControllerGuide();
        try
        {
            return await BrowserFilePicker.PickAsync(root.ContentIslandEnvironment.AppWindowId, request, token);
        }
        finally
        {
            SystemFilePickerOpen = false;
            ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && (pin is null ? foreground && ReferenceEquals(presenter, surface) : ReferenceEquals(pin, pinned) && PinnedInputActive))
            {
                presenter?.SetPresentationInputEnabled(pin is null ? interactive && !switching && activeWidget == requestedWidget : PinnedInputActive);
                presenter?.SetAutomaticFocusEnabled(pin is null ? MainFocusEnabled : PinnedInputActive);
                pin?.SetInput(PinnedInputActive);
                if (focus is { IsLoaded: true, IsEnabled: true }) focus.Focus(FocusState.Keyboard);
                else if (pin is null) QueueEntryFocus();
            }
        }
    }

    private bool ReceiveSystemFilePicker(ControllerFrame frame)
    {
        if (!SystemFilePickerOpen) return false;
        heldAction.Reset(); rightStick.Reset();
        if (frame.Connected == 0 || frame.Primed != 0) { shellGestures.Reset(); return true; }
        shellGestures.CaptureFrame(frame);
        shellGestures.ReconcileReleasedState(frame.State);
        return true;
    }
}
