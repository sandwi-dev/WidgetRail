using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameBarAlternative.Samples.YtMusicWidget;

/// <summary>
/// Narrow, loopback-only client for the YTMDesktop2 companion application's local API.
/// The endpoint restrictions are intentionally carried over from the original Game Bar widget.
/// </summary>
public sealed class YtmDesktopApiClient : IYtMusicClient, IDisposable
{
    public const string DefaultEndpoint = "http://127.0.0.1:13091";

    private readonly Uri _baseUri;
    private readonly HttpClient _client;
    private readonly IYtMusicCredentialStore _credentialStore;
    private readonly object _credentialLock = new();
    private string? _token;

    public YtmDesktopApiClient(
        string endpoint = DefaultEndpoint,
        string? token = null,
        HttpMessageHandler? handler = null,
        IYtMusicCredentialStore? credentialStore = null)
    {
        _baseUri = ValidateEndpoint(endpoint);
        _credentialStore = credentialStore ??
            new WindowsCredentialManagerYtMusicCredentialStore(_baseUri.AbsoluteUri);
        _token = NormalizeToken(token) ?? NormalizeToken(_credentialStore.LoadToken());
        handler ??= new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(35),
        };
    }

    public async Task<YtMusicConnectionInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "/", null, cancellationToken).ConfigureAwait(false);
        return new YtMusicConnectionInfo(
            GetBoolean(document.RootElement, "authRequired"),
            HasCredential());
    }

    public async Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var trackTask = SendAsync(HttpMethod.Get, "/track", null, cancellationToken);
        var stateTask = SendAsync(HttpMethod.Get, "/track/state", null, cancellationToken);
        await Task.WhenAll(trackTask, stateTask).ConfigureAwait(false);
        using var trackDocument = await trackTask.ConfigureAwait(false);
        using var stateDocument = await stateTask.ConfigureAwait(false);
        return ParseSnapshot(trackDocument.RootElement, stateDocument.RootElement);
    }

    public async Task SendCommandAsync(
        YtMusicCommand command,
        CancellationToken cancellationToken = default,
        bool? toggleState = null)
    {
        var (path, body) = command switch
        {
            YtMusicCommand.TogglePlayback => ("/track/toggle-play-state", null),
            YtMusicCommand.Previous => ("/track/prev", null),
            YtMusicCommand.Next => ("/track/next", null),
            YtMusicCommand.Like => ("/track/like", (object)(toggleState ?? true)),
            YtMusicCommand.Dislike => ("/track/dislike", (object)(toggleState ?? true)),
            YtMusicCommand.Shuffle => ("/track/shuffle", null),
            YtMusicCommand.Repeat => ("/track/repeat", null),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
    }

    public async Task<YtMusicPairingCode> RequestPairingCodeAsync(CancellationToken cancellationToken = default)
    {
        var body = new
        {
            appId = "gamebaralternative.ytmusic",
            appName = "Game Bar Alternative YT Music",
            appVersion = "0.1.0",
        };
        using var document = await SendAsync(
            HttpMethod.Post,
            "/auth/requestcode",
            body,
            cancellationToken,
            includeAuthorization: false).ConfigureAwait(false);
        var code = GetString(document.RootElement, "code");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("YTMDesktop2 did not return a pairing code.");
        return new YtMusicPairingCode(code);
    }

    public async Task CompletePairingAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var body = new { appId = "gamebaralternative.ytmusic", code = code.Trim() };
        using var document = await SendAsync(
            HttpMethod.Post,
            "/auth/request",
            body,
            cancellationToken,
            includeAuthorization: false).ConfigureAwait(false);
        var token = GetString(document.RootElement, "token");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("YTMDesktop2 approved pairing but returned no token.");
        var normalized = NormalizeToken(token)!;
        _credentialStore.SaveToken(normalized);
        lock (_credentialLock) _token = normalized;
    }

    public void ClearCredential()
    {
        lock (_credentialLock) _token = null;
        _credentialStore.ClearToken();
    }

    public void Dispose() => _client.Dispose();

    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The YTMDesktop2 endpoint must be a local HTTP URL.");

        var normalizedHost = uri.DnsSafeHost;
        var isLoopback = string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(normalizedHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(normalizedHost, "::1", StringComparison.OrdinalIgnoreCase);
        if (!isLoopback || uri.Port is < 9999 or > 39999)
            throw new InvalidOperationException("Only localhost YTMDesktop2 endpoints on ports 9999-39999 are allowed.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The YTMDesktop2 endpoint cannot contain credentials, a query, or a fragment.");

        return new UriBuilder(uri) { Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    internal static YtMusicPlaybackSnapshot ParseSnapshot(JsonElement track, JsonElement state)
    {
        var video = GetObject(track, "video");
        var music = GetObject(track, "music");
        var meta = GetObject(track, "meta");
        var metadataTrackId = GetString(video, "videoId") ?? string.Empty;
        var metadataTitle = GetString(video, "title");
        var metadataArtist = GetString(video, "author");
        var hasCompleteMetadata =
            !string.IsNullOrWhiteSpace(metadataTrackId) &&
            !string.IsNullOrWhiteSpace(metadataTitle) &&
            !string.IsNullOrWhiteSpace(metadataArtist);
        var title = hasCompleteMetadata ? metadataTitle! : "YouTube Music";
        var artist = hasCompleteMetadata ? metadataArtist! : "No track metadata available";
        var album = GetString(music, "album") ?? string.Empty;
        var artwork = GetString(meta, "thumbnail") ?? string.Empty;
        var trackId = GetString(state, "id") ?? metadataTrackId;
        var duration = GetNumber(state, "duration", GetNumber(meta, "duration", 0));
        var position = GetNumber(state, "uiProgress", GetNumber(state, "progress", 0));
        duration = SanitizeNonNegative(duration);
        position = Math.Min(SanitizeNonNegative(position), duration > 0 ? duration : double.MaxValue);

        return new YtMusicPlaybackSnapshot(
            trackId,
            title,
            artist,
            album,
            IsSafeArtworkUrl(artwork) ? artwork : string.Empty,
            GetBoolean(state, "playing"),
            GetBoolean(state, "liked"),
            GetBoolean(state, "disliked"),
            position,
            duration,
            GetOptionalBoolean(state, "shuffle") ?? GetOptionalBoolean(state, "shuffled"),
            GetOptionalRepeatMode(state),
            metadataTrackId,
            hasCompleteMetadata);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool includeAuthorization = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path.TrimStart('/')));
        string? token;
        lock (_credentialLock) token = _token;
        if (includeAuthorization && !string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new YtMusicAuthorizationRequiredException();
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(responseBody) ? response.ReasonPhrase : responseBody.Trim();
            if (detail is { Length: > 500 }) detail = detail[..500];
            throw new InvalidOperationException(
                $"YTMDesktop2 returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }

    private static JsonElement GetObject(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var item) &&
        item.ValueKind == JsonValueKind.Object
            ? item
            : default;

    private static string? GetString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var item)) return null;
        return item.ValueKind switch
        {
            JsonValueKind.String => item.GetString(),
            JsonValueKind.Number => item.GetRawText(),
            _ => null,
        };
    }

    private static double GetNumber(JsonElement value, string propertyName, double defaultValue) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetDouble(out var number)
            ? number
            : defaultValue;

    private static bool GetBoolean(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            item.GetBoolean();

    private static bool? GetOptionalBoolean(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out var item)
            ? item.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static YtMusicRepeatMode? GetOptionalRepeatMode(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) return null;
        if (!state.TryGetProperty("repeat", out var value) &&
            !state.TryGetProperty("repeatMode", out value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number switch
            {
                0 => YtMusicRepeatMode.Off,
                1 => YtMusicRepeatMode.All,
                2 => YtMusicRepeatMode.One,
                _ => null,
            };
        }
        if (value.ValueKind != JsonValueKind.String) return null;
        return value.GetString()?.Trim().ToLowerInvariant() switch
        {
            "off" or "none" => YtMusicRepeatMode.Off,
            "all" or "list" or "playlist" or "queue" => YtMusicRepeatMode.All,
            "one" or "track" or "single" => YtMusicRepeatMode.One,
            _ => null,
        };
    }

    private static string? NormalizeToken(string? token) => string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    private bool HasCredential()
    {
        lock (_credentialLock) return !string.IsNullOrWhiteSpace(_token);
    }
    private static double SanitizeNonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static bool IsSafeArtworkUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
