using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

internal sealed record WidgetActionExecutionBinding(object Identity,
    Func<WidgetActionEvent, CancellationToken, ValueTask> Invoke);
internal sealed record WidgetIndexedItemBinding(IndexedCollectionItem Declaration,
    Func<WidgetActionEvent, CancellationToken, ValueTask> Invoke,
    Func<WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? ResolveArtwork);
internal sealed record WidgetIndexedRead(IReadOnlyList<WidgetIndexedItemBinding> Items, int NodeCount);

public abstract partial class Widget
{
    internal const int MaximumIndexedLeases = 32;
    internal const int MaximumIndexedLeaseItems = 1024;
    internal const int MaximumIndexedLeaseNodes = 32768;
    private sealed record IndexedLeaseState(IndexedCollectionLease Lease, IndexedCollectionRangeRequest Request,
        IWidgetIndexedCollection Source, WidgetIndexedRead Read);
    private sealed record IndexedActionIdentity(string Source, long Query, string Collection, string? PinnedLayout, string Key);
    private int activeIndexedArtwork;
    private bool indexedLeaseOwnerRetired;
    private readonly Dictionary<string, IndexedLeaseState> indexedLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IndexedCollectionRangeRequest> indexedReservations = new(StringComparer.Ordinal);

    internal async ValueTask<IndexedCollectionLease> AcquireIndexedRangeAsync(IndexedCollectionRangeRequest request,
        CancellationToken cancellationToken)
    {
        IndexedCollectionContract.ValidateRequest(request);
        var id = Guid.NewGuid().ToString("N");
        lock (indexedSourcesGate)
        {
            ObjectDisposedException.ThrowIf(indexedLeaseOwnerRetired, this);
            RetireIndexedLeasesLocked();
            if (indexedReservations.Values.Any(pending => pending.DemandId == request.DemandId) ||
                indexedLeases.Values.Any(lease => lease.Request.DemandId == request.DemandId))
                throw new InvalidOperationException("The indexed semantic demand is already retained or pending.");
            if (indexedLeases.Count + indexedReservations.Count >= MaximumIndexedLeases ||
                indexedLeases.Values.Sum(lease => lease.Read.Items.Count) + indexedReservations.Values.Sum(demand => demand.Count) + request.Count > MaximumIndexedLeaseItems ||
                indexedLeases.Values.Sum(lease => lease.Read.NodeCount) + (indexedReservations.Count + 1) * IndexedCollectionLimits.MaximumRangeNodes > MaximumIndexedLeaseNodes)
                throw new InvalidOperationException("The indexed semantic retention budget is full.");
            indexedReservations.Add(id, request);
        }
        try
        {
            var parent = Volatile.Read(ref _latestSnapshot) ?? throw new InvalidOperationException("No parent presentation is available.");
            var scope = IndexedCollectionContract.ResolveScope(parent, request);
            IWidgetIndexedCollection source;
            lock (indexedSourcesGate)
                source = indexedSources.GetValueOrDefault(request.Source.SourceId) ?? throw new InvalidOperationException("Indexed source is not registered.");
            var read = await source.ReadAsync(request, cancellationToken).ConfigureAwait(false);
            var range = new IndexedCollectionRange(parent.WidgetInstanceId, request.CollectionId, request.Source, scope,
                request.StartIndex, request.DemandId, Array.AsReadOnly(read.Items.Select(item => item.Declaration).ToArray()), request.PinnedLayoutId);
            lock (indexedSourcesGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WidgetLifetimeToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(indexedLeaseOwnerRetired, this);
                parent = Volatile.Read(ref _latestSnapshot) ?? throw new InvalidOperationException("Parent presentation retired.");
                IndexedCollectionContract.ValidateRange(parent, request, range);
                if (source.Descriptor != request.Source) throw new InvalidOperationException("Indexed query retired.");
                foreach (var item in read.Items)
                    if (HasWidgetArtwork(item.Declaration.Root) && item.ResolveArtwork is null)
                        throw new InvalidOperationException("Indexed rows declaring widget-owned artwork require a captured artwork resolver.");
                var lease = new IndexedCollectionLease(id, range);
                indexedReservations.Remove(id);
                indexedLeases.Add(id, new(lease, request, source, read));
                return lease;
            }
        }
        finally { lock (indexedSourcesGate) indexedReservations.Remove(id); }
    }

