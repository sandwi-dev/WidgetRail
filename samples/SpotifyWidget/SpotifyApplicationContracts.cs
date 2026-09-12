namespace WidgetRail.Samples.SpotifyWidget;

public static class SpotifyApplicationContract
{
    public const string ExactRedirectUri = "http://127.0.0.1:43827/callback/";
    public const string DeveloperDashboardUri = "https://developer.spotify.com/dashboard";
    public const int MaximumClientIdInputCharacters = 96;
    public const int MaximumClientIdCharacters = 128;
    public const int MaximumQueueItems = 50;
    public const int MaximumCollectionPageSize = 50;
    public const int MaximumCollectionOffset = 100_000;
    public const long MaximumPositionMilliseconds = 604_800_000;
    public static IReadOnlyList<SpotifyAuthorizationScope> RequiredAuthorizationScopes { get; } =
        Array.AsReadOnly(
        [
            SpotifyAuthorizationScope.PlaybackStateRead,
            SpotifyAuthorizationScope.PlaybackStateControl,
            SpotifyAuthorizationScope.LocalPlayback,
            SpotifyAuthorizationScope.PlaylistsRead,
        ]);
}

public sealed record SpotifyConfigurationSummary(bool IsConfigured, string RedirectUri);
public sealed record ConfigureSpotifyClientRequest(string ClientId);

public enum SpotifyAuthorizationState
{
    Unconfigured,
    Disconnected,
    Authorizing,
    Connected,
    ReauthorizationRequired,
}

public enum SpotifyAuthorizationScope
{
    PlaybackStateRead,
    PlaybackStateControl,
    LocalPlayback,
    PlaylistsRead,
    LibraryRead,
    LibraryModify,
    RecentlyPlayedRead,
    UserTopRead,
}

public sealed record SpotifyAuthorizationSummary(
    SpotifyAuthorizationState State,
    IReadOnlyList<SpotifyAuthorizationScope> RequestedScopes,
    IReadOnlyList<SpotifyAuthorizationScope> GrantedScopes,
    string? DisplayMessage);

public sealed record ConnectSpotifyRequest(
    IReadOnlyList<SpotifyAuthorizationScope> RequestedScopes);

public enum SpotifyPlaybackItemType
{
    Track,
    Episode,
}

public enum SpotifyRepeatState
{
    Off,
    Context,
    Track,
}

public sealed record SpotifyPlaybackDisallowedActions(
    bool Pausing,
    bool Resuming,
    bool Seeking,
    bool SkippingNext,
    bool SkippingPrevious,
    bool TogglingRepeatContext,
    bool TogglingRepeatTrack,
    bool TogglingShuffle);

public sealed record SpotifyPlaybackItemSummary(
    SpotifyPlaybackItemType ItemType,
    string Title,
    string Subtitle,
    string? ContextName,
    string? ArtworkUrl,
    string? Uri);

public sealed record SpotifyPlaybackSummary(
    bool IsAvailable,
    bool IsPlaying,
    long ProgressMilliseconds,
    long DurationMilliseconds,
    long CapturedAtUnixMilliseconds,
    SpotifyRepeatState RepeatState,
    bool ShuffleState,
    SpotifyPlaybackItemSummary? Item,
    SpotifyPlaybackDisallowedActions DisallowedActions,
    string Attribution);

public enum SpotifyPlaybackOperation
{
    Play,
    Pause,
    Next,
    Previous,
    Seek,
    SetRepeat,
    SetShuffle,
}

public sealed record SpotifyPlaybackCommand(
    SpotifyPlaybackOperation Operation,
    long? PositionMilliseconds = null,
    SpotifyRepeatState? RepeatState = null,
    bool? Enabled = null);

public enum SpotifyLocalPlaybackState
{
    Disabled,
    Starting,
    Ready,
    Active,
    NotReady,
    AutoplayBlocked,
    ReauthorizationRequired,
    PremiumRequired,
    Unavailable,
    Error,
}

public sealed record SpotifyLocalPlaybackSummary(
    SpotifyLocalPlaybackState State,
    string DeviceName,
    int? VolumePercent,
    string? DisplayMessage);

public enum SpotifyLocalPlaybackOperation
{
    StartAndTransfer,
    Stop,
    SetVolume,
}

public sealed record SpotifyLocalPlaybackCommand(
    SpotifyLocalPlaybackOperation Operation,
    int? VolumePercent = null,
    bool? ContinuePlaying = null);

