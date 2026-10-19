namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed record MusicItem(string Id, string Kind, string Title, string Subtitle = "", string Artwork = "", string Section = "", bool? Liked = null);
public sealed record MusicPage(string Title, IReadOnlyList<MusicItem> Items);
public sealed record PlayNextResult(int AddedCount, bool QueueLimitReached);
public sealed record MusicQueueAddition(MusicState State, MusicItem[] OriginalQueue, PlayNextResult Result);
public sealed record MusicQueueRemoval(MusicState State, MusicItem[] OriginalQueue, bool CurrentRemoved);
public sealed record PlayerState(string TrackId = "", bool Playing = false, bool Buffering = false,
    double Position = 0, double Duration = 0, double Volume = .5, string? Error = null);
public sealed record MusicState(bool Connected, IReadOnlyList<MusicItem> Queue, int Index,
    bool Shuffle, string Repeat, PlayerState Player, string? PlaybackNotice = null)
{
    public static MusicState Empty { get; } = new(false, [], -1, false, "off", new());
    public MusicItem? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public IReadOnlyDictionary<string, bool?> SongLikes { get; init; } = new Dictionary<string, bool?>();
    public IReadOnlySet<string> PendingLikes { get; init; } = new HashSet<string>();
    public long RatingGeneration { get; init; }
    public bool? LikeFor(MusicItem item) => SongLikes.TryGetValue(item.Id, out var liked) ? liked : null;
}

public interface IMusicService : IAsyncDisposable
{
    MusicState State { get; }
    event Action? Changed;
    Task InitializeAsync(CancellationToken token);
    Task<string> SignInAsync(CancellationToken token);
    Task CancelSignInAsync(CancellationToken token);
    Task DisconnectAsync(CancellationToken token);
    Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token);
    Task<IReadOnlyList<string>> SearchSuggestionsAsync(string query, CancellationToken token);
    Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token);
    Task SelectQueueItemAsync(IReadOnlyList<MusicItem> expectedQueue, int index, CancellationToken token);
    Task RemoveQueueItemAsync(IReadOnlyList<MusicItem> expectedQueue, int index, CancellationToken token);
    Task ReadSongLikeAsync(MusicItem item, CancellationToken token);
    Task SetSongLikeAsync(MusicItem item, bool liked, CancellationToken token);
    Task RadioAsync(MusicItem song, CancellationToken token);
    Task<PlayNextResult> PlayNextAsync(MusicItem item, CancellationToken token);
    Task CommandAsync(string command, double? value, CancellationToken token);
}

// Queue movement is independent of page navigation and never wraps unless repeat permits it.
public static class MusicQueue
{
    public const int MaximumItems = 500;

    public static MusicQueueRemoval Remove(MusicState state, IReadOnlyList<MusicItem> originalQueue, int index)
    {
        if (index < 0 || index >= state.Queue.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var removed = state.Queue[index];
        var queue = state.Queue.Where((_, position) => position != index).ToArray();
        var originalIndex = -1;
        for (var i = 0; i < originalQueue.Count; i++)
            if (ReferenceEquals(originalQueue[i], removed)) { originalIndex = i; break; }
        if (originalIndex < 0) throw new InvalidOperationException("The queue occurrence is missing from the original order.");
        var original = originalQueue.Where((_, position) => position != originalIndex).ToArray();
        var currentRemoved = index == state.Index;
        var nextIndex = currentRemoved
            ? index < queue.Length ? index : state.Repeat == "all" && queue.Length > 0 ? 0 : -1
            : state.Index - (index < state.Index ? 1 : 0);
        return new(state with { Queue = queue, Index = nextIndex }, original, currentRemoved);
    }

    private static (MusicItem[] Items, int CurrentIndex, MusicItem[] Removed, int AddedCount) InsertNext(
        IReadOnlyList<MusicItem> queue, int index, IReadOnlyList<MusicItem> songs)
    {
        if (songs.Count == 0 || songs.Any(song => song.Kind != "song") ||
            index < -1 || index >= queue.Count || queue.Count > MaximumItems)
            throw new ArgumentException("Select a song from a valid queue.");
        var count = Math.Min(songs.Count, MaximumItems - (index >= 0 ? 1 : 0));
        var excess = Math.Max(0, queue.Count + count - MaximumItems);
        // Trim the upcoming tail first, then oldest played entries. The current
        // occurrence and the new playlist block are never candidates for eviction.
        var trimTail = Math.Min(excess, queue.Count - index - 1);
        var trimHead = excess - trimTail;
        var removed = queue.Take(trimHead).Concat(queue.TakeLast(trimTail)).ToArray();
        var result = queue.Skip(trimHead).Take(queue.Count - excess).ToList();
        index -= trimHead;
        result.InsertRange(index + 1, songs.Take(count));
        return (result.ToArray(), index, removed, count);
    }

    public static MusicQueueAddition AddNext(MusicState state, IReadOnlyList<MusicItem> originalQueue,
        IReadOnlyList<MusicItem> songs)
    {
        // Each insertion gets distinct occurrences, even for repeated song IDs.
        var additions = songs.Take(MaximumItems).Select(song => song with { }).ToArray();
        var current = state.Current;
        var insertion = InsertNext(current is null ? [] : state.Queue, current is null ? -1 : state.Index, additions);
        MusicItem[] original;
        if (current is not null && state.Shuffle)
        {
            var removed = new HashSet<MusicItem>(insertion.Removed, ReferenceEqualityComparer.Instance);
            var retained = originalQueue.Where(item => !removed.Contains(item)).ToList();
            var originalIndex = retained.FindIndex(item => ReferenceEquals(item, current));
            if (originalIndex < 0) throw new InvalidOperationException("The current queue occurrence is missing from the original order.");
            retained.InsertRange(originalIndex + 1, additions.Take(insertion.AddedCount));
            original = retained.ToArray();
        }
        else original = insertion.Items;
        return new(state with { Queue = insertion.Items, Index = current is null ? 0 : insertion.CurrentIndex }, original,
            new(insertion.AddedCount, insertion.Items.Length == MaximumItems));
    }

    public static int Next(int count, int index, string repeat, bool ended) =>
        count == 0 ? -1 : ended && repeat == "one" ? index :
        index + 1 < count ? index + 1 : repeat == "all" ? 0 : -1;

    public static MusicItem[] Shuffled(IReadOnlyList<MusicItem> items, int index, Random random)
    {
        if (index < 0 || index >= items.Count) return items.ToArray();
        var result = items.ToArray();
        for (var i = result.Length - 1; i > index + 1; i--)
        {
            var j = random.Next(index + 1, i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }
}
