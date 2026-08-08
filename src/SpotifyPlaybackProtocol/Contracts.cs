using System.Text.Json;

namespace GameBarAlternative.SpotifyPlayback;

public static class SpotifyPlaybackProtocol
{
    public const int Version = 1;
    public const int MaximumMessageCharacters = 32_768;
    public const int MaximumRequestIdCharacters = 64;
    public const int MaximumDeviceNameCharacters = 64;
    public const int MaximumTokenCharacters = 8_192;
    public const int MaximumPendingCommands = 64;
    public const long MaximumSeekMilliseconds = 604_800_000;
    public const string RequiredScope = "streaming";
}

public enum SpotifyPlaybackLifecycleState
{
    LoadingSdk,
    Disconnected,
    Connecting,
    Ready,
    NotReady,
    Disconnecting,
    Faulted,
    Stopped,
}

public enum SpotifyPlaybackSignal
{
    SdkLoaded,
    ConnectRequested,
    Ready,
    NotReady,
    DisconnectRequested,
    Disconnected,
    InitializationError,
    AuthenticationError,
    AccountError,
    PlaybackError,
    AutoplayFailed,
    Shutdown,
}

public sealed record SpotifyPlaybackTransition(
    SpotifyPlaybackLifecycleState Previous,
    SpotifyPlaybackLifecycleState Current,
    SpotifyPlaybackSignal Signal,
    bool Changed);

public sealed record SpotifyPlaybackPreconditions(
    bool IsPremiumAccount,
    IReadOnlyList<string> GrantedScopes)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(GrantedScopes);
        if (!IsPremiumAccount)
            throw new SpotifyPlaybackProtocolException(
                "premium_required", "Spotify Premium is required for local playback.");
        if (GrantedScopes.Count is < 1 or > 16 ||
            GrantedScopes.Any(scope => string.IsNullOrWhiteSpace(scope) || scope.Length > 64))
            throw new SpotifyPlaybackProtocolException(
                "invalid_scopes", "The granted Spotify scopes are invalid.");
        if (!GrantedScopes.Contains(SpotifyPlaybackProtocol.RequiredScope,
                StringComparer.Ordinal))
            throw new SpotifyPlaybackProtocolException(
                "streaming_scope_required",
                "The Spotify streaming scope is required for local playback.");
    }
}

public sealed record SpotifyPlaybackConnectOptions(
    string DeviceName,
    double InitialVolume,
    SpotifyPlaybackPreconditions Preconditions)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceName);
        if (DeviceName.Length > SpotifyPlaybackProtocol.MaximumDeviceNameCharacters ||
            DeviceName.Any(char.IsControl))
            throw new SpotifyPlaybackProtocolException(
                "invalid_device_name", "The Spotify device name is invalid.");
        if (!double.IsFinite(InitialVolume) || InitialVolume is < 0 or > 1)
            throw new SpotifyPlaybackProtocolException(
                "invalid_volume",
                "The Spotify playback volume must be between zero and one.");
        ArgumentNullException.ThrowIfNull(Preconditions);
        Preconditions.Validate();
    }
}

/// <summary>A short-lived secret which deliberately renders as redacted.</summary>
public sealed class SpotifyAccessTokenLease
{
    public SpotifyAccessTokenLease(
        string accessToken,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> grantedScopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (accessToken.Length > SpotifyPlaybackProtocol.MaximumTokenCharacters ||
            accessToken.Any(character => character is < '!' or > '~'))
            throw new SpotifyPlaybackProtocolException(
                "invalid_token", "The Spotify access-token lease is invalid.");
        ArgumentNullException.ThrowIfNull(grantedScopes);
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
        GrantedScopes = grantedScopes.ToArray();
    }

    public string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }
    public IReadOnlyList<string> GrantedScopes { get; }
    public override string ToString() => "<redacted Spotify access-token lease>";

    public void Validate(DateTimeOffset now)
    {
        if (ExpiresAt <= now)
            throw new SpotifyPlaybackProtocolException(
                "expired_token", "The Spotify access-token lease has expired.");
        new SpotifyPlaybackPreconditions(true, GrantedScopes).Validate();
    }
}

public sealed record SpotifyPlaybackDisallows(
    bool Pausing,
    bool Resuming,
    bool Seeking,
    bool SkippingNext,
    bool SkippingPrevious);

public sealed record SpotifyPlaybackTrack(
    string? Uri,
    string? Id,
    string Type,
    string MediaType,
    string Name,
    bool IsPlayable,
    string? AlbumName,
    string? ArtworkUrl,
    IReadOnlyList<string> Artists);

public sealed record SpotifyLocalPlaybackState(
    bool IsAvailable,
    bool Paused,
    long PositionMilliseconds,
    long DurationMilliseconds,
    int RepeatMode,
    bool Shuffle,
    SpotifyPlaybackDisallows Disallows,
    SpotifyPlaybackTrack? CurrentTrack);

public sealed record SpotifyPlaybackRequest(
    int Version,
    string RequestId,
    string Type,
    JsonElement Payload);

public sealed record SpotifyPlaybackEvent(
    int Version,
    string Type,
    string? RequestId,
    object? Payload);

/// <summary>A validated event received by the trusted parent process.</summary>
public sealed record SpotifyPlaybackEventEnvelope(
    int Version,
    string Type,
    string? RequestId,
    JsonElement Payload);

public sealed class SpotifyPlaybackProtocolException : Exception
{
    public SpotifyPlaybackProtocolException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
