using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private readonly object _hostTaskActivationGate = new();
    private long _lastHostTaskActivation;

    private BridgeTaskActivationResponse ActivateHostTaskWindow(BridgeHostTaskActivationRequest request, CancellationToken token)
    {
        request.Validate();
        token.ThrowIfCancellationRequested();
        lock (_hostTaskActivationGate)
        {
            if (request.Sequence <= _lastHostTaskActivation) return Result("Rejected", "host-activation-replayed");
            _lastHostTaskActivation = request.Sequence;
            // Keep this short native commit ordered as well as its sequence check.
            // An older request must never activate after a newer request.
            if (!BridgeTaskActivationTickets.Fresh(request.InitiatedAt, Environment.TickCount64)) return Result("Rejected", "host-activation-expired");
            var target = new TaskWindowTarget(request.Handle, request.ProcessId, request.ProcessCreated, request.ClassName);
            var trace = new List<string>();
            var platform = new WindowsTaskWindowActivation(message => { if (trace.Count < 4) trace.Add(message); });
            if (!platform.IsCurrent(target)) return Result("Rejected", "window-identity-changed");
            token.ThrowIfCancellationRequested();
            if (!BridgeTaskActivationTickets.Fresh(request.InitiatedAt, Environment.TickCount64)) return Result("Rejected", "host-activation-expired");
            var accepted = platform.RequestActivation(target);
            return Result(accepted ? "Requested" : "Denied", accepted ? "activation-requested" : "windows-denied", string.Join(" | ", trace));
        }
        static BridgeTaskActivationResponse Result(string result, string code, string trace = "") =>
            new(result, code, Environment.ProcessId, trace.Length <= 4096 ? trace : trace[..4096]);
    }

    private BridgeTaskActivationResponse CompleteTaskActivation(BridgeTaskActivationRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ticket = _taskActivations.Take(request, Environment.TickCount64);
        if (ticket is null) return Result("Rejected", "missing-expired-or-consumed-effect");
        using var publication = _registry.TryAdmitTaskActivation(ticket);
        if (publication is null || publication.Value.InstanceId != ticket.InstanceId ||
            publication.Value.RuntimeGeneration != ticket.RuntimeGeneration)
            return Result("Rejected", "worker-authority-changed");
        var trace = new List<string>();
        var platform = new WindowsTaskWindowActivation(message => { if (trace.Count < 4) trace.Add(message); });
        if (!platform.IsCurrent(ticket.Target)) return Result("Rejected", "window-identity-changed");
        token.ThrowIfCancellationRequested();
        if (!BridgeTaskActivationTickets.Fresh(ticket.InitiatedAt, Environment.TickCount64))
            return Result("Rejected", "effect-expired-before-activation");
        // Execute in the broker that received permission before widget input.
        var accepted = platform.RequestActivation(ticket.Target);
        return Result(accepted ? "Requested" : "Denied", accepted ? "activation-requested" : "windows-denied", string.Join(" | ", trace));
        static BridgeTaskActivationResponse Result(string result, string code, string trace = "") =>
            new(result, code, Environment.ProcessId, trace.Length <= 4096 ? trace : trace[..4096]);
    }
}
