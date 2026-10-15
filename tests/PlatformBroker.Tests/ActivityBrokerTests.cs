using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
internal static class ActivityBrokerTests
{
    internal static async Task Authority()
    {
        var consent = new ConsentStore(Path.Combine(Path.GetTempPath(), "activity-authority-" + Guid.NewGuid().ToString("N")));
        var identity = new BrokerWidgetIdentity("example.widget", "example.publisher", "instance");
        var calls = 0;
        if (PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.WidgetActivityV1)) throw new Exception("Activity must be intrinsic");
        await using var broker = new PlatformCapabilityBroker(identity, [], consent, new SimulatedPlatformBrokerBackend(),
            [PlatformCapabilities.WidgetActivityV1], AppLibrarySavedIdIssuer.Shared,
            activity: (_, _) => { calls++; return ValueTask.FromResult(new WidgetActivityReceipt(true)); });
        broker.SetLifecycle(BrokerLifecycleState.Background);
        var request = new BrokerRequestEnvelope(1, 1, identity, PlatformCapabilities.WidgetActivityV1, "activity.report", BrokerJson.ToElement(new WidgetActivity(LocalAudio: true)));
        await broker.ExecuteAsync(request); await broker.ExecuteAsync(request with { RequestId = 2 });
        if (calls != 1) throw new Exception("Unchanged status republished");
        await broker.ExecuteAsync(request with { RequestId = 3, Payload = BrokerJson.ToElement(new WidgetActivity()) });
        if (calls != 2) throw new Exception("Clearing status was lost");
        try { await broker.ExecuteAsync(request with { Widget = identity with { PackageId = "other.widget" } }); throw new Exception("Spoof accepted"); }
        catch (BrokerException) { }
        try { await broker.ExecuteAsync(request with { Payload = BrokerJson.ToElement(new { widgetId = "other", localAudio = true }) }); throw new Exception("Custom identity accepted"); }
        catch (BrokerException error) when (error.Code == "invalid_payload") { }
        broker.SetLifecycle(BrokerLifecycleState.Destroying);
        try { await broker.ExecuteAsync(request); throw new Exception("Retired worker reported activity"); }
        catch (BrokerException) { }
    }
}
