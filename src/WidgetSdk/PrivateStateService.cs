using System.Text;
using System.Text.Json;

namespace GameBarAlternative.WidgetSdk;

/// <summary>
/// Durable, readable JSON state scoped by the trusted host to the authenticated
/// publisher and package. It is always available in active lifecycle states,
/// requires no manifest declaration or consent toggle, and must not be used for
/// secrets. Revision 0 means the authority has never successfully mutated its
/// state; every successful write or clear advances the revision.
/// </summary>
public sealed class WidgetPrivateStateService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IWidgetCapabilityClient _client;

    internal WidgetPrivateStateService(IWidgetCapabilityClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async ValueTask<WidgetPrivateStateSnapshot> ReadJsonAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _client.InvokeAsync(
            WidgetPrivateStateCapabilities.Read,
            new WidgetCapabilityQuery(),
            cancellationToken).ConfigureAwait(false);
        if (response is null || response.Revision < 0 ||
            response.Exists != (response.CanonicalJsonBase64 is not null))
            throw MalformedResponse();
        if (!response.Exists)
            return new WidgetPrivateStateSnapshot(false, null, response.Revision);

        var canonical = DecodeCanonical(response.CanonicalJsonBase64!);
        return new WidgetPrivateStateSnapshot(
            true, StrictUtf8.GetString(canonical), response.Revision);
    }

    public async ValueTask<WidgetPrivateStateValue<T>> ReadAsync<T>(
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadJsonAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Exists)
            return new WidgetPrivateStateValue<T>(false, default, snapshot.Revision);
        var value = JsonSerializer.Deserialize<T>(snapshot.Json!, serializerOptions);
        return new WidgetPrivateStateValue<T>(true, value, snapshot.Revision);
    }

    public async ValueTask<WidgetPrivateStateMutation> WriteJsonAsync(
        string json,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        ValidateExpectedRevision(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Encoding.UTF8.GetBytes(CanonicalizeForHost(json));
        var response = await _client.InvokeAsync(
            WidgetPrivateStateCapabilities.Write,
            new WriteWidgetPrivateStateTransportRequest(
                Convert.ToBase64String(canonical), expectedRevision),
            cancellationToken).ConfigureAwait(false);
        return ValidateMutation(response);
    }

    public ValueTask<WidgetPrivateStateMutation> WriteAsync<T>(
        T value,
        long? expectedRevision = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(value, serializerOptions);
        return WriteJsonAsync(json, expectedRevision, cancellationToken);
    }

    public async ValueTask<WidgetPrivateStateMutation> ClearAsync(
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExpectedRevision(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _client.InvokeAsync(
            WidgetPrivateStateCapabilities.Clear,
            new ClearWidgetPrivateStateTransportRequest(expectedRevision),
            cancellationToken).ConfigureAwait(false);
        return ValidateMutation(response);
    }

    private static WidgetPrivateStateMutation ValidateMutation(
        WidgetPrivateStateTransportMutation? response) =>
        response is { Revision: > 0 }
            ? new WidgetPrivateStateMutation(response.Revision)
            : throw MalformedResponse();

    private static byte[] DecodeCanonical(string value)
    {
        if (value.Length is < 1 or >
            WidgetCommunityPlatformLimits.MaximumPrivateStateBase64Characters)
            throw MalformedResponse();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException) { throw MalformedResponse(); }
        if (bytes.Length is < 1 or > WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes ||
            !string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
            throw MalformedResponse();
        var canonical = Canonicalize(bytes, parameterName: null);
        if (!bytes.AsSpan().SequenceEqual(canonical)) throw MalformedResponse();
        return bytes;
    }

    private static byte[] Canonicalize(ReadOnlySpan<byte> utf8, string? parameterName)
    {
        if (utf8.Length is < 1 or > WidgetCommunityPlatformLimits.MaximumPrivateStateInputUtf8Bytes)
            throw InvalidJson(parameterName);
        try
        {
            RejectDuplicateProperties(utf8);
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            using var output = new MemoryStream(Math.Min(
                utf8.Length, WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes));
            using (var writer = new Utf8JsonWriter(output))
                WriteCanonical(writer, document.RootElement);
            var result = output.ToArray();
            if (result.Length > WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
                throw InvalidJson(parameterName);
            return result;
        }
        catch (ArgumentException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            if (parameterName is null) throw MalformedResponse();
            throw new ArgumentException("Private state must be strict, bounded JSON.",
                parameterName, exception);
        }
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                     (objects.Count == 0 ||
                      !objects.Peek().Add(reader.GetString() ?? string.Empty)))
                throw new JsonException("Duplicate JSON property.");
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

    private static void ValidateExpectedRevision(long? revision)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(
                nameof(revision), "Expected revision cannot be negative.");
    }

    private static Exception InvalidJson(string? parameterName) =>
        parameterName is null
            ? MalformedResponse()
            : new ArgumentException("Private state must be strict, bounded JSON.", parameterName);

    private static WidgetCapabilityException MalformedResponse() => new(
        "malformed_response", "The private state provider returned an invalid result.");

    internal static string CanonicalizeForHost(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] source;
        try { source = StrictUtf8.GetBytes(json); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Private state must be valid UTF-8 JSON.", nameof(json), exception);
        }
        if (source.Length is < 1 or >
            WidgetCommunityPlatformLimits.MaximumPrivateStateInputUtf8Bytes)
            throw new ArgumentException("Private state input exceeds its bound.", nameof(json));
        return StrictUtf8.GetString(Canonicalize(source, nameof(json)));
    }
}
