using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetCapabilityQuery;

public sealed record WidgetCapabilityAcknowledgement(
    [property: JsonRequired] bool Acknowledged);

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

public sealed record ActivateWidgetRecentActivityRequest(
    [property: JsonRequired] string ActivityId);

public sealed record WidgetRecentActivitiesChanged(
    [property: JsonRequired] IReadOnlyList<WidgetRecentActivity> Activities);

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

    public static WidgetCapabilityEvent<WidgetBluetoothChanged> BluetoothChanged { get; } =
        new("system.network.bluetooth.read.v1", "network.bluetooth.changed");
}

/// <summary>Typed recent foreground-activity contracts with no OS identifiers.</summary>
public static class WidgetRecentActivityCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetRecentActivity>>
        GetRecent { get; } = new("system.activity.recent.read.v1", "activity.recent.list");

    public static WidgetCapabilityOperation<ActivateWidgetRecentActivityRequest,
        WidgetCapabilityAcknowledgement> Activate { get; } =
        new("system.activity.recent.activate.v1", "activity.recent.activate");

    public static WidgetCapabilityEvent<WidgetRecentActivitiesChanged> Changed { get; } =
        new("system.activity.recent.read.v1", "activity.recent.changed");
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

    public async ValueTask ActivateAsync(
        string activityId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        var response = await _client.InvokeAsync(
            WidgetRecentActivityCapabilities.Activate,
            new ActivateWidgetRecentActivityRequest(activityId), cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The recent activity provider returned an invalid acknowledgement.");
    }

    public IAsyncEnumerable<WidgetRecentActivitiesChanged> WatchAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetRecentActivityCapabilities.Changed, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetRecentActivitiesChanged>>
        OpenSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetRecentActivityCapabilities.Changed, cancellationToken);
}
