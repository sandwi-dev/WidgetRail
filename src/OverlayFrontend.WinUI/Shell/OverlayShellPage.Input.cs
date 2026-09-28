using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly RightStickScroll rightStick = new();
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
        if (frame.Connected == 0) { trayHold.Reset(); shellOwnedReleases.Clear(); }
        if (retired || !visible || switching && interactive || frame.Connected == 0) { rightStick.Reset(); return; }
        if (trayHold.Capturing && (frame.State.Buttons & 0x8000) == 0 && (frame.ReleasedButtons & 0x8000) == 0)
        { trayHold.Reset(); shellOwnedReleases.Remove(ControllerButton.Y); }
        _ = RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, !reordering, Environment.TickCount64));
        Presentation.WidgetControllerPrompts.Set(frame.LastInputFamily);
        if (pinned is not null && (frame.State.Buttons & 0x300) == 0x300 && (frame.PressedButtons & 0x4000) != 0)
        { shellOwnedReleases.Add(ControllerButton.X); _ = UnpinAsync(save: true); return; }
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var next = direction.Direction switch
        {
            NavigationDirection.Left => FocusNavigationDirection.Left,
            NavigationDirection.Right => FocusNavigationDirection.Right,
            NavigationDirection.Up => FocusNavigationDirection.Up,
            NavigationDirection.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        if (direction.Phase != NavigationPhase.None && next != FocusNavigationDirection.None)
        {
            if (PinnedInputActive) pinned!.Presenter.MoveFocus(next);
            else if (IsMediaFullscreen) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = fullscreenView });
            else if (interactive && RecoveryVisible) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = StatusChrome });
            else if (interactive) surface?.MoveFocus(next);
            else if (!NavigateTray(next)) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = Tray });
        }
        if ((PinnedInputActive ? pinned!.Presenter : interactive && !IsMediaFullscreen ? surface : null) is { } current)
        {
            var delta = rightStick.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
            if (delta.X != 0 || delta.Y != 0) current.ScrollBy(delta.X, delta.Y);
        }
        else rightStick.Reset();
        foreach (var (mask, button) in Buttons)
        {
            if ((frame.PressedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Pressed);
            if ((frame.ReleasedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Released);
        }
        if (frame.LeftTriggerPressed != 0) _ = RouteButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
        if (frame.LeftTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
        if (frame.RightTriggerPressed != 0) _ = RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        if (frame.RightTriggerReleased != 0) _ = RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
    }

    private async Task RouteButtonAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase == ControllerEventPhase.Released && shellOwnedReleases.Remove(button))
        {
            if (button == ControllerButton.Y)
                await RunTrayHoldAsync(trayHold.Release(TrayGestureIdentity, !reordering, Environment.TickCount64));
            return;
        }
        if (retired || switching && interactive || !visible) return;
        try
        {
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
                    var accepted = await pin.Presenter.HandleControllerButtonAsync(button, phase, cancellationToken: lifetime.Token);
                    if (!accepted && button == ControllerButton.B && phase == ControllerEventPhase.Pressed && ReferenceEquals(pinned, pin))
                    { shellOwnedReleases.Add(button); ExitPinnedInteraction(restoreMain: true); }
                    return;
                }
            }
            if (RouteFullscreenButton(button, phase)) return;
            if (!interactive)
            {
                await RouteTrayButtonAsync(button, phase);
                return;
            }
            if (RecoveryVisible)
            {
                if (phase == ControllerEventPhase.Pressed && button == ControllerButton.A && Retry.Visibility == Visibility.Visible)
                { shellOwnedReleases.Add(button); await RetryPresentationAsync(); }
                else if (phase == ControllerEventPhase.Pressed && button == ControllerButton.B)
                { shellOwnedReleases.Add(button); SetInteractive(false); FocusTray(); }
                return;
            }
            if (surface is null) return;
            var target = surface;
            var version = selectionVersion;
            var handled = await target.HandleControllerButtonAsync(button, phase, cancellationToken: lifetime.Token);
            if (!handled && button == ControllerButton.B && phase == ControllerEventPhase.Pressed &&
                !retired && visible && version == selectionVersion && ReferenceEquals(target, surface))
            {
                SetInteractive(false);
                ResetInputPresentation();
                FocusTray();
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }
}
