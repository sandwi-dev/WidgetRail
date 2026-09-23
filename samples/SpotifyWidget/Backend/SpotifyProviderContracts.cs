namespace WidgetRail.WindowsSpotifyProvider;

public readonly record struct SpotifyIntegrationIdentity(string PublisherId, string PackageId)
{
    internal string Authority
    {
        get
        {
            ValidatePart(PublisherId, nameof(PublisherId));
            ValidatePart(PackageId, nameof(PackageId));
            return $"{PublisherId}\n{PackageId}";
        }
    }

    private static void ValidatePart(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 128 || value.Any(character =>
                character is < '!' or > '~'))
            throw new ArgumentException("Spotify integration identity is invalid.", name);
    }
}

public sealed record SpotifyProviderConfiguration(bool IsConfigured, string RedirectUri);

public sealed record SpotifyProviderAuthorization(
    bool IsConfigured,
    bool IsConnected,
    string Message,
    IReadOnlyList<string>? GrantedScopes = null,
    bool NeedsReconsent = false,
    bool IsAuthorizing = false,
    string? CachePartitionId = null);

/// <summary>
/// Host-process-only token lease for a trusted Spotify playback component.
/// This type is intentionally absent from PlatformBroker and WidgetSdk.
/// </summary>
public sealed record TrustedHostSpotifyAccessToken(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> GrantedScopes);

public sealed record SpotifyProviderPlaybackActions(
    bool CanPause,
    bool CanResume,
    bool CanSeek,
    bool CanSkipNext,
    bool CanSkipPrevious,
    bool CanSetRepeatContext,
    bool CanSetRepeatTrack,
    bool CanSetShuffle);

public sealed record SpotifyProviderPlayback(
    bool IsAvailable,
    bool IsPlaying,
    long ProgressMilliseconds,
    long DurationMilliseconds,
    string ItemType,
    string Title,
    string Subtitle,
    string? ContextName,
    string? ArtworkUrl,
    string? SpotifyUri,
    string RepeatState,
    bool ShuffleState,
    int? VolumePercent,
    SpotifyProviderPlaybackActions Actions,
    string Attribution = "Spotify")
{
    public string? DeviceId { get; init; }
}

public enum SpotifyProviderPlaybackOperation
{
    Play,
    Pause,
    Next,
    Previous,
    Seek,
    SetRepeat,
    SetShuffle,
    SetVolume,
}

public sealed record SpotifyProviderPlaybackCommand(
    SpotifyProviderPlaybackOperation Operation,
    long? PositionMilliseconds = null,
    string? RepeatState = null,
    bool? Enabled = null,
    int? VolumePercent = null);

public sealed class SpotifyProviderException : Exception
{
    public SpotifyProviderException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed record SpotifyClientConfiguration(string ClientId);
internal sealed record SpotifyAccessToken(
    string Value, DateTimeOffset ExpiresAt, IReadOnlySet<string> GrantedScopes);
internal sealed record SpotifyTokenResponse(
    string AccessToken,
    TimeSpan Lifetime,
    string? RefreshToken,
    IReadOnlySet<string> GrantedScopes);
internal sealed record SpotifyRefreshCredential(
    string ClientId, string RefreshToken, IReadOnlySet<string> GrantedScopes,
    string? CachePartitionId = null);

internal sealed record SpotifyAuthorizationCallback(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription);

internal sealed record SpotifyHttpRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? FormBody = null,
    string? JsonBody = null);

internal sealed record SpotifyHttpResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

public interface ISpotifyClientConfigurationStore
{
    Task<SpotifyClientConfiguration?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken);
    Task WriteAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyClientConfiguration configuration,
        CancellationToken cancellationToken);
}

internal interface ISpotifyTokenVault
{
    Task<SpotifyRefreshCredential?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken);
    Task SaveAsync(
        SpotifyIntegrationIdentity identity, SpotifyRefreshCredential credential,
        CancellationToken cancellationToken);
    Task DeleteAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken);
}

internal interface ISpotifyHttpTransport : IAsyncDisposable
{
    Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request, CancellationToken cancellationToken);
}

internal interface ISpotifyBrowserLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken);
}

internal interface ISpotifyAuthorizationCallbackReceiver
{
    Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, string expectedState, TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface ISpotifyDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
