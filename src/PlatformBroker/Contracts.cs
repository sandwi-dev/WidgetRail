using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformBroker;

public sealed record BrokerWidgetIdentity(
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string PublisherId,
    [property: JsonRequired] string InstanceId)
{
    public void Validate()
    {
        if (string.IsNullOrEmpty(PackageId) || string.IsNullOrEmpty(PublisherId) ||
            string.IsNullOrEmpty(InstanceId) ||
            !ContractValidation.PackageId().IsMatch(PackageId) ||
            !ContractValidation.PackageId().IsMatch(PublisherId) ||
            !ContractValidation.InstanceId().IsMatch(InstanceId))
            throw new BrokerException("invalid_identity", "Widget identity is invalid.");
    }
}

public enum BrokerLifecycleState
{
    /// <summary>
    /// The authenticated channel exists, but the host has not activated the
    /// widget yet. No capability operation is available in this state.
    /// </summary>
    Created,
    Background,
    Visible,
    Interactive,
    Destroying,
}

public enum ConsentDecision
{
    Grant,
    Deny,
}

public sealed record AudioSessionSummary(
    string SessionId,
    string DisplayName,
    double Volume,
    bool IsMuted,
    bool IsActive);

public sealed record SetAudioSessionVolumeRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] double Volume);
public sealed record SetAudioSessionMutedRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] bool IsMuted);

public sealed record AudioOutputSummary(
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted);
public sealed record SetAudioOutputVolumeRequest(
    [property: JsonRequired] double Volume);
public sealed record SetAudioOutputMutedRequest(
    [property: JsonRequired] bool IsMuted);

public enum AudioDeviceDirection
{
    Output,
    Input,
}

/// <summary>Sanitized active endpoint. DeviceId is host-generated and contains no endpoint ID.</summary>
public sealed record AudioDeviceSummary(
    string DeviceId,
    string DisplayName,
    AudioDeviceDirection Direction,
    bool IsDefault);

public sealed record AudioInputSummary(
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted);
public sealed record SetAudioInputVolumeRequest(
    [property: JsonRequired] double Volume);
public sealed record SetAudioInputMutedRequest(
    [property: JsonRequired] bool IsMuted);

public enum NetworkConnectivity
{
    None,
    Local,
    Internet,
}

public enum NetworkTransportKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum NetworkWirelessAvailability
{
    Available,
    NoAdapter,
    RadioOff,
    ServiceUnavailable,
}

public enum NetworkDetailsAccess
{
    Available,
    PrivacyRestricted,
    Unavailable,
}

public enum NetworkConnectionAttemptState
{
    None,
    Connecting,
    Failed,
}

public sealed record NetworkStatusSummary(
    NetworkConnectivity Connectivity,
    NetworkTransportKind Transport,
    NetworkWirelessAvailability WirelessAvailability,
    NetworkDetailsAccess DetailsAccess,
    NetworkConnectionAttemptState ConnectionAttemptState,
    string? AttemptProfileId,
    string? ActiveProfileId,
    string? ActiveProfileName,
    int? SignalPercent);

public enum NetworkConnectionDetailsState
{
    Available,
    Offline,
    Ambiguous,
    PrivacyDenied,
    Unavailable,
}

public enum NetworkConnectionDetailsConnectivity
{
    None,
    Local,
    Constrained,
    Internet,
}

public sealed record NetworkConnectionDetailsSummary(
    long Revision,
    NetworkConnectionDetailsState State,
    NetworkConnectionDetailsConnectivity Connectivity,
    NetworkTransportKind Transport,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> DefaultGateways,
    IReadOnlyList<string> DnsServers);

public sealed record NetworkConnectionDetailsChangedEvent(long Revision);

public sealed record SavedNetworkProfileSummary(
    string ProfileId,
    string DisplayName,
    bool IsConnected,
    int? SignalPercent);

public sealed record SwitchSavedNetworkProfileRequest(
    [property: JsonRequired] string ProfileId);

public enum WifiScanState
{
    NotScanned,
    Scanning,
    Ready,
    PreciseLocationDenied,
    Unavailable,
}

public enum WifiSecurityKind
{
    Open,
    Personal,
    Enterprise,
    Unknown,
}

/// <summary>
/// A visible network from one bounded Native Wifi scan. NetworkId is an opaque,
/// scan-generation-bound token; it never contains an SSID, BSSID, or profile name.
/// </summary>
public sealed record AvailableWifiNetworkSummary(
    string NetworkId,
    string DisplayName,
    int SignalPercent,
    WifiSecurityKind Security,
    bool CredentialRequired,
    bool IsConnected,
    bool HasSavedProfile);

