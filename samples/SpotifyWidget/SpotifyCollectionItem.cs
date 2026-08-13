using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

internal sealed record SpotifyPlaylistCollectionItem(
    SpotifyPlaylistSummary Value,
    WidgetCollectionItemKey Key);

internal sealed record SpotifyMediaCollectionItem(
    SpotifyMediaItemSummary Value,
    WidgetCollectionItemKey Key);

internal static class SpotifyCollectionIdentity
{
    internal static WidgetCollectionItemKey Playlist(string playlistId) =>
        new("playlist." + Token(playlistId));

    internal static WidgetCollectionItemKey Media(string uri) =>
        new("media." + Token(uri));

    internal static WidgetCollectionItemKey MediaOccurrence(
        string uri,
        string collectionContext,
        string evidence,
        int collision)
    {
        if (collision < 0) throw new ArgumentOutOfRangeException(nameof(collision));
        return new WidgetCollectionItemKey(
            "media." + Token(uri) + ".occ." +
            Token(collectionContext + "\u001f" + evidence + "\u001f" +
                collision.ToString(CultureInfo.InvariantCulture)));
    }

    internal static string FocusId(string prefix, string mode, WidgetCollectionItemKey key) =>
        $"{prefix}.{mode}.{key.Value}";

    internal static WidgetCollectionCursor Cursor(int offset)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return new("offset." + offset.ToString(CultureInfo.InvariantCulture));
    }

    internal static int Offset(WidgetCollectionCursor? cursor)
    {
        if (cursor is null) return 0;
        const string prefix = "offset.";
        var value = cursor.Value.Value;
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(value.AsSpan(prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var offset) || offset < 0)
            throw new InvalidOperationException("Spotify returned an invalid collection cursor.");
        return offset;
    }

    internal static WidgetCursorPage<TItem> Page<TItem>(
        IReadOnlyList<TItem> items,
        int offset,
        int limit,
        int total) where TItem : notnull
    {
        if (offset < 0 || limit < 1 || total < 0 || offset > total || items.Count > limit)
            throw new InvalidOperationException("Spotify returned an invalid collection page.");
        WidgetCollectionCursor? before = offset == 0
            ? null : Cursor(Math.Max(0, offset - limit));
        var nextOffset = checked(offset + limit);
        WidgetCollectionCursor? after = nextOffset >= total ? null : Cursor(nextOffset);
        return new(items, before, after);
    }

    private static string Token(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }
}

internal sealed class SpotifyMediaOccurrencePolicy(int maximumRetainedOccurrences)
{
    private readonly object _gate = new();
    private readonly List<Occurrence> _occurrences = [];
    private string? _collectionContext;
    private long _sequence;
    private long _requestGeneration;

    internal int RetainedCount
    {
        get { lock (_gate) return _occurrences.Count; }
    }

