using System.Text.Json;
using WidgetRail.Samples.SpotifyWidget;

namespace WidgetRail.WindowsSpotifyProvider;

internal static partial class SpotifyResponseParser
{
    internal static SpotifySearchPage ParseSearchPage(string body, SpotifySearchKind kind,
        int requestedOffset, int requestedLimit)
    {
        DemandBoundedBody(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var type = kind.ToString().ToLowerInvariant();
            var root = RequireObject(document.RootElement);
            if (!root.TryGetProperty(type + "s", out var page))
                throw Invalid("Spotify returned an incomplete search page.");
            page = RequireObject(page);
            var offset = RequiredBoundedInt32(page, "offset", 0, 999);
            var limit = RequiredBoundedInt32(page, "limit", 1, 10);
            var total = Math.Min(1000, RequiredBoundedInt32(page, "total", 0, int.MaxValue));
            var values = RequireArray(page, "items");
            if (offset != requestedOffset || limit > requestedLimit || values.GetArrayLength() > limit)
                throw Invalid("Spotify returned an inconsistent search page.");
            var items = new List<SpotifySearchItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var item = RequireObject(value);
                if (OptionalString(item, "type", 32) != type) continue;
                var id = RequiredIdentifier(item, "id");
                if (!ids.Add(id)) continue;
                var title = RequiredDisplayString(item, "name", 160);
                var subtitle = kind is SpotifySearchKind.Track or SpotifySearchKind.Album
                    ? SanitizeDisplayValue(JoinArtistNames(item), 160) ?? "Spotify"
                    : kind == SpotifySearchKind.Playlist && item.TryGetProperty("owner", out var owner) &&
                        owner.ValueKind == JsonValueKind.Object
                        ? OptionalDisplayString(owner, "display_name", 160) ?? "Spotify" : type;
                var artwork = kind == SpotifySearchKind.Track ? ReadArtwork(item, "track") : ReadImageArray(item);
                var playable = OptionalNullableBoolean(item, "is_playable") ?? true;
                if (item.TryGetProperty("restrictions", out var restrictions) &&
                    restrictions.ValueKind == JsonValueKind.Object && restrictions.EnumerateObject().Any()) playable = false;
                items.Add(new(kind, id, title, subtitle, artwork,
                    "spotify:" + type + ":" + id, BuildSpotifyUrl(type, id), playable));
            }
            return new(items, offset, limit, Math.Max(offset, total), items.Count == values.GetArrayLength());
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception) { throw Invalid("Spotify returned invalid search results.", exception); }
    }
}
