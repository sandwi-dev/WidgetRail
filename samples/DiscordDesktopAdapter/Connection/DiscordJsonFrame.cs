using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WidgetRail.DiscordDesktop.Connection;

/// <summary>Bounded transport objects, not authorization to execute a domain command.</summary>
public static class DiscordJsonFrame
{
    public const int MaximumPayloadBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async ValueTask<JsonElement> ReadAsync(Stream stream, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count is <= 0 or > MaximumPayloadBytes) throw new InvalidDataException("Invalid Discord frame length.");
        var payload = new byte[count];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return Parse(payload);
    }

    public static async ValueTask WriteAsync(Stream stream, JsonElement value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Validate(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) value.WriteTo(writer);
        if (buffer.WrittenCount is <= 0 or > MaximumPayloadBytes) throw new InvalidDataException("Discord frame exceeds its bound.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, buffer.WrittenCount);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(buffer.WrittenMemory, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static JsonElement Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is <= 0 or > MaximumPayloadBytes) throw new InvalidDataException("Discord frame exceeds its bound.");
        try
        {
            _ = StrictUtf8.GetCharCount(payload.Span);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            Validate(document.RootElement);
            return document.RootElement.Clone();
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        { throw new InvalidDataException("Invalid Discord JSON object.", error); }
    }

    private static void Validate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Discord frames must contain JSON objects.");
        ValidateTree(value, 0);
    }

    private static void ValidateTree(JsonElement value, int depth)
    {
        if (depth > 32 || depth == 32 && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            throw new InvalidDataException("Discord JSON nesting exceeds its bound.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate Discord JSON property.");
                ValidateTree(property.Value, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) ValidateTree(child, depth + 1);
        }
        else if (value.ValueKind == JsonValueKind.String) _ = value.GetString();
    }

    internal static JsonElement Object(Action<Utf8JsonWriter> write)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes)) { writer.WriteStartObject(); write(writer); writer.WriteEndObject(); }
        return Parse(bytes.WrittenMemory);
    }
}
