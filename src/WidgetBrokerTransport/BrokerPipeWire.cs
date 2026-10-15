using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace WidgetRail.PlatformBroker;

internal static class BrokerPipeMessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "helloAccepted";
    public const string Request = "request";
    public const string Response = "response";
    public const string Lifecycle = "lifecycle";
    public const string Subscribe = "subscribe";
    public const string Unsubscribe = "unsubscribe";
    public const string Cancel = "cancel";
    public const string Acknowledged = "acknowledged";
    public const string Error = "error";
    public const string Event = "event";
    public const string SubscriptionRevoked = "subscriptionRevoked";
}

internal sealed record BrokerPipeEnvelope
{
    [JsonRequired] public required int ProtocolVersion { get; init; }
    [JsonRequired] public required string Type { get; init; }
    [JsonRequired] public required long CorrelationId { get; init; }
    [JsonRequired] public required JsonElement Payload { get; init; }
    public long? ActionExecutionId { get; init; }
}

internal sealed record BrokerPipeHello(
    [property: JsonRequired] string Nonce,
    [property: JsonRequired] BrokerWidgetIdentity Widget);
internal sealed record BrokerPipeLifecycle(
    [property: JsonRequired] BrokerLifecycleState State);
internal sealed record BrokerPipeSubscriptionRequest(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] string CapabilityId,
    [property: JsonRequired] string EventType);
internal sealed record BrokerPipeUnsubscribe(
    [property: JsonRequired] string SubscriptionId);
internal sealed record BrokerPipeCancel(
    [property: JsonRequired] long RequestCorrelationId);
internal sealed record BrokerPipeError(
    [property: JsonRequired] string Code);
internal sealed record BrokerPipeEvent(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] BrokerEventEnvelope Event);
internal sealed record BrokerPipeSubscriptionRevoked(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] string Code);

internal sealed class BrokerPipeFrameChannel(Stream stream, int maximumFrameBytes)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly int _maximumFrameBytes = maximumFrameBytes is >= 1024 and <= 256 * 1024
        ? maximumFrameBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));

    public async ValueTask WriteAsync(BrokerPipeEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = BrokerPipeJson.Serialize(envelope);
        if (payload.Length == 0 || payload.Length > _maximumFrameBytes)
            throw new BrokerException("frame_too_large", "Broker transport frame exceeds its bound.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BrokerPipeEnvelope> ReadAsync(CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > _maximumFrameBytes)
            throw new BrokerException("frame_too_large", "Broker transport frame length is invalid.");
        var payload = GC.AllocateUninitializedArray<byte>(length);
        await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return BrokerPipeJson.Parse(payload);
    }

    private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Broker transport closed mid-frame.");
            offset += read;
        }
    }
}

internal static class BrokerPipeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = BrokerWireJson.MaximumDepth,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static BrokerPipeJson() => Options.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static BrokerPipeEnvelope Parse(ReadOnlySpan<byte> payload)
    {
        try
        {
            BrokerWireJson.RejectDuplicateProperties(payload);
            var envelope = JsonSerializer.Deserialize<BrokerPipeEnvelope>(payload, Options)
                ?? throw new JsonException("Transport envelope was null.");
            if (envelope.ProtocolVersion != BrokerWireJson.ProtocolVersion)
                throw new BrokerException("unsupported_protocol", "Broker transport protocol is unsupported.");
            if (string.IsNullOrEmpty(envelope.Type) || envelope.Type.Length > 64 ||
                !envelope.Type.All(character => char.IsAsciiLetter(character)))
                throw new BrokerException("malformed_frame", "Broker transport message type is invalid.");
            if (envelope.CorrelationId < 0 || envelope.CorrelationId > 9_007_199_254_740_991L)
                throw new BrokerException("malformed_frame", "Broker transport correlation ID is invalid.");
            BrokerWireJson.ValidateElement(envelope.Payload, "malformed_frame");
            return envelope with { Payload = envelope.Payload.Clone() };
        }
        catch (BrokerException) { throw; }
        catch (JsonException exception)
        {
            throw new BrokerException("malformed_frame", "Broker transport JSON is invalid.", exception);
        }
    }

    public static T Payload<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(Options)
                ?? throw new JsonException("Transport payload was null.");
        }
        catch (JsonException exception)
        {
            throw new BrokerException("malformed_frame", "Broker transport payload is invalid.", exception);
        }
    }

    public static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
