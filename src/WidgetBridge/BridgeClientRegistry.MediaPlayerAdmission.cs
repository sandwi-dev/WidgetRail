using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    internal sealed class MediaPlayerAdmission(ConfiguredWidget widget, Func<bool> current, CancellationToken lifetime, Action release) : IDisposable
    {
        internal ConfiguredWidget Widget { get; } = widget;
        internal bool IsCurrent => current();
        internal CancellationToken Lifetime { get; } = lifetime;
        private Action? released = release;
        public void Dispose() => Interlocked.Exchange(ref released, null)?.Invoke();
    }

    internal MediaPlayerAdmission AdmitMediaPlayer(BridgeMediaPlayerRequest request)
    {
        if (request.IndexedOrigin is not { } origin)
        {
            var configured = DemandMediaPlayer(request);
            return new(configured, () =>
            {
                try { return DemandMediaPlayer(request).CatalogFingerprint == configured.CatalogFingerprint; }
                catch (Exception error) when (error is BridgeProtocolException or ObjectDisposedException) { return false; }
            }, CancellationToken.None, () => { });
        }
        IndexedCollectionInputContract.ValidateReference(origin.Item);
        lock (_gate)
        {
            var owner = FindIndexedOwnerLocked(request.WidgetId, origin.InstanceId, origin.RuntimeGeneration,
                origin.PresentationGeneration, origin.Item.LeaseId) ?? throw new BridgeProtocolException("Media preview lease retired.");
            var parent = DemandIndexedInputOwnerLocked(owner);
            if (owner.Registration.CachedWorkerRun != request.WorkerRun || request.Media?.IsWellFormed() != true ||
                owner.Request.Range.PinnedLayoutId != request.PinnedLayoutId || owner.Active >= WidgetRail.WidgetSdk.Widget.ActionQueueCapacity)
                throw new BridgeProtocolException("Media preview ownership changed.");
            var owners = ResolveIndexedInputOwners(parent, owner);
            if (owners.LastOrDefault(node => node.Kind == ViewNodeKind.FocusPresentationSurface)?.FocusPreview is not { AllowMedia: true, EnterButton: not null })
                throw new BridgeProtocolException("Native media was not enabled for this preview.");
            var item = owner.Runtime.Lease.Range.Items.SingleOrDefault(item => item.Key == origin.Item.ItemKey)
                ?? throw new BridgeProtocolException("Media preview occurrence retired.");
            var root = owner.Request.Range.Kind == IndexedCollectionRequestKind.FocusPreview ? item.Root : item.Root.FocusPresentation;
            if (root is null || FindNodeInScope(root, request.ElementId, true) is not { } declaration ||
                MediaPlayerDefinition.FromNode(declaration) != request.Media)
                throw new BridgeProtocolException("Media was not declared by this exact preview occurrence.");
            ++owner.Active;
            return new(owner.Registration.Configured, () =>
            {
                lock (_gate)
                {
                    try { _ = DemandIndexedInputOwnerLocked(owner); return owner.Registration.CachedWorkerRun == request.WorkerRun; }
                    catch (Exception error) when (error is BridgeProtocolException or ArgumentException or ObjectDisposedException) { return false; }
                }
            }, owner.Cancellation.Token, () => { lock (_gate) EndIndexedOperationLocked(owner); });
        }
    }
}
