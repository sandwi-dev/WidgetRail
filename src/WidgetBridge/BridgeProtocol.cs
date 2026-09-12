using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public static class BridgeProtocol
{
    public const int CurrentVersion = 1;
    public const int DefaultMaximumMessageBytes = ProtocolConstants.MaximumEncodedArtworkFrameBytes;
    public const int AbsoluteMaximumMessageBytes = ProtocolConstants.MaximumEncodedArtworkFrameBytes;
}

internal static class BridgeMessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "hello-accepted";
    public const string ListWidgets = "list-widgets";
    public const string Widgets = "widgets";
    public const string GetPlatformAppearance = "get-platform-appearance";
    public const string ControllerControl = "controller-control";
    public const string ApplicationControl = "application-control";
    public const string PlatformAppearance = "platform-appearance";
    public const string AppearanceChanged = "platform-appearance-changed";
    public const string CatalogChanged = "widget-catalog-changed";
    public const string GetSnapshot = "get-snapshot";
    public const string ResolveArtwork = "resolve-artwork";
    public const string Artwork = "artwork";
    public const string ResolvePackageIcon = "resolve-package-icon";
    public const string PackageIcon = "package-icon";
    public const string ResolveEmbeddedMedia = "resolve-embedded-media";
    public const string EmbeddedMediaSession = "embedded-media";
    public const string EmbeddedMediaPlaybackEvent = "embedded-media-playback-event";
    public const string RestartWidget = "restart-widget";
    public const string SetWidgetLifecycle = "set-widget-lifecycle";
    public const string Snapshot = "snapshot";
    public const string PresentationUpdate = "presentation-update";
    public const string Action = "action";
    public const string QuickAction = "quick-action";
    public const string ControllerInput = "controller-input";
    public const string ConnectProtectedWifi = "connect-protected-wifi";
    public const string InstallLocalWidgetPackage = "install-local-widget-package";
    public const string CancelLocalWidgetPackageInstall = "cancel-local-widget-package-install";
    public const string LocalWidgetPackageInstallCompleted = "local-widget-package-install-completed";
    public const string ControllerInputResult = "controller-input-result";
    public const string Acknowledged = "acknowledged";
    public const string Invalidation = "widget-invalidated";
    public const string HostEffect = "widget-host-effect";
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
internal sealed record BridgePresentationRequest(
    string WidgetId,
    PresentationUpdateCapabilities? Capabilities = null,
    long BaseSequence = 0,
    WidgetRail.WidgetSdk.WidgetPresentationTransactionKind TransactionKind =
        WidgetRail.WidgetSdk.WidgetPresentationTransactionKind.OrdinaryCheckpoint,
    long RecoveryOriginSequence = 0);
// Current OverlayHost always supplies the paired generation authority. The
// nullable pair preserves the private synchronous pre-WIDGE-192 caller shape.
internal sealed record BridgeArtworkRequest(
    string WidgetId,
    string ArtworkHandle,
    string? RuntimeGeneration = null,
    string? PresentationGeneration = null);
internal sealed record BridgePackageIconRequest(
    string WidgetId,
    string RuntimeGeneration,
    string PresentationGeneration,
    string PackageContentDigest,
    string AssetId,
    string SourceSha256,
    string NormalizedSha256);
internal sealed record BridgeEmbeddedMediaRequest(
    string WidgetId,
    string InstanceId,
    string RuntimeGeneration,
    string PresentationGeneration,
    long Sequence,
    string SessionId);
internal sealed record BridgeEmbeddedMediaResource(
    string Path,
    string ContentType,
    string Sha256,
    string ContentBase64);
internal sealed record BridgeEmbeddedMediaBundle(
    string WidgetId,
    string InstanceId,
    string RuntimeGeneration,
    string PresentationGeneration,
    long Sequence,
    string SessionId,
    string EntryAsset,
    WidgetSurfaceHints Surface,
    double AspectRatio,
    string AccessibleName,
    IReadOnlyList<EmbeddedMediaCommand> Commands,
    IReadOnlyList<string> AllowedFrameOrigins,
    IReadOnlyList<string> AllowedFrameDomainFamilies,
    EmbeddedMediaPlaybackCommand? PendingCommand,
    IReadOnlyList<MediaPresentationKind> SupportedPresentations,
    double? MediaSeekStepSeconds,
    IReadOnlyList<BridgeEmbeddedMediaResource> Resources);
internal sealed record BridgeEmbeddedMediaPlaybackEventRequest(
    string WidgetId,
    string InstanceId,
    string RuntimeGeneration,
    string PresentationGeneration,
    long Sequence,
    EmbeddedMediaPlaybackEvent Event);
internal sealed record BridgeWidgetLifecycleRequest(
    string WidgetId,
    WidgetRail.WidgetSdk.WidgetLifecycleState State,
    BridgePresentationEstablishment? Presentation = null);