public sealed record AvailableWifiNetworksSummary(
    WifiScanState ScanState,
    IReadOnlyList<AvailableWifiNetworkSummary> Networks);

public sealed record ConnectAvailableWifiNetworkRequest(
    [property: JsonRequired] string NetworkId);

/// <summary>
/// Closed trusted-host result for a protected Personal Wi-Fi attempt. This
/// contract is not exposed through widget capability dispatch.
/// </summary>
public enum ProtectedWifiConnectionStatus
{
    Connecting,
    Rejected,
}

public sealed record ProtectedWifiConnectionResult(
    ProtectedWifiConnectionStatus Status,
    string Code);

public sealed record AudioSessionsChangedEvent(
    IReadOnlyList<AudioSessionSummary> Sessions,
    bool IsAvailable = true);
public sealed record AudioOutputChangedEvent(
    AudioOutputSummary? Output,
    bool IsAvailable = true);
public sealed record AudioDevicesChangedEvent(
    IReadOnlyList<AudioDeviceSummary> Devices,
    bool IsAvailable = true);
public sealed record AudioInputChangedEvent(
    AudioInputSummary? Input,
    bool IsAvailable = true);
public sealed record NetworkStatusChangedEvent(NetworkStatusSummary Status);
public sealed record AvailableWifiNetworksChangedEvent(AvailableWifiNetworksSummary Snapshot);

public enum WifiRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public sealed record WifiRadioSummary(WifiRadioState State, bool CanControl);
public sealed record SetWifiRadioStateRequest([property: JsonRequired] bool Enabled);
public sealed record WifiRadioChangedEvent(WifiRadioSummary Radio);

public enum BluetoothRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public enum BluetoothDiscoveryState
{
    Enumerating,
    Ready,
    Unavailable,
}

/// <summary>
/// Sanitized Bluetooth association endpoint. DeviceId is a host-generated,
/// process-lifetime token and never contains a native device ID, address, or handle.
/// </summary>
public sealed record BluetoothDeviceSummary(
    string DeviceId,
    string DisplayName,
    bool IsPaired,
    bool IsConnected,
    bool IsPresent);

public sealed record BluetoothSummary(
    BluetoothRadioState RadioState,
    bool CanControlRadio,
    BluetoothDiscoveryState DiscoveryState,
    IReadOnlyList<BluetoothDeviceSummary> Devices);

public sealed record SetBluetoothRadioStateRequest([property: JsonRequired] bool Enabled);
public sealed record BluetoothChangedEvent(BluetoothSummary Snapshot);

public enum BluetoothPairingResultStatus
{
    Paired,
    AlreadyPaired,
    NotReady,
    Rejected,
    TooManyConnections,
    HardwareFailure,
    AuthenticationTimedOut,
    AuthenticationNotAllowed,
    AuthenticationFailed,
    NoSupportedProfiles,
    ProtectionLevelNotMet,
    AccessDenied,
    InvalidCeremonyData,
    CanceledByUser,
    OperationInProgress,
    UserInteractionRequired,
    RemoteAlreadyAssociated,
    DeviceUnavailable,
    Failed,
}

public sealed record PairBluetoothDeviceRequest(
    [property: JsonRequired] string DeviceId);
public sealed record BluetoothPairingResultSummary(
    [property: JsonRequired] BluetoothPairingResultStatus Outcome);
public enum BluetoothUnpairingResultStatus
{
    Unpaired,
    AlreadyUnpaired,
    OperationInProgress,
    AccessDenied,
    DeviceUnavailable,
    Failed,
}
public sealed record UnpairBluetoothDeviceRequest(
    [property: JsonRequired] string DeviceId);
public sealed record BluetoothUnpairingResultSummary(
    [property: JsonRequired] BluetoothUnpairingResultStatus Outcome);
public sealed record OpenBluetoothDeviceSettingsRequest(
    [property: JsonRequired] string DeviceId);

public enum RecentActivityKind
{
    Unknown,
    Application,
    Game,
}

/// <summary>
/// Bounded, privacy-filtered foreground activity. ActivityId is an opaque,
/// process-lifetime token and never contains an HWND, PID, path, or package identity.
/// </summary>
public sealed record RecentActivitySummary(
    string ActivityId,
    string DisplayName,
    RecentActivityKind Kind,
    bool IsRunning,
    bool IsMostRecent);

