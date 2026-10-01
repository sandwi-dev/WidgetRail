using System.Globalization;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal sealed record SpotifyLibraryQuery(long Generation);
internal sealed record SpotifySearchQuery(long Generation, string Text, SpotifySearchKind Kind);
internal sealed record SpotifyDetailQuery(SpotifyPlaylistSelection? Selection, SpotifySelectedPlaylistPageSource? Pages);

/// <summary>Provider paging and occurrence policy; navigation and command authority remain in the widget owner.</summary>
internal static class SpotifyDiscoveredCollections
{
    internal static WidgetDiscoveredCollectionOptions<SpotifyLibraryQuery, SpotifyPlaylistCollectionItem> Library(
        ISpotifyApplicationService spotify, int pageSize, int maximumItems,
        Func<SpotifyLibraryQuery, SpotifyPlaylistCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction,
        Func<Exception, WidgetResourceError> mapError) => new()
    {
        PageSize = pageSize, MaximumItems = maximumItems, MapError = mapError,
        DuplicatePolicy = WidgetDiscoveredDuplicatePolicy.KeepFirst,
        ItemKey = item => item.Key,
        LoadNext = async (_, continuation, count, token) =>
        {
            var offset = Offset(continuation);
            var page = await spotify.GetPlaylistsAsync(offset, count, token).ConfigureAwait(false);
            var next = Next(offset, count, page.Offset, page.Limit, page.Total, page.Items.Count);
            return new(page.Items.Select(item => new SpotifyPlaylistCollectionItem(item,
                SpotifyCollectionIdentity.Playlist(item.PlaylistId))).ToArray(), next);
        },
        RenderItem = (_, item, _) => SpotifyPresentation.IndexedPlaylistRow(item),
        OnAction = onAction,
    };

    internal static WidgetDiscoveredCollectionOptions<SpotifyDetailQuery, SpotifyMediaCollectionItem> Detail(
        int pageSize, int maximumItems, Action<SpotifyDetailQuery, SpotifyPlaylistSummary, CancellationToken> publishMetadata,
        Func<SpotifyDetailQuery, SpotifyMediaCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction,
        Func<Exception, WidgetResourceError> mapError) => new()
    {
        PageSize = pageSize, MaximumItems = maximumItems, MapError = mapError,
        ItemKey = item => item.Key,
        LoadNext = async (query, continuation, count, token) =>
        {
            if (query.Selection is not { } selection || query.Pages is not { } pages)
                throw new InvalidOperationException("No Spotify playlist is selected.");
            var offset = Offset(continuation);
            var page = await pages.LoadAsync(offset, count, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var next = Next(offset, count, page.Items.Offset, page.Items.Limit, page.Items.Total, page.Items.Items.Count);
            publishMetadata(query, page.Playlist, token);
            // Provider-page occurrences do not claim raw positions in filtered
            // responses, and never become the native discovered extent.
            return new(page.Items.Items.Select((item, index) => new SpotifyMediaCollectionItem(item,
                SpotifyCollectionIdentity.MediaOccurrence(item.Uri, selection.Key.PlaylistId,
                    "page:" + offset.ToString(CultureInfo.InvariantCulture) + ":slot:" + index.ToString(CultureInfo.InvariantCulture), 0))).ToArray(), next);
        },
        RenderItem = (_, item, _) => SpotifyPresentation.IndexedPlaylistTrackRow(item),
        OnAction = onAction,
    };

    internal static WidgetDiscoveredCollectionOptions<SpotifySearchQuery, SpotifySearchCollectionItem> Search(
        ISpotifyApplicationService spotify, int maximumItems,
        Func<SpotifySearchQuery, SpotifySearchCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction,
        Func<Exception, WidgetResourceError> mapError) => new()
    {
        PageSize = Math.Min(10, maximumItems), MaximumItems = maximumItems, MapError = mapError,
        ItemKey = item => item.Key,
        LoadNext = async (query, continuation, count, token) =>
        {
            if (query.Text.Length == 0) return new([], null);
            var offset = Offset(continuation);
            var page = await spotify.SearchAsync(query.Text, query.Kind, offset, count, token).ConfigureAwait(false);
            var next = Next(offset, count, page.Offset, page.Limit, page.Total, page.Items.Count);
            return new(page.Items.Select((item, index) => new SpotifySearchCollectionItem(item,
                new("search." + query.Generation.ToString(CultureInfo.InvariantCulture) + "." +
                    offset.ToString(CultureInfo.InvariantCulture) + "." + index.ToString(CultureInfo.InvariantCulture) + "." +
                    SpotifyCollectionIdentity.Media(item.Uri).Value))).ToArray(), next);
        },
        RenderItem = (_, item, _) => SpotifyPresentation.IndexedSearchRow(item),
        OnAction = onAction,
    };

    private static int Offset(string? continuation) => SpotifyCollectionIdentity.Offset(
        continuation is null ? null : new WidgetCollectionCursor(continuation));
    private static string? Next(int requestedOffset, int requestedCount, int offset, int limit, int total, int count)
    {
        if (offset != requestedOffset || limit is < 1 || limit > requestedCount || total < 0 || count > limit)
            throw new InvalidOperationException("Spotify returned an invalid continuation page.");
        var next = checked(offset + limit);
        return next < total ? SpotifyCollectionIdentity.Cursor(next).Value : null;
    }
}
