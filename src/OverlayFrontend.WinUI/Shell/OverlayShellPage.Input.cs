using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly RightStickScroll rightStick = new();
    private Browser.BrowserSurface? dpadBrowser;
    private static readonly (ushort Mask, ControllerButton Button)[] Buttons =
    [
        (0x1000, ControllerButton.A), (0x2000, ControllerButton.B),
        (0x4000, ControllerButton.X), (0x8000, ControllerButton.Y),
        (0x100, ControllerButton.LeftBumper), (0x200, ControllerButton.RightBumper),
        (0x40, ControllerButton.LeftStick), (0x80, ControllerButton.RightStick),
        (0x10, ControllerButton.Menu), (0x20, ControllerButton.View),
    ];

    internal void Receive(ControllerFrame frame)
    {
        var keyboard = ActiveControllerKeyboard;
        var delivered = false;
        try { ReceiveCore(frame, out delivered); }
        finally
        {
            if (!delivered) SampleBrowserDpad(null, frame);
            var current = ActiveControllerKeyboard;
            if (keyboard is not null)
            {
                if (delivered && (foreground || PinnedInputActive) && ReferenceEquals(keyboard, current))
                    keyboard.SampleHeldButtons(frame.State.Buttons, frame.PressedButtons, Environment.TickCount64);
                else keyboard.ResetRepeat();
            }
        }
    }

    private Presentation.WidgetTextEntryDialog? ActiveControllerKeyboard => expandedBrowser?.Surface?.ControllerTextEntry ??
        (PinnedInputActive ? pinned?.Popup?.Child.ControllerTextEntry ?? pinned?.Presenter?.ControllerTextEntry : interactive ? surface?.ControllerTextEntry : null);

    private void ReceiveCore(ControllerFrame frame, out bool delivered)
    {
        delivered = false;
        if (ReceiveWebAppEditor(frame) || ReceiveSystemFilePicker(frame) || ReceiveLocalInstall(frame)) return;
        if (ReceiveHostChoice(frame)) return;
        if (nativeBrowserPickerActive) { heldAction.Reset(); rightStick.Reset(); return; }
        if (ReceivePinnedPlacement(frame)) { heldAction.Reset(); return; }
        if (frame.Connected == 0) { trayHold.Reset(); shellOwnedReleases.Clear(); radialInput.Reset(); }
        if (retired || !visible || frame.Connected == 0 || frame.Primed != 0) { heldAction.Reset(); rightStick.Reset(); return; }
        if (startupPresentationPending)
        {
            foreach (var (mask, button) in Buttons)
            {
                if ((frame.PressedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Pressed);
                if ((frame.ReleasedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Released);
            }
            if (frame.LeftTriggerPressed != 0) _ = RouteButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
            if (frame.RightTriggerPressed != 0) _ = RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
            if (frame.LeftTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
            if (frame.RightTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
            heldAction.Reset(); rightStick.Reset(); return;
        }
        if (switching && interactive)
        {
            // Entry suppresses fresh input, but the gesture that started it must
            // still finish. Otherwise a short B/A tap leaves its ownership latch
            // set and every subsequent press of that button is discarded.
            foreach (var (mask, button) in Buttons)
                if ((frame.ReleasedButtons & mask) != 0 && shellOwnedReleases.Contains(button))
                    _ = RouteButtonAsync(button, ControllerEventPhase.Released);
            heldAction.Reset(); rightStick.Reset(); return;
        }
        if (trayHold.Capturing && (frame.State.Buttons & 0x8000) == 0 && (frame.ReleasedButtons & 0x8000) == 0)
        { trayHold.Reset(); shellOwnedReleases.Remove(ControllerButton.Y); }
        _ = RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, !reordering, Environment.TickCount64));
        Presentation.WidgetControllerPrompts.Set(frame.LastInputFamily);
        if (pinned is not null && (frame.State.Buttons & 0x300) == 0x300 && (frame.PressedButtons & 0x4000) != 0)
        { shellOwnedReleases.Add(ControllerButton.X); _ = UnpinAsync(save: true); return; }
        var radialNavigation = ReceiveRadialNavigation(frame);
        var pointerPresenter = PinnedInputActive ? pinned!.Presenter : interactive ? surface : null;
        var activeBrowser = radialNavigation ? null : expandedBrowser?.Surface?.ActiveSurface ??
            (PinnedInputActive && pinned?.Popup is { } activePopup ? activePopup.Child.ActiveSurface : pointerPresenter?.ControllerBrowser);
        SampleBrowserDpad(activeBrowser, frame);
        var browserPointer = !radialNavigation && (IsMediaFullscreen
            ? fullscreenLayer.MovePointer(frame.State.LeftThumbX, frame.State.LeftThumbY)
            : PinnedInputActive && pinned?.Popup is { } popup
                ? popup.Slot.MovePointer(frame.State.LeftThumbX, frame.State.LeftThumbY)
                : pointerPresenter?.MoveBrowserPointer(frame.State.LeftThumbX, frame.State.LeftThumbY) == true);
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : browserPointer ? default : frame.StickNavigation;
        var next = direction.Direction switch
        {
            NavigationDirection.Left => FocusNavigationDirection.Left,
            NavigationDirection.Right => FocusNavigationDirection.Right,
            NavigationDirection.Up => FocusNavigationDirection.Up,
            NavigationDirection.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        if (!radialNavigation && direction.Phase != NavigationPhase.None && next != FocusNavigationDirection.None)
        {
            if (IsMediaFullscreen) fullscreenLayer.MoveFocus(next);
            else if (PinnedInputActive)
            {
                if (pinned!.Presenter is { } widget) widget.MoveFocus(next, direction.Phase == NavigationPhase.Repeated);
                else if (pinned.MediaView is { } media) media.MoveFocus(next);
                else pinned.Popup?.Slot.MoveFocus(next);
            }

            else if (interactive && RecoveryVisible) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = StatusChrome });
            else if (interactive && surface is { } focusedSurface)
            {
                rightStick.Reset();
                if (!focusedSurface.MoveFocus(next, direction.Phase == NavigationPhase.Repeated)) LeaveWidgetDirectionalBoundary(focusedSurface, next);
            }
            else if (!NavigateTray(next)) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = Tray });
        }
        // Images own right-stick panning only while their canvas has focus.
        // Left-stick/D-pad navigation remains available even when zoomed in.
        var imagePan = !radialNavigation && radialInput.AllowScroll && (IsMediaFullscreen
            ? fullscreenLayer.MoveAnalog(frame.State.RightThumbX, frame.State.RightThumbY)
            : pointerPresenter?.PanMediaImage(frame.State.RightThumbX, frame.State.RightThumbY) == true);
        if (!imagePan && radialInput.AllowScroll && !IsMediaFullscreen && PinnedInputActive && pinned?.Popup is { } scrollingPopup)
        {
            var delta = rightStick.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
            if (delta.X != 0 || delta.Y != 0) scrollingPopup.Slot.Scroll(delta.X, delta.Y);
        }
        else if (!imagePan && radialInput.AllowScroll && !IsMediaFullscreen &&
            (PinnedInputActive ? pinned!.Presenter : interactive ? surface : null) is { } current)
        {
            var now = Environment.TickCount64;
            var delta = rightStick.Sample(frame.State.RightThumbX, frame.State.RightThumbY, now);
            if (delta.X != 0 || delta.Y != 0) current.ScrollBy(delta.X, delta.Y);
            else if (rightStick.ShouldSettle(now) && current.SettleScrollFocus()) rightStick.Reset();
        }
        else rightStick.Reset();
        foreach (var (mask, button) in Buttons)
        {
            if ((frame.PressedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Pressed);
            if ((frame.ReleasedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Released);
        }
        if (frame.LeftTriggerPressed != 0) BeginTrigger(ControllerButton.LeftTrigger);
        if (frame.LeftTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
        if (frame.RightTriggerPressed != 0) BeginTrigger(ControllerButton.RightTrigger);
        if (frame.RightTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
        PumpHeldAction(frame);
        delivered = true;
    }

    private void SampleBrowserDpad(Browser.BrowserSurface? browser, ControllerFrame frame)
    {
        if (browser is { IsBrowsing: false } or { HasDialog: true } or { IsToolbarFocused: true }) browser = null;
        if (!ReferenceEquals(dpadBrowser, browser))
        {
            dpadBrowser?.CancelDpadHold();
            dpadBrowser = browser;
        }
        // Native navigation events have Pressed/Repeated but no Released phase.
        // State bits remain set between repeats, including diagonal transitions.
        // The event fallback supports synthetic controller-frame validation.
        browser?.SampleDpadHold((frame.State.Buttons & 0x000f) != 0 || frame.DpadNavigation.Phase != NavigationPhase.None);
    }

    private void FocusDirectionalTray()
    {
        // Native OverlayState distinguishes Directional entry from Back entry:
        // the bottom edge always enters the rail, even with a radial switcher.
        radialRequested = false;
        SetInteractive(false);
        RefreshRadialChooser();
        FocusTray();
    }

    private async Task RouteButtonAsync(ControllerButton button, ControllerEventPhase phase,
        ControllerInputOrigin origin = ControllerInputOrigin.PhysicalController)
    {
        // A host-owned gesture stays owned until release, even if it moved focus
        // between the pin and main window. A queued duplicate press must not
        // become a fresh Back/View action in the newly active surface.
        if (phase == ControllerEventPhase.Pressed && shellOwnedReleases.Contains(button)) return;
        if (phase == ControllerEventPhase.Released && shellOwnedReleases.Remove(button))
        {
            if (button == ControllerButton.Y)
                await RunTrayHoldAsync(trayHold.Release(TrayGestureIdentity, !reordering, Environment.TickCount64));
            return;
        }
        if (retired || !visible) return;
        if (webAppEditor is { } editor) { editor.Handle(button, phase); return; }
        if (SystemFilePickerOpen) return;
        if (nativeBrowserPickerActive) return;
        if (LocalInstallActive) { localInstallDialog?.Handle(button, phase); return; }
        if (hostChoiceDialog is { } dialog)
        { HandleHostChoiceButton(dialog, button, phase); return; }
        if (ReceiveStartupInput(button, phase) || switching && interactive) return;
        try
        {
            if (await RoutePinnedPlacementButtonAsync(button, phase)) return;
            if (RouteFullscreenButton(button, phase)) return;
            if (pinned is { } pin && !IsMediaFullscreen)
            {
                if (button == ControllerButton.View && phase == ControllerEventPhase.Pressed)
                {
                    shellOwnedReleases.Add(button);
                    if (PinnedInputActive) ExitPinnedInteraction(restoreMain: true);
                    else await EnterPinnedAsync();
                    return;
                }
                if (PinnedInputActive)
                {
                    var accepted = pin.Popup is { } popup ? popup.Slot.HandleButton(button, phase) : pin.Media is not null ? RouteCompactButton(pin, button, phase) :
                        await pin.Presenter!.HandleControllerButtonAsync(button, phase, origin, cancellationToken: lifetime.Token);
                    if (!accepted && button == ControllerButton.B && phase == ControllerEventPhase.Pressed && ReferenceEquals(pinned, pin))
                    { shellOwnedReleases.Add(button); ExitPinnedInteraction(restoreMain: true); }
                    return;
                }
            }

            if (!interactive)
            {
                await RouteTrayButtonAsync(button, phase, origin);
                return;
            }
            if (RecoveryVisible)
            {
                if (phase == ControllerEventPhase.Pressed && (button == ControllerButton.A || showingStoppedWidget && button == ControllerButton.X) && Retry.Visibility == Visibility.Visible)
                { shellOwnedReleases.Add(button); await RetryPresentationAsync(); }
                else if (phase == ControllerEventPhase.Pressed && button == ControllerButton.B)
                { shellOwnedReleases.Add(button); PrepareRadialBackEntry(); SetInteractive(false); FocusTray(); }
                return;
            }
            if (surface is null) return;
            var target = surface;
            var version = selectionVersion;
            var handled = await target.HandleControllerButtonAsync(button, phase, origin, cancellationToken: lifetime.Token);
            if (!handled && button == ControllerButton.B && phase == ControllerEventPhase.Pressed &&
                !retired && visible && version == selectionVersion && ReferenceEquals(target, surface))
            {
                shellOwnedReleases.Add(button);
                PrepareRadialBackEntry();
                SetInteractive(false);
                ResetInputPresentation();
                FocusTray();
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void LeaveWidgetDirectionalBoundary(Presentation.WidgetViewPresenter presenter, FocusNavigationDirection direction)
    {
        if (!retired && visible && interactive && ReferenceEquals(surface, presenter) && !PinnedInputActive &&
            !IsMediaFullscreen && direction == FocusNavigationDirection.Down && presenter.CanLeaveRootScope)
            FocusDirectionalTray();
    }
}
