namespace GameBarAlternative.PlatformBroker;

/// <summary>Deterministic seam for tests and development; owns no OS resource.</summary>
public sealed class SimulatedPlatformBrokerBackend : IPlatformBrokerBackend
{
    private readonly List<AudioSessionSummary> _audioSessions = [];
    private readonly List<AudioDeviceSummary> _audioDevices = [];
    private readonly List<SavedNetworkProfileSummary> _networkProfiles = [];
    private readonly List<AvailableWifiNetworkSummary> _availableWifiNetworks = [];
    private readonly List<RecentActivitySummary> _recentActivities = [];
    private readonly List<AppLibraryBackendItemSummary> _appLibrary = [];
    private readonly List<BluetoothDeviceSummary> _bluetoothDevices = [];
    private readonly List<MediaSessionSummary> _mediaSessions = [];
    private readonly Dictionary<string, (string Secret, long WrittenAt)> _privateSecrets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? JsonBase64, long Revision)> _privateState =
        new(StringComparer.Ordinal);

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
    public int BluetoothRadioControlCalls { get; private set; }
    public int BluetoothPairCalls { get; private set; }
    public int BluetoothManageCalls { get; private set; }
    public int MediaControlCalls { get; private set; }
    public int SpotifyConfigurationCalls { get; private set; }
    public int SpotifyConnectCalls { get; private set; }
    public int SpotifyDisconnectCalls { get; private set; }
    public int SpotifyPlaybackControlCalls { get; private set; }
    public int SpotifyTransferCalls { get; private set; }
    public int SpotifyQueueAddCalls { get; private set; }
    public int SpotifyPlaybackStartCalls { get; private set; }
    public int SpotifyLocalPlaybackControlCalls { get; private set; }
    public int AppLibraryLaunchCalls { get; private set; }
    public int AppLibraryReadCalls { get; private set; }
    public int AppLibraryRefreshCalls { get; private set; }
    public int AppLibraryIconCalls { get; private set; }
    public int LoopbackCalls { get; private set; }
    public int PrivateSecretSaveCalls { get; private set; }
    public int PrivateSecretDeleteCalls { get; private set; }
    public int LastLoopbackPort { get; private set; }
    public bool LastLoopbackWasPost { get; private set; }
    public LoopbackJsonRequest? LastLoopbackRequest { get; private set; }
    public Func<BrokerWidgetIdentity, int, bool, LoopbackJsonRequest,
        CancellationToken, Task<LoopbackJsonResponse>>? LoopbackHandler { get; set; }
    public Func<string, CancellationToken, Task<AppLibraryIconSummary>>?
        AppLibraryIconHandler { get; set; }
    public string? LastLaunchedAppId { get; private set; }
    public string? LastControlledMediaSessionId { get; private set; }
    public MediaSessionCommand? LastMediaCommand { get; private set; }
    public BrokerWidgetIdentity? LastSpotifyIdentity { get; private set; }
    public ConnectSpotifyRequest? LastSpotifyConnectRequest { get; private set; }
    public SpotifyPlaybackCommand? LastSpotifyPlaybackCommand { get; private set; }
    public TransferSpotifyPlaybackRequest? LastSpotifyTransferRequest { get; private set; }
    public AddSpotifyQueueItemRequest? LastSpotifyQueueAddRequest { get; private set; }
    public StartSpotifyPlaybackRequest? LastSpotifyStartRequest { get; private set; }
    public WifiRadioSummary WifiRadio { get; set; } = new(WifiRadioState.On, true);
    public BluetoothRadioState BluetoothRadioState { get; set; } = BluetoothRadioState.On;
    public bool CanControlBluetoothRadio { get; set; } = true;
    public BluetoothPairingResultStatus BluetoothPairingResult { get; set; } =
        BluetoothPairingResultStatus.Paired;
    public string? LastBluetoothDeviceId { get; private set; }
    public WifiScanState WifiScanState { get; set; } = WifiScanState.NotScanned;
    public AudioOutputSummary AudioOutput { get; set; } = new(0.5, false);
    public AudioInputSummary AudioInput { get; set; } = new(0.5, false);
    public SpotifyConfigurationSummary SpotifyConfiguration { get; set; } =
        new(false, "http://127.0.0.1:43827/callback/");
    public SpotifyAuthorizationSummary SpotifyAuthorization { get; set; } =
        new(SpotifyAuthorizationState.Unconfigured, [], [], null);
    public SpotifyPlaybackSummary SpotifyPlayback { get; set; } = new(
        false, false, 0, 0, 0, SpotifyRepeatState.Off, false, null,
        new SpotifyPlaybackDisallowedActions(false, false, false, false,
            false, false, false, false), "Spotify");
    public SpotifyDevicesSummary SpotifyDevices { get; set; } = new([]);
    public SpotifyQueueSummary SpotifyQueue { get; set; } = new(null, [], false);
    public SpotifyLocalPlaybackSummary SpotifyLocalPlayback { get; set; } = new(
        SpotifyLocalPlaybackState.Disabled, "This PC · Game Bar", null, null);
    public SpotifyPlaylistPageSummary SpotifyPlaylists { get; set; } = new([], 0, 20, 0);
    public SpotifyPlaylistItemsSummary? SpotifyPlaylistItems { get; set; }

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

    public void SetAppLibrary(IEnumerable<AppLibraryItemSummary> items)
    {
        _appLibrary.Clear();
        _appLibrary.AddRange(items.Select(item => new AppLibraryBackendItemSummary(
            item.AppId, item.AppId, item.DisplayName, item.Kind)));
    }

    public void SetAppLibraryBackend(IEnumerable<AppLibraryBackendItemSummary> items)
    {
        _appLibrary.Clear();
        _appLibrary.AddRange(items);
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

    public Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BluetoothPairCalls++;
        LastBluetoothDeviceId = deviceId;
        return Task.FromResult(new BluetoothPairingResultSummary(BluetoothPairingResult));
    }

    public Task OpenBluetoothDeviceSettingsAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BluetoothManageCalls++;
        LastBluetoothDeviceId = deviceId;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RecentActivitySummary>>(_recentActivities.ToArray());
    }

    public Task<IReadOnlyList<AppLibraryBackendItemSummary>> GetAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryReadCalls++;
        return Task.FromResult<IReadOnlyList<AppLibraryBackendItemSummary>>(_appLibrary.ToArray());
    }

    public Task<IReadOnlyList<AppLibraryBackendItemSummary>> RefreshAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryRefreshCalls++;
        return Task.FromResult<IReadOnlyList<AppLibraryBackendItemSummary>>(_appLibrary.ToArray());
    }

    public Task LaunchAppLibraryItemAsync(
        string appId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryLaunchCalls++;
        LastLaunchedAppId = appId;
        return Task.CompletedTask;
    }

    public Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
        string appId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryIconCalls++;
        return AppLibraryIconHandler?.Invoke(appId, cancellationToken) ??
            Task.FromResult(new AppLibraryIconSummary(null));
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

    public Task<SpotifyConfigurationSummary> GetSpotifyConfigurationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyConfiguration);
    }

    public Task<SpotifyConfigurationSummary> ConfigureSpotifyClientAsync(
        BrokerWidgetIdentity identity, ConfigureSpotifyClientRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyConfigurationCalls++;
        LastSpotifyIdentity = identity;
        SpotifyConfiguration = SpotifyConfiguration with { IsConfigured = true };
        SpotifyAuthorization = new(
            SpotifyAuthorizationState.Disconnected, [], [], null);
        return Task.FromResult(SpotifyConfiguration);
    }

    public Task<SpotifyAuthorizationSummary> GetSpotifyAuthorizationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyAuthorization);
    }

    public Task<SpotifyAuthorizationSummary> ConnectSpotifyAsync(
        BrokerWidgetIdentity identity, ConnectSpotifyRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyConnectCalls++;
        LastSpotifyIdentity = identity;
        LastSpotifyConnectRequest = request;
        SpotifyAuthorization = new(
            SpotifyAuthorizationState.Connected,
            request.RequestedScopes.ToArray(), request.RequestedScopes.ToArray(), null);
        return Task.FromResult(SpotifyAuthorization);
    }

    public Task<SpotifyAuthorizationSummary> DisconnectSpotifyAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyDisconnectCalls++;
        LastSpotifyIdentity = identity;
        SpotifyAuthorization = new(
            SpotifyConfiguration.IsConfigured
                ? SpotifyAuthorizationState.Disconnected
                : SpotifyAuthorizationState.Unconfigured,
            [], [], null);
        return Task.FromResult(SpotifyAuthorization);
    }

    public Task<SpotifyPlaybackSummary> GetSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyPlayback);
    }

    public Task ControlSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyPlaybackControlCalls++;
        LastSpotifyIdentity = identity;
        LastSpotifyPlaybackCommand = command;
        return Task.CompletedTask;
    }

    public Task<SpotifyDevicesSummary> GetSpotifyDevicesAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyDevices);
    }

    public Task TransferSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, TransferSpotifyPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyTransferCalls++;
        LastSpotifyIdentity = identity;
        LastSpotifyTransferRequest = request;
        return Task.CompletedTask;
    }

    public Task<SpotifyQueueSummary> GetSpotifyQueueAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyQueue);
    }

    public Task AddSpotifyQueueItemAsync(
        BrokerWidgetIdentity identity, AddSpotifyQueueItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyQueueAddCalls++;
        LastSpotifyIdentity = identity;
        LastSpotifyQueueAddRequest = request;
        return Task.CompletedTask;
    }

    public Task StartSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyPlaybackStartCalls++;
        LastSpotifyIdentity = identity;
        LastSpotifyStartRequest = request;
        return Task.CompletedTask;
    }

    public Task<SpotifyLocalPlaybackSummary> GetSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyLocalPlayback);
    }

    public Task<SpotifyLocalPlaybackSummary> ControlSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyLocalPlaybackControlCalls++;
        LastSpotifyIdentity = identity;
        SpotifyLocalPlayback = command.Operation switch
        {
            SpotifyLocalPlaybackOperation.StartAndTransfer => SpotifyLocalPlayback with
            {
                State = SpotifyLocalPlaybackState.Active,
                DisplayMessage = "Playing here",
            },
            SpotifyLocalPlaybackOperation.Stop => SpotifyLocalPlayback with
            {
                State = SpotifyLocalPlaybackState.Disabled,
                DisplayMessage = null,
            },
            SpotifyLocalPlaybackOperation.SetVolume => SpotifyLocalPlayback with
            {
                VolumePercent = command.VolumePercent,
            },
            _ => SpotifyLocalPlayback,
        };
        return Task.FromResult(SpotifyLocalPlayback);
    }

    public Task<SpotifyPlaylistPageSummary> GetSpotifyPlaylistsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyPlaylists);
    }

    public Task<SpotifyPlaylistItemsSummary> GetSpotifyPlaylistItemsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistItemsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSpotifyIdentity = identity;
        return Task.FromResult(SpotifyPlaylistItems ?? throw new BrokerException(
            "not_found", "Spotify playlist is unavailable."));
    }

    public Task<PrivateSecretMetadataSummary> GetPrivateSecretMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_privateSecrets.TryGetValue(SecretKey(identity, slot), out var value)
            ? new PrivateSecretMetadataSummary(true, value.WrittenAt)
            : new PrivateSecretMetadataSummary(false, null));
    }

    public Task SavePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrivateSecretSaveCalls++;
        _privateSecrets[SecretKey(identity, slot)] =
            (secret, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Task.CompletedTask;
    }

    public Task DeletePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrivateSecretDeleteCalls++;
        _privateSecrets.Remove(SecretKey(identity, slot));
        return Task.CompletedTask;
    }

    public Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_privateState.TryGetValue(StateKey(identity), out var state)
            ? new PrivateStateSnapshotSummary(
                state.JsonBase64 is not null, state.JsonBase64, state.Revision)
            : new PrivateStateSnapshotSummary(false, null, 0));
    }

    public Task<PrivateStateMutationSummary> WritePrivateStateAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken) =>
        MutatePrivateStateAsync(identity, request.ExpectedRevision,
            request.CanonicalJsonBase64, cancellationToken);

    public Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken) =>
        MutatePrivateStateAsync(identity, request.ExpectedRevision, null, cancellationToken);

    private Task<PrivateStateMutationSummary> MutatePrivateStateAsync(
        BrokerWidgetIdentity identity, long? expectedRevision, string? jsonBase64,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = StateKey(identity);
        var revision = _privateState.TryGetValue(key, out var current)
            ? current.Revision
            : 0;
        if (expectedRevision is { } expected && expected != revision)
            throw new BrokerException("state_conflict", "Private state changed before this update.");
        revision++;
        _privateState[key] = (jsonBase64, revision);
        return Task.FromResult(new PrivateStateMutationSummary(revision));
    }

    public Task<LoopbackJsonResponse> SendLoopbackJsonAsync(
        BrokerWidgetIdentity identity, int port, bool isPost,
        LoopbackJsonRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoopbackCalls++;
        LastLoopbackPort = port;
        LastLoopbackWasPost = isPost;
        LastLoopbackRequest = request;
        if (request.BearerSecretSlot is { } slot &&
            !_privateSecrets.ContainsKey(SecretKey(identity, slot)))
            throw new BrokerException("secret_not_found", "Private secret slot is empty.");
        return SendAsync();

        async Task<LoopbackJsonResponse> SendAsync()
        {
            var response = LoopbackHandler is null
                ? new LoopbackJsonResponse(200, "{}", [])
                : await LoopbackHandler(identity, port, isPost, request, cancellationToken)
                    .ConfigureAwait(false);
            if (response.StatusCode == 401 &&
                request is
                {
                    InvalidateBearerSecretOnUnauthorized: true,
                    BearerSecretSlot: { } rejectedSlot,
                })
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrivateSecretDeleteCalls++;
                _privateSecrets.Remove(SecretKey(identity, rejectedSlot));
            }
            return response;
        }
    }

    private static string SecretKey(BrokerWidgetIdentity identity, string slot) =>
        $"{identity.PublisherId}\0{identity.PackageId}\0{slot}";

    private static string StateKey(BrokerWidgetIdentity identity) =>
        $"{identity.PublisherId}\0{identity.PackageId}";
}