    internal bool ReleaseIndexedDemand(IndexedCollectionRangeRequest request)
    {
        IndexedCollectionContract.ValidateRequest(request);
        lock (indexedSourcesGate)
        {
            var ids = indexedLeases.Where(pair => pair.Value.Request == request).Select(pair => pair.Key).ToArray();
            foreach (var id in ids) indexedLeases.Remove(id);
            return ids.Length != 0;
        }
    }

    internal bool ReleaseIndexedRange(string leaseId)
    {
        StableIdentifier.Validate(leaseId, nameof(leaseId));
        lock (indexedSourcesGate) return indexedLeases.Remove(leaseId);
    }

    // Null means stale/unbound input; an admission value belongs to the existing queue.
    internal WidgetOperationAdmission? AdmitIndexedInput(IndexedCollectionInputRequest input,
        IndexedCollectionInputContext correlation, WidgetCapabilityGestureContext? gestureContext = null)
    {
        IndexedCollectionInputContract.ValidateInput(input);
        IndexedCollectionInputContract.ValidateContext(correlation);
        WidgetActionEvent action;
        WidgetActionExecutionBinding? binding;
        lock (indexedSourcesGate)
        {
            RetireIndexedLeasesLocked();
            if (!indexedLeases.TryGetValue(input.Item.LeaseId, out var lease) || correlation.InputScopeId != lease.Lease.Range.ScopeId)
                return null;
            var item = lease.Read.Items.SingleOrDefault(item => item.Declaration.Key == input.Item.ItemKey);
            if (item is null) return null;
            var parent = Volatile.Read(ref _latestSnapshot)!;
            if (parent.Sequence != correlation.SnapshotSequence) return null;
            IReadOnlyList<ViewNode> currentOwners;
            try { currentOwners = IndexedCollectionInputContract.ResolveOwnerPath(parent, lease.Request); }
            catch (InvalidOperationException) { return null; }
            var current = IndexedCollectionInputContract.Resolve(currentOwners, item.Declaration.Root, input);
            if (current is null) return null;
            action = new WidgetActionEvent(current.ActionId, current.OwnerId, input.Button, input.Phase,
                correlation.Sequence, correlation.MonotonicTimestampMicroseconds, InputScopeId: lease.Lease.Range.ScopeId)
                {
                    FocusedElementId = item.Declaration.Root.Id,
                    FocusedCollectionItem = new(lease.Request.CollectionId, lease.Request.Source.SourceId,
                        lease.Request.Source.QueryGeneration, item.Declaration.Key,
                        lease.Request.StartIndex + lease.Read.Items.TakeWhile(candidate => candidate.Declaration.Key != item.Declaration.Key).Count()),
                };
            binding = current.IsItem ? new WidgetActionExecutionBinding(
                new IndexedActionIdentity(lease.Request.Source.SourceId, lease.Request.Source.QueryGeneration,
                    lease.Request.CollectionId, lease.Request.PinnedLayoutId, item.Declaration.Key), item.Invoke) : null;
            // Captured delegates retain only query/item values. Releasing the data
            // lease cannot retarget or cancel an already-admitted queued command.
        }
        return AdmitAction(action, gestureContext, binding);
    }

