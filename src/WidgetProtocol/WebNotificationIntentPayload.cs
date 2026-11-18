using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Opaque native notification identity. It contains no notification text, browser credentials, or destination authority.</summary>
public sealed record WebNotificationIntentPayload(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("tag")] string Tag)
{
    public bool IsWellFormed() => new WebSessionReference(SessionId).IsWellFormed() &&
        Tag is { Length: > 0 and <= 256 } && !Tag.Any(char.IsControl);
}
