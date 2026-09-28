using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetRuntime;

public sealed partial class WidgetProcessClient
{
    private readonly object indexedGate = new();
    private readonly Dictionary<IndexedCollectionRangeRequest, IndexedRead> indexedReads = [];
    private sealed class IndexedRead(CancellationTokenSource cancellation)
    {
        internal readonly CancellationTokenSource Cancellation = cancellation;
        internal Task<bool>? CancellationCompletion;
    }

    /// <summary>
    /// Reads declarative items without granting action authority. Query, scope and
    /// generation must still match the currently published parent when the reply arrives.
    /// </summary>
    public Task<IndexedCollectionRange> ReadIndexedRangeAsync(
        IndexedCollectionRangeRequest request, CancellationToken cancellationToken = default) =>
        ReadIndexedRangeCoreAsync(request, null, cancellationToken);

    internal Task<IndexedCollectionRange> ReadIndexedRangeAsync(
        IndexedCollectionRangeRequest request, int expectedStartOrdinal, CancellationToken cancellationToken) =>
        ReadIndexedRangeCoreAsync(request, expectedStartOrdinal, cancellationToken);

    private async Task<IndexedCollectionRange> ReadIndexedRangeCoreAsync(
        IndexedCollectionRangeRequest request, int? expectedStartOrdinal, CancellationToken cancellationToken)
    {
        IndexedCollectionContract.ValidateRequest(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = new IndexedRead(demand);
        lock (indexedGate)
        {
            if (indexedReads.Count >= WidgetIndexedRangeLane.Capacity) throw new InvalidOperationException("The indexed range request limit has been reached.");
            if (!indexedReads.TryAdd(request, operation)) throw new InvalidOperationException("The indexed range demand is already pending.");
        }
        WidgetProcessSession? session = null;
        WidgetPendingRequests.WidgetPendingRequest? pending = null;
        var sent = false;
        var completed = false;
        try
        {
            if (expectedStartOrdinal is null) await EnsureConnectedAsync(demand.Token).ConfigureAwait(false);
            await _lifecycleGate.WaitAsync(demand.Token).ConfigureAwait(false);
            try
            {
                session = Volatile.Read(ref _session) ?? throw new IOException("Widget pipe disconnected.");
                if (!session.IsRunning || _stopping || _disposed ||
                    expectedStartOrdinal is { } expected && (expected <= 0 || Starts != expected))
                    throw new InvalidOperationException("The expected indexed worker session is no longer running.");
            }
            finally { _lifecycleGate.Release(); }
            var parent = Volatile.Read(ref _materializedSnapshot) ?? throw new InvalidOperationException("No parent presentation is available.");
            _ = IndexedCollectionContract.ResolveScope(parent, request);
            pending = session.PendingRequests.Register(MessageTypes.ReadIndexedRange);
            // Only transport/session deadlines may interrupt writing a frame.
            using (var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken))
            {
                writeDeadline.CancelAfter(_options.RequestTimeout);
                try
                {
                    await session.WriteAsync(new RuntimeEnvelope
                    {
                        Type = MessageTypes.ReadIndexedRange, RequestId = pending.RequestId, Payload = RuntimeJson.ToElement(request),
                    }, writeDeadline.Token).ConfigureAwait(false);
                    sent = true;
                }
                catch { session.Terminate(); throw; }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(demand.Token, session.CancellationToken);
            // Provider deadlines can be up to 30 seconds, independently of action/render timeouts.
            deadline.CancelAfter(TimeSpan.FromSeconds(35));
            RuntimeEnvelope response;
            try { response = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (WidgetProcessException) { completed = true; throw; }
            catch (OperationCanceledException) when (!demand.IsCancellationRequested && !session.CancellationToken.IsCancellationRequested)
            { throw new TimeoutException("The indexed range response timed out."); }
            completed = true;
            demand.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _session), session) || session.IsTerminal ||
                expectedStartOrdinal is { } expectedOrdinal && Starts != expectedOrdinal)
                throw new OperationCanceledException("The indexed range session retired.");
            if (response.Type != MessageTypes.IndexedRange) throw new WidgetProtocolViolationException("The worker returned an unexpected indexed range response.");
            var result = RuntimeJson.FromElement<IndexedCollectionRange>(response.Payload);
            parent = Volatile.Read(ref _materializedSnapshot) ?? throw new InvalidOperationException("The parent presentation retired.");
            IndexedCollectionContract.ValidateRange(parent, request, result);
            return result;
        }
        finally
        {
            try
            {
                if (sent && !completed && session is { IsTerminal: false })
                    await CancelConnectedIndexedRangeAsync(session, request, pending!.Response).ConfigureAwait(false);
            }
            finally
            {
                pending?.Dispose();
                Task<bool>? cancellation;
                lock (indexedGate)
                {
                    indexedReads.Remove(request);
                    cancellation = operation.CancellationCompletion;
                }
                if (cancellation is not null) await cancellation.ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Cancels only an active matching demand. Its read owner sends cancellation
    /// to the exact captured worker session after completing any in-progress frame.
    /// A missing/finished demand never starts a worker or targets its replacement.
    /// </summary>
    public Task<bool> CancelIndexedRangeAsync(IndexedCollectionRangeRequest request)
    {
        IndexedCollectionContract.ValidateRequest(request);
        lock (indexedGate)
        {
            if (!indexedReads.TryGetValue(request, out var operation)) return Task.FromResult(false);
            // CancelAsync marks the token synchronously; callbacks execute asynchronously.
            return operation.CancellationCompletion ??= CancelAsync(operation.Cancellation);
        }
        static async Task<bool> CancelAsync(CancellationTokenSource cancellation)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            return true;
        }
    }

    private static async Task CancelConnectedIndexedRangeAsync(WidgetProcessSession session, IndexedCollectionRangeRequest request, Task<RuntimeEnvelope> originalResponse)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var pending = session.PendingRequests.Register(MessageTypes.CancelIndexedRange);
        try
        {
            await session.WriteAsync(new RuntimeEnvelope
            {
                Type = MessageTypes.CancelIndexedRange, RequestId = pending.RequestId, Payload = RuntimeJson.ToElement(request),
            }, deadline.Token).ConfigureAwait(false);
            var response = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (response.Type != MessageTypes.Acknowledged)
                throw new WidgetProtocolViolationException("The worker did not acknowledge indexed cancellation.");
            // Retain correlation until the original demand has a terminal response.
            // Otherwise its late cancellation reply would become an unknown request ID.
            try { _ = await originalResponse.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (WidgetProcessException) { }
        }
        catch
        {
            // A lost cancellation boundary cannot leave unidentified work in a live session.
            session.Terminate();
        }
    }
}
