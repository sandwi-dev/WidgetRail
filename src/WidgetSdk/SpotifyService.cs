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
    LocalPlayback,
    PlaylistsRead,
    LibraryRead,
    LibraryModify,
    RecentlyPlayedRead,
    UserTopRead,
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

public enum WidgetSpotifyLocalPlaybackState
{
    Disabled,
    Starting,
    Ready,
    Active,
    NotReady,
    ReauthorizationRequired,
    PremiumRequired,
    Unavailable,
    Error,
}

/// <summary>
/// Sanitized state for the trusted singleton Web Playback SDK host. The SDK's
/// access token and Spotify device ID never cross the widget boundary.
/// </summary>
public sealed record WidgetSpotifyLocalPlaybackSummary(
    [property: JsonRequired] WidgetSpotifyLocalPlaybackState State,
    [property: JsonRequired] string DeviceName,
    int? VolumePercent,
    string? DisplayMessage);

public enum WidgetSpotifyLocalPlaybackOperation
{
    StartAndTransfer,
    Stop,
    SetVolume,
}

public sealed record WidgetSpotifyLocalPlaybackCommand(
    [property: JsonRequired] WidgetSpotifyLocalPlaybackOperation Operation,
    int? VolumePercent = null,
    bool? ContinuePlaying = null);

public sealed record WidgetSpotifyDeviceSummary(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Type,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] bool IsRestricted,
    [property: JsonRequired] bool SupportsVolume,
    int? VolumePercent,
    [property: JsonRequired] bool IsLocalHost);

public sealed record WidgetSpotifyDevicesSummary(
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyDeviceSummary> Devices);

public sealed record TransferWidgetSpotifyPlaybackRequest(
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] bool ContinuePlaying);

/// <summary>A bounded track or episode used by queue and collection surfaces.</summary>
public sealed record WidgetSpotifyMediaItemSummary(
    [property: JsonRequired] WidgetSpotifyPlaybackItemType ItemType,
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Subtitle,
    [property: JsonRequired] long DurationMilliseconds,
    string? ArtworkUrl,
    [property: JsonRequired] string Uri,
    [property: JsonRequired] string SpotifyUrl,
    [property: JsonRequired] bool IsPlayable);

public sealed record WidgetSpotifyQueueSummary(
    WidgetSpotifyMediaItemSummary? CurrentlyPlaying,
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyMediaItemSummary> Items,
    [property: JsonRequired] bool IsTruncated);

public sealed record AddWidgetSpotifyQueueItemRequest(
    [property: JsonRequired] string Uri,
    string? DeviceId = null);

public sealed record WidgetSpotifyPlaylistSummary(
    [property: JsonRequired] string PlaylistId,
    [property: JsonRequired] string Name,
    string? Description,
    string? ArtworkUrl,
    [property: JsonRequired] string SpotifyUrl,
    [property: JsonRequired] string Uri,
    [property: JsonRequired] string OwnerName,
    [property: JsonRequired] bool IsCollaborative,
    bool? IsPublic,
    [property: JsonRequired] int ItemCount);

public sealed record WidgetSpotifyPlaylistPageRequest(
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit);

public sealed record WidgetSpotifyPlaylistPageSummary(
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyPlaylistSummary> Items,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit,
    [property: JsonRequired] int Total);

public sealed record WidgetSpotifyPlaylistItemsRequest(
    [property: JsonRequired] string PlaylistId,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit);

public sealed record WidgetSpotifyPlaylistItemsSummary(
    [property: JsonRequired] WidgetSpotifyPlaylistSummary Playlist,
    [property: JsonRequired] IReadOnlyList<WidgetSpotifyMediaItemSummary> Items,
    [property: JsonRequired] int Offset,
    [property: JsonRequired] int Limit,
    [property: JsonRequired] int Total);

public sealed record StartWidgetSpotifyPlaybackRequest(
    string? ContextUri,
    IReadOnlyList<string>? ItemUris,
    string? DeviceId = null,
    int? Offset = null);

/// <summary>Exact, versioned Spotify capability contracts understood by the broker.</summary>
public static class WidgetSpotifyCapabilities
{
    public const string ConfigurationCapabilityId = "external.spotify.configuration.v1";
    public const string AuthorizationCapabilityId = "external.spotify.authorization.v1";
    public const string PlaybackReadCapabilityId = "external.spotify.playback.read.v1";
    public const string PlaybackControlCapabilityId = "external.spotify.playback.control.v1";
    public const string LocalPlaybackCapabilityId = "external.spotify.local-playback.v1";
    public const string PlaylistsReadCapabilityId = "external.spotify.playlists.read.v1";

