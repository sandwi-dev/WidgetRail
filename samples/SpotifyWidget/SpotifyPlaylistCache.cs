namespace WidgetRail.Samples.SpotifyWidget;

/// <summary>Small, per-widget cache. Only a fresh metadata observation authorizes reuse.</summary>
internal sealed class SpotifyPlaylistCache(string? diskRoot = null)
{
    internal const int MaximumPlaylists = 16;
    internal const int MaximumItems = 1024;
    internal const int MaximumPages = 64;
    internal sealed class Entry(string id, string version)
    {
        internal string Id { get; } = id;
        internal string Version { get; } = version;
        internal long LastUsed;
        internal Dictionary<(int Offset, int Limit), Page> Pages { get; } = [];
    }
    internal sealed record Page(SpotifyPlaylistItemsPageSummary Value, long LastUsed);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _clock;
    private string? _partition;
    private SpotifyPlaylistDiskCache? _disk;
    private Task _cleanup = Task.CompletedTask;
    private void TrackCleanup(Task task) => _cleanup = _cleanup.IsCompleted ? task : Task.WhenAll(_cleanup, task);
    internal Task WhenIdleAsync(CancellationToken token = default)
    {
        lock (_gate) return _cleanup.WaitAsync(token);
    }

    internal void SetPartition(string? partition)
    {
        if (diskRoot is null) return;
        if (!Guid.TryParseExact(partition, "N", out _)) partition = null;
        lock (_gate)
        {
            if (_partition == partition) return;
            if (_disk is { } previous) TrackCleanup(previous.RetireAndClearAsync());
            foreach (var entry in _entries.Values) entry.Pages.Clear();
            _entries.Clear();
            _partition = partition;
            _disk = partition is null ? null : new(diskRoot, partition);
        }
    }
    internal async Task<SpotifyPlaylistItemsPageSummary?> GetAsync(Entry? entry, int offset, int limit, CancellationToken token)
    {
        if (Get(entry, offset, limit) is { } memory) return memory;
        SpotifyPlaylistDiskCache? disk;
        lock (_gate) { if (!IsCurrent(entry)) return null; disk = _disk; }
        if (disk is null) return null;
        SpotifyPlaylistItemsPageSummary? page;
        try
        {
            page = await disk.ReadAsync(entry!.Id, entry.Version, offset, limit, token)
                .WaitAsync(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
        }
        catch (TimeoutException) { return null; }
        lock (_gate)
        {
            if (!IsCurrent(entry) || !ReferenceEquals(disk, _disk)) return null;
            if (page is not null) Put(entry, offset, limit, page);
        }
        return page;
    }
    internal Task PutAsync(Entry? entry, int offset, int limit, SpotifyPlaylistItemsPageSummary page, CancellationToken token)
    {
        Put(entry, offset, limit, page);
        lock (_gate)
        {
            if (IsCurrent(entry) && _disk is { } disk)
                TrackCleanup(disk.WriteAsync(entry!.Id, entry.Version, offset, limit, page, token));
        }
        return Task.CompletedTask; // Publish fetched tracks without waiting for optional disk writes.
    }

    internal Entry? Observe(SpotifyPlaylistSummary playlist)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(playlist.SnapshotId))
            {
                RemoveLocked(playlist.PlaylistId);
                return null; // Missing version is never permission to reuse old pages.
            }
            if (!_entries.TryGetValue(playlist.PlaylistId, out var entry) ||
                entry.Version != playlist.SnapshotId)
            {
                RemoveLocked(playlist.PlaylistId);
                _entries[playlist.PlaylistId] = entry = new(playlist.PlaylistId, playlist.SnapshotId);
            }
            entry.LastUsed = ++_clock;
            while (_entries.Count > MaximumPlaylists)
                RemoveLocked(_entries.Values.MinBy(value => value.LastUsed)!.Id);
            return entry;
        }
    }

    internal SpotifyPlaylistItemsPageSummary? Get(Entry? entry, int offset, int limit)
    {
        lock (_gate)
        {
            if (!IsCurrent(entry) || !entry!.Pages.TryGetValue((offset, limit), out var page)) return null;
            entry.LastUsed = ++_clock;
            entry.Pages[(offset, limit)] = page with { LastUsed = _clock };
            return page.Value;
        }
    }

    internal void Put(Entry? entry, int offset, int limit, SpotifyPlaylistItemsPageSummary page)
    {
        lock (_gate)
        {
            if (!IsCurrent(entry) || page.Items.Count > MaximumItems) return;
            entry!.LastUsed = ++_clock;
            entry.Pages[(offset, limit)] = new(page with { Items = page.Items.ToArray() }, _clock);
            while (_entries.Values.Sum(value => value.Pages.Count) > MaximumPages ||
                _entries.Values.Sum(value => value.Pages.Values.Sum(item => item.Value.Items.Count)) > MaximumItems)
            {
                var oldest = _entries.Values.SelectMany(value => value.Pages.Select(item =>
                    (Entry: value, Key: item.Key, Page: item.Value))).MinBy(item => item.Page.LastUsed);
                oldest.Entry.Pages.Remove(oldest.Key);
            }
        }
    }

    internal void Remove(string playlistId)
    {
        lock (_gate)
        {
            RemoveLocked(playlistId);
            if (_disk is { } disk) TrackCleanup(disk.RemoveAsync(playlistId));
        }
    }
    internal void Clear()
    {
        lock (_gate)
        {
            if (_disk is { } disk) TrackCleanup(disk.RetireAndClearAsync());
            _disk = null; _partition = null;
            foreach (var entry in _entries.Values) entry.Pages.Clear();
            _entries.Clear();
        }
    }
    private void RemoveLocked(string playlistId)
    {
        if (_entries.Remove(playlistId, out var entry)) entry.Pages.Clear();
    }
    private bool IsCurrent(Entry? entry) => entry is not null &&
        _entries.TryGetValue(entry.Id, out var current) && ReferenceEquals(entry, current);
}
