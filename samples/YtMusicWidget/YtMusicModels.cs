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

public sealed record YtMusicConnectionInfo(bool AuthRequired, bool HasCredential = false);

public sealed record YtMusicPairingCode(string Code);

public sealed record YtMusicUpdatePolicy
{
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan OptimisticConfirmationWindow { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TransportConfirmationWindow { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan TransportRefreshInitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan TransportRefreshDelayStep { get; init; } = TimeSpan.FromMilliseconds(100);
    public int TransportRefreshAttempts { get; init; } = 5;

    internal void Validate()
    {
        if (ProgressInterval < WidgetTicker.MinimumInterval || ProgressInterval > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(ProgressInterval));
        if (PollInterval < ProgressInterval || PollInterval > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        if (OptimisticConfirmationWindow < TimeSpan.FromMilliseconds(100) ||
            OptimisticConfirmationWindow > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(OptimisticConfirmationWindow));
        if (TransportConfirmationWindow < OptimisticConfirmationWindow ||
            TransportConfirmationWindow > TimeSpan.FromSeconds(15))
            throw new ArgumentOutOfRangeException(nameof(TransportConfirmationWindow));
        if (TransportRefreshInitialDelay < TimeSpan.FromMilliseconds(10) ||
            TransportRefreshInitialDelay > TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(TransportRefreshInitialDelay));
        if (TransportRefreshDelayStep < TimeSpan.Zero ||
            TransportRefreshDelayStep > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(TransportRefreshDelayStep));
        if (TransportRefreshAttempts is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(TransportRefreshAttempts));
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
    YtMusicRepeatMode? RepeatMode = null,
    string MetadataTrackId = "",
    bool HasCompleteMetadata = true)
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
        0,
        MetadataTrackId: string.Empty,
        HasCompleteMetadata: false);
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
    void ClearCredential();
}

/// <summary>
/// Durable token storage owned by the trusted desktop worker. Implementations
/// must not expose the token through widget view state, logs, or exceptions.
/// </summary>
public interface IYtMusicCredentialStore
{
    string? LoadToken();
    void SaveToken(string token);
    void ClearToken();
}

/// <summary>Raised when YTMDesktop2 rejects the current bearer credential.</summary>
public sealed class YtMusicAuthorizationRequiredException : InvalidOperationException
{
    public YtMusicAuthorizationRequiredException()
        : base("YTMDesktop2 authorization is required.")
    {
    }
}
