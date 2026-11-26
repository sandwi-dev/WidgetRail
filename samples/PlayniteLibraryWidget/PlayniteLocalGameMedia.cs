using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLocalGameMedia(string? Logo, string? Trailer, string? Microtrailer, string? LogoRevision)
{
    internal WidgetArtworkHandle? LogoHandle(string gameId) => Logo == null || LogoRevision == null ? null :
        new WidgetArtworkHandle("playnite.logo." + gameId + "." + LogoRevision);
    internal MediaPlayerSource? PreviewSource => (Microtrailer ?? Trailer) is { } path ? MediaPlayerSource.LocalFile(path) : null;

    internal static PlayniteLocalGameMedia? Read(JsonElement media, string gameId)
    {
        string? PathFor(string field, params string[] names)
        {
            if (!media.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { } path || !ArtworkFileContract.IsLocalPath(path) ||
                !names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)) return null;
            if (field != "logo" && !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), gameId, StringComparison.OrdinalIgnoreCase)) return null;
            return path;
        }
        var logo = PathFor("logo", "Logo.png", "logo_2x.png");
        var trailer = PathFor("trailer", "VideoTrailer.mp4");
        var micro = PathFor("microtrailer", "VideoMicrotrailer.mp4");
        var revision = media.TryGetProperty("logoRevision", out var stamp) && stamp.ValueKind == JsonValueKind.String &&
            stamp.GetString() is { Length: > 0 and <= 40 } text && text.All(c => char.IsAsciiHexDigit(c) || c == '-') ? text : null;
        return logo == null && trailer == null && micro == null ? null : new(logo, trailer, micro, revision);
    }
}
