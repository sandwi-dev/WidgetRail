using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;

namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetCapabilityQuery;

public sealed record WidgetCapabilityAcknowledgement(
    [property: JsonRequired] bool Acknowledged);

public static class WidgetCommunityPlatformLimits
{
    public const int MinimumLoopbackPort = 1_024;
    public const int MaximumLoopbackPort = 65_535;
    public const int MaximumLoopbackPathCharacters = 2_048;
    public const int MaximumLoopbackHeaderCount = 16;
    public const int MaximumLoopbackHeaderNameCharacters = 64;
    public const int MaximumLoopbackHeaderValueCharacters = 1_024;
    public const int MaximumLoopbackHeaderCharacters = 8_192;
    public const int MaximumLoopbackRequestBodyUtf8Bytes = 16 * 1_024;
    public const int MaximumLoopbackResponseBodyUtf8Bytes = 96 * 1_024;
    public const int DefaultLoopbackTimeoutMilliseconds = 10_000;
    public const int MaximumLoopbackTimeoutMilliseconds = 40_000;
    public const int MaximumPrivateSecretSlotCharacters = 64;
    public const int MaximumPrivateSecretUtf8Bytes = 2_048;
    public const int MaximumPrivateStateUtf8Bytes = 64 * 1_024;
    public const int MaximumPrivateStateInputUtf8Bytes = 256 * 1_024;
    internal const int MaximumPrivateStateBase64Characters =
        ((MaximumPrivateStateUtf8Bytes + 2) / 3) * 4;
}

public sealed record WidgetLoopbackHttpHeader(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Value);

public sealed record WidgetLoopbackRequestOptions
{
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? BearerSecretSlot { get; init; }
    /// <summary>
    /// When an injected bearer credential receives HTTP 401, asks the trusted
    /// host to delete that exact package-scoped slot before returning the
    /// response. This is host-side credential hygiene, not general secret
    /// control, and is valid only with <see cref="BearerSecretSlot"/>.
    /// </summary>
    public bool InvalidateBearerSecretOnUnauthorized { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(
        WidgetCommunityPlatformLimits.DefaultLoopbackTimeoutMilliseconds);
}

public sealed record WidgetLoopbackJsonRequest(
    [property: JsonRequired] string Path,
    [property: JsonRequired] IReadOnlyList<WidgetLoopbackHttpHeader> Headers,
    string? BearerSecretSlot,
    string? JsonBody,
    [property: JsonRequired] int TimeoutMilliseconds,
    bool InvalidateBearerSecretOnUnauthorized = false);

public sealed record WidgetLoopbackJsonResponse(
    [property: JsonRequired] int StatusCode,
    [property: JsonRequired] string JsonBody,
    [property: JsonRequired] IReadOnlyList<WidgetLoopbackHttpHeader> Headers);

public sealed record WidgetPrivateSecretSlotRequest([property: JsonRequired] string Slot);
public sealed record SaveWidgetPrivateSecretRequest(
    [property: JsonRequired] string Slot,
    [property: JsonRequired] string Secret);
public sealed record WidgetPrivateSecretExists([property: JsonRequired] bool Exists);
public sealed record WidgetPrivateSecretMetadata(
    [property: JsonRequired] bool Exists,
    long? LastWrittenUnixMilliseconds);

public sealed record WidgetPrivateStateSnapshot(
    [property: JsonRequired] bool Exists,
    string? Json,
    [property: JsonRequired] long Revision);
public sealed record WidgetPrivateStateValue<T>(
    [property: JsonRequired] bool Exists,
    T? Value,
    [property: JsonRequired] long Revision);
public sealed record WidgetPrivateStateMutation([property: JsonRequired] long Revision);

internal sealed record WidgetPrivateStateTransportSnapshot(
    [property: JsonRequired] bool Exists,
    string? CanonicalJsonBase64,
    [property: JsonRequired] long Revision);
internal sealed record WriteWidgetPrivateStateTransportRequest(
    [property: JsonRequired] string CanonicalJsonBase64,
    long? ExpectedRevision);
internal sealed record ClearWidgetPrivateStateTransportRequest(long? ExpectedRevision);
internal sealed record WidgetPrivateStateTransportMutation(
    [property: JsonRequired] long Revision);

public sealed record WidgetAudioSession(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted,
    [property: JsonRequired] bool IsActive);

public sealed record SetWidgetAudioSessionVolumeRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] double Volume);

public sealed record SetWidgetAudioSessionMutedRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] bool IsMuted);

public sealed record WidgetAudioSessionsChanged(
    [property: JsonRequired] IReadOnlyList<WidgetAudioSession> Sessions,
    [property: JsonRequired] bool IsAvailable = true);

public sealed record WidgetAudioOutput(
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted);

public sealed record SetWidgetAudioOutputVolumeRequest(
    [property: JsonRequired] double Volume);

public sealed record SetWidgetAudioOutputMutedRequest(
    [property: JsonRequired] bool IsMuted);

public sealed record WidgetAudioOutputChanged(
    [property: JsonRequired] WidgetAudioOutput? Output,
    [property: JsonRequired] bool IsAvailable = true);

public enum WidgetAudioDeviceDirection
{
    Output,
    Input,
}

public sealed record WidgetAudioDevice(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] WidgetAudioDeviceDirection Direction,
    [property: JsonRequired] bool IsDefault);

public sealed record WidgetAudioDevicesChanged(
    [property: JsonRequired] IReadOnlyList<WidgetAudioDevice> Devices,
    [property: JsonRequired] bool IsAvailable = true);

public sealed record WidgetAudioInput(
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted);

public sealed record SetWidgetAudioInputVolumeRequest(
    [property: JsonRequired] double Volume);

public sealed record SetWidgetAudioInputMutedRequest(
    [property: JsonRequired] bool IsMuted);

public sealed record WidgetAudioInputChanged(
    [property: JsonRequired] WidgetAudioInput? Input,
    [property: JsonRequired] bool IsAvailable = true);

public enum WidgetNetworkConnectivity
{
    None,
    Local,
    Internet,
}

public enum WidgetNetworkTransportKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum WidgetNetworkWirelessAvailability
{
    Available,
    NoAdapter,
    RadioOff,
    ServiceUnavailable,
}

public enum WidgetNetworkDetailsAccess
{
    Available,
    PrivacyRestricted,
    Unavailable,
}

public enum WidgetNetworkConnectionAttemptState
{
    None,
    Connecting,
    Failed,
}

