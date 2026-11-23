using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record SteamTrailer(uint Id, string Name, string Thumbnail, string? Hls, string? Dash, string? Mp4, bool Highlight);
internal sealed record SteamScreenshot(uint Id, string Thumbnail, string Image);
internal sealed record SteamStoreMedia(IReadOnlyList<SteamTrailer> Trailers, IReadOnlyList<SteamScreenshot> Screenshots)
{
    internal string? PublisherTrailerId { get; init; }
    internal string? PublisherWebsite { get; init; }
    internal string? StoreName { get; init; }
    internal bool HasNativeTrailer => Trailers.Any(trailer => trailer.Dash is not null || trailer.Mp4 is not null);
    internal static SteamStoreMedia Empty { get; } = new([], []);
    internal static SteamStoreMedia Parse(uint appId, ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry) || entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            Number(data, "steam_appid") != appId) return Empty;

        var trailers = new List<SteamTrailer>();
        var screenshots = new List<SteamScreenshot>();
        foreach (var movie in Items(data, "movies"))
        {
            if (Number(movie, "id") is not uint id || trailers.Any(t => t.Id == id) ||
                Asset(movie, "thumbnail") is not { } thumbnail) continue;
            var hls = Asset(movie, "hls_h264");
            var dash = Asset(movie, "dash_h264");
            var mp4 = movie.TryGetProperty("mp4", out var files) && files.ValueKind == JsonValueKind.Object
                ? Asset(files, "480") ?? Asset(files, "max") : null;
            if (hls is null && dash is null && mp4 is null) continue;
            var label = Text(movie, "name");
            var highlight = movie.TryGetProperty("highlight", out var flag) && flag.ValueKind == JsonValueKind.True;
            trailers.Add(new(id, label is { Length: > 0 and <= 100 } && !label.Any(char.IsControl) ? label : "Trailer",
                thumbnail, hls, dash, mp4, highlight));
        }
        foreach (var image in Items(data, "screenshots"))
        {
            if (Number(image, "id") is not uint id || screenshots.Any(s => s.Id == id) ||
                Asset(image, "path_thumbnail") is not { } thumbnail || Asset(image, "path_full") is not { } full) continue;
            screenshots.Add(new(id, thumbnail, full));
            if (screenshots.Count == 12) break;
        }
        return new(trailers.OrderByDescending(t => t.Highlight).Take(4).ToArray(), screenshots)
        {
            StoreName = Text(data, "name") is { Length: > 0 and <= 256 } name ? name : null,
            PublisherWebsite = Text(data, "website") is { Length: > 0 and <= 2048 } website ? website : null,
        };
    }

    private static IEnumerable<JsonElement> Items(JsonElement data, string key) =>
        data.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Take(64).Where(item => item.ValueKind == JsonValueKind.Object) : [];
    private static uint? Number(JsonElement item, string key) => item.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number) ? number : null;
    private static string? Text(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Asset(JsonElement item, string key)
    {
        var url = Text(item, key);
        if (url is null || !MediaPlayerSource.IsPublicMediaLocation(url)) return null;
        var host = new Uri(url).IdnHost;
        return host.EndsWith(".steamstatic.com", StringComparison.Ordinal) ||
            host.EndsWith(".steamusercontent.com", StringComparison.Ordinal) ? url : null;
    }
}
