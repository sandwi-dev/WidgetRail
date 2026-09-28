using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonSerializable(typeof(ViewSnapshot))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