public sealed record WidgetNetworkStatus(
    [property: JsonRequired] WidgetNetworkConnectivity Connectivity,
    [property: JsonRequired] WidgetNetworkTransportKind Transport,
    [property: JsonRequired] WidgetNetworkWirelessAvailability WirelessAvailability,
    [property: JsonRequired] WidgetNetworkDetailsAccess DetailsAccess,
    [property: JsonRequired] WidgetNetworkConnectionAttemptState ConnectionAttemptState,
    [property: JsonRequired] string? AttemptProfileId,
    [property: JsonRequired] string? ActiveProfileId,
    [property: JsonRequired] string? ActiveProfileName,
    [property: JsonRequired] int? SignalPercent);

public sealed record WidgetSavedNetworkProfile(
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] bool IsConnected,
    [property: JsonRequired] int? SignalPercent);

public sealed record SwitchWidgetSavedNetworkProfileRequest(
    [property: JsonRequired] string ProfileId);

public enum WidgetWifiScanState
{
    NotScanned,
    Scanning,
    Ready,
    PreciseLocationDenied,
    Unavailable,
}

public enum WidgetWifiSecurityKind
{
    Open,
    Personal,
    Enterprise,
    Unknown,
}

public sealed record WidgetAvailableWifiNetwork(
    [property: JsonRequired] string NetworkId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] int SignalPercent,
    [property: JsonRequired] WidgetWifiSecurityKind Security,
    [property: JsonRequired] bool CredentialRequired,
    [property: JsonRequired] bool IsConnected,
    [property: JsonRequired] bool HasSavedProfile);

public sealed record WidgetAvailableWifiNetworks(
    [property: JsonRequired] WidgetWifiScanState ScanState,
    [property: JsonRequired] IReadOnlyList<WidgetAvailableWifiNetwork> Networks);

public sealed record ConnectWidgetAvailableWifiNetworkRequest(
    [property: JsonRequired] string NetworkId);

public sealed record WidgetAvailableWifiNetworksChanged(
    [property: JsonRequired] WidgetAvailableWifiNetworks Snapshot);

public enum WidgetWifiRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public sealed record WidgetWifiRadio(
    [property: JsonRequired] WidgetWifiRadioState State,
    [property: JsonRequired] bool CanControl);

public sealed record SetWidgetWifiRadioRequest([property: JsonRequired] bool Enabled);
public sealed record WidgetWifiRadioChanged([property: JsonRequired] WidgetWifiRadio Radio);

public enum WidgetBluetoothRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public enum WidgetBluetoothDiscoveryState
{
    Enumerating,
    Ready,
    Unavailable,
}

public sealed record WidgetBluetoothDevice(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] bool IsPaired,
    [property: JsonRequired] bool IsConnected,
    [property: JsonRequired] bool IsPresent);

public sealed record WidgetBluetoothSnapshot(
    [property: JsonRequired] WidgetBluetoothRadioState RadioState,
    [property: JsonRequired] bool CanControlRadio,
    [property: JsonRequired] WidgetBluetoothDiscoveryState DiscoveryState,
    [property: JsonRequired] IReadOnlyList<WidgetBluetoothDevice> Devices);

public sealed record SetWidgetBluetoothRadioRequest([property: JsonRequired] bool Enabled);
public sealed record WidgetBluetoothChanged(
    [property: JsonRequired] WidgetBluetoothSnapshot Snapshot);

public enum WidgetBluetoothPairingOutcome
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

public sealed record PairWidgetBluetoothDeviceRequest(
    [property: JsonRequired] string DeviceId);
public sealed record WidgetBluetoothPairingResult(
    [property: JsonRequired] WidgetBluetoothPairingOutcome Outcome);
public enum WidgetBluetoothUnpairingOutcome
{
    Unpaired,
    AlreadyUnpaired,
    OperationInProgress,
    AccessDenied,
    DeviceUnavailable,
    Failed,
}
public sealed record UnpairWidgetBluetoothDeviceRequest(
    [property: JsonRequired] string DeviceId);
public sealed record WidgetBluetoothUnpairingResult(
    [property: JsonRequired] WidgetBluetoothUnpairingOutcome Outcome);
public sealed record OpenWidgetBluetoothDeviceSettingsRequest(
    [property: JsonRequired] string DeviceId);

public sealed record WidgetNetworkStatusChanged(
    [property: JsonRequired] WidgetNetworkStatus Status);

public enum WidgetRecentActivityKind
{
    Unknown,
    Application,
    Game,
}

public sealed record WidgetRecentActivity(
    [property: JsonRequired] string ActivityId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] WidgetRecentActivityKind Kind,
    [property: JsonRequired] bool IsRunning,
    [property: JsonRequired] bool IsMostRecent);

public sealed record WidgetRecentActivitiesChanged(
    [property: JsonRequired] IReadOnlyList<WidgetRecentActivity> Activities);

public enum WidgetAppLibraryKind
{
    Unknown,
    Application,
    Game,
}

/// <summary>
/// Sanitized launchable app metadata. AppId is a short-lived launch token.
/// SavedId is a durable opaque token scoped to this widget authority and is the
/// value to retain in private state. Neither is a path, command line, AUMID,
/// package identity, provider identity, or launcher-specific identifier.
/// </summary>
public sealed record WidgetAppLibraryItem(
    [property: JsonRequired] string AppId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] WidgetAppLibraryKind Kind)
{
    [JsonRequired]
    public string SavedId { get; init; } = string.Empty;

    /// <summary>
    /// Optional opaque host artwork registration. It carries no path, URL,
    /// image bytes, provider identity, or launch authority.
    /// </summary>
    public string? ArtworkHandle { get; init; }

    [JsonRequired]
    public string SourceAttribution { get; init; } = string.Empty;
}

public enum WidgetAppLibrarySortOrder
{
    DisplayName,
    DisplayNameDescending,
    SourceThenDisplayName,
}

public enum WidgetAppLibrarySourceHealth
{
    Healthy,
    Degraded,
    Unavailable,
    Refreshing,
}

/// <summary>
/// Sanitized observation-only status for one normalized local library source.
/// SourceId cannot be used to query, refresh, or launch through that source.
/// </summary>
public sealed record WidgetAppLibrarySource(
    [property: JsonRequired] string SourceId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] WidgetAppLibrarySourceHealth Health,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] string StatusCode);

