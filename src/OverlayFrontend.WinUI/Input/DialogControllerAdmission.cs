using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal enum DialogControllerAdmissionResult { Unavailable, AwaitingRelease, Ready }

/// <summary>Shared host-dialog release boundary; ownership and cleanup stay with the caller.</summary>
internal sealed class DialogControllerAdmission
{
    internal bool IsArmed { get; private set; }

    internal DialogControllerAdmissionResult Observe(ControllerFrame frame, bool ownsInput)
    {
        if (!ownsInput || frame.Connected == 0 || frame.Primed != 0)
        {
            Reset();
            return DialogControllerAdmissionResult.Unavailable;
        }
        // Preserve the dialogs' existing neutral rule, including zero triggers
        // and ignoring sticks. External-window handoff has a different contract.
        if (frame.State.Buttons == 0 && frame.State.LeftTrigger == 0 && frame.State.RightTrigger == 0)
            IsArmed = true;
        return IsArmed ? DialogControllerAdmissionResult.Ready : DialogControllerAdmissionResult.AwaitingRelease;
    }

    internal void Reset() => IsArmed = false;
}
