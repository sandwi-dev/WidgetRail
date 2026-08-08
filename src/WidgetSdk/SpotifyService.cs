using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetSdk;

/// <summary>
/// Public Spotify application configuration. A client ID is public OAuth
/// metadata; client secrets and authorization tokens never cross this SDK boundary.
/// </summary>
public sealed record WidgetSpotifyConfigurationSummary(
    [property: JsonRequired] bool IsConfigured,
    [property: JsonRequired] string RedirectUri);

public sealed record ConfigureWidgetSpotifyClientRequest(
    [property: JsonRequired] string ClientId);

public enum WidgetSpotifyAuthorizationState
{
    Unconfigured,
    Disconnected,
    Authorizing,
    Connected,
    ReauthorizationRequired,
}

/// <summary>Closed Spotify user scopes exposed by the v1 widget contract.</summary>
public enum WidgetSpotifyAuthorizationScope
{
    PlaybackStateRead,
    PlaybackStateControl,
}

public sealed record ConnectWidgetSpotifyRequest(
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyAuthorizationScope> RequestedScopes);

public sealed record WidgetSpotifyAuthorizationSummary(
    [property: JsonRequired] WidgetSpotifyAuthorizationState State,
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyAuthorizationScope> RequestedScopes,
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyAuthorizationScope> GrantedScopes,
    string? DisplayMessage);

public enum WidgetSpotifyPlaybackItemType
{
    Track,
    Episode,
}

public enum WidgetSpotifyRepeatState
{
    Off,
    Context,
    Track,
}

/// <summary>
/// Spotify's current-device restrictions. A true property means that the
/// corresponding operation must not be offered by the widget.
/// </summary>
public sealed record WidgetSpotifyPlaybackDisallowedActions(
    [property: JsonRequired] bool Pausing,
    [property: JsonRequired] bool Resuming,
    [property: JsonRequired] bool Seeking,
    [property: JsonRequired] bool SkippingNext,
    [property: JsonRequired] bool SkippingPrevious,
    [property: JsonRequired] bool TogglingRepeatContext,
    [property: JsonRequired] bool TogglingRepeatTrack,
    [property: JsonRequired] bool TogglingShuffle);

/// <summary>Sanitized Spotify metadata; no token or raw Web API document is exposed.</summary>
public sealed record WidgetSpotifyPlaybackItemSummary(
    [property: JsonRequired] WidgetSpotifyPlaybackItemType ItemType,
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Subtitle,
    string? ContextName,
    string? ArtworkUrl,
    string? Uri);

public sealed record WidgetSpotifyPlaybackSummary(
    [property: JsonRequired] bool IsAvailable,
    [property: JsonRequired] bool IsPlaying,
    [property: JsonRequired] long ProgressMilliseconds,
    [property: JsonRequired] long DurationMilliseconds,
    [property: JsonRequired] long CapturedAtUnixMilliseconds,
    [property: JsonRequired] WidgetSpotifyRepeatState RepeatState,
    [property: JsonRequired] bool ShuffleState,
    WidgetSpotifyPlaybackItemSummary? Item,
    [property: JsonRequired] WidgetSpotifyPlaybackDisallowedActions DisallowedActions,
    [property: JsonRequired] string Attribution);

public enum WidgetSpotifyPlaybackOperation
{
    Play,
    Pause,
    Next,
    Previous,
    Seek,
    SetRepeat,
    SetShuffle,
}

public sealed record WidgetSpotifyPlaybackCommand(
    [property: JsonRequired] WidgetSpotifyPlaybackOperation Operation,
    long? PositionMilliseconds = null,
    WidgetSpotifyRepeatState? RepeatState = null,
    bool? Enabled = null);

public sealed record WidgetSpotifyPlaybackChanged(
    [property: JsonRequired] WidgetSpotifyPlaybackSummary Playback);

/// <summary>Exact, versioned Spotify capability contracts understood by the broker.</summary>
public static class WidgetSpotifyCapabilities
{
    public const string ConfigurationCapabilityId = "external.spotify.configuration.v1";
    public const string AuthorizationCapabilityId = "external.spotify.authorization.v1";
    public const string PlaybackReadCapabilityId = "external.spotify.playback.read.v1";
    public const string PlaybackControlCapabilityId = "external.spotify.playback.control.v1";

