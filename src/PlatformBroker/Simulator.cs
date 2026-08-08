namespace GameBarAlternative.PlatformBroker;

/// <summary>Deterministic seam for tests and development; owns no OS resource.</summary>
public sealed class SimulatedPlatformBrokerBackend : IPlatformBrokerBackend
{
    private readonly List<AudioSessionSummary> _audioSessions = [];
    private readonly List<AudioDeviceSummary> _audioDevices = [];
    private readonly List<SavedNetworkProfileSummary> _networkProfiles = [];
    private readonly List<AvailableWifiNetworkSummary> _availableWifiNetworks = [];
    private readonly List<RecentActivitySummary> _recentActivities = [];
    private readonly List<BluetoothDeviceSummary> _bluetoothDevices = [];
    private readonly List<MediaSessionSummary> _mediaSessions = [];

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public NetworkStatusSummary NetworkStatus { get; set; } =
        new(
            NetworkConnectivity.None,
            NetworkTransportKind.None,
            NetworkWirelessAvailability.NoAdapter,
            NetworkDetailsAccess.Unavailable,
            NetworkConnectionAttemptState.None,
            null,
            null,
            null,
            null);

    public int AudioControlCalls { get; private set; }
    public int NetworkSwitchCalls { get; private set; }
    public int WifiScanCalls { get; private set; }
    public int WifiConnectCalls { get; private set; }
    public int WifiRadioControlCalls { get; private set; }
    public int RecentActivityActivationCalls { get; private set; }
    public int BluetoothRadioControlCalls { get; private set; }
    public string? LastActivatedActivityId { get; private set; }
    public int MediaControlCalls { get; private set; }
    public string? LastControlledMediaSessionId { get; private set; }
    public MediaSessionCommand? LastMediaCommand { get; private set; }
    public WifiRadioSummary WifiRadio { get; set; } = new(WifiRadioState.On, true);
    public BluetoothRadioState BluetoothRadioState { get; set; } = BluetoothRadioState.On;
    public bool CanControlBluetoothRadio { get; set; } = true;
    public WifiScanState WifiScanState { get; set; } = WifiScanState.NotScanned;
    public AudioOutputSummary AudioOutput { get; set; } = new(0.5, false);
    public AudioInputSummary AudioInput { get; set; } = new(0.5, false);

    public void SetAudioSessions(IEnumerable<AudioSessionSummary> sessions)
    {
        _audioSessions.Clear();
        _audioSessions.AddRange(sessions);
    }

    public void SetAudioDevices(IEnumerable<AudioDeviceSummary> devices)
    {
        _audioDevices.Clear();
        _audioDevices.AddRange(devices);
    }

    public void SetSavedNetworkProfiles(IEnumerable<SavedNetworkProfileSummary> profiles)
    {
        _networkProfiles.Clear();
        _networkProfiles.AddRange(profiles);
    }

    public void SetAvailableWifiNetworks(IEnumerable<AvailableWifiNetworkSummary> networks)
    {
        _availableWifiNetworks.Clear();
        _availableWifiNetworks.AddRange(networks);
        WifiScanState = WifiScanState.Ready;
    }

    public void SetRecentActivities(IEnumerable<RecentActivitySummary> activities)
    {
        _recentActivities.Clear();
        _recentActivities.AddRange(activities);
    }

    public void SetBluetoothDevices(IEnumerable<BluetoothDeviceSummary> devices)
    {
        _bluetoothDevices.Clear();
        _bluetoothDevices.AddRange(devices);
    }

    public void SetMediaSessions(IEnumerable<MediaSessionSummary> sessions)
    {
        _mediaSessions.Clear();
        _mediaSessions.AddRange(sessions);
    }

    public void Publish(BrokerPlatformEvent platformEvent) =>
        EventPublished?.Invoke(this, platformEvent);

    public Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AudioSessionSummary>>(_audioSessions.ToArray());
    }

    public Task SetAudioSessionVolumeAsync(
        string sessionId, double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        return Task.CompletedTask;
    }

    public Task SetAudioSessionMutedAsync(
        string sessionId, bool isMuted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        return Task.CompletedTask;
    }

    public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AudioOutput);
    }

    public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        AudioOutput = AudioOutput with { Volume = volume };
        return Task.CompletedTask;
    }

    public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        AudioOutput = AudioOutput with { IsMuted = isMuted };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AudioDeviceSummary>>(_audioDevices.ToArray());
    }

    public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AudioInput);
    }

    public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        AudioInput = AudioInput with { Volume = volume };
        return Task.CompletedTask;
    }

    public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioControlCalls++;
        AudioInput = AudioInput with { IsMuted = isMuted };
        return Task.CompletedTask;
    }

    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NetworkStatus);
    }

    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SavedNetworkProfileSummary>>(_networkProfiles.ToArray());
    }

    public Task SwitchSavedNetworkProfileAsync(
        string profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetworkSwitchCalls++;
        return Task.CompletedTask;
    }

    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AvailableWifiNetworksSummary(
            WifiScanState, _availableWifiNetworks.ToArray()));
    }

    public Task RequestWifiScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WifiScanCalls++;
        WifiScanState = WifiScanState.Scanning;
        _availableWifiNetworks.Clear();
        return Task.CompletedTask;
    }

    public Task ConnectAvailableWifiNetworkAsync(
        string networkId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WifiConnectCalls++;
        return Task.CompletedTask;
    }

    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WifiRadio);
    }

    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WifiRadioControlCalls++;
        WifiRadio = new(enabled ? WifiRadioState.On : WifiRadioState.Off, true);
        EventPublished?.Invoke(this, new BrokerPlatformEvent(
            PlatformCapabilities.NetworkWifiRadioReadV1,
            PlatformCapabilities.NetworkWifiRadioChanged,
            new WifiRadioChangedEvent(WifiRadio)));
        return Task.CompletedTask;
    }

    public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BluetoothSummary(
            BluetoothRadioState, CanControlBluetoothRadio,
            BluetoothDiscoveryState.Ready, _bluetoothDevices.ToArray()));
    }

    public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BluetoothRadioControlCalls++;
        BluetoothRadioState = enabled ? BluetoothRadioState.On : BluetoothRadioState.Off;
        var snapshot = new BluetoothSummary(
            BluetoothRadioState, CanControlBluetoothRadio,
            BluetoothDiscoveryState.Ready, _bluetoothDevices.ToArray());
        EventPublished?.Invoke(this, new BrokerPlatformEvent(
            PlatformCapabilities.NetworkBluetoothReadV1,
            PlatformCapabilities.NetworkBluetoothChanged,
            new BluetoothChangedEvent(snapshot)));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RecentActivitySummary>>(_recentActivities.ToArray());
    }

    public Task ActivateRecentActivityAsync(
        string activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecentActivityActivationCalls++;
        LastActivatedActivityId = activityId;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MediaSessionSummary>> GetMediaSessionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MediaSessionSummary>>(_mediaSessions.ToArray());
    }

    public Task ControlMediaSessionAsync(
        string sessionId,
        MediaSessionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaControlCalls++;
        LastControlledMediaSessionId = sessionId;
        LastMediaCommand = command;
        return Task.CompletedTask;
    }
}
