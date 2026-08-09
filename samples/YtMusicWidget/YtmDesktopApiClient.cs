using System.Text.Json;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.YtMusicWidget;

/// <summary>
/// YTMDesktop2 client implemented exclusively with public, typed host services.
/// The addon never opens a socket, reads a persisted token, or calls a Windows
/// credential API. The host fixes the origin to 127.0.0.1:13091 and injects the
/// package-scoped bearer secret only for requests that explicitly name its slot.
/// </summary>
public sealed class YtmDesktopApiClient : IYtMusicClient, IDisposable
{
    public const string PackageVersion = "0.2.6";
    public const int CompanionPort = 13091;
    public const string BearerSecretSlot = "ytmdesktop2.bearer";
    private static readonly TimeSpan PairingApprovalTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan TrackMetadataCacheDuration = TimeSpan.FromMinutes(5);

    private readonly WidgetHostServices _services;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    private readonly object _credentialStateLock = new();
    private readonly object _trackCacheLock = new();
    private bool _credentialKnown;
    private bool _hasCredential;
    private CachedTrackMetadata? _cachedTrack;
    private long _cachedTrackTimestamp;

    public YtmDesktopApiClient(WidgetHostServices services, TimeProvider? timeProvider = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<YtMusicConnectionInfo> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var hasCredential = await HasCredentialAsync(cancellationToken).ConfigureAwait(false);
        using var document = await SendAsync(
            isPost: false, "/", jsonBody: null, includeAuthorization: hasCredential,
            options: null, cancellationToken).ConfigureAwait(false);
        return new YtMusicConnectionInfo(
            GetBoolean(document.RootElement, "authRequired"),
            hasCredential);
    }

    public async Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var hasCredential = await HasCredentialAsync(cancellationToken).ConfigureAwait(false);
        using var stateDocument = await SendAsync(
            isPost: false, "/track/state", jsonBody: null, includeAuthorization: hasCredential,
            options: null, cancellationToken).ConfigureAwait(false);
        var state = stateDocument.RootElement;
        var stateTrackId = GetString(state, "id");
        var now = _timeProvider.GetTimestamp();
        CachedTrackMetadata? metadata;
        lock (_trackCacheLock)
        {
            metadata = IsReusableTrackMetadata(_cachedTrack, stateTrackId, now)
                ? _cachedTrack
                : null;
        }

        if (metadata is null)
        {
            using var trackDocument = await SendAsync(
                isPost: false, "/track", jsonBody: null, includeAuthorization: hasCredential,
                options: null, cancellationToken).ConfigureAwait(false);
            metadata = ParseTrackMetadata(trackDocument.RootElement);
            if (metadata.HasCompleteMetadata)
            {
                lock (_trackCacheLock)
                {
                    _cachedTrack = metadata;
                    _cachedTrackTimestamp = _timeProvider.GetTimestamp();
                }
            }
        }