public sealed record SpotifyDeviceSummary(
    string DeviceId,
    string Name,
    string Type,
    bool IsActive,
    bool IsRestricted,
    bool SupportsVolume,
    int? VolumePercent,
    bool IsLocalHost);

public sealed record SpotifyDevicesSummary(IReadOnlyList<SpotifyDeviceSummary> Devices);

public sealed record TransferSpotifyPlaybackRequest(
    string DeviceId,
    bool ContinuePlaying);

public sealed record SpotifyMediaItemSummary(
    SpotifyPlaybackItemType ItemType,
    string Title,
    string Subtitle,
    long DurationMilliseconds,
    string? ArtworkUrl,
    string Uri,
    string SpotifyUrl,
    bool IsPlayable);

public sealed record SpotifyQueueSummary(
    SpotifyMediaItemSummary? CurrentlyPlaying,
    IReadOnlyList<SpotifyMediaItemSummary> Items,
    bool IsTruncated);

public sealed record AddSpotifyQueueItemRequest(string Uri, string? DeviceId = null);

public sealed record SpotifyPlaylistSummary(
    string PlaylistId,
    string Name,
    string? Description,
    string? ArtworkUrl,
    string SpotifyUrl,
    string Uri,
    string OwnerName,
    bool IsCollaborative,
    bool? IsPublic,
    int ItemCount,
    string? SnapshotId = null);

public sealed record SpotifyPlaylistPageSummary(
    IReadOnlyList<SpotifyPlaylistSummary> Items,
    int Offset,
    int Limit,
    int Total,
    bool HasAuthoritativeWindow = true);

public sealed record SpotifyPlaylistPageRequest(int Offset, int Limit);

public sealed record SpotifyPlaylistItemsRequest(
    string PlaylistId,
    int Offset,
    int Limit);

public sealed record SpotifyPlaylistItemsPageSummary(
    IReadOnlyList<SpotifyMediaItemSummary> Items,
    int Offset,
    int Limit,
    int Total,
    bool HasAuthoritativeWindow = true);

public sealed record StartSpotifyPlaybackRequest(
    string? ContextUri,
    IReadOnlyList<string>? ItemUris,
    string? DeviceId = null,
    int? Offset = null,
    string? OffsetUri = null);

public sealed class SpotifyApplicationException : Exception
{
    public SpotifyApplicationException(
        string code,
        string message,
        Exception? innerException = null) : base(message, innerException) => Code = code;

    public string Code { get; }
}

public interface ISpotifyApplicationService : IAsyncDisposable
{
    ValueTask<SpotifySearchPage> SearchAsync(string query, SpotifySearchKind kind,
        int offset, int limit, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SpotifySearchPage>(new SpotifyApplicationException(
            "search_unavailable", "Search is unavailable. Update the Spotify widget and try again."));
    ValueTask<SpotifyConfigurationSummary> ConfigureClientAsync(
        string clientId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SpotifyConfigurationSummary>(
            new SpotifyApplicationException(
                "setup_unavailable", "Spotify setup is unavailable."));
    ValueTask OpenDeveloperDashboardAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new SpotifyApplicationException(
            "setup_unavailable", "Spotify setup is unavailable."));
    ValueTask CopyRedirectUriAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new SpotifyApplicationException(
            "setup_unavailable", "Spotify setup is unavailable."));
    ValueTask<SpotifyConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyAuthorizationSummary> GetAuthorizationAsync(
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyAuthorizationSummary> ConnectAsync(
        IReadOnlyCollection<SpotifyAuthorizationScope> requestedScopes,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyAuthorizationSummary> DisconnectAsync(
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyPlaybackSummary> GetPlaybackAsync(
        CancellationToken cancellationToken = default);
    ValueTask ControlPlaybackAsync(
        SpotifyPlaybackCommand command,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyDevicesSummary> GetDevicesAsync(
        CancellationToken cancellationToken = default);
    ValueTask TransferPlaybackAsync(
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyQueueSummary> GetQueueAsync(
        CancellationToken cancellationToken = default);
    ValueTask AddToQueueAsync(
        string uri,
        string? deviceId = null,
        CancellationToken cancellationToken = default);
    ValueTask StartPlaybackAsync(
        StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyLocalPlaybackSummary> GetLocalPlaybackAsync(
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyLocalPlaybackSummary> ControlLocalPlaybackAsync(
        SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyPlaylistPageSummary> GetPlaylistsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyPlaylistSummary> GetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken = default);
    ValueTask<SpotifyPlaylistItemsPageSummary> GetPlaylistItemsAsync(
        string playlistId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
