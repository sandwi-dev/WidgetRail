using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Permissions a widget can request for its current backend document.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WebSessionPermissionKind>))]
public enum WebSessionPermissionKind { Microphone, Notifications }

[JsonConverter(typeof(JsonStringEnumConverter<WebSessionPermissionState>))]
public enum WebSessionPermissionState
{
    Unavailable = WebSessionMicrophoneState.Unavailable,
    Granted = WebSessionMicrophoneState.Granted,
    Denied = WebSessionMicrophoneState.Denied,
    Cancelled = WebSessionMicrophoneState.Cancelled
}

/// <summary>A website permission decision, scoped by the host to the current backend
/// document and profile. This is not an operating-system notification setting.</summary>
public sealed record WebSessionPermissionResult(WebSessionPermissionState State, bool Remembered = false)
{
    public bool IsWellFormed() => Enum.IsDefined(State) &&
        (!Remembered || State is WebSessionPermissionState.Granted or WebSessionPermissionState.Denied);
}
