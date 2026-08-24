using System.Globalization;
using System.Text.Json;
using WidgetRail.Samples.SpotifyWidget;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed record SpotifyAuthorizedRequest(
    HttpMethod Method,
    Uri Uri,
    string RequiredScope,
    string? AdditionalRequiredScope = null,
    string? JsonBody = null,
    SpotifyQueueDiagnosticContext? QueueDiagnostic = null);

internal readonly record struct SpotifyQueueDiagnosticContext(
    long Operation,
    long Generation);

/// <summary>
/// Narrow token-session boundary. Endpoint families can request only an exact
/// Spotify method/URI/scope/body; the backend remains the sole identity, vault,
/// access-token, 401-refresh, retry, and cancellation authority.
/// </summary>
internal interface ISpotifyAuthorizedRequestSender
{
    Task<SpotifyHttpResponse> SendAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyAuthorizedRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SpotifyAuthorizedRequestSender(
    Func<SpotifyIntegrationIdentity, SpotifyAuthorizedRequest, CancellationToken,
        Task<SpotifyHttpResponse>> send) : ISpotifyAuthorizedRequestSender
{
    private readonly Func<SpotifyIntegrationIdentity, SpotifyAuthorizedRequest,
        CancellationToken, Task<SpotifyHttpResponse>> _send =
        send ?? throw new ArgumentNullException(nameof(send));

    public Task<SpotifyHttpResponse> SendAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyAuthorizedRequest request,
        CancellationToken cancellationToken) =>
        _send(identity, request, cancellationToken);
}

internal sealed class SpotifyPlaybackApi(ISpotifyAuthorizedRequestSender sender)
{
    internal const int MaximumDevices = 64;
    private static readonly Uri PlayerUri = new("https://api.spotify.com/v1/me/player");
    private static readonly Uri DevicesUri = new("https://api.spotify.com/v1/me/player/devices");
    private static readonly Uri QueueUri = new("https://api.spotify.com/v1/me/player/queue");
    private readonly ISpotifyAuthorizedRequestSender _sender =
        sender ?? throw new ArgumentNullException(nameof(sender));

