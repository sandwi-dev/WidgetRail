using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal static class BridgeIndexedRangeValidation
{
    internal static void Validate(BridgeIndexedRangeRequest request)
    {
        if (request is null || !BridgeRequestKey.IsBoundedIdentifier(request.WidgetId) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.InstanceId) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration))
            throw new BridgeProtocolException("Indexed range identity is invalid.");
        try { IndexedCollectionContract.ValidateRequest(request.Range); }
        catch (ArgumentException) { throw new BridgeProtocolException("Indexed range request is invalid."); }
    }
}

internal sealed partial class BridgeClientRegistry
{
    private readonly Dictionary<BridgeIndexedRangeRequest, IndexedReadOperation> indexedReads = [];
    private const int MaximumIndexedReadsPerWidget = 4;
    private const int MaximumIndexedReads = 8;

    private sealed class IndexedReadOperation(ClientRegistration registration, CancellationToken cancellationToken)
    {
        internal readonly ClientRegistration Registration = registration;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        internal Task? CancellationCompletion;
        internal bool Completing;
        internal void Cancel()
        {
            if (!Completing) CancellationCompletion ??= Cancellation.CancelAsync();
        }
    }

    internal async Task<BridgeClientPublication<BridgeIndexedRangeResponse>> ReadIndexedRangeAsync(
        BridgeIndexedRangeRequest request, CancellationToken cancellationToken)
    {
        BridgeIndexedRangeValidation.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        ClientRegistration registration;
        IndexedReadOperation operation;
        BridgeClientPublication<ClientRegistration>? lease;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_clients.TryGetValue(request.WidgetId, out registration!) || !IsCurrentLocked(registration) ||
                !registration.Client.IsRunning)
                throw new BridgeProtocolException("Indexed ranges require an existing running presentation.");
            if (indexedReads.Count >= MaximumIndexedReads ||
                indexedReads.Values.Count(read => ReferenceEquals(read.Registration, registration)) >= MaximumIndexedReadsPerWidget)
                throw new BridgeProtocolException("Indexed range admission is full.");
            if (indexedReads.Keys.Any(existing => existing.WidgetId == request.WidgetId &&
                existing.Range.CollectionId == request.Range.CollectionId && existing.Range.Source == request.Range.Source &&
                existing.Range.DemandId == request.Range.DemandId && existing.Range.PinnedLayoutId == request.Range.PinnedLayoutId))
                throw new BridgeProtocolException("Indexed range demand is already active.");
            lease = AdmitPublicationLocked(registration, registration);
            operation = new(registration, cancellationToken);
            indexedReads.Add(request, operation);
        }
        try
        {
            var token = operation.Cancellation.Token;
            int workerStart;
            await registration.OperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate) DemandIndexedAuthorityLocked(registration, request);
                workerStart = registration.Client.Starts;
            }
            finally { registration.OperationGate.Release(); }

            // No serial request gate is held across provider I/O. The publication
            // lease prevents registration disposal until the actual read drains.
            var range = await registration.Client.ReadIndexedRangeAsync(request.Range, workerStart, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await registration.OperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    var parent = DemandIndexedAuthorityLocked(registration, request);
                    if (registration.Client.Starts != workerStart)
                        throw new BridgeProtocolException("Indexed range worker instance changed.");
                    IndexedCollectionContract.ValidateRange(parent, request.Range, range);
                    var result = lease.Map(_ => new BridgeIndexedRangeResponse(request.WidgetId, request.InstanceId,
                        request.RuntimeGeneration, request.PresentationGeneration, range));
                    lease = null;
                    return result;
                }
            }
            finally { registration.OperationGate.Release(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (exception is BridgeProtocolException) throw;
            throw new BridgeProtocolException("Indexed range demand was cancelled, unavailable, or changed authority.");
        }
        finally
        {
            Task? cancellation;
            lock (_gate)
            {
                operation.Completing = true;
                cancellation = operation.CancellationCompletion;
            }
            try { if (cancellation is not null) await cancellation.ConfigureAwait(false); }
            finally
            {
                lock (_gate) indexedReads.Remove(request);
                operation.Cancellation.Dispose();
                lease?.Dispose();
            }
        }
    }

    internal Task<bool> CancelIndexedRangeAsync(BridgeIndexedRangeRequest request)
    {
        BridgeIndexedRangeValidation.Validate(request);
        lock (_gate)
        {
            // Look up the exact captured demand; never resolve/create/restart a client.
            if (!indexedReads.TryGetValue(request, out var operation) || operation.Completing) return Task.FromResult(false);
            operation.Cancel();
            return Task.FromResult(true);
        }
    }

    private ViewSnapshot DemandIndexedAuthorityLocked(ClientRegistration registration, BridgeIndexedRangeRequest request)
    {
        var descriptor = registration.Configured.PublicDescriptor();
        if (!IsCurrentLocked(registration) || !registration.HasCurrentSnapshotWorker ||
            descriptor.InstanceId != request.InstanceId || descriptor.RuntimeGeneration != request.RuntimeGeneration ||
            descriptor.PresentationGeneration != request.PresentationGeneration ||
            registration.CachedSnapshot is not { } snapshot || snapshot.WidgetInstanceId != request.InstanceId)
            throw new BridgeProtocolException("Indexed range parent authority is stale.");
        try { _ = IndexedCollectionContract.ResolveScope(snapshot, request.Range); }
        catch (ArgumentException) { throw new BridgeProtocolException("Indexed range query authority is stale."); }
        return snapshot;
    }

    private void CancelIndexedReadsLocked(ClientRegistration registration)
    {
        foreach (var operation in indexedReads.Values)
            if (ReferenceEquals(operation.Registration, registration)) operation.Cancel();
    }

    private void CancelInvalidIndexedReadsLocked(ClientRegistration registration)
    {
        foreach (var (request, operation) in indexedReads)
        {
            if (!ReferenceEquals(operation.Registration, registration)) continue;
            try { _ = DemandIndexedAuthorityLocked(registration, request); }
            catch (Exception exception) when (exception is BridgeProtocolException or ProtocolValidationException)
            { operation.Cancel(); }
        }
    }

    private void CommitIndexedAwareSnapshot(ClientRegistration registration, ViewSnapshot snapshot)
    {
        lock (_gate)
        {
            registration.CommitCachedSnapshot(snapshot);
            CancelInvalidIndexedReadsLocked(registration);
        }
    }
}
