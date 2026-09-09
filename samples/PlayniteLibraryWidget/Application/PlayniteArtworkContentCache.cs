using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal readonly record struct PlayniteArtworkCacheEviction(string Handle, int Bytes);

internal readonly record struct PlayniteArtworkCacheStoreResult(
    bool Stored,
    int PreviousBytes,
    IReadOnlyList<PlayniteArtworkCacheEviction> Evictions);

/// <summary>
/// Byte-bounded residency for already validated encoded artwork. Published
/// handle registration and provider authority remain owned by the application
/// service so content eviction can never revoke a live handle.
/// </summary>
internal sealed class PlayniteArtworkContentCache
{
    internal const long MaximumRetainedBytes = 128L * 1024 * 1024;

    private readonly long _maximumRetainedBytes;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = [];

    internal PlayniteArtworkContentCache(
        long maximumRetainedBytes = MaximumRetainedBytes)
    {
        if (maximumRetainedBytes is < 1 or > MaximumRetainedBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        _maximumRetainedBytes = maximumRetainedBytes;
    }

    internal int Count => _entries.Count;
    internal long RetainedBytes { get; private set; }
    internal bool Contains(string handle) => _entries.ContainsKey(handle);

    internal bool TryGet(string handle, out WidgetEncodedArtwork? artwork)
    {
        if (!_entries.TryGetValue(handle, out var entry))
        {
            artwork = null;
            return false;
        }

        _recency.Remove(entry.Recency);
        _recency.AddFirst(entry.Recency);
        artwork = entry.Artwork;
        return true;
    }

    internal PlayniteArtworkCacheStoreResult Store(
        string handle,
        WidgetEncodedArtwork artwork)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(artwork);
        var bytes = artwork.Bytes.Length;
        var previousBytes = RemoveCore(handle)?.Bytes ?? 0;
        if (bytes > _maximumRetainedBytes)
            return new(false, previousBytes, []);

        var recency = _recency.AddFirst(handle);
        _entries.Add(handle, new(artwork, recency));
        RetainedBytes += bytes;
        var evictions = new List<PlayniteArtworkCacheEviction>();
        while (RetainedBytes > _maximumRetainedBytes && _recency.Last is { } oldest)
        {
            var removed = RemoveCore(oldest.Value);
            if (removed is not null)
                evictions.Add(new(oldest.Value, removed.Bytes));
        }
        return new(true, previousBytes, evictions);
    }

    internal PlayniteArtworkCacheEviction? Remove(string handle)
    {
        var removed = RemoveCore(handle);
        return removed is null ? null : new(handle, removed.Bytes);
    }

    internal void Clear()
    {
        _entries.Clear();
        _recency.Clear();
        RetainedBytes = 0;
    }

    private RemovedEntry? RemoveCore(string handle)
    {
        if (!_entries.Remove(handle, out var entry)) return null;
        _recency.Remove(entry.Recency);
        var bytes = entry.Artwork.Bytes.Length;
        RetainedBytes -= bytes;
        return new(bytes);
    }

    private sealed record Entry(
        WidgetEncodedArtwork Artwork,
        LinkedListNode<string> Recency);

    private sealed record RemovedEntry(int Bytes);
}
