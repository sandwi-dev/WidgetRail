using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class PermissionCapabilityDomain(BrokerWidgetIdentity identity, IReadOnlySet<string> declarations,
    ConsentStore consent, Func<CancellationToken, Task> refresh, PermissionRequestRegistry? registry = null) : IDisposable
{
    private readonly PermissionRequestRegistry requests = registry ?? PermissionRequestRegistry.Shared;
    private readonly object gate = new();
    private bool interactive, disposed;
    internal void CancelPending() { lock (gate) requests.CancelOwner(this); }
    internal void SetLifecycle(BrokerLifecycleState state)
    {
        lock (gate)
        {
            interactive = !disposed && state == BrokerLifecycleState.Interactive;
            if (!interactive) requests.CancelOwner(this);
        }
    }
    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation is "permissions.status" or "permissions.cancel")
        {
            var ticket = BrokerJson.ParsePayload<WidgetPermissionTicket>(payload);
            if (!CaptureLimits.ValidToken(ticket.RequestId)) throw new BrokerException("invalid_payload", "Invalid permission ticket.");
            if (operation == "permissions.cancel") { requests.Cancel(this, ticket.RequestId); return BrokerCapabilityDomains.Acknowledged(); }
            return BrokerJson.ToElement(requests.Status(this, ticket.RequestId));
        }
        var query = BrokerJson.ParsePayload<WidgetPermissionQuery>(payload);
        if (!query.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid permission request.");
        var current = await consent.LoadAsync(token).ConfigureAwait(false);
        var states = query.CapabilityIds.Select(id => new WidgetPermissionStatus(id,
            !declarations.Contains(id) || !PlatformCapabilities.IsManifestDeclarable(id) ? WidgetPermissionState.Unavailable :
            current.Entries.FirstOrDefault(e => e.PackageId == identity.PackageId && e.PublisherId == identity.PublisherId && e.CapabilityId == id)?.Decision switch
            { ConsentDecision.Grant => WidgetPermissionState.Granted, ConsentDecision.Deny => WidgetPermissionState.Denied, _ => WidgetPermissionState.NotDecided })).ToArray();
        if (operation == "permissions.check") return BrokerJson.ToElement(new WidgetPermissionStatuses(states));
        if (operation != "permissions.request") throw new BrokerException("unsupported_operation", "Unknown permission operation.");
        if (states.All(s => s.State == WidgetPermissionState.Granted)) await refresh(token).ConfigureAwait(false);
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (!interactive || disposed || states.Any(s => s.State == WidgetPermissionState.Unavailable)) return BrokerJson.ToElement(new WidgetPermissionRequestState(WidgetPermissionOutcome.Unavailable));
            if (states.All(s => s.State == WidgetPermissionState.Granted)) return BrokerJson.ToElement(new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted));
            var missing = states.Where(s => s.State != WidgetPermissionState.Granted).Select(s => s.CapabilityId).ToArray();
            return BrokerJson.ToElement(requests.Begin(this, identity, missing, async (outcome, ct) =>
            {
                await consent.SetDecisionsAsync(identity, missing, outcome == WidgetPermissionOutcome.Granted ? ConsentDecision.Grant : ConsentDecision.Deny, ct).ConfigureAwait(false);
                await refresh(ct).ConfigureAwait(false);
                if (outcome == WidgetPermissionOutcome.Granted)
                {
                    var granted = await consent.LoadAsync(ct).ConfigureAwait(false);
                    if (query.CapabilityIds.Any(id => !granted.Entries.Any(e => e.PackageId == identity.PackageId && e.PublisherId == identity.PublisherId && e.CapabilityId == id && e.Decision == ConsentDecision.Grant)))
                        throw new BrokerException("permission_changed", "Requested permission changed during consent.");
                }
            }));
        }
    }
    public void Dispose() { lock (gate) { disposed = true; interactive = false; requests.CancelOwner(this, remove: true); } }
}
