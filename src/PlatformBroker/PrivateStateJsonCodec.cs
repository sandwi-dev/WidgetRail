using System.Text;
using System.Text.Json;

namespace WidgetRail.PlatformBroker;

/// <summary>Shared distrust boundary for the host-side private-state transport.</summary>
internal static class PrivateStateJsonCodec
{
    internal static byte[] DecodeCanonicalBase64(string? value, string errorCode)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > CommunityPlatformLimits.MaximumPrivateStateBase64Characters)
            throw new BrokerException(errorCode, "Private state JSON is invalid or too large.");

        byte[] decoded;
        try { decoded = Convert.FromBase64String(value); }
        catch (FormatException exception)
        {
            throw new BrokerException(errorCode, "Private state JSON encoding is invalid.", exception);
        }
        if (decoded.Length is < 1 or > CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes ||
            !string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal))
            throw new BrokerException(errorCode, "Private state JSON encoding is invalid.");

        var canonical = Canonicalize(decoded, errorCode);
        if (!decoded.AsSpan().SequenceEqual(canonical))
            throw new BrokerException(errorCode, "Private state JSON is not canonical.");
        return decoded;
    }

    internal static string CanonicalBase64(ReadOnlySpan<byte> utf8, string errorCode) =>
        Convert.ToBase64String(Canonicalize(utf8, errorCode));

    internal static byte[] Canonicalize(ReadOnlySpan<byte> utf8, string errorCode)
    {
        if (utf8.Length is < 1 or > CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
            throw new BrokerException(errorCode, "Private state JSON is invalid or too large.");
        try
        {
            BrokerJson.RejectDuplicateProperties(utf8);
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
            using var output = new MemoryStream(utf8.Length);
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false,
                   }))
                WriteCanonical(writer, document.RootElement);
            var result = output.ToArray();
            if (result.Length > CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
                throw new BrokerException(errorCode, "Private state JSON is invalid or too large.");
            return result;
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new BrokerException(errorCode, "Private state JSON is invalid.", exception);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject()
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported JSON token.");
        }
    }
}
