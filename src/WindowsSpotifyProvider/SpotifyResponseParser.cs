using System.Text.Json;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsSpotifyProvider;

/// <summary>
/// Strict bounded parsing for Spotify Web API responses. This owner has no
/// transport, OAuth, vault, integration identity, or local-playback authority.
/// </summary>
internal static class SpotifyResponseParser
{
    private const int MaximumQueueItems = 100;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static SpotifyProviderPlayback ParsePlayback(string body)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var isPlaying = OptionalBoolean(root, "is_playing");
            var progress = OptionalInt64(root, "progress_ms") ?? 0;
            var repeat = OptionalString(root, "repeat_state", 16) ?? "off";
            var shuffle = OptionalBoolean(root, "shuffle_state");
            int? volume = null;
            if (root.TryGetProperty("device", out var device) &&
                device.ValueKind == JsonValueKind.Object &&
                device.TryGetProperty("volume_percent", out var volumeElement) &&
                volumeElement.TryGetInt32(out var parsedVolume))
                volume = Math.Clamp(parsedVolume, 0, 100);

            if (!root.TryGetProperty("item", out var item) ||
                item.ValueKind != JsonValueKind.Object)
                return UnavailablePlayback() with
                {
                    IsPlaying = isPlaying,
                    ProgressMilliseconds = Math.Max(0, progress),
                    RepeatState = repeat,
                    ShuffleState = shuffle,
                    VolumePercent = volume,
                };

