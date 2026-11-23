using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task RetainedArtworkAuthorityAsync()
    {
        var surface = new WidgetViewPresenter { Width = 360, Height = 240 };
        host.Children.Add(surface);
        var bytes = await ArtworkPngAsync(640, 360);
        var firstCurrent = true; var nextCurrent = true; var reads = 0;
        surface.ResolveArtworkAsync = (_, _) =>
        { reads++; return Task.FromResult<WidgetArtworkSource?>(new WidgetEncodedArtwork(WidgetArtworkContentType.Png, bytes)); };
        surface.ArtworkAuthorityCurrent = () => firstCurrent;
        var node = new ViewNode { Id = "reboundimage", Kind = ViewNodeKind.Image, ArtworkHandle = "same.artwork", ImageFit = ImageFit.Fill,
            AccessibilityLabel = "Generated image" };
        var small = new Dictionary<string, BridgeNodeRenderStyles> { [node.Id] = Compute("#reboundimage { width:80px; height:45px; }", node.Id, "image") };
        var large = new Dictionary<string, BridgeNodeRenderStyles> { [node.Id] = Compute("#reboundimage { width:320px; height:180px; }", node.Id, "image") };
        try
        {
            surface.Apply(CreateFrame(node, small));
            await Wait(() => surface.BorrowDecodedArtwork("same.artwork") is BitmapImage);
            var first = (BitmapImage)surface.BorrowDecodedArtwork("same.artwork")!;
            Check(reads == 1, "Initial retained artwork resolves once under its admitted owner");
            firstCurrent = false;
            surface.ArtworkAuthorityCurrent = () => nextCurrent;
            surface.Apply(CreateFrame(node, small));
            Check(ReferenceEquals(first, surface.BorrowDecodedArtwork("same.artwork")), "Same-source rebinding preserves decoded pixels");
            surface.Apply(CreateFrame(node, large));
            await Wait(() => surface.BorrowDecodedArtwork("same.artwork") is BitmapImage image && !ReferenceEquals(image, first));
            Check(reads == 2, "Retained demand upgrades against the new authority callback rather than its retired predecessor");
            nextCurrent = false;
            Check(surface.BorrowDecodedArtwork("same.artwork") is null, "Retiring the new authority blocks borrowing retained pixels");
        }
        finally { await surface.DisposeAsync(); host.Children.Remove(surface); }
    }
}
