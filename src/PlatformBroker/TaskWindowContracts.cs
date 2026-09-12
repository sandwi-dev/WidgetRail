using System.Text.Json.Serialization;

namespace WidgetRail.PlatformBroker;

public sealed record TaskWindowSummary(
    [property: JsonRequired] string WindowId,
    [property: JsonRequired] string ApplicationName,
    [property: JsonRequired] string Title,
    [property: JsonRequired] bool IsMinimized)
{
    [JsonIgnore] public NativeWindowPreviewTarget? PreviewTarget { get; init; }
}

public sealed record TaskWindowRequest([property: JsonRequired] string WindowId);
