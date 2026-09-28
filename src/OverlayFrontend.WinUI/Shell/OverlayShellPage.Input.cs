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
        if (retired || !visible || switching && interactive || frame.Connected == 0) { rightStick.Reset(); return; }
        surface?.SetControllerFamily(frame.LastInputFamily);
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
            if (IsMediaFullscreen) FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = fullscreenView });
            else if (interactive) surface?.MoveFocus(next);
            else FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = Tray });
        }
        if (interactive && !IsMediaFullscreen && surface is { } current)
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
        if (retired || switching && interactive || !visible) return;
        try
        {
            if (RouteFullscreenButton(button, phase)) return;
            if (!interactive)
            {
                if (phase != ControllerEventPhase.Pressed) return;
                if (button == ControllerButton.B) HideRequested?.Invoke();
                else if (button == ControllerButton.A)
                {
                    // Focus is the activation target; ListView selection does not
                    // necessarily follow XY controller focus.
                    if (FocusedTrayWidget() is { } descriptor) await SelectAsync(descriptor.Id);
                }
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
