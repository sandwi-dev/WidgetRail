namespace WidgetRail.Samples.YouTubeWidget;

internal static class YouTubeLinkParser
{
    internal const int MaximumLinkCharacters = 512;
    private const int VideoIdCharacters = 11;

    public static bool TryParse(string? value, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (candidate.Length > MaximumLinkCharacters ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        string? parsed = host switch
        {
            "youtu.be" or "www.youtu.be" => SinglePathSegment(uri.AbsolutePath),
            "youtube.com" or "www.youtube.com" or "m.youtube.com" =>
                ParseYouTubePath(uri),
            _ => null,
        };
        if (!IsVideoId(parsed)) return false;
        videoId = parsed!;
        return true;
    }

    private static string? ParseYouTubePath(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 1 &&
            string.Equals(segments[0], "watch", StringComparison.OrdinalIgnoreCase))
            return QueryValue(uri.Query, "v");
        if (segments.Length == 2 &&
            (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase)))
            return Uri.UnescapeDataString(segments[1]);
        return null;
    }

    private static string? SinglePathSegment(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 ? Uri.UnescapeDataString(segments[0]) : null;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.Ordinal))
                continue;
            return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return null;
    }

    internal static bool IsVideoId(string? value) =>
        value is { Length: VideoIdCharacters } &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
