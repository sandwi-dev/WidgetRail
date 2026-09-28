using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

public sealed partial class WidgetProcessClient
{
    private int activeIndexedInputs;
    private int activeIndexedArtwork;

    private void DemandIndexedSession(WidgetProcessSession session, IndexedCollectionRangeRequest request, bool validateQuery = true)
    {
        if (_disposed || _stopping || !ReferenceEquals(Volatile.Read(ref _session), session) || !session.IsRunning || session.IsTerminal)
            throw new InvalidOperationException("The indexed worker session retired.");
        if (!validateQuery) return;
        var parent = Volatile.Read(ref _materializedSnapshot) ?? throw new InvalidOperationException("Indexed parent presentation retired.");
        _ = IndexedCollectionContract.ResolveScope(parent, request);
    }

    internal async Task<WidgetOperationAdmission?> AdmitIndexedInputAsync(WidgetProcessSession session,
        IndexedCollectionRangeRequest request, IndexedCollectionInputRequest input, IndexedCollectionInputContext correlation,
        CancellationToken cancellationToken)
    {
        IndexedCollectionInputContract.ValidateContext(correlation);
        cancellationToken.ThrowIfCancellationRequested();
        DemandIndexedSession(session, request);
        if (Interlocked.Increment(ref activeIndexedInputs) > Widget.ActionQueueCapacity)
        { Interlocked.Decrement(ref activeIndexedInputs); throw new InvalidOperationException("Indexed input admission is full."); }
        var execution = ExchangeAsync();
        ObserveIndexedOperation(execution);
        return await execution.WaitAsync(cancellationToken).ConfigureAwait(false);

        async Task<WidgetOperationAdmission?> ExchangeAsync()
        {
            try
            {
                var response = await ExchangeOwnedIndexedAsync(session, MessageTypes.IndexedInput, new IndexedInputPayload(input, correlation)).ConfigureAwait(false);
                DemandIndexedSession(session, request, validateQuery: false);
                if (response.Type != MessageTypes.Acknowledged) throw new WidgetProtocolViolationException("Invalid indexed input acknowledgement.");
                var admission = RuntimeJson.FromElement<IndexedInputAdmissionPayload>(response.Payload).Admission;
                if (admission is not (null or WidgetOperationAdmission.Enqueued or WidgetOperationAdmission.Joined or
                    WidgetOperationAdmission.Replaced or WidgetOperationAdmission.RejectedInactive or WidgetOperationAdmission.RejectedCapacity))
                    throw new WidgetProtocolViolationException("Invalid indexed input admission result.");
                return admission;
            }
            finally { Interlocked.Decrement(ref activeIndexedInputs); }
        }
    }

    internal async Task ReleaseIndexedLeaseAsync(WidgetProcessSession session, string leaseId)
    {
        if (_disposed || _stopping || session.IsTerminal || !session.IsRunning) return;
        try
        {
            var response = await ExchangeOwnedIndexedAsync(session, MessageTypes.ReleaseIndexedRange, new ReleaseIndexedRangePayload(leaseId)).ConfigureAwait(false);
            if (response.Type != MessageTypes.Acknowledged) throw new WidgetProtocolViolationException("Invalid indexed release acknowledgement.");
            _ = RuntimeJson.FromElement<IndexedReleaseResultPayload>(response.Payload);
        }
        catch (Exception) when (_disposed || _stopping || session.IsTerminal || !session.IsRunning) { }
        catch { session.Terminate(); throw; }
    }

    // Once written, an action may already be admitted. Caller cancellation only
    // stops waiting; the bounded exchange retains correlation until reply/teardown.
    private async Task<RuntimeEnvelope> ExchangeOwnedIndexedAsync<T>(WidgetProcessSession session, string type, T payload)
    {
        using var pending = session.PendingRequests.Register(type);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        try
        {
            await session.WriteAsync(new() { Type = type, RequestId = pending.RequestId, Payload = RuntimeJson.ToElement(payload) }, deadline.Token).ConfigureAwait(false);
            return await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (WidgetProcessException) { throw; }
        catch
        {
            // A partial frame or uncorrelated late reply cannot survive in a live session.
            session.Terminate(); throw;
        }
    }

    internal async Task<WidgetEncodedArtwork?> ResolveIndexedArtworkAsync(WidgetProcessSession session,
        IndexedCollectionRangeRequest request, IndexedCollectionItemReference item, string artworkHandle, CancellationToken cancellationToken)
    {
        IndexedCollectionInputContract.ValidateReference(item);
        StableIdentifier.Validate(artworkHandle, nameof(artworkHandle));
        cancellationToken.ThrowIfCancellationRequested();
        DemandIndexedSession(session, request);
        if (Interlocked.Increment(ref activeIndexedArtwork) > WidgetIndexedArtworkLane.Capacity)
        { Interlocked.Decrement(ref activeIndexedArtwork); throw new InvalidOperationException("Indexed artwork admission is full."); }
        using var pending = session.PendingRequests.Register(MessageTypes.ResolveIndexedArtwork);
        var sent = false; var completed = false;
        try
        {
            using (var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken))
            {
                writeDeadline.CancelAfter(_options.RequestTimeout);
                try
                {
                    await session.WriteAsync(new() { Type = MessageTypes.ResolveIndexedArtwork, RequestId = pending.RequestId,
                        Payload = RuntimeJson.ToElement(new ResolveIndexedArtworkPayload(item, artworkHandle)) }, writeDeadline.Token).ConfigureAwait(false);
                    sent = true;
                }
                catch { session.Terminate(); throw; }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            RuntimeEnvelope response;
            try { response = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (WidgetProcessException) { completed = true; throw; }
            completed = true;
            cancellationToken.ThrowIfCancellationRequested();
            DemandIndexedSession(session, request);
            if (response.Type != MessageTypes.Artwork) throw new WidgetProtocolViolationException("Invalid indexed artwork response.");
            var payload = RuntimeJson.FromElement<EncodedArtworkPayload>(response.Payload);
            if (payload.ContentType is null && payload.ContentBase64 is null) return null;
            if (payload.ContentBase64 is null || payload.ContentType is null ||
                WidgetEncodedArtworkContract.ParseContentType(payload.ContentType) is not { } contentType)
                throw new WidgetProtocolViolationException("Invalid indexed artwork metadata.");
            var result = new WidgetEncodedArtwork(contentType, Convert.FromBase64String(payload.ContentBase64));
            if (!WidgetEncodedArtworkContract.IsValid(result)) throw new WidgetProtocolViolationException("Invalid indexed artwork bytes.");
            return result;
        }
        finally
        {
            try
            {
                if (sent && !completed && !session.IsTerminal)
                {
                    try
                    {
                        var cancel = await ExchangeOwnedIndexedAsync(session, MessageTypes.CancelIndexedArtwork, new CancelIndexedArtworkPayload(pending.RequestId)).ConfigureAwait(false);
                        if (cancel.Type != MessageTypes.Acknowledged) throw new WidgetProtocolViolationException("Invalid indexed artwork cancellation acknowledgement.");
                        _ = RuntimeJson.FromElement<IndexedCancellationResultPayload>(cancel.Payload);
                        try { await pending.Response.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                        catch (WidgetProcessException) { }
                    }
                    catch { session.Terminate(); }
                }
            }
            finally { Interlocked.Decrement(ref activeIndexedArtwork); }
        }
    }

    private static void ObserveIndexedOperation(Task task) =>
        _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