        return ParseSnapshot(metadata, state);
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
            YtMusicCommand.Like => ("/track/like", JsonSerializer.Serialize(toggleState ?? true)),
            YtMusicCommand.Dislike => ("/track/dislike", JsonSerializer.Serialize(toggleState ?? true)),
            YtMusicCommand.Shuffle => ("/track/shuffle", null),
            YtMusicCommand.Repeat => ("/track/repeat", null),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var hasCredential = await HasCredentialAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendAsync(
            isPost: true, path, body ?? "{}", includeAuthorization: hasCredential,
            options: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<YtMusicPairingCode> RequestPairingCodeAsync(
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            appId = "gamebaralternative.ytmusic",
            appName = "Game Bar Alternative YT Music",
            appVersion = PackageVersion,
        });
        using var document = await SendAsync(
            isPost: true, "/auth/requestcode", body, includeAuthorization: false,
            options: null, cancellationToken).ConfigureAwait(false);
        var code = GetString(document.RootElement, "code");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("YTMDesktop2 did not return a pairing code.");
        return new YtMusicPairingCode(code);
    }

    public async Task CompletePairingAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var body = JsonSerializer.Serialize(new
        {
            appId = "gamebaralternative.ytmusic",
            code = code.Trim(),
        });
        using var document = await SendAsync(
            isPost: true,
            "/auth/request",
            body,
            includeAuthorization: false,
            options: new WidgetLoopbackRequestOptions { Timeout = PairingApprovalTimeout },
            cancellationToken).ConfigureAwait(false);
        var token = GetString(document.RootElement, "token");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("YTMDesktop2 approved pairing but returned no token.");
        await _services.PrivateSecrets.SaveAsync(
            BearerSecretSlot, token.Trim(), cancellationToken).ConfigureAwait(false);
        SetCredentialState(hasCredential: true);
    }

    public async Task ClearCredentialAsync(CancellationToken cancellationToken = default)
    {
        // Fail safe locally before an asynchronous provider call. A failed
        // delete cannot cause this client to reuse a rejected credential.
        SetCredentialState(hasCredential: false);
        await _services.PrivateSecrets.DeleteAsync(
            BearerSecretSlot, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _credentialGate.Dispose();

    internal static YtMusicPlaybackSnapshot ParseSnapshot(JsonElement track, JsonElement state)
    {
        return ParseSnapshot(ParseTrackMetadata(track), state);
    }

    private static CachedTrackMetadata ParseTrackMetadata(JsonElement track)
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
        return new CachedTrackMetadata(
            metadataTrackId,
            title,
            artist,
            album,
            IsSafeArtworkUrl(artwork) ? artwork : string.Empty,
            SanitizeNonNegative(GetNumber(meta, "duration", 0)),
            hasCompleteMetadata);
    }

    private static YtMusicPlaybackSnapshot ParseSnapshot(
        CachedTrackMetadata metadata,
        JsonElement state)
    {
        var trackId = GetString(state, "id") ?? metadata.TrackId;
        var duration = GetNumber(state, "duration", metadata.DurationSeconds);
        var position = GetNumber(state, "uiProgress", GetNumber(state, "progress", 0));
        duration = SanitizeNonNegative(duration);
        position = Math.Min(SanitizeNonNegative(position), duration > 0 ? duration : double.MaxValue);

        return new YtMusicPlaybackSnapshot(
            trackId,
            metadata.Title,
            metadata.Artist,
            metadata.Album,
            metadata.ArtworkUrl,
            GetBoolean(state, "playing"),
            GetBoolean(state, "liked"),
            GetBoolean(state, "disliked"),
            position,
            duration,
            GetOptionalBoolean(state, "shuffle") ?? GetOptionalBoolean(state, "shuffled"),
            GetOptionalRepeatMode(state),
            metadata.TrackId,
            metadata.HasCompleteMetadata);
    }

    private bool IsReusableTrackMetadata(
        CachedTrackMetadata? metadata,
        string? stateTrackId,
        long now) =>
        metadata is { HasCompleteMetadata: true } &&
        !string.IsNullOrWhiteSpace(stateTrackId) &&
        string.Equals(metadata.TrackId, stateTrackId, StringComparison.Ordinal) &&
        _timeProvider.GetElapsedTime(_cachedTrackTimestamp, now) < TrackMetadataCacheDuration;

    private async Task<JsonDocument> SendAsync(
        bool isPost,
        string path,
        string? jsonBody,
        bool includeAuthorization,
        WidgetLoopbackRequestOptions? options,
        CancellationToken cancellationToken)
    {
        options = options is null
            ? new WidgetLoopbackRequestOptions()
            : options with { };
        if (includeAuthorization)
            options = options with
            {
                BearerSecretSlot = BearerSecretSlot,
                InvalidateBearerSecretOnUnauthorized = true,
            };

        var response = isPost
            ? await _services.Loopback.PostJsonAsync(
                CompanionPort, path, jsonBody ?? "{}", options, cancellationToken)
                .ConfigureAwait(false)
            : await _services.Loopback.GetJsonAsync(
                CompanionPort, path, options, cancellationToken)
                .ConfigureAwait(false);
        if (response.StatusCode == 401)
        {
            // The opted-in host request has already invalidated the durable
            // slot before returning. Keep only the non-secret local cache in
            // sync; do not attempt a second lifecycle-gated Delete operation.
            SetCredentialState(hasCredential: false);
            throw new YtMusicAuthorizationRequiredException();
        }
        if (response.StatusCode is < 200 or > 299)
            throw new YtMusicServiceException(response.StatusCode);

        try
        {
            return JsonDocument.Parse(
                string.IsNullOrWhiteSpace(response.JsonBody) ? "{}" : response.JsonBody,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "YTMDesktop2 returned malformed JSON.", exception);
        }
    }

    private async Task<bool> HasCredentialAsync(CancellationToken cancellationToken)
    {
        lock (_credentialStateLock)
            if (_credentialKnown) return _hasCredential;

        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_credentialStateLock)
                if (_credentialKnown) return _hasCredential;
            bool exists;
            try
            {
                exists = await _services.PrivateSecrets.ExistsAsync(
                    BearerSecretSlot, cancellationToken).ConfigureAwait(false);
            }
            catch (WidgetCapabilityException)
            {
                // Secret storage is optional so an authentication-disabled
                // companion remains usable without a vault grant. Pairing will
                // surface a concrete error if persistence is actually needed.
                exists = false;
            }
            SetCredentialState(exists);
            return exists;
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    private void SetCredentialState(bool hasCredential)
    {
        lock (_credentialStateLock)
        {
            _hasCredential = hasCredential;
            _credentialKnown = true;
        }
    }

    private static JsonElement GetObject(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var item) &&
        item.ValueKind == JsonValueKind.Object
            ? item
            : default;

    private static string? GetString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out var item)) return null;
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

    private static double SanitizeNonNegative(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static bool IsSafeArtworkUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    private sealed record CachedTrackMetadata(
        string TrackId,
        string Title,
        string Artist,
        string Album,
        string ArtworkUrl,
        double DurationSeconds,
        bool HasCompleteMetadata);
}
