using System.Text.Json.Serialization;
namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<MediaPlayerSourceKind>))]
public enum MediaPlayerSourceKind { Capture, PackageAsset, WebUrl, LocalFile, SavedCapture, WebImage, EmbeddedSession }

[JsonConverter(typeof(JsonStringEnumConverter<MediaPlayerPresentation>))]
public enum MediaPlayerPresentation { Interactive, Animation, Inline }

/// <summary>Exact saved-library content, independent of title/favorite metadata. Contains no path or open-file grant.</summary>
public sealed record SavedCaptureMediaReference(string Id, long Revision)
{
    public bool IsWellFormed() => CaptureLimits.ValidToken(Id) && Revision >= 0;
}

/// <summary>Declarative media location. LocalFile is admitted only for full-trust widgets.</summary>
public sealed record MediaPlayerSource(MediaPlayerSourceKind Kind, string? Location = null, CaptureAttachment? Attachment = null)
{
    /// <summary>Anonymous, bounded file acquisition pinned to the source HTTPS origin. Requires protocol 133.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPublicMedia { get; init; }
    public static MediaPlayerSource PublicFile(string url) => WebUrl(url) with { IsPublicMedia = true };
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAdaptiveDash { get; init; }
    public static MediaPlayerSource PublicDash(string url) => PublicFile(url) with { IsAdaptiveDash = true };
    public static MediaPlayerSource PublicImage(string url) => WebImage(url) with { IsPublicMedia = true };
    /// <summary>Mount the widget's sealed private embedded player for the selected gallery occurrence.</summary>
    public static MediaPlayerSource EmbeddedSession(string sessionId, string mediaKey) =>
        new(MediaPlayerSourceKind.EmbeddedSession, sessionId) { EmbeddedMediaKey = mediaKey };
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmbeddedMediaKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SavedCaptureMediaReference? SavedCapture { get; init; }
    public static MediaPlayerSource FromSavedCapture(string id, long revision) => new(MediaPlayerSourceKind.SavedCapture) { SavedCapture = new(id, revision) };
    public static MediaPlayerSource FromCapture(CaptureAttachment attachment) => new(MediaPlayerSourceKind.Capture, Attachment: attachment);
    public static MediaPlayerSource PackageAsset(string path) => new(MediaPlayerSourceKind.PackageAsset, path);
    public static MediaPlayerSource WebUrl(string url) => new(MediaPlayerSourceKind.WebUrl, url);
    /// <summary>An HTTPS image decoded with the host's bounded artwork pipeline and shown with native image controls.</summary>
    public static MediaPlayerSource WebImage(string url) => new(MediaPlayerSourceKind.WebImage, url);
    public static MediaPlayerSource LocalFile(string path) => new(MediaPlayerSourceKind.LocalFile, path);
    public bool IsWellFormed() => (Kind == MediaPlayerSourceKind.EmbeddedSession || EmbeddedMediaKey is null) &&
        (!IsAdaptiveDash || IsPublicMedia && Kind == MediaPlayerSourceKind.WebUrl) &&
        (!IsPublicMedia || Kind is MediaPlayerSourceKind.WebUrl or MediaPlayerSourceKind.WebImage && IsPublicMediaLocation(Location)) &&
        (Kind == MediaPlayerSourceKind.SavedCapture || SavedCapture is null) && Kind switch
    {
        MediaPlayerSourceKind.Capture => Location is null && Attachment?.IsWellFormed() == true,
        MediaPlayerSourceKind.PackageAsset => Attachment is null && IsPackagePath(Location),
        MediaPlayerSourceKind.WebUrl => Attachment is null && WebBrowserDocument.IsWebUrl(Location),
        MediaPlayerSourceKind.WebImage => Attachment is null && WebBrowserDocument.IsWebUrl(Location) &&
            Uri.TryCreate(Location, UriKind.Absolute, out var imageUri) && imageUri.Scheme == Uri.UriSchemeHttps,
        MediaPlayerSourceKind.LocalFile => Attachment is null && IsLocalPath(Location),
        MediaPlayerSourceKind.EmbeddedSession => Attachment is null && Location is { Length: > 0 and <= 128 } &&
            Location.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') &&
            EmbeddedMediaKey is { Length: > 0 and <= ProtocolConstants.MaximumEmbeddedMediaKeyLength } &&
            EmbeddedMediaKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'),
        MediaPlayerSourceKind.SavedCapture => Location is null && Attachment is null && SavedCapture?.IsWellFormed() == true,
        _ => false,
    };
    public static bool IsPublicMediaLocation(string? source) => source is { Length: > 0 and <= 8192 } &&
        !source.Any(value => char.IsControl(value) || value == '\\') && Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    public static bool IsPackagePath(string? path) => path is { Length: > 0 and <= 1024 } &&
        !path.Any(c => char.IsControl(c) || c is '\\' or ':' or '?' or '#' or '*') &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or "..") && part == part.Trim());
    public static bool IsLocalPath(string? path) => path is { Length: >= 4 and <= 4096 } &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\' &&
        !path[2..].Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|');
}

