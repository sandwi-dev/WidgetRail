using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using WidgetRail.Internal;

namespace WidgetRail.PlatformBroker;

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

public enum AppLibraryAvailabilityState
{
    Installed,
    Unavailable,
    StaleSource,
}

public sealed record AppLibraryAvailabilitySummary(
    AppLibraryAvailabilityState State,
    bool IsLaunchable,
    string StatusCode);

public enum AppLibraryArtworkRole { Tile, Cover, Hero, Logo }
public enum AppLibraryArtworkFallback { Application, Game }

public sealed record AppLibraryArtworkSummary(
    AppLibraryArtworkRole Role,
    string Handle,
    string Revision,
    AppLibraryArtworkFallback Fallback);

public sealed record AppLibraryArtworkSet(
    [property: JsonRequired] IReadOnlyList<AppLibraryArtworkSummary> Items);

public sealed record AppLibraryMetadataAttribution(
    string Provider,
    string RecordRevision,
    string Attribution,
    long RetrievedAtUnixMilliseconds);

public sealed record AppLibraryMetadataSummary(
    string Revision,
    [property: JsonRequired] AppLibraryMetadataAttribution Attribution)
{
    public string? SortTitle { get; init; }
    public string? Version { get; init; }
    public long? LastPlayedAtUnixMilliseconds { get; init; }
    public long? PlaytimeMinutes { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string? Description { get; init; }
}

public enum AppLibraryAction
{
    Launch,
    Install,
    Pause,
    Resume,
    Cancel,
    Update,
    Repair,
    Move,
    Import,
    Uninstall,
    CloudSync,
    OpenSourceClient,
    ManageAddOns,
}

public sealed record AppLibraryCapabilitySet(
    [property: JsonRequired] IReadOnlyList<AppLibraryAction> Actions);

public enum AppLibraryOperationKind
{
    Launch,
    Install,
    Update,
    Repair,
    Move,
    Import,
    Uninstall,
    CloudSync,
}

public enum AppLibraryOperationState
{
    Pending,
    RequestAccepted,
    LauncherStarted,
    Running,
    Paused,
    Completed,
    Failed,
}

public sealed record AppLibraryOperationSummary(
    string OperationId,
    AppLibraryOperationKind Kind,
    AppLibraryOperationState State,
    string StatusCode);

public sealed record AppLibrarySourceReference(string SourceId, string DisplayName);

public sealed record AppLibraryItemPresentation(
    string DisplayName,
    AppLibraryKind Kind,
    [property: JsonRequired] AppLibrarySourceReference Source,
    [property: JsonRequired] AppLibraryAvailabilitySummary Availability,
    [property: JsonRequired] AppLibraryArtworkSet Artwork,
    AppLibraryMetadataSummary? Metadata,
    [property: JsonRequired] AppLibraryCapabilitySet Capabilities,
    AppLibraryOperationSummary? ActiveOperation);

/// <summary>
/// One launchable application projected for an authenticated widget. AppId is
/// a short-lived launch token. SavedId is a durable, authority-scoped opaque
/// token suitable for the widget's private state. Neither identifier contains
/// a path, command line, AUMID, package identity, provider identity, or
/// launcher-specific identifier.
/// </summary>
public sealed record AppLibraryItemSummary(
    string AppId,
    string SavedId,
    [property: JsonRequired] AppLibraryItemPresentation Presentation)
{
    public const int CurrentPresentationVersion = 1;

    [JsonRequired]
    public int PresentationVersion { get; init; } = CurrentPresentationVersion;

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
    string SourceAttribution = "Windows")
{
    [JsonIgnore]
    public string SourceIdentity { get; init; } = string.Empty;

    /// <summary>
    /// Trusted backend-only launch admission. False rows remain useful installed
    /// evidence but receive no public launch registration or capability.
    /// </summary>
    [JsonIgnore]
    public bool IsLaunchable { get; init; } = true;

    [JsonIgnore]
    public AppLibraryAvailabilityState AvailabilityState { get; init; } =
        AppLibraryAvailabilityState.Installed;

    [JsonIgnore]
    public string? AvailabilityStatusCode { get; init; }

    [JsonIgnore]
    public IReadOnlyList<AppLibraryAction>? SupportedActions { get; init; }
}

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

public enum AppLibrarySourceAccountState
{
    NotApplicable,
    SignedOut,
    SigningIn,
    Ready,
    Expired,
    Denied,
    Unavailable,
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
    string StatusCode)
{
    public AppLibrarySourceAccountState AccountState { get; init; } =
        AppLibrarySourceAccountState.NotApplicable;
    public long? LastSuccessfulRefreshAtUnixMilliseconds { get; init; }
}

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

/// <summary>One privacy-safe current-window match to an installed registration.</summary>
public sealed record RunningAppCandidateSummary(
    [property: JsonRequired] string SavedId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] AppLibraryKind Kind,
    [property: JsonRequired] string SourceAttribution)
{
    public AppLibraryArtworkSet Artwork { get; init; } = new([]);
}

public sealed record RunningAppObservationSummary(
    [property: JsonRequired] IReadOnlyList<RunningAppCandidateSummary> Items,
    [property: JsonRequired] string Revision);

public sealed record ConfirmRunningAppRequest(
    [property: JsonRequired] string SavedId,
    [property: JsonRequired] string Revision);

public sealed record ConfirmRunningAppSummary(AppLibraryItemSummary? Item);

public sealed record RegisterRunningAppRequest(
    [property: JsonRequired] string SavedId,
    [property: JsonRequired] string Revision);