    public const string ConfigurationGetOperationId = "spotify.configuration.get";
    public const string ConfigurationConfigureOperationId = "spotify.configuration.configure";
    public const string AuthorizationGetOperationId = "spotify.authorization.get";
    public const string AuthorizationConnectOperationId = "spotify.authorization.connect";
    public const string AuthorizationDisconnectOperationId = "spotify.authorization.disconnect";
    public const string PlaybackGetOperationId = "spotify.playback.get";
    public const string PlaybackControlOperationId = "spotify.playback.control";
    public const string PlaybackChangedEventType = "spotify.playback.changed";

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyConfigurationSummary> GetConfiguration { get; } =
        new(ConfigurationCapabilityId, ConfigurationGetOperationId);

    public static WidgetCapabilityOperation<ConfigureWidgetSpotifyClientRequest,
        WidgetSpotifyConfigurationSummary> ConfigureClient { get; } =
        new(ConfigurationCapabilityId, ConfigurationConfigureOperationId);

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyAuthorizationSummary> GetAuthorization { get; } =
        new(AuthorizationCapabilityId, AuthorizationGetOperationId);

    public static WidgetCapabilityOperation<ConnectWidgetSpotifyRequest,
        WidgetSpotifyAuthorizationSummary> Connect { get; } =
        new(AuthorizationCapabilityId, AuthorizationConnectOperationId);

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyAuthorizationSummary> Disconnect { get; } =
        new(AuthorizationCapabilityId, AuthorizationDisconnectOperationId);

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyPlaybackSummary> GetPlayback { get; } =
        new(PlaybackReadCapabilityId, PlaybackGetOperationId);

    public static WidgetCapabilityOperation<WidgetSpotifyPlaybackCommand,
        WidgetCapabilityAcknowledgement> ControlPlayback { get; } =
        new(PlaybackControlCapabilityId, PlaybackControlOperationId);

    public static WidgetCapabilityEvent<WidgetSpotifyPlaybackChanged> PlaybackChanged { get; } =
        new(PlaybackReadCapabilityId, PlaybackChangedEventType);
}

/// <summary>
/// Typed access to the trusted Spotify broker. The service intentionally has
/// no generic HTTP, token, or client-secret API.
/// </summary>
public sealed class WidgetSpotifyService
{
    public const string ExactRedirectUri = "http://127.0.0.1:43827/callback/";
    public const int MaximumClientIdCharacters = 128;
    public const long MaximumPositionMilliseconds = 604_800_000;

    private readonly IWidgetCapabilityClient _client;

    internal WidgetSpotifyService(IWidgetCapabilityClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public bool IsAvailable => _client.IsAvailable;

    public async ValueTask<WidgetSpotifyConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken = default) =>
        ValidateConfiguration(await _client.InvokeAsync(
            WidgetSpotifyCapabilities.GetConfiguration,
            new WidgetCapabilityQuery(), cancellationToken).ConfigureAwait(false));

