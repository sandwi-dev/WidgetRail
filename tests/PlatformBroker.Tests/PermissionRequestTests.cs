using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class PermissionRequestTests
{
    private static readonly BrokerWidgetIdentity identity = new("example.permissions", "example.publisher", "instance");
    private const string read = PlatformCapabilities.CaptureLibraryReadV1;
    private const string manage = PlatformCapabilities.CaptureLibraryManageV1;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task ConsentAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-permission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConsentStore(root); var registry = new PermissionRequestRegistry(); var refreshed = 0;
            using var domain = new PermissionCapabilityDomain(identity, new HashSet<string> { read, manage }, store, _ => { refreshed++; return Task.CompletedTask; }, registry);
            Task<JsonElement> Call(string operation, object request) => domain.ExecuteAsync(operation, BrokerJson.ToElement(request), default);
            domain.SetLifecycle(BrokerLifecycleState.Background);
            var statuses = BrokerJson.ParsePayload<WidgetPermissionStatuses>(await Call("permissions.check", new WidgetPermissionQuery([read, "arbitrary.permission.v1", PlatformCapabilities.PrivateStateV1])));
            Check(statuses.Permissions.Select(p => p.State).SequenceEqual([WidgetPermissionState.NotDecided, WidgetPermissionState.Unavailable, WidgetPermissionState.Unavailable]), "Status leaked undeclared or intrinsic authority");
            Check(registry.Take() is null, "Reading permission status opened a prompt");
            var background = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read])));
            Check(background.Outcome == WidgetPermissionOutcome.Unavailable && registry.Take() is null, "Background request prompted");
            domain.SetLifecycle(BrokerLifecycleState.Interactive);
            var undeclared = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read, "arbitrary.permission.v1"])));
            Check(undeclared.Outcome == WidgetPermissionOutcome.Unavailable && registry.Take() is null, "Partial undeclared request was admitted");
            var ticket = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read, manage])));
            var prompt = registry.Take()!;
            Check(prompt.Widget == identity && prompt.RequestId == ticket.RequestId && prompt.CapabilityIds.SequenceEqual([read, manage]), "Prompt lost bound identity/declarations");
            Check((await store.LoadAsync()).Entries.Count == 0, "Enqueue granted permission");
            var second = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read])));
            Check(second.Outcome == WidgetPermissionOutcome.Unavailable, "Duplicate request queued another dialog");
            using var foreign = new PermissionCapabilityDomain(identity, new HashSet<string> { read }, store, _ => Task.CompletedTask, registry);
            var other = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await foreign.ExecuteAsync("permissions.status", BrokerJson.ToElement(new WidgetPermissionTicket(ticket.RequestId!)), default));
            Check(other.Outcome == WidgetPermissionOutcome.Unavailable, "Same-identity foreign worker read another ticket");
            Check(await registry.CompleteAsync(ticket.RequestId!, WidgetPermissionOutcome.Granted, default), "Host grant did not commit");
            Check(refreshed == 1 && (await store.LoadAsync()).Entries.All(e => e.Decision == ConsentDecision.Grant), "Consent not durable before completion");
            var result = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.status", new WidgetPermissionTicket(ticket.RequestId!)));
            Check(result.Outcome == WidgetPermissionOutcome.Granted, "Caller cannot continue after grant");
            Check(!await registry.CompleteAsync(ticket.RequestId!, WidgetPermissionOutcome.Denied, default), "Replayed decision changed consent");
            var already = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read])));
            Check(already.Outcome == WidgetPermissionOutcome.Granted && already.RequestId is null && registry.Take() is null, "Already granted request prompted");
            await store.SetDecisionAsync(identity, manage, ConsentDecision.Deny);
            var deny = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await Call("permissions.request", new WidgetPermissionQuery([read, manage])));
            Check(registry.Take()!.CapabilityIds.SequenceEqual([manage]), "Dialog included already granted access");
            Check(await registry.CompleteAsync(deny.RequestId!, WidgetPermissionOutcome.Denied, default), "Explicit denial did not complete");
            Check(await store.GetDecisionAsync(identity, read) == ConsentDecision.Grant && await store.GetDecisionAsync(identity, manage) == ConsentDecision.Deny, "Denial changed unrelated consent");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    internal static async Task LifetimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-permission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var time = new Clock(); var registry = new PermissionRequestRegistry(time); var store = new ConsentStore(root);
            using var domain = new PermissionCapabilityDomain(identity, new HashSet<string> { read }, store, _ => Task.CompletedTask, registry);
            async Task<string> Begin() { domain.SetLifecycle(BrokerLifecycleState.Interactive); return BrokerJson.ParsePayload<WidgetPermissionRequestState>(await domain.ExecuteAsync("permissions.request", BrokerJson.ToElement(new WidgetPermissionQuery([read])), default)).RequestId!; }
            var id = await Begin(); domain.SetLifecycle(BrokerLifecycleState.Visible);
            Check(!registry.IsCurrent(id) && !await registry.CompleteAsync(id, WidgetPermissionOutcome.Granted, default), "Lost interaction request granted");
            Check((await store.LoadAsync()).Entries.Count == 0, "Cancellation persisted a decision");
            id = await Begin(); time.Now += TimeSpan.FromMinutes(3);
            Check(registry.Take() is null && !await registry.CompleteAsync(id, WidgetPermissionOutcome.Granted, default), "Expired prompt granted");
            id = await Begin(); domain.CancelPending();
            Check(!registry.IsCurrent(id), "Consent invalidation left a live prompt");
            var recovered = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await domain.ExecuteAsync("permissions.request", BrokerJson.ToElement(new WidgetPermissionQuery([read])), default));
            Check(recovered.Outcome == WidgetPermissionOutcome.Pending, "Cancelling consent work incorrectly changed the broker lifecycle");
            domain.CancelPending();
            id = await Begin(); domain.Dispose();
            Check(registry.Take() is null && !await registry.CompleteAsync(id, WidgetPermissionOutcome.Granted, default), "Retired worker prompt granted");
            var owner = new object(); var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var committed = false;
            var slow = registry.Begin(owner, identity, [read], async (_, token) => { waiting.SetResult(); await Task.Delay(Timeout.Infinite, token); committed = true; });
            var completion = registry.CompleteAsync(slow.RequestId!, WidgetPermissionOutcome.Granted, default);
            await waiting.Task; registry.CancelOwner(owner, remove: true);
            Check(!await completion && !committed, "Retirement did not cancel a pending consent write");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    internal static async Task BrokerAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-permission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConsentStore(root); var backend = new SimulatedPlatformBrokerBackend();
            await using var broker = new PlatformCapabilityBroker(identity, [read], store, backend, [PlatformCapabilities.WidgetPermissionsV1]);
            broker.SetLifecycle(BrokerLifecycleState.Background); long sequence = 0;
            Task<JsonElement> Call(string op) => broker.ExecuteAsync(new(1, ++sequence, identity, PlatformCapabilities.WidgetPermissionsV1, op, BrokerJson.ToElement(new WidgetPermissionQuery([read]))));
            await Call("permissions.check");
            try { await Call("permissions.request"); throw new Exception("Background request bypassed lifecycle"); }
            catch (BrokerException error) { Check(error.Code == "lifecycle_denied", "Unexpected background denial"); }
            Check(!PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.WidgetPermissionsV1), "Widget can self-declare intrinsic service");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
