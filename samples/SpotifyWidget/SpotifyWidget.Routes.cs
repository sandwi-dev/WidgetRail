using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    private void Navigate(SpotifyDestination destination, string sourceElementId)
    {
        _playlists.ClearRequestedFocus(invalidate: false);
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _destination = destination;
            _pageError = null;
            _pageLoading = false;
            _readyInitialFocusId = sourceElementId;
        }
        Invalidate();
    }

    private async Task NavigateAndLoadAsync(
        SpotifyDestination destination,
        string sourceElementId,
        WidgetOperationContext operation)
    {
        _playlists.ClearRequestedFocus(invalidate: false);
        var shouldLoad = false;
        lock (_gate)
        {
            ClearPlaylistSelectionLocked();
            _destination = destination;
            _pageError = null;
            _readyInitialFocusId = sourceElementId;
            shouldLoad = destination switch
            {
                SpotifyDestination.Devices => _devices is null || _localPlayback is null ||
                    !IsFresh(_devicesCachedAt, DevicesCacheLifetime),
                _ => false,
            };
            _pageLoading = shouldLoad;
        }
        Invalidate();
        if (shouldLoad) await LoadDestinationAsync(destination, operation)
            .ConfigureAwait(false);
    }

    private Task ReloadCurrentPageAsync(WidgetOperationContext operation)
    {
        SpotifyDestination destination;
        lock (_gate)
        {
            destination = _destination;
            _pageLoading = true;
            _pageError = null;
        }
        Invalidate();
        return LoadDestinationAsync(destination, operation);
    }

    private async Task LoadDestinationAsync(
        SpotifyDestination destination,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            switch (destination)
            {
                case SpotifyDestination.Devices:
                    var devicesTask = HostServices.Spotify.GetDevicesAsync(cancellationToken).AsTask();
                    var localTask = HostServices.Spotify.GetLocalPlaybackAsync(cancellationToken).AsTask();
                    await Task.WhenAll(devicesTask, localTask).ConfigureAwait(false);
                    if (!operation.IsCurrent) return;
                    lock (_gate)
                    {
                        _devices = devicesTask.Result;
                        _localPlayback = localTask.Result;
                        _preferredPlaybackDeviceId = devicesTask.Result.Devices
                            .FirstOrDefault(device => device.IsActive && !device.IsRestricted)
                            ?.DeviceId ?? _preferredPlaybackDeviceId;
                        _devicesCachedAt = _timeProvider.GetUtcNow();
                    }
                    break;
            }
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = "This Spotify feature is unavailable in the trusted host.";
            }
        }
        catch (WidgetCapabilityException exception)
        {
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = PageError(exception);
            }
        }
        catch (Exception)
        {
            if (!operation.IsCurrent) return;
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = "Spotify could not load this page. Try again.";
            }
        }
        if (operation.IsCurrent) Invalidate();
    }

    private void OpenPlaylist(WidgetCollectionItemKey key, string sourceElementId)
    {
        var mode = sourceElementId.Contains(".compact.", StringComparison.Ordinal)
            ? "compact" : "wide";
        lock (_gate)
        {
            var playlist = _playlists.Snapshot.Items
                .FirstOrDefault(item => item.Key == key)?.Value;
            if (_destination != SpotifyDestination.Playlists || playlist is null) return;
            _playlists.SelectAnchor(key, invalidate: false);
            _playlistItems.Reset(invalidate: false);
            _playlistOccurrences.Reset();
            var generation = checked(++_playlistSelectionGeneration);
            _pageError = null;
            _playlistSelection = new(
                new(playlist.PlaylistId, generation), playlist, mode, sourceElementId);
            _playlistItemsSelectionGeneration = generation;
            _readyInitialFocusId = $"spotify.playlist.play.{mode}";
            _playlistItems.EnsureLoaded();
        }
    }

    private async ValueTask<WidgetCursorPage<SpotifyMediaCollectionItem>>
        LoadSelectedPlaylistCursorPageAsync(
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            CancellationToken cancellationToken)
    {
        var offset = SpotifyCollectionIdentity.Offset(cursor);
        SpotifyPlaylistSelection? selection;
        lock (_gate) selection = _playlistSelection;
        if (selection is null)
            throw new InvalidOperationException("No Spotify playlist is selected.");
        var occurrenceRequest = _playlistOccurrences.BeginPage(
            selection.Key.PlaylistId, offset, direction);
        var page = await HostServices.Spotify.GetPlaylistItemsAsync(
            selection.Key.PlaylistId, offset, limit, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(page.Playlist.PlaylistId, selection.Key.PlaylistId,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Spotify returned a different playlist.");
        var items = _playlistOccurrences.NormalizePage(
            occurrenceRequest,
            page.Items,
            _playlistItems.Snapshot.Items.Select(item => item.Key).ToArray());
        return SpotifyCollectionIdentity.Page(items, page.Offset, page.Limit, page.Total);
    }

    private bool IsFresh(DateTimeOffset? cachedAt, TimeSpan lifetime) =>
        cachedAt is { } value && _timeProvider.GetUtcNow() - value < lifetime;

    private void ClearPageCaches()
    {
        lock (_gate) ClearPageCachesLocked();
    }

    private void ClearPageCachesLocked()
    {
        _playlists.Reset(invalidate: false);
        ClearPlaylistSelectionLocked();
        _queue.Reset(invalidate: false);
        _queueOccurrences.Reset();
        _devices = null;
        _localPlayback = null;
        _devicesCachedAt = null;
        _preferredPlaybackDeviceId = null;
        _pageLoading = false;
        _pageError = null;
    }

    private void ClearPlaylistSelectionLocked()
    {
        _playlistItems.Reset(invalidate: false);
        _playlistOccurrences.Reset();
        _playlistSelection = null;
        _playlistItemsSelectionGeneration = null;
    }

}
