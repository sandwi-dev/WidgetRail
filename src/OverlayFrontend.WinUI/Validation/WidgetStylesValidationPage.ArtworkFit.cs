using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeArtworkFitAsync()
    {
        var bytes = await ArtworkPngAsync(480, 240);
        var requests = 0;
        presenter.ResolveArtworkAsync = (_, _) =>
        { ++requests; return Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, bytes)); };
        presenter.ArtworkGeneration = "fit-fixture";
        presenter.Width = 120; presenter.Height = 120;
        var node = new ViewNode { Id = "fit", Kind = ViewNodeKind.Image, ArtworkHandle = "fit.art",
            ImageFit = ImageFit.Contain, AccessibilityLabel = "Landscape image" };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
            { [node.Id] = Compute("image { object-fit: cover; }", node.Id, "image") };
        try
        {
            var frame = CreateFrame(node, styles);
            presenter.Apply(frame);
            await Wait(() => Find<WidgetArtworkView>("Widget.fit")?.Source is BitmapImage);
            var image = Find<WidgetArtworkView>("Widget.fit")!;
            presenter.UpdateLayout();
            await Task.Delay(80); // settle the existing low-priority size demand
            var source = image.Source;
            var priorRequests = requests;
            Check(image.Stretch == Stretch.UniformToFill,
                "computed object-fit takes precedence over the image declaration");
            var changed = new Dictionary<string, BridgeNodeRenderStyles>
                { [node.Id] = Compute("image { object-fit: contain; }", node.Id, "image") };
            presenter.Apply(frame with { AppearanceRevision = 1, RenderStyles = changed });
            presenter.UpdateLayout(); await Task.Delay(80);
            Check(image.Stretch == Stretch.Uniform && ReferenceEquals(source, image.Source) && requests == priorRequests,
                "appearance-only cover-to-contain change retains decoded pixels and does not refetch artwork");
            presenter.Apply(frame with { AppearanceRevision = 2, RenderStyles = new Dictionary<string, BridgeNodeRenderStyles>() });
            presenter.UpdateLayout(); await Task.Delay(80);
            Check(image.Stretch == Stretch.Uniform && ReferenceEquals(source, image.Source) && requests == priorRequests,
                "removing object-fit restores declared contain without replacing the image or source");

            // Native Stretch.None must receive intrinsic pixels. A target-sized
            // thumbnail would otherwise silently change its apparent natural size.
            changed[node.Id] = Compute("image { object-fit: none; }", node.Id, "image");
            presenter.Apply(frame with { AppearanceRevision = 3, RenderStyles = changed });
            await Wait(() => image.Source is BitmapImage { DecodePixelWidth: 480, DecodePixelHeight: 240 });
            Check(image.Stretch == Stretch.None && ReferenceEquals(image, Find<WidgetArtworkView>("Widget.fit")),
                "object-fit none uses native intrinsic geometry with full bounded source dimensions");
            priorRequests = requests;
            presenter.Apply(frame with { AppearanceRevision = 4, RenderStyles = changed });
            presenter.UpdateLayout(); await Task.Delay(80);
            Check(requests == priorRequests, "unchanged natural-fit style does not refetch intrinsic artwork");

            var background = new ViewNode { Id = "fit.surface", Kind = ViewNodeKind.BackgroundSurface,
                ArtworkHandle = "fit.art", ImageFit = ImageFit.Cover,
                Children = new ViewNode[] { new() { Id = "fit.label", Kind = ViewNodeKind.Text, Text = "Independent foreground" } } };
            var surfaceStyles = new Dictionary<string, BridgeNodeRenderStyles>
                { [background.Id] = Compute("* { object-fit: contain; }", background.Id, "backgroundSurface") };
            var surfaceFrame = CreateFrame(background, surfaceStyles);
            presenter.Apply(surfaceFrame);
            await Wait(() => Find<WidgetPresentationSurface>("Widget.fit.surface")?.ArtworkSource is not null);
            var surface = Find<WidgetPresentationSurface>("Widget.fit.surface")!;
            Check(surface.ArtworkMotion.View.Children.OfType<Border>().All(layer => ((ImageBrush)layer.Background).Stretch == Stretch.Uniform),
                "background layers share computed object-fit precedence without replacing their native crossfade");
            var backgroundSource = surface.ArtworkSource;
            surfaceStyles[background.Id] = Compute("* { object-fit: fill; }", background.Id, "backgroundSurface");
            presenter.Apply(surfaceFrame with { AppearanceRevision = 1, RenderStyles = surfaceStyles });
            await Wait(() => surface.ArtworkMotion.View.Children.OfType<Border>().All(layer => ((ImageBrush)layer.Background).Stretch == Stretch.Fill));
            Check(ReferenceEquals(surface, Find<WidgetPresentationSurface>("Widget.fit.surface")) && backgroundSource is not null,
                "appearance-only background fit updates retain the presentation surface and current pixels during any needed decode");
            await BackgroundArtworkStylesAsync();
            await OrdinaryArtworkStylesAsync();
        }
        finally
        {
            presenter.ResolveArtworkAsync = null; presenter.ArtworkGeneration = string.Empty;
            presenter.Width = double.NaN; presenter.Height = double.NaN;
        }
    }
}
