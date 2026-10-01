using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.WidgetBridge;

/// <summary>Session-local, bounded, one-shot authority for an already admitted host effect.</summary>
internal sealed class BridgeTaskActivationTickets
{
    internal sealed record Ticket(string WidgetId, string Fingerprint, string InstanceId,
        string RuntimeGeneration, long InitiatedAt, TaskWindowTarget Target, BridgeWorkerRun WorkerRun);
    private readonly object gate = new();
    private readonly Dictionary<long, Ticket> pending = [];
    internal int Count { get { lock (gate) return pending.Count; } }
    internal static bool Fresh(long initiated, long now) => initiated >= 0 && initiated <= now && now - initiated <= 2000;
    internal void Offer(long sequence, Ticket ticket, long now)
    {
        lock (gate)
        {
            foreach (var key in pending.Where(pair => !Fresh(pair.Value.InitiatedAt, now)).Select(pair => pair.Key).ToArray()) pending.Remove(key);
            if (!Fresh(ticket.InitiatedAt, now)) return;
            if (pending.Count >= 64) pending.Remove(pending.MinBy(pair => pair.Key).Key);
            pending.Add(sequence, ticket);
        }
    }
    internal Ticket? Take(BridgeTaskActivationRequest request, long now)
    {
        lock (gate)
        {
            if (!pending.Remove(request.Sequence, out var ticket)) return null;
            return ticket.WidgetId == request.WidgetId && ticket.RuntimeGeneration == request.RuntimeGeneration &&
                Fresh(ticket.InitiatedAt, now) ? ticket : null;
        }
    }
    internal void Clear() { lock (gate) pending.Clear(); }
}
