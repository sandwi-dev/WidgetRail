using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetRuntime;

public sealed partial class WidgetProcessClient
{
    private long _intentDeliverySequence;
    private int _intentDeliveryPending;

    internal async Task<bool> DeliverIntentAsync(WidgetIntentRequest intent,
        int expectedStartOrdinal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!intent.IsWellFormed()) throw new ArgumentException("Invalid intent request.", nameof(intent));
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _intentDeliveryPending, 1, 0) != 0)
            throw new InvalidOperationException("An intent delivery is already pending.");
        try
        {
            var session = DemandInputWorker(expectedStartOrdinal);
            var deliveryId = Interlocked.Increment(ref _intentDeliverySequence);
            using var pending = session.PendingRequests.Register(MessageTypes.DeliverIntent);
            using (var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken))
            {
                writeDeadline.CancelAfter(_options.RequestTimeout);
                try
                {
                    // User cancellation cannot interrupt a partially written frame.
                    await session.WriteAsync(new RuntimeEnvelope { Type = MessageTypes.DeliverIntent,
                        RequestId = pending.RequestId, Payload = RuntimeJson.ToElement(new IntentDeliveryPayload(deliveryId, intent)) },
                        writeDeadline.Token).ConfigureAwait(false);
                }
                catch { session.Terminate(); throw; }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.CancellationToken);
            deadline.CancelAfter(_options.RequestTimeout);
            try
            {
                var response = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, Volatile.Read(ref _session)) || session.IsTerminal || Starts != expectedStartOrdinal)
                    throw new WidgetInputWorkerRetiredException();
                if (response.Type != MessageTypes.IntentResult)
                    throw new WidgetProtocolViolationException("Expected an intent delivery result.");
                return RuntimeJson.FromElement<IntentDeliveryResultPayload>(response.Payload).Accepted;
            }
            catch (OperationCanceledException)
            {
                await CancelConnectedIntentAsync(session, deliveryId, pending.Response).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && !session.IsTerminal)
                    throw new TimeoutException("Intent delivery exceeded its deadline.");
                throw;
            }
            catch (WidgetProcessException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelConnectedIntentAsync(session, deliveryId, pending.Response).ConfigureAwait(false);
                throw new OperationCanceledException(cancellationToken);
            }
        }
        finally { Volatile.Write(ref _intentDeliveryPending, 0); }
    }

    private static async Task CancelConnectedIntentAsync(WidgetProcessSession session, long deliveryId, Task<RuntimeEnvelope> original)
    {
        if (session.IsTerminal) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var pending = session.PendingRequests.Register(MessageTypes.CancelIntent);
        try
        {
            await session.WriteAsync(new RuntimeEnvelope { Type = MessageTypes.CancelIntent, RequestId = pending.RequestId,
                Payload = RuntimeJson.ToElement(new IntentCancellationPayload(deliveryId)) }, deadline.Token).ConfigureAwait(false);
            var cancelled = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (cancelled.Type != MessageTypes.Acknowledged) throw new WidgetProtocolViolationException("Invalid intent cancellation acknowledgement.");
            try { await original.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (WidgetProcessException) { } // Expected terminal cancellation result; retain response correlation until drained.
        }
        catch { session.Terminate(); } // An uncooperative receiver cannot outlive cancelled host authority.
    }
}