public sealed record WidgetAppLibraryQuery(
    bool InstalledOnly = true,
    WidgetAppLibraryKind? Kind = null,
    string? SourceAttribution = null,
    WidgetAppLibrarySortOrder Sort = WidgetAppLibrarySortOrder.DisplayName)
{
    public const int MaximumSearchTextLength = 96;
    public const int MaximumFavoriteSavedIds = 128;
    public string? SearchText { get; init; }
    public IReadOnlyList<string> FavoriteSavedIds { get; init; } = [];
}

public sealed record WidgetAppLibraryCursorRequest(
    [property: JsonRequired] WidgetAppLibraryQuery Query,
    string? Cursor,
    WidgetCursorDirection? Direction,
    [property: JsonRequired] int Limit,
    bool Refresh = false);

public sealed record WidgetAppLibraryPage(
    [property: JsonRequired] IReadOnlyList<WidgetAppLibraryItem> Items,
    string? Before,
    string? After,
    [property: JsonRequired] string Revision)
{
    [JsonRequired]
    public IReadOnlyList<WidgetAppLibrarySource> Sources { get; init; } = [];
}

public sealed record ResolveSavedWidgetAppLibraryItemsRequest(
    [property: JsonRequired] IReadOnlyList<string> SavedIds);

public sealed record ResolveSavedWidgetAppLibraryItemsResponse(
    [property: JsonRequired] IReadOnlyList<WidgetAppLibraryItem> Items);

public sealed record LaunchWidgetAppLibraryItemRequest(
    [property: JsonRequired] string AppId)
{
    public bool CloseOverlayOnSuccess { get; init; }
}

public enum WidgetAppLaunchOverlayBehavior
{
    KeepOpen,
    CloseOnConfirmedSuccess,
}

internal enum WidgetAppLaunchObservationState
{
    RequestAccepted,
    LauncherStarted,
    Running,
    Ended,
}

internal sealed record WidgetAppLaunchObservation(
    [property: JsonRequired] WidgetAppLaunchObservationState State,
    [property: JsonRequired] bool SupportsRunning,
    [property: JsonRequired] bool SupportsEnded);

public enum WidgetMediaPlaybackStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

public enum WidgetMediaSessionCommand
{
    Play,
    Pause,
    TogglePlayPause,
    Previous,
    Next,
}

public sealed record WidgetMediaSession(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] string AppName,
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Artist,
    [property: JsonRequired] WidgetMediaPlaybackStatus PlaybackStatus,
    [property: JsonRequired] long PositionMilliseconds,
    [property: JsonRequired] long DurationMilliseconds,
    [property: JsonRequired] long CapturedAtUnixMilliseconds,
    [property: JsonRequired] double PlaybackRate,
    [property: JsonRequired] bool IsCurrent,
    [property: JsonRequired] bool CanPlay,
    [property: JsonRequired] bool CanPause,
    [property: JsonRequired] bool CanTogglePlayPause,
    [property: JsonRequired] bool CanPrevious,
    [property: JsonRequired] bool CanNext)
{
    /// <summary>Optional host-sanitized inline PNG album artwork.</summary>
    public string? ArtworkPngBase64 { get; init; }
}

public sealed record ControlWidgetMediaSessionRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] WidgetMediaSessionCommand Command);

public sealed record WidgetMediaSessionsChanged(
    [property: JsonRequired] IReadOnlyList<WidgetMediaSession> Sessions);

