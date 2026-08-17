using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.SpotifyPlayback;

public static class SpotifyPlaybackProtocolCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static SpotifyPlaybackRequest DecodeRequest(string json)
    {
        using var document = ParseEnvelope(json, requireRequestId: true);
        var root = document.RootElement;
        return new(
            root.GetProperty("version").GetInt32(),
            root.GetProperty("requestId").GetString()!,
            root.GetProperty("type").GetString()!,
            root.GetProperty("payload").Clone());
    }

    public static SpotifyPlaybackEventEnvelope DecodeEvent(string json)
    {
        using var document = ParseEnvelope(json, requireRequestId: false);
        var root = document.RootElement;
        var requestId = root.GetProperty("requestId").ValueKind == JsonValueKind.Null
            ? null
            : root.GetProperty("requestId").GetString();
        return new(
            root.GetProperty("version").GetInt32(),
            root.GetProperty("type").GetString()!,
            requestId,
            root.GetProperty("payload").Clone());
    }

    public static string EncodeRequest(string requestId, string type, object? payload)
    {
        ValidateToken(requestId, SpotifyPlaybackProtocol.MaximumRequestIdCharacters,
            "request ID");
        ValidateToken(type, 32, "message type");
        var json = JsonSerializer.Serialize(new
        {
            version = SpotifyPlaybackProtocol.Version,
            requestId,
            type,
            payload = payload ?? new { },
        }, SerializerOptions);
        return Bound(json);
    }

    public static string EncodeEvent(SpotifyPlaybackEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Version != SpotifyPlaybackProtocol.Version)
            throw Invalid("unsupported_version",
                "The playback-host protocol version is unsupported.");
        ValidateToken(value.Type, 32, "message type");
        if (value.RequestId is { } requestId)
            ValidateToken(requestId, SpotifyPlaybackProtocol.MaximumRequestIdCharacters,
                "request ID");
        return Bound(JsonSerializer.Serialize(value, SerializerOptions));
    }

    public static T DecodePayload<T>(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw Invalid("invalid_payload", "The playback-host payload is invalid.");
        try
        {
            return payload.Deserialize<T>(SerializerOptions) ??
                throw Invalid("invalid_payload", "The playback-host payload is invalid.");
        }
        catch (SpotifyPlaybackProtocolException) { throw; }
        catch (JsonException)
        {
            throw Invalid("invalid_payload", "The playback-host payload is invalid.");
        }
    }

    public static T DecodePayload<T>(SpotifyPlaybackRequest request) =>
        DecodePayload<T>(request.Payload);

    private static JsonDocument ParseEnvelope(string json, bool requireRequestId)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length is 0 or > SpotifyPlaybackProtocol.MaximumMessageCharacters)
            throw Invalid("message_too_large",
                "The playback-host message is outside its bounds.");
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !TryExactProperties(root, "version", "requestId", "type", "payload") ||
                    root.GetProperty("version").ValueKind != JsonValueKind.Number ||
                    !root.GetProperty("version").TryGetInt32(out var version) ||
                    root.GetProperty("type").ValueKind != JsonValueKind.String ||
                    root.GetProperty("payload").ValueKind != JsonValueKind.Object)
                    throw Invalid("invalid_message",
                        "The playback-host message shape is invalid.");
                if (version != SpotifyPlaybackProtocol.Version)
                    throw Invalid("unsupported_version",
                        "The playback-host protocol version is unsupported.");
                var request = root.GetProperty("requestId");
                if (requireRequestId && request.ValueKind != JsonValueKind.String ||
                    !requireRequestId && request.ValueKind is not (
                        JsonValueKind.String or JsonValueKind.Null))
                    throw Invalid("invalid_message",
                        "The playback-host message shape is invalid.");
                if (request.ValueKind == JsonValueKind.String)
                    ValidateToken(request.GetString()!,
                        SpotifyPlaybackProtocol.MaximumRequestIdCharacters, "request ID");
                ValidateToken(root.GetProperty("type").GetString()!, 32, "message type");
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
        catch (SpotifyPlaybackProtocolException) { throw; }
        catch (JsonException)
        {
            throw Invalid("invalid_json",
                "The playback-host message is not valid JSON.");
        }
        catch (InvalidOperationException)
        {
            throw Invalid("invalid_message",
                "The playback-host message shape is invalid.");
        }
    }

    private static string Bound(string json)
    {
        if (json.Length > SpotifyPlaybackProtocol.MaximumMessageCharacters)
            throw Invalid("message_too_large",
                "The playback-host message is outside its bounds.");
        return json;
    }

    private static bool TryExactProperties(JsonElement element, params string[] expected)
    {
        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == expected.Length &&
            properties.Distinct(StringComparer.Ordinal).Count() == expected.Length &&
            expected.All(name => properties.Contains(name, StringComparer.Ordinal));
    }

    private static void ValidateToken(string value, int maximum, string label)
    {
        if (value.Length is 0 || value.Length > maximum ||
            value.Any(character => character is < '!' or > '~'))
            throw Invalid("invalid_message", $"The playback-host {label} is invalid.");
    }

    private static SpotifyPlaybackProtocolException Invalid(string code, string message) =>
        new(code, message);
}

public static class BoundedTextLineReader
{
    public static async ValueTask<string?> ReadAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var buffer = new char[1];
        var value = new System.Text.StringBuilder(Math.Min(maximumCharacters, 256));
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0) return value.Length == 0 ? null : value.ToString();
            if (buffer[0] == '\n')
            {
                if (value.Length > 0 && value[^1] == '\r') value.Length--;
                return value.ToString();
            }
            if (value.Length == maximumCharacters)
                throw new SpotifyPlaybackProtocolException(
                    "message_too_large", "The playback-host input line is outside its bounds.");
            value.Append(buffer[0]);
        }
    }
}
