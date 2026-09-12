using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal static class WidgetRuntimeProtocol
{
    public const int CurrentVersion = 2;
    public const int DefaultMaximumMessageBytes = ProtocolConstants.MaximumEncodedArtworkFrameBytes;
    public const int AbsoluteMaximumMessageBytes = ProtocolConstants.MaximumEncodedArtworkFrameBytes;
    internal const int MaximumWorkerDiagnosticMessageLength = 512;
    internal const int MaximumProtocolValidationDiagnosticMessageLength = 640;
}

internal static class MessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "hello-accepted";
    public const string Render = "render";
    public const string SetWidgetLifecycle = "set-widget-lifecycle";
    public const string Snapshot = "snapshot";
    public const string PresentationUpdate = "presentation-update";
    public const string Action = "action";
    public const string ControllerInput = "controller-input";
    public const string ControllerInputResult = "controller-input-result";
    // Separate request keeps the strict runtime-v2 handshake and existing
    // input payloads compatible with packaged application workers.
    public const string RevalidatedControllerInput = "revalidated-controller-input";
    public const string RevalidatedControllerInputResult = "revalidated-controller-input-result";
    public const string EmbeddedMediaPlaybackEvent = "embedded-media-playback-event";
    public const string ResolveArtwork = "resolve-artwork";
    public const string Artwork = "artwork";
    public const string DashboardGestureActivationRequested = "dashboard-gesture-activation-requested";
    public const string DashboardGestureActivationResult = "dashboard-gesture-activation-result";
    public const string Acknowledged = "acknowledged";
    public const string Invalidated = "invalidated";
    public const string ActionTerminal = "action-terminal";
    public const string ControllerActionFailed = "controller-action-failed";
    public const string Error = "error";
    public const string Stop = "stop";
}

internal static class WorkerErrorCodes
{
    internal const string RequestFailed = "worker_request_failed";
    internal const string ProtocolValidationFailed = "worker_protocol_validation_failed";
}

internal sealed record RuntimeEnvelope
{
    public int ProtocolVersion { get; init; } = WidgetRuntimeProtocol.CurrentVersion;
    public required string Type { get; init; }
    public long RequestId { get; init; }
    public required JsonElement Payload { get; init; }
}

internal sealed record HelloPayload(string WidgetInstanceId, string SessionNonce);
internal sealed record HelloAcceptedPayload(bool SupportsActionTerminals = false);
internal sealed record WidgetLifecyclePayload(WidgetLifecycleState State);
internal sealed record RenderPayload
{
    public PresentationUpdateCapabilities? UpdateCapabilities { get; init; }
    public long BaseSequence { get; init; }
    public string? PresentationGeneration { get; init; }
    public bool RequireCheckpoint { get; init; } = true;
}
internal sealed record RuntimeRenderRequest(
    WidgetPresentationTransactionKind TransactionKind,
    long BaseSequence,
    long RecoveryOriginSequence,
    string? PresentationGeneration,
    PresentationUpdateCapabilities UpdateCapabilities);
internal sealed record WidgetRuntimePresentation(
    WidgetPresentationTransactionKind TransactionKind,
    long RequestBaseSequence,
    long RecoveryOriginSequence,
    WidgetRail.WidgetProtocol.ViewSnapshot Snapshot,
    WidgetRail.WidgetProtocol.PresentationUpdateBatch? Update);
internal sealed record InvalidationPayload(long Revision);
internal sealed record ActionAdmissionPayload(WidgetOperationAdmission Admission);
internal sealed record ActionTerminalPayload(
    long ExecutionId,
    WidgetActionExecutionOutcome Outcome);
internal sealed record ControllerActionFailurePayload(
    string ActionId,
    string SourceElementId,
    string Message);
internal sealed record ErrorPayload(string Code, string Message);
internal sealed record ControllerInputResultPayload(bool Handled);
// Null means rejected before invoking any widget handler, never unhandled.
internal sealed record RevalidatedControllerInputPayload(ControllerInputEvent Input, string? ActionId);
internal sealed record RevalidatedControllerInputResultPayload(bool? Handled);
internal sealed record ResolveArtworkPayload(string ArtworkHandle);
internal sealed record EncodedArtworkPayload(string? ContentType, string? ContentBase64);
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
    private static readonly byte[] ArtworkSuffix = "\"}}"u8.ToArray();
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly int _maximumMessageBytes = ValidateMaximum(maximumMessageBytes);
    private bool _writeFaulted;

    public async ValueTask WriteAsync(RuntimeEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfWriteFaulted();
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, RuntimeJson.Options);
        if (payload.Length == 0 || payload.Length > _maximumMessageBytes)
            throw new WidgetProtocolViolationException($"Message length {payload.Length} is outside the permitted range.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _writeFaulted = true;
            throw;
        }
    }

    internal async ValueTask WriteArtworkAsync(
        long requestId,
        string contentType,
        ReadOnlyMemory<byte> artworkBytes,
        CancellationToken cancellationToken)
    {
        ThrowIfWriteFaulted();
        if (requestId < 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (artworkBytes.IsEmpty) throw new ArgumentException(
            "Artwork bytes cannot be empty.", nameof(artworkBytes));

        var contentTypeJson = JsonSerializer.Serialize(contentType, RuntimeJson.Options);
        var prefix = Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":" + WidgetRuntimeProtocol.CurrentVersion
            .ToString(CultureInfo.InvariantCulture) +
            ",\"type\":\"artwork\",\"requestId\":" +
            requestId.ToString(CultureInfo.InvariantCulture) +
            ",\"payload\":{\"contentType\":" + contentTypeJson +
            ",\"contentBase64\":\"");
        var encodedLength = checked(((long)artworkBytes.Length + 2L) / 3L * 4L);
        var frameLength = checked((long)prefix.Length + encodedLength + ArtworkSuffix.Length);
        if (frameLength <= 0 || frameLength > _maximumMessageBytes)
            throw new WidgetProtocolViolationException(
                $"Message length {frameLength} is outside the permitted range.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, (int)frameLength);
        var encoded = new byte[16 * 1024];
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            var offset = 0;
            while (offset < artworkBytes.Length)
            {
                var remaining = artworkBytes.Length - offset;
                var count = Math.Min(12 * 1024, remaining);
                if (count < remaining) count -= count % 3;
                var final = count == remaining;
                var written = EncodeBase64(
                    artworkBytes.Slice(offset, count), encoded, final);
                await _stream.WriteAsync(
                    encoded.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
                offset += count;
            }
            await _stream.WriteAsync(ArtworkSuffix, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _writeFaulted = true;
            throw;
        }
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

    private static int EncodeBase64(
        ReadOnlyMemory<byte> source,
        byte[] destination,
        bool isFinalBlock)
    {
        var status = Base64.EncodeToUtf8(
            source.Span, destination, out var consumed, out var written,
            isFinalBlock);
        if (status != OperationStatus.Done || consumed != source.Length)
            throw new InvalidOperationException("Artwork Base64 encoding did not complete.");
        return written;
    }

    private void ThrowIfWriteFaulted()
    {
        if (_writeFaulted)
            throw new InvalidOperationException(
                "The runtime channel cannot be reused after a partial write.");
    }
}

internal sealed class WidgetProtocolViolationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
