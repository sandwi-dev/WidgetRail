using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Document bytes cross the strict broker channel as base64. Markup whitespace
/// is content, while control characters remain forbidden in broker metadata.
/// </summary>
internal sealed class ProviderDocumentContentJsonConverter : JsonConverter<ProviderDocumentContent>
{
    public override ProviderDocumentContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1)
            throw new JsonException("Invalid document envelope.");
        // Keep previously valid single-line documents from older SDKs readable.
        // Raw multiline requests still fail the broker's unchanged frame rules.
        if (root.TryGetProperty("html", out var legacy))
        {
            if (legacy.ValueKind != JsonValueKind.Array || legacy.GetArrayLength() is < 1 or > 5 ||
                legacy.EnumerateArray().Any(part => part.ValueKind != JsonValueKind.String))
                throw new JsonException("Invalid legacy document envelope.");
            var values = legacy.EnumerateArray().Select(part => part.GetString()!).ToArray();
            if (!ProviderDocumentContent.IsValid(values)) throw new JsonException("Invalid document content.");
            return new(values);
        }
        if (!root.TryGetProperty("htmlUtf8Base64", out var encoded) || encoded.ValueKind != JsonValueKind.Array ||
            encoded.GetArrayLength() is < 1 or > 5) throw new JsonException("Invalid document envelope.");
        var html = new List<string>();
        var remaining = ProviderDocumentContent.MaximumUtf8Bytes;
        foreach (var part in encoded.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.String || part.GetString() is not { } text ||
                text.Length == 0 || text.Length > ((remaining + 2) / 3) * 4)
                throw new JsonException("Document bytes exceed their bound.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(text); }
            catch (FormatException error) { throw new JsonException("Invalid document encoding.", error); }
            if (bytes.Length > remaining) throw new JsonException("Document bytes exceed their bound.");
            remaining -= bytes.Length;
            try { html.Add(ProviderDocumentContent.StrictUtf8.GetString(bytes)); }
            catch (DecoderFallbackException error) { throw new JsonException("Invalid document text encoding.", error); }
        }
        if (!ProviderDocumentContent.IsValid(html)) throw new JsonException("Invalid document content.");
        return new(html);
    }

    public override void Write(Utf8JsonWriter writer, ProviderDocumentContent value, JsonSerializerOptions options)
    {
        if (!ProviderDocumentContent.IsValid(value.Html)) throw new JsonException("Invalid document content.");
        writer.WriteStartObject();
        writer.WriteStartArray("htmlUtf8Base64");
        foreach (var html in value.Html)
            writer.WriteBase64StringValue(ProviderDocumentContent.StrictUtf8.GetBytes(html));
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
