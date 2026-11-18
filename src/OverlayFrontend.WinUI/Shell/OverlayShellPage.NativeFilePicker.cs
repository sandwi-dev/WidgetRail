using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    /// <summary>One input/focus lifetime for browser and widget native pickers.
    /// Callers retain their own document/widget authority and cancellation policy.</summary>
    private async Task<T> WithNativeFilePickerAsync<T>(XamlRoot root, PinnedSurface? pin,
        Func<CancellationToken, Task<T>> choose, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Broker admission may have awaited IPC while a different host dialog
        // opened. Acquire native picker ownership only after that await finishes.
        if (retired || !visible || SystemFilePickerOpen || HostChoiceActive || LocalInstallActive ||
            (pin is null ? !ReferenceEquals(root, XamlRoot) : !ReferenceEquals(pin, pinned) ||
                !ReferenceEquals(root, pin.Window.AutomationRoot.XamlRoot) || !pin.IsCurrent || !PinnedInputActive))
            throw new OperationCanceledException();
        var presenter = pin is null ? surface : pin.Presenter;
        var focus = FocusManager.GetFocusedElement(root) as Control;
        if (pin is not null) ActivatePinForHostDialog(pin);
        SystemFilePickerOpen = true;
        presenter?.SetAutomaticFocusEnabled(false);
        presenter?.SetPresentationInputEnabled(false);
        pin?.SetInput(false);
        shellGestures.Claim(ControllerButton.A);
        ResetInputPresentation(); PublishControllerGuide();
        try { return await choose(token); }
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
}