public sealed record RecentActivitiesChangedEvent(
    IReadOnlyList<RecentActivitySummary> Activities);

public enum AppLibraryKind
{
    Unknown,
    Application,
    Game,
}

/// <summary>
/// One launchable application projected for an authenticated widget. AppId is
/// a short-lived launch token. SavedId is a durable, authority-scoped opaque
/// token suitable for the widget's private state. Neither identifier contains
/// a path, command line, AUMID, package identity, provider identity, or
/// launcher-specific identifier.
/// </summary>
public sealed record AppLibraryItemSummary(
    string AppId,
    string DisplayName,
    AppLibraryKind Kind)
{
    [JsonRequired]
    public string SavedId { get; init; } = string.Empty;

    /// <summary>
    /// Opaque, generation-bound host artwork registration. It is not a path,
    /// URL, provider identity, launch token, or persisted image payload.
    /// </summary>
    public string? ArtworkHandle { get; init; }

    /// <summary>Sanitized source label such as Windows or Steam.</summary>
    [JsonRequired]
    public string SourceAttribution { get; init; } = string.Empty;
}

/// <summary>
/// Trusted backend-only application identity. ProviderAppId is a short-lived
/// launch token. StableProviderIdentity is private host material used only to
/// derive an authority-scoped SavedId; this record must never cross widget IPC.
/// </summary>
public sealed record AppLibraryBackendItemSummary(
    [property: JsonIgnore] string ProviderAppId,
    [property: JsonIgnore] string StableProviderIdentity,
    string DisplayName,
    AppLibraryKind Kind,
    [property: JsonIgnore] string ArtworkRevision = "",
    string SourceAttribution = "Windows");

public enum AppLibrarySortOrder
{
    DisplayName,
    DisplayNameDescending,
    SourceThenDisplayName,
}
public enum AppLibraryCursorDirection { Before, After }

public sealed record AppLibraryBackendQuery(
    bool InstalledOnly = true,
    AppLibraryKind? Kind = null,
    string? SourceAttribution = null,
    AppLibrarySortOrder Sort = AppLibrarySortOrder.DisplayName)
{
    public string? SearchText { get; init; }
    [JsonIgnore]
    public IReadOnlyList<string>? StableIdentityFilter { get; init; }
}

public sealed record AppLibraryBackendCursorRequest(
    AppLibraryBackendQuery Query,
    string? Cursor,
    AppLibraryCursorDirection? Direction,
    int Limit,
    bool Refresh = false);

public sealed record AppLibraryBackendCursorPage(
    IReadOnlyList<AppLibraryBackendItemSummary> Items,
    string? Before,
    string? After,
    string Revision)
{
    public IReadOnlyList<AppLibrarySourceSummary> Sources { get; init; } = [];
}

public enum AppLibrarySourceHealth
{
    Healthy,
    Degraded,
    Unavailable,
    Refreshing,
}

/// <summary>
/// Bounded value-only health for one normalized local app-library source.
/// SourceId is observation-only and cannot address or control an adapter.
/// </summary>
public sealed record AppLibrarySourceSummary(
    string SourceId,
    string DisplayName,
    AppLibrarySourceHealth Health,
    long Revision,
    string StatusCode);

/// <summary>
/// Sanitized host-only evidence from one exact app-library launch adapter.
/// No process, window, command, or filesystem identity is carried.
/// </summary>
public enum AppLibraryLaunchObservationState
{
    RequestAccepted,
    LauncherStarted,
    Running,
    Ended,
}

public sealed record AppLibraryLaunchObservationSummary(
    AppLibraryLaunchObservationState State,
    bool SupportsRunning,
    bool SupportsEnded);

/// <summary>Trusted backend icon result; the broker validates every byte.</summary>
public sealed record AppLibraryIconSummary(string? PngBase64);

public static class AppLibraryImageLimits
{
    public const int MaximumPngBytes = 12 * 1024;
    public const int MaximumPixelDimension = 64;
}

public sealed record AppLibraryQuery(
    bool InstalledOnly = true,
    AppLibraryKind? Kind = null,
    string? SourceAttribution = null,
    AppLibrarySortOrder Sort = AppLibrarySortOrder.DisplayName)
{
    public string? SearchText { get; init; }
    public IReadOnlyList<string> FavoriteSavedIds { get; init; } = [];
}

