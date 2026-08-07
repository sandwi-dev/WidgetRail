using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetBridge;

public static class BridgeProtocol
{
    public const int CurrentVersion = 1;
    public const int DefaultMaximumMessageBytes = 1_048_576;
    public const int AbsoluteMaximumMessageBytes = 4_194_304;
}

internal static class BridgeMessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "hello-accepted";
    public const string ListWidgets = "list-widgets";
    public const string Widgets = "widgets";
    public const string GetSnapshot = "get-snapshot";
    public const string SetWidgetLifecycle = "set-widget-lifecycle";
    public const string Snapshot = "snapshot";
    public const string Action = "action";
    public const string QuickAction = "quick-action";
    public const string ControllerInput = "controller-input";
    public const string ControllerInputResult = "controller-input-result";
    public const string Acknowledged = "acknowledged";
    public const string Invalidation = "widget-invalidated";
    public const string Failure = "widget-failed";
    public const string Error = "error";
    public const string Stop = "stop";
}

internal sealed record BridgeEnvelope
{
    public int ProtocolVersion { get; init; } = BridgeProtocol.CurrentVersion;
    public required string Type { get; init; }
    public long RequestId { get; init; }
    public required JsonElement Payload { get; init; }
}

internal sealed record BridgeHello(string ClientName);
internal sealed record WidgetIdRequest(string WidgetId);
internal sealed record BridgeWidgetLifecycleRequest(
    string WidgetId,
    GameBarAlternative.WidgetSdk.WidgetLifecycleState State);
internal sealed record BridgeActionRequest(string WidgetId, GameBarAlternative.WidgetSdk.WidgetActionEvent Action);
internal sealed record BridgeQuickActionRequest(string WidgetId, string QuickActionId, long Sequence = 0, long MonotonicTimestampMicroseconds = 0);
internal sealed record BridgeControllerInputRequest(string WidgetId, GameBarAlternative.WidgetSdk.ControllerInputEvent Input);
internal sealed record BridgeInvalidation(string WidgetId, long Revision);
internal sealed record BridgeError(string Code, string Message);

internal static class BridgeJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T FromElement<T>(JsonElement element) =>
        element.Deserialize<T>(Options) ?? throw new JsonException($"A {typeof(T).Name} payload was null.");

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

internal sealed class BridgeFrameChannel(Stream stream, int maximumMessageBytes)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly int _maximumMessageBytes = maximumMessageBytes is >= 256 and <= BridgeProtocol.AbsoluteMaximumMessageBytes
        ? maximumMessageBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));

    public async ValueTask WriteAsync(BridgeEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, BridgeJson.Options);
        if (bytes.Length is <= 0 || bytes.Length > _maximumMessageBytes)
            throw new BridgeProtocolException($"Bridge message length {bytes.Length} is invalid.");
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BridgeEnvelope> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > _maximumMessageBytes)
            throw new BridgeProtocolException($"Peer announced invalid bridge message length {length}.");
        var bytes = new byte[length];
        await _stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<BridgeEnvelope>(bytes, BridgeJson.Options)
            ?? throw new BridgeProtocolException("Peer sent a null bridge message.");
        if (envelope.ProtocolVersion != BridgeProtocol.CurrentVersion)
            throw new BridgeProtocolException(
                $"Unsupported bridge protocol {envelope.ProtocolVersion}; expected {BridgeProtocol.CurrentVersion}.");
        if (string.IsNullOrWhiteSpace(envelope.Type) || envelope.Type.Length > 64)
            throw new BridgeProtocolException("Bridge message type is invalid.");
        if (envelope.RequestId < 0)
            throw new BridgeProtocolException("Bridge request ID cannot be negative.");
        return envelope;
    }
}

public sealed class BridgeProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