    internal Request BeginPage(
        string collectionContext,
        int offset,
        WidgetCursorDirection? direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionContext);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (_gate)
        {
            if (!string.Equals(_collectionContext, collectionContext,
                    StringComparison.Ordinal))
            {
                _collectionContext = collectionContext;
                _occurrences.Clear();
            }
            return new(collectionContext, offset, direction,
                checked(++_requestGeneration));
        }
    }

    internal IReadOnlyList<SpotifyMediaCollectionItem> NormalizePage(
        Request request,
        IReadOnlyList<SpotifyMediaItemSummary> items,
        IReadOnlyCollection<WidgetCollectionItemKey> retainedKeys)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(retainedKeys);
        if (items.Count > maximumRetainedOccurrences)
            throw new InvalidOperationException("Spotify occurrence page is too large.");

        var evidence = BuildEvidence(items);
        lock (_gate)
        {
            if (request.Generation != _requestGeneration ||
                !string.Equals(request.CollectionContext, _collectionContext,
                    StringComparison.Ordinal))
                return NormalizeSuperseded(request, items, evidence);

            var occupied = request.Direction is null
                ? new HashSet<WidgetCollectionItemKey>()
                : retainedKeys.ToHashSet();
            var used = new HashSet<WidgetCollectionItemKey>();
            var normalized = new SpotifyMediaCollectionItem[items.Count];
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var position = checked(request.Offset + index);
                var itemEvidence = evidence[index];
                var candidate = _occurrences
                    .Where(entry => string.Equals(entry.Uri, item.Uri,
                        StringComparison.Ordinal) &&
                        !used.Contains(entry.Key) &&
                        (!occupied.Contains(entry.Key) || entry.PageOffset == request.Offset))
                    .OrderByDescending(entry => string.Equals(
                        entry.ContextEvidence, itemEvidence.ContextEvidence,
                        StringComparison.Ordinal))
                    .ThenByDescending(entry => string.Equals(
                        entry.ItemFingerprint, itemEvidence.ItemFingerprint,
                        StringComparison.Ordinal))
                    .ThenBy(entry => Math.Abs((long)entry.Position - position))
                    .ThenBy(entry => entry.Key.Value, StringComparer.Ordinal)
                    .FirstOrDefault();

                var key = candidate?.Key ?? AllocateKey(
                    item.Uri, request.CollectionContext, itemEvidence.ContextEvidence,
                    occupied, used);
                used.Add(key);
                if (candidate is not null) _occurrences.Remove(candidate);
                _occurrences.Add(new(
                    item.Uri,
                    key,
                    itemEvidence.ItemFingerprint,
                    itemEvidence.ContextEvidence,
                    position,
                    request.Offset,
                    checked(++_sequence)));
                normalized[index] = new(item, key);
            }

            while (_occurrences.Count > maximumRetainedOccurrences)
            {
                var stale = _occurrences
                    .Where(entry => !used.Contains(entry.Key))
                    .OrderBy(entry => entry.LastSeen)
                    .FirstOrDefault();
                if (stale is null) break;
                _occurrences.Remove(stale);
            }
            return Array.AsReadOnly(normalized);
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _collectionContext = null;
            _occurrences.Clear();
            checked { _requestGeneration++; }
        }
    }

    private static IReadOnlyList<SpotifyMediaCollectionItem> NormalizeSuperseded(
        Request request,
        IReadOnlyList<SpotifyMediaItemSummary> items,
        IReadOnlyList<Evidence> evidence)
    {
        var keys = new HashSet<WidgetCollectionItemKey>();
        var normalized = new SpotifyMediaCollectionItem[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var collision = 0;
            WidgetCollectionItemKey key;
            do
            {
                key = SpotifyCollectionIdentity.MediaOccurrence(
                    items[index].Uri,
                    request.CollectionContext,
                    evidence[index].ContextEvidence,
                    collision++);
            } while (!keys.Add(key));
            normalized[index] = new(items[index], key);
        }
        return Array.AsReadOnly(normalized);
    }

    private WidgetCollectionItemKey AllocateKey(
        string uri,
        string collectionContext,
        string contextEvidence,
        IReadOnlySet<WidgetCollectionItemKey> occupied,
        IReadOnlySet<WidgetCollectionItemKey> used)
    {
        var semantic = SpotifyCollectionIdentity.Media(uri);
        if (!occupied.Contains(semantic) && !used.Contains(semantic) &&
            _occurrences.All(entry => entry.Key != semantic))
            return semantic;

        for (var collision = 0; collision <= maximumRetainedOccurrences * 2; collision++)
        {
            var candidate = SpotifyCollectionIdentity.MediaOccurrence(
                uri, collectionContext, contextEvidence, collision);
            if (!occupied.Contains(candidate) && !used.Contains(candidate) &&
                _occurrences.All(entry => entry.Key != candidate))
                return candidate;
        }
        throw new InvalidOperationException("Spotify occurrence identity is exhausted.");
    }

    private static Evidence[] BuildEvidence(
        IReadOnlyList<SpotifyMediaItemSummary> items)
    {
        var evidence = new Evidence[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            string? before = null;
            for (var previous = index - 1; previous >= 0; previous--)
            {
                if (string.Equals(items[previous].Uri, item.Uri, StringComparison.Ordinal))
                    continue;
                before = items[previous].Uri;
                break;
            }
            string? after = null;
            for (var next = index + 1; next < items.Count; next++)
            {
                if (string.Equals(items[next].Uri, item.Uri, StringComparison.Ordinal))
                    continue;
                after = items[next].Uri;
                break;
            }
            var fingerprint = string.Join('\u001f',
                (int)item.ItemType,
                item.Uri,
                item.Title,
                item.Subtitle,
                item.DurationMilliseconds,
                item.SpotifyUrl,
                item.IsPlayable);
            evidence[index] = new(
                fingerprint,
                fingerprint + "\u001e" + before + "\u001e" + after);
        }
        return evidence;
    }

    private sealed record Evidence(string ItemFingerprint, string ContextEvidence);
    internal sealed record Request(
        string CollectionContext,
        int Offset,
        WidgetCursorDirection? Direction,
        long Generation);
    private sealed record Occurrence(
        string Uri,
        WidgetCollectionItemKey Key,
        string ItemFingerprint,
        string ContextEvidence,
        int Position,
        int PageOffset,
        long LastSeen);
}
