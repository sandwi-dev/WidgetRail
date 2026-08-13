using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

internal static class WidgetRuntimeProtocol
{
    public const int CurrentVersion = 2;
    public const int DefaultMaximumMessageBytes = 1_048_576;
    public const int AbsoluteMaximumMessageBytes = 4_194_304;
}

internal static class MessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "hello-accepted";
    public const string Render = "render";
    public const string SetWidgetLifecycle = "set-widget-lifecycle";
    public const string Snapshot = "snapshot";
    public const string Action = "action";
    public const string ControllerInput = "controller-input";
    public const string ControllerInputResult = "controller-input-result";
    public const string DashboardGestureActivationRequested = "dashboard-gesture-activation-requested";
    public const string DashboardGestureActivationResult = "dashboard-gesture-activation-result";
    public const string Acknowledged = "acknowledged";
    public const string Invalidated = "invalidated";
    public const string ControllerActionFailed = "controller-action-failed";
    public const string Error = "error";
    public const string Stop = "stop";
}

internal sealed record RuntimeEnvelope
{
    public int ProtocolVersion { get; init; } = WidgetRuntimeProtocol.CurrentVersion;
    public required string Type { get; init; }
    public long RequestId { get; init; }
    public required JsonElement Payload { get; init; }
}

internal sealed record HelloPayload(string WidgetInstanceId, string SessionNonce);
internal sealed record WidgetLifecyclePayload(WidgetLifecycleState State);
internal sealed record InvalidationPayload(long Revision);
internal sealed record ActionAdmissionPayload(WidgetOperationAdmission Admission);
internal sealed record ControllerActionFailurePayload(
    string ActionId,
    string SourceElementId,
    string Message);
internal sealed record ErrorPayload(string Code, string Message);
internal sealed record ControllerInputResultPayload(bool Handled);
internal sealed record DashboardGestureActivationRequestPayload(
    long ActivationId,
    string CapabilityId,
    string OperationId,
    long InputSequence,
    long SnapshotSequence);
internal sealed record DashboardGestureActivationResultPayload(
    long ActivationId,
    bool Authorized);

internal static class RuntimeJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T FromElement<T>(JsonElement value) =>
        value.Deserialize<T>(Options) ?? throw new JsonException($"A {typeof(T).Name} payload was null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal sealed class LengthPrefixedJsonChannel(Stream stream, int maximumMessageBytes)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly int _maximumMessageBytes = ValidateMaximum(maximumMessageBytes);

    public async ValueTask WriteAsync(RuntimeEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, RuntimeJson.Options);
        if (payload.Length == 0 || payload.Length > _maximumMessageBytes)
            throw new WidgetProtocolViolationException($"Message length {payload.Length} is outside the permitted range.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RuntimeEnvelope> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > _maximumMessageBytes)
            throw new WidgetProtocolViolationException($"Peer announced invalid message length {length}.");

        var payload = new byte[length];
        await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<RuntimeEnvelope>(payload, RuntimeJson.Options)
            ?? throw new WidgetProtocolViolationException("Peer sent a null message.");
        if (envelope.ProtocolVersion != WidgetRuntimeProtocol.CurrentVersion)
            throw new WidgetProtocolViolationException(
                $"Unsupported runtime protocol {envelope.ProtocolVersion}; expected {WidgetRuntimeProtocol.CurrentVersion}.");
        if (string.IsNullOrWhiteSpace(envelope.Type) || envelope.Type.Length > 64)
            throw new WidgetProtocolViolationException("Message type is missing or too long.");
        if (envelope.RequestId < 0)
            throw new WidgetProtocolViolationException("Request ID cannot be negative.");
        return envelope;
    }

    private static int ValidateMaximum(int value)
    {
        if (value is < 256 or > WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}

internal sealed class WidgetProtocolViolationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
