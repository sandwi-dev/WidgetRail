using GameBarAlternative.PlatformSettings;

namespace GameBarAlternative.WindowsSpotifyProvider;

/// <summary>Adapts the host's generic package-scoped public settings store.</summary>
public sealed class WidgetConfigurationSpotifyClientStore(WidgetConfigurationStore store) :
    ISpotifyClientConfigurationStore
{
    public const string ClientIdKey = "client-id";
    private readonly WidgetConfigurationStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public async Task<SpotifyClientConfiguration?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        var snapshot = await _store.ReadAsync(
            identity.PackageId, identity.PublisherId, cancellationToken).ConfigureAwait(false);
        return snapshot.Values.TryGetValue(ClientIdKey, out var clientId)
            ? new SpotifyClientConfiguration(clientId) : null;
    }

    public async Task WriteAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyClientConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _store.SetAsync(identity.PackageId, identity.PublisherId,
            ClientIdKey, configuration.ClientId, cancellationToken).ConfigureAwait(false);
    }
}