    public const string ConfigurationGetOperationId = "spotify.configuration.get";
    public const string ConfigurationConfigureOperationId = "spotify.configuration.configure";
    public const string AuthorizationGetOperationId = "spotify.authorization.get";
    public const string AuthorizationConnectOperationId = "spotify.authorization.connect";
    public const string AuthorizationDisconnectOperationId = "spotify.authorization.disconnect";
    public const string PlaybackGetOperationId = "spotify.playback.get";
    public const string PlaybackControlOperationId = "spotify.playback.control";
    public const string PlaybackDevicesGetOperationId = "spotify.playback.devices.get";
    public const string PlaybackTransferOperationId = "spotify.playback.transfer";
    public const string PlaybackQueueGetOperationId = "spotify.playback.queue.get";
    public const string PlaybackQueueAddOperationId = "spotify.playback.queue.add";
    public const string PlaybackStartOperationId = "spotify.playback.start";
    public const string LocalPlaybackGetOperationId = "spotify.local-playback.get";
    public const string LocalPlaybackControlOperationId = "spotify.local-playback.control";
    public const string PlaylistsGetOperationId = "spotify.playlists.get";
    public const string PlaylistItemsGetOperationId = "spotify.playlists.items.get";
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

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyDevicesSummary> GetDevices { get; } =
        new(PlaybackReadCapabilityId, PlaybackDevicesGetOperationId);

    public static WidgetCapabilityOperation<TransferWidgetSpotifyPlaybackRequest,
        WidgetCapabilityAcknowledgement> TransferPlayback { get; } =
        new(PlaybackControlCapabilityId, PlaybackTransferOperationId);

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyQueueSummary> GetQueue { get; } =
        new(PlaybackReadCapabilityId, PlaybackQueueGetOperationId);

    public static WidgetCapabilityOperation<AddWidgetSpotifyQueueItemRequest,
        WidgetCapabilityAcknowledgement> AddToQueue { get; } =
        new(PlaybackControlCapabilityId, PlaybackQueueAddOperationId);

    public static WidgetCapabilityOperation<StartWidgetSpotifyPlaybackRequest,
        WidgetCapabilityAcknowledgement> StartPlayback { get; } =
        new(PlaybackControlCapabilityId, PlaybackStartOperationId);

    public static WidgetCapabilityOperation<WidgetCapabilityQuery,
        WidgetSpotifyLocalPlaybackSummary> GetLocalPlayback { get; } =
        new(LocalPlaybackCapabilityId, LocalPlaybackGetOperationId);

    public static WidgetCapabilityOperation<WidgetSpotifyLocalPlaybackCommand,
        WidgetSpotifyLocalPlaybackSummary> ControlLocalPlayback { get; } =
        new(LocalPlaybackCapabilityId, LocalPlaybackControlOperationId);

    public static WidgetCapabilityOperation<WidgetSpotifyPlaylistPageRequest,
        WidgetSpotifyPlaylistPageSummary> GetPlaylists { get; } =
        new(PlaylistsReadCapabilityId, PlaylistsGetOperationId);

    public static WidgetCapabilityOperation<WidgetSpotifyPlaylistItemsRequest,
        WidgetSpotifyPlaylistItemsSummary> GetPlaylistItems { get; } =
        new(PlaylistsReadCapabilityId, PlaylistItemsGetOperationId);

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
    public const int MaximumCollectionPageSize = 50;
    public const int MaximumCollectionOffset = 100_000;

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
        if (normalized.Length is < 1 or > 8)
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