public sealed record AppLibraryCursorRequest(
    [property: JsonRequired] AppLibraryQuery Query,
    string? Cursor,
    AppLibraryCursorDirection? Direction,
    [property: JsonRequired] int Limit,
    bool Refresh = false);

public sealed record AppLibraryCursorPageSummary(
    [property: JsonRequired] IReadOnlyList<AppLibraryItemSummary> Items,
    string? Before,
    string? After,
    [property: JsonRequired] string Revision)
{
    [JsonRequired]
    public IReadOnlyList<AppLibrarySourceSummary> Sources { get; init; } = [];
}

public sealed record ResolveSavedAppLibraryItemsRequest(
    [property: JsonRequired] IReadOnlyList<string> SavedIds);

public sealed record ResolveSavedAppLibraryItemsSummary(
    [property: JsonRequired] IReadOnlyList<AppLibraryItemSummary> Items);

public sealed record LaunchAppLibraryItemRequest(
    [property: JsonRequired] string AppId)
{
    public bool CloseOverlayOnSuccess { get; init; }
}

public enum MediaPlaybackStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

public enum MediaSessionCommand
{
    Play,
    Pause,
    TogglePlayPause,
    Previous,
    Next,
}

/// <summary>
/// Sanitized Windows media-session state. SessionId is host-generated and no
/// AUMID, package ID, process ID, path, handle, or native object crosses IPC.
/// Position may be projected from CapturedAtUnixMilliseconds while Playing.
/// </summary>
public sealed record MediaSessionSummary(
    string SessionId,
    string AppName,
    string Title,
    string Artist,
    MediaPlaybackStatus PlaybackStatus,
    long PositionMilliseconds,
    long DurationMilliseconds,
    long CapturedAtUnixMilliseconds,
    double PlaybackRate,
    bool IsCurrent,
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanPrevious,
    bool CanNext)
{
    /// <summary>
    /// Optional canonical bounded PNG bytes. The trusted provider normalizes
    /// GSMTC artwork; widgets never receive a path, package identity, or stream.
    /// </summary>
    public string? ArtworkPngBase64 { get; init; }
}

public static class MediaSessionImageLimits
{
    public const int MaximumPngBytes = AppLibraryImageLimits.MaximumPngBytes;
    public const int MaximumPixelDimension = AppLibraryImageLimits.MaximumPixelDimension;
    public const int MaximumSnapshotPngBytes = 96 * 1024;
}

public sealed record ControlMediaSessionRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] MediaSessionCommand Command);

public sealed record MediaSessionsChangedEvent(
    IReadOnlyList<MediaSessionSummary> Sessions);

/// <summary>
/// Public Spotify application configuration. A client ID is public OAuth
/// metadata; client secrets and tokens never cross the broker boundary.
/// </summary>
public sealed record SpotifyConfigurationSummary(
    [property: JsonRequired] bool IsConfigured,
    [property: JsonRequired] string RedirectUri);

public sealed record ConfigureSpotifyClientRequest(
    [property: JsonRequired] string ClientId);

public enum SpotifyAuthorizationState
{
    Unconfigured,
    Disconnected,
    Authorizing,
    Connected,
    ReauthorizationRequired,
}

/// <summary>Closed user scopes supported by the v1 Spotify broker slice.</summary>
public enum SpotifyAuthorizationScope
{
    PlaybackStateRead,
    PlaybackStateControl,
    LocalPlayback,
    PlaylistsRead,
    LibraryRead,
    LibraryModify,
    RecentlyPlayedRead,
    UserTopRead,
}

public sealed record ConnectSpotifyRequest(
    [property: JsonRequired] IReadOnlyList<SpotifyAuthorizationScope> RequestedScopes);

public sealed record SpotifyAuthorizationSummary(
    [property: JsonRequired] SpotifyAuthorizationState State,
    [property: JsonRequired] IReadOnlyList<SpotifyAuthorizationScope> RequestedScopes,
    [property: JsonRequired] IReadOnlyList<SpotifyAuthorizationScope> GrantedScopes,
    string? DisplayMessage);

public enum SpotifyPlaybackItemType
{
    Track,
    Episode,
}

public enum SpotifyRepeatState
{
    Off,
    Context,
    Track,
}

