using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json.Serialization.Metadata;
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
    public const string WindowPreviewPermissions = "window-preview-permissions";
    public const string ApplicationControl = "application-control";
    public const string CompleteTaskActivation = "complete-task-activation";
    public const string PlatformAppearance = "platform-appearance";
    public const string AppearanceChanged = "platform-appearance-changed";
    public const string CatalogChanged = "widget-catalog-changed";
    public const string GetSnapshot = "get-snapshot";
    public const string ReadIndexedRange = "read-indexed-range";
    public const string IndexedRange = "indexed-range";
    public const string CancelIndexedRange = "cancel-indexed-range";
    public const string AcquireIndexedRange = "acquire-indexed-range";
    public const string IndexedLease = "indexed-lease";
    public const string ReleaseIndexedLease = "release-indexed-lease";
    public const string RefreshIndexedStyles = "refresh-indexed-styles";
    public const string IndexedStyles = "indexed-styles";
    public const string RefreshPresentationStyles = "refresh-presentation-styles";
    public const string PresentationStyles = "presentation-styles";
    public const string IndexedInput = "indexed-input";
    public const string ResolveIndexedArtwork = "resolve-indexed-artwork";
    public const string CancelIndexedArtwork = "cancel-indexed-artwork";
    public const string IndexedArtwork = "indexed-artwork";
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
    public const string PinnedAction = "pinned-action-v1";
    public const string ResolvePinnedArtwork = "resolve-pinned-artwork-v1";
    public const string QuickAction = "quick-action";
    public const string ControllerInput = "controller-input";
    public const string ConnectProtectedWifi = "connect-protected-wifi";
    public const string InstallLocalWidgetPackage = "install-local-widget-package";
    public const string CancelLocalWidgetPackageInstall = "cancel-local-widget-package-install";
    public const string ApproveLocalWidgetPackageInstall = "approve-local-widget-package-install";
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

internal sealed record BridgeHello(string ClientName, bool WindowPreviews = false,
    bool ExclusiveControllerControl = true, bool HeldDpadScroll = true, bool StartupRegistration = true);
internal sealed record WidgetIdRequest(string WidgetId);
internal sealed record BridgeEmptyPayload;
internal sealed record BridgeDisplayIdentity(string Id, string Name, IReadOnlyList<string> DevicePaths);
internal sealed record BridgeAppearanceRequest(BridgeDisplayIdentity Display);
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
    string? PresentationGeneration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemandId = null);
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
    EmbeddedMediaPlaybackEvent Event,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeWorkerRun? WorkerRun = null);
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
internal sealed record BridgePinnedActionRequest(string WidgetId, string InstanceId, string RuntimeGeneration,
    string PresentationGeneration, WidgetRail.WidgetSdk.PinnedActionInput Input, BridgeWorkerRun? WorkerRun = null);
internal sealed record BridgePinnedArtworkRequest(int Version, string WidgetId, string InstanceId, string RuntimeGeneration,
    string PresentationGeneration, string LayoutId, long SnapshotSequence, string InputScopeId, string ArtworkHandle, string DemandId);
internal sealed record BridgeActionRequest(string WidgetId, WidgetRail.WidgetSdk.WidgetActionEvent Action, BridgeWorkerRun? WorkerRun = null);
internal sealed record BridgeQuickActionRequest(string WidgetId, string QuickActionId, long Sequence = 0, long MonotonicTimestampMicroseconds = 0);
internal sealed record BridgeControllerInputRequest(
    string WidgetId,
    WidgetRail.WidgetSdk.ControllerInputEvent Input,
    string? RuntimeGeneration = null,
    string? ExpectedActionId = null,
    string? ExpectedSelectOptionActionId = null,
    BridgeWorkerRun? WorkerRun = null);
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
    string PresentationGeneration,
    string? UpdateTargetHash = null);
internal sealed record BridgeLocalWidgetPackageInstallRequest(
    string OperationId,
    string PackagePath,
    BridgeLocalWidgetPackageOrigin Origin);
internal sealed record BridgeLocalWidgetPackageInstallCancelRequest(string OperationId);
internal sealed record BridgeLocalWidgetPackageInstallApprovalRequest(string OperationId, bool Approved);
internal sealed record BridgeLocalWidgetPackageInstallCompleted(
    string OperationId,
    string Status,
    string WidgetId,
    string Version,
    string Message);
internal sealed record BridgeInvalidation(string WidgetId, long Revision);
internal sealed record BridgeAppearanceChanged(long Revision);
internal sealed record BridgeCatalogChangedEvent(long Revision);
internal sealed record BridgeError(string Code, string Message);
internal sealed record BridgeEncodedArtworkEvent(
    string WidgetId,
    string ArtworkHandle,
    string RuntimeGeneration,
    string PresentationGeneration,
    string ContentType,
    ReadOnlyMemory<byte> ContentBase64,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemandId = null);
internal sealed record BridgeLegacyArtworkEvent(
    string WidgetId,
    string ArtworkHandle,
    string RuntimeGeneration,
    string PresentationGeneration,
    string ContentType,
    string ContentBase64,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemandId = null);

