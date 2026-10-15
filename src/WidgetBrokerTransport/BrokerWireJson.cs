using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace WidgetRail.PlatformBroker;

internal static class BrokerWireJson
{
    public const int ProtocolVersion = 1;
    // A 64 KiB private-state document expands to roughly 86 KiB as base64,
    // plus the authenticated broker envelope. The pipe itself remains capped
    // at 256 KiB.
    public const int MaximumRequestBytes = 128 * 1024;
    public const int MaximumEventBytes = 64 * 1024;
    public const int MaximumResponseBytes = 224 * 1024;
    public const int MaximumDepth = 16;
    public const int MaximumTransportStringLength =
        WidgetRail.Internal.CommunityPlatformContractLimits.MaximumLoopbackResponseBodyUtf8Bytes;
    public const int MaximumArrayItems = 256;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] SerializeResponse(BrokerResponseEnvelope response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        if (bytes.Length > MaximumResponseBytes)
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
                    if (text.Length > MaximumTransportStringLength || text.Any(char.IsControl))
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

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = MaximumDepth,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
