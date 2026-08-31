using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// One bounded, non-interactive image source for a <see cref="BackgroundSurfaceElement"/>.
/// It grants no file, network, window, focus, or input authority.
/// </summary>
public sealed class BackgroundSurfaceArtwork
{
    private BackgroundSurfaceArtwork(
        string? imageSource,
        WidgetArtworkHandle? artworkHandle,
        ImageFit fit)
    {
        ImageSource = imageSource;
        ArtworkHandle = artworkHandle;
        Fit = fit;
    }

    internal string? ImageSource { get; }
    internal WidgetArtworkHandle? ArtworkHandle { get; }
    internal ImageFit Fit { get; }

    public static BackgroundSurfaceArtwork FromHttps(
        string source,
        ImageFit fit = ImageFit.Cover)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateFit(fit);
        if (source.Length > ProtocolConstants.MaximumStringLength ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException(
                "Background artwork must be an absolute HTTPS URL without embedded credentials.",
                nameof(source));
        return new(source, null, fit);
    }

    public static BackgroundSurfaceArtwork FromInlinePng(
        string pngBase64,
        ImageFit fit = ImageFit.Cover)
    {
        ValidateFit(fit);
        return new(UI.CanonicalInlinePngSource(pngBase64, nameof(pngBase64)), null, fit);
    }

    public static BackgroundSurfaceArtwork FromHandle(
        WidgetArtworkHandle handle,
        ImageFit fit = ImageFit.Cover)
    {
        ValidateFit(fit);
        StableIdentifier.Validate(handle.Value, nameof(handle));
        return new(null, handle, fit);
    }

    private static void ValidateFit(ImageFit fit)
    {
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
    }
}

/// <summary>
/// Paints one optional bounded image behind one foreground subtree. The child
/// alone owns layout, focus, input, shortcuts, and accessibility semantics.
/// </summary>
public sealed record BackgroundSurfaceElement : WidgetElement
{
    internal BackgroundSurfaceElement(
        string id,
        WidgetElement content,
        BackgroundSurfaceArtwork? artwork) : base(RequireId(id))
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Artwork = artwork;
        StyleClasses = ["wrail-background-surface"];
    }

    public WidgetElement Content { get; init; }
    public BackgroundSurfaceArtwork? Artwork { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.BackgroundSurface,
        ImageSource = Artwork?.ImageSource,
        ArtworkHandle = Artwork?.ArtworkHandle?.Value,
        ImageFit = Artwork?.Fit,
        StyleClasses = StyleClasses,
        Children = [Content.ToProtocolNode()],
    };
}