/// <summary>
/// Spotify's per-device disallow map projected onto the operations exposed by
/// this broker version. True means the widget must not offer that operation.
/// </summary>
public sealed record SpotifyPlaybackDisallowedActions(
    [property: JsonRequired] bool Pausing,
    [property: JsonRequired] bool Resuming,
    [property: JsonRequired] bool Seeking,
    [property: JsonRequired] bool SkippingNext,
    [property: JsonRequired] bool SkippingPrevious,
    [property: JsonRequired] bool TogglingRepeatContext,
    [property: JsonRequired] bool TogglingRepeatTrack,
    [property: JsonRequired] bool TogglingShuffle);

/// <summary>Sanitized track or episode metadata; no token or raw API document is exposed.</summary>
public sealed record SpotifyPlaybackItemSummary(
    [property: JsonRequired] SpotifyPlaybackItemType ItemType,
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Subtitle,
    string? ContextName,
    string? ArtworkUrl,
    string? Uri);

public sealed record SpotifyPlaybackSummary(
    [property: JsonRequired] bool IsAvailable,
    [property: JsonRequired] bool IsPlaying,
    [property: JsonRequired] long ProgressMilliseconds,
    [property: JsonRequired] long DurationMilliseconds,
    [property: JsonRequired] long CapturedAtUnixMilliseconds,
    [property: JsonRequired] SpotifyRepeatState RepeatState,
    [property: JsonRequired] bool ShuffleState,
    SpotifyPlaybackItemSummary? Item,
    [property: JsonRequired] SpotifyPlaybackDisallowedActions DisallowedActions,
    [property: JsonRequired] string Attribution);

public enum SpotifyPlaybackOperation
{
    Play,
    Pause,
    Next,
    Previous,
    Seek,
    SetRepeat,
    SetShuffle,
}

public sealed record SpotifyPlaybackCommand(
    [property: JsonRequired] SpotifyPlaybackOperation Operation,
    long? PositionMilliseconds,
    SpotifyRepeatState? RepeatState,
    bool? Enabled);

public sealed record SpotifyPlaybackChangedEvent(
    [property: JsonRequired] SpotifyPlaybackSummary Playback);

public enum SpotifyLocalPlaybackState
{
    Disabled,
    Starting,
    Ready,
    Active,
    NotReady,
    ReauthorizationRequired,
    PremiumRequired,
    Unavailable,
    Error,
}

public sealed record SpotifyLocalPlaybackSummary(
    [property: JsonRequired] SpotifyLocalPlaybackState State,
    [property: JsonRequired] string DeviceName,
    int? VolumePercent,
    string? DisplayMessage);

public enum SpotifyLocalPlaybackOperation
{
    StartAndTransfer,
    Stop,
    SetVolume,
}

public sealed record SpotifyLocalPlaybackCommand(
    [property: JsonRequired] SpotifyLocalPlaybackOperation Operation,
    int? VolumePercent,
    bool? ContinuePlaying);

public sealed record SpotifyDeviceSummary(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Type,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] bool IsRestricted,
    [property: JsonRequired] bool SupportsVolume,
    int? VolumePercent,
    [property: JsonRequired] bool IsLocalHost);

public sealed record SpotifyDevicesSummary(
    [property: JsonRequired] IReadOnlyList<SpotifyDeviceSummary> Devices);

public sealed record TransferSpotifyPlaybackRequest(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] bool ContinuePlaying);

public sealed record SpotifyMediaItemSummary(
    [property: JsonRequired] SpotifyPlaybackItemType ItemType,
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Subtitle,
    [property: JsonRequired] long DurationMilliseconds,
    string? ArtworkUrl,
    [property: JsonRequired] string Uri,
    [property: JsonRequired] string SpotifyUrl,
    [property: JsonRequired] bool IsPlayable);

public sealed record SpotifyQueueSummary(
    SpotifyMediaItemSummary? CurrentlyPlaying,
    [property: JsonRequired] IReadOnlyList<SpotifyMediaItemSummary> Items,
    [property: JsonRequired] bool IsTruncated);

public sealed record AddSpotifyQueueItemRequest(
    [property: JsonRequired] string Uri,
    string? DeviceId);

public sealed record SpotifyPlaylistSummary(
    [property: JsonRequired] string PlaylistId,
    [property: JsonRequired] string Name,
    string? Description,
    string? ArtworkUrl,
    [property: JsonRequired] string SpotifyUrl,
    [property: JsonRequired] string Uri,
    [property: JsonRequired] string OwnerName,
    [property: JsonRequired] bool IsCollaborative,
    bool? IsPublic,
    [property: JsonRequired] int ItemCount);

