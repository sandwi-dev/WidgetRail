namespace WidgetRail.Samples.SpotifyWidget;

internal sealed record SpotifySelectedPlaylistPage(
    SpotifyPlaylistSummary Playlist,
    SpotifyPlaylistItemsPageSummary Items);

/// <summary>
/// One selected-playlist generation owns one validated metadata value and its
/// adjacent items requests. Metadata is loaded once after the first successful
/// demand; a failed or cancelled load remains retryable in the same generation.
/// </summary>
internal sealed class SpotifySelectedPlaylistPageSource(
    ISpotifyApplicationService spotify,
    SpotifyPlaylistSelectionKey selectionKey,
    SpotifyPlaylistCache? cache = null)
{
    private readonly ISpotifyApplicationService _spotify = spotify ??
        throw new ArgumentNullException(nameof(spotify));
    private readonly SpotifyPlaylistSelectionKey _selectionKey = selectionKey;
    private readonly SemaphoreSlim _metadataGate = new(1, 1);
    private SpotifyPlaylistSummary? _metadata;
    private readonly SpotifyPlaylistCache _cache = cache ?? new();
    private SpotifyPlaylistCache.Entry? _cacheEntry;

    internal async ValueTask<SpotifySelectedPlaylistPage> LoadAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var playlist = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (await _cache.GetAsync(_cacheEntry, offset, limit, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(playlist, cached);
        }
        var items = await _spotify.GetPlaylistItemsAsync(
            _selectionKey.PlaylistId, offset, limit, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _cache.PutAsync(_cacheEntry, offset, limit, items, cancellationToken).ConfigureAwait(false);
        return new(playlist, items);
    }

    private async ValueTask<SpotifyPlaylistSummary> GetMetadataAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _metadata) is { } cached) return cached;
        await _metadataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_metadata is { } current) return current;
            var loaded = await _spotify.GetPlaylistAsync(
                _selectionKey.PlaylistId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(loaded.PlaylistId, _selectionKey.PlaylistId,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Spotify returned a different playlist.");
            cancellationToken.ThrowIfCancellationRequested();
            _cacheEntry = _cache.Observe(loaded);
            Volatile.Write(ref _metadata, loaded);
            return loaded;
        }
        finally
        {
            _metadataGate.Release();
        }
    }
}
