using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal sealed record SpotifyQueueQuery(long Generation, IReadOnlyList<SpotifyMediaCollectionItem> Items)
{
    internal IReadOnlyDictionary<string, bool?> Saved { get; init; } = new Dictionary<string, bool?>();
}

/// <summary>
/// Queue is one complete, bounded provider observation without adjacent cursors.
/// Playlist/Search use separate discovered-prefix sources, not a fabricated total.
/// </summary>
internal static class SpotifyIndexedQueue
{
    internal static WidgetLoadedCollectionOptions<SpotifyQueueQuery, SpotifyMediaCollectionItem> Options(
        Func<SpotifyQueueQuery, SpotifyMediaCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction) => new()
        {
            ItemKey = item => item.Key,
            PreserveOccurrences = true,
            RenderItem = (query, item, context) => SpotifyPresentation.IndexedQueueRow(item, context.Index == 0,
                query.Saved.GetValueOrDefault(item.Value.Uri)),
            OnAction = onAction,
        };

    // Called under the owner's gate. Queries freeze the complete observation;
    // row rendering never reads a live resource or follows a provider cursor.
    internal static SpotifyPresentationState Prepare(SpotifyPresentationState state, long generation,
        WidgetLoadedCollection<SpotifyQueueQuery, SpotifyMediaCollectionItem> source, ref SpotifyQueueQuery? previous, Func<string, bool?>? savedState = null)
    {
        if (state.ViewState != SpotifyWidgetViewState.Ready) return state;
        var items = Array.AsReadOnly(state.Queue.Items.ToArray());
        var next = new SpotifyQueueQuery(generation, items)
        {
            Saved = items.Select(item => item.Value.Uri).Distinct(StringComparer.Ordinal)
                .ToDictionary(uri => uri, uri => savedState?.Invoke(uri), StringComparer.Ordinal),
        };
        if (previous is null || previous.Generation != next.Generation || !previous.Items.SequenceEqual(items) || !previous.Saved.SequenceEqual(next.Saved))
            source.Publish("queue", next, items);
        previous = next;
        return state with { IndexedQueue = UI.CollectionList("spotify.queue.scroll", source, 82, "Upcoming Spotify queue")
            .Classes("spotify-page-scroll") };
    }
}
