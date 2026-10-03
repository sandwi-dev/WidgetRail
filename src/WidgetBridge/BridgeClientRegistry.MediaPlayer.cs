using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    internal ConfiguredWidget DemandMediaPlayer(BridgeMediaPlayerRequest request)
    {
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_clients.TryGetValue(request.WidgetId, out var client) || !IsCurrentLocked(client) ||
                !client.HasCurrentSnapshotWorker || client.CachedWorkerRun != request.WorkerRun ||
                client.FindInputOriginSnapshot(request.SnapshotSequence) is not { } origin || client.CachedSnapshot is not { } current ||
                request.Media?.IsWellFormed() != true) throw new BridgeProtocolException("The media source retired.");
            if (request.PinnedLayoutId is { } layout)
            {
                if (!client.Configured.PinningSupported || layout == PinnedSurfaceContract.FullWidgetLayoutId && !client.Configured.FullWidgetPinningSupported)
                    throw new BridgeProtocolException("The media pin retired.");
                origin = PinnedActionContract.Project(origin, layout, inheritRootless: true);
                current = PinnedActionContract.Project(current, layout, inheritRootless: true);
            }
            if (Find(origin.Root, request.ElementId) != request.Media || Find(current.Root, request.ElementId) != request.Media)
                throw new BridgeProtocolException("The media source changed.");
            return client.Configured;
        }
        static MediaPlayerDefinition? Find(ViewNode node, string id) => node.Id == id ? MediaPlayerDefinition.FromNode(node) :
            node.Children.Select(child => Find(child, id)).FirstOrDefault(value => value is not null);
    }
}
