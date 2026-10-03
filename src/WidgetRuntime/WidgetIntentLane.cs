using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>One bounded intent handler; cancellation must not wait behind author code.</summary>
internal sealed class WidgetIntentLane
{
    private readonly WidgetWorkerAsyncLane<long, IntentDeliveryPayload> lane;
    internal WidgetIntentLane(Widget widget, Func<RuntimeEnvelope, CancellationToken, Task> send,
        Action<Exception> failed, CancellationToken sessionToken)
    {
        lane = new(1, async (requestId, request, demand, session) =>
        {
            RuntimeEnvelope response;
            try
            {
                var controlResult = request.Control && await widget.ApplyIntentControlAsync(request.Intent, request.Feedback, demand).ConfigureAwait(false);
                var accepted = request.Control ? WidgetRail.WidgetProtocol.WidgetIntentResult.Rejected : await widget.ApplyIntentAsync(request.DeliveryId, request.Intent, demand).ConfigureAwait(false);
                demand.ThrowIfCancellationRequested();
                response = new() { Type = MessageTypes.IntentResult, RequestId = requestId,
                    Payload = RuntimeJson.ToElement(new IntentDeliveryResultPayload(accepted, controlResult)) };
            }
            catch (OperationCanceledException)
            { response = WidgetIndexedRangeLane.Error(requestId, "intent_cancelled", "Intent delivery was cancelled."); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { response = WidgetIndexedRangeLane.Error(requestId, "intent_failed", "The widget could not accept this intent."); }
            await send(response, session).ConfigureAwait(false);
        }, failed, sessionToken);
    }

    internal bool TryDeliver(long requestId, IntentDeliveryPayload request)
    {
        if (request.DeliveryId <= 0 || request.Intent is null || !request.Intent.IsWellFormed())
            throw new ArgumentException("Invalid intent delivery.");
        return lane.TryStart(requestId, request.DeliveryId, request) == WorkerAsyncAdmission.Accepted;
    }
    internal Task<bool> CancelAsync(long deliveryId) => deliveryId > 0 ? lane.CancelAndDrainAsync(deliveryId) : Task.FromResult(false);
    internal Task CloseAsync() => lane.CloseAsync();
}
