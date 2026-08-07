namespace GameBarAlternative.PlatformBroker;

/// <summary>Deterministic seam for tests and development; owns no OS resource.</summary>
public sealed class SimulatedPlatformBrokerBackend : IPlatformBrokerBackend
{
    private readonly List<AudioSessionSummary> _audioSessions = [];
    private readonly List<SavedNetworkProfileSummary> _networkProfiles = [];
    private readonly List<AvailableWifiNetworkSummary> _availableWifiNetworks = [];

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
    public WifiScanState WifiScanState { get; set; } = WifiScanState.NotScanned;
    public AudioOutputSummary AudioOutput { get; set; } = new(0.5, false);

    public void SetAudioSessions(IEnumerable<AudioSessionSummary> sessions)
    {
        _audioSessions.Clear();
        _audioSessions.AddRange(sessions);
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
}