/// <summary>Reusable typed definitions for the audio provider.</summary>
public static class WidgetAudioCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetAudioSession>>
        GetSessions { get; } = new("system.audio.sessions.read.v1", "audio.sessions.list");

    public static WidgetCapabilityOperation<SetWidgetAudioSessionVolumeRequest, WidgetCapabilityAcknowledgement>
        SetSessionVolume { get; } = new("system.audio.sessions.control.v1", "audio.session.set-volume");

    public static WidgetCapabilityOperation<SetWidgetAudioSessionMutedRequest, WidgetCapabilityAcknowledgement>
        SetSessionMuted { get; } = new("system.audio.sessions.control.v1", "audio.session.set-muted");

    public static WidgetCapabilityEvent<WidgetAudioSessionsChanged> SessionsChanged { get; } =
        new("system.audio.sessions.read.v1", "audio.sessions.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetAudioOutput>
        GetOutput { get; } = new("system.audio.output.read.v1", "audio.output.get");

    public static WidgetCapabilityOperation<SetWidgetAudioOutputVolumeRequest, WidgetCapabilityAcknowledgement>
        SetOutputVolume { get; } = new("system.audio.output.control.v1", "audio.output.set-volume");

    public static WidgetCapabilityOperation<SetWidgetAudioOutputMutedRequest, WidgetCapabilityAcknowledgement>
        SetOutputMuted { get; } = new("system.audio.output.control.v1", "audio.output.set-muted");

    public static WidgetCapabilityEvent<WidgetAudioOutputChanged> OutputChanged { get; } =
        new("system.audio.output.read.v1", "audio.output.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetAudioDevice>>
        GetDevices { get; } = new("system.audio.devices.read.v1", "audio.devices.list");

    public static WidgetCapabilityEvent<WidgetAudioDevicesChanged> DevicesChanged { get; } =
        new("system.audio.devices.read.v1", "audio.devices.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetAudioInput>
        GetInput { get; } = new("system.audio.input.read.v1", "audio.input.get");

    public static WidgetCapabilityOperation<SetWidgetAudioInputVolumeRequest, WidgetCapabilityAcknowledgement>
        SetInputVolume { get; } = new("system.audio.input.control.v1", "audio.input.set-volume");

    public static WidgetCapabilityOperation<SetWidgetAudioInputMutedRequest, WidgetCapabilityAcknowledgement>
        SetInputMuted { get; } = new("system.audio.input.control.v1", "audio.input.set-muted");

    public static WidgetCapabilityEvent<WidgetAudioInputChanged> InputChanged { get; } =
        new("system.audio.input.read.v1", "audio.input.changed");
}

/// <summary>Reusable typed definitions for the saved-network provider.</summary>
public static class WidgetNetworkCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetNetworkStatus>
        GetStatus { get; } = new("system.network.read.v1", "network.status.get");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetSavedNetworkProfile>>
        GetSavedProfiles { get; } = new("system.network.read.v1", "network.saved-profiles.list");

    public static WidgetCapabilityOperation<SwitchWidgetSavedNetworkProfileRequest, WidgetCapabilityAcknowledgement>
        SwitchSavedProfile { get; } =
            new("system.network.saved-profile.switch.v1", "network.saved-profile.switch");

    public static WidgetCapabilityEvent<WidgetNetworkStatusChanged> StatusChanged { get; } =
        new("system.network.read.v1", "network.status.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetAvailableWifiNetworks>
        GetAvailableWifi { get; } =
            new("system.network.wifi.read.v1", "network.wifi.available.get");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement>
        RequestWifiScan { get; } =
            new("system.network.wifi.read.v1", "network.wifi.scan");

    public static WidgetCapabilityOperation<ConnectWidgetAvailableWifiNetworkRequest,
        WidgetCapabilityAcknowledgement> ConnectAvailableWifi { get; } =
            new("system.network.wifi.connect.v1", "network.wifi.connect");

    public static WidgetCapabilityEvent<WidgetAvailableWifiNetworksChanged>
        AvailableWifiChanged { get; } =
            new("system.network.wifi.read.v1", "network.wifi.available.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetWifiRadio>
        GetWifiRadio { get; } =
            new("system.network.wifi.radio.read.v1", "network.wifi.radio.get");

    public static WidgetCapabilityOperation<SetWidgetWifiRadioRequest,
        WidgetCapabilityAcknowledgement> SetWifiRadio { get; } =
            new("system.network.wifi.radio.control.v1", "network.wifi.radio.set");

    public static WidgetCapabilityEvent<WidgetWifiRadioChanged> WifiRadioChanged { get; } =
        new("system.network.wifi.radio.read.v1", "network.wifi.radio.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetBluetoothSnapshot>
        GetBluetooth { get; } =
            new("system.network.bluetooth.read.v1", "network.bluetooth.get");

    public static WidgetCapabilityOperation<SetWidgetBluetoothRadioRequest,
        WidgetCapabilityAcknowledgement> SetBluetoothRadio { get; } =
            new("system.network.bluetooth.radio.control.v1", "network.bluetooth.radio.set");

    public static WidgetCapabilityOperation<PairWidgetBluetoothDeviceRequest,
        WidgetBluetoothPairingResult> PairBluetoothDevice { get; } =
            new("system.network.bluetooth.pair.v1", "network.bluetooth.device.pair");

    public static WidgetCapabilityOperation<UnpairWidgetBluetoothDeviceRequest,
        WidgetBluetoothUnpairingResult> UnpairBluetoothDevice { get; } =
            new("system.network.bluetooth.unpair.v1", "network.bluetooth.device.unpair");

    public static WidgetCapabilityOperation<OpenWidgetBluetoothDeviceSettingsRequest,
        WidgetCapabilityAcknowledgement> OpenBluetoothDeviceSettings { get; } =
            new("system.network.bluetooth.manage.v1",
                "network.bluetooth.device.settings.open");

    public static WidgetCapabilityEvent<WidgetBluetoothChanged> BluetoothChanged { get; } =
        new("system.network.bluetooth.read.v1", "network.bluetooth.changed");
}

/// <summary>Typed recent foreground-activity contracts with no OS identifiers.</summary>
public static class WidgetRecentActivityCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetRecentActivity>>
        GetRecent { get; } = new("system.activity.recent.read.v1", "activity.recent.list");

    public static WidgetCapabilityEvent<WidgetRecentActivitiesChanged> Changed { get; } =
        new("system.activity.recent.read.v1", "activity.recent.changed");
}

/// <summary>Typed, read-only launchable Start Menu library contract.</summary>
public static class WidgetAppLibraryCapabilities
{
    public static WidgetCapabilityOperation<WidgetAppLibraryCursorRequest, WidgetAppLibraryPage>
        GetPage { get; } = new("system.apps.library.read.v1", "apps.library.list");

    public static WidgetCapabilityOperation<ResolveSavedWidgetAppLibraryItemsRequest,
        ResolveSavedWidgetAppLibraryItemsResponse> ResolveSaved { get; } =
        new("system.apps.library.read.v1", "apps.library.resolve-saved");

    public static WidgetCapabilityOperation<LaunchWidgetAppLibraryItemRequest,
        WidgetCapabilityAcknowledgement> Launch { get; } =
        new("system.apps.library.launch.v1", "apps.library.launch");

    internal static WidgetCapabilityOperation<LaunchWidgetAppLibraryItemRequest,
        WidgetAppLaunchObservation> LaunchObserved { get; } =
        new("system.apps.library.launch.v1", "apps.library.launch-observed");
}

/// <summary>Typed, sanitized Windows system-media-session contracts.</summary>
public static class WidgetMediaCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetMediaSession>>
        GetSessions { get; } = new("system.media.sessions.read.v1", "media.sessions.get");

    public static WidgetCapabilityOperation<ControlWidgetMediaSessionRequest,
        WidgetCapabilityAcknowledgement> Control { get; } =
        new("system.media.sessions.control.v1", "media.session.control");

    public static WidgetCapabilityEvent<WidgetMediaSessionsChanged> Changed { get; } =
        new("system.media.sessions.read.v1", "media.sessions.changed");
}

public static class WidgetLoopbackCapabilities
{
    public const string CapabilityPrefix = "network.loopback:";
    public const string GetJsonOperation = "loopback.http.get-json";
    public const string PostJsonOperation = "loopback.http.post-json";

    public static string CapabilityId(int port) =>
        port is >= WidgetCommunityPlatformLimits.MinimumLoopbackPort and
            <= WidgetCommunityPlatformLimits.MaximumLoopbackPort
            ? $"{CapabilityPrefix}{port}"
            : throw new ArgumentOutOfRangeException(nameof(port));

    public static WidgetCapabilityOperation<WidgetLoopbackJsonRequest, WidgetLoopbackJsonResponse>
        GetJson(int port) => new(CapabilityId(port), GetJsonOperation);

    public static WidgetCapabilityOperation<WidgetLoopbackJsonRequest, WidgetLoopbackJsonResponse>
        PostJson(int port) => new(CapabilityId(port), PostJsonOperation);
}

public static class WidgetPrivateSecretCapabilities
{
    public const string CapabilityId = "storage.private-secrets.v1";
    public static WidgetCapabilityOperation<WidgetPrivateSecretSlotRequest, WidgetPrivateSecretExists>
        Exists { get; } = new(CapabilityId, "private-secret.exists");
    public static WidgetCapabilityOperation<WidgetPrivateSecretSlotRequest, WidgetPrivateSecretMetadata>
        Metadata { get; } = new(CapabilityId, "private-secret.metadata");
    public static WidgetCapabilityOperation<SaveWidgetPrivateSecretRequest, WidgetCapabilityAcknowledgement>
        Save { get; } = new(CapabilityId, "private-secret.save");
    public static WidgetCapabilityOperation<WidgetPrivateSecretSlotRequest, WidgetCapabilityAcknowledgement>
        Delete { get; } = new(CapabilityId, "private-secret.delete");
}

internal static class WidgetPrivateStateCapabilities
{
    internal const string CapabilityId = "storage.private-state.v1";
    internal static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetPrivateStateTransportSnapshot> Read { get; } =
        new(CapabilityId, "private-state.read");
    internal static WidgetCapabilityOperation<WriteWidgetPrivateStateTransportRequest,
        WidgetPrivateStateTransportMutation> Write { get; } =
        new(CapabilityId, "private-state.write");
    internal static WidgetCapabilityOperation<ClearWidgetPrivateStateTransportRequest,
        WidgetPrivateStateTransportMutation> Clear { get; } =
        new(CapabilityId, "private-state.clear");
}

public sealed class WidgetAudioService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetAudioService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<IReadOnlyList<WidgetAudioSession>> GetSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetSessions, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetAudioOutput> GetOutputAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetOutput, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<IReadOnlyList<WidgetAudioDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetDevices, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetAudioInput> GetInputAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetInput, new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SetSessionVolumeAsync(
        string sessionId, double volume, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetSessionVolume,
            new SetWidgetAudioSessionVolumeRequest(sessionId, volume), cancellationToken)
            .ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetSessionMutedAsync(
        string sessionId, bool isMuted, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetSessionMuted,
            new SetWidgetAudioSessionMutedRequest(sessionId, isMuted), cancellationToken)
            .ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetOutputVolumeAsync(
        double volume,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between zero and one.");
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetOutputVolume,
            new SetWidgetAudioOutputVolumeRequest(volume), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetOutputMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetOutputMuted,
            new SetWidgetAudioOutputMutedRequest(isMuted), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetInputVolumeAsync(
        double volume, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between zero and one.");
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetInputVolume,
            new SetWidgetAudioInputVolumeRequest(volume), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetInputMutedAsync(
        bool isMuted, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetInputMuted,
            new SetWidgetAudioInputMutedRequest(isMuted), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public IAsyncEnumerable<WidgetAudioSessionsChanged> WatchSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.SessionsChanged, cancellationToken);

    /// <summary>
    /// Opens an acknowledged session-change subscription. For a race-free
    /// current-state observer, await this first, call <see cref="GetSessionsAsync"/>,
    /// then consume <see cref="IWidgetCapabilitySubscription{TPayload}.ReadAllAsync"/>.
    /// Events are full coalesced session snapshots and therefore reconcile any
    /// changes that occurred while the current snapshot was being fetched.
    /// </summary>
    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioSessionsChanged>>
        OpenSessionsSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.SessionsChanged, cancellationToken);

    public IAsyncEnumerable<WidgetAudioOutputChanged> WatchOutputAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.OutputChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioOutputChanged>>
        OpenOutputSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.OutputChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioDevicesChanged>>
        OpenDevicesSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.DevicesChanged, cancellationToken);

    public IAsyncEnumerable<WidgetAudioDevicesChanged> WatchDevicesAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.DevicesChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioInputChanged>>
        OpenInputSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.InputChanged, cancellationToken);

    public IAsyncEnumerable<WidgetAudioInputChanged> WatchInputAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.InputChanged, cancellationToken);

    private static void DemandAcknowledged(WidgetCapabilityAcknowledgement response)
    {
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The audio provider returned an invalid acknowledgement.");
    }
}

