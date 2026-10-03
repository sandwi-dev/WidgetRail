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
    internal override ViewNode ToProtocolNode() => new()
    { Id = Id, Kind = ViewNodeKind.MediaPlayer, MediaPlayer = Media, AccessibilityLabel = AccessibleName, StyleClasses = StyleClasses };
}
public static partial class UI
{
    public static MediaPlayerElement MediaPlayer(MediaPlayerSource source, string id, string accessibleName = "Media player",
        MediaPlayerOptions? options = null, long revision = 1) => new(new(source, options ?? new(), revision), id, accessibleName);
}