internal sealed record BridgePresentationEstablishment(
    PresentationUpdateCapabilities? Capabilities,
    long BaseSequence,
    WidgetRail.WidgetSdk.WidgetPresentationTransactionKind TransactionKind =
        WidgetRail.WidgetSdk.WidgetPresentationTransactionKind.OrdinaryCheckpoint,
    long RecoveryOriginSequence = 0);
internal sealed record BridgeActionRequest(string WidgetId, WidgetRail.WidgetSdk.WidgetActionEvent Action);
internal sealed record BridgeQuickActionRequest(string WidgetId, string QuickActionId, long Sequence = 0, long MonotonicTimestampMicroseconds = 0);
internal sealed record BridgeControllerInputRequest(
    string WidgetId,
    WidgetRail.WidgetSdk.ControllerInputEvent Input,
    string? RuntimeGeneration = null,
    string? ExpectedActionId = null,
    string? ExpectedSelectOptionActionId = null);
internal sealed record BridgeProtectedWifiRequest(
    string WidgetId,
    string RuntimeGeneration,
    string SourceElementId,
    int SecretLength);
internal sealed record BridgeLocalWidgetPackageOrigin(
    string WidgetId,
    string PackageId,
    string PublisherId,
    string InstanceId,
    string RuntimeGeneration,
    string PresentationGeneration);
internal sealed record BridgeLocalWidgetPackageInstallRequest(
    string OperationId,
    string PackagePath,
    BridgeLocalWidgetPackageOrigin Origin);
internal sealed record BridgeLocalWidgetPackageInstallCancelRequest(string OperationId);
internal sealed record BridgeLocalWidgetPackageInstallCompleted(
    string OperationId,
    string Status,
    string WidgetId,
    string Version,
    string Message);
internal sealed record BridgeInvalidation(string WidgetId, long Revision);
internal sealed record BridgeHostEffect(
    string WidgetId,
    string RuntimeGeneration,
    string Effect,
    long Sequence,
    long InitiatedAtMilliseconds = 0);
internal sealed record BridgeAppearanceChanged(long Revision);
internal sealed record BridgeCatalogChangedEvent(long Revision);
internal sealed record BridgeError(string Code, string Message);
internal sealed record BridgeEncodedArtworkEvent(
    string WidgetId,
    string ArtworkHandle,
    string RuntimeGeneration,
    string PresentationGeneration,
    string ContentType,
    ReadOnlyMemory<byte> ContentBase64);
internal sealed record BridgeLegacyArtworkEvent(
    string WidgetId,
    string ArtworkHandle,
    string RuntimeGeneration,
    string PresentationGeneration,
    string ContentType,
    string ContentBase64);

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
        options.Converters.Add(new EmbeddedMediaCommandJsonConverter());
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
        await WriteSerializedAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync<T>(
        string type,
        long requestId,
        T payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);
        await WriteSerializedAsync(
            new BridgeOutgoingEnvelope<T>
            {
                Type = type,
                RequestId = requestId,
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteSerializedAsync<T>(
        T envelope,
        CancellationToken cancellationToken)
    {
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

    internal async ValueTask<BridgeProtectedWifiSecret> ReadProtectedWifiSecretAsync(
        int expectedLength,
        CancellationToken cancellationToken)
    {
        if (expectedLength is < 8 or > 63)
            throw new BridgeProtocolException("Protected Wi-Fi secret length is invalid.");
        var header = new byte[sizeof(int)];
        byte[]? bytes = new byte[expectedLength];
        try
        {
            await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length != expectedLength)
                throw new BridgeProtocolException(
                    "Protected Wi-Fi secret frame length did not match its metadata.");
            await _stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var result = BridgeProtectedWifiSecret.DecodeOwned(bytes);
            bytes = null;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

internal sealed record BridgeOutgoingEnvelope<T>
{
    public int ProtocolVersion { get; init; } = BridgeProtocol.CurrentVersion;
    public required string Type { get; init; }
    public long RequestId { get; init; }
    public required T Payload { get; init; }
}

internal sealed class BridgeProtectedWifiSecret : IDisposable
{
    private char[] _characters;

    internal BridgeProtectedWifiSecret(char[] characters) =>
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));

    internal static BridgeProtectedWifiSecret DecodeOwned(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        char[]? characters = null;
        try
        {
            characters = new char[bytes.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] is < 32 or > 126)
                    throw new BridgeProtocolException(
                        "Protected Wi-Fi secret contains an invalid character.");
                characters[index] = (char)bytes[index];
            }
            var result = new BridgeProtectedWifiSecret(characters);
            characters = null;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (characters is not null) Array.Clear(characters);
        }
    }

    internal char[] Characters => _characters;

    public void Dispose()
    {
        var characters = Interlocked.Exchange(ref _characters, []);
        Array.Clear(characters);
    }
}

public sealed class BridgeProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class BridgeStaleControllerInputAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStalePinnedInputAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStaleArtworkAuthorityException(string message)
    : Exception(message);
internal sealed class BridgeStalePackageIconAuthorityException(string message)
    : Exception(message);

internal sealed class BridgeStalePresentationBaseException()
    : Exception("The retained presentation base is not current in the widget bridge.");