public sealed class WidgetNetworkService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetNetworkService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<WidgetNetworkStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetNetworkCapabilities.GetStatus, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<IReadOnlyList<WidgetSavedNetworkProfile>> GetSavedProfilesAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetNetworkCapabilities.GetSavedProfiles, new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SwitchSavedProfileAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.SwitchSavedProfile,
            new SwitchWidgetSavedNetworkProfileRequest(profileId), cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The network provider returned an invalid acknowledgement.");
    }

    public IAsyncEnumerable<WidgetNetworkStatusChanged> WatchStatusAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetNetworkCapabilities.StatusChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetNetworkStatusChanged>>
        OpenStatusSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetNetworkCapabilities.StatusChanged, cancellationToken);

    public ValueTask<WidgetAvailableWifiNetworks> GetAvailableWifiAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetNetworkCapabilities.GetAvailableWifi,
            new WidgetCapabilityQuery(),
            cancellationToken);

    public async ValueTask RequestWifiScanAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.RequestWifiScan,
            new WidgetCapabilityQuery(),
            cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask ConnectAvailableWifiAsync(
        string networkId, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.ConnectAvailableWifi,
            new ConnectWidgetAvailableWifiNetworkRequest(networkId),
            cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public IAsyncEnumerable<WidgetAvailableWifiNetworksChanged> WatchAvailableWifiAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetNetworkCapabilities.AvailableWifiChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetAvailableWifiNetworksChanged>>
        OpenAvailableWifiSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetNetworkCapabilities.AvailableWifiChanged, cancellationToken);

    public ValueTask<WidgetWifiRadio> GetWifiRadioAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetNetworkCapabilities.GetWifiRadio,
            new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SetWifiRadioAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.SetWifiRadio,
            new SetWidgetWifiRadioRequest(enabled), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public IAsyncEnumerable<WidgetWifiRadioChanged> WatchWifiRadioAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetNetworkCapabilities.WifiRadioChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetWifiRadioChanged>>
        OpenWifiRadioSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetNetworkCapabilities.WifiRadioChanged, cancellationToken);

    public ValueTask<WidgetBluetoothSnapshot> GetBluetoothAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetNetworkCapabilities.GetBluetooth,
            new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SetBluetoothRadioAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.SetBluetoothRadio,
            new SetWidgetBluetoothRadioRequest(enabled), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask<WidgetBluetoothPairingResult> PairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken = default)
    {
        ValidateOpaqueId(deviceId, nameof(deviceId));
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.PairBluetoothDevice,
            new PairWidgetBluetoothDeviceRequest(deviceId), cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !Enum.IsDefined(response.Outcome))
            throw new WidgetCapabilityException(
                "malformed_response", "The Bluetooth provider returned an invalid pairing result.");
        return response;
    }

    public async ValueTask<WidgetBluetoothUnpairingResult> UnpairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken = default)
    {
        ValidateOpaqueId(deviceId, nameof(deviceId));
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.UnpairBluetoothDevice,
            new UnpairWidgetBluetoothDeviceRequest(deviceId), cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !Enum.IsDefined(response.Outcome))
            throw new WidgetCapabilityException(
                "malformed_response", "The Bluetooth provider returned an invalid removal result.");
        return response;
    }

    public async ValueTask OpenBluetoothDeviceSettingsAsync(
        string deviceId, CancellationToken cancellationToken = default)
    {
        ValidateOpaqueId(deviceId, nameof(deviceId));
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.OpenBluetoothDeviceSettings,
            new OpenWidgetBluetoothDeviceSettingsRequest(deviceId), cancellationToken)
            .ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public IAsyncEnumerable<WidgetBluetoothChanged> WatchBluetoothAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetNetworkCapabilities.BluetoothChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetBluetoothChanged>>
        OpenBluetoothSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetNetworkCapabilities.BluetoothChanged, cancellationToken);

    private static void DemandAcknowledged(WidgetCapabilityAcknowledgement response)
    {
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The network provider returned an invalid acknowledgement.");
    }

    private static void ValidateOpaqueId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
            throw new ArgumentException(
                "The Bluetooth device identifier is invalid.", parameterName);
    }
}

