using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformBroker;

public sealed record BrokerRequestEnvelope(
    [property: JsonRequired] int ProtocolVersion,
    [property: JsonRequired] long RequestId,
    [property: JsonRequired] BrokerWidgetIdentity Widget,
    [property: JsonRequired] string CapabilityId,
    [property: JsonRequired] string Operation,
    [property: JsonRequired] JsonElement Payload,
    long? GestureInputSequence = null,
    long? GestureSnapshotSequence = null);

public sealed record BrokerResponseEnvelope(
    int ProtocolVersion,
    long RequestId,
    bool Succeeded,
    JsonElement? Payload,
    string? ErrorCode);

public sealed record BrokerEventEnvelope(
    int ProtocolVersion,
    long Sequence,
    string CapabilityId,
    string EventType,
    JsonElement Payload);
