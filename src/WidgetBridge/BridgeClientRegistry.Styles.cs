using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    internal BridgeClientPublication<BridgePresentationStylesResponse> RefreshPresentationStyles(BridgePresentationStylesRequest request,
        Func<ConfiguredWidget, ViewSnapshot, BridgeResolvedStyleSnapshot> resolveStyles, CancellationToken cancellationToken)
    {
        ClientRegistration registration;
        ConfiguredWidget configured;
        ViewSnapshot snapshot;
        BridgeClientPublication<ClientRegistration> lifetime;
        lock (_gate)
        {
            DemandNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_clients.TryGetValue(request.WidgetId, out registration!) || !IsCurrentLocked(registration) ||
                !registration.HasCurrentSnapshotWorker || registration.Configured.InstanceId != request.InstanceId ||
                registration.Configured.PublicDescriptor().RuntimeGeneration != request.RuntimeGeneration ||
                registration.Configured.PublicDescriptor().PresentationGeneration != request.PresentationGeneration ||
                registration.FindInputOriginSnapshot(request.SnapshotSequence) is not { } retained)
                throw new BridgeProtocolException("Style refresh presentation is unavailable.");
            configured = registration.Configured;
            snapshot = retained;
            lifetime = AdmitPublicationLocked(registration, registration);
        }
        using (lifetime)
        {
            var resolved = resolveStyles(configured, snapshot);
            if (resolved.Revision < 0) throw new BridgeProtocolException("Invalid appearance revision.");
            var styles = BridgeRenderStyleContract.ValidateAndFreeze(resolved.RenderStyles,
                BridgeRenderStyleContract.SnapshotNodeIds(snapshot), requireComplete: true);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentLocked(registration) || !registration.HasCurrentSnapshotWorker ||
                    !ReferenceEquals(configured, registration.Configured) ||
                    !ReferenceEquals(snapshot, registration.FindInputOriginSnapshot(request.SnapshotSequence)))
                    throw new BridgeProtocolException("Style refresh presentation retired before publication.");
                return AdmitPublicationLocked(registration, new BridgePresentationStylesResponse(request.WidgetId,
                    request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.SnapshotSequence,
                    resolved.Revision, styles));
            }
        }
    }
}
