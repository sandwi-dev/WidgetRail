using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>Range preparation stays independent of serial action/presentation processing.</summary>
internal sealed class WidgetIndexedRangeLane
{
    internal const int Capacity = 4;
    private readonly Widget widget;
    private readonly Func<RuntimeEnvelope, CancellationToken, Task> send;
    private readonly WidgetWorkerAsyncLane<DemandKey, Request> lane;
    private sealed record Request(IndexedCollectionRangeRequest Range, bool Acquire);
    private sealed record DemandKey(string CollectionId, IndexedCollectionDescriptor Source, string DemandId, string? PinnedLayoutId)
    {
        internal static DemandKey From(IndexedCollectionRangeRequest request) =>
            new(request.CollectionId, request.Source, request.DemandId, request.PinnedLayoutId);
    }

    internal WidgetIndexedRangeLane(Widget widget, Func<RuntimeEnvelope, CancellationToken, Task> send,
        Action<Exception> failed, CancellationToken sessionToken)
    {
        this.widget = widget; this.send = send;
        lane = new(Capacity, RunAsync, failed, sessionToken);
    }

    internal bool TryRead(long requestId, IndexedCollectionRangeRequest request, out string error, bool acquire = false)
    {
        IndexedCollectionContract.ValidateRequest(request);
        var result = lane.TryStart(requestId, DemandKey.From(request), new(request, acquire));
        error = result switch { WorkerAsyncAdmission.Closed => "worker_stopping", WorkerAsyncAdmission.Duplicate => "indexed_demand_duplicate",
            WorkerAsyncAdmission.Full => "indexed_range_busy", _ => string.Empty };
        return result == WorkerAsyncAdmission.Accepted;
    }

    internal bool Cancel(IndexedCollectionRangeRequest request)
    {
        IndexedCollectionContract.ValidateRequest(request);
        var cancelled = lane.Cancel(DemandKey.From(request), operation => operation.Range == request);
        // A successfully written lease still belongs to this exact demand. This
        // handles cancellation arriving after acquisition but before host admission.
        return widget.ReleaseIndexedDemand(request) || cancelled;
    }

    internal Task CloseAsync() => lane.CloseAsync();

    private async Task RunAsync(long requestId, Request request, CancellationToken demand, CancellationToken session)
    {
        IndexedCollectionLease? lease = null;
        var delivered = false;
        try
        {
            RuntimeEnvelope response;
            try
            {
                if (request.Acquire)
                {
                    lease = await widget.AcquireIndexedRangeAsync(request.Range, demand).ConfigureAwait(false);
                    demand.ThrowIfCancellationRequested();
                    response = new() { Type = MessageTypes.IndexedLease, RequestId = requestId, Payload = RuntimeJson.ToElement(lease) };
                }
                else
                {
                    var range = await widget.ReadIndexedRangeAsync(request.Range, demand).ConfigureAwait(false);
                    demand.ThrowIfCancellationRequested();
                    response = new() { Type = MessageTypes.IndexedRange, RequestId = requestId, Payload = RuntimeJson.ToElement(range) };
                }
            }
            catch (OperationCanceledException) { response = Error(requestId, "indexed_range_cancelled", "The indexed range demand was cancelled."); }
            catch (Exception error) when (error is not OutOfMemoryException) { response = Error(requestId, "indexed_range_failed", "The indexed range is unavailable or its authority changed."); }
            // Demand cancellation cannot interrupt frame bytes. Session shutdown owns writes.
            await send(response, session).ConfigureAwait(false);
            delivered = response.Type == MessageTypes.IndexedLease;
        }
        finally
        {
            if (lease is not null && (!delivered || demand.IsCancellationRequested || session.IsCancellationRequested))
                widget.ReleaseIndexedRange(lease.LeaseId);
        }
    }

    internal static RuntimeEnvelope Error(long requestId, string code, string message) => new()
    {
        Type = MessageTypes.Error, RequestId = requestId, Payload = RuntimeJson.ToElement(new ErrorPayload(code, message)),
    };
}
