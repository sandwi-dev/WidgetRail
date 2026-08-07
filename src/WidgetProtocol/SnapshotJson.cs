using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetProtocol;

public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new ProtocolValidationException(errors);
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
    }

    public static ViewSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        var snapshot = JsonSerializer.Deserialize<ViewSnapshot>(payload, Options)
            ?? throw new JsonException("The snapshot payload was null.");
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new ProtocolValidationException(errors);
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
