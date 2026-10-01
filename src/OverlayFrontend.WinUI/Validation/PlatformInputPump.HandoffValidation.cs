using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal sealed partial class PlatformInputPump
{
    internal ushort? HandoffValidationButtons { get; set; }
    partial void AdjustHandoffReleaseValidationFrame(ref ControllerFrame frame)
    {
        if (HandoffValidationButtons is not { } buttons) return;
        frame = ControllerFrame.Create();
        frame.Connected = 1;
        frame.State.Buttons = buttons;
    }
}