/// <summary>Initial playback preferences for interactive viewers and passive animations.</summary>
public sealed record MediaPlayerOptions(bool AutoPlay = false, bool Loop = false, bool Muted = false,
    double Volume = 1, double PlaybackRate = 1)
{
    /// <summary>Decorative motion: respect reduced-motion and viewport visibility, while allowing explicit playback in an interactive viewer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAnimation { get; init; }
    public bool IsWellFormed() => double.IsFinite(Volume) && Volume is >= 0 and <= 1 &&
        double.IsFinite(PlaybackRate) && PlaybackRate is >= 0.5 and <= 2;
}

/// <summary>One stable occurrence in a bounded host-owned media gallery.</summary>
public sealed record MediaPlayerGalleryItem(string Id, string Label, MediaPlayerSource Source, MediaPlayerOptions? Options = null)
{
    /// <summary>Optional anonymous HTTPS thumbnail for a gallery strip. Supply one for every member.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicThumbnail { get; init; }
    /// <summary>Optional higher-resolution still for the inline carousel, acquired as anonymous public artwork.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicPreviewImage { get; init; }
    public bool IsWellFormed() => Id is { Length: > 0 and <= 128 } &&
        Id.All(value => char.IsAsciiLetterOrDigit(value) || value is '.' or '-' or '_') &&
        !string.IsNullOrWhiteSpace(Label) && Label.Length <= 256 && !Label.Any(char.IsControl) &&
        Source?.IsWellFormed() == true && (Options is null || Options.IsWellFormed()) &&
        (PublicThumbnail is null || MediaPlayerSource.IsPublicMediaLocation(PublicThumbnail)) &&
        (PublicPreviewImage is null || MediaPlayerSource.IsPublicMediaLocation(PublicPreviewImage));
}

public sealed record MediaPlayerDefinition(MediaPlayerSource Source, MediaPlayerOptions Options, long Revision = 1)
{
    public const int MaximumGalleryItems = 16;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MediaPlayerGalleryItem>? Gallery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool InlineCarousel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MediaPlayerPresentation Presentation { get; init; }
    public bool IsWellFormed() => Source?.IsWellFormed() == true && Options?.IsWellFormed() == true && Revision is >= 1 and <= 9_007_199_254_740_991 &&
        Enum.IsDefined(Presentation) && (Presentation!=MediaPlayerPresentation.Inline || Gallery is null &&
            Options is { AutoPlay:false,IsAnimation:false } && Source.Kind!=MediaPlayerSourceKind.WebImage) &&
        (Presentation!=MediaPlayerPresentation.Animation || Gallery is null &&
            Options is { AutoPlay:true,Loop:true,Muted:true,IsAnimation:true } && Source.Kind==MediaPlayerSourceKind.WebUrl &&
            Uri.TryCreate(Source.Location,UriKind.Absolute,out var animationUri) && animationUri.Scheme==Uri.UriSchemeHttps) &&
        (!InlineCarousel || Gallery is { Count: > 0 } && Gallery[0]?.PublicThumbnail is not null) &&
        (Gallery is null || Gallery.Count is > 0 and <= MaximumGalleryItems && Gallery.All(item => item?.IsWellFormed() == true && (InlineCarousel || item.PublicPreviewImage is null)) &&
            Gallery[0].Source == Source && Gallery.All(item => (item.PublicThumbnail is null) == (Gallery[0].PublicThumbnail is null)) &&
            Gallery.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == Gallery.Count);
    public bool DeclaresGalleryItem(string? id) => id is null || Gallery?.Any(item => item.Id == id) == true;
    public MediaPlayerDefinition SelectGalleryItem(string? id)
    {
        if (!IsWellFormed() || !DeclaresGalleryItem(id)) throw new ArgumentException("The media gallery item is unavailable.", nameof(id));
        if (Gallery is null) return this;
        var item = id is null ? Gallery[0] : Gallery.Single(item => item.Id == id);
        return new(item.Source, item.Options ?? Options, Revision);
    }
    // Declarations cross JSON/pipe boundaries; collection reference equality
    // cannot authorize a selected item or preserve an unchanged declaration.
    public bool Equals(MediaPlayerDefinition? other) => other is not null && Source == other.Source && Options == other.Options && Presentation == other.Presentation &&
        Revision == other.Revision && InlineCarousel == other.InlineCarousel && (Gallery is null ? other.Gallery is null : other.Gallery is not null && Gallery.SequenceEqual(other.Gallery));
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Source); hash.Add(Options); hash.Add(Revision); hash.Add(Presentation); hash.Add(InlineCarousel);
        if (Gallery is not null) foreach (var item in Gallery) hash.Add(item);
        return hash.ToHashCode();
    }
    public static MediaPlayerDefinition FromCapture(CaptureAttachment capture) => new(MediaPlayerSource.FromCapture(capture), new(Muted: true));
    public static MediaPlayerDefinition? FromNode(ViewNode node) => node.MediaPlayer ?? (node.CapturedMedia is { } capture ? FromCapture(capture) : null);
}
