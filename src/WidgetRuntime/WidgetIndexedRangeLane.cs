using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>Bounded read-only work. Never occupies the serial action/presentation queue.</summary>
internal sealed class WidgetIndexedRangeLane(
    Widget widget,
    Func<RuntimeEnvelope, CancellationToken, Task> send,
    Action<Exception> failed,
    CancellationToken sessionToken)
{
    internal const int Capacity = 4;
    private readonly object gate = new();
    private readonly Dictionary<DemandKey, Operation> operations = [];
    private Task? closeTask;
    private bool closed;

    private sealed record DemandKey(string CollectionId, IndexedCollectionDescriptor Source, string DemandId, string? PinnedLayoutId)
    {
        internal static DemandKey From(IndexedCollectionRangeRequest request) =>
            new(request.CollectionId, request.Source, request.DemandId, request.PinnedLayoutId);
    }

    private sealed class Operation(IndexedCollectionRangeRequest request, CancellationToken sessionToken)
    {
        internal readonly IndexedCollectionRangeRequest Request = request;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? CancellationCompletion;
        internal bool Completing;
    }

    internal bool TryRead(long requestId, IndexedCollectionRangeRequest request, out string error)
    {
        IndexedCollectionContract.ValidateRequest(request);
        lock (gate)
        {
            var key = DemandKey.From(request);
            if (closed) { error = "worker_stopping"; return false; }
            if (operations.ContainsKey(key)) { error = "indexed_demand_duplicate"; return false; }
            if (operations.Count >= Capacity) { error = "indexed_range_busy"; return false; }
            var operation = new Operation(request, sessionToken);
            operations.Add(key, operation);
            // Even synchronous provider/declaration work must not execute on the pipe reader.
            _ = Task.Run(() => RunAsync(key, operation, requestId));
            error = string.Empty;
            return true;
        }
    }

    internal bool Cancel(IndexedCollectionRangeRequest request)
    {
        IndexedCollectionContract.ValidateRequest(request);
        lock (gate)
        {
            if (!operations.TryGetValue(DemandKey.From(request), out var operation) || operation.Request != request || operation.Completing) return false;
            CancelLocked(operation);
            return true;
        }
    }

    private static void CancelLocked(Operation operation)
    {
        if (!operation.Completing) operation.CancellationCompletion ??= operation.Cancellation.CancelAsync();
    }

    internal Task CloseAsync()
    {
        lock (gate)
        {
            if (closeTask is not null) return closeTask;
            closed = true;
            foreach (var operation in operations.Values) CancelLocked(operation);
            return closeTask = Task.WhenAll(operations.Values.Select(operation => operation.Finished.Task))
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private async Task RunAsync(DemandKey key, Operation operation, long requestId)
    {
        try
        {
            RuntimeEnvelope response;
            try
            {
                var range = await widget.ReadIndexedRangeAsync(operation.Request, operation.Cancellation.Token).ConfigureAwait(false);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                response = new() { Type = MessageTypes.IndexedRange, RequestId = requestId, Payload = RuntimeJson.ToElement(range) };
            }
            catch (OperationCanceledException)
            {
                response = Error(requestId, "indexed_range_cancelled", "The indexed range demand was cancelled.");
            }
            catch (Exception)
            {
                // Provider details never cross this new read boundary.
                response = Error(requestId, "indexed_range_failed", "The indexed range is unavailable or its authority changed.");
            }
            // Demand cancellation must never interrupt a partial frame. Session teardown owns writes.
            await send(response, sessionToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!sessionToken.IsCancellationRequested) failed(exception);
        }
        finally
        {
            Task? cancellation;
            lock (gate)
            {
                operation.Completing = true;
                cancellation = operation.CancellationCompletion;
            }
            try { if (cancellation is not null) await cancellation.ConfigureAwait(false); }
            catch (Exception exception) { if (!sessionToken.IsCancellationRequested) failed(exception); }
            finally
            {
                lock (gate)
                {
                    operation.Cancellation.Dispose();
                    operations.Remove(key);
                    operation.Finished.TrySetResult();
                }
            }
        }
    }

    internal static RuntimeEnvelope Error(long requestId, string code, string message) => new()
    {
        Type = MessageTypes.Error, RequestId = requestId, Payload = RuntimeJson.ToElement(new ErrorPayload(code, message)),
    };
}
