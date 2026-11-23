using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteTrailerPresentation
{
    internal static string? LinkedTrailer(IReadOnlyList<PlayniteGameLink>? links)
    {
        string? selected = null;
        foreach (var link in links ?? [])
        {
            if (link.Name.Trim().ToLowerInvariant() is not ("trailer" or "official trailer" or "launch trailer" or
                "release trailer" or "youtube trailer") || !YouTubeLinkParser.TryParse(link.Url, out var candidate)) continue;
            if (selected is not null && selected != candidate) return null;
            selected = candidate;
        }
        return selected;
    }

    internal static string SessionId(string id, long generation) => $"playnite.trailer.{generation}.{id}";
    internal static EmbeddedMediaSession Session(string id, string gameName, long generation) => new()
    {
        Id = SessionId(id, generation), AccessibleName = "Trailer for " + gameName,
        EntryAsset = "payload/media/trailer.html", AspectRatio = 16d / 9,
        Surface = new() { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 356, MinimumHeight = 200 },
        Resources = [
            new() { Path = "payload/media/trailer.html", ContentType = "text/html" },
            new() { Path = "payload/media/adapter-runtime.js", ContentType = "application/javascript" },
        ],
        AllowedFrameOrigins = ["https://www.youtube-nocookie.com", "https://www.youtube.com"],
        AllowedFrameDomainFamilies = ["googlevideo.com", "ytimg.com", "gstatic.com", "youtube.com"],
        Commands = [EmbeddedMediaCommand.TogglePlayback, EmbeddedMediaCommand.ToggleMute, EmbeddedMediaCommand.SeekBackward, EmbeddedMediaCommand.SeekForward],
    };
}
