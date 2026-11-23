using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// Static decoded paint is immutable in this renderer. No visual parent,
// resolver, resource lease or animation owner is retained by this value.
internal sealed record WidgetStaticArtworkPaint(string Id, string Source, bool PublicMedia, BitmapImage Image);

internal sealed partial class WidgetViewPresenter
{
    private readonly Dictionary<Binding, WidgetStaticArtworkPaint> retainedStaticArtwork = [];

    internal IReadOnlyList<WidgetStaticArtworkPaint> CaptureStaticArtworkPaint()
    {
        if (disposed) return Array.Empty<WidgetStaticArtworkPaint>();
        var result = new List<WidgetStaticArtworkPaint>();
        foreach (var binding in bindings.Values)
            if (CaptureStaticArtwork(binding) is { } paint) result.Add(paint);
        return result;
    }

    private WidgetStaticArtworkPaint? CaptureStaticArtwork(Binding binding)
    {
        if (binding.Element is not WidgetArtworkView { Source: BitmapImage { IsAnimatedBitmap: false, PixelWidth: > 0, PixelHeight: > 0 } image } ||
            !declarations.TryGetValue(binding.Identity.Id, out var declaration) ||
            declaration.Node is not { ArtworkHandle: null, ImageSource: { } source } node) return null;
        var identity = node.ImageIsPublicMedia ? "public:" + source : source;
        // A replacement may still display the previous image while decoding.
        // Never label those pixels with the replacement's source identity.
        var decoded = imageDemands.TryGetValue(binding, out var demand) && demand.Completed && demand.Identity == identity &&
            ReferenceEquals(demand.Native?.LastDecode?.Image, image);
        var retained = retainedStaticArtwork.TryGetValue(binding, out var previous) &&
            ReferenceEquals(previous.Image, image) && Matches(previous, node);
        return decoded || retained ? new(binding.Identity.Id, source, node.ImageIsPublicMedia, image) : null;
    }

    internal void SeedStaticArtworkPaint(IReadOnlyList<WidgetStaticArtworkPaint>? paint)
    {
        if (disposed || paint is null) return;
        foreach (var value in paint)
            if (bindings.TryGetValue(value.Id, out var binding) && binding.Element is WidgetArtworkView { Source: null } image &&
                declarations.TryGetValue(value.Id, out var declaration) && Matches(value, declaration.Node))
                SetStaticArtworkPaint(binding, image, value);
    }

    private void RetainStaticArtworkPaint(Dictionary<string, Binding> next, Dictionary<string, Declaration> plan)
    {
        foreach (var (id, incoming) in next)
            if (incoming.Element is WidgetArtworkView { Source: null } image && bindings.TryGetValue(id, out var old) &&
                !ReferenceEquals(old, incoming) && CaptureStaticArtwork(old) is { } value &&
                plan.TryGetValue(id, out var declaration) && Matches(value, declaration.Node))
                SetStaticArtworkPaint(incoming, image, value);
    }

    private void SetStaticArtworkPaint(Binding binding, WidgetArtworkView image, WidgetStaticArtworkPaint paint)
    {
        retainedStaticArtwork[binding] = paint;
        image.Source = paint.Image;
    }

    private static bool Matches(WidgetStaticArtworkPaint paint, ViewNode node) => !paint.Image.IsAnimatedBitmap && node.Kind == ViewNodeKind.Image &&
        node.ArtworkHandle is null && node.ImageSource == paint.Source && node.ImageIsPublicMedia == paint.PublicMedia;
}