public sealed record RegisterRunningAppSummary(
    [property: JsonRequired] AppLibraryItemSummary Item,
    [property: JsonRequired] bool AlreadyRegistered);

public sealed record ForgetRunningAppRequest(
    [property: JsonRequired] string SavedId);

public sealed record RegisterRunningAppBackendRequest(
    [property: JsonRequired] string SavedId,
    [property: JsonRequired] string StableProviderIdentity,
    [property: JsonRequired] string InstanceEvidence,
    [property: JsonRequired] string ObservationRevision);

public sealed record RegisterRunningAppBackendSummary(
    [property: JsonRequired] AppLibraryBackendItemSummary Item,
    [property: JsonRequired] bool AlreadyRegistered);

public sealed record AppLibraryRegistrationStateSummary(
    [property: JsonRequired] bool Exists,
    [property: JsonRequired] long Revision);

public sealed record AppLibraryPackageRegistrationRetirementSummary(
    [property: JsonRequired] bool Committed,
    [property: JsonRequired] bool CleanupPending);

/// <summary>Host-only normalized observation; private identities never cross IPC.</summary>
public sealed record RunningAppBackendObservation(
    [property: JsonIgnore] string StableProviderIdentity,
    [property: JsonIgnore] string InstanceEvidence,
    string DisplayName,
    AppLibraryKind Kind,
    string SourceAttribution)
{
    [JsonIgnore] public AppLibraryBackendItemSummary? ArtworkItem { get; init; }
}

public sealed record RunningAppBackendObservationPage(
    IReadOnlyList<RunningAppBackendObservation> Items,
    string Revision);

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
    public const int MaximumLoopbackPathCharacters =
        CommunityPlatformContractLimits.MaximumLoopbackPathCharacters;
    public const int MaximumLoopbackHeaderCount =
        CommunityPlatformContractLimits.MaximumLoopbackHeaderCount;
    public const int MaximumLoopbackHeaderNameCharacters =
        CommunityPlatformContractLimits.MaximumLoopbackHeaderNameCharacters;
    public const int MaximumLoopbackHeaderValueCharacters =
        CommunityPlatformContractLimits.MaximumLoopbackHeaderValueCharacters;
    public const int MaximumLoopbackHeaderCharacters =
        CommunityPlatformContractLimits.MaximumLoopbackHeaderCharacters;
    public const int MaximumLoopbackRequestBodyUtf8Bytes =
        CommunityPlatformContractLimits.MaximumLoopbackRequestBodyUtf8Bytes;
    public const int MaximumLoopbackResponseBodyUtf8Bytes =
        CommunityPlatformContractLimits.MaximumLoopbackResponseBodyUtf8Bytes;
    public const int DefaultLoopbackTimeoutMilliseconds =
        CommunityPlatformContractLimits.DefaultLoopbackTimeoutMilliseconds;
    public const int MaximumLoopbackTimeoutMilliseconds =
        CommunityPlatformContractLimits.MaximumLoopbackTimeoutMilliseconds;
    public const int MaximumPrivateSecretSlotCharacters =
        CommunityPlatformContractLimits.MaximumPrivateSecretSlotCharacters;
    public const int MaximumPrivateSecretUtf8Bytes =
        CommunityPlatformContractLimits.MaximumPrivateSecretUtf8Bytes;
    public const int MaximumPrivateStateUtf8Bytes =
        CommunityPlatformContractLimits.MaximumPrivateStateUtf8Bytes;
    public const int MaximumPrivateStateBase64Characters =
        CommunityPlatformContractLimits.MaximumPrivateStateBase64Characters;
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
        BrokerWidgetIdentity identity,
        string appId,
        CancellationToken cancellationToken) =>
        Task.FromException(
            new BrokerException("platform_unavailable", "App launch is unavailable."));

    async Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        BrokerWidgetIdentity identity,
        string appId,
        CancellationToken cancellationToken)
    {
        await LaunchAppLibraryItemAsync(identity, appId, cancellationToken)
            .ConfigureAwait(false);
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

    Task<RunningAppBackendObservationPage> ObserveRunningAppsAsync(
        CancellationToken cancellationToken) =>
        Task.FromException<RunningAppBackendObservationPage>(
            new BrokerException("platform_unavailable", "Running-app observation is unavailable."));

    Task<RegisterRunningAppBackendSummary> RegisterRunningAppAsync(
        BrokerWidgetIdentity identity,
        RegisterRunningAppBackendRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<RegisterRunningAppBackendSummary>(
            new BrokerException("platform_unavailable", "Running-app registration is unavailable."));

    Task<IReadOnlyList<AppLibraryBackendItemSummary>> ResolveRegisteredRunningAppsAsync(
        BrokerWidgetIdentity identity,
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AppLibraryBackendItemSummary>>([]);

    Task ForgetRunningAppAsync(
        BrokerWidgetIdentity identity,
        string savedId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    Task<AppLibraryRegistrationStateSummary> GetRunningAppRegistrationStateAsync(
        BrokerWidgetIdentity identity,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AppLibraryRegistrationStateSummary(false, 0));

    Task ClearRunningAppRegistrationsAsync(
        BrokerWidgetIdentity identity,
        long expectedRevision,
        CancellationToken cancellationToken) => Task.CompletedTask;

    Task<AppLibraryPackageRegistrationRetirementSummary>
        RetireRunningAppPackageRegistrationsAsync(
            string packageId,
            CancellationToken cancellationToken) =>
        Task.FromException<AppLibraryPackageRegistrationRetirementSummary>(
            new BrokerException(
                "platform_unavailable",
                "Running-app registration cleanup is unavailable."));
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
    IMediaPlatformBrokerBackend,
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
