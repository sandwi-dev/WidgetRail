using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

public static class SnapshotJson
{
    private static readonly ProtocolJsonContext Context = new(CreateOptions());

    public static byte[] Serialize(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new ProtocolValidationException(errors);
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, Context.ViewSnapshot);
    }

    public static ViewSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        var snapshot = JsonSerializer.Deserialize(payload, Context.ViewSnapshot)
            ?? throw new JsonException("The snapshot payload was null.");
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0 && SnapshotOmissionDefaults.TryRestore(payload, out var compatible))
        {
            snapshot = JsonSerializer.Deserialize(compatible, Context.ViewSnapshot)!;
            errors = ViewSnapshotValidator.Validate(snapshot);
        }
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        return snapshot;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };
        options.Converters.Add(new EmbeddedMediaCommandJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed class ProtocolValidationException : Exception
{
    public ProtocolValidationException(IReadOnlyList<ProtocolValidationError> errors)
        : base($"The widget protocol payload contains {errors.Count} validation error(s).") => Errors = errors;

    public IReadOnlyList<ProtocolValidationError> Errors { get; }
}
