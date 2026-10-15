using System.Text.Json;

namespace WidgetRail.PlatformBroker;

public static class BrokerJson
{
    public const int ProtocolVersion = BrokerWireJson.ProtocolVersion;
    public const int MaximumRequestBytes = BrokerWireJson.MaximumRequestBytes;
    public const int MaximumEventBytes = BrokerWireJson.MaximumEventBytes;
    public const int MaximumResponseBytes = BrokerWireJson.MaximumResponseBytes;
    public const int MaximumDepth = BrokerWireJson.MaximumDepth;
    public const int MaximumTransportStringLength = BrokerWireJson.MaximumTransportStringLength;
    public const int MaximumArrayItems = BrokerWireJson.MaximumArrayItems;
    private static JsonSerializerOptions Options => BrokerWireJson.StrictOptions;
    public static byte[] SerializeResponse(BrokerResponseEnvelope response) => BrokerWireJson.SerializeResponse(response);
    public static JsonElement ToElement<T>(T value) => BrokerWireJson.ToElement(value);
    internal static T ParsePayload<T>(JsonElement payload) => BrokerWireJson.ParsePayload<T>(payload);
    internal static void ValidateElement(JsonElement element, string code) => BrokerWireJson.ValidateElement(element, code);
    internal static JsonSerializerOptions StrictOptions => BrokerWireJson.StrictOptions;
    internal static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8) => BrokerWireJson.RejectDuplicateProperties(utf8);

    public static BrokerRequestEnvelope ParseRequest(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > MaximumRequestBytes)
            throw new BrokerException("request_too_large", "Broker request size is invalid.");
        try
        {
            RejectDuplicateProperties(utf8);
            var request = JsonSerializer.Deserialize<BrokerRequestEnvelope>(utf8, Options)
                ?? throw new JsonException("Request was null.");
            ValidateRequest(request);
            ValidateElement(request.Payload, "invalid_payload");
            return request with { Payload = request.Payload.Clone() };
        }
        catch (BrokerException) { throw; }
        catch (JsonException exception)
        {
            throw new BrokerException("malformed_request", "Broker request JSON is invalid.", exception);
        }
    }

    private static void ValidateRequest(BrokerRequestEnvelope request)
    {
        if (request.ProtocolVersion != ProtocolVersion)
            throw new BrokerException("unsupported_protocol", "Broker protocol version is unsupported.");
        if (request.RequestId <= 0 || request.RequestId > 9_007_199_254_740_991L)
            throw new BrokerException("invalid_request_id", "Broker request ID is invalid.");
        if (request.Widget is null)
            throw new BrokerException("invalid_identity", "Widget identity is invalid.");
        request.Widget.Validate();
        if (request.Payload.ValueKind != JsonValueKind.Object)
            throw new BrokerException("invalid_payload", "Broker payload must be an object.");
        if (string.IsNullOrEmpty(request.CapabilityId) || string.IsNullOrEmpty(request.Operation) ||
            !PlatformCapabilities.TryGet(request.CapabilityId, out var capability) ||
            !capability.Operations.Contains(request.Operation))
            throw new BrokerException("unsupported_capability", "Capability or operation is unsupported.");
        if (request.GestureInputSequence.HasValue != request.GestureSnapshotSequence.HasValue ||
            request.GestureInputSequence is { } inputSequence && inputSequence <= 0 ||
            request.GestureSnapshotSequence is { } snapshotSequence && snapshotSequence <= 0)
            throw new BrokerException(
                "invalid_gesture", "Gesture input and snapshot sequences must be positive and supplied together.");
    }

}