    public ValueTask<WidgetSpotifyDevicesSummary> GetDevicesAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetSpotifyCapabilities.GetDevices,
            new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask TransferPlaybackAsync(
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken = default)
    {
        ValidateOpaqueSpotifyId(deviceId, nameof(deviceId));
        await RequireAcknowledgementAsync(
            WidgetSpotifyCapabilities.TransferPlayback,
            new TransferWidgetSpotifyPlaybackRequest(deviceId, continuePlaying),
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<WidgetSpotifyQueueSummary> GetQueueAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetSpotifyCapabilities.GetQueue,
            new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask AddToQueueAsync(
        string uri,
        string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSpotifyUri(uri, nameof(uri));
        if (deviceId is not null) ValidateOpaqueSpotifyId(deviceId, nameof(deviceId));
        await RequireAcknowledgementAsync(
            WidgetSpotifyCapabilities.AddToQueue,
            new AddWidgetSpotifyQueueItemRequest(uri, deviceId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StartPlaybackAsync(
        StartWidgetSpotifyPlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateStartPlayback(request);
        await RequireAcknowledgementAsync(
            WidgetSpotifyCapabilities.StartPlayback, request, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<WidgetSpotifyLocalPlaybackSummary> GetLocalPlaybackAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetSpotifyCapabilities.GetLocalPlayback,
            new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetSpotifyLocalPlaybackSummary> ControlLocalPlaybackAsync(
        WidgetSpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateLocalPlaybackCommand(command);
        return _client.InvokeAsync(
            WidgetSpotifyCapabilities.ControlLocalPlayback, command, cancellationToken);
    }

    public ValueTask<WidgetSpotifyPlaylistPageSummary> GetPlaylistsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);
        return _client.InvokeAsync(
            WidgetSpotifyCapabilities.GetPlaylists,
            new WidgetSpotifyPlaylistPageRequest(offset, limit), cancellationToken);
    }

    public ValueTask<WidgetSpotifyPlaylistItemsSummary> GetPlaylistItemsAsync(
        string playlistId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateOpaqueSpotifyId(playlistId, nameof(playlistId));
        ValidatePage(offset, limit);
        return _client.InvokeAsync(
            WidgetSpotifyCapabilities.GetPlaylistItems,
            new WidgetSpotifyPlaylistItemsRequest(playlistId, offset, limit),
            cancellationToken);
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

    private async ValueTask RequireAcknowledgementAsync<TRequest>(
        WidgetCapabilityOperation<TRequest, WidgetCapabilityAcknowledgement> operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _client.InvokeAsync(operation, request, cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response",
                "The Spotify provider returned an invalid acknowledgement.");
    }

    private static void ValidateLocalPlaybackCommand(WidgetSpotifyLocalPlaybackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Operation))
            throw new ArgumentOutOfRangeException(nameof(command));
        var valid = command.Operation switch
        {
            WidgetSpotifyLocalPlaybackOperation.StartAndTransfer =>
                command.VolumePercent is null && command.ContinuePlaying is not null,
            WidgetSpotifyLocalPlaybackOperation.Stop =>
                command.VolumePercent is null && command.ContinuePlaying is null,
            WidgetSpotifyLocalPlaybackOperation.SetVolume =>
                command.VolumePercent is >= 0 and <= 100 && command.ContinuePlaying is null,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException(
                "Spotify local-playback command is invalid.", nameof(command));
    }

    private static void ValidateStartPlayback(StartWidgetSpotifyPlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.ContextUri is null) == (request.ItemUris is null) ||
            request.ItemUris is { Count: < 1 or > 50 } || request.Offset is < 0 ||
            request.Offset is not null && request.ContextUri is null)
            throw new ArgumentException("Spotify playback selection is invalid.", nameof(request));
        if (request.ContextUri is not null)
            ValidateSpotifyUri(request.ContextUri, nameof(request));
        if (request.ItemUris is not null)
        {
            if (request.ItemUris.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Spotify playback URI is invalid.", nameof(request));
            foreach (var uri in request.ItemUris) ValidateSpotifyUri(uri, nameof(request));
        }
        if (request.DeviceId is not null)
            ValidateOpaqueSpotifyId(request.DeviceId, nameof(request));
    }

    private static void ValidateSpotifyUri(string uri, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, parameterName);
        if (uri.Length > 2_048 || uri.Any(char.IsControl) ||
            !uri.StartsWith("spotify:", StringComparison.Ordinal) ||
            uri.Count(character => character == ':') < 2)
            throw new ArgumentException("Spotify URI is invalid.", parameterName);
    }

    private static void ValidateOpaqueSpotifyId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("Spotify identifier is invalid.", parameterName);
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset is < 0 or > MaximumCollectionOffset ||
            limit is < 1 or > MaximumCollectionPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
