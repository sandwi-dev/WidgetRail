using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>
/// A host-rendered controller symbol, independent of input bindings. Stick
/// movement and stick presses are deliberately different presentation values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ControllerPrompt>))]
public enum ControllerPrompt
{
    A, B, X, Y,
    LeftBumper, RightBumper, LeftTrigger, RightTrigger,
    LeftStickPress, RightStickPress, LeftStickMove, RightStickMove,
    DPad, DPadHorizontal, DPadVertical, DPadUp, DPadDown, DPadLeft, DPadRight,
    View, Menu, Guide,
}