            var itemType = RequiredString(item, "type", 32);
            if (itemType is not ("track" or "episode"))
                throw Invalid("Spotify returned an unsupported playback item.");
            var duration = OptionalInt64(item, "duration_ms") ?? 0;
            return new SpotifyProviderPlayback(
                true,
                isPlaying,
                Math.Clamp(progress, 0, Math.Max(duration, 0)),
                Math.Max(duration, 0),
                itemType,
                RequiredString(item, "name", 512),
                itemType == "track" ? JoinArtistNames(item) : ReadEpisodeShow(item),
                ReadContext(root),
                ReadArtwork(item, itemType),
                OptionalString(item, "uri", 2_048),
                repeat,
                shuffle,
                volume,
                ReadActions(root));
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned invalid playback data.", exception);
        }
    }

    internal static SpotifyDevicesSummary ParseDevices(string body)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var devices = RequireArray(RequireObject(document.RootElement), "devices");
            if (devices.GetArrayLength() > SpotifyPlaybackApi.MaximumDevices)
                throw Invalid("Spotify returned too many playback devices.");
            var result = new List<SpotifyDeviceSummary>(devices.GetArrayLength());
            foreach (var value in devices.EnumerateArray())
            {
                var device = RequireObject(value);
                var volume = OptionalNullableInt32(device, "volume_percent");
                if (volume is < 0 or > 100)
                    throw Invalid("Spotify returned an invalid device volume.");
                result.Add(new SpotifyDeviceSummary(
                    RequiredIdentifier(device, "id"),
                    RequiredDisplayString(device, "name", 160),
                    RequiredDisplayString(device, "type", 160),
                    OptionalNullableBoolean(device, "is_active") ?? false,
                    OptionalNullableBoolean(device, "is_restricted") ?? false,
                    OptionalNullableBoolean(device, "supports_volume") ?? false,
                    volume,
                    false));
            }
            return new SpotifyDevicesSummary(result);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned invalid playback devices.", exception);
        }
    }

    internal static SpotifyQueueSummary ParseQueue(string body)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            SpotifyMediaItemSummary? current = null;
            if (root.TryGetProperty("currently_playing", out var currentElement) &&
                currentElement.ValueKind == JsonValueKind.Object)
                current = ParseMediaItem(currentElement);
            var queue = RequireArray(root, "queue");
            var items = new List<SpotifyMediaItemSummary>(
                Math.Min(queue.GetArrayLength(), MaximumQueueItems));
            var truncated = queue.GetArrayLength() > MaximumQueueItems;
            foreach (var item in queue.EnumerateArray())
            {
                if (items.Count == MaximumQueueItems) break;
                if (item.ValueKind == JsonValueKind.Object &&
                    ParseMediaItem(item) is { } parsed)
                    items.Add(parsed);
            }
            return new SpotifyQueueSummary(current, items, truncated);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned an invalid queue.", exception);
        }
    }

    internal static SpotifyPlaylistPageSummary ParsePlaylistPage(
        string body,
        int requestedOffset,
        int requestedLimit)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var offset = RequiredBoundedInt32(root, "offset", 0,
                SpotifyApiValidation.MaximumCollectionOffset);
            var limit = RequiredBoundedInt32(root, "limit", 1,
                SpotifyApiValidation.MaximumCollectionPageSize);
            var total = RequiredBoundedInt32(root, "total", 0, int.MaxValue);
            if (offset != requestedOffset || limit > requestedLimit)
                throw Invalid("Spotify returned an inconsistent playlist page.");
            var values = RequireArray(root, "items");
            if (values.GetArrayLength() > limit)
                throw Invalid("Spotify returned too many playlists.");
            var items = new List<SpotifyPlaylistSummary>(values.GetArrayLength());
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                items.Add(ParsePlaylist(value));
            }
            return new SpotifyPlaylistPageSummary(items, offset, limit, total);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned an invalid playlist page.", exception);
        }
    }

    internal static SpotifyPlaylistSummary ParsePlaylistDocument(string body)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            return ParsePlaylist(document.RootElement);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned an invalid playlist.", exception);
        }
    }

    internal static SpotifyPlaylistItemsSummary ParsePlaylistItems(
        SpotifyPlaylistSummary playlist,
        string body,
        int requestedOffset,
        int requestedLimit)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var offset = RequiredBoundedInt32(root, "offset", 0,
                SpotifyApiValidation.MaximumCollectionOffset);
            var limit = RequiredBoundedInt32(root, "limit", 1,
                SpotifyApiValidation.MaximumCollectionPageSize);
            var total = RequiredBoundedInt32(root, "total", 0, int.MaxValue);
            if (offset != requestedOffset || limit > requestedLimit)
                throw Invalid("Spotify returned an inconsistent playlist item page.");
            var values = RequireArray(root, "items");
            if (values.GetArrayLength() > limit)
                throw Invalid("Spotify returned too many playlist items.");
            var items = new List<SpotifyMediaItemSummary>(values.GetArrayLength());
            foreach (var wrapper in values.EnumerateArray())
            {
                if (wrapper.ValueKind != JsonValueKind.Object ||
                    !wrapper.TryGetProperty("item", out var item) ||
                    item.ValueKind != JsonValueKind.Object)
                    continue;
                if (ParseMediaItem(item) is { } parsed) items.Add(parsed);
            }
            return new SpotifyPlaylistItemsSummary(playlist, items, offset, limit, total);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw Invalid("Spotify returned invalid playlist items.", exception);
        }
    }

    internal static void EnsureSuccess(
        SpotifyHttpResponse response,
        bool allowNoContent = false)
    {
        ArgumentNullException.ThrowIfNull(response);
        DemandBoundedBody(response.Body);
        if (response.StatusCode is >= 200 and <= 299 &&
            (allowNoContent || response.StatusCode != 204))
            return;
        var message = ReadApiError(response.Body) ?? response.StatusCode switch
        {
            400 => "Spotify rejected the request.",
            401 => "Spotify authorization expired. Connect again.",
            403 => "Spotify did not allow this action.",
            404 => "Spotify has no active playback device.",
            429 => "Spotify rate limit reached. Try again later.",
            >= 500 => "Spotify is temporarily unavailable.",
            _ => "Spotify request failed.",
        };
        var code = response.StatusCode switch
        {
            400 => "invalid_request",
            401 => "authorization_expired",
            403 => "forbidden",
            404 => "resource_not_found",
            429 => "rate_limited",
            >= 500 => "spotify_unavailable",
            _ => "spotify_error",
        };
        throw new SpotifyProviderException(code, message);
    }

    internal static SpotifyProviderPlayback UnavailablePlayback() => new(
        false, false, 0, 0, "none", "Nothing playing", "Spotify", null, null, null,
        "off", false, null,
        new(false, false, false, false, false, false, false, false));

    internal static void DemandBoundedBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            if (StrictUtf8.GetByteCount(body) > SpotifyHttpTransport.MaximumResponseBytes)
                throw new SpotifyProviderException(
                    "response_too_large", "Spotify response is too large.");
        }
        catch (EncoderFallbackException exception)
        {
            throw Invalid("Spotify returned invalid text.", exception);
        }
    }

    private static SpotifyPlaylistSummary ParsePlaylist(JsonElement value)
    {
        var playlist = RequireObject(value);
        var id = RequiredIdentifier(playlist, "id");
        var count = 0;
        if (playlist.TryGetProperty("items", out var itemPage) &&
            itemPage.ValueKind == JsonValueKind.Object)
            count = OptionalNullableInt32(itemPage, "total") ?? 0;
        else if (playlist.TryGetProperty("tracks", out var trackPage) &&
            trackPage.ValueKind == JsonValueKind.Object)
            count = OptionalNullableInt32(trackPage, "total") ?? 0;
        if (count < 0) throw Invalid("Spotify returned an invalid playlist size.");
        var ownerName = "Spotify";
        if (playlist.TryGetProperty("owner", out var owner) &&
            owner.ValueKind == JsonValueKind.Object)
            ownerName = OptionalDisplayString(owner, "display_name", 160) ?? "Spotify";
        return new SpotifyPlaylistSummary(
            id,
            RequiredDisplayString(playlist, "name", 160),
            OptionalDisplayString(playlist, "description", 160),
            ReadImageArray(playlist),
            BuildSpotifyUrl("playlist", id),
            "spotify:playlist:" + id,
            ownerName,
            OptionalNullableBoolean(playlist, "collaborative") ?? false,
            OptionalNullableBoolean(playlist, "public"),
            count);
    }

    private static SpotifyMediaItemSummary? ParseMediaItem(JsonElement value)
    {
        var item = RequireObject(value);
        var type = OptionalString(item, "type", 32);
        if (type is not ("track" or "episode")) return null;
        var id = OptionalString(item, "id", 256);
        var uri = OptionalString(item, "uri", 2_048);
        if (id is null || uri is null) return null;
        SpotifyApiValidation.ValidateOpaqueId(id, "media identifier", "invalid_response");
        SpotifyApiValidation.ValidateSpotifyUri(uri, "invalid_response");
        var duration = OptionalInt64(item, "duration_ms") ?? 0;
        if (duration is < 0 or > 604_800_000)
            throw Invalid("Spotify returned an invalid media duration.");
        var subtitle = type == "track" ? JoinArtistNames(item) : ReadEpisodeShow(item);
        return new SpotifyMediaItemSummary(
            type == "track" ? SpotifyPlaybackItemType.Track : SpotifyPlaybackItemType.Episode,
            RequiredDisplayString(item, "name", 160),
            SanitizeDisplayValue(subtitle, 160) ?? "Spotify",
            duration,
            ReadArtwork(item, type),
            uri,
            BuildSpotifyUrl(type, id),
            OptionalNullableBoolean(item, "is_playable") ?? true);
    }

    private static SpotifyProviderPlaybackActions ReadActions(JsonElement root)
    {
        var disallows = default(JsonElement);
        if (root.TryGetProperty("actions", out var actions) &&
            actions.ValueKind == JsonValueKind.Object)
            actions.TryGetProperty("disallows", out disallows);
        bool Allowed(string name) => disallows.ValueKind != JsonValueKind.Object ||
            !disallows.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.True;
        return new(Allowed("pausing"), Allowed("resuming"), Allowed("seeking"),
            Allowed("skipping_next"), Allowed("skipping_prev"),
            Allowed("toggling_repeat_context"), Allowed("toggling_repeat_track"),
            Allowed("toggling_shuffle"));
    }

    private static string JoinArtistNames(JsonElement item)
    {
        if (!item.TryGetProperty("artists", out var artists) ||
            artists.ValueKind != JsonValueKind.Array)
            return "Spotify";
        return string.Join(", ", artists.EnumerateArray().Take(8)
            .Select(artist => OptionalString(artist, "name", 256))
            .Where(name => name is not null)) is { Length: > 0 } names
                ? names
                : "Spotify";
    }

    private static string ReadEpisodeShow(JsonElement item) =>
        item.TryGetProperty("show", out var show) &&
        show.ValueKind == JsonValueKind.Object
            ? OptionalString(show, "name", 512) ?? "Spotify"
            : "Spotify";

    private static string? ReadArtwork(JsonElement item, string itemType)
    {
        JsonElement images;
        if (itemType == "track")
        {
            if (!item.TryGetProperty("album", out var album) ||
                album.ValueKind != JsonValueKind.Object ||
                !album.TryGetProperty("images", out images))
                return null;
        }
        else if (!item.TryGetProperty("images", out images)) return null;
        if (images.ValueKind != JsonValueKind.Array) return null;
        foreach (var image in images.EnumerateArray())
        {
            var url = OptionalString(image, "url", 2_048);
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                return url;
        }
        return null;
    }

    private static string? ReadContext(JsonElement root) =>
        root.TryGetProperty("context", out var context) &&
        context.ValueKind == JsonValueKind.Object
            ? OptionalString(context, "type", 64)
            : null;

    private static string? ReadApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("error", out var error)) return null;
            if (error.ValueKind == JsonValueKind.Object)
                return OptionalString(error, "message", 512) is { } message
                    ? SanitizeMessage(message)
                    : null;
            if (error.ValueKind == JsonValueKind.String)
                return OptionalString(root, "error_description", 512) is { } description
                    ? SanitizeMessage(description)
                    : SanitizeMessage(error.GetString());
        }
        catch (JsonException) { }
        return null;
    }

    private static string RequiredIdentifier(JsonElement parent, string property)
    {
        var value = RequiredString(parent, property, 256);
        SpotifyApiValidation.ValidateOpaqueId(value, "identifier", "invalid_response");
        return value;
    }

    private static string RequiredDisplayString(
        JsonElement parent,
        string property,
        int maximum) => OptionalDisplayString(parent, property, maximum) ??
            throw Invalid("Spotify response was missing display data.");

    private static string? OptionalDisplayString(
        JsonElement parent,
        string property,
        int maximum)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid("Spotify response display data was invalid.");
        return SanitizeDisplayValue(value.GetString(), maximum);
    }

    private static string? SanitizeDisplayValue(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = new string(value.Where(character => !char.IsControl(character))
            .Take(maximum).ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static JsonElement RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("Spotify response was invalid.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            throw Invalid("Spotify response was missing required data.");
        return value;
    }

    private static int RequiredBoundedInt32(
        JsonElement parent,
        string property,
        int minimum,
        int maximum)
    {
        var value = OptionalNullableInt32(parent, property);
        if (value is null || value < minimum || value > maximum)
            throw Invalid("Spotify response contained an invalid number.");
        return value.Value;
    }

    private static int? OptionalNullableInt32(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
            throw Invalid("Spotify response contained an invalid number.");
        return parsed;
    }

    private static bool? OptionalNullableBoolean(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid("Spotify response contained an invalid boolean."),
        };
    }

    private static string? ReadImageArray(JsonElement parent)
    {
        if (!parent.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.Object) continue;
            var url = OptionalString(image, "url", 2_048);
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                parsed.Scheme == Uri.UriSchemeHttps && parsed.IsDefaultPort &&
                string.IsNullOrEmpty(parsed.UserInfo))
                return url;
        }
        return null;
    }

    private static string RequiredString(JsonElement parent, string property, int maximum)
    {
        if (!parent.TryGetProperty(property, out var element) ||
            element.ValueKind != JsonValueKind.String)
            throw Invalid("Spotify response was missing required data.");
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw Invalid("Spotify response was invalid.");
        return value;
    }

    private static string? OptionalString(JsonElement parent, string property, int maximum)
    {
        if (!parent.TryGetProperty(property, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw Invalid("Spotify response was invalid.");
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw Invalid("Spotify response was invalid.");
        return value;
    }

    private static bool OptionalBoolean(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var element) &&
        element.ValueKind == JsonValueKind.True;

    private static long? OptionalInt64(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var element) &&
        element.TryGetInt64(out var value)
            ? value
            : null;

    private static string BuildSpotifyUrl(string type, string id) =>
        "https://open.spotify.com/" + type + "/" + Uri.EscapeDataString(id);

    private static string? SanitizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Where(character => !char.IsControl(character))
            .Take(256).ToArray());
    }

    private static SpotifyProviderException Invalid(
        string message,
        Exception? innerException = null) =>
        new("invalid_response", message, innerException);
}
