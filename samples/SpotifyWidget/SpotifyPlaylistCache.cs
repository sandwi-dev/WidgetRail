namespace WidgetRail.Samples.SpotifyWidget;

/// <summary>Small, per-widget cache. Only a fresh metadata observation authorizes reuse.</summary>
internal sealed class SpotifyPlaylistCache
{
    internal const int MaximumPlaylists = 4;
    internal const int MaximumItems = 192;
    internal const int MaximumPages = 16;
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

    internal void Remove(string playlistId) { lock (_gate) RemoveLocked(playlistId); }
    internal void Clear()
    {
        lock (_gate)
        {
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
