using System.Text.Json.Serialization;
namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<MediaPlayerSourceKind>))]
public enum MediaPlayerSourceKind { Capture, PackageAsset, WebUrl, LocalFile }

/// <summary>Declarative media location. LocalFile is admitted only for full-trust widgets.</summary>
public sealed record MediaPlayerSource(MediaPlayerSourceKind Kind, string? Location = null, CaptureAttachment? Attachment = null)
{
    public static MediaPlayerSource FromCapture(CaptureAttachment attachment) => new(MediaPlayerSourceKind.Capture, Attachment: attachment);
    public static MediaPlayerSource PackageAsset(string path) => new(MediaPlayerSourceKind.PackageAsset, path);
    public static MediaPlayerSource WebUrl(string url) => new(MediaPlayerSourceKind.WebUrl, url);
    public static MediaPlayerSource LocalFile(string path) => new(MediaPlayerSourceKind.LocalFile, path);
    public bool IsWellFormed() => Kind switch
    {
        MediaPlayerSourceKind.Capture => Location is null && Attachment?.IsWellFormed() == true,
        MediaPlayerSourceKind.PackageAsset => Attachment is null && IsPackagePath(Location),
        MediaPlayerSourceKind.WebUrl => Attachment is null && WebBrowserDocument.IsWebUrl(Location),
        MediaPlayerSourceKind.LocalFile => Attachment is null && IsLocalPath(Location),
        _ => false,
    };
    public static bool IsPackagePath(string? path) => path is { Length: > 0 and <= 1024 } &&
        !path.Any(c => char.IsControl(c) || c is '\\' or ':' or '?' or '#' or '*') &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or "..") && part == part.Trim());
    public static bool IsLocalPath(string? path) => path is { Length: >= 4 and <= 4096 } &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\' &&
        !path[2..].Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|');
}

/// <summary>Initial playback preferences. Native controls remain available regardless of source.</summary>
public sealed record MediaPlayerOptions(bool AutoPlay = false, bool Loop = false, bool Muted = false,
    double Volume = 1, double PlaybackRate = 1)
{
    public bool IsWellFormed() => double.IsFinite(Volume) && Volume is >= 0 and <= 1 &&
        double.IsFinite(PlaybackRate) && PlaybackRate is >= 0.5 and <= 2;
}

public sealed record MediaPlayerDefinition(MediaPlayerSource Source, MediaPlayerOptions Options, long Revision = 1)
{
    public bool IsWellFormed() => Source?.IsWellFormed() == true && Options?.IsWellFormed() == true && Revision is >= 1 and <= 9_007_199_254_740_991;
    public static MediaPlayerDefinition FromCapture(CaptureAttachment capture) => new(MediaPlayerSource.FromCapture(capture), new(Muted: true));
    public static MediaPlayerDefinition? FromNode(ViewNode node) => node.MediaPlayer ?? (node.CapturedMedia is { } capture ? FromCapture(capture) : null);
}