internal static class BridgeJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, TypeInfo<T>());
    public static T FromElement<T>(JsonElement element)
    {
        var result = element.Deserialize(TypeInfo<T>()) ?? throw new JsonException($"A {typeof(T).Name} payload was null.");
        // Keep additive catalog omissions equivalent to the immutable model's
        // initializers. Explicit null remains invalid at admission.
        if (result is BridgeWidgetDescriptor[] widgets)
            for (var index = 0; index < widgets.Length; ++index)
            {
                var widget = widgets[index];
                if (widget is null) continue;
                var raw = element[index];
                if (widget.IconAssets is null && !raw.TryGetProperty("iconAssets", out _)) widget = widget with { IconAssets = [] };
                if (widget.QuickActions is null && !raw.TryGetProperty("quickActions", out _)) widget = widget with { QuickActions = [] };
                widgets[index] = widget;
            }
        return result;
    }

    internal static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        // Trusted Bridge tools still support their existing reflection-based
        // diagnostic payloads. Trimmed hosts use only the generated wire contracts.
        options.TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault
            ? new DefaultJsonTypeInfoResolver()
            : BridgeJsonContext.Default;
        options.Converters.Add(new EmbeddedMediaCommandJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal sealed class BridgeFrameChannel(Stream stream, int maximumMessageBytes)
{
    // One bounded scratch allocation per channel; the returned JsonElement
    // owns its own data. Large exceptional frames never raise this high-water
    // mark, and no process-global pool retains buffers from retired channels.
    private const int MaximumRetainedReadBufferBytes = 1024 * 1024;
    private byte[]? _readBuffer;
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
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", BridgeProtocol.CurrentVersion);
            writer.WriteString("type", type);
            writer.WriteNumber("requestId", requestId);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, BridgeJson.TypeInfo<T>());
            writer.WriteEndObject();
        }
        await WriteBytesAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask WriteSerializedAsync(BridgeEnvelope envelope, CancellationToken cancellationToken) =>
        WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, BridgeJson.TypeInfo<BridgeEnvelope>()), cancellationToken);

    private async ValueTask WriteBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
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
        var bytes = Interlocked.Exchange(ref _readBuffer, null);
        if (bytes is null || bytes.Length < length) bytes = new byte[length];
        try
        {
            await _stream.ReadExactlyAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes.AsSpan(0, length), BridgeJson.TypeInfo<BridgeEnvelope>())
                ?? throw new BridgeProtocolException("Peer sent a null bridge message.");
            if (envelope.ProtocolVersion == 0)
            {
                // Preserve the existing additive-envelope default only for omission;
                // an explicitly supplied unsupported version still fails below.
                using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
                if (!document.RootElement.TryGetProperty("protocolVersion", out _))
                    envelope = envelope with { ProtocolVersion = BridgeProtocol.CurrentVersion };
            }
            if (envelope.ProtocolVersion != BridgeProtocol.CurrentVersion)
                throw new BridgeProtocolException(
                    $"Unsupported bridge protocol {envelope.ProtocolVersion}; expected {BridgeProtocol.CurrentVersion}.");
            if (string.IsNullOrWhiteSpace(envelope.Type) || envelope.Type.Length > 64)
                throw new BridgeProtocolException("Bridge message type is invalid.");
            if (envelope.RequestId < 0)
                throw new BridgeProtocolException("Bridge request ID cannot be negative.");
            return envelope;
        }
        finally
        {
            Array.Clear(bytes);
            if (bytes.Length <= MaximumRetainedReadBufferBytes)
                Interlocked.CompareExchange(ref _readBuffer, bytes, null);
        }
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

internal sealed record BridgeIndexedRangeRequest(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionRangeRequest Range);
internal sealed record BridgeIndexedRangeResponse(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionRange Range);
internal sealed record BridgeIndexedLeaseResponse(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionLease Lease,
    [property: JsonRequired] IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles,
    long AppearanceRevision = 0);
// Trusted Bridge publication identity; independent of package fingerprints and
// of snapshot sequence numbers that restart with each worker process.
public sealed record BridgeWorkerRun(long RegistryGeneration, int StartOrdinal);

internal sealed record BridgeResolvedStyleSnapshot(long Revision, IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles);
internal sealed record BridgePresentationStylesRequest(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration, long SnapshotSequence);
internal sealed record BridgePresentationStylesResponse(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration, long SnapshotSequence,
    long AppearanceRevision, [property: JsonRequired] IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles);
internal sealed record BridgeIndexedStylesResponse(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration, string LeaseId,
    long AppearanceRevision, [property: JsonRequired] IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles);
internal sealed record BridgeIndexedLeaseRequest(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration, string LeaseId);
internal sealed record BridgeIndexedInputRequest(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionInputRequest Input, IndexedCollectionInputContext Context);
internal sealed record BridgeIndexedArtworkRequest(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionItemReference Item, string ArtworkHandle, string DemandId);
internal sealed record BridgeIndexedArtworkResponse(
    string WidgetId, string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionItemReference Item, string ArtworkHandle, string DemandId,
    string ContentType, ReadOnlyMemory<byte> ContentBase64);
