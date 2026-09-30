using System.Text.Json.Serialization;

namespace WidgetRail.WidgetBridge;

internal sealed record BridgeHostEffect(
    string WidgetId,
    string RuntimeGeneration,
    string Effect,
    long Sequence,
    long InitiatedAtMilliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, WidgetRail.PlatformBroker.NativeWindowPreviewTarget>? WindowPreviews = null);

internal sealed class BridgeStaleControllerInputAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStalePinnedInputAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStaleIndexedInputAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStaleMediaAuthorityException(string message, bool command = false)
    : Exception(message)
{
    internal string Code => command ? "embedded_media_command_stale" : "embedded_media_stale";
}
internal sealed class BridgeStaleArtworkAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStalePackageIconAuthorityException(string message)
    : Exception(message);

internal sealed class BridgeStalePresentationBaseException()
    : Exception("The retained presentation base is not current in the widget bridge.");
