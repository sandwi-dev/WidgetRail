namespace GameBarAlternative.PlatformBroker;

/// <summary>Joins independently owned host providers without exposing either one to widgets.</summary>
public sealed class CompositePlatformBrokerBackend : IPlatformBrokerBackend, IAsyncDisposable
{
    private readonly IAudioPlatformBrokerBackend _audio;
    private readonly INetworkPlatformBrokerBackend _network;
    private readonly IActivityPlatformBrokerBackend _activity;
    private readonly IBluetoothPlatformBrokerBackend _bluetooth;
    private int _disposed;

    public CompositePlatformBrokerBackend(
        IAudioPlatformBrokerBackend audio,
        INetworkPlatformBrokerBackend network,
        IActivityPlatformBrokerBackend? activity = null,
        IBluetoothPlatformBrokerBackend? bluetooth = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _activity = activity ?? UnavailableActivityPlatformBrokerBackend.Instance;
        _bluetooth = bluetooth ?? UnavailableBluetoothPlatformBrokerBackend.Instance;
        _audio.EventPublished += ForwardAudioEvent;
        _network.EventPublished += ForwardNetworkEvent;
        _activity.EventPublished += ForwardActivityEvent;
        _bluetooth.EventPublished += ForwardBluetoothEvent;
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

    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        _network.GetWifiRadioAsync(cancellationToken);

    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        _network.SetWifiRadioAsync(enabled, cancellationToken);

    public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken) =>
        _bluetooth.GetBluetoothAsync(cancellationToken);

    public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        _bluetooth.SetBluetoothRadioAsync(enabled, cancellationToken);

    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken) =>
        _activity.GetRecentActivitiesAsync(cancellationToken);

    public Task ActivateRecentActivityAsync(
        string activityId, CancellationToken cancellationToken) =>
        _activity.ActivateRecentActivityAsync(activityId, cancellationToken);

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _audio.EventPublished -= ForwardAudioEvent;
        _network.EventPublished -= ForwardNetworkEvent;
        _activity.EventPublished -= ForwardActivityEvent;
        _bluetooth.EventPublished -= ForwardBluetoothEvent;
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
    }

    private sealed class UnavailableActivityPlatformBrokerBackend : IActivityPlatformBrokerBackend
    {
        internal static UnavailableActivityPlatformBrokerBackend Instance { get; } = new();
        public event EventHandler<BrokerPlatformEvent>? EventPublished { add { } remove { } }

        public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<RecentActivitySummary>>(
                new BrokerException("platform_unavailable", "Recent activity is unavailable."));

        public Task ActivateRecentActivityAsync(
            string activityId, CancellationToken cancellationToken) =>
            Task.FromException(
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
    }
}
