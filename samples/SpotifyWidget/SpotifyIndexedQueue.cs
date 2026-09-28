using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal sealed record SpotifyQueueQuery(long Generation, IReadOnlyList<SpotifyMediaCollectionItem> Items);

/// <summary>
/// Queue is one complete, bounded provider observation without adjacent cursors.
/// Playlist/Search cursor windows are not complete queries and stay separate.
/// </summary>
internal static class SpotifyIndexedQueue
{
    internal static WidgetIndexedCollectionOptions<SpotifyQueueQuery, SpotifyMediaCollectionItem> Options(
        Func<SpotifyQueueQuery, SpotifyMediaCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction) => new()
        {
            ReadRange = (query, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<SpotifyMediaCollectionItem>>(query.Items.Skip(start).Take(count).ToArray());
            },
            ItemKey = item => item.Key,
            RenderItem = (_, item, context) => SpotifyPresentation.IndexedQueueRow(item, context.Index == 0),
            OnAction = onAction,
        };

    // Called under the owner's gate. Queries freeze the complete observation;
    // row rendering never reads a live resource or follows a provider cursor.
    internal static SpotifyPresentationState Prepare(SpotifyPresentationState state, long generation,
        WidgetIndexedCollection<SpotifyQueueQuery, SpotifyMediaCollectionItem> source, ref SpotifyQueueQuery? previous)
    {
        if (state.ViewState != SpotifyWidgetViewState.Ready) return state;
        var items = Array.AsReadOnly(state.Queue.Items.ToArray());
        var next = new SpotifyQueueQuery(generation, items);
        var membershipChanged = previous is null || !previous.Items.Select(item => item.Key).SequenceEqual(items.Select(item => item.Key));
        if (membershipChanged) source.PublishQuery(next, items.Count);
        else if (previous!.Generation != next.Generation || !previous.Items.SequenceEqual(items)) source.UpdateContent(next);
        previous = next;
        return state with { IndexedQueue = UI.CollectionList("spotify.queue.scroll", source, 82, "Upcoming Spotify queue")
            .Classes("spotify-page-scroll") };
    }
}
