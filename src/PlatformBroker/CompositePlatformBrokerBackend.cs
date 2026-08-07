namespace GameBarAlternative.PlatformBroker;

/// <summary>Joins independently owned host providers without exposing either one to widgets.</summary>
public sealed class CompositePlatformBrokerBackend : IPlatformBrokerBackend, IAsyncDisposable
{
    private readonly IAudioPlatformBrokerBackend _audio;
    private readonly INetworkPlatformBrokerBackend _network;
    private int _disposed;

    public CompositePlatformBrokerBackend(
        IAudioPlatformBrokerBackend audio,
        INetworkPlatformBrokerBackend network)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _audio.EventPublished += ForwardAudioEvent;
        _network.EventPublished += ForwardNetworkEvent;
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

    private void ForwardAudioEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId is PlatformCapabilities.AudioSessionsReadV1 or
            PlatformCapabilities.AudioOutputReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    private void ForwardNetworkEvent(object? sender, BrokerPlatformEvent platformEvent)
    {
        if (platformEvent.CapabilityId is PlatformCapabilities.NetworkReadV1 or
            PlatformCapabilities.NetworkWifiReadV1)
            EventPublished?.Invoke(this, platformEvent);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _audio.EventPublished -= ForwardAudioEvent;
        _network.EventPublished -= ForwardNetworkEvent;
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
    }
}
