namespace WidgetRail.PlatformBroker;

/// <summary>Deterministic seam for tests and development; owns no OS resource.</summary>
public sealed class SimulatedPlatformBrokerBackend : IPlatformBrokerBackend
{
    private readonly List<AudioSessionSummary> _audioSessions = [];
    private readonly List<AudioDeviceSummary> _audioDevices = [];
    private readonly List<SavedNetworkProfileSummary> _networkProfiles = [];
    private readonly List<AvailableWifiNetworkSummary> _availableWifiNetworks = [];
    private readonly List<RecentActivitySummary> _recentActivities = [];
    private readonly List<AppLibraryBackendItemSummary> _appLibrary = [];
    private readonly List<RunningAppBackendObservation> _runningApps = [];
    private readonly Dictionary<string, Dictionary<string, AppLibraryBackendItemSummary>>
        _registeredRunningApps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _registeredRunningAppRevisions =
        new(StringComparer.Ordinal);
    private long _runningAppsRevision = 1;
    private long _appLibraryRevision = 1;
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
    public NetworkConnectionDetailsSummary NetworkConnectionDetails { get; set; } = new(
        0, NetworkConnectionDetailsState.Unavailable,
        NetworkConnectionDetailsConnectivity.None, NetworkTransportKind.None, [], [], []);

    public int AudioControlCalls { get; private set; }
    public int NetworkSwitchCalls { get; private set; }
    public int NetworkConnectionDetailsReadCalls { get; private set; }
    public int WifiScanCalls { get; private set; }
    public int WifiConnectCalls { get; private set; }
    public int WifiRadioControlCalls { get; private set; }
    public int BluetoothRadioControlCalls { get; private set; }
    public int BluetoothPairCalls { get; private set; }
    public int BluetoothManageCalls { get; private set; }
    public int MediaControlCalls { get; private set; }
    public int AppLibraryLaunchCalls { get; private set; }
    public Func<string, CancellationToken, Task<AppLibraryLaunchObservationSummary>>?
        AppLibraryObservedLaunchHandler { get; set; }
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
    internal Func<CancellationToken, Task<IReadOnlyList<MediaSessionSummary>>>?
        MediaSessionsHandler { get; set; }
    internal IReadOnlyList<AppLibrarySourceSummary> AppLibrarySources { get; set; } = [];
    public string? LastLaunchedAppId { get; private set; }
    public string? LastControlledMediaSessionId { get; private set; }
    public MediaSessionCommand? LastMediaCommand { get; private set; }
    public WifiRadioSummary WifiRadio { get; set; } = new(WifiRadioState.On, true);
    public BluetoothRadioState BluetoothRadioState { get; set; } = BluetoothRadioState.On;
    public bool CanControlBluetoothRadio { get; set; } = true;
    public BluetoothPairingResultStatus BluetoothPairingResult { get; set; } =
        BluetoothPairingResultStatus.Paired;
    public BluetoothUnpairingResultStatus BluetoothUnpairingResult { get; set; } =
        BluetoothUnpairingResultStatus.Unpaired;
    public string? LastBluetoothDeviceId { get; private set; }
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

    public void SetAppLibrary(IEnumerable<AppLibraryItemSummary> items)
    {
        var normalized = items.ToArray();
        _appLibrary.Clear();
        _appLibrary.AddRange(normalized.Select(item => new AppLibraryBackendItemSummary(
            item.AppId, item.SavedId, item.Presentation.DisplayName,
            item.Presentation.Kind,
            item.Presentation.Artwork.Items.FirstOrDefault()?.Revision ?? string.Empty,
            item.Presentation.Source.DisplayName)
        {
            SourceIdentity = item.Presentation.Source.SourceId,
            IsLaunchable = item.Presentation.Availability.IsLaunchable,
            AvailabilityState = item.Presentation.Availability.State,
            AvailabilityStatusCode = item.Presentation.Availability.StatusCode,
            SupportedActions = item.Presentation.Capabilities.Actions.ToArray(),
        }));
        AppLibrarySources = normalized
            .Select(item => item.Presentation.Source)
            .DistinctBy(source => source.SourceId, StringComparer.Ordinal)
            .Select(source => new AppLibrarySourceSummary(
                source.SourceId, source.DisplayName,
                AppLibrarySourceHealth.Healthy, 0, "healthy"))
            .ToArray();
        _appLibraryRevision++;
    }

    public void SetAppLibraryBackend(IEnumerable<AppLibraryBackendItemSummary> items)
    {
        _appLibrary.Clear();
        _appLibrary.AddRange(items);
        _appLibraryRevision++;
    }

