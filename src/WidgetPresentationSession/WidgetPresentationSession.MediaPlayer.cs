using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public MediaPlayerDefinition ResolveMediaPlayer(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null)
    {
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            var origin = displayed.Snapshot; var latest = current.Snapshot;
            if (selection is not null || projection is not null)
            {
                if (selection is null || projection is null || !ReferenceEquals(projection.Frame, displayed)) throw MediaStale();
                latest = DemandPinnedInputLocked(selection, projection); origin = projection.Snapshot;
            }
            var before = Find(origin.Root, elementId); var after = Find(latest.Root, elementId);
            if (displayed.Authority.WorkerRun is null || displayed.Authority.WorkerRun != current.Authority.WorkerRun ||
                before is null || before != after || !before.IsWellFormed()) throw MediaStale();
            return before;
        }
        static MediaPlayerDefinition? Find(ViewNode node, string id) => node.Id == id ? MediaPlayerDefinition.FromNode(node) :
            node.Children.Select(child => Find(child, id)).FirstOrDefault(value => value is not null);
    }
    public async Task<HostMediaPlayerSource> ResolveMediaPlayerSourceAsync(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null, CancellationToken token = default)
    {
        var media = ResolveMediaPlayer(displayed, elementId, selection, projection);
        var request = new BridgeMediaPlayerRequest(displayed.Authority.WidgetId, displayed.Authority.WorkerRun!,
            displayed.Authority.SnapshotSequence, elementId, media, selection?.LayoutId);
        var response = await RequestAsync(BridgeMessageTypes.ResolveMediaPlayer, request, BridgeMessageTypes.ResolveMediaPlayer, token).ConfigureAwait(false);
        var source = BridgeJson.FromElement<HostMediaPlayerSource>(response.Payload);
        if (ResolveMediaPlayer(displayed, elementId, selection, projection) != media || source.Media != media ||
            !Uri.TryCreate(source.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("file" or "http" or "https") ||
            media.Source.Kind == MediaPlayerSourceKind.WebUrl && source.Uri != media.Source.Location) throw MediaStale();
        return source;
    }
    private static WidgetPresentationSessionException MediaStale() => new("media_player_stale", "The displayed media source retired or changed.");
}
