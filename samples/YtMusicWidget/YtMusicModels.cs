using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.YtMusicWidget;

public enum YtMusicCommand
{
    TogglePlayback,
    Previous,
    Next,
    Like,
    Dislike,
    Shuffle,
    Repeat,
}

public enum YtMusicRepeatMode
{
    Off,
    All,
    One,
}

public sealed record YtMusicConnectionInfo(bool AuthRequired);

public sealed record YtMusicPairingCode(string Code);

public sealed record YtMusicUpdatePolicy
{
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan OptimisticConfirmationWindow { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (ProgressInterval < WidgetTicker.MinimumInterval || ProgressInterval > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(ProgressInterval));
        if (PollInterval < ProgressInterval || PollInterval > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        if (OptimisticConfirmationWindow < TimeSpan.FromMilliseconds(100) ||
            OptimisticConfirmationWindow > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(OptimisticConfirmationWindow));
    }
}

public sealed record YtMusicPlaybackSnapshot(
    string TrackId,
    string Title,
    string Artist,
    string Album,
    string ArtworkUrl,
    bool IsPlaying,
    bool IsLiked,
    bool IsDisliked,
    double PositionSeconds,
    double DurationSeconds,
    bool? IsShuffleEnabled = null,
    YtMusicRepeatMode? RepeatMode = null)
{
    public static YtMusicPlaybackSnapshot Empty { get; } = new(
        string.Empty,
        "YouTube Music",
        "No track loaded in YTMDesktop2",
        string.Empty,
        string.Empty,
        false,
        false,
        false,
        0,
        0);
}

public interface IYtMusicClient
{
    Task<YtMusicConnectionInfo> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task SendCommandAsync(
        YtMusicCommand command,
        CancellationToken cancellationToken = default,
        bool? toggleState = null);
    Task<YtMusicPairingCode> RequestPairingCodeAsync(CancellationToken cancellationToken = default);
    Task CompletePairingAsync(string code, CancellationToken cancellationToken = default);
}