    internal async ValueTask<WidgetEncodedArtwork?> ResolveIndexedArtworkAsync(IndexedCollectionItemReference reference,
        string artworkHandle, CancellationToken cancellationToken)
    {
        IndexedCollectionInputContract.ValidateReference(reference);
        StableIdentifier.Validate(artworkHandle, nameof(artworkHandle));
        IndexedLeaseState lease;
        WidgetIndexedItemBinding item;
        CancellationTokenSource lifetime;
        lock (indexedSourcesGate)
        {
            RetireIndexedLeasesLocked();
            if (!indexedLeases.TryGetValue(reference.LeaseId, out lease!)) return null;
            item = lease.Read.Items.SingleOrDefault(candidate => candidate.Declaration.Key == reference.ItemKey)!;
            if (item?.ResolveArtwork is null || !ContainsArtwork(item.Declaration.Root, artworkHandle)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            WidgetLifetimeToken.ThrowIfCancellationRequested();
            if (activeIndexedArtwork >= 4) throw new InvalidOperationException("The indexed artwork read limit has been reached.");
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, WidgetLifetimeToken);
            lifetime.CancelAfter(TimeSpan.FromSeconds(10));
            ++activeIndexedArtwork;
        }
        var token = lifetime.Token;
        var load = Task.Run(async () => await item.ResolveArtwork(new(artworkHandle), token).ConfigureAwait(false), token);
        _ = load.ContinueWith(task =>
        {
            _ = task.Exception;
            lock (indexedSourcesGate) --activeIndexedArtwork;
            lifetime.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        WidgetEncodedArtwork? result;
        try { result = await load.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !WidgetLifetimeToken.IsCancellationRequested)
        { throw new TimeoutException("Indexed artwork resolution timed out."); }
        cancellationToken.ThrowIfCancellationRequested();
        WidgetLifetimeToken.ThrowIfCancellationRequested();
        lock (indexedSourcesGate)
        {
            RetireIndexedLeasesLocked();
            if (!indexedLeases.TryGetValue(reference.LeaseId, out var current) || !ReferenceEquals(current, lease)) return null;
        }
        if (result is not null && !WidgetEncodedArtworkContract.IsValid(result))
            throw new InvalidOperationException("The indexed artwork resolver returned invalid encoded artwork.");
        return result;
    }

    private void RetireIndexedLeases() { lock (indexedSourcesGate) RetireIndexedLeasesLocked(); }
    private void RetireIndexedLeasesLocked()
    {
        var parent = Volatile.Read(ref _latestSnapshot);
        var scopes = new Dictionary<(string Collection, IndexedCollectionDescriptor Source, string? Pinned), string?>();
        foreach (var (id, lease) in indexedLeases.ToArray())
        {
            var valid = !indexedLeaseOwnerRetired && parent is not null && !WidgetLifetimeToken.IsCancellationRequested &&
                parent.WidgetInstanceId == lease.Lease.Range.WidgetInstanceId && lease.Source.Descriptor == lease.Request.Source;
            if (valid)
            {
                var key = (lease.Request.CollectionId, lease.Request.Source, lease.Request.PinnedLayoutId);
                if (!scopes.TryGetValue(key, out var scope))
                {
                    try { scope = IndexedCollectionContract.ResolveScope(parent!, lease.Request); }
                    catch (Exception error) when (error is ArgumentException or ProtocolValidationException) { scope = null; }
                    scopes.Add(key, scope);
                }
                valid = scope == lease.Lease.Range.ScopeId;
            }
            if (!valid) indexedLeases.Remove(id);
        }
    }
    private void ClearIndexedLeases() { lock (indexedSourcesGate) { indexedLeaseOwnerRetired = true; indexedLeases.Clear(); } }
    private static bool HasWidgetArtwork(ViewNode node) => IsWidgetArtwork(node.ArtworkHandle) || IsWidgetArtwork(node.FocusBackgroundArtworkHandle) ||
        (node.FocusPresentation is { } focus && HasWidgetArtwork(focus)) || (node.DefaultFocusPresentation is { } fallback && HasWidgetArtwork(fallback)) || node.Children.Any(HasWidgetArtwork);
    private static bool IsWidgetArtwork(string? handle) => handle is not null && !WidgetRail.Internal.AppLibraryArtworkHandle.IsBrokerHandle(handle);
    private static bool ContainsArtwork(ViewNode node, string handle) => node.ArtworkHandle == handle || node.FocusBackgroundArtworkHandle == handle ||
        (node.FocusPresentation is { } focus && ContainsArtwork(focus, handle)) ||
        (node.DefaultFocusPresentation is { } fallback && ContainsArtwork(fallback, handle)) || node.Children.Any(child => ContainsArtwork(child, handle));
}
