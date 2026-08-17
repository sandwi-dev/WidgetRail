namespace WidgetRail.PlatformBroker;

/// <summary>Joins independently owned host providers without exposing either one to widgets.</summary>
public sealed class CompositePlatformBrokerBackend : IPlatformBrokerBackend,
    IProtectedWifiHostBackend, IAsyncDisposable
{
    private readonly IAudioPlatformBrokerBackend _audio;
    private readonly INetworkPlatformBrokerBackend _network;
    private readonly IActivityPlatformBrokerBackend _activity;
    private readonly IAppLibraryPlatformBrokerBackend _appLibrary;
    private readonly IBluetoothPlatformBrokerBackend _bluetooth;
    private readonly IMediaPlatformBrokerBackend _media;
    private readonly IPrivateSecretPlatformBrokerBackend _privateSecrets;
    private readonly ILoopbackHttpPlatformBrokerBackend _loopbackHttp;
    private readonly IPrivateStatePlatformBrokerBackend _privateState;
    private int _disposed;

    public CompositePlatformBrokerBackend(
        IAudioPlatformBrokerBackend audio,
        INetworkPlatformBrokerBackend network,
        IActivityPlatformBrokerBackend? activity = null,
        IBluetoothPlatformBrokerBackend? bluetooth = null,
        IMediaPlatformBrokerBackend? media = null,
        IAppLibraryPlatformBrokerBackend? appLibrary = null,
        IPrivateSecretPlatformBrokerBackend? privateSecrets = null,
        ILoopbackHttpPlatformBrokerBackend? loopbackHttp = null,
        IPrivateStatePlatformBrokerBackend? privateState = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _activity = activity ?? UnavailableActivityPlatformBrokerBackend.Instance;
        _appLibrary = appLibrary ?? UnavailableAppLibraryPlatformBrokerBackend.Instance;
        _bluetooth = bluetooth ?? UnavailableBluetoothPlatformBrokerBackend.Instance;
        _media = media ?? UnavailableMediaPlatformBrokerBackend.Instance;
        _privateSecrets = privateSecrets ?? UnavailablePrivateSecretPlatformBrokerBackend.Instance;
        _loopbackHttp = loopbackHttp ?? UnavailableLoopbackHttpPlatformBrokerBackend.Instance;
        _privateState = privateState ?? UnavailablePrivateStatePlatformBrokerBackend.Instance;
        _audio.EventPublished += ForwardAudioEvent;
        _network.EventPublished += ForwardNetworkEvent;
        _activity.EventPublished += ForwardActivityEvent;
        _bluetooth.EventPublished += ForwardBluetoothEvent;
        _media.EventPublished += ForwardMediaEvent;
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken) => _audio.GetAudioSessionsAsync(cancellationToken);

    public Task SetAudioSessionVolumeAsync(
        string sessionId, double volume, CancellationToken cancellationToken) =>
        _audio.SetAudioSessionVolumeAsync(sessionId, volume, cancellationToken);

    public Task SetAudioSessionMutedAsync(
        string sessionId, bool isMuted, CancellationToken cancellationToken) =>
        _audio.SetAudioSessionMutedAsync(sessionId, isMuted, cancellationToken);

    public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken) =>
        _audio.GetAudioOutputAsync(cancellationToken);

    public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        _audio.SetAudioOutputVolumeAsync(volume, cancellationToken);

    public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        _audio.SetAudioOutputMutedAsync(isMuted, cancellationToken);

    public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(
        CancellationToken cancellationToken) => _audio.GetAudioDevicesAsync(cancellationToken);

    public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken) =>
        _audio.GetAudioInputAsync(cancellationToken);

    public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        _audio.SetAudioInputVolumeAsync(volume, cancellationToken);

    public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        _audio.SetAudioInputMutedAsync(isMuted, cancellationToken);

    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        _network.GetNetworkStatusAsync(cancellationToken);

    public Task<NetworkConnectionDetailsSummary> GetNetworkConnectionDetailsAsync(
        CancellationToken cancellationToken) =>
        _network.GetNetworkConnectionDetailsAsync(cancellationToken);

    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(
        CancellationToken cancellationToken) => _network.GetSavedNetworkProfilesAsync(cancellationToken);

    public Task SwitchSavedNetworkProfileAsync(
        string profileId, CancellationToken cancellationToken) =>
        _network.SwitchSavedNetworkProfileAsync(profileId, cancellationToken);

    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(
        CancellationToken cancellationToken) =>
        _network.GetAvailableWifiNetworksAsync(cancellationToken);

    public Task RequestWifiScanAsync(CancellationToken cancellationToken) =>
        _network.RequestWifiScanAsync(cancellationToken);

    public Task ConnectAvailableWifiNetworkAsync(
        string networkId, CancellationToken cancellationToken) =>
        _network.ConnectAvailableWifiNetworkAsync(networkId, cancellationToken);

    public Task<ProtectedWifiConnectionResult> ConnectProtectedWifiAsync(
        string networkId,
        char[] secret,
        CancellationToken cancellationToken) =>
        _network is IProtectedWifiHostBackend protectedWifi
            ? protectedWifi.ConnectProtectedWifiAsync(networkId, secret, cancellationToken)
            : Task.FromException<ProtectedWifiConnectionResult>(new BrokerException(
                "platform_unavailable", "Protected Wi-Fi connection is unavailable."));

    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        _network.GetWifiRadioAsync(cancellationToken);

    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        _network.SetWifiRadioAsync(enabled, cancellationToken);

    public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken) =>
        _bluetooth.GetBluetoothAsync(cancellationToken);

    public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        _bluetooth.SetBluetoothRadioAsync(enabled, cancellationToken);

    public Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken) =>
        _bluetooth.PairBluetoothDeviceAsync(deviceId, cancellationToken);

    public Task<BluetoothUnpairingResultSummary> UnpairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken) =>
        _bluetooth.UnpairBluetoothDeviceAsync(deviceId, cancellationToken);

    public Task OpenBluetoothDeviceSettingsAsync(
        string deviceId, CancellationToken cancellationToken) =>
        _bluetooth.OpenBluetoothDeviceSettingsAsync(deviceId, cancellationToken);

    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken) =>
        _activity.GetRecentActivitiesAsync(cancellationToken);

    public Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
        AppLibraryBackendCursorRequest request,
        CancellationToken cancellationToken) =>
        _appLibrary.QueryAppLibraryAsync(request, cancellationToken);

    Task<AppLibraryIconSummary> IAppLibraryPlatformBrokerBackend.GetAppLibraryIconAsync(
        string appId,
        CancellationToken cancellationToken) =>
        _appLibrary.GetAppLibraryIconAsync(appId, cancellationToken);

    Task<RunningAppBackendObservationPage>
        IAppLibraryPlatformBrokerBackend.ObserveRunningAppsAsync(
            CancellationToken cancellationToken) =>
            _appLibrary.ObserveRunningAppsAsync(cancellationToken);

    public Task LaunchAppLibraryItemAsync(
        string appId, CancellationToken cancellationToken) =>
        _appLibrary.LaunchAppLibraryItemAsync(appId, cancellationToken);

    public Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        string appId, CancellationToken cancellationToken) =>
        _appLibrary.LaunchAppLibraryItemObservedAsync(appId, cancellationToken);

    public Task<IReadOnlyList<MediaSessionSummary>> GetMediaSessionsAsync(
        CancellationToken cancellationToken) =>
        _media.GetMediaSessionsAsync(cancellationToken);

    public Task ControlMediaSessionAsync(
        string sessionId,
        MediaSessionCommand command,
        CancellationToken cancellationToken) =>
        _media.ControlMediaSessionAsync(sessionId, command, cancellationToken);

    public Task<PrivateSecretMetadataSummary> GetPrivateSecretMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        _privateSecrets.GetPrivateSecretMetadataAsync(identity, slot, cancellationToken);

    public Task SavePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken) =>
        _privateSecrets.SavePrivateSecretAsync(identity, slot, secret, cancellationToken);

    public Task DeletePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        _privateSecrets.DeletePrivateSecretAsync(identity, slot, cancellationToken);

    public Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        _privateState.ReadPrivateStateAsync(identity, cancellationToken);

    public Task<PrivateStateMutationSummary> WritePrivateStateAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken) =>
        _privateState.WritePrivateStateAsync(identity, request, cancellationToken);

    public Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken) =>
        _privateState.ClearPrivateStateAsync(identity, request, cancellationToken);

    public Task<LoopbackJsonResponse> SendLoopbackJsonAsync(
        BrokerWidgetIdentity identity, int port, bool isPost,
        LoopbackJsonRequest request, CancellationToken cancellationToken) =>
        _loopbackHttp.SendLoopbackJsonAsync(
            identity, port, isPost, request, cancellationToken);

    private void ForwardAudioEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId is PlatformCapabilities.AudioSessionsReadV1 or
            PlatformCapabilities.AudioOutputReadV1 or
            PlatformCapabilities.AudioDevicesReadV1 or
            PlatformCapabilities.AudioInputReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    private void ForwardNetworkEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId is PlatformCapabilities.NetworkReadV1 or
            PlatformCapabilities.NetworkWifiReadV1 or
            PlatformCapabilities.NetworkWifiRadioReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    private void ForwardActivityEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId == PlatformCapabilities.RecentActivityReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    private void ForwardBluetoothEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId == PlatformCapabilities.NetworkBluetoothReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    private void ForwardMediaEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId == PlatformCapabilities.MediaSessionsReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _audio.EventPublished -= ForwardAudioEvent;
        _network.EventPublished -= ForwardNetworkEvent;
        _activity.EventPublished -= ForwardActivityEvent;
        _bluetooth.EventPublished -= ForwardBluetoothEvent;
        _media.EventPublished -= ForwardMediaEvent;
        if (_audio is IAsyncDisposable asyncAudio)
            await asyncAudio.DisposeAsync().ConfigureAwait(false);
        else if (_audio is IDisposable audio)
            audio.Dispose();
        if (!ReferenceEquals(_audio, _network))
        {
            if (_network is IAsyncDisposable asyncNetwork)
                await asyncNetwork.DisposeAsync().ConfigureAwait(false);
            else if (_network is IDisposable network)
                network.Dispose();
        }
        if (!ReferenceEquals(_activity, _audio) && !ReferenceEquals(_activity, _network))
        {
            if (_activity is IAsyncDisposable asyncActivity)
                await asyncActivity.DisposeAsync().ConfigureAwait(false);
            else if (_activity is IDisposable activity)
                activity.Dispose();
        }
        if (!ReferenceEquals(_bluetooth, _audio) && !ReferenceEquals(_bluetooth, _network) &&
            !ReferenceEquals(_bluetooth, _activity))
        {
            if (_bluetooth is IAsyncDisposable asyncBluetooth)
                await asyncBluetooth.DisposeAsync().ConfigureAwait(false);
            else if (_bluetooth is IDisposable bluetooth)
                bluetooth.Dispose();
        }
        if (!ReferenceEquals(_media, _audio) && !ReferenceEquals(_media, _network) &&
            !ReferenceEquals(_media, _activity) && !ReferenceEquals(_media, _bluetooth))
        {
            if (_media is IAsyncDisposable asyncMedia)
                await asyncMedia.DisposeAsync().ConfigureAwait(false);
            else if (_media is IDisposable media)
                media.Dispose();
        }
        if (!ReferenceEquals(_appLibrary, _audio) && !ReferenceEquals(_appLibrary, _network) &&
            !ReferenceEquals(_appLibrary, _activity) && !ReferenceEquals(_appLibrary, _bluetooth) &&
            !ReferenceEquals(_appLibrary, _media))
        {
            if (_appLibrary is IAsyncDisposable asyncAppLibrary)
                await asyncAppLibrary.DisposeAsync().ConfigureAwait(false);
            else if (_appLibrary is IDisposable appLibrary)
                appLibrary.Dispose();
        }
        if (!ReferenceEquals(_privateSecrets, _audio) &&
            !ReferenceEquals(_privateSecrets, _network) &&
            !ReferenceEquals(_privateSecrets, _activity) &&
            !ReferenceEquals(_privateSecrets, _bluetooth) &&
            !ReferenceEquals(_privateSecrets, _media) &&
            !ReferenceEquals(_privateSecrets, _appLibrary))
        {
            if (_privateSecrets is IAsyncDisposable asyncPrivateSecrets)
                await asyncPrivateSecrets.DisposeAsync().ConfigureAwait(false);
            else if (_privateSecrets is IDisposable privateSecrets)
                privateSecrets.Dispose();
        }
        if (!ReferenceEquals(_loopbackHttp, _audio) &&
            !ReferenceEquals(_loopbackHttp, _network) &&
            !ReferenceEquals(_loopbackHttp, _activity) &&
            !ReferenceEquals(_loopbackHttp, _bluetooth) &&
            !ReferenceEquals(_loopbackHttp, _media) &&
            !ReferenceEquals(_loopbackHttp, _appLibrary) &&
            !ReferenceEquals(_loopbackHttp, _privateSecrets))
        {
            if (_loopbackHttp is IAsyncDisposable asyncLoopback)
                await asyncLoopback.DisposeAsync().ConfigureAwait(false);
            else if (_loopbackHttp is IDisposable loopback)
                loopback.Dispose();
        }
        if (!ReferenceEquals(_privateState, _audio) &&
            !ReferenceEquals(_privateState, _network) &&
            !ReferenceEquals(_privateState, _activity) &&
            !ReferenceEquals(_privateState, _bluetooth) &&
            !ReferenceEquals(_privateState, _media) &&
            !ReferenceEquals(_privateState, _appLibrary) &&
            !ReferenceEquals(_privateState, _privateSecrets) &&
            !ReferenceEquals(_privateState, _loopbackHttp))
        {
            if (_privateState is IAsyncDisposable asyncPrivateState)
                await asyncPrivateState.DisposeAsync().ConfigureAwait(false);
            else if (_privateState is IDisposable privateState)
                privateState.Dispose();
        }
    }

    private sealed class UnavailableActivityPlatformBrokerBackend : IActivityPlatformBrokerBackend
    {
        internal static UnavailableActivityPlatformBrokerBackend Instance { get; } = new();
        public event EventHandler<BrokerPlatformEvent>? EventPublished { add { } remove { } }

        public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<RecentActivitySummary>>(
                new BrokerException("platform_unavailable", "Recent activity is unavailable."));

    }

    private sealed class UnavailableBluetoothPlatformBrokerBackend : IBluetoothPlatformBrokerBackend
    {
        internal static UnavailableBluetoothPlatformBrokerBackend Instance { get; } = new();
        public event EventHandler<BrokerPlatformEvent>? EventPublished { add { } remove { } }

        public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken) =>
            Task.FromException<BluetoothSummary>(
                new BrokerException("platform_unavailable", "Bluetooth is unavailable."));

        public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken) =>
            Task.FromException(
                new BrokerException("platform_unavailable", "Bluetooth radio control is unavailable."));

        public Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
            string deviceId, CancellationToken cancellationToken) =>
            Task.FromException<BluetoothPairingResultSummary>(
                new BrokerException("platform_unavailable", "Bluetooth pairing is unavailable."));

        public Task<BluetoothUnpairingResultSummary> UnpairBluetoothDeviceAsync(
            string deviceId, CancellationToken cancellationToken) =>
            Task.FromException<BluetoothUnpairingResultSummary>(
                new BrokerException("platform_unavailable", "Bluetooth removal is unavailable."));

        public Task OpenBluetoothDeviceSettingsAsync(
            string deviceId, CancellationToken cancellationToken) =>
            Task.FromException(
                new BrokerException(
                    "platform_unavailable", "Bluetooth device management is unavailable."));
    }

    private sealed class UnavailableAppLibraryPlatformBrokerBackend : IAppLibraryPlatformBrokerBackend
    {
        internal static UnavailableAppLibraryPlatformBrokerBackend Instance { get; } = new();

        public Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
            AppLibraryBackendCursorRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<AppLibraryBackendCursorPage>(
                new BrokerException("platform_unavailable", "App library is unavailable."));

        public Task LaunchAppLibraryItemAsync(
            string appId, CancellationToken cancellationToken) =>
            Task.FromException(
                new BrokerException("platform_unavailable", "App launch is unavailable."));
    }

    private sealed class UnavailableMediaPlatformBrokerBackend : IMediaPlatformBrokerBackend
    {
        internal static UnavailableMediaPlatformBrokerBackend Instance { get; } = new();
        public event EventHandler<BrokerPlatformEvent>? EventPublished { add { } remove { } }

        public Task<IReadOnlyList<MediaSessionSummary>> GetMediaSessionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<MediaSessionSummary>>(
                new BrokerException("platform_unavailable", "Windows media sessions are unavailable."));

        public Task ControlMediaSessionAsync(
            string sessionId,
            MediaSessionCommand command,
            CancellationToken cancellationToken) =>
            Task.FromException(
                new BrokerException("platform_unavailable", "Windows media session control is unavailable."));
    }

    private sealed class UnavailablePrivateSecretPlatformBrokerBackend :
        IPrivateSecretPlatformBrokerBackend
    {
        internal static UnavailablePrivateSecretPlatformBrokerBackend Instance { get; } = new();
    }

    private sealed class UnavailableLoopbackHttpPlatformBrokerBackend :
        ILoopbackHttpPlatformBrokerBackend
    {
        internal static UnavailableLoopbackHttpPlatformBrokerBackend Instance { get; } = new();
    }

    private sealed class UnavailablePrivateStatePlatformBrokerBackend :
        IPrivateStatePlatformBrokerBackend
    {
        internal static UnavailablePrivateStatePlatformBrokerBackend Instance { get; } = new();
    }
}
