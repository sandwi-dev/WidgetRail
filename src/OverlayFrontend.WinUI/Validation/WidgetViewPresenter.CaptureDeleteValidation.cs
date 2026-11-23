using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    internal Func<MediaPlayerDefinition, CancellationToken, Task<HostMediaPlayerSource>>? ValidationMediaResolver { get; set; }
    private bool TryApplyValidationMedia(MediaPlayerView media, ViewNode node)
    {
        if (ValidationMediaResolver is not { } resolver || !presentationActive || disposed || presentationOnly || MediaPlayerDefinition.FromNode(node) is not { } definition) return false;
        media.Apply(token => resolver(definition, token), definition,
            () => !disposed && declarations.TryGetValue(node.Id, out var current) && MediaPlayerDefinition.FromNode(current.Node) == definition, this,
            resolveGalleryItem: (key, _, token) => resolver(definition.SelectGalleryItem(key), token));
        return true;
    }
}
