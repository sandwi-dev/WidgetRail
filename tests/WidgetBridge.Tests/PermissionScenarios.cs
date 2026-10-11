using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

internal static class PermissionScenarios
{
    internal static async Task HostPipe()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-permission-pipe-" + Guid.NewGuid().ToString("N"));
        var pipe = "permission-pipe-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var identity = new BrokerWidgetIdentity("example.permission", "example.publisher", "permission.instance");
        const string permission = PlatformCapabilities.AudioOutputReadV1;
        var store = new ConsentStore(root);
        try
        {
            var widget = new ConfiguredWidget { Id = "permission", PackageId = identity.PackageId, PublisherId = identity.PublisherId,
                InstanceId = identity.InstanceId, Name = "Permission fixture", WorkerExecutable = Environment.ProcessPath!, DeclaredCapabilities = [permission],
                WorkerFingerprint = new string('a',64), CatalogFingerprint = new string('b',64) };
            var backend = new SimulatedPlatformBrokerBackend();
            await using var broker = new PlatformCapabilityBroker(identity, [permission], store, backend, [PlatformCapabilities.WidgetPermissionsV1]);
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            await broker.RefreshConsentAsync(deadline.Token);
            await using var server = new WidgetBridgeServer(pipe, new([widget]), consentStore: store);
            var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
            await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
            var query = new WidgetPermissionQuery([permission]);
            var pending = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await broker.ExecuteAsync(new(1,1,identity,PlatformCapabilities.WidgetPermissionsV1,"permissions.request",BrokerJson.ToElement(query)), deadline.Token));
            var prompt = (await session.TakePermissionAsync(deadline.Token)).Prompt;
            if (prompt is null || prompt.WidgetId != widget.Id || prompt.WidgetName != widget.Name || prompt.RequestId != pending.RequestId ||
                prompt.PermissionNames.Single() != CapabilityPresentation.CapabilityName(permission)) throw new Exception("Trusted identity/labels were not delivered through the host pipe");
            if (!await session.IsPermissionCurrentAsync(prompt.RequestId, deadline.Token)) throw new Exception("Prompt prematurely retired");
            if (!await session.CompletePermissionAsync(prompt.RequestId, WidgetPermissionOutcome.Granted, deadline.Token)) throw new Exception("Host completion failed");
            if (await store.GetDecisionAsync(identity, permission, deadline.Token) != ConsentDecision.Grant) throw new Exception("Host completion preceded persisted consent");
            // Approval reconciles the same live broker. No worker reload is required.
            await broker.ExecuteAsync(new(1,2,identity,permission,PlatformCapabilities.AudioOutputGet,BrokerJson.ToElement(new { })), deadline.Token);
            if (await session.CompletePermissionAsync(prompt.RequestId, WidgetPermissionOutcome.Denied, deadline.Token)) throw new Exception("Replayed completion was accepted");
            await store.SetDecisionAsync(identity, permission, ConsentDecision.Deny, deadline.Token);
            var next = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await broker.ExecuteAsync(new(1,3,identity,PlatformCapabilities.WidgetPermissionsV1,"permissions.request",BrokerJson.ToElement(query)), deadline.Token));
            broker.SetLifecycle(BrokerLifecycleState.Background);
            if (await session.IsPermissionCurrentAsync(next.RequestId!, deadline.Token) || (await session.TakePermissionAsync(deadline.Token)).Prompt is not null)
                throw new Exception("Background prompt remained exposed to the frontend");
            await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
