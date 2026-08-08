using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformBroker;

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

public static class BrokerJson
{
    public const int ProtocolVersion = 1;
    public const int MaximumRequestBytes = 32 * 1024;
    public const int MaximumEventBytes = 64 * 1024;
    public const int MaximumDepth = 16;
    public const int MaximumStringLength = 4096;
    public const int MaximumArrayItems = 256;
    private static readonly JsonSerializerOptions Options = CreateOptions();

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

    public static byte[] SerializeResponse(BrokerResponseEnvelope response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        if (bytes.Length > MaximumEventBytes)
            throw new BrokerException("response_too_large", "Broker response exceeds its bound.");
        return bytes;
    }

    public static JsonElement ToElement<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);
        ValidateElement(element, "invalid_backend_data");
        return element;
    }

    internal static T ParsePayload<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(Options)
                ?? throw new BrokerException("invalid_payload", "Broker payload was null.");
        }
        catch (BrokerException) { throw; }
        catch (JsonException exception)
        {
            throw new BrokerException("invalid_payload", "Broker payload does not match the operation.", exception);
        }
    }

    internal static void ValidateElement(JsonElement element, string code)
    {
        var count = 0;
        Walk(element, 0);
        return;

        void Walk(JsonElement value, int depth)
        {
            if (depth > MaximumDepth || ++count > 4096)
                throw new BrokerException(code, "Broker JSON exceeds structural bounds.");
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = 0;
                    foreach (var property in value.EnumerateObject())
                    {
                        if (++properties > 64 || property.Name.Length > 128)
                            throw new BrokerException(code, "Broker object exceeds its bounds.");
                        Walk(property.Value, depth + 1);
                    }
                    break;
                case JsonValueKind.Array:
                    var items = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (++items > MaximumArrayItems)
                            throw new BrokerException(code, "Broker array exceeds its bound.");
                        Walk(item, depth + 1);
                    }
                    break;
                case JsonValueKind.String:
                    var text = value.GetString() ?? string.Empty;
                    if (text.Length > MaximumStringLength || text.Any(char.IsControl))
                        throw new BrokerException(code, "Broker string exceeds its bound.");
                    break;
                case JsonValueKind.Number:
                    if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
                        throw new BrokerException(code, "Broker number is invalid.");
                    break;
            }
        }
    }

    internal static JsonSerializerOptions StrictOptions => Options;

    internal static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth,
        });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString() ?? string.Empty;
                if (objects.Count == 0 || !objects.Peek().Add(name))
                    throw new JsonException("Duplicate JSON property.");
            }
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

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = MaximumDepth,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
