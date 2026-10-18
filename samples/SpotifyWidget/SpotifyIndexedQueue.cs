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
    internal static WidgetIndexedCollectionOptions<SpotifyQueueQuery, SpotifyMediaCollectionItem> Options(
        Func<SpotifyQueueQuery, SpotifyMediaCollectionItem, WidgetActionEvent, CancellationToken, ValueTask> onAction) => new()
        {
            ReadRange = (query, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<SpotifyMediaCollectionItem>>(query.Items.Skip(start).Take(count).ToArray());
            },
            ItemKey = item => item.Key,
            OccurrenceKeys = query => query.Items.Select(item => item.Key).ToArray(),
            RenderItem = (query, item, context) => SpotifyPresentation.IndexedQueueRow(item, context.Index == 0,
                query.Saved.GetValueOrDefault(item.Value.Uri)),
            OnAction = onAction,
        };

    // Called under the owner's gate. Queries freeze the complete observation;
    // row rendering never reads a live resource or follows a provider cursor.
    internal static SpotifyPresentationState Prepare(SpotifyPresentationState state, long generation,
        WidgetIndexedCollection<SpotifyQueueQuery, SpotifyMediaCollectionItem> source, ref SpotifyQueueQuery? previous, Func<string, bool?>? savedState = null)
    {
        if (state.ViewState != SpotifyWidgetViewState.Ready) return state;
        var items = Array.AsReadOnly(state.Queue.Items.ToArray());
        var next = new SpotifyQueueQuery(generation, items)
        {
            Saved = items.Select(item => item.Value.Uri).Distinct(StringComparer.Ordinal)
                .ToDictionary(uri => uri, uri => savedState?.Invoke(uri), StringComparer.Ordinal),
        };
        var membershipChanged = previous is null || !previous.Items.Select(item => item.Key).SequenceEqual(items.Select(item => item.Key));
        if (membershipChanged) source.PublishQuery(next, items.Count);
        else if (previous!.Generation != next.Generation || !previous.Items.SequenceEqual(items) || !previous.Saved.SequenceEqual(next.Saved)) source.UpdateContent(next);
        previous = next;
        return state with { IndexedQueue = UI.CollectionList("spotify.queue.scroll", source, 82, "Upcoming Spotify queue")
            .Classes("spotify-page-scroll") };
    }
}