    internal async Task<SpotifyProviderPlayback> GetPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(identity, HttpMethod.Get, PlayerUri,
            WindowsSpotifyPlatformBackend.PlaybackReadScope, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 204) return SpotifyResponseParser.UnavailablePlayback();
        SpotifyResponseParser.EnsureSuccess(response);
        return SpotifyResponseParser.ParsePlayback(response.Body);
    }

    internal async Task ControlPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyProviderPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (method, uri) = BuildControlRequest(command);
        var response = await SendAsync(identity, method, uri,
            WindowsSpotifyPlatformBackend.PlaybackControlScope, cancellationToken)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response, allowNoContent: true);
    }

    internal async Task<SpotifyDevicesSummary> GetDevicesAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(identity, HttpMethod.Get, DevicesUri,
            WindowsSpotifyPlatformBackend.PlaybackReadScope, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 204) return new SpotifyDevicesSummary([]);
        SpotifyResponseParser.EnsureSuccess(response);
        return SpotifyResponseParser.ParseDevices(response.Body);
    }

    internal async Task TransferPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidateOpaqueId(deviceId, "device identifier");
        var body = JsonSerializer.Serialize(new
        {
            device_ids = new[] { deviceId },
            play = continuePlaying,
        });
        var response = await SendAsync(identity, HttpMethod.Put, PlayerUri,
            WindowsSpotifyPlatformBackend.PlaybackControlScope, cancellationToken, body)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response, allowNoContent: true);
    }

    internal async Task<SpotifyQueueSummary> GetQueueAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyQueueDiagnosticContext? diagnostic,
        CancellationToken cancellationToken)
    {
        var response = await _sender.SendAsync(identity,
            new SpotifyAuthorizedRequest(
                HttpMethod.Get,
                QueueUri,
                WindowsSpotifyPlatformBackend.PlaybackReadScope,
                QueueDiagnostic: diagnostic),
            cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 204) return new SpotifyQueueSummary(null, [], false);
        SpotifyResponseParser.EnsureSuccess(response);
        return SpotifyResponseParser.ParseQueue(response.Body);
    }

    internal async Task AddQueueItemAsync(
        SpotifyIntegrationIdentity identity,
        string uri,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidateSpotifyUri(uri);
        if (deviceId is not null)
            SpotifyApiValidation.ValidateOpaqueId(deviceId, "device identifier");
        var query = "?uri=" + Uri.EscapeDataString(uri) +
            (deviceId is null ? string.Empty :
                "&device_id=" + Uri.EscapeDataString(deviceId));
        var response = await SendAsync(identity, HttpMethod.Post,
            new Uri(QueueUri + query), WindowsSpotifyPlatformBackend.PlaybackControlScope,
            cancellationToken).ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response, allowNoContent: true);
    }

    internal async Task StartPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        string? deviceId,
        string? contextUri,
        IReadOnlyList<string>? itemUris,
        int? offset,
        string? offsetUri,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidateStartPlayback(
            deviceId, contextUri, itemUris, offset, offsetUri);
        var uri = new Uri("https://api.spotify.com/v1/me/player/play" +
            (deviceId is null ? string.Empty :
                "?device_id=" + Uri.EscapeDataString(deviceId)));
        string body;
        if (contextUri is not null)
        {
            body = offsetUri is not null
                ? JsonSerializer.Serialize(new
                {
                    context_uri = contextUri,
                    offset = new { uri = offsetUri },
                })
                : offset is not null
                    ? JsonSerializer.Serialize(new
                    {
                        context_uri = contextUri,
                        offset = new { position = offset },
                    })
                    : JsonSerializer.Serialize(new { context_uri = contextUri });
        }
        else
        {
            body = JsonSerializer.Serialize(new { uris = itemUris });
        }
        var response = await SendAsync(identity, HttpMethod.Put, uri,
            WindowsSpotifyPlatformBackend.PlaybackControlScope, cancellationToken, body)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response, allowNoContent: true);
    }

    private Task<SpotifyHttpResponse> SendAsync(
        SpotifyIntegrationIdentity identity,
        HttpMethod method,
        Uri uri,
        string requiredScope,
        CancellationToken cancellationToken,
        string? jsonBody = null) => _sender.SendAsync(identity,
            new SpotifyAuthorizedRequest(method, uri, requiredScope, JsonBody: jsonBody),
            cancellationToken);

    private static (HttpMethod Method, Uri Uri) BuildControlRequest(
        SpotifyProviderPlaybackCommand command)
    {
        var (method, path) = command.Operation switch
        {
            SpotifyProviderPlaybackOperation.Play => (HttpMethod.Put, "/v1/me/player/play"),
            SpotifyProviderPlaybackOperation.Pause => (HttpMethod.Put, "/v1/me/player/pause"),
            SpotifyProviderPlaybackOperation.Next => (HttpMethod.Post, "/v1/me/player/next"),
            SpotifyProviderPlaybackOperation.Previous =>
                (HttpMethod.Post, "/v1/me/player/previous"),
            SpotifyProviderPlaybackOperation.Seek => (HttpMethod.Put,
                "/v1/me/player/seek?position_ms=" + SpotifyApiValidation.RequiredRange(
                    command.PositionMilliseconds, 0, int.MaxValue, "position_ms")),
            SpotifyProviderPlaybackOperation.SetRepeat => (HttpMethod.Put,
                "/v1/me/player/repeat?state=" +
                SpotifyApiValidation.ValidateRepeat(command.RepeatState)),
            SpotifyProviderPlaybackOperation.SetShuffle => (HttpMethod.Put,
                "/v1/me/player/shuffle?state=" +
                SpotifyApiValidation.RequiredBoolean(command.Enabled, "state")),
            SpotifyProviderPlaybackOperation.SetVolume => (HttpMethod.Put,
                "/v1/me/player/volume?volume_percent=" +
                SpotifyApiValidation.RequiredRange(
                    command.VolumePercent, 0, 100, "volume_percent")),
            _ => throw new SpotifyProviderException(
                "invalid_request", "Spotify playback command is invalid."),
        };
        return (method, new Uri("https://api.spotify.com" + path));
    }
}

internal sealed class SpotifyCollectionApi(ISpotifyAuthorizedRequestSender sender)
{
    private static readonly Uri PlaylistsUri = new("https://api.spotify.com/v1/me/playlists");
    private readonly ISpotifyAuthorizedRequestSender _sender =
        sender ?? throw new ArgumentNullException(nameof(sender));