public sealed record SpotifyPlaylistPageRequest(
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit);

public sealed record SpotifyPlaylistPageSummary(
    [property: JsonRequired] IReadOnlyList<SpotifyPlaylistSummary> Items,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit,
    [property: JsonRequired] int Total);

public sealed record SpotifyPlaylistItemsRequest(
    [property: JsonRequired] string PlaylistId,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit);

public sealed record SpotifyPlaylistItemsSummary(
    [property: JsonRequired] SpotifyPlaylistSummary Playlist,
    [property: JsonRequired] IReadOnlyList<SpotifyMediaItemSummary> Items,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit,
    [property: JsonRequired] int Total);

public sealed record StartSpotifyPlaybackRequest(
    string? ContextUri,
    IReadOnlyList<string>? ItemUris,
    string? DeviceId,
    int? Offset,
    string? OffsetUri = null);

public sealed record LoopbackHttpHeader(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Value);

public sealed record LoopbackJsonRequest(
    [property: JsonRequired] string Path,
    [property: JsonRequired] IReadOnlyList<LoopbackHttpHeader> Headers,
    string? BearerSecretSlot,
    string? JsonBody,
    [property: JsonRequired] int TimeoutMilliseconds,
    bool InvalidateBearerSecretOnUnauthorized = false);

public sealed record LoopbackJsonResponse(
    [property: JsonRequired] int StatusCode,
    [property: JsonRequired] string JsonBody,
    [property: JsonRequired] IReadOnlyList<LoopbackHttpHeader> Headers);

public sealed record PrivateSecretSlotRequest([property: JsonRequired] string Slot);
public sealed record SavePrivateSecretRequest(
    [property: JsonRequired] string Slot,
    [property: JsonRequired] string Secret);
public sealed record PrivateSecretExistsSummary([property: JsonRequired] bool Exists);
public sealed record PrivateSecretMetadataSummary(
    [property: JsonRequired] bool Exists,
    long? LastWrittenUnixMilliseconds);

/// <summary>
/// Package-scoped readable state. JSON is transported as base64 so arbitrary
/// JSON strings remain data inside the bounded broker envelope. Providers must
/// decode and revalidate the canonical UTF-8 document before persistence.
/// </summary>
public sealed record PrivateStateSnapshotSummary(
    [property: JsonRequired] bool Exists,
    string? CanonicalJsonBase64,
    [property: JsonRequired] long Revision);
public sealed record WritePrivateStateRequest(
    [property: JsonRequired] string CanonicalJsonBase64,
    long? ExpectedRevision);
public sealed record ClearPrivateStateRequest(long? ExpectedRevision);
public sealed record PrivateStateMutationSummary([property: JsonRequired] long Revision);

public static class CommunityPlatformLimits
{
    public const int MaximumLoopbackPathCharacters = 2_048;
    public const int MaximumLoopbackHeaderCount = 16;
    public const int MaximumLoopbackHeaderNameCharacters = 64;
    public const int MaximumLoopbackHeaderValueCharacters = 1_024;
    public const int MaximumLoopbackHeaderCharacters = 8_192;
    public const int MaximumLoopbackRequestBodyUtf8Bytes = 16 * 1024;
    public const int MaximumLoopbackResponseBodyUtf8Bytes = 96 * 1024;
    public const int DefaultLoopbackTimeoutMilliseconds = 10_000;
    public const int MaximumLoopbackTimeoutMilliseconds = 40_000;
    public const int MaximumPrivateSecretSlotCharacters = 64;
    public const int MaximumPrivateSecretUtf8Bytes = 2_048;
    public const int MaximumPrivateStateUtf8Bytes = 64 * 1024;
    public const int MaximumPrivateStateBase64Characters =
        ((MaximumPrivateStateUtf8Bytes + 2) / 3) * 4;
}

public sealed record BrokerPlatformEvent(string CapabilityId, string EventType, object Payload);

public interface IPlatformBrokerEventSource
{
    event EventHandler<BrokerPlatformEvent>? EventPublished;
}

public interface IAudioPlatformBrokerBackend : IPlatformBrokerEventSource
{

    Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(CancellationToken cancellationToken);
    Task SetAudioSessionVolumeAsync(string sessionId, double volume, CancellationToken cancellationToken);
    Task SetAudioSessionMutedAsync(string sessionId, bool isMuted, CancellationToken cancellationToken);
    Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken);
    Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken);
    Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken);
    Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(CancellationToken cancellationToken);
    Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken);
    Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken);
    Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken);
}

