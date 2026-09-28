using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>Shared range/artwork provider capacity available to a host demand scheduler.</summary>
    public int MaximumConcurrentIndexedRequests => IndexedProviderCapacity;
    private readonly SemaphoreSlim _indexedReleaseCapacity = new(4, 4);
    private readonly Dictionary<string, WidgetPresentationIndexedLease> _indexedLeases = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _indexedLeaseRetirements = [];
    private readonly Dictionary<string, IndexedArtworkDemand> _indexedArtworkDemands = new(StringComparer.Ordinal);
    private int _indexedLeaseReservations;
    private int _indexedLeaseReservedItems;
    private int _indexedLeaseRetiringItems;
    private int _indexedInputExchanges;
    private int IndexedProviderCapacity => Math.Min(4, (Math.Min(_options.MaximumPendingRequests, BridgePresentationTransport.MaximumWireRequests) - 1) / 2);

    private sealed class IndexedArtworkDemand(WidgetPresentationIndexedLease lease, BridgeIndexedArtworkRequest request,
        CancellationTokenSource lifetime)
    {
        internal readonly WidgetPresentationIndexedLease Lease = lease;
        internal readonly BridgeIndexedArtworkRequest Request = request;
        internal readonly CancellationTokenSource Lifetime = lifetime;
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static bool MatchesIndexedIdentity(WidgetPresentationAuthority authority, string widget, string instance,
        string runtime, string presentation) => authority.WidgetId == widget && authority.WidgetInstanceId == instance &&
        authority.RuntimeGeneration == runtime && authority.PresentationGeneration == presentation;

    internal bool IsIndexedLeaseCurrent(WidgetPresentationIndexedLease lease)
    {
        lock (_gate) return IsIndexedLeaseCurrentLocked(lease);
    }

    private bool IsIndexedLeaseCurrentLocked(WidgetPresentationIndexedLease lease) =>
        !lease.Retired && _indexedLeases.TryGetValue(lease.LeaseId, out var owned) && ReferenceEquals(owned, lease) &&
        IsIndexedOwnerCurrentLocked(lease.Authority, lease.Request.Range, lease.ScopeId);

    private void DemandIndexedLeaseLocked(WidgetPresentationIndexedLease lease)
    {
        if (!IsIndexedLeaseCurrentLocked(lease))
            throw new WidgetPresentationSessionException("indexed_retired", "The indexed lease is no longer current.");
    }

    internal Task ReleaseIndexedLeaseAsync(WidgetPresentationIndexedLease lease)
    {
        lock (_gate) RetireIndexedLeaseLocked(lease);
        return lease.Released.Task;
    }

    private void RetireIndexedLeasesLocked(string? widgetId, WidgetPresentationAuthority? retained)
    {
        foreach (var lease in _indexedLeases.Values.ToArray())
            if ((widgetId is null || lease.Authority.WidgetId == widgetId) &&
                (retained is null || !IsIndexedLeaseCurrentLocked(lease))) RetireIndexedLeaseLocked(lease);
    }

    private void RetireIndexedLeaseLocked(WidgetPresentationIndexedLease lease)
    {
        if (lease.Retired) return;
        lease.Retired = true;
        _indexedLeases.Remove(lease.LeaseId);
        _indexedLeaseRetirements.Add(lease.Released.Task);
        _indexedLeaseRetiringItems += lease.Range.Items.Count;
        // Never run cancellation callbacks or transport work beneath the session lock.
        _ = Task.Run(async () =>
        {
            try
            {
                await lease.Lifetime.CancelAsync().ConfigureAwait(false);
                var authority = lease.Authority;
                await _indexedReleaseCapacity.WaitAsync().ConfigureAwait(false);
                try
                {
                var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = _transport.RequestWithWriteCompletionAsync(BridgeMessageTypes.ReleaseIndexedLease,
                    new BridgeIndexedLeaseRequest(authority.WidgetId, authority.WidgetInstanceId,
                        authority.RuntimeGeneration, authority.PresentationGeneration, lease.LeaseId), written);
                ObserveIndexedReply(release); ObserveIndexedReply(written.Task);
                    // A healthy peer can be busy before this request reaches
                    // the wire. Bound that wait separately from its reply so a
                    // permanently occupied control lane cannot hang disposal.
                    await written.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await release.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (response.Type != BridgeMessageTypes.Acknowledged)
                    throw new BridgeProtocolException("WidgetBridge did not acknowledge indexed lease release.");
                RequireBooleanAcknowledgement(response.Payload, "released");
                }
                finally { _indexedReleaseCapacity.Release(); }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                FailTerminal(new WidgetPresentationSessionException("indexed_release_failed", "Indexed lease release could not be confirmed."));
                await _transport.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                lease.Lifetime.Dispose();
                lock (_gate) { _indexedLeaseRetirements.Remove(lease.Released.Task); _indexedLeaseRetiringItems -= lease.Range.Items.Count; }
                lease.Released.TrySetResult();
            }
        });
    }

    internal bool IndexedInputIsClaimed(WidgetPresentationIndexedLease lease, WidgetPresentationAuthority origin,
        string itemKey, ControllerButton button, ControllerEventPhase phase)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var input = new IndexedCollectionInputRequest(new(lease.LeaseId, itemKey), button, phase);
        IndexedCollectionInputContract.ValidateInput(input);
        lock (_gate)
        {
            DemandIndexedLeaseLocked(lease);
            DemandPinnedIndexedInputLocked(lease);
            _ = ValidateAuthority(origin);
            if (!SameIndexedOwner(origin, lease.Authority))
                throw new WidgetPresentationSessionException("indexed_input_stale", "Indexed input does not belong to this owner.");
            var item = lease.Range.Items.SingleOrDefault(candidate => candidate.Key == itemKey)
                ?? throw new WidgetPresentationSessionException("indexed_input_stale", "Indexed input item is no longer available.");
            var snapshot = _states[origin.WidgetId].LastGood!.Snapshot;
            IReadOnlyList<ViewNode> owners;
            try { owners = IndexedCollectionInputContract.ResolveOwnerPath(snapshot, lease.Request.Range); }
            catch (InvalidOperationException)
            { throw new WidgetPresentationSessionException("input_scope_stale", "The indexed row is outside the active projection scope."); }
            return IndexedCollectionInputContract.ClaimsInput(owners, item.Root, button, phase);
        }
    }

    internal async Task<WidgetOperationAdmission?> AdmitIndexedInputAsync(WidgetPresentationIndexedLease lease,
        WidgetPresentationAuthority origin, IndexedCollectionInputRequest input, IndexedCollectionInputContext context,
        CancellationToken cancellationToken, WidgetPresentationFrame? displayed = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IndexedCollectionInputContract.ValidateInput(input);
        IndexedCollectionInputContract.ValidateContext(context);
        var pinnedDispatch = lease.Request.Range.PinnedLayoutId is not null;
        if (pinnedDispatch) await _pinnedDispatch.WaitAsync(cancellationToken).ConfigureAwait(false);
        var exchangeOwnsDispatch = false;
        try
        {
            lock (_gate)
            {
                DemandIndexedLeaseLocked(lease);
                DemandPinnedIndexedInputLocked(lease);
                if (displayed is null) _ = ValidateAuthority(origin);
                else
                {
                    var owners = ValidateDisplayedIndexedInputLocked(lease, displayed);
                    var item = lease.Range.Items.SingleOrDefault(item => item.Key == input.Item.ItemKey)
                        ?? throw new WidgetPresentationSessionException("indexed_input_stale", "The indexed input item is outside its lease.");
                    DemandSameIndexedBinding(owners.Origin, owners.Current, item.Root, input);
                }
                if (!SameIndexedOwner(origin, lease.Authority) || input.Item.LeaseId != lease.LeaseId ||
                    !lease.Range.Items.Any(item => item.Key == input.Item.ItemKey))
                    throw new WidgetPresentationSessionException("indexed_input_stale", "Indexed input does not belong to this owner.");
                var snapshot = _states[origin.WidgetId].LastGood!.Snapshot;
                var activeScope = IndexedCollectionContract.ResolveActiveInputScope(snapshot, lease.Request.Range);
                if (activeScope != lease.ScopeId)
                    throw new WidgetPresentationSessionException("input_scope_stale", "The indexed row is outside the active projection scope.");
                if (_indexedInputExchanges >= Widget.ActionQueueCapacity)
                    throw new WidgetPresentationSessionException("indexed_input_saturated", "Indexed input admission is full.");
                ++_indexedInputExchanges;
            }
            var exchange = ExchangeAsync();
            exchangeOwnsDispatch = true;
            ObserveIndexedReply(exchange);
            return await exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { if (pinnedDispatch && !exchangeOwnsDispatch) _pinnedDispatch.Release(); }

        async Task<WidgetOperationAdmission?> ExchangeAsync()
        {
            try
            {
                // The transport retains correlation even if caller cancellation stops awaiting.
                var response = await _transport.RequestAsync(BridgeMessageTypes.IndexedInput,
                    new BridgeIndexedInputRequest(origin.WidgetId, origin.WidgetInstanceId, origin.RuntimeGeneration,
                        origin.PresentationGeneration, input, context), CancellationToken.None).ConfigureAwait(false);
                if (response.Type == BridgeMessageTypes.Error)
                {
                    var failure = ReadRequestFailure(response.Payload);
                    throw new WidgetPresentationSessionException(failure.Code, failure.Message);
                }
                if (response.Type != BridgeMessageTypes.Acknowledged)
                    throw new BridgeProtocolException("WidgetBridge returned an invalid indexed input result.");
                RequireObjectProperties(response.Payload, "admission");
                if (response.Payload.GetProperty("admission").ValueKind == JsonValueKind.Null) return null;
                var admission = response.Payload.GetProperty("admission").Deserialize<WidgetOperationAdmission>(BridgeJson.Options);
                if (admission is not (WidgetOperationAdmission.Enqueued or WidgetOperationAdmission.Joined or WidgetOperationAdmission.Replaced or
                    WidgetOperationAdmission.RejectedInactive or WidgetOperationAdmission.RejectedCapacity))
                    throw new BridgeProtocolException("WidgetBridge returned an invalid indexed input admission.");
                return admission;
            }
            finally
            {
                lock (_gate) --_indexedInputExchanges;
                if (pinnedDispatch) _pinnedDispatch.Release();
            }
        }
    }

    internal async Task<WidgetEncodedArtwork?> ResolveIndexedArtworkAsync(WidgetPresentationIndexedLease lease,
        string itemKey, string handle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reference = new IndexedCollectionItemReference(lease.LeaseId, itemKey);
        IndexedCollectionInputContract.ValidateReference(reference);
        StableIdentifier.Validate(handle, nameof(handle));
        IndexedArtworkDemand operation;
        lock (_gate)
        {
            DemandIndexedLeaseLocked(lease);
            var item = lease.Range.Items.SingleOrDefault(value => value.Key == itemKey);
            if (item is null || !ContainsIndexedArtwork(item.Root, handle))
                throw new WidgetPresentationSessionException("indexed_artwork_unavailable", "The artwork is not declared by this indexed item.");
            var capacity = IndexedProviderCapacity;
            if (_indexedArtworkDemands.Count >= capacity ||
                _indexedArtworkDemands.Count + _indexedDemands.Count >= capacity)
                throw new WidgetPresentationSessionException("indexed_artwork_saturated", "Indexed artwork admission is full.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, lease.Lifetime.Token);
            lifetime.CancelAfter(_options.ArtworkTimeout);
            var authority = lease.Authority;
            var request = new BridgeIndexedArtworkRequest(authority.WidgetId, authority.WidgetInstanceId,
                authority.RuntimeGeneration, authority.PresentationGeneration, reference, handle, Guid.NewGuid().ToString("N"));
            operation = new(lease, request, lifetime);
            _indexedArtworkDemands.Add(request.DemandId, operation);
        }
        Task<BridgeEnvelope>? reply = null;
        try
        {
            operation.Lifetime.Token.ThrowIfCancellationRequested();
            reply = _transport.RequestWithWriteCompletionAsync(BridgeMessageTypes.ResolveIndexedArtwork, operation.Request, operation.Written);
            ObserveIndexedReply(operation.Written.Task); ObserveIndexedReply(reply);
            var response = await reply.WaitAsync(operation.Lifetime.Token).ConfigureAwait(false);
            operation.Lifetime.Token.ThrowIfCancellationRequested();
            if (response.Type == BridgeMessageTypes.Error)
            {
                var failure = ReadRequestFailure(response.Payload);
                throw new WidgetPresentationSessionException(failure.Code, failure.Message);
            }
            if (response.Type != BridgeMessageTypes.IndexedArtwork)
                throw new BridgeProtocolException("WidgetBridge returned an invalid indexed artwork result.");
            var payload = BridgeJson.FromElement<BridgeIndexedArtworkResponse>(response.Payload);
            if (!MatchesIndexedIdentity(lease.Authority, payload.WidgetId, payload.InstanceId, payload.RuntimeGeneration, payload.PresentationGeneration) ||
                payload.Item != operation.Request.Item || payload.ArtworkHandle != handle || payload.DemandId != operation.Request.DemandId)
                throw new BridgeProtocolException("WidgetBridge returned foreign indexed artwork authority.");
            if (payload.ContentType == string.Empty && payload.ContentBase64.IsEmpty)
            {
                lock (_gate) { operation.Lifetime.Token.ThrowIfCancellationRequested(); DemandIndexedLeaseLocked(lease); }
                return null;
            }
            var type = WidgetEncodedArtworkContract.ParseContentType(payload.ContentType);
            if (type is null) throw new BridgeProtocolException("WidgetBridge returned unsupported indexed artwork.");
            var artwork = new WidgetEncodedArtwork(type.Value, payload.ContentBase64.ToArray());
            if (!WidgetEncodedArtworkContract.IsValid(artwork)) throw new BridgeProtocolException("WidgetBridge returned invalid indexed artwork bytes.");
            lock (_gate) { operation.Lifetime.Token.ThrowIfCancellationRequested(); DemandIndexedLeaseLocked(lease); }
            return artwork;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            lock (_gate)
                if (!IsIndexedLeaseCurrentLocked(lease))
                    throw new WidgetPresentationSessionException("indexed_retired", "The indexed artwork owner retired.");
            throw new WidgetPresentationSessionException("indexed_artwork_timeout", "Indexed artwork timed out.");
        }
        finally
        {
            try { if (reply is { IsCompleted: false }) await CancelIndexedArtworkDemandAsync(operation, reply).ConfigureAwait(false); }
            finally
            {
                lock (_gate) _indexedArtworkDemands.Remove(operation.Request.DemandId);
                operation.Lifetime.Dispose(); operation.Done.TrySetResult();
            }
        }
    }

    private async Task CancelIndexedArtworkDemandAsync(IndexedArtworkDemand operation, Task<BridgeEnvelope> original)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await operation.Written.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var cancellation = _transport.RequestAsync(BridgeMessageTypes.CancelIndexedArtwork, operation.Request, CancellationToken.None);
            ObserveIndexedReply(cancellation);
            var response = await cancellation.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (response.Type != BridgeMessageTypes.Acknowledged) throw new BridgeProtocolException("Indexed artwork cancellation was not acknowledged.");
            RequireBooleanAcknowledgement(response.Payload, "cancelled");
            _ = await original.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            FailTerminal(new WidgetPresentationSessionException("indexed_cancel_failed", "Indexed artwork cancellation could not be confirmed."));
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool ContainsIndexedArtwork(ViewNode node, string handle) =>
        node.ArtworkHandle == handle || node.FocusBackgroundArtworkHandle == handle ||
        node.Children.Any(child => ContainsIndexedArtwork(child, handle)) ||
        node.FocusPresentation is { } focused && ContainsIndexedArtwork(focused, handle) ||
        node.DefaultFocusPresentation is { } fallback && ContainsIndexedArtwork(fallback, handle);
    private static void RequireBooleanAcknowledgement(JsonElement payload, string property)
    {
        RequireObjectProperties(payload, property);
        if (payload.GetProperty(property).ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new BridgeProtocolException("WidgetBridge returned an invalid indexed acknowledgement.");
    }
}