    internal async Task<SpotifyPlaylistPageSummary> GetPlaylistsAsync(
        SpotifyIntegrationIdentity identity,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidatePage(offset, limit);
        var uri = new Uri(PlaylistsUri + "?offset=" + offset.ToString(
            CultureInfo.InvariantCulture) + "&limit=" + limit.ToString(
                CultureInfo.InvariantCulture));
        var response = await SendAsync(identity, uri, cancellationToken)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response);
        return SpotifyResponseParser.ParsePlaylistPage(response.Body, offset, limit);
    }

    internal async Task<SpotifyPlaylistSummary> GetPlaylistAsync(
        SpotifyIntegrationIdentity identity,
        string playlistId,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidateOpaqueId(playlistId, "playlist identifier");
        var baseUri = "https://api.spotify.com/v1/playlists/" +
            Uri.EscapeDataString(playlistId);
        var playlistResponse = await SendAsync(identity, new Uri(baseUri), cancellationToken)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(playlistResponse);
        return SpotifyResponseParser.ParsePlaylistDocument(playlistResponse.Body);
    }

    internal async Task<SpotifyPlaylistItemsPageSummary> GetPlaylistItemsAsync(
        SpotifyIntegrationIdentity identity,
        string playlistId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        SpotifyApiValidation.ValidateOpaqueId(playlistId, "playlist identifier");
        SpotifyApiValidation.ValidatePage(offset, limit);
        var baseUri = "https://api.spotify.com/v1/playlists/" +
            Uri.EscapeDataString(playlistId);
        var itemsUri = new Uri(baseUri + "/items?offset=" + offset.ToString(
            CultureInfo.InvariantCulture) + "&limit=" + limit.ToString(
            CultureInfo.InvariantCulture));
        var itemsResponse = await SendAsync(identity, itemsUri, cancellationToken)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(itemsResponse);
        return SpotifyResponseParser.ParsePlaylistItems(
            itemsResponse.Body, offset, limit);
    }

    private Task<SpotifyHttpResponse> SendAsync(
        SpotifyIntegrationIdentity identity,
        Uri uri,
        CancellationToken cancellationToken) => _sender.SendAsync(identity,
            new SpotifyAuthorizedRequest(
                HttpMethod.Get, uri,
                WindowsSpotifyPlatformBackend.PlaylistReadPrivateScope,
                WindowsSpotifyPlatformBackend.PlaylistReadCollaborativeScope),
            cancellationToken);
}

internal static class SpotifyApiValidation
{
    internal const int MaximumCollectionPageSize = 50;
    internal const int MaximumCollectionOffset = 100_000;

    internal static void ValidateOpaqueId(
        string value,
        string label,
        string errorCode = "invalid_request")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
            value.Any(character => character is < '!' or > '~'))
            throw new SpotifyProviderException(errorCode, $"Spotify {label} is invalid.");
    }

    internal static void ValidateSpotifyUri(
        string value,
        string errorCode = "invalid_request")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_048 ||
            value.Any(char.IsControl) ||
            !value.StartsWith("spotify:", StringComparison.Ordinal) ||
            value.Count(character => character == ':') < 2)
            throw new SpotifyProviderException(errorCode, "Spotify URI is invalid.");
    }

    internal static void ValidatePage(int offset, int limit)
    {
        if (offset is < 0 or > MaximumCollectionOffset ||
            limit is < 1 or > MaximumCollectionPageSize)
            throw new SpotifyProviderException(
                "invalid_request", "Spotify page request is invalid.");
    }

    internal static void ValidateStartPlayback(
        string? deviceId,
        string? contextUri,
        IReadOnlyList<string>? itemUris,
        int? offset,
        string? offsetUri)
    {
        if ((contextUri is null) == (itemUris is null) ||
            itemUris is { Count: < 1 or > MaximumCollectionPageSize } ||
            offset is < 0 || offset is not null && contextUri is null ||
            offsetUri is not null && contextUri is null ||
            offset is not null && offsetUri is not null)
            throw new SpotifyProviderException(
                "invalid_request", "Spotify playback selection is invalid.");
        if (contextUri is not null) ValidateSpotifyUri(contextUri);
        if (offsetUri is not null) ValidateSpotifyUri(offsetUri);
        if (itemUris is not null)
            foreach (var uri in itemUris) ValidateSpotifyUri(uri);
        if (deviceId is not null) ValidateOpaqueId(deviceId, "device identifier");
    }

    internal static string ValidateRepeat(string? state) => state switch
    {
        "track" or "context" or "off" => state,
        _ => throw new SpotifyProviderException(
            "invalid_request", "Spotify repeat state is invalid."),
    };

    internal static string RequiredBoolean(bool? value, string name) => value switch
    {
        true => "true",
        false => "false",
        _ => throw new SpotifyProviderException(
            "invalid_request", $"Spotify {name} is required."),
    };

    internal static long RequiredRange(
        long? value,
        long minimum,
        long maximum,
        string name)
    {
        if (value is null || value < minimum || value > maximum)
            throw new SpotifyProviderException(
                "invalid_request", $"Spotify {name} is invalid.");
        return value.Value;
    }

    internal static int RequiredRange(
        int? value,
        int minimum,
        int maximum,
        string name)
    {
        if (value is null || value < minimum || value > maximum)
            throw new SpotifyProviderException(
                "invalid_request", $"Spotify {name} is invalid.");
        return value.Value;
    }
}
