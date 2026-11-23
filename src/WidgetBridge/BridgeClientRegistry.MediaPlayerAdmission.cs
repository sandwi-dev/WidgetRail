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
            if (owner.Registration.CachedWorkerRun != request.WorkerRun || request.Media?.IsWellFormed() != true ||
                !request.Media.DeclaresGalleryItem(request.GalleryItemId) ||
                owner.Request.Range.PinnedLayoutId != request.PinnedLayoutId || owner.Active >= WidgetRail.WidgetSdk.Widget.ActionQueueCapacity)
                throw new BridgeProtocolException("Media preview ownership changed.");
            var inlineAnimation = request.Media.Presentation == MediaPlayerPresentation.Animation &&
                owner.Request.Range.Kind != IndexedCollectionRequestKind.FocusPreview;
            var item = owner.Runtime.Lease.Range.Items.SingleOrDefault(item => item.Key == origin.Item.ItemKey)
                ?? throw new BridgeProtocolException("Media preview occurrence retired.");
            var interactiveBodyMedia = ReadingGroupContentContract.IsInteractivePlayer(request.Media) && item.Root.IsReadingGroup &&
                owner.Request.Range.Kind != IndexedCollectionRequestKind.FocusPreview;
            // Passive body media has the same data authority as row artwork.
            // An inactive input scope (e.g. beneath a modal) is not a new grant
            // to play interactive media or bypass preview admission.
            var parent = inlineAnimation ? DemandIndexedOwnerLocked(owner) : DemandIndexedInputOwnerLocked(owner);
            if (interactiveBodyMedia && (item.Root.IsDisabled == true || item.Root.IsBusy == true ||
                ResolveIndexedInputOwners(parent, owner).Any(node => node.IndexedSelection?.IsActive == true)))
                throw new BridgeProtocolException("Inline media input is unavailable.");
            if (!inlineAnimation && !interactiveBodyMedia && ResolveIndexedInputOwners(parent, owner).LastOrDefault(node => node.Kind == ViewNodeKind.FocusPresentationSurface)?.FocusPreview is not { AllowMedia: true, EnterButton: not null })
                throw new BridgeProtocolException("Native media was not enabled for this preview.");
            var root = inlineAnimation || interactiveBodyMedia || owner.Request.Range.Kind == IndexedCollectionRequestKind.FocusPreview ? item.Root : item.Root.FocusPresentation;
            if (root is null || (inlineAnimation || interactiveBodyMedia ? FindAnimationBodyNode(root, request.ElementId) : FindNodeInScope(root, request.ElementId, true)) is not { } declaration ||
                interactiveBodyMedia && !ReadingGroupContentContract.IsInlineControl(declaration) ||
                MediaPlayerDefinition.FromNode(declaration) != request.Media)
                throw new BridgeProtocolException("Media was not declared by this exact preview occurrence.");
            ++owner.Active;
            return new(owner.Registration.Configured, () =>
            {
                lock (_gate)
                {
                    try
                    {
                        var currentParent = inlineAnimation ? DemandIndexedOwnerLocked(owner) : DemandIndexedInputOwnerLocked(owner);
                        return owner.Registration.CachedWorkerRun == request.WorkerRun &&
                            (!interactiveBodyMedia || !ResolveIndexedInputOwners(currentParent, owner).Any(node => node.IndexedSelection?.IsActive == true));
                    }
                    catch (Exception error) when (error is BridgeProtocolException or ArgumentException or ObjectDisposedException) { return false; }
                }
            }, owner.Cancellation.Token, () => { lock (_gate) EndIndexedOperationLocked(owner); });
        }
    }
    private static ViewNode? FindAnimationBodyNode(ViewNode node, string id) => node.Id == id ? node :
        node.Children.Select(child => FindAnimationBodyNode(child, id)).FirstOrDefault(value => value is not null);
}
