using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Immutable compiled, closed JSON Schema subset. Unsupported keywords (including
/// references) are rejected, never ignored. This validates data, not authority.
/// </summary>
public sealed class CompiledWidgetIntentContract
{
    public const int MaximumSchemaBytes = 16 * 1024;
    public const int MaximumPayloadBytes = 32 * 1024;
    public const int MaximumDepth = 6;
    private readonly JsonElement schema;
    public string Id { get; }
    public int Version { get; }
    public string SchemaDigest { get; }

    private CompiledWidgetIntentContract(WidgetIntentContract contract)
    {
        Id = contract.Id;
        Version = contract.Version;
        schema = contract.PayloadSchema.Clone();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonical(writer, schema);
        SchemaDigest = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static CompiledWidgetIntentContract Create(WidgetIntentContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!WidgetIntentContracts.IsValidId(contract.Id) || contract.Version is < 1 or > 65535 ||
            contract.PayloadSchema.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(contract.PayloadSchema.GetRawText()) > MaximumSchemaBytes)
            throw Invalid();
        var remaining = 128;
        ValidateSchema(contract.PayloadSchema, 0, ref remaining);
        if (contract.PayloadSchema.GetProperty("type").GetString() != "object") throw Invalid();
        return new(contract);
    }

    public bool Accepts(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumPayloadBytes) return false;
        return Match(schema, payload);
    }

    private static void ValidateSchema(JsonElement value, int depth, ref int remaining)
    {
        if (depth > MaximumDepth || --remaining < 0 || value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = Fields(value);
        if (!fields.TryGetValue("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String) throw Invalid();
        var type = typeValue.GetString();
        string[] allowed = type switch
        {
            "object" => ["type", "properties", "required", "additionalProperties"],
            "array" => ["type", "items", "minItems", "maxItems"],
            "string" => ["type", "minLength", "maxLength", "enum"],
            "number" or "integer" => ["type", "minimum", "maximum"],
            "boolean" => ["type"],
            _ => throw Invalid(),
        };
        if (fields.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw Invalid();
        switch (type)
        {
            case "object":
                if (!fields.TryGetValue("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False ||
                    !fields.TryGetValue("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) throw Invalid();
                var children = Fields(properties);
                if (children.Count > 32 || children.Keys.Any(key => key.Length is 0 or > 64 || key.Any(char.IsControl))) throw Invalid();
                foreach (var child in children.Values) ValidateSchema(child, depth + 1, ref remaining);
                if (fields.TryGetValue("required", out var required))
                {
                    var names = Strings(required, 32);
                    if (names.Any(name => !children.ContainsKey(name))) throw Invalid();
                }
                break;
            case "array":
                Bounds(fields, "minItems", "maxItems", 64);
                if (!fields.TryGetValue("items", out var items)) throw Invalid();
                ValidateSchema(items, depth + 1, ref remaining);
                break;
            case "string":
                var (min, max) = Bounds(fields, "minLength", "maxLength", 8192);
                if (fields.TryGetValue("enum", out var choices))
                {
                    var options = Strings(choices, 64);
                    if (options.Count == 0 || options.Any(option => Length(option) < min || Length(option) > max)) throw Invalid();
                }
                break;
            case "integer":
            case "number":
                if (!fields.TryGetValue("minimum", out var lower) || !lower.TryGetDecimalSafe(out var minimum) ||
                    !fields.TryGetValue("maximum", out var upper) || !upper.TryGetDecimalSafe(out var maximum) ||
                    minimum > maximum || type == "integer" && (decimal.Truncate(minimum) != minimum || decimal.Truncate(maximum) != maximum)) throw Invalid();
                break;
        }
    }

    private static bool Match(JsonElement definition, JsonElement value)
    {
        switch (definition.GetProperty("type").GetString())
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object) return false;
                var properties = definition.GetProperty("properties");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                    if (!seen.Add(property.Name) || !properties.TryGetProperty(property.Name, out var child) || !Match(child, property.Value)) return false;
                return !definition.TryGetProperty("required", out var required) || required.EnumerateArray().All(name => seen.Contains(name.GetString()!));
            case "array":
                return value.ValueKind == JsonValueKind.Array &&
                    value.GetArrayLength() >= Minimum(definition, "minItems") &&
                    value.GetArrayLength() <= definition.GetProperty("maxItems").GetInt32() &&
                    value.EnumerateArray().All(item => Match(definition.GetProperty("items"), item));
            case "string":
                return value.ValueKind == JsonValueKind.String &&
                    Length(value.GetString()!) >= Minimum(definition, "minLength") &&
                    Length(value.GetString()!) <= definition.GetProperty("maxLength").GetInt32() &&
                    (!definition.TryGetProperty("enum", out var choices) || choices.EnumerateArray().Any(choice => choice.GetString() == value.GetString()));
            case "boolean": return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case "integer":
            case "number":
                return value.TryGetDecimalSafe(out var number) &&
                    number >= definition.GetProperty("minimum").GetDecimal() && number <= definition.GetProperty("maximum").GetDecimal() &&
                    (definition.GetProperty("type").GetString() != "integer" || decimal.Truncate(number) == number);
            default: return false;
        }
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement value)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!result.TryAdd(property.Name, property.Value)) throw Invalid();
        return result;
    }

    private static HashSet<string> Strings(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw Invalid();
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || !values.Add(item.GetString()!)) throw Invalid();
        return values;
    }

    private static (int Min, int Max) Bounds(Dictionary<string, JsonElement> fields, string minName, string maxName, int limit)
    {
        if (!fields.TryGetValue(maxName, out var maxValue) || maxValue.ValueKind != JsonValueKind.Number ||
            !maxValue.TryGetInt32(out var max) || max < 0 || max > limit) throw Invalid();
        var min = 0;
        if (fields.TryGetValue(minName, out var minValue) && (minValue.ValueKind != JsonValueKind.Number ||
            !minValue.TryGetInt32(out min) || min < 0 || min > max)) throw Invalid();
        return (min, max);
    }

    private static int Minimum(JsonElement schema, string key) => schema.TryGetProperty(key, out var value) ? value.GetInt32() : 0;
    private static int Length(string value) => value.EnumerateRunes().Count();
    private static ArgumentException Invalid() => new("Invalid bounded intent contract.");

    // Object property order and set-valued keyword order have no effect on identity.
    // Other syntactic changes deliberately require an identical contract definition.
    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Canonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray().OrderBy(item => item.GetString(), StringComparer.Ordinal)) item.WriteTo(writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                break;
            default: value.WriteTo(writer); break;
        }
    }
}

internal static class IntentJsonNumbers
{
    internal static bool TryGetDecimalSafe(this JsonElement value, out decimal number)
    {
        number = 0;
        // TryGetDecimal can round tiny fractions to zero or round a long fraction
        // to an integer. Such rounding must not bypass minimum/integer constraints.
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number) &&
            Normalize(value.GetRawText()) == Normalize(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string? Normalize(string text)
    {
        var exponentAt = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentAt >= 0)
        {
            if (!int.TryParse(text.AsSpan(exponentAt + 1), System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out exponent) || exponent is < -1000 or > 1000) return null;
            text = text[..exponentAt];
        }
        var negative = text.StartsWith('-');
        if (negative) text = text[1..];
        var point = text.IndexOf('.');
        if (point >= 0) { exponent -= text.Length - point - 1; text = text.Remove(point, 1); }
        text = text.TrimStart('0');
        if (text.Length == 0) return "0";
        var significant = text.TrimEnd('0');
        exponent += text.Length - significant.Length;
        return (negative ? "-" : "") + significant + "e" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