public interface INetworkPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken);
    Task<NetworkConnectionDetailsSummary> GetNetworkConnectionDetailsAsync(
        CancellationToken cancellationToken) => Task.FromResult(new NetworkConnectionDetailsSummary(
            0, NetworkConnectionDetailsState.Unavailable,
            NetworkConnectionDetailsConnectivity.None, NetworkTransportKind.None, [], [], []));
    Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken);
    Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken);
    Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken);
    Task RequestWifiScanAsync(CancellationToken cancellationToken);
    Task ConnectAvailableWifiNetworkAsync(string networkId, CancellationToken cancellationToken);
    Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken);
    Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken);
}

/// <summary>
/// Least-authority trusted-host path for a credential collected outside every
/// widget worker. Implementations must not retain <paramref name="secret"/>.
/// </summary>
public interface IProtectedWifiHostBackend
{
    Task<ProtectedWifiConnectionResult> ConnectProtectedWifiAsync(
        string networkId,
        char[] secret,
        CancellationToken cancellationToken);
}

public interface IActivityPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken);
}

public interface IAppLibraryPlatformBrokerBackend
{
    Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
        AppLibraryBackendCursorRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<AppLibraryBackendCursorPage>(
            new BrokerException("platform_unavailable", "App library is unavailable."));

    Task LaunchAppLibraryItemAsync(
        string appId,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "App launch is unavailable."));

    async Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        await LaunchAppLibraryItemAsync(appId, cancellationToken).ConfigureAwait(false);
        return new(AppLibraryLaunchObservationState.RequestAccepted, false, false);
    }

    /// <summary>
    /// Rasterizes an icon only for an already resolved provider token. List
    /// discovery intentionally remains text-only so enumerating hundreds of
    /// applications cannot inflate broker or widget snapshots.
    /// </summary>
    Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
        string appId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AppLibraryIconSummary(null));
}

public interface IBluetoothPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken);
    Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken);
    Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken) =>
        Task.FromException<BluetoothPairingResultSummary>(
            new BrokerException("platform_unavailable", "Bluetooth pairing is unavailable."));
    Task<BluetoothUnpairingResultSummary> UnpairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken) =>
        Task.FromException<BluetoothUnpairingResultSummary>(
            new BrokerException("platform_unavailable", "Bluetooth removal is unavailable."));
    Task OpenBluetoothDeviceSettingsAsync(
        string deviceId, CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException(
                "platform_unavailable", "Bluetooth device management is unavailable."));
}

