namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class MusicService
{
    private readonly SemaphoreSlim _ratingGate = new(1, 1);
    private long _ratingEpoch, _providerAccountGeneration;
    private bool _ratingAccountChanging;

    private void ResetSongLikes()
    {
        lock (_gate)
        {
            ++_ratingEpoch;
            _ratingAccountChanging = true;
            _state = _state with { SongLikes = new Dictionary<string, bool?>(), PendingLikes = new HashSet<string>(), RatingGeneration = _ratingEpoch };
        }
        Changed?.Invoke();
    }

    public Task ReadSongLikeAsync(MusicItem item, CancellationToken token) => SongLikeAsync(item, null, token);
    public Task SetSongLikeAsync(MusicItem item, bool liked, CancellationToken token) => SongLikeAsync(item, liked, token);

    private async Task SongLikeAsync(MusicItem item, bool? desired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (item.Kind != "song") throw new ArgumentException("Select a song.", nameof(item));
        long epoch, account;
        lock (_gate)
        {
            if (!_state.Connected || _ratingAccountChanging) throw new IOException("Connect your account to like songs.");
            if (_state.PendingLikes.Contains(item.Id)) return;
            if (desired is null && _state.SongLikes.ContainsKey(item.Id)) return;
            epoch = _ratingEpoch; account = _providerAccountGeneration;
            if (desired is not null)
                _state = _state with { PendingLikes = new HashSet<string>(_state.PendingLikes, StringComparer.Ordinal) { item.Id } };
        }
        if (desired is not null) Changed?.Invoke();
        try
        {
            await _ratingGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                    if (epoch != _ratingEpoch || _ratingAccountChanging || !_state.Connected ||
                        desired is null && _state.SongLikes.ContainsKey(item.Id)) return;
                object parameters = desired is { } liked
                    ? new { videoId = item.Id, accountGeneration = account, liked }
                    : new { videoId = item.Id, accountGeneration = account };
                var response = await _backend!.CallAsync("song_like", parameters, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!response.TryGetProperty("liked", out var rating) ||
                    rating.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False or System.Text.Json.JsonValueKind.Null))
                    throw new IOException("The song rating is unavailable.");
                bool? acknowledged = rating.ValueKind == System.Text.Json.JsonValueKind.Null ? null : rating.GetBoolean();
                if (desired is not null && acknowledged != desired) throw new IOException("The song rating was not acknowledged.");
                lock (_gate)
                {
                    if (epoch != _ratingEpoch || _ratingAccountChanging || !_state.Connected) return;
                    var ratings = new Dictionary<string, bool?>(_state.SongLikes, StringComparer.Ordinal);
                    if (ratings.Count >= 1024 && !ratings.ContainsKey(item.Id)) ratings.Remove(ratings.Keys.First());
                    ratings[item.Id] = acknowledged;
                    _state = _state with { SongLikes = ratings };
                }
                Changed?.Invoke();
            }
            finally { _ratingGate.Release(); }
        }
        finally
        {
            if (desired is not null)
            {
                lock (_gate)
                    if (epoch == _ratingEpoch)
                    {
                        var pending = new HashSet<string>(_state.PendingLikes, StringComparer.Ordinal);
                        pending.Remove(item.Id);
                        _state = _state with { PendingLikes = pending };
                    }
                Changed?.Invoke();
            }
        }
    }
}