    internal void SetRunningAppBackend(IEnumerable<RunningAppBackendObservation> items)
    {
        _runningApps.Clear();
        _runningApps.AddRange(items);
        _runningAppsRevision++;
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

    public Task SetDefaultAudioOutputDeviceAsync(string deviceId, CancellationToken cancellationToken) =>
        SetDefaultAudioDeviceAsync(deviceId, AudioDeviceDirection.Output, cancellationToken);
    public Task SetDefaultAudioInputDeviceAsync(string deviceId, CancellationToken cancellationToken) =>
        SetDefaultAudioDeviceAsync(deviceId, AudioDeviceDirection.Input, cancellationToken);
    private Task SetDefaultAudioDeviceAsync(string deviceId, AudioDeviceDirection direction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_audioDevices.Any(device => device.DeviceId == deviceId && device.Direction == direction))
            throw new BrokerException("resource_not_found", "The audio device is no longer available.");
        AudioControlCalls++;
        for (var index = 0; index < _audioDevices.Count; index++)
            if (_audioDevices[index].Direction == direction)
                _audioDevices[index] = _audioDevices[index] with { IsDefault = _audioDevices[index].DeviceId == deviceId };
        return Task.CompletedTask;
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

    public Task<NetworkConnectionDetailsSummary> GetNetworkConnectionDetailsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetworkConnectionDetailsReadCalls++;
        return Task.FromResult(NetworkConnectionDetails);
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

    public Task<BluetoothUnpairingResultSummary> UnpairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastBluetoothDeviceId = deviceId;
        if (BluetoothUnpairingResult is BluetoothUnpairingResultStatus.Unpaired or
            BluetoothUnpairingResultStatus.AlreadyUnpaired)
            _bluetoothDevices.RemoveAll(device =>
                string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
        return Task.FromResult(new BluetoothUnpairingResultSummary(BluetoothUnpairingResult));
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

    public Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
        AppLibraryBackendCursorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Refresh)
        {
            AppLibraryRefreshCalls++;
            _appLibraryRevision++;
        }
        else AppLibraryReadCalls++;
        var filtered = _appLibrary.Where(item =>
                request.Query.Kind is null || item.Kind == request.Query.Kind)
            .Where(item => request.Query.SourceAttribution is null ||
                item.SourceAttribution == request.Query.SourceAttribution)
            .Where(item => request.Query.SearchText is null ||
                item.DisplayName.Contains(
                    request.Query.SearchText, StringComparison.OrdinalIgnoreCase))
            .Where(item => request.Query.StableIdentityFilter is null ||
                request.Query.StableIdentityFilter.Contains(
                    item.StableProviderIdentity, StringComparer.Ordinal));
        var ordered = request.Query.Sort switch
        {
            AppLibrarySortOrder.DisplayNameDescending => filtered
                .OrderByDescending(item => item.DisplayName,
                    StringComparer.OrdinalIgnoreCase),
            AppLibrarySortOrder.SourceThenDisplayName => filtered
                .OrderBy(item => item.SourceAttribution,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(item => item.DisplayName,
                StringComparer.OrdinalIgnoreCase),
        };
        var orderedItems = ordered
            .ThenBy(item => item.StableProviderIdentity, StringComparer.Ordinal)
            .ToArray();
        var offset = 0;
        if (request.Cursor is not null)
        {
            var parts = request.Cursor.Split('.');
            if (parts.Length != 4 || parts[0] != "sim" ||
                parts[1] != _appLibraryRevision.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ||
                parts[2] != (request.Direction == AppLibraryCursorDirection.Before ? "B" : "A") ||
                !int.TryParse(parts[3], out offset))
                throw new BrokerException("invalid_cursor", "The app-library cursor is stale.");
        }
        var items = orderedItems.Skip(offset).Take(request.Limit).ToArray();
        var before = offset > 0
            ? $"sim.{_appLibraryRevision}.B.{Math.Max(0, offset - request.Limit)}"
            : null;
        var next = offset + items.Length;
        var after = next < orderedItems.Length
            ? $"sim.{_appLibraryRevision}.A.{next}"
            : null;
        return Task.FromResult(new AppLibraryBackendCursorPage(
            items, before, after, $"sim-revision-{_appLibraryRevision}")
        {
            Sources = AppLibrarySources,
        });
    }

    public Task<RunningAppBackendObservationPage> ObserveRunningAppsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RunningAppBackendObservationPage(
            _runningApps.ToArray(), $"sim-running-{_runningAppsRevision}"));
    }

    public Task<RegisterRunningAppBackendSummary> RegisterRunningAppAsync(
        BrokerWidgetIdentity identity, RegisterRunningAppBackendRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        identity.Validate();
        var observation = _runningApps.SingleOrDefault(item =>
            string.Equals(item.StableProviderIdentity,
                request.StableProviderIdentity, StringComparison.Ordinal) &&
            string.Equals(item.InstanceEvidence,
                request.InstanceEvidence, StringComparison.Ordinal));
        if (observation is null || !string.Equals(
                request.ObservationRevision,
                $"sim-running-{_runningAppsRevision}", StringComparison.Ordinal))
            throw new BrokerException(
                "stale_observation", "The running-app observation is stale.");
        var installed = _appLibrary.SingleOrDefault(item => string.Equals(
            item.StableProviderIdentity, observation.StableProviderIdentity,
            StringComparison.Ordinal));
        if (installed is not null)
            return Task.FromResult(new RegisterRunningAppBackendSummary(
                installed, AlreadyRegistered: true));
        var key = RegistrationAuthorityKey(identity);
        if (!_registeredRunningApps.TryGetValue(key, out var registrations))
            _registeredRunningApps[key] = registrations =
                new Dictionary<string, AppLibraryBackendItemSummary>(StringComparer.Ordinal);
        var alreadyRegistered = registrations.TryGetValue(request.SavedId, out var item);
        item ??= new AppLibraryBackendItemSummary(
            $"portable-{Guid.NewGuid():N}", observation.StableProviderIdentity,
            observation.DisplayName, observation.Kind, string.Empty,
            observation.SourceAttribution)
        {
            SourceIdentity = "source-portable",
            IsLaunchable = true,
            AvailabilityState = AppLibraryAvailabilityState.Installed,
            AvailabilityStatusCode = "registered_portable",
            SupportedActions = [AppLibraryAction.Launch],
        };
        registrations[request.SavedId] = item;
        if (!alreadyRegistered)
            _registeredRunningAppRevisions[key] =
                _registeredRunningAppRevisions.GetValueOrDefault(key) + 1;
        return Task.FromResult(new RegisterRunningAppBackendSummary(
            item, alreadyRegistered));
    }

    public Task<IReadOnlyList<AppLibraryBackendItemSummary>>
        ResolveRegisteredRunningAppsAsync(
            BrokerWidgetIdentity identity, IReadOnlyList<string> savedIds,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistrationAuthorityKey(identity);
        if (!_registeredRunningApps.TryGetValue(key, out var registrations))
            return Task.FromResult<IReadOnlyList<AppLibraryBackendItemSummary>>([]);
        return Task.FromResult<IReadOnlyList<AppLibraryBackendItemSummary>>(
            savedIds.Where(registrations.ContainsKey)
                .Select(savedId => registrations[savedId]).ToArray());
    }

    public Task ForgetRunningAppAsync(
        BrokerWidgetIdentity identity, string savedId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistrationAuthorityKey(identity);
        if (_registeredRunningApps.TryGetValue(key, out var registrations) &&
            registrations.Remove(savedId))
            _registeredRunningAppRevisions[key] =
                _registeredRunningAppRevisions.GetValueOrDefault(key) + 1;
        return Task.CompletedTask;
    }

    public Task<AppLibraryRegistrationStateSummary> GetRunningAppRegistrationStateAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistrationAuthorityKey(identity);
        return Task.FromResult(new AppLibraryRegistrationStateSummary(
            _registeredRunningApps.TryGetValue(key, out var registrations) &&
                registrations.Count != 0,
            _registeredRunningAppRevisions.GetValueOrDefault(key)));
    }

    public Task ClearRunningAppRegistrationsAsync(
        BrokerWidgetIdentity identity, long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistrationAuthorityKey(identity);
        var revision = _registeredRunningAppRevisions.GetValueOrDefault(key);
        if (revision != expectedRevision)
            throw new BrokerException(
                "app_registration_conflict", "Running-app registrations changed.");
        _registeredRunningApps.Remove(key);
        _registeredRunningAppRevisions[key] = revision + 1;
        return Task.CompletedTask;
    }

    public Task<AppLibraryPackageRegistrationRetirementSummary>
        RetireRunningAppPackageRegistrationsAsync(
            string packageId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var suffix = "\0" + packageId;
        foreach (var key in _registeredRunningApps.Keys
                     .Where(key => key.EndsWith(
                         suffix, StringComparison.Ordinal))
                     .ToArray())
        {
            _registeredRunningApps.Remove(key);
            _registeredRunningAppRevisions.Remove(key);
        }
        return Task.FromResult(
            new AppLibraryPackageRegistrationRetirementSummary(
                Committed: true, CleanupPending: false));
    }

    private static string RegistrationAuthorityKey(BrokerWidgetIdentity identity) =>
        identity.PublisherId + "\0" + identity.PackageId;

    public Task LaunchAppLibraryItemAsync(
        BrokerWidgetIdentity identity, string appId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryLaunchCalls++;
        LastLaunchedAppId = appId;
        return Task.CompletedTask;
    }

    public Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        BrokerWidgetIdentity identity, string appId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLibraryLaunchCalls++;
        LastLaunchedAppId = appId;
        return AppLibraryObservedLaunchHandler?.Invoke(appId, cancellationToken) ??
            Task.FromResult(new AppLibraryLaunchObservationSummary(
                AppLibraryLaunchObservationState.RequestAccepted, false, false));
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
        return MediaSessionsHandler?.Invoke(cancellationToken) ??
            Task.FromResult<IReadOnlyList<MediaSessionSummary>>(_mediaSessions.ToArray());
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
