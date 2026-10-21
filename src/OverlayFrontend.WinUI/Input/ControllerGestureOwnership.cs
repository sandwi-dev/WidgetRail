using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>
/// Tracks gestures consumed by host chrome across focus transitions. This does
/// not select an input owner, admit a frame, dispatch actions or reset a mode.
/// Callers query ownership before capturing presses from the current frame.
/// </summary>
internal sealed class ControllerGestureOwnership
{
    private static readonly (ushort Mask, ControllerButton Button)[] digitalButtons =
    [
        (0x1000, ControllerButton.A), (0x2000, ControllerButton.B),
        (0x4000, ControllerButton.X), (0x8000, ControllerButton.Y),
        (0x100, ControllerButton.LeftBumper), (0x200, ControllerButton.RightBumper),
        (0x40, ControllerButton.LeftStick), (0x80, ControllerButton.RightStick),
        (0x10, ControllerButton.Menu), (0x20, ControllerButton.View),
    ];
    internal static ReadOnlySpan<(ushort Mask, ControllerButton Button)> DigitalButtons => digitalButtons;
    private readonly HashSet<ControllerButton> owned = [];

    internal void Claim(ControllerButton button) => owned.Add(button);
    internal bool Owns(ControllerButton button) => owned.Contains(button);
    internal bool Release(ControllerButton button) => owned.Remove(button);
    internal void Reset() => owned.Clear();

    // Dialogs forward their own trigger events. Their release-only observation
    // must not acquire fresh gestures or alter trigger ownership.
    internal void ReleaseDigitalButtons(ControllerFrame frame)
    {
        foreach (var (mask, button) in digitalButtons)
            if ((frame.ReleasedButtons & mask) != 0) Release(button);
    }

    internal void PrimeDigitalButtons(ushort buttons)
    {
        foreach (var (mask, button) in digitalButtons)
        {
            if ((buttons & mask) != 0) Claim(button);
            else Release(button);
        }
    }

    internal void CaptureFrame(ControllerFrame frame)
    {
        foreach (var (mask, button) in digitalButtons)
        {
            if ((frame.PressedButtons & mask) != 0) Claim(button);
            if ((frame.ReleasedButtons & mask) != 0) Release(button);
        }
        if (frame.LeftTriggerPressed != 0) Claim(ControllerButton.LeftTrigger);
        if (frame.RightTriggerPressed != 0) Claim(ControllerButton.RightTrigger);
        if (frame.LeftTriggerReleased != 0) Release(ControllerButton.LeftTrigger);
        if (frame.RightTriggerReleased != 0) Release(ControllerButton.RightTrigger);
    }

    // Native focus/modal transitions can omit release edges. Reconcile only
    // releases from physical state; this never claims a fresh press.
    internal void ReconcileReleasedState(RawControllerState state)
    {
        foreach (var (mask, button) in digitalButtons)
            if ((state.Buttons & mask) == 0) Release(button);
        if (state.LeftTrigger == 0) Release(ControllerButton.LeftTrigger);
        if (state.RightTrigger == 0) Release(ControllerButton.RightTrigger);
    }
}
