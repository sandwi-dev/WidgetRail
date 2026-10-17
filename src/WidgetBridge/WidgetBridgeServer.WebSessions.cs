using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandleWebSessionsAsync(BridgeEnvelope request, CancellationToken token)
    {
        var registry = WebSessionRegistry.Shared;
        if (request.Type == BridgeMessageTypes.CheckWebSessionView)
        {
            var view = BridgeJson.FromElement<BridgeWebSessionViewRequest>(request.Payload);
            var current = view.WorkerRun is not null && _registry.IsCaptureWorkerCurrent(view.WidgetId, view.WorkerRun) && registry.IsOwned(view.WidgetId, view.Session);
            await ReplyAsync(request.Type, request.RequestId, new BridgeWebSessionViewResult(current), token).ConfigureAwait(false);
        }
        else if (request.Type == BridgeMessageTypes.TakeWebSessions)
        {
            var batch = await registry.TakeAsync(token).ConfigureAwait(false);
            var result = new List<BridgeWebSessionWork>();
            foreach (var item in batch.Work)
            {
                if (item.Command.Operation == WebSessionOperation.Close) { result.Add(new(item, [])); continue; }
                if (!registry.IsCurrent(item)) continue;
                try
                {
                    var (catalog, _) = _registry.CatalogSnapshot();
                    var configured = catalog.GetConfigured(item.WidgetId);
                    if (configured.InstanceId != item.Identity.InstanceId || configured.PackageId != item.Identity.PackageId ||
                        configured.PublisherId != item.Identity.PublisherId) throw new BridgeProtocolException("Web session owner retired.");
                    var declared = item.Command.Definition?.Source is { Kind: WebSessionSourceKind.Package } source
                        ? source.Resources : item.Command.Definition?.PageAdapter is { } adapter
                            ? [new EmbeddedMediaResource { Path = adapter.ScriptAsset, ContentType = "text/javascript" }] : [];
                    var resources = declared.Count > 0 ? EmbeddedMediaAssetResolver.ResolveResources(configured, declared) : [];
                    if (registry.IsCurrent(item)) result.Add(new(item, resources.Select(r =>
                        new BridgeWebSessionResource(r.Path, r.ContentType, r.Sha256, r.ContentBase64)).ToArray()));
                }
                catch (Exception error) when (error is BridgeProtocolException or KeyNotFoundException or InvalidOperationException)
                { registry.Complete(new(item.RequestId, "session_assets_unavailable")); }
            }
            await ReplyAsync(request.Type, request.RequestId, new BridgeWebSessionBatch(result, batch.LiveSessions), token).ConfigureAwait(false);
        }
        else if (request.Type == BridgeMessageTypes.CompleteWebSession)
        {
            registry.Complete(BridgeJson.FromElement<HostWebSessionResult>(request.Payload));
            await ReplyAsync(request.Type, request.RequestId, new BridgeEmptyPayload(), token).ConfigureAwait(false);
        }
        else
        {
            registry.Publish(BridgeJson.FromElement<HostWebSessionMessages>(request.Payload));
            await ReplyAsync(request.Type, request.RequestId, new BridgeEmptyPayload(), token).ConfigureAwait(false);
        }
    }
}