public sealed class WidgetRecentActivityService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetRecentActivityService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<IReadOnlyList<WidgetRecentActivity>> GetRecentAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetRecentActivityCapabilities.GetRecent,
            new WidgetCapabilityQuery(), cancellationToken);

    public IAsyncEnumerable<WidgetRecentActivitiesChanged> WatchAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetRecentActivityCapabilities.Changed, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetRecentActivitiesChanged>>
        OpenSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetRecentActivityCapabilities.Changed, cancellationToken);
}

public sealed class WidgetAppLibraryService
{
    public const int MaximumPageSize = 64;
    public const int MaximumSavedItems = 64;
    private const int MaximumOpaqueIdLength = 128;
    private readonly IWidgetCapabilityClient _client;

    internal WidgetAppLibraryService(IWidgetCapabilityClient client) => _client = client;

    /// <summary>
    /// Reads one bounded page. Cursors are opaque and bound to the exact query,
    /// provider revision, direction, and page size.
    /// </summary>
    public async ValueTask<WidgetAppLibraryPage> QueryAsync(
        WidgetAppLibraryQuery query,
        WidgetCollectionCursor? cursor = null,
        WidgetCursorDirection? direction = null,
        int limit = MaximumPageSize,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalizedSearch = NormalizeSearchText(query.SearchText);
        if (!Enum.IsDefined(query.Sort) ||
            query.Kind is { } kind && !Enum.IsDefined(kind) ||
            query.SourceAttribution is { } source &&
                (string.IsNullOrWhiteSpace(source) || source.Length > 64 ||
                    source.Any(char.IsControl)) ||
            query.SearchText is not null && normalizedSearch is null ||
            query.FavoriteSavedIds is null ||
            query.FavoriteSavedIds.Count > WidgetAppLibraryQuery.MaximumFavoriteSavedIds ||
            query.FavoriteSavedIds.Distinct(StringComparer.Ordinal).Count() !=
                query.FavoriteSavedIds.Count ||
            query.FavoriteSavedIds.Any(savedId => !IsValidSavedId(savedId)))
            throw new ArgumentException("The app-library query is invalid.", nameof(query));
        if ((cursor is null) != (direction is null))
            throw new ArgumentException(
                "A cursor and direction must be supplied together.", nameof(cursor));
        if (limit is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var page = await _client.InvokeAsync(
            WidgetAppLibraryCapabilities.GetPage,
            new WidgetAppLibraryCursorRequest(
                query with
                {
                    SearchText = normalizedSearch,
                    FavoriteSavedIds = query.FavoriteSavedIds.ToArray(),
                }, cursor?.Value, direction, limit, refresh),
            cancellationToken).ConfigureAwait(false);
        if (page?.Items is null || page.Items.Count > limit ||
            page.Sources is null || page.Sources.Count > 16 ||
            page.Revision is not { Length: > 0 and <= 128 } ||
            page.Before is { Length: > 128 } || page.After is { Length: > 128 })
            throw MalformedPage();
        ValidatePageItems(page.Items);
        ValidatePageSources(page.Sources);
        try
        {
            if (page.Before is not null) _ = new WidgetCollectionCursor(page.Before);
            if (page.After is not null) _ = new WidgetCollectionCursor(page.After);
        }
        catch (ArgumentException)
        {
            throw MalformedPage();
        }
        return page;
    }

    internal static string? NormalizeSearchText(string? value)
    {
        if (value is null) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return null;
        return normalized.Length <= WidgetAppLibraryQuery.MaximumSearchTextLength &&
               !normalized.Any(char.IsControl) ? normalized : null;
    }

    private static bool IsValidSavedId(string? value) =>
        value is { Length: > 6 and <= 128 } &&
        value.StartsWith("saved-", StringComparison.Ordinal) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    public ValueTask LaunchAsync(
        string appId,
        CancellationToken cancellationToken = default) =>
        LaunchAsync(appId, WidgetAppLaunchOverlayBehavior.KeepOpen, cancellationToken);

    public async ValueTask LaunchAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken = default)
    {
        ValidateOpaqueId(appId, nameof(appId));
        if (!Enum.IsDefined(overlayBehavior))
            throw new ArgumentOutOfRangeException(nameof(overlayBehavior));
        var response = await _client.InvokeAsync(
            WidgetAppLibraryCapabilities.Launch,
            new LaunchWidgetAppLibraryItemRequest(appId)
            {
                CloseOverlayOnSuccess =
                    overlayBehavior == WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
            },
            cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The app library provider returned an invalid acknowledgement.");
    }

    internal async ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken = default)
    {
        ValidateOpaqueId(appId, nameof(appId));
        if (!Enum.IsDefined(overlayBehavior))
            throw new ArgumentOutOfRangeException(nameof(overlayBehavior));
        var response = await _client.InvokeAsync(
            WidgetAppLibraryCapabilities.LaunchObserved,
            new LaunchWidgetAppLibraryItemRequest(appId)
            {
                CloseOverlayOnSuccess =
                    overlayBehavior == WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
            }, cancellationToken).ConfigureAwait(false);
        if (response is null || !Enum.IsDefined(response.State) ||
            response.State == WidgetAppLaunchObservationState.Running &&
                !response.SupportsRunning ||
            response.State == WidgetAppLaunchObservationState.Ended &&
                !response.SupportsEnded)
            throw new WidgetCapabilityException(
                "malformed_response", "The app library provider returned invalid launch evidence.");
        return response;
    }

