using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIntentScenarios
{
    internal static async Task NotificationIntents()
    {
        var source = Source();
        await using var fixture = Fixture([source]);
        var request = await PrepareSource(fixture);
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background);
        var intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = "https://example.com/notification" }));
        var command = new WidgetNotificationCommand("ready", new("ready", "Ready") { OpenIntent = intent });
        Check(fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, request.WorkerRun, command, out var token));
        Check((await fixture.Registry.PrepareNotificationIntentAsync(new("other", token!), default)).Kind == WidgetIntentLaunchKind.Rejected);
        Check((await fixture.Registry.PrepareNotificationIntentAsync(new("source", new string('a',32)), default)).Kind == WidgetIntentLaunchKind.Rejected);
        var prepared = await fixture.Registry.PrepareNotificationIntentAsync(new("source", token!), default);
        Check(prepared.Kind == WidgetIntentLaunchKind.ExternalBrowser);
        fixture.Registry.ReleaseNotificationIntent(new("source", token!));
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).Accepted);
        Check(fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, request.WorkerRun, command, out token));
        prepared = await fixture.Registry.PrepareNotificationIntentAsync(new("source", token!), default);
        Check((await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).ExternalUrl == "https://example.com/notification");
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).Accepted);
        var undeclared = command with { Notification = command.Notification! with { OpenIntent = WidgetIntentRequest.Create(WidgetIntentContracts.AppUpdates, JsonSerializer.SerializeToElement(new { })) } };
        Check(!fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, request.WorkerRun, undeclared, out _));
        var prior = token;
        Check(fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, request.WorkerRun, command, out token));
        Check((await fixture.Registry.PrepareNotificationIntentAsync(new("source", prior!), default)).Kind == WidgetIntentLaunchKind.Rejected);
        Check(fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, request.WorkerRun, new("ready"), out _));
        Check((await fixture.Registry.PrepareNotificationIntentAsync(new("source", token!), default)).Kind == WidgetIntentLaunchKind.Rejected);

        var settings = Target("settings") with { Intents = new() { Requests = [WidgetIntentContracts.AppUpdates], Handles = [new(WidgetIntentContracts.AppUpdates)] } };
        var other = Target("other") with { Intents = new() { Handles = [new(WidgetIntentContracts.AppUpdates)] } };
        await using var hostFixture = Fixture([settings, other]);
        var host = await hostFixture.Registry.PrepareAppUpdateIntentAsync(default);
        Check(host.Kind == WidgetIntentLaunchKind.Widget && host.Destinations.Single().WidgetId == "settings");
    }
}
