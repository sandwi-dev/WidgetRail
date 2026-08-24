using WidgetRail.WindowsSpotifyProvider;

namespace WidgetRail.Samples.SpotifyWidget;

internal sealed class SpotifyApplicationService(
    WindowsSpotifyPlatformBackend backend,
    SpotifyIntegrationIdentity identity,
    ISpotifySetupActions? setupActions = null) :
    ISpotifyApplicationService,
    ISpotifyCorrelatedQueueService
{
    private readonly WindowsSpotifyPlatformBackend _backend = backend ??
        throw new ArgumentNullException(nameof(backend));
    private readonly SpotifyIntegrationIdentity _identity = identity;
    private readonly ISpotifySetupActions _setupActions = setupActions ??
        new SpotifyWindowsSetupActions();

    public ValueTask<SpotifyConfigurationSummary> ConfigureClientAsync(
        string clientId,
        CancellationToken cancellationToken = default) => new(
        _backend.ConfigureSpotifyClientAsync(
            _identity, new ConfigureSpotifyClientRequest(clientId), cancellationToken));

    public ValueTask OpenDeveloperDashboardAsync(
        CancellationToken cancellationToken = default) =>
        _setupActions.OpenDeveloperDashboardAsync(cancellationToken);

    public ValueTask CopyRedirectUriAsync(
        CancellationToken cancellationToken = default) =>
        _setupActions.CopyRedirectUriAsync(cancellationToken);

    public ValueTask<SpotifyConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyConfigurationAsync(_identity, cancellationToken));

    public ValueTask<SpotifyAuthorizationSummary> GetAuthorizationAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyAuthorizationAsync(_identity, cancellationToken));

    public ValueTask<SpotifyAuthorizationSummary> ConnectAsync(
        IReadOnlyCollection<SpotifyAuthorizationScope> requestedScopes,
        CancellationToken cancellationToken = default) => new(
        _backend.ConnectSpotifyAsync(_identity,
            new ConnectSpotifyRequest(requestedScopes.ToArray()), cancellationToken));

    public ValueTask<SpotifyAuthorizationSummary> DisconnectAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.DisconnectSpotifyAsync(_identity, cancellationToken));

    public ValueTask<SpotifyPlaybackSummary> GetPlaybackAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyPlaybackAsync(_identity, cancellationToken));

    public ValueTask ControlPlaybackAsync(
        SpotifyPlaybackCommand command,
        CancellationToken cancellationToken = default) => new(
        _backend.ControlSpotifyPlaybackAsync(_identity, command, cancellationToken));

    public ValueTask<SpotifyDevicesSummary> GetDevicesAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyDevicesAsync(_identity, cancellationToken));

    public ValueTask TransferPlaybackAsync(
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken = default) => new(
        _backend.TransferSpotifyPlaybackAsync(_identity,
            new TransferSpotifyPlaybackRequest(deviceId, continuePlaying), cancellationToken));

    public ValueTask<SpotifyQueueSummary> GetQueueAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyQueueAsync(_identity, cancellationToken));

    ValueTask<SpotifyQueueSummary> ISpotifyCorrelatedQueueService.GetQueueAsync(
        long operation,
        long generation,
        CancellationToken cancellationToken) => new(
        _backend.GetSpotifyQueueAsync(
            _identity, operation, generation, cancellationToken));

    public ValueTask AddToQueueAsync(
        string uri,
        string? deviceId = null,
        CancellationToken cancellationToken = default) => new(
        _backend.AddSpotifyQueueItemAsync(_identity,
            new AddSpotifyQueueItemRequest(uri, deviceId), cancellationToken));

    public ValueTask StartPlaybackAsync(
        StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken = default) => new(
        _backend.StartSpotifyPlaybackAsync(_identity, request, cancellationToken));

    public ValueTask<SpotifyLocalPlaybackSummary> GetLocalPlaybackAsync(
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyLocalPlaybackAsync(_identity, cancellationToken));

    public ValueTask<SpotifyLocalPlaybackSummary> ControlLocalPlaybackAsync(
        SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken = default) => new(
        _backend.ControlSpotifyLocalPlaybackAsync(_identity, command, cancellationToken));

    public ValueTask<SpotifyPlaylistPageSummary> GetPlaylistsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyPlaylistsAsync(_identity,
            new SpotifyPlaylistPageRequest(offset, limit), cancellationToken));

    public ValueTask<SpotifyPlaylistSummary> GetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyPlaylistAsync(_identity, playlistId, cancellationToken));

    public ValueTask<SpotifyPlaylistItemsPageSummary> GetPlaylistItemsAsync(
        string playlistId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) => new(
        _backend.GetSpotifyPlaylistItemsAsync(_identity,
            new SpotifyPlaylistItemsRequest(playlistId, offset, limit), cancellationToken));

    public ValueTask DisposeAsync() => _backend.DisposeAsync();
}