public interface IMediaPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<IReadOnlyList<MediaSessionSummary>> GetMediaSessionsAsync(
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<MediaSessionSummary>>(
            new BrokerException("platform_unavailable", "Windows media sessions are unavailable."));
    Task ControlMediaSessionAsync(
        string sessionId,
        MediaSessionCommand command,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Windows media session control is unavailable."));
}

/// <summary>
/// Trusted Spotify provider. Every call is identity-scoped so configuration,
/// authorization state, and tokens cannot be shared across widget packages.
/// </summary>
public interface ISpotifyPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<SpotifyConfigurationSummary> GetSpotifyConfigurationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyConfigurationSummary>(
            new BrokerException("platform_unavailable", "Spotify configuration is unavailable."));
    Task<SpotifyConfigurationSummary> ConfigureSpotifyClientAsync(
        BrokerWidgetIdentity identity, ConfigureSpotifyClientRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyConfigurationSummary>(
            new BrokerException("platform_unavailable", "Spotify configuration is unavailable."));
    Task<SpotifyAuthorizationSummary> GetSpotifyAuthorizationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyAuthorizationSummary>(
            new BrokerException("platform_unavailable", "Spotify authorization is unavailable."));
    Task<SpotifyAuthorizationSummary> ConnectSpotifyAsync(
        BrokerWidgetIdentity identity, ConnectSpotifyRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyAuthorizationSummary>(
            new BrokerException("platform_unavailable", "Spotify authorization is unavailable."));
    Task<SpotifyAuthorizationSummary> DisconnectSpotifyAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyAuthorizationSummary>(
            new BrokerException("platform_unavailable", "Spotify authorization is unavailable."));
    Task<SpotifyPlaybackSummary> GetSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyPlaybackSummary>(
            new BrokerException("platform_unavailable", "Spotify playback is unavailable."));
    Task ControlSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyPlaybackCommand command,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Spotify playback control is unavailable."));
    Task<SpotifyDevicesSummary> GetSpotifyDevicesAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyDevicesSummary>(
            new BrokerException("platform_unavailable", "Spotify devices are unavailable."));
    Task TransferSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, TransferSpotifyPlaybackRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Spotify device transfer is unavailable."));
    Task<SpotifyQueueSummary> GetSpotifyQueueAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyQueueSummary>(
            new BrokerException("platform_unavailable", "Spotify queue is unavailable."));
    Task AddSpotifyQueueItemAsync(
        BrokerWidgetIdentity identity, AddSpotifyQueueItemRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Spotify queue control is unavailable."));
    Task StartSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Spotify playback selection is unavailable."));
    Task<SpotifyLocalPlaybackSummary> GetSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<SpotifyLocalPlaybackSummary>(
            new BrokerException("platform_unavailable", "Spotify local playback is unavailable."));
    Task<SpotifyLocalPlaybackSummary> ControlSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyLocalPlaybackSummary>(
            new BrokerException("platform_unavailable", "Spotify local playback is unavailable."));
    Task<SpotifyPlaylistPageSummary> GetSpotifyPlaylistsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistPageRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyPlaylistPageSummary>(
            new BrokerException("platform_unavailable", "Spotify playlists are unavailable."));
    Task<SpotifyPlaylistItemsSummary> GetSpotifyPlaylistItemsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistItemsRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyPlaylistItemsSummary>(
            new BrokerException("platform_unavailable", "Spotify playlist items are unavailable."));
}

public interface IPrivateSecretPlatformBrokerBackend
{
    Task<PrivateSecretMetadataSummary> GetPrivateSecretMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        Task.FromException<PrivateSecretMetadataSummary>(
            new BrokerException("platform_unavailable", "Private secret storage is unavailable."));
    Task SavePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Private secret storage is unavailable."));
    Task DeletePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "Private secret storage is unavailable."));
}

public interface IPrivateStatePlatformBrokerBackend
{
    Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        Task.FromException<PrivateStateSnapshotSummary>(
            new BrokerException("platform_unavailable", "Private state storage is unavailable."));
    Task<PrivateStateMutationSummary> WritePrivateStateAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<PrivateStateMutationSummary>(
            new BrokerException("platform_unavailable", "Private state storage is unavailable."));
    Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<PrivateStateMutationSummary>(
            new BrokerException("platform_unavailable", "Private state storage is unavailable."));
}

public interface ILoopbackHttpPlatformBrokerBackend
{
    Task<LoopbackJsonResponse> SendLoopbackJsonAsync(
        BrokerWidgetIdentity identity,
        int port,
        bool isPost,
        LoopbackJsonRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<LoopbackJsonResponse>(
            new BrokerException("platform_unavailable", "Loopback HTTP is unavailable."));
}

/// <summary>
/// Complete host backend. Providers can implement the narrower audio or network
/// contracts and be joined with <see cref="CompositePlatformBrokerBackend"/>.
/// </summary>
public interface IPlatformBrokerBackend : IAudioPlatformBrokerBackend,
    INetworkPlatformBrokerBackend, IActivityPlatformBrokerBackend,
    IAppLibraryPlatformBrokerBackend, IBluetoothPlatformBrokerBackend,
    IMediaPlatformBrokerBackend, ISpotifyPlatformBrokerBackend,
    IPrivateSecretPlatformBrokerBackend,
    IPrivateStatePlatformBrokerBackend, ILoopbackHttpPlatformBrokerBackend
{
}

public sealed class BrokerException : Exception
{
    public BrokerException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

internal static partial class ContractValidation
{
    internal const int MaximumDisplayNameLength = 160;
    internal const int MaximumOpaqueIdLength = 128;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex PackageId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    internal static partial Regex InstanceId();

    internal static void OpaqueId(string value, string code = "invalid_payload")
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumOpaqueIdLength ||
            !InstanceId().IsMatch(value))
            throw new BrokerException(code, "An opaque platform identifier is invalid.");
    }

    internal static void DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumDisplayNameLength ||
            value.Any(char.IsControl))
            throw new BrokerException("invalid_backend_data", "Platform display data is invalid.");
    }

    internal static void Percent(int? value)
    {
        if (value is < 0 or > 100)
            throw new BrokerException("invalid_backend_data", "Platform percentage is invalid.");
    }
}
