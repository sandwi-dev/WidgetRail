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

public sealed record ActivateRecentActivityRequest(
    [property: JsonRequired] string ActivityId);

public sealed record RecentActivitiesChangedEvent(
    IReadOnlyList<RecentActivitySummary> Activities);

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
    bool CanNext);

public sealed record ControlMediaSessionRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] MediaSessionCommand Command);

public sealed record MediaSessionsChangedEvent(
    IReadOnlyList<MediaSessionSummary> Sessions);

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
    Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken);
    Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken);
    Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken);
    Task RequestWifiScanAsync(CancellationToken cancellationToken);
    Task ConnectAvailableWifiNetworkAsync(string networkId, CancellationToken cancellationToken);
    Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken);
    Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken);
}

public interface IActivityPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken);
    Task ActivateRecentActivityAsync(string activityId, CancellationToken cancellationToken);
}

public interface IBluetoothPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken);
    Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken);
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
/// Complete host backend. Providers can implement the narrower audio or network
/// contracts and be joined with <see cref="CompositePlatformBrokerBackend"/>.
/// </summary>
public interface IPlatformBrokerBackend : IAudioPlatformBrokerBackend,
    INetworkPlatformBrokerBackend, IActivityPlatformBrokerBackend,
    IBluetoothPlatformBrokerBackend, IMediaPlatformBrokerBackend
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