    public async ValueTask<WidgetSpotifyConfigurationSummary> ConfigureClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        return ValidateConfiguration(await _client.InvokeAsync(
            WidgetSpotifyCapabilities.ConfigureClient,
            new ConfigureWidgetSpotifyClientRequest(clientId), cancellationToken)
            .ConfigureAwait(false));
    }

    public ValueTask<WidgetSpotifyAuthorizationSummary> GetAuthorizationAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetSpotifyCapabilities.GetAuthorization,
            new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetSpotifyAuthorizationSummary> ConnectAsync(
        IReadOnlyCollection<WidgetSpotifyAuthorizationScope> requestedScopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedScopes);
        var normalized = requestedScopes.ToArray();
        if (normalized.Length is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(requestedScopes));
        if (normalized.Any(scope => !Enum.IsDefined(scope)))
            throw new ArgumentOutOfRangeException(nameof(requestedScopes));
        if (normalized.Distinct().Count() != normalized.Length)
            throw new ArgumentException("Spotify authorization scopes must be unique.",
                nameof(requestedScopes));
        Array.Sort(normalized);
        return _client.InvokeAsync(
            WidgetSpotifyCapabilities.Connect,
            new ConnectWidgetSpotifyRequest(normalized), cancellationToken);
    }

    public ValueTask<WidgetSpotifyAuthorizationSummary> DisconnectAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetSpotifyCapabilities.Disconnect,
            new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetSpotifyPlaybackSummary> GetPlaybackAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(
            WidgetSpotifyCapabilities.GetPlayback,
            new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask ControlPlaybackAsync(
        WidgetSpotifyPlaybackCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidatePlaybackCommand(command);
        var response = await _client.InvokeAsync(
            WidgetSpotifyCapabilities.ControlPlayback, command, cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response",
                "The Spotify provider returned an invalid acknowledgement.");
    }

    public ValueTask PlayAsync(CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(new(WidgetSpotifyPlaybackOperation.Play), cancellationToken);

    public ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(new(WidgetSpotifyPlaybackOperation.Pause), cancellationToken);

    public ValueTask NextAsync(CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(new(WidgetSpotifyPlaybackOperation.Next), cancellationToken);

    public ValueTask PreviousAsync(CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(new(WidgetSpotifyPlaybackOperation.Previous), cancellationToken);

    public ValueTask SeekAsync(
        long positionMilliseconds,
        CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(
            new(WidgetSpotifyPlaybackOperation.Seek, PositionMilliseconds: positionMilliseconds),
            cancellationToken);

    public ValueTask SetRepeatAsync(
        WidgetSpotifyRepeatState repeatState,
        CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(
            new(WidgetSpotifyPlaybackOperation.SetRepeat, RepeatState: repeatState),
            cancellationToken);

    public ValueTask SetShuffleAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        ControlPlaybackAsync(
            new(WidgetSpotifyPlaybackOperation.SetShuffle, Enabled: enabled),
            cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetSpotifyPlaybackChanged>>
        OpenPlaybackSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(
            WidgetSpotifyCapabilities.PlaybackChanged, cancellationToken);

    public IAsyncEnumerable<WidgetSpotifyPlaybackChanged> WatchPlaybackAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetSpotifyCapabilities.PlaybackChanged, cancellationToken);

    private static void ValidateClientId(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (clientId.Length > MaximumClientIdCharacters ||
            clientId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException(
                "Spotify client ID must contain at most 128 ASCII letters and digits.",
                nameof(clientId));
    }

    private static WidgetSpotifyConfigurationSummary ValidateConfiguration(
        WidgetSpotifyConfigurationSummary? configuration)
    {
        if (configuration is null ||
            !string.Equals(configuration.RedirectUri, ExactRedirectUri,
                StringComparison.Ordinal))
            throw new WidgetCapabilityException(
                "malformed_response",
                "The Spotify provider returned an invalid configuration summary.");
        return configuration;
    }

    private static void ValidatePlaybackCommand(WidgetSpotifyPlaybackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Operation))
            throw new ArgumentOutOfRangeException(nameof(command));
        var valid = command.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play or WidgetSpotifyPlaybackOperation.Pause or
                WidgetSpotifyPlaybackOperation.Next or WidgetSpotifyPlaybackOperation.Previous =>
                command.PositionMilliseconds is null && command.RepeatState is null &&
                command.Enabled is null,
            WidgetSpotifyPlaybackOperation.Seek =>
                command.PositionMilliseconds is >= 0 and <= MaximumPositionMilliseconds &&
                command.RepeatState is null && command.Enabled is null,
            WidgetSpotifyPlaybackOperation.SetRepeat =>
                command.PositionMilliseconds is null &&
                command.RepeatState is { } repeatState && Enum.IsDefined(repeatState) &&
                command.Enabled is null,
            WidgetSpotifyPlaybackOperation.SetShuffle =>
                command.PositionMilliseconds is null && command.RepeatState is null &&
                command.Enabled is not null,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException("Spotify playback command is invalid.", nameof(command));
    }
}