    /// <summary>
    /// Resolves durable SavedIds from private state to current launch tokens.
    /// Results preserve request order; IDs for apps that are no longer
    /// available are omitted. SavedIds are authority-scoped and cannot be used
    /// by another widget package.
    /// </summary>
    public async ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(savedIds);
        if (savedIds.Count > MaximumSavedItems)
            throw new ArgumentOutOfRangeException(nameof(savedIds));
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in savedIds)
        {
            ValidateOpaqueId(savedId, nameof(savedIds));
            if (!savedId.StartsWith("saved-", StringComparison.Ordinal) ||
                !unique.Add(savedId))
                throw new ArgumentException(
                    "Saved app identifiers must be unique host-issued tokens.", nameof(savedIds));
        }
        if (savedIds.Count == 0) return [];
        var response = await _client.InvokeAsync(
            WidgetAppLibraryCapabilities.ResolveSaved,
            new ResolveSavedWidgetAppLibraryItemsRequest(savedIds.ToArray()),
            cancellationToken).ConfigureAwait(false);
        if (response?.Items is null || response.Items.Count > savedIds.Count)
            throw new WidgetCapabilityException(
                "malformed_response", "The app library provider returned an invalid resolution.");
        var requestedPositions = savedIds
            .Select((savedId, index) => (savedId, index))
            .ToDictionary(entry => entry.savedId, entry => entry.index, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lastPosition = -1;
        foreach (var item in response.Items)
        {
            if (item is null)
                throw MalformedResolution();
            try
            {
                ValidateOpaqueId(item.AppId, nameof(response));
                ValidateOpaqueId(item.SavedId, nameof(response));
            }
            catch (ArgumentException)
            {
                throw MalformedResolution();
            }
            if (!requestedPositions.TryGetValue(item.SavedId, out var position) ||
                position <= lastPosition || !seen.Add(item.SavedId) ||
                string.IsNullOrWhiteSpace(item.DisplayName) ||
                item.DisplayName.Length > 160 || item.DisplayName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(item.SourceAttribution) ||
                item.SourceAttribution.Length > 64 ||
                item.SourceAttribution.Any(char.IsControl) ||
                !Enum.IsDefined(item.Kind))
                throw MalformedResolution();
            lastPosition = position;
        }
        return response.Items.ToArray();
    }

    private static WidgetCapabilityException MalformedResolution() => new(
        "malformed_response", "The app library provider returned an invalid resolution.");

    private static WidgetCapabilityException MalformedPage() => new(
        "malformed_response", "The app library provider returned an invalid cursor page.");

    private static void ValidatePageSources(IReadOnlyList<WidgetAppLibrarySource> sources)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null || !Enum.IsDefined(source.Health) ||
                source.Revision < 0 ||
                string.IsNullOrWhiteSpace(source.DisplayName) ||
                source.DisplayName.Length > 64 || source.DisplayName.Any(char.IsControl) ||
                source.StatusCode is not { Length: > 0 and <= 48 } ||
                source.StatusCode.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
                throw MalformedPage();
            try { ValidateOpaqueId(source.SourceId, nameof(sources)); }
            catch (ArgumentException) { throw MalformedPage(); }
            if (!sourceIds.Add(source.SourceId)) throw MalformedPage();
        }
    }

    private static void ValidatePageItems(IReadOnlyList<WidgetAppLibraryItem> items)
    {
        var appIds = new HashSet<string>(StringComparer.Ordinal);
        var savedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || !Enum.IsDefined(item.Kind) ||
                string.IsNullOrWhiteSpace(item.DisplayName) ||
                item.DisplayName.Length > 160 || item.DisplayName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(item.SourceAttribution) ||
                item.SourceAttribution.Length > 64 ||
                item.SourceAttribution.Any(char.IsControl))
                throw MalformedPage();
            try
            {
                ValidateOpaqueId(item.AppId, nameof(items));
                ValidateOpaqueId(item.SavedId, nameof(items));
            }
            catch (ArgumentException)
            {
                throw MalformedPage();
            }
            if (!appIds.Add(item.AppId) || !savedIds.Add(item.SavedId))
                throw MalformedPage();
        }
    }

    private static void ValidateOpaqueId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumOpaqueIdLength ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-')))
            throw new ArgumentException("The app identifier is invalid.", parameterName);
    }
}

public sealed class WidgetMediaService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetMediaService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<IReadOnlyList<WidgetMediaSession>> GetSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetMediaCapabilities.GetSessions, new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask ControlAsync(
        string sessionId,
        WidgetMediaSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        var response = await _client.InvokeAsync(
            WidgetMediaCapabilities.Control,
            new ControlWidgetMediaSessionRequest(sessionId, command),
            cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The media provider returned an invalid acknowledgement.");
    }

    public ValueTask<IWidgetCapabilitySubscription<WidgetMediaSessionsChanged>>
        OpenSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetMediaCapabilities.Changed, cancellationToken);

    public IAsyncEnumerable<WidgetMediaSessionsChanged> WatchAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetMediaCapabilities.Changed, cancellationToken);
}

