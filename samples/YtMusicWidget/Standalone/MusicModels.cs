namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed record MusicItem(string Id, string Kind, string Title, string Subtitle = "", string Artwork = "");
public sealed record MusicPage(string Title, IReadOnlyList<MusicItem> Items);
public sealed record PlayerState(string TrackId = "", bool Playing = false, bool Buffering = false,
    double Position = 0, double Duration = 0, double Volume = .5, string? Error = null);
public sealed record MusicState(bool Connected, IReadOnlyList<MusicItem> Queue, int Index,
    bool Shuffle, string Repeat, PlayerState Player)
{
    public static MusicState Empty { get; } = new(false, [], -1, false, "off", new());
    public MusicItem? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
}

public interface IMusicService : IAsyncDisposable
{
    MusicState State { get; }
    event Action? Changed;
    Task InitializeAsync(CancellationToken token);
    Task<string> SignInAsync(CancellationToken token);
    Task DisconnectAsync(CancellationToken token);
    Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token);
    Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token);
    Task RadioAsync(MusicItem song, CancellationToken token);
    Task CommandAsync(string command, double? value, CancellationToken token);
}

// Queue movement is independent of page navigation and never wraps unless repeat permits it.
public static class MusicQueue
{
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
