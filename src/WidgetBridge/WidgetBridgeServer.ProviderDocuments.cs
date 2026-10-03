using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandleProviderDocumentAsync(BridgeEnvelope envelope, CancellationToken token)
    {
        var request = BridgeJson.FromElement<BridgeProviderDocumentRequest>(envelope.Payload);
        DemandWorker();
        var (catalog, _) = _registry.CatalogSnapshot();
        var widget = catalog.GetConfigured(request.WidgetId);
        var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
        if (_consentStore is null || !widget.DeclaredCapabilities.Contains(PlatformCapabilities.ProviderDocumentsV1) ||
            await _consentStore.GetDecisionAsync(identity, PlatformCapabilities.ProviderDocumentsV1, token).ConfigureAwait(false) != ConsentDecision.Grant)
            throw new BrokerException("permission_denied", "Provider document permission is unavailable.");
        DemandWorker();
        var html = ProviderDocumentRegistry.Shared.Resolve(identity, request.Reference);
        await ReplyAsync(envelope.Type, envelope.RequestId, new BridgeProviderDocument(request.Reference, html), token).ConfigureAwait(false);
        void DemandWorker()
        {
            if (request.Reference?.IsWellFormed() != true || !_registry.IsCaptureWorkerCurrent(request.WidgetId, request.WorkerRun))
                throw new BrokerException("attribution_unavailable", "Provider document worker retired.");
        }
    }
}