/// <summary>
/// Constrained JSON HTTP for a manifest-declared, exact nonprivileged loopback
/// port. This API never accepts a host name, URI authority, proxy, redirect or
/// raw socket and therefore does not grant ambient network access.
/// </summary>
public sealed class WidgetLoopbackHttpService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetLoopbackHttpService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<WidgetLoopbackJsonResponse> GetJsonAsync(
        int port,
        string path,
        WidgetLoopbackRequestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(port, path, jsonBody: null, options, cancellationToken);

    public ValueTask<WidgetLoopbackJsonResponse> PostJsonAsync(
        int port,
        string path,
        string jsonBody,
        WidgetLoopbackRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);
        return SendAsync(port, path, jsonBody, options, cancellationToken);
    }

    private ValueTask<WidgetLoopbackJsonResponse> SendAsync(
        int port,
        string path,
        string? jsonBody,
        WidgetLoopbackRequestOptions? options,
        CancellationToken cancellationToken)
    {
        var capabilityId = WidgetLoopbackCapabilities.CapabilityId(port);
        ValidatePath(path);
        options ??= new WidgetLoopbackRequestOptions();
        var headers = ValidateHeaders(options.Headers);
        if (options.BearerSecretSlot is { } slot) ValidateSlot(slot);
        if (options.InvalidateBearerSecretOnUnauthorized && options.BearerSecretSlot is null)
            throw new ArgumentException(
                "Unauthorized bearer invalidation requires a bearer secret slot.",
                nameof(options));
        var timeoutMilliseconds = ValidateTimeout(options.Timeout);
        if (jsonBody is not null) ValidateJson(jsonBody);
        var request = new WidgetLoopbackJsonRequest(
            path, headers, options.BearerSecretSlot, jsonBody, timeoutMilliseconds,
            options.InvalidateBearerSecretOnUnauthorized);
        return _client.InvokeAsync(
            jsonBody is null
                ? WidgetLoopbackCapabilities.GetJson(port)
                : WidgetLoopbackCapabilities.PostJson(port),
            request,
            cancellationToken);
    }

    private static void ValidatePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length is < 1 or > WidgetCommunityPlatformLimits.MaximumLoopbackPathCharacters ||
            path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('#') ||
            path.Any(character => character is '\r' or '\n' || char.IsControl(character)) ||
            !Uri.TryCreate(path, UriKind.Relative, out _))
            throw new ArgumentException("Path must be a bounded origin-form path.", nameof(path));
    }

    private static IReadOnlyList<WidgetLoopbackHttpHeader> ValidateHeaders(
        IReadOnlyDictionary<string, string>? source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count > WidgetCommunityPlatformLimits.MaximumLoopbackHeaderCount)
            throw new ArgumentException("Too many loopback request headers.", nameof(source));
        var result = new List<WidgetLoopbackHttpHeader>(source.Count);
        var total = 0;
        foreach (var (name, value) in source.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(name) ||
                name.Length > WidgetCommunityPlatformLimits.MaximumLoopbackHeaderNameCharacters ||
                value is null ||
                value.Length > WidgetCommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters ||
                !name.All(IsHttpTokenCharacter) ||
                value.Any(character => character is '\r' or '\n' || char.IsControl(character)) ||
                IsRestrictedRequestHeader(name))
                throw new ArgumentException("A loopback request header is invalid.", nameof(source));
            total += name.Length + value.Length;
            if (total > WidgetCommunityPlatformLimits.MaximumLoopbackHeaderCharacters)
                throw new ArgumentException("Loopback request headers are too large.", nameof(source));
            result.Add(new WidgetLoopbackHttpHeader(name, value));
        }
        return result.AsReadOnly();
    }

    private static int ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromMilliseconds(
                WidgetCommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds) ||
            !double.IsFinite(timeout.TotalMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return checked((int)Math.Ceiling(timeout.TotalMilliseconds));
    }

    private static void ValidateJson(string json)
    {
        if (json.Length == 0 || Encoding.UTF8.GetByteCount(json) >
            WidgetCommunityPlatformLimits.MaximumLoopbackRequestBodyUtf8Bytes)
            throw new ArgumentException("Loopback JSON body is empty or too large.", nameof(json));
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Loopback request body must be valid JSON.", nameof(json), exception);
        }
    }

    private static bool IsRestrictedRequestHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or
            '^' or '_' or '`' or '|' or '~';

    internal static void ValidateSlot(string slot)
    {
        if (string.IsNullOrEmpty(slot) ||
            slot.Length > WidgetCommunityPlatformLimits.MaximumPrivateSecretSlotCharacters ||
            !slot.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-'))
            throw new ArgumentException("Private secret slot is invalid.", nameof(slot));
    }
}

/// <summary>
/// Package-scoped private persistence for authentication material. Stored
/// values can be replaced or deleted but are deliberately never readable by a
/// widget; loopback HTTP may ask the host to inject a slot as a Bearer value.
/// </summary>
public sealed class WidgetPrivateSecretService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetPrivateSecretService(IWidgetCapabilityClient client) => _client = client;

    public async ValueTask<bool> ExistsAsync(
        string slot, CancellationToken cancellationToken = default)
    {
        WidgetLoopbackHttpService.ValidateSlot(slot);
        var result = await _client.InvokeAsync(
            WidgetPrivateSecretCapabilities.Exists,
            new WidgetPrivateSecretSlotRequest(slot),
            cancellationToken).ConfigureAwait(false);
        return result?.Exists ?? throw new WidgetCapabilityException(
            "malformed_response", "The private secret provider returned an invalid result.");
    }

    public ValueTask<WidgetPrivateSecretMetadata> GetMetadataAsync(
        string slot, CancellationToken cancellationToken = default)
    {
        WidgetLoopbackHttpService.ValidateSlot(slot);
        return _client.InvokeAsync(
            WidgetPrivateSecretCapabilities.Metadata,
            new WidgetPrivateSecretSlotRequest(slot),
            cancellationToken);
    }

    public async ValueTask SaveAsync(
        string slot, string secret, CancellationToken cancellationToken = default)
    {
        WidgetLoopbackHttpService.ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length == 0 || secret.Contains('\0'))
            throw new ArgumentException("Private secret is empty or too large.", nameof(secret));
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(secret) >
                WidgetCommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes)
                throw new ArgumentException("Private secret is empty or too large.", nameof(secret));
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Private secret is not valid UTF-8 text.", nameof(secret), exception);
        }
        DemandAcknowledged(await _client.InvokeAsync(
            WidgetPrivateSecretCapabilities.Save,
            new SaveWidgetPrivateSecretRequest(slot, secret),
            cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask DeleteAsync(
        string slot, CancellationToken cancellationToken = default)
    {
        WidgetLoopbackHttpService.ValidateSlot(slot);
        DemandAcknowledged(await _client.InvokeAsync(
            WidgetPrivateSecretCapabilities.Delete,
            new WidgetPrivateSecretSlotRequest(slot),
            cancellationToken).ConfigureAwait(false));
    }

    private static void DemandAcknowledged(WidgetCapabilityAcknowledgement? response)
    {
        if (response?.Acknowledged != true)
            throw new WidgetCapabilityException(
                "malformed_response", "The private secret provider returned an invalid acknowledgement.");
    }
}
