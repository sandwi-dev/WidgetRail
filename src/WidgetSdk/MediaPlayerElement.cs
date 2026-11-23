using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;

/// <summary>Shared inline native player for packaged files, media URLs, full-trust local files and captures.</summary>
public sealed record MediaPlayerElement : WidgetElement
{
    internal MediaPlayerElement(MediaPlayerDefinition media, string id, string accessibleName) : base(RequireId(id))
    {
        if (!media.IsWellFormed()) throw new ArgumentException("Invalid media source or playback options.", nameof(media));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleName);
        Media = media; AccessibleName = accessibleName;
    }
    public MediaPlayerDefinition Media { get; init; }
    public string AccessibleName { get; init; }
    public DirectionalScrollAnchor? ThumbnailAnchor { get; private init; }
    public MediaPlayerElement WithThumbnailAnchor(FocusAnchorPoint itemPoint, int viewportPercent)
    {
        if (Media.Gallery is not { Count: > 0 } gallery || gallery[0].PublicThumbnail is null)
            throw new InvalidOperationException("A thumbnail anchor requires a gallery with thumbnails.");
        var anchor = new DirectionalScrollAnchor(itemPoint, viewportPercent);
        if (!anchor.IsValid()) throw new ArgumentException("Specify a defined anchor point and a percentage from 0 to 100.");
        return this with { ThumbnailAnchor = anchor };
    }
    /// <summary>Optional scoped artwork shown by the native player while media loads.</summary>
    public WidgetArtworkHandle? Poster { get; init; }
    public string? PosterSource { get; init; }
    public MediaPlayerElement WithPoster(WidgetArtworkHandle artwork) => this with { Poster = artwork, PosterSource = null };
    /// <summary>Native loading artwork using the same bounded image-source contract as UI.Image.</summary>
    public MediaPlayerElement WithPosterSource(string imageSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageSource);
        return this with { PosterSource=imageSource,Poster=null };
    }
    internal override ViewNode ToProtocolNode() => new()
    { Id = Id, Kind = ViewNodeKind.MediaPlayer, MediaPlayer = Media, AccessibilityLabel = AccessibleName, StyleClasses = StyleClasses,
        DirectionalAnchor = ThumbnailAnchor, ArtworkHandle = Poster?.Value,ImageSource=PosterSource, ImageIsPublicMedia = PosterSource is not null && Media.Source.IsPublicMedia,
        ImageFit = Poster is null && PosterSource is null ? null : WidgetProtocol.ImageFit.Contain };
}
public static partial class UI
{
    /// <summary>Poster-first video loaded only after explicit Play. The host suspends it when its presentation leaves view.</summary>
    public static MediaPlayerElement InlineVideo(MediaPlayerSource source, string id, string accessibleName = "Video",
        MediaPlayerOptions? options = null, long revision = 1) => new(
            new(source, options ?? new(Muted: true), revision) { Presentation = MediaPlayerPresentation.Inline }, id, accessibleName);
    /// <summary>Bounded anonymous HTTPS artwork. Redirects must remain on the source origin; private destinations are rejected.</summary>
    public static ImageElement PublicImage(string source, string id, string accessibilityLabel, ImageFit fit = ImageFit.Cover)
    {
        if (!MediaPlayerSource.IsPublicMediaLocation(source)) throw new ArgumentException("Invalid public image source.", nameof(source));
        return Image(source, id, accessibilityLabel, fit) with { IsPublicMedia = true };
    }
    /// <summary>A decorative animation acquired through anonymous, bounded, same-origin HTTPS transport.</summary>
    public static MediaPlayerElement PublicAnimation(string url, string id, string accessibleName = "Animation") => new(
        new(MediaPlayerSource.PublicFile(url), new(AutoPlay: true, Loop: true, Muted: true) { IsAnimation = true })
            { Presentation = MediaPlayerPresentation.Animation }, id, accessibleName);
    /// <summary>A noninteractive, silent looping HTTPS animation. The host owns visible-only playback and reduced-motion fallback.
    /// Use an ordinary interactive media/gallery viewer for explicit playback controls.</summary>
    public static MediaPlayerElement Animation(string url,string id,string accessibleName="Animation")=>new(
        new(MediaPlayerSource.WebUrl(url),new(AutoPlay:true,Loop:true,Muted:true){IsAnimation=true}){Presentation=MediaPlayerPresentation.Animation},id,accessibleName);
    /// <summary>One native viewer with host-owned selection and resource lifetime for all supplied items.</summary>
    public static MediaPlayerElement MediaGallery(IReadOnlyList<MediaPlayerGalleryItem> items, string id,
        string accessibleName = "Media gallery", MediaPlayerOptions? options = null, long revision = 1)
        => MediaGallery(items, id, inlineCarousel: false, accessibleName, options, revision);

    /// <summary>Opt into an inline carousel with adjacent still previews around one active player.</summary>
    public static MediaPlayerElement MediaGallery(IReadOnlyList<MediaPlayerGalleryItem> items, string id,
        bool inlineCarousel, string accessibleName = "Media gallery", MediaPlayerOptions? options = null, long revision = 1)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > MediaPlayerDefinition.MaximumGalleryItems)
            throw new ArgumentOutOfRangeException(nameof(items));
        var snapshot = Array.AsReadOnly(items.ToArray());
        if (snapshot.Any(item => item is null)) throw new ArgumentException("Gallery items cannot be null.", nameof(items));
        return new(new(snapshot[0].Source, options ?? new(), revision) { Gallery = snapshot, InlineCarousel = inlineCarousel }, id, accessibleName);
    }
    public static MediaPlayerElement MediaPlayer(MediaPlayerSource source, string id, string accessibleName = "Media player",
        MediaPlayerOptions? options = null, long revision = 1) => new(new(source, options ?? new(), revision), id, accessibleName);
}
