using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.SpotifyPlaybackHost;

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
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length is 0 or > SpotifyPlaybackProtocol.MaximumMessageCharacters)
            throw Invalid("message_too_large", "The playback-host message is outside its bounds.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryExactProperties(root, "version", "requestId", "type", "payload"))
                throw Invalid("invalid_message", "The playback-host message shape is invalid.");

            var version = RequiredInt32(root, "version");
            var requestId = RequiredString(root, "requestId");
            var type = RequiredString(root, "type");
            var payload = root.GetProperty("payload");
            if (version != SpotifyPlaybackProtocol.Version)
                throw Invalid("unsupported_version", "The playback-host protocol version is unsupported.");
            ValidateToken(requestId, SpotifyPlaybackProtocol.MaximumRequestIdCharacters,
                "request ID");
            ValidateToken(type, 32, "message type");
            if (payload.ValueKind != JsonValueKind.Object)
                throw Invalid("invalid_message", "The playback-host payload must be an object.");
            return new(version, requestId, type, payload.Clone());
        }
        catch (SpotifyPlaybackProtocolException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Invalid("invalid_json", "The playback-host message is not valid JSON.");
        }
        catch (InvalidOperationException)
        {
            throw Invalid("invalid_message", "The playback-host message shape is invalid.");
        }
    }

    public static string EncodeEvent(SpotifyPlaybackEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        if (json.Length > SpotifyPlaybackProtocol.MaximumMessageCharacters)
            throw Invalid("message_too_large", "The playback-host event is outside its bounds.");
        return json;
    }

    internal static T DecodePayload<T>(SpotifyPlaybackRequest request)
        => DecodePayload<T>(request.Payload);

    internal static T DecodePayload<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(SerializerOptions) ??
                throw Invalid("invalid_payload", "The playback-host payload is invalid.");
        }
        catch (SpotifyPlaybackProtocolException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Invalid("invalid_payload", "The playback-host payload is invalid.");
        }
    }

    private static bool TryExactProperties(JsonElement element, params string[] expected)
    {
        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == expected.Length &&
            properties.Distinct(StringComparer.Ordinal).Count() == expected.Length &&
            expected.All(name => properties.Contains(name, StringComparer.Ordinal));
    }

    private static int RequiredInt32(JsonElement root, string name) =>
        root.GetProperty(name).GetInt32();

    private static string RequiredString(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ??
        throw Invalid("invalid_message", "The playback-host message shape is invalid.");

    private static void ValidateToken(string value, int maximum, string label)
    {
        if (value.Length is 0 || value.Length > maximum ||
            value.Any(character => character is < '!' or > '~'))
            throw Invalid("invalid_message", $"The playback-host {label} is invalid.");
    }

    private static SpotifyPlaybackProtocolException Invalid(string code, string message) =>
        new(code, message);
}

internal static class BoundedTextLineReader
{
    internal static async ValueTask<string?> ReadAsync(
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
