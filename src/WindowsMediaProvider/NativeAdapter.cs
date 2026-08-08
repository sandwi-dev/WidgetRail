namespace GameBarAlternative.WindowsMediaProvider;

internal enum NativeMediaPlaybackStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

internal enum NativeMediaCommand
{
    Play,
    Pause,
    TogglePlayPause,
    Previous,
    Next,
}

internal enum NativeMediaControlResult
{
    Succeeded,
    NotSupported,
    NotFound,
    Unavailable,
}

internal sealed record NativeMediaSession(
    string NativeId,
    string AppName,
    string Title,
    string Artist,
    NativeMediaPlaybackStatus PlaybackStatus,
    long PositionMilliseconds,
    long DurationMilliseconds,
    long CapturedAtUnixMilliseconds,
    double PlaybackRate,
    bool IsCurrent,
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanPrevious,
    bool CanNext,
    string? ArtworkPngBase64 = null);

internal interface IWindowsMediaNativeAdapter : IAsyncDisposable
{
    event EventHandler? StateChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NativeMediaSession>> ReadSessionsAsync(
        CancellationToken cancellationToken);
    Task<NativeMediaControlResult> ControlAsync(
        string nativeId,
        NativeMediaCommand command,
        CancellationToken cancellationToken);
}

internal interface IWindowsMediaNativeAdapterFactory
{
    IWindowsMediaNativeAdapter Create();
}
