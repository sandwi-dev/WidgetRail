using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class NotificationBrokerTests
{
    internal static async Task Authority()
    {
        var consent = new ConsentStore(Path.Combine(Path.GetTempPath(), "notification-authority-" + Guid.NewGuid().ToString("N")));
        var identity = new BrokerWidgetIdentity("example.widget", "example.publisher", "instance");
        var calls = 0;
        if (PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.WidgetNotificationsV1)) throw new Exception("Not an intrinsic service");
        await using var broker = new PlatformCapabilityBroker(identity, [], consent, new SimulatedPlatformBrokerBackend(),
            [PlatformCapabilities.WidgetNotificationsV1], AppLibrarySavedIdIssuer.Shared,
            notifications: (_, _) => { calls++; return ValueTask.FromResult(new WidgetNotificationReceipt(true)); });
        broker.SetLifecycle(BrokerLifecycleState.Background);
        var command = new WidgetNotificationCommand("song", new("song", "Synthetic track"));
        var request = new BrokerRequestEnvelope(1, 1, identity, PlatformCapabilities.WidgetNotificationsV1, "notifications.publish", BrokerJson.ToElement(command));
        await broker.ExecuteAsync(request);
        await broker.ExecuteAsync(request with { RequestId = 2 }); // Rate-limited without queuing another publication.
        if (calls != 1) throw new Exception("Repeated publication was not bounded");
        await broker.ExecuteAsync(request with { RequestId = 3, Payload = BrokerJson.ToElement(new WidgetNotificationCommand("song")) });
        if (calls != 2) throw new Exception("Dismissal should remain available during the quiet admission interval");
        try { await broker.ExecuteAsync(request with { Widget = identity with { PackageId = "another.widget" } }); throw new Exception("Identity spoof accepted"); }
        catch (BrokerException) { }
        try { await broker.ExecuteAsync(request with { Payload = BrokerJson.ToElement(command with { Notification = command.Notification! with { DurationMilliseconds = 999999 } }) }); throw new Exception("Invalid customization accepted"); }
        catch (BrokerException error) when (error.Code == "invalid_payload") { }
        broker.SetLifecycle(BrokerLifecycleState.Destroying);
        try { await broker.ExecuteAsync(request); throw new Exception("Retired worker published"); }
        catch (BrokerException) { }
    }
}
