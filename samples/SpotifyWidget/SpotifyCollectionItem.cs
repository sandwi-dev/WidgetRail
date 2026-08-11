using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

internal sealed record SpotifyPlaylistCollectionItem(
    WidgetSpotifyPlaylistSummary Value,
    WidgetCollectionItemKey Key);

internal sealed record SpotifyMediaCollectionItem(
    WidgetSpotifyMediaItemSummary Value,
    WidgetCollectionItemKey Key);

internal static class SpotifyCollectionIdentity
{
    internal static WidgetCollectionItemKey Playlist(string playlistId) =>
        new("playlist." + Token(playlistId));

    internal static WidgetCollectionItemKey Media(string uri) =>
        new("media." + Token(uri));

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
