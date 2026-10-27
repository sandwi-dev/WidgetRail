using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WebSessionMicrophoneState>))]
public enum WebSessionMicrophoneState { Unavailable, Granted, Denied, Cancelled }

/// <summary>Website microphone admission only, not proof of operating-system or device access.
/// Remembered means an existing profile grant, or the user's choice to save the grant when the
/// current document first requests its microphone. Navigation before use retires that choice.</summary>
public sealed record WebSessionMicrophoneResult(WebSessionMicrophoneState State, bool Remembered = false)
{
    public bool IsWellFormed() => Enum.IsDefined(State) &&
        (!Remembered || State is WebSessionMicrophoneState.Granted or WebSessionMicrophoneState.Denied);
}
