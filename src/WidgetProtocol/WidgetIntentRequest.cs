using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentPresentation>))]
public enum WidgetIntentPresentation { PreferExistingSurface, OpenWidget }

[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentRouting>))]
public enum WidgetIntentRouting { WidgetPreferred, Windows }

/// <summary>InteractionRequired accepts the request once and asks the host to show its stored state.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentResult>))]
public enum WidgetIntentResult { Rejected, Accepted, InteractionRequired }

[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentStatus>))]
public enum WidgetIntentStatus { Accepted, Unavailable, Rejected, Cancelled, Failed }

/// <summary>A terminal routing result. Accepted means the receiver accepted the action, not that its network work succeeded.</summary>
public sealed record WidgetIntentFeedback(string RequestId, WidgetIntentRequest Request, WidgetIntentStatus Status);

/// <summary>A declarative request bound to one displayed action, never an execution grant.</summary>
public sealed record WidgetIntentRequest(string ContractId, int Version, string SchemaDigest, JsonElement Payload)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WidgetIntentPresentation Presentation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WidgetIntentRouting Routing { get; init; }
    /// <summary>Ask the originating worker to observe this request's terminal result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReportsResult { get; init; }
    /// <summary>One declared alternative; the sender may select it only after unavailable or rejected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetIntentRequest? Fallback { get; init; }

    public static WidgetIntentRequest Create(WidgetIntentContract contract, JsonElement payload,
        WidgetIntentPresentation presentation = WidgetIntentPresentation.PreferExistingSurface)
        => Create(contract, payload, WidgetIntentRouting.WidgetPreferred, presentation);

    public static WidgetIntentRequest Create(WidgetIntentContract contract, JsonElement payload,
        WidgetIntentRouting routing, WidgetIntentPresentation presentation = WidgetIntentPresentation.PreferExistingSurface)
    {
        if (!Enum.IsDefined(presentation)) throw new ArgumentOutOfRangeException(nameof(presentation));
        if (!Enum.IsDefined(routing)) throw new ArgumentOutOfRangeException(nameof(routing));
        var compiled = CompiledWidgetIntentContract.Create(contract);
        if (!compiled.Accepts(payload)) throw new ArgumentException("Intent payload does not match its contract.", nameof(payload));
        return new(compiled.Id, compiled.Version, compiled.SchemaDigest, payload.Clone()) { Presentation = presentation, Routing = routing };
    }

    public bool IsWellFormed()
    {
        if (Fallback is { } fallback && (!ReportsResult || fallback.Fallback is not null || !fallback.IsWellFormed())) return false;
        if (!Enum.IsDefined(Presentation) || !Enum.IsDefined(Routing) || !WidgetIntentContracts.IsValidId(ContractId) || Version is < 1 or > 65535 ||
            SchemaDigest is not { Length: 64 } || !SchemaDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            Payload.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(Payload.GetRawText()) > CompiledWidgetIntentContract.MaximumPayloadBytes) return false;
        return Check(Payload, 0);
    }

    public bool Matches(WidgetIntentRequest? other) => other is not null &&
        ContractId == other.ContractId && Version == other.Version && SchemaDigest == other.SchemaDigest && Presentation == other.Presentation && Routing == other.Routing && ReportsResult == other.ReportsResult &&
        (Fallback is null ? other.Fallback is null : Fallback.Matches(other.Fallback)) &&
        IsWellFormed() && other.IsWellFormed() && Fingerprint(Payload) == Fingerprint(other.Payload);

    private static bool Check(JsonElement value, int depth)
    {
        if (depth > CompiledWidgetIntentContract.MaximumDepth) return false;
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                    if (names.Count >= 32 || property.Name.Length is 0 or > 64 || property.Name.Any(char.IsControl) ||
                        !names.Add(property.Name) || !Check(property.Value, depth + 1)) return false;
                return true;
            case JsonValueKind.Array:
                return value.GetArrayLength() <= 64 && value.EnumerateArray().All(item => Check(item, depth + 1));
            case JsonValueKind.String: return value.GetString()!.EnumerateRunes().Count() <= 8192;
            case JsonValueKind.Number: return value.TryGetDecimalSafe(out _);
            case JsonValueKind.True:
            case JsonValueKind.False: return true;
            default: return false;
        }
    }

    private static string Fingerprint(JsonElement payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, payload);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                break;
            default: value.WriteTo(writer); break;
        }
    }
}
