using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetIndexedArtworkLane
{
    internal const int Capacity = 4;
    private readonly Widget widget;
    private readonly Func<long, WidgetEncodedArtwork?, CancellationToken, Task> sendArtwork;
    private readonly Func<RuntimeEnvelope, CancellationToken, Task> send;
    private readonly WidgetWorkerAsyncLane<long, ResolveIndexedArtworkPayload> lane;
    internal WidgetIndexedArtworkLane(Widget widget, Func<long, WidgetEncodedArtwork?, CancellationToken, Task> sendArtwork,
        Func<RuntimeEnvelope, CancellationToken, Task> send, Action<Exception> failed, CancellationToken session)
    {
        this.widget = widget; this.sendArtwork = sendArtwork; this.send = send;
        lane = new(Capacity, RunAsync, failed, session);
    }
    internal bool TryRead(long requestId, ResolveIndexedArtworkPayload request, out string error)
    {
        IndexedCollectionInputContract.ValidateReference(request.Item);
        StableIdentifier.Validate(request.ArtworkHandle, nameof(request.ArtworkHandle));
        var result = lane.TryStart(requestId, requestId, request);
        error = result switch { WorkerAsyncAdmission.Closed => "worker_stopping", WorkerAsyncAdmission.Duplicate => "indexed_artwork_duplicate",
            WorkerAsyncAdmission.Full => "indexed_artwork_busy", _ => string.Empty };
        return result == WorkerAsyncAdmission.Accepted;
    }
    internal bool Cancel(long requestId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestId);
        return lane.Cancel(requestId, _ => true);
    }
    internal Task CloseAsync() => lane.CloseAsync();
    private async Task RunAsync(long requestId, ResolveIndexedArtworkPayload request, CancellationToken demand, CancellationToken session)
    {
        WidgetEncodedArtwork? artwork;
        try
        {
            artwork = await widget.ResolveIndexedArtworkAsync(request.Item, request.ArtworkHandle, demand).ConfigureAwait(false);
            demand.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        { await send(WidgetIndexedRangeLane.Error(requestId, "indexed_artwork_cancelled", "The indexed artwork demand was cancelled."), session).ConfigureAwait(false); return; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { await send(WidgetIndexedRangeLane.Error(requestId, "indexed_artwork_failed", "The indexed artwork is unavailable or retired."), session).ConfigureAwait(false); return; }
        await sendArtwork(requestId, artwork, session).ConfigureAwait(false);
    }
}
